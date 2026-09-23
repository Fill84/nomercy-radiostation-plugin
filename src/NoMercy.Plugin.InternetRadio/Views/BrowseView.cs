// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;
using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.State;

namespace NoMercy.Plugin.InternetRadio.Views;

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
        bool searchFailed = false,
        SearchFacets? facets = null,
        IStationLogos? logos = null)
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

            // The box itself, not a button leading to one. Searching is how a station
            // outside the seventeen-tag sweep is reached at all - which, since the curated
            // list went, is all but a handful of the fifty thousand in radio-browser - so
            // it belongs on the screen you land on rather than behind a click.
            //
            // The results appear here too, and they have to: a plugin endpoint answers
            // with data and cannot navigate, so after a submit the client refreshes the
            // route it is already on. Answering on a different route would mean submitting
            // here and the answer appearing on a page nothing takes you to.
            SearchView.Field(state.LastSearch ?? string.Empty, state, facets ?? new SearchFacets()),
        ];

        // Every axis, not just the name. A search by genre or country left this
        // false, so Popular stayed on screen under the results - two grids of
        // unrelated stations under one box, which is what this flag exists to
        // prevent and what it stopped preventing the moment filters arrived.
        bool searching = searchFailed
            || !string.IsNullOrWhiteSpace(state.LastSearch)
            || !string.IsNullOrWhiteSpace(state.LastGenre)
            || !string.IsNullOrWhiteSpace(state.LastCountry)
            || !string.IsNullOrWhiteSpace(state.LastLanguage);

        children.AddRange(
            SearchView.Results(state.LastSearch ?? string.Empty, searchResults ?? [], searchFailed, state, logos));

        // Absent, not empty. A heading over nothing reads as a screen that failed to
        // load, and everyone's first visit here has no favourites at all.
        if (state.Favourites.Count > 0)
        {
            // A key: "subtitle" also builds a plain text leaf under an
            // NMContentHeader, resolved the same way a "title" is.
            children.Add(PluginViews.Text("browse-favourites-heading", "stations.favourites", "subtitle"));
            children.Add(StationCards.Grid("browse-favourites", state.Favourites, "fav", logos));
        }

        children.Add(GenreChips(catalog));

        // Popular steps aside while a search is on screen. Two grids of unrelated stations
        // under one box is a page where it is not clear which one answered you.
        if (!searching)
        {
            children.Add(PluginViews.Text("browse-popular-heading", "stations.popular", "subtitle"));
            children.Add(StationCards.Grid(
                "browse-popular-grid",
                catalog.Popular(StationCards.PopularCount),
                "popular",
                logos));
        }

        return PluginViews.Declarative(PluginViews.Container("browse-root", [.. children]));
    }

    private static PluginComponent GenreChips(StationCatalog catalog)
    {
        List<PluginComponent> chips =
        [
            // The catalogue's own genre name, not chrome - there is no key for a word
            // radio-browser handed this plugin at runtime.
            .. catalog.Genres.Select(genre =>
                PluginViews.Button(
                    $"browse-genre-{genre.Section.Slug}",
                    $"{genre.Section.Label} ({genre.Count})",
                    PluginActionIntent.Navigate(RadioRoutes.Genre(genre.Section.Slug))
                )
            ),
            IconButton.Build(
                "browse-all",
                "stations.all",
                PluginActionIntent.Navigate(RadioRoutes.AllStations),
                icon: "gridMasonry"
            ),
        ];

        return PluginViews.Row("browse-genres", [.. chips]);
    }
}
