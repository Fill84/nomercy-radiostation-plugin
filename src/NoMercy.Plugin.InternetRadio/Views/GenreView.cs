// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// One genre, every station in it, each ready to play.
public static class GenreView
{
    public static PluginView Build(StationCatalog catalog, string slug, UserState state)
    {
        IReadOnlyList<RadioStation> stations = catalog.ByGenreSlug(slug);

        if (stations.Count == 0)
        {
            // A stale bookmark or a genre that emptied out between refreshes. Not an
            // error - the way back is what is actually useful here.
            return PluginViews.Declarative(
                PluginViews.Container(
                    "genre-root",
                    BackToBrowse,
                    PluginViews.EmptyState(
                        "genre-empty",
                        "No stations in this genre",
                        "It may have been renamed or emptied since this page was last opened."
                    )
                )
            );
        }

        string label = GenreMap.BySlug(slug)?.Label ?? stations[0].Genre ?? GenreMap.Other;

        return PluginViews.Declarative(
            PluginViews.Container(
                "genre-root",
                BackToBrowse,
                PluginViews.Text("genre-title", label, "title"),
                PluginViews.Text("genre-count", $"{stations.Count} stations", "caption"),
                PluginViews.Grid("genre-grid", [.. stations
                    .Select(station => StationCards.WithFavourite(
                        station, state.Favourites.Any(favourite => favourite.Id == station.Id)))])
            )
        );
    }

    private static PluginComponent BackToBrowse =>
        PluginViews.Button(
            "genre-back",
            "All genres",
            PluginActionIntent.Navigate(RadioRoutes.Browse),
            icon: "arrowLeft"
        );
}
