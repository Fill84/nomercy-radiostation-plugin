// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// One station as a tile, shared by every grid so two screens cannot drift into behaving
// differently for the same station.
//
// There is no layout code here any more, and that is the point. The client's own grid is
// `grid-cols-[repeat(auto-fill,minmax(10rem,1fr))]`, so it sizes every tile alike and
// reflows on its own. Every previous attempt to get even tiles - widths, tokens, fractions
// - was working around a grid that was never being reached, because the name this plugin
// sent for it resolved to a design-system card instead. See Ui.
public static class StationCards
{
    /// <summary>How many stations the browse page's "Popular" grid shows.</summary>
    public const int PopularCount = 18;

    /// <summary>Shown on the toggle when the station is not a favourite yet.</summary>
    public const string AddFavouriteLabel = "Add to favourites";

    /// <summary>Shown on the toggle when it is.</summary>
    public const string RemoveFavouriteLabel = "Remove from favourites";

    /// <summary>A grid of stations, laid out by the client.</summary>
    public static PluginComponent Grid(
        string id,
        IEnumerable<RadioStation> stations,
        UserState state,
        string scope)
    {
        // A set built once, not a scan per tile. A grid is eighteen tiles and a favourites
        // list is unbounded, so the naive form is quadratic in the two things here most
        // likely to grow.
        HashSet<string> favourites = [.. state.Favourites.Select(favourite => favourite.Id)];

        return Ui.Grid(
            id,
            stations.Select(station => Tile(station, favourites.Contains(station.Id), scope)));
    }

    /// <summary>
    /// One station: a card that plays it, and a control to keep it.
    ///
    /// The card carries the play action, so a click anywhere on the cover or the name
    /// starts the station — one click is listening, which is the entire job. The favourite
    /// toggle is a SIBLING of that card rather than a child: nested inside, the click would
    /// land on the thing carrying play and keeping a station would also play it.
    /// </summary>
    public static PluginComponent Tile(RadioStation station, bool isFavourite, string scope = "")
    {
        string id = Scoped(scope, station.Id);

        return Ui.Container(
            $"station-tile-{id}",
            Ui.Card(
                $"station-card-{id}",
                station.Name,
                Subtitle(station),
                // Null rather than the station's own url when the browser would refuse it:
                // the card draws no image at all instead of a broken one.
                CoverUrl(station) is null ? null : MediaProxy.Cover(station.Id),
                Play(station)),
            Ui.Button(
                $"station-favourite-{id}",
                isFavourite ? RemoveFavouriteLabel : AddFavouriteLabel,
                ToggleFavourite(station),
                variant: isFavourite ? "primary" : "secondary"));
    }

    /// <summary>
    /// The key a media intent carries the station's own id under.
    ///
    /// Not part of PlayMedia's signature — the factory takes a url, a title, an artist and
    /// a cover, and nothing else — but the payload is an open dictionary, so this rides
    /// along beside them.
    ///
    /// It is here because the client has to identify a track somehow and, with no id in
    /// the payload, it builds one out of the stream url: `plugin:{pluginId}:{streamUrl}`.
    /// That identifier then goes into a CSS selector and into a route, and a url is legal
    /// in neither — which is what throws before any audio starts, and why no /stream/
    /// request ever reaches this server. See
    /// docs/upstream/2026-08-08-plugin-media-cannot-play.md.
    /// </summary>
    public const string StationIdKey = "id";

    /// <summary>
    /// The play intent for a station, so every screen starts the same station the same way.
    /// </summary>
    public static PluginActionIntent Play(RadioStation station) =>
        WithStationId(
            PluginActionIntent.PlayMedia(
                // Through this plugin's own endpoint when we know where this server lives.
                // The station's own url is refused by the dashboard's media-src, so the
                // direct url is a fallback that plays nothing - kept only so a view still
                // renders.
                MediaProxy.Stream(station.Id) ?? station.StreamUrl,
                station.Name,
                // No artist. The player does not merely print this - it builds an artist
                // LINK from it and resolves a route for it, and a live stream has no
                // artist, so a genre there made the app route to something that is not one.
                null,
                CoverUrl(station) is null ? null : MediaProxy.Cover(station.Id)),
            station);

    /// <summary>Queueing a station, built from the same relayed urls as <see cref="Play"/>.</summary>
    public static PluginActionIntent Enqueue(RadioStation station) =>
        WithStationId(
            PluginActionIntent.Enqueue(
                MediaProxy.Stream(station.Id) ?? station.StreamUrl,
                station.Name,
                null,
                CoverUrl(station) is null ? null : MediaProxy.Cover(station.Id)),
            station);

    /// <summary>Adding or removing this station.</summary>
    public static PluginActionIntent ToggleFavourite(RadioStation station) =>
        PluginActionIntent.CallPlugin(
            $"{InternetRadioController.ToggleFavouriteMethod}/{Uri.EscapeDataString(station.Id)}");

    // Written into the payload after the factory built it rather than by hand-rolling the
    // intent here: the factory owns which keys a media intent carries and what they are
    // called, and a copy of that here would drift the first time it gains one.
    private static PluginActionIntent WithStationId(
        PluginActionIntent intent, RadioStation station)
    {
        intent.Payload[StationIdKey] = station.Id;

        return intent;
    }

    /// <summary>
    /// A node id, qualified by the section it is drawn in.
    ///
    /// One station legitimately appears twice on the browse page — kept in the favourites
    /// row and popular in the grid below it — and unqualified ids made those two the same
    /// node id in one payload. A client keying on id then has two elements claiming to be
    /// the same thing, which is a real bug in the browser and an invisible one here.
    /// </summary>
    private static string Scoped(string scope, string stationId) =>
        string.IsNullOrEmpty(scope) ? stationId : $"{scope}-{stationId}";

    /// <summary>
    /// The station's logo, or null when the browser could not draw it anyway.
    ///
    /// The same judgement StationGates makes about a stream, for the same reason: the
    /// dashboard is served over https, so an http image is blocked as mixed content and
    /// renders as a broken icon — which reads as this plugin being broken rather than as a
    /// station's logo having rotted. Six of them had already rotted to 404, 403 or an HTML
    /// page, so this is the ordinary case and not the exotic one.
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

    /// <summary>Genre and country, whichever of them is known. Null when neither is.</summary>
    public static string? Subtitle(RadioStation station)
    {
        string[] parts =
            [.. new[] { station.Genre, station.Country }.Where(part => !string.IsNullOrWhiteSpace(part))!];

        return parts.Length > 0 ? string.Join(" · ", parts) : null;
    }
}
