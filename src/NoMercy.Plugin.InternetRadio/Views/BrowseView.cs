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

        List<PluginComponent> children =
        [
            // No title here. The host draws the plugin's name as the page heading already,
            // and repeating it put "Internet Radio" on screen twice, one line apart.
            Ui.Text(
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
            SearchView.Field(state.LastSearch ?? string.Empty, state),
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
            SearchView.Results(state.LastSearch ?? string.Empty, searchResults ?? [], searchFailed, state));

        // Absent, not empty. A heading over nothing reads as a screen that failed to
        // load, and everyone's first visit here has no favourites at all.
        if (state.Favourites.Count > 0)
        {
            children.Add(Ui.Text("browse-favourites-heading", "Favourites", "subtitle"));
            children.Add(StationCards.Grid("browse-favourites", state.Favourites, state, "fav"));
        }

        children.Add(GenreChips(catalog));

        // Popular steps aside while a search is on screen. Two grids of unrelated stations
        // under one box is a page where it is not clear which one answered you.
        if (!searching)
        {
            children.Add(Ui.Text("browse-popular-heading", "Popular", "subtitle"));
            children.Add(StationCards.Grid(
                "browse-popular-grid",
                catalog.Popular(StationCards.PopularCount),
                state,
                "popular"));
        }

        return PluginViews.Declarative(Ui.Container("browse-root", [.. children]));
    }

    private static PluginComponent GenreChips(StationCatalog catalog)
    {
        List<PluginComponent> chips =
        [
            .. catalog.Genres.Select(genre =>
                Ui.Button(
                    $"browse-genre-{genre.Section.Slug}",
                    $"{genre.Section.Label} ({genre.Count})",
                    PluginActionIntent.Navigate(RadioRoutes.Genre(genre.Section.Slug))
                )
            ),
            Ui.Button(
                "browse-all",
                "All stations",
                PluginActionIntent.Navigate(RadioRoutes.AllStations),
                icon: "gridMasonry"
            ),
        ];

        return Ui.Row("browse-genres", [.. chips]);
    }
}
