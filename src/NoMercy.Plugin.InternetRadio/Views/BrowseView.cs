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
    public static PluginView Build(StationCatalog catalog, UserState state)
    {
        if (catalog.IsEmpty)
        {
            return PluginViews.Declarative(EmptyCatalog.Build(catalog));
        }

        List<PluginComponent> children =
        [
            // No title here. The host draws the plugin's name as the page heading already,
            // and repeating it put "Internet Radio" on screen twice, one line apart.
            PluginViews.Text(
                "browse-summary",
                $"{catalog.Count} stations across {catalog.Genres.Count} genres. Pick one and it plays.",
                "caption"
            ),

            // High on the page, because searching is how a station outside the
            // seventeen-tag sweep is reached at all - which, since the curated list went,
            // is all but a handful of the fifty thousand in radio-browser.
            PluginViews.Button(
                "browse-search",
                "Search stations",
                PluginActionIntent.Navigate(RadioRoutes.SearchRoot),
                icon: "magnifyingGlass"),
        ];

        // Absent, not empty. A heading over nothing reads as a screen that failed to
        // load, and everyone's first visit here has no favourites at all.
        if (state.Favourites.Count > 0)
        {
            children.Add(PluginViews.Text("browse-favourites-heading", "Favourites", "subtitle"));
            children.Add(StationCards.Grid("browse-favourites", state.Favourites, state, "fav"));
        }

        children.Add(GenreChips(catalog));

        children.Add(PluginViews.Text("browse-popular-heading", "Popular", "subtitle"));
        children.Add(StationCards.Grid(
            "browse-popular-grid",
            catalog.Popular(StationCards.PopularCount),
            state,
            "popular"));

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
