// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// Every station in the catalogue, as the same tiles every other screen draws.
//
// It used to be a table of names and bitrates, on the reasoning that the grids play and
// this one inspects. That split cost more than it bought: it made one screen behave unlike
// every other, and a name in a row is a poorer way to recognise a station than its logo.
// The detail page is still there and still reachable - the difference is that this page no
// longer exists to be the only way in.
public static class AllStationsView
{
    public static PluginView Build(StationCatalog catalog, UserState state)
    {
        if (catalog.IsEmpty)
        {
            return PluginViews.Declarative(
                Ui.Container("all-root", BackToBrowse, EmptyCatalog.Build(catalog))
            );
        }

        // InvariantCulture, not CurrentCulture: this is the only culture-sensitive
        // operation in the plugin, and the container's locale must not change the order
        // between two otherwise-identical deployments.
        IReadOnlyList<RadioStation> stations =
        [
            .. catalog.Stations.OrderBy(
                station => station.Name, StringComparer.InvariantCultureIgnoreCase),
        ];

        return PluginViews.Declarative(
            Ui.Container(
                "all-root",
                BackToBrowse,
                Ui.Text("all-title", "All stations", "title"),
                Ui.Text(
                    "all-count",
                    stations.Count == 1 ? "1 station" : $"{stations.Count} stations",
                    "caption"
                ),
                StationCards.Grid("all-grid", stations, state, "all")
            )
        );
    }

    private static PluginComponent BackToBrowse =>
        Ui.Button(
            "all-back",
            "Back",
            PluginActionIntent.Navigate(RadioRoutes.Browse),
            icon: "arrowLeft"
        );
}
