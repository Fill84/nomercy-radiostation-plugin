// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// The landing screen: what genres exist, and the most popular stations ready to play.
//
// Deliberately NOT one grid per genre. That would put every station on one page and
// make the genre routes redundant; this page answers "what is there and give me
// something now", and the genre pages answer "show me all of one kind".
public static class BrowseView
{
    public static PluginView Build(StationCatalog catalog)
    {
        if (catalog.IsEmpty)
        {
            return PluginViews.Declarative(EmptyCatalog.Build(catalog));
        }

        List<PluginComponent> children =
        [
            PluginViews.Text("browse-title", "Internet Radio", "title"),
            PluginViews.Text(
                "browse-summary",
                $"{catalog.Count} stations across {catalog.Genres.Count} genres. Pick one and it plays.",
                "caption"
            ),
            GenreChips(catalog),
            PluginViews.Text("browse-popular-heading", "Popular", "subtitle"),
            PluginViews.Grid(
                "browse-popular-grid",
                [.. catalog.Popular(StationCards.PopularCount).Select(StationCards.Play)]
            ),
        ];

        return PluginViews.Declarative(PluginViews.Container("browse-root", [.. children]));
    }

    private static PluginComponent GenreChips(StationCatalog catalog)
    {
        List<PluginComponent> chips =
        [
            .. catalog.Genres.Select(genre =>
                PluginViews.Button(
                    $"browse-genre-{genre.Section.Slug}",
                    $"{genre.Section.Label} ({genre.Count})",
                    PluginActionIntent.Navigate(RadioRoutes.Genre(genre.Section.Slug))
                )
            ),
            PluginViews.Button(
                "browse-all",
                "All stations",
                PluginActionIntent.Navigate(RadioRoutes.AllStations),
                icon: "gridMasonry"
            ),
        ];

        return PluginViews.Row("browse-genres", [.. chips]);
    }
}
