// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// Finding a station the genre sweep never returned.
//
// Pure Build, like every view here: the query runs in the plugin, and this renders what
// came back. A view that reached the network could not be tested exhaustively, and this
// one has four states that all have to be told apart on sight.
public static class SearchView
{
    public const string FieldName = "query";

    public static PluginView Build(
        string? term, IReadOnlyList<RadioStation> results, bool queryFailed, UserState? state = null) =>
        PluginViews.Declarative(
            PluginViews.Container(
                "search-root",
                BackToBrowse,
                PluginViews.Text("search-title", "Search stations", "title"),
                Field(term),
                Body(term, results, queryFailed, state ?? UserState.Empty)
            )
        );

    /// <summary>
    /// The field, carrying whatever was last searched for. A submitted query that
    /// vanishes from the box reads as a search that was lost rather than one that ran.
    /// </summary>
    public static PluginComponent Field(string? term) =>
        PluginViews.Form(
            "search-form",
            "Search",
            PluginActionIntent.CallPlugin(InternetRadioController.SearchMethod),
            new PluginFormField
            {
                Name = FieldName,
                Label = "Station name",
                Type = PluginFormFieldType.Text,
                Value = term,
                Placeholder = "Search every station on radio-browser",
            }
        );

    // Three ways to have no results, and they must not look alike. "We could not reach
    // radio-browser" and "there is no such station" ask the user to do different things,
    // and a single "nothing found" would have them retrying the wrong one.
    private static PluginComponent Body(
        string? term, IReadOnlyList<RadioStation> results, bool queryFailed, UserState state)
    {
        if (queryFailed)
        {
            return PluginViews.EmptyState(
                "search-failed",
                "Search is unavailable",
                "radio-browser did not answer. Try again in a moment."
            );
        }

        if (string.IsNullOrWhiteSpace(term))
        {
            return PluginViews.EmptyState(
                "search-idle",
                "Search for a station",
                "Type a name to find stations anywhere in the radio-browser database."
            );
        }

        if (results.Count == 0)
        {
            return PluginViews.EmptyState(
                "search-empty",
                "Nothing found",
                $"No playable station matches \"{term}\"."
            );
        }

        return PluginViews.Grid("search-grid", [.. results
            .Select(station => StationCards.WithFavourite(
                station, state.Favourites.Any(favourite => favourite.Id == station.Id)))]);
    }

    private static PluginComponent BackToBrowse =>
        PluginViews.Button(
            "search-back",
            "Browse",
            PluginActionIntent.Navigate(RadioRoutes.Browse),
            icon: "arrowLeft"
        );
}
