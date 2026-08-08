// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// One station as a card, shared by the browse and genre grids so the two screens
// cannot drift into behaving differently for the same station.
public static class StationCards
{
    /// <summary>How many stations the browse page's "Popular" grid shows.</summary>
    public const int PopularCount = 18;

    /// <summary>
    /// A card whose action is playMedia. Not navigate: the client turns this straight
    /// into playTrack(), so one click is listening — which is the entire job, and the
    /// one path that works while both inbound plugin transports are broken.
    /// </summary>
    public static PluginComponent Play(RadioStation station, string scope = "") =>
        PluginViews.Card(
            $"station-card-{Scoped(scope, station.Id)}",
            station.Name,
            Subtitle(station),
            CoverUrl(station),
            PluginActionIntent.PlayMedia(
                station.StreamUrl,
                station.Name,
                // The player shows this where a track's artist would go; the genre is
                // the most useful thing a live stream has to put there.
                station.Genre,
                CoverUrl(station)
            )
        );

    /// <summary>Genre and country, whichever of them is known. Null when neither is.</summary>
    public static string? Subtitle(RadioStation station)
    {
        string[] parts =
            [.. new[] { station.Genre, station.Country }.Where(part => !string.IsNullOrWhiteSpace(part))!];

        return parts.Length > 0 ? string.Join(" · ", parts) : null;
    }

    /// <summary>
    /// A node id, qualified by the section it is drawn in.
    ///
    /// One station legitimately appears twice on the browse page - kept in the favourites
    /// row and popular in the grid below it - and unqualified ids made those two the same
    /// node id in one payload. A client keying on id then has two elements claiming to be
    /// the same thing, which is a real bug in the browser and an invisible one here.
    /// </summary>
    private static string Scoped(string scope, string stationId) =>
        string.IsNullOrEmpty(scope) ? stationId : $"{scope}-{stationId}";

    /// <summary>Shown on the toggle when the station is not a favourite yet.</summary>
    public const string AddFavouriteLabel = "Add to favourites";

    /// <summary>Shown on the toggle when it is.</summary>
    public const string RemoveFavouriteLabel = "Remove from favourites";

    /// <summary>
    /// The station's logo, or null when the browser could not draw it anyway.
    ///
    /// The same judgement StationGates makes about a stream, for the same reason: the
    /// dashboard is served over https, so an http image is blocked as mixed content and
    /// renders as a broken icon - which reads as this plugin being broken rather than as
    /// a station's logo having rotted. Six of them had already rotted to 404, 403 or an
    /// HTML page, so this is the ordinary case and not the exotic one.
    ///
    /// Null rather than a placeholder URL: what an absent cover should look like is the
    /// design system's decision, not this plugin's, and NMImage already has one.
    /// </summary>
    public static string? CoverUrl(RadioStation station)
    {
        if (string.IsNullOrWhiteSpace(station.LogoUrl))
        {
            return null;
        }

        return Uri.TryCreate(station.LogoUrl, UriKind.Absolute, out Uri? parsed)
            && parsed.Scheme == Uri.UriSchemeHttps
            ? station.LogoUrl
            : null;
    }

    /// <summary>
    /// The play card with a favourite toggle beside it.
    ///
    /// Beside, not inside: PluginViews.Card takes exactly one action and the card's is
    /// already playMedia. One click has to stay "listen to this", which is the whole job
    /// of the card - so keeping is a second control rather than a mode on the first.
    ///
    /// The two states differ by label, and by variant on top of it - never by colour
    /// alone. A toggle whose only difference is a tint is unreadable to a good share of
    /// viewers, and to everyone in a screenshot.
    ///
    /// No icon, deliberately. The Moooom set verified for this plugin has no heart, and
    /// pluginIcon() substitutes `plugged` for a name the app does not have rather than
    /// failing - so guessing one would put a plug on every station card, silently, and
    /// no test here could see it.
    /// </summary>
    public static PluginComponent WithFavourite(
        RadioStation station, bool isFavourite, string scope = "") =>
        PluginViews.Row(
            $"station-row-{Scoped(scope, station.Id)}",
            Play(station, scope),
            PluginViews.Button(
                $"station-favourite-{Scoped(scope, station.Id)}",
                isFavourite ? RemoveFavouriteLabel : AddFavouriteLabel,
                PluginActionIntent.CallPlugin(
                    $"{InternetRadioController.ToggleFavouriteMethod}/{Uri.EscapeDataString(station.Id)}"),
                variant: isFavourite ? "primary" : "secondary"
            )
        );
}
