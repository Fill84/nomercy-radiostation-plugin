// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;
using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.State;

namespace NoMercy.Plugin.InternetRadio.Views;

// One station: what it is, and the three things you can do with it.
public static class StationView
{
    /// <summary>
    /// How often a page with a stream on it asks for itself again, in seconds.
    ///
    /// A minted stream url is good for fifteen minutes; ten leaves room for a slow page. A
    /// page left open longer than the url lasts would offer one the server refuses - and
    /// the player says nothing when a stream fails to load.
    /// </summary>
    public const int StreamRefreshSeconds = 600;

    private static IReadOnlyList<PluginTableColumn> Columns { get; } =
        [
            new() { Key = "field", Label = "Field", Width = "12rem" },
            new() { Key = "value", Label = "Value" },
        ];

    public static PluginView Build(StationCatalog catalog, string id, UserState state) =>
        Build(catalog.ById(id) ?? state.Favourites.FirstOrDefault(kept => kept.Id == id), state);

    /// <summary>
    /// The page for an already-resolved station.
    ///
    /// Resolution moved out of the view because a card can now be opened from a search
    /// result, and a search result is not in the catalogue: it came straight off
    /// radio-browser and was never cached. Looking it up needs the network, and views here
    /// are pure Build methods on purpose.
    /// </summary>
    public static PluginView Build(
        RadioStation? station,
        UserState state,
        string? nowPlaying = null,
        StationStream? stream = null,
        IStationLogos? logos = null)
    {
        if (station is null)
        {
            // The catalogue refreshes underneath an open page, so a link followed a
            // minute later can point at a station that is no longer listed.
            return PluginViews.Declarative(
                PluginViews.Container(
                    "station-root",
                    BackButtons,
                    // The title WOULD resolve - EmptyState's title is a plain text
                    // leaf - but it stays English: every ViewFixtures.All() fixture
                    // asks StationView for a station that exists ("groove"), so this
                    // branch is never reached by TranslationKeys.Drawn(), and a key
                    // nothing in the shared fixtures draws is exactly what
                    // TranslationTests.EveryKeyInTheBundleIsOneAViewDraws exists to
                    // catch. The message stays literal regardless - it lands on an
                    // NMHelper's `helperText`, which NMRender never resolves.
                    PluginViews.EmptyState(
                        "station-missing",
                        "Station not found",
                        "It may have been removed from the catalogue since this page was opened."
                    )
                )
            );
        }

        // Inline, like every other logo this plugin draws: nothing else reaches an
        // image element on this platform any more.
        string? cover = (logos ?? NoStationLogos.Instance).DataUriFor(station);

        PluginComponent page = PluginViews.Container(
            "station-root",
            BackButtons,
            PluginViews.Detail(
                $"station-detail-{station.Id}",
                station.Name,
                Description(station),
                cover,
                Actions(station, state.Favourites.Any(kept => kept.Id == station.Id), stream, cover),
                Facts(station, nowPlaying)
            )
        );

        return stream is { IsPlayable: true }
            ? PluginViews.Declarative(StreamRefreshSeconds, page)
            : PluginViews.Declarative(page);
    }

    /// <summary>
    /// Composed only from what is known, so a sparse station reads as a short
    /// sentence rather than one full of blanks.
    /// </summary>
    private static string? Description(RadioStation station)
    {
        List<string> sentences = [];

        string where = station.Country is { } country ? $" from {country}" : string.Empty;
        if (station.Genre is { } genre)
        {
            sentences.Add($"{genre}{where}.");
        }
        else if (station.Country is { } only)
        {
            sentences.Add($"Broadcasting from {only}.");
        }

        string quality = string.Join(
            ' ',
            new[]
            {
                station.BitrateKbps is { } kbps ? $"{kbps} kbps" : null,
                station.Codec,
            }.Where(part => !string.IsNullOrWhiteSpace(part))
        );

        if (!string.IsNullOrWhiteSpace(quality))
        {
            sentences.Add($"{quality}.");
        }

        return sentences.Count > 0 ? string.Join(' ', sentences) : null;
    }

    // This page is where playing, queueing and keeping a station live.
    //
    // Every label built here - Play, Add to queue, the favourite toggle - stays
    // English on purpose, and so does the unavailable caption below. A button's
    // label becomes an unresolved `ariaLabel` prop as well as a resolved text leaf
    // (see EmptyCatalog.Build), and a caption's text lands on an NMHelper's
    // `helperText`, which NMRender never resolves at all - neither prop can carry a
    // key without the key itself reaching the viewer.
    private static PluginComponent Actions(
        RadioStation station, bool isFavourite, StationStream? stream, string? cover)
    {
        List<PluginComponent> items = [];

        if (stream is { IsPlayable: true, Url: { } url })
        {
            items.Add(IconButton.Build($"station-play-{station.Id}", "Play", StationCards.Play(station, url, cover), icon: "play"));
            items.Add(
                IconButton.Build(
                    $"station-enqueue-{station.Id}",
                    "Add to queue",
                    StationCards.Enqueue(station, url, cover),
                    icon: "playlistAdd"
                )
            );
        }
        else
        {
            // No fallback to the station's own address: the browser refuses it, so a
            // button with it would be a button that does nothing.
            items.Add(PluginViews.Text($"station-stream-unavailable-{station.Id}", Unavailable(stream), "caption"));
        }

        // Two states that differ by label and by variant, never by colour alone: a
        // toggle whose only difference is a tint is unreadable to a good share of
        // viewers, and to everyone in a screenshot.
        items.Add(
            PluginViews.Button(
                $"station-favourite-{station.Id}",
                isFavourite ? StationCards.RemoveFavouriteLabel : StationCards.AddFavouriteLabel,
                StationCards.ToggleFavourite(station),
                variant: isFavourite ? "primary" : "secondary"
            )
        );

        return PluginViews.Row($"station-actions-{station.Id}", [.. items]);
    }

    // What the viewer can do about it depends on why: a missing approval is the owner's
    // to give, anything else is this server's to fix.
    private static string Unavailable(StationStream? stream) =>
        stream?.RefusalCode is PluginRefusalCodes.CapabilityScopeRefused
            or PluginRefusalCodes.CapabilityNotDeclared
            ? "Playing is waiting for the server owner to approve streaming for Internet Radio."
            : "This server cannot play stations right now. The server owner can find the reason in the server log.";

    private static PluginComponent Facts(RadioStation station, string? nowPlaying)
    {
        List<(string Field, string? Value)> facts =
        [
            // First, and above the station's own attributes: it is the one line that
            // changes while the page is open, and the one a listener came to read.
            // Absent until the station announces something, which the row filter below
            // already handles - a station that sends no metadata simply has no row
            // rather than one saying it does not know.
            ("Now playing", nowPlaying),
            ("Genre", station.Genre),
            ("Country", station.Country),
            ("Language", station.Language),
            ("Bitrate", station.BitrateKbps is { } kbps ? $"{kbps} kbps" : null),
            ("Codec", station.Codec),
            // Shown in full. It is the first thing worth having when a station will
            // not play, and the table scrolls horizontally rather than truncating.
            ("Stream", station.StreamUrl),
            ("Source", Provenance(station)),
        ];

        IEnumerable<PluginComponent> rows = facts
            .Where(fact => !string.IsNullOrWhiteSpace(fact.Value))
            .Select(fact =>
                PluginViews.Row(
                    $"station-fact-{station.Id}-{StationGates.Slugify(fact.Field)}",
                    new Dictionary<string, object?> { ["field"] = fact.Field, ["value"] = fact.Value }
                )
            );

        return PluginViews.Table($"station-facts-{station.Id}", Columns, [.. rows]);
    }

    private static string Provenance(RadioStation station) =>
        station.IsUserSupplied
            ? $"Your own {StationOverrides.FileName}"
            : $"radio-browser.info ({station.Id})";

    // The spec calls for both: back to the table this page is usually reached
    // from, and back to the landing page for someone who arrived here from a
    // bookmark or a grid card instead.
    //
    // Both labels stay English: a button's label also becomes an unresolved
    // `ariaLabel` prop, so keying either would leave the raw key in the accessible
    // name. See EmptyCatalog.Build.
    private static PluginComponent BackButtons =>
        PluginViews.Row(
            "station-back-row",
            IconButton.Build(
                "station-back-all",
                "All stations",
                PluginActionIntent.Navigate(RadioRoutes.AllStations),
                icon: "arrowLeft"
            ),
            IconButton.Build(
                "station-back-browse",
                "Home",
                PluginActionIntent.Navigate(RadioRoutes.Browse),
                icon: "home"
            )
        );
}
