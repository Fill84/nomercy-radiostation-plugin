// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Diagnostics.CodeAnalysis;
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
        // Both are nullable on the wire DTO precisely so a row missing one does not
        // throw during parsing (see RadioBrowserStation's header comment) - which
        // means admission, not deserialization, is what has to reject it.
        if (string.IsNullOrWhiteSpace(station.StationUuid) || string.IsNullOrWhiteSpace(station.Name))
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
    ///
    /// Keying on the slugified name as well as the URL means this can also drop a
    /// station that is genuinely not a duplicate — radio-browser carries plenty of
    /// unrelated broadcasters that happen to share a generic name, separate "Jazz FM"
    /// or "Rock Radio" licensees in different countries with nothing else in common.
    /// That tradeoff is accepted anyway: two identical-looking rows in the grid read
    /// as a bug to the user, while one station missing out of a sweep of hundreds
    /// does not read as anything at all. Seeds are added before the genre sweep runs
    /// and first occurrence wins, so the stations that were curated on purpose always
    /// survive a name collision rather than being the ones dropped. There is no
    /// logging for this, so a station suspected to be missing for this reason is
    /// found by checking whether its name collides with one already kept, not by
    /// looking for a log line.
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
    /// Guards a URL handed to the client's webview (currently: a station's homepage)
    /// before it leaves the plugin. Homepage is untrusted from both of its sources -
    /// radio-browser.info is a community-editable database, and StationOverrides is
    /// deliberately ungated - and unlike StreamUrl (forced to HTTPS by Admits above)
    /// nothing restricts its scheme. Whether the client sandboxes a javascript:,
    /// file: or data: value is not knowable from this repo, so the plugin must not
    /// emit one. True only for an absolute http or https URL.
    /// </summary>
    public static bool IsSafeExternalUrl([NotNullWhen(true)] string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

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
