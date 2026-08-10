// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// One station as a tile, drawn with the components the app draws its own libraries with.
//
// There is no layout in this file any more, and that is the point. Every earlier attempt -
// widths, tokens, fractions, a wrapping row - was this plugin trying to reproduce a grid by
// hand, and the result never quite matched Films or Music because it never could. NMGrid is
// that grid: two columns on a phone, five to nine as the window widens, six on a
// television. NMMusicCard is the tile Music already uses.
//
// A card navigates rather than plays, because that is what a card in this app does: it is a
// RouterLink to its own page, and what you can do with a station lives there. Play, queue
// and favourite are all on the station page.
public static class StationCards
{
    /// <summary>How many stations the browse page's "Popular" grid shows.</summary>
    public const int PopularCount = 18;

    /// <summary>
    /// How much of a row one tile takes.
    ///
    /// A fraction, not a length: see the note above on NMSize. Six to a row leaves tiles
    /// large enough to read the name under, and the gap between them pushes the sixth onto
    /// the next line on a narrow window, which wraps evenly rather than cutting one off.
    /// </summary>
    public const string TileWidth = "1/6";

    /// <summary>Shown on the toggle when the station is not a favourite yet.</summary>
    public const string AddFavouriteLabel = "Add to favourites";

    /// <summary>Shown on the toggle when it is.</summary>
    public const string RemoveFavouriteLabel = "Remove from favourites";

    /// <summary>Stations in the app's own grid.</summary>
    public static PluginComponent Grid(
        string id,
        IEnumerable<RadioStation> stations,
        UserState state,
        string scope) =>
        Ui.MediaGrid(id, stations.Select(station => Tile(station, scope)));

    /// <summary>
    /// One station, as the app draws an artist: a square cover, the name under it, and a
    /// link to its own page.
    /// </summary>
    public static PluginComponent Tile(RadioStation station, string scope = "") =>
        Ui.MediaCard(
            $"station-tile-{Scoped(scope, station.Id)}",
            station.Name,
            AppRoutes.Station(station.Id),
            // Null rather than the station's own url when the browser would refuse it: the
            // card then draws its own placeholder instead of a broken image.
            CoverUrl(station) is null ? null : MediaProxy.Cover(station.Id),
            // Same treatment as an artist - a square, framed logo - rather than the album
            // branch, which draws a record sleeve this is not.
            type: "artist");

    /// <summary>
    /// The key a media intent carries the station's own id under.
    ///
    /// Not part of PlayMedia's signature - the factory takes a url, a title, an artist and
    /// a cover, and nothing else - but the payload is an open dictionary, so this rides
    /// along beside them.
    ///
    /// It is here because the client has to identify a track somehow and, with no id in
    /// the payload, it builds one out of the stream url: `plugin:{pluginId}:{streamUrl}`.
    /// That identifier then goes into a CSS selector and into a route, and a url is legal
    /// in neither - which is what throws before any audio starts, and why no /stream/
    /// request ever reaches this server. See
    /// docs/upstream/2026-08-08-plugin-media-cannot-play.md.
    ///
    /// Sending it costs nothing and is ignored until the client reads it. A station's
    /// radio-browser uuid is also the honest key: it is what the relay routes on, and it
    /// does not change when a station moves its stream, which a url-derived id does - so
    /// history and resume state stop hanging off something that was never a key.
    /// </summary>
    public const string StationIdKey = "id";

    /// <summary>
    /// The play intent for a station, so every screen starts the same station the same way.
    /// </summary>
    public static PluginActionIntent Play(RadioStation station) =>
        WithStationId(PlayIntent(station), station);

    private static PluginActionIntent PlayIntent(RadioStation station) =>
        PluginActionIntent.PlayMedia(
            // Through this plugin's own endpoint when we know where this server lives.
            // The station's own url is refused by the dashboard's media-src, so the direct
            // url is a fallback that plays nothing - kept only so a view still renders.
            MediaProxy.Stream(station.Id) ?? station.StreamUrl,
            station.Name,
            // No artist. The player does not merely print this - it builds an artist LINK
            // from it, resolves a route for it, and derives a DOM id from the track id to
            // anchor it. A live stream has no artist, and putting the genre there made the
            // app try to route to a genre that does not exist ("Cannot read properties of
            // undefined (reading 'path')") and then build the selector
            // "#trackLink-artists-plugin:<id>:https://…/stream/…" - invalid, because a url
            // has colons and slashes in it. Those two were every "Something went wrong"
            // toast on the page.
            null,
            CoverUrl(station) is null ? null : MediaProxy.Cover(station.Id));

    /// <summary>Queueing a station, built from the same relayed urls as <see cref="Play"/>.</summary>
    public static PluginActionIntent Enqueue(RadioStation station) =>
        WithStationId(
            PluginActionIntent.Enqueue(
                MediaProxy.Stream(station.Id) ?? station.StreamUrl,
                station.Name,
                null,
                CoverUrl(station) is null ? null : MediaProxy.Cover(station.Id)),
            station);

    /// <summary>The key a media intent carries its now-playing declaration under.</summary>
    public const string NowPlayingKey = "nowPlaying";

    /// <summary>
    /// How long between asking a station what is on air, once it has told us once.
    ///
    /// A poll is a short second connection to the station, so this is a courtesy to them
    /// as much as a cost to us. Half a minute is finer-grained than most stations announce
    /// anyway.
    /// </summary>
    public const int NowPlayingIntervalSeconds = 30;

    /// <summary>
    /// How long between asking before the first answer arrives.
    ///
    /// A listener who has just tuned in is looking at the station's own name where a track
    /// title belongs, and waiting half a minute to replace it is the difference between a
    /// player that knows what it is playing and one that does not. Stations announce
    /// shortly after a listener joins, so asking again soon usually settles it.
    /// </summary>
    public const int NowPlayingFirstIntervalSeconds = 5;

    // Written into the payload after the factory built it rather than by hand-rolling the
    // intent here: the factory owns which keys a media intent carries and what they are
    // called, and a copy of that here would drift the first time it gains one.
    private static PluginActionIntent WithStationId(
        PluginActionIntent intent, RadioStation station)
    {
        intent.Payload[StationIdKey] = station.Id;

        // A statement about this item, not a request for a feature. The client asks
        // nothing of an item that says nothing here, which is the right default: a track
        // with a fixed title must not be polled, and only the plugin knows which of its
        // media changes under its own name.
        intent.Payload[NowPlayingKey] = new Dictionary<string, object>
        {
            ["method"] = InternetRadioController.NowPlayingMethod,
            ["intervalSeconds"] = NowPlayingIntervalSeconds,
            ["firstIntervalSeconds"] = NowPlayingFirstIntervalSeconds,
        };

        return intent;
    }

    /// <summary>
    /// Adding or removing this station, as the toggle every tile and the station page draw.
    /// </summary>
    public static PluginActionIntent ToggleFavourite(RadioStation station) =>
        PluginActionIntent.CallPlugin(
            $"{InternetRadioController.ToggleFavouriteMethod}/{Uri.EscapeDataString(station.Id)}");

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
