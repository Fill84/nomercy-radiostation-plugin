// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;
using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.State;

namespace NoMercy.Plugin.InternetRadio.Views;

// Every station in the catalogue, as the same tiles every other screen draws.
//
// It used to be a table of names and bitrates, on the reasoning that the grids play and
// this one inspects. That split cost more than it bought: it made one screen behave unlike
// every other, and a name in a row is a poorer way to recognise a station than its logo.
// The detail page is still there and still reachable - the difference is that this page no
// longer exists to be the only way in.
public static class AllStationsView
{
    public static PluginView Build(StationCatalog catalog, UserState state, IStationLogos? logos = null)
    {
        if (catalog.IsEmpty)
        {
            return PluginViews.Declarative(
                PluginViews.Container("all-root", BackToBrowse, EmptyCatalog.Build(catalog))
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
            PluginViews.Container(
                "all-root",
                BackToBrowse,
                PluginViews.Text("all-title", "stations.all", "title"),
                PluginViews.Text(
                    "all-count",
                    stations.Count == 1 ? "1 station" : $"{stations.Count} stations",
                    "caption"
                ),
                StationCards.Grid("all-grid", stations, "all", logos)
            )
        );
    }

    private static PluginComponent BackToBrowse =>
        IconButton.Build(
            "all-back",
            "back",
            PluginActionIntent.Navigate(RadioRoutes.Browse),
            icon: "arrowLeft"
        );
}
