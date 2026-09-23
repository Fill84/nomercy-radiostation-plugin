// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;
using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.Controllers;

namespace NoMercy.Plugin.InternetRadio.Views;

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

    /// <summary>Shown on the toggle when the station is not a favourite yet.</summary>
    public const string AddFavouriteLabel = "Add to favourites";

    /// <summary>Shown on the toggle when it is.</summary>
    public const string RemoveFavouriteLabel = "Remove from favourites";

    /// <summary>Stations in the app's own grid.</summary>
    public static PluginComponent Grid(
        string id,
        IEnumerable<RadioStation> stations,
        string scope,
        IStationLogos? logos = null) =>
        Ui.MediaGrid(id, stations.Select(station => Tile(station, scope, logos)));

    /// <summary>
    /// One station, as the app draws an artist: a square cover, the name under it, and a
    /// link to its own page.
    /// </summary>
    public static PluginComponent Tile(RadioStation station, string scope = "", IStationLogos? logos = null) =>
        Ui.MediaCard(
            $"station-tile-{Scoped(scope, station.Id)}",
            station.Name,
            AppRoutes.Station(station.Id),
            // A data: URI or nothing. Null draws the card's own placeholder, which is also
            // what a station without a logo has always shown.
            (logos ?? NoStationLogos.Instance).DataUriFor(station),
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
    /// in neither - which is what throws before any audio starts, and why no playback
    /// request ever reaches this server. See
    /// docs/upstream/2026-08-08-plugin-media-cannot-play.md.
    ///
    /// Sending it costs nothing and is ignored until the client reads it. A station's
    /// radio-browser uuid is also the honest key: it is what the nowplaying route and
    /// every station lookup resolve on, and it does not change when a station moves its
    /// stream, which a url-derived id does - so history and resume state stop hanging off
    /// something that was never a key.
    /// </summary>
    public const string StationIdKey = "id";

    /// <summary>
    /// The play intent for a station, so every screen starts the same station the same way.
    ///
    /// The url is one the host minted for this listener; the cover is inline. Neither is
    /// the station's own address, which the browser would refuse.
    /// </summary>
    public static PluginActionIntent Play(RadioStation station, string streamUrl, string? cover) =>
        // No artist on the item: a live stream's artist changes with every track and
        // arrives through now-playing.
        WithNowPlaying(
            WithStationId(PluginActionIntent.PlayMedia(streamUrl, station.Name, null, cover), station));

    /// <summary>The payload key a client reads to learn that this item announces its track.</summary>
    public const string NowPlayingKey = "nowPlaying";

    /// <summary>
    /// How often, in seconds, a client should ask what is on air - and, before the first
    /// answer, how soon to ask again.
    /// </summary>
    private const int SettledSeconds = 15;

    private const int FirstSeconds = 2;

    /// <summary>
    /// Says that this item's track changes while it plays, and how often to ask.
    ///
    /// Declared by the plugin rather than assumed by the client. A client that polls
    /// every plugin item it is handed is deciding a plugin's behaviour for it, and it
    /// picks the interval for a stream it knows nothing about. A station announces its
    /// track, so this station's item says so; an item that does not carry this block is
    /// never asked.
    /// </summary>
    private static PluginActionIntent WithNowPlaying(PluginActionIntent intent)
    {
        intent.Payload[NowPlayingKey] = new Dictionary<string, object>
        {
            ["method"] = InternetRadioController.NowPlayingMethod,
            ["intervalSeconds"] = SettledSeconds,
            ["firstIntervalSeconds"] = FirstSeconds
        };

        return intent;
    }

    /// <summary>Queueing a station, from the same minted url and cover as <see cref="Play"/>.</summary>
    ///
    /// <remarks>
    /// The url is the host's, minted for this listener, and it is valid for fifteen
    /// minutes - see <c>HostStationStreams</c>. Nothing here bounds how long a station
    /// waits in the queue before its turn comes, so a queue that sits longer than that
    /// carries a url the host will answer 403 to when playback finally starts. That is
    /// accepted rather than worked around: refreshing a queued url would need the queue
    /// itself to ask this plugin again, which it has no way to do, and the fix already
    /// exists - opening the station's own page mints a fresh one. See the plugin README.
    /// </remarks>
    public static PluginActionIntent Enqueue(RadioStation station, string streamUrl, string? cover) =>
        WithNowPlaying(
            WithStationId(PluginActionIntent.Enqueue(streamUrl, station.Name, null, cover), station));

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

    /// <summary>Genre and country, whichever of them is known. Null when neither is.</summary>
    public static string? Subtitle(RadioStation station)
    {
        string[] parts =
            [.. new[] { station.Genre, station.Country }.Where(part => !string.IsNullOrWhiteSpace(part))!];

        return parts.Length > 0 ? string.Join(" · ", parts) : null;
    }
}
