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

    public static PluginView Build(StationCatalog catalog, string id)
    {
        RadioStation? station = catalog.ById(id);

        if (station is null)
        {
            // The catalogue refreshes underneath an open page, so a link followed a
            // minute later can point at a station that is no longer listed.
            return PluginViews.Declarative(
                PluginViews.Container(
                    "station-root",
                    BackToAll,
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
                BackToAll,
                PluginViews.Detail(
                    $"station-detail-{station.Id}",
                    station.Name,
                    Description(station),
                    station.LogoUrl,
                    Actions(station),
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

    private static PluginComponent Actions(RadioStation station)
    {
        List<PluginComponent> buttons =
        [
            PluginViews.Button(
                $"station-play-{station.Id}",
                "Play",
                PluginActionIntent.PlayMedia(
                    station.StreamUrl, station.Name, station.Genre, station.LogoUrl),
                icon: "play"
            ),
            PluginViews.Button(
                $"station-enqueue-{station.Id}",
                "Add to queue",
                PluginActionIntent.Enqueue(
                    station.StreamUrl, station.Name, station.Genre, station.LogoUrl),
                icon: "playlistAdd"
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

    private static PluginComponent BackToAll =>
        PluginViews.Button(
            "station-back",
            "All stations",
            PluginActionIntent.Navigate(RadioRoutes.AllStations),
            icon: "arrowLeft"
        );
}
