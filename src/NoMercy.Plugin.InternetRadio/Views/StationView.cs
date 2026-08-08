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
                Ui.Container(
                    "station-root",
                    BackButtons,
                    Ui.EmptyState(
                        "station-missing",
                        "Station not found",
                        "It may have been removed from the catalogue since this page was opened."
                    )
                )
            );
        }

        List<PluginComponent> children = [BackButtons];

        // Above the detail, because it is the thing you came for.
        if (Player(station) is { } player)
        {
            children.Add(player);
        }

        children.Add(
            Ui.Detail(
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
            );

        return PluginViews.Declarative(Ui.Container("station-root", [.. children]));
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

    // Playing happens in an embedded page this plugin serves, not through the dashboard's
    // player. The dashboard's player cannot play plugin media at all: it derives a track id
    // from the stream url and then uses that id as a CSS selector, so it throws before
    // requesting a byte. See PlayerPage, and app-web issue #15.
    //
    // Add-to-queue is gone with it - a queue belongs to the player that has one, and
    // offering a button that only ever raises an error toast is worse than not offering it.
    private static PluginComponent Actions(RadioStation station, bool isFavourite)
    {
        List<PluginComponent> buttons =
        [
            // Two states that differ by label and by variant, never by colour alone: a
            // toggle whose only difference is a tint is unreadable to a good share of
            // viewers, and to everyone in a screenshot.
            Ui.Button(
                $"station-favourite-{station.Id}",
                isFavourite ? StationCards.RemoveFavouriteLabel : StationCards.AddFavouriteLabel,
                StationCards.ToggleFavourite(station),
                variant: isFavourite ? "primary" : "secondary"
            ),
        ];

        // Only when there is somewhere safe to go. A button that opens nothing - or that
        // opens a javascript:/file:/data: URL a community-editable source supplied - is
        // worse than an absent one. See StationGates.IsSafeExternalUrl.
        if (StationGates.IsSafeExternalUrl(station.Homepage))
        {
            buttons.Add(
                Ui.Button(
                    $"station-homepage-{station.Id}",
                    "Open homepage",
                    PluginActionIntent.OpenWebView(station.Homepage),
                    icon: "globe"
                )
            );
        }

        return Ui.Row($"station-actions-{station.Id}", [.. buttons]);
    }

    /// <summary>
    /// The player, or nothing when this server's address is not known yet.
    ///
    /// Absent rather than broken: before any request has told the relay where this server
    /// lives there is no url to point an iframe at, and an empty frame reads as a failure.
    /// </summary>
    private static PluginComponent? Player(RadioStation station) =>
        MediaProxy.Player(station.Id) is { } url
            ? Ui.WebView($"station-player-{station.Id}", url)
            : null;

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
                Ui.TableRow(
                    $"station-fact-{station.Id}-{StationGates.Slugify(fact.Field)}",
                    new Dictionary<string, object?> { ["field"] = fact.Field, ["value"] = fact.Value }
                )
            );

        return Ui.Table($"station-facts-{station.Id}", Columns, [.. rows]);
    }

    private static string Provenance(RadioStation station) =>
        station.IsUserSupplied
            ? $"Your own {StationOverrides.FileName}"
            : $"radio-browser.info ({station.Id})";

    // The spec calls for both: back to the table this page is usually reached
    // from, and back to the landing page for someone who arrived here from a
    // bookmark or a grid card instead.
    private static PluginComponent BackButtons =>
        Ui.Row(
            "station-back-row",
            Ui.Button(
                "station-back-all",
                "All stations",
                PluginActionIntent.Navigate(RadioRoutes.AllStations),
                icon: "arrowLeft"
            ),
            Ui.Button(
                "station-back-browse",
                "Home",
                PluginActionIntent.Navigate(RadioRoutes.Browse),
                icon: "home"
            )
        );
}
