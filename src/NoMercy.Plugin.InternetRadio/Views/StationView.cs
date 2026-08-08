// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// One station: what it is, and the three things you can do with it.
public static class StationView
{
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
    public static PluginView Build(RadioStation? station, UserState state)
    {
        if (station is null)
        {
            // The catalogue refreshes underneath an open page, so a link followed a
            // minute later can point at a station that is no longer listed.
            return PluginViews.Declarative(
                PluginViews.Container(
                    "station-root",
                    BackButtons,
                    PluginViews.EmptyState(
                        "station-missing",
                        "Station not found",
                        "It may have been removed from the catalogue since this page was opened."
                    )
                )
            );
        }

        return PluginViews.Declarative(
            PluginViews.Container(
                "station-root",
                BackButtons,
                PluginViews.Detail(
                    $"station-detail-{station.Id}",
                    station.Name,
                    Description(station),
                    // Relayed, like every other image this plugin draws: the dashboard's
                    // img-src refuses the station's own host, so its own url renders as a
                    // broken icon.
                    StationCards.CoverUrl(station) is null ? null : MediaProxy.Cover(station.Id),
                    Actions(station, state.Favourites.Any(kept => kept.Id == station.Id)),
                    Facts(station)
                )
            )
        );
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

    // This page is where playing and keeping a station live, because the card that opens it
    // cannot carry either: NMMusicCard's context_menu_items take a string action
    // identifier, not a PluginActionIntent.
    private static PluginComponent Actions(RadioStation station, bool isFavourite)
    {
        List<PluginComponent> buttons =
        [
            // Built by StationCards so the station page and every grid start the same
            // station the same way. Both go through this plugin's own relay, and neither
            // passes an artist - the genre used to go there, and the player builds an
            // artist LINK out of it and then fails to resolve a route for a genre that is
            // not one, which was the "Something went wrong" toast on every play.
            PluginViews.Button(
                $"station-play-{station.Id}",
                "Play",
                StationCards.Play(station),
                icon: "play"
            ),
            PluginViews.Button(
                $"station-enqueue-{station.Id}",
                "Add to queue",
                StationCards.Enqueue(station),
                icon: "playlistAdd"
            ),
            // Two states that differ by label and by variant, never by colour alone: a
            // toggle whose only difference is a tint is unreadable to a good share of
            // viewers, and to everyone in a screenshot.
            PluginViews.Button(
                $"station-favourite-{station.Id}",
                isFavourite ? StationCards.RemoveFavouriteLabel : StationCards.AddFavouriteLabel,
                StationCards.ToggleFavourite(station),
                variant: isFavourite ? "primary" : "secondary"
            ),
        ];

        // Only when there is somewhere safe to go. A button that opens nothing - or
        // that opens a javascript:/file:/data: URL a community-editable source
        // supplied - is worse than an absent one. See StationGates.IsSafeExternalUrl.
        if (StationGates.IsSafeExternalUrl(station.Homepage))
        {
            buttons.Add(
                PluginViews.Button(
                    $"station-homepage-{station.Id}",
                    "Open homepage",
                    PluginActionIntent.OpenWebView(station.Homepage),
                    icon: "globe"
                )
            );
        }

        return PluginViews.Row($"station-actions-{station.Id}", [.. buttons]);
    }

    private static PluginComponent Facts(RadioStation station)
    {
        List<(string Field, string? Value)> facts =
        [
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
    private static PluginComponent BackButtons =>
        PluginViews.Row(
            "station-back-row",
            PluginViews.Button(
                "station-back-all",
                "All stations",
                PluginActionIntent.Navigate(RadioRoutes.AllStations),
                icon: "arrowLeft"
            ),
            PluginViews.Button(
                "station-back-browse",
                "Home",
                PluginActionIntent.Navigate(RadioRoutes.Browse),
                icon: "home"
            )
        );
}
