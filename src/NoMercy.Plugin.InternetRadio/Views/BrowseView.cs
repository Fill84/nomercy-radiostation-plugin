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
    public static PluginView Build(
        StationCatalog catalog,
        UserState state,
        IReadOnlyList<RadioStation>? searchResults = null,
        bool searchFailed = false)
    {
        if (catalog.IsEmpty)
        {
            return PluginViews.Declarative(EmptyCatalog.Build(catalog));
        }

        // A set, not a scan per card. A grid is eighteen cards and a favourites list is
        // unbounded, so the naive form is quadratic in the two things most likely to grow.
        HashSet<string> favourites = [.. state.Favourites.Select(station => station.Id)];

        List<PluginComponent> children =
        [
            // No title here. The host draws the plugin's name as the page heading already,
            // and repeating it put "Internet Radio" on screen twice, one line apart.
            PluginViews.Text(
                "browse-summary",
                $"{catalog.Count} stations across {catalog.Genres.Count} genres. Pick one and it plays.",
                "caption"
            ),

            // Above everything else it could be below. Searching is how a station outside
            // the seventeen-tag sweep is reached at all, which since the curated list went
            // is most of the database - so it belongs on the screen you land on rather
            // than behind a click.
            SearchView.Field(state.LastSearch),
        ];

        // Results go directly under the field that produced them. They cannot live on a
        // route of their own: a controller answers with data and cannot navigate, so the
        // client refreshes the page the form was on - see the comment in SearchView.
        bool searching = searchFailed || !string.IsNullOrWhiteSpace(state.LastSearch);
        children.AddRange(
            SearchView.Results(state.LastSearch, searchResults ?? [], searchFailed, state));

        // Absent, not empty. A heading over nothing reads as a screen that failed to
        // load, and everyone's first visit here has no favourites at all.
        if (state.Favourites.Count > 0)
        {
            children.Add(PluginViews.Text("browse-favourites-heading", "Favourites", "subtitle"));
            children.Add(PluginViews.Grid(
                "browse-favourites",
                [.. state.Favourites.Select(station => StationCards.WithFavourite(station, true, "fav"))]
            ));
        }

        children.Add(GenreChips(catalog));

        // Popular steps aside while a search is on screen. Two grids of unrelated stations
        // under one field is a page where it is not clear which one answered you.
        if (!searching)
        {
            children.Add(PluginViews.Text("browse-popular-heading", "Popular", "subtitle"));
            children.Add(PluginViews.Grid(
                "browse-popular-grid",
                [.. catalog.Popular(StationCards.PopularCount)
                    .Select(station => StationCards.WithFavourite(
                        station, favourites.Contains(station.Id), "popular"))]
            ));
        }

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
