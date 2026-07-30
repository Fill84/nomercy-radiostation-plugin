// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text;

namespace NoMercy.Plugin.InternetRadio;

// What a station has to be before it is allowed into the catalogue.
//
// These are admission rules for DISCOVERED stations, applied to what radio-browser
// declares. They are not proof a stream works - see RadioBrowserStation.LastCheckOk
// for why that distinction is real. A user's own stations.json is deliberately not
// gated: a hand-written list is their call, and silently dropping their entries
// would be worse than letting one fail visibly in the player.
public static class StationGates
{
    /// <summary>
    /// url_resolved is what radio-browser followed redirects to, and is the better
    /// answer when it has one.
    /// </summary>
    public static string EffectiveUrl(RadioBrowserStation station) =>
        !string.IsNullOrWhiteSpace(station.UrlResolved) ? station.UrlResolved : station.Url ?? string.Empty;

    public static bool Admits(RadioBrowserStation station)
    {
        if (string.IsNullOrWhiteSpace(station.Name))
        {
            return false;
        }

        // HTTPS is mandatory, not preferred: the dashboard is served over HTTPS, so
        // the browser blocks an http stream as mixed content before it reaches the
        // player. A station that cannot play is worse than one that is absent,
        // because the absent one does not look like the plugin is broken.
        string url = EffectiveUrl(station);
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        // Silence on every client but Safari.
        if (station.Hls != 0)
        {
            return false;
        }

        return station.LastCheckOk == 1;
    }

    /// <summary>
    /// First occurrence wins, so a seed keeps its place when the genre sweep finds
    /// the same station again — which it routinely does, since a curated station is
    /// usually also a popular one.
    /// </summary>
    public static IReadOnlyList<RadioStation> Deduplicate(IEnumerable<RadioStation> stations)
    {
        HashSet<string> seenUrls = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seenNames = new(StringComparer.Ordinal);
        List<RadioStation> kept = [];

        foreach (RadioStation station in stations)
        {
            string url = station.StreamUrl.Trim().TrimEnd('/');
            string name = Slugify(station.Name);

            // Both keys, because the same station appears under different mirror
            // hosts (same name, different URL) and under different names for the
            // same stream (same URL, different name).
            if (!seenUrls.Add(url) || !seenNames.Add(name))
            {
                continue;
            }

            kept.Add(station);
        }

        return kept;
    }

    /// <summary>
    /// A lowercase, hyphen-separated, ASCII-safe form of a name. Used both as the
    /// dedupe key and as the route id for a user-supplied station, so it has to be
    /// stable for the same name and safe in a URL path segment.
    /// </summary>
    public static string Slugify(string name)
    {
        StringBuilder builder = new(name.Length);
        bool pendingSeparator = false;

        foreach (char character in name)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        // A name with nothing slug-safe in it still needs a routable id, and an
        // empty one would collide with every other such station.
        return builder.Length > 0 ? builder.ToString() : "station";
    }
}
