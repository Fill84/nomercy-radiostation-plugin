// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;
using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.State;

namespace NoMercy.Plugin.InternetRadio.Views;

// One genre, every station in it, each ready to play.
public static class GenreView
{
    public static PluginView Build(
        StationCatalog catalog, string slug, UserState state, IStationLogos? logos = null)
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
                    // The title WOULD resolve - EmptyState's title is a plain text
                    // leaf - but it stays English: every ViewFixtures.All() fixture
                    // asks GenreView for a genre that exists ("ambient"), so this
                    // branch is never reached by TranslationKeys.Drawn(), and a key
                    // nothing in the shared fixtures draws is exactly what
                    // TranslationTests.EveryKeyInTheBundleIsOneAViewDraws exists to
                    // catch. The message stays literal regardless - it lands on an
                    // NMHelper's `helperText`, which NMRender never resolves.
                    PluginViews.EmptyState(
                        "genre-empty",
                        "No stations in this genre",
                        "It may have been renamed or emptied since this page was last opened."
                    )
                )
            );
        }

        // The catalogue's own genre name, not chrome - there is no key for a word
        // radio-browser handed this plugin at runtime. "genre.title" exists, but only
        // for the route table's generic breadcrumb label - see InternetRadioPlugin.Table.
        string label = GenreMap.BySlug(slug)?.Label ?? stations[0].Genre ?? GenreMap.Other;

        return PluginViews.Declarative(
            PluginViews.Container(
                "genre-root",
                BackToBrowse,
                PluginViews.Text("genre-title", label, "title"),
                PluginViews.Text("genre-count", $"{stations.Count} stations", "caption"),
                StationCards.Grid("genre-grid", stations, "genre", logos)
            )
        );
    }

    // Left English: a button's label also becomes an unresolved `ariaLabel` prop, so
    // keying it would leave the raw key in the accessible name rather than translating
    // it. Every button and icon-button label in this plugin stays English for the
    // same reason - see EmptyCatalog.Build.
    private static PluginComponent BackToBrowse =>
        IconButton.Build(
            "genre-back",
            "All genres",
            PluginActionIntent.Navigate(RadioRoutes.Browse),
            icon: "arrowLeft"
        );
}
