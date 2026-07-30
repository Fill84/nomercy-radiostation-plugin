// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// Every station with its metadata, as a table whose rows lead to the detail page.
//
// The split is deliberate: the grids play on click, this inspects on click. Putting
// both affordances on one surface means every station needs two hit targets, and a
// card is one.
public static class AllStationsView
{
    /// <summary>Shown where a value is not known. Never "0", which would be a claim.</summary>
    private const string Unknown = "—";

    private static IReadOnlyList<PluginTableColumn> Columns { get; } =
        [
            new() { Key = "name", Label = "Station" },
            new() { Key = "genre", Label = "Genre" },
            new() { Key = "country", Label = "Country" },
            new() { Key = "bitrate", Label = "Bitrate", Align = "right" },
            new() { Key = "codec", Label = "Codec" },
        ];

    public static PluginView Build(StationCatalog catalog)
    {
        if (catalog.IsEmpty)
        {
            return PluginViews.Declarative(
                PluginViews.Container("all-root", BackToBrowse, EmptyCatalog.Build(catalog))
            );
        }

        IEnumerable<PluginComponent> rows = catalog
            .Stations.OrderBy(station => station.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(Row);

        return PluginViews.Declarative(
            PluginViews.Container(
                "all-root",
                BackToBrowse,
                PluginViews.Text("all-title", "All stations", "title"),
                PluginViews.Text(
                    "all-hint",
                    "Select a station to see its details and play it.",
                    "caption"
                ),
                PluginViews.Table("all-table", Columns, [.. rows], "No stations.")
            )
        );
    }

    private static PluginComponent Row(RadioStation station) =>
        PluginViews.Row(
            $"all-row-{station.Id}",
            new Dictionary<string, object?>
            {
                ["name"] = station.Name,
                ["genre"] = station.Genre ?? Unknown,
                ["country"] = station.Country ?? Unknown,
                // Formatted here rather than sent as a number with a Bytes/Rate cell
                // type: neither of those means kbps, and both would be relabelled by
                // the client into something this is not.
                ["bitrate"] = station.BitrateKbps is { } kbps ? $"{kbps} kbps" : Unknown,
                ["codec"] = station.Codec ?? Unknown,
            },
            PluginActionIntent.Navigate(RadioRoutes.Station(station.Id))
        );

    private static PluginComponent BackToBrowse =>
        PluginViews.Button(
            "all-back",
            "Back",
            PluginActionIntent.Navigate(RadioRoutes.Browse),
            icon: "arrowLeft"
        );
}
