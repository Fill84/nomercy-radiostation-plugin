// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// The search field and its results, as sections of whatever page draws them.
//
// Not a page of its own any more. A plugin controller answers with a data envelope and
// cannot tell the client where to navigate, so a form that submits on one route can only
// ever be answered on that same route - the client refreshes where it already is. Putting
// the field on the browse page and the results behind /search meant the results were
// unreachable by any sequence of clicks: the term was stored, the page came back, and it
// was still the browse page.
//
// So the results render where the field is. /search still resolves, and renders the same
// page, so an old link is not a dead end.
public static class SearchView
{
    public const string FieldName = "query";

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

    /// <summary>
    /// What a search turned up, or why it turned up nothing.
    ///
    /// Three ways to have no results, and they must not look alike: unreachable, nothing
    /// typed yet, and nothing matched. Reporting an outage as an empty result set has the
    /// viewer retrying a search that was never the problem.
    /// </summary>
    public static IEnumerable<PluginComponent> Results(
        string? term, IReadOnlyList<RadioStation> results, bool queryFailed, UserState state)
    {
        if (queryFailed)
        {
            yield return Heading("search-results-heading", "Search");
            yield return PluginViews.EmptyState(
                "search-failed",
                "Search is unavailable",
                "radio-browser did not answer. Try again in a moment."
            );
            yield return ClearButton();
            yield break;
        }

        if (string.IsNullOrWhiteSpace(term))
        {
            yield break;
        }

        yield return Heading("search-results-heading", $"Results for “{term}”");

        if (results.Count == 0)
        {
            yield return PluginViews.EmptyState(
                "search-empty",
                "Nothing found",
                $"No playable station matches “{term}”."
            );
        }
        else
        {
            yield return PluginViews.Grid(
                "search-grid",
                [.. results.Select(station => StationCards.WithFavourite(
                    station,
                    state.Favourites.Any(favourite => favourite.Id == station.Id),
                    "search"))]
            );
        }

        yield return ClearButton();
    }

    private static PluginComponent Heading(string id, string text) =>
        PluginViews.Text(id, text, "subtitle");

    // A plain button carries nothing but its path, so clearing cannot reuse the form's
    // submit - it has its own endpoint rather than a magic empty value.
    private static PluginComponent ClearButton() =>
        PluginViews.Button(
            "search-clear",
            "Clear search",
            PluginActionIntent.CallPlugin(InternetRadioController.ClearSearchMethod),
            variant: "secondary"
        );
}
