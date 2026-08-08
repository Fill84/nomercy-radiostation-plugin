// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// A search box, a button, and the results underneath. What it should have been all along.
//
// It did not work before for one reason, and it was not a missing feature: the component
// name. PluginComponentType.Form is "NMCard", which the client resolves as a design-system
// card, so the real PluginForm - which renders a <form>, collects its fields and posts them
// - was never reached, and every submit arrived as an empty body. Named correctly, the
// client posts {"query": "..."} to this plugin's endpoint and then refreshes the view. See
// Ui for the full account.
//
// The term is also still a route. /search/tomorrowland renders the same page, so a search
// stays shareable and bookmarkable, and typing is not the only way in.
public static class SearchView
{
    public const string FieldName = "query";

    public static PluginView Build(
        string term,
        IReadOnlyList<RadioStation> results,
        bool queryFailed,
        UserState state)
    {
        List<PluginComponent> children =
        [
            Ui.Button(
                "search-back",
                "Back",
                PluginActionIntent.Navigate(RadioRoutes.Browse),
                icon: "arrowLeft"),

            Field(term),
        ];

        children.AddRange(Results(term, results, queryFailed, state));

        return PluginViews.Declarative(Ui.Container("search-root", [.. children]));
    }

    /// <summary>
    /// The box you type into.
    ///
    /// Carries whatever is being searched for, so arriving on a search does not read as one
    /// that was thrown away, and correcting a typo means editing rather than retyping.
    /// </summary>
    public static PluginComponent Field(string term) =>
        Ui.Form(
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
            });

    /// <summary>
    /// What a search turned up, or why it turned up nothing.
    ///
    /// Three ways to have no results and they must not look alike: nothing typed yet,
    /// unreachable, and nothing matched. Reporting an outage as an empty result set has the
    /// viewer retyping a search that was never the problem.
    /// </summary>
    private static IEnumerable<PluginComponent> Results(
        string term, IReadOnlyList<RadioStation> results, bool queryFailed, UserState state)
    {
        if (term.Length == 0)
        {
            yield break;
        }

        if (queryFailed)
        {
            yield return Ui.EmptyState(
                "search-failed",
                "Search is unavailable",
                "radio-browser did not answer. Try again in a moment.");

            yield break;
        }

        if (results.Count == 0)
        {
            yield return Ui.EmptyState(
                "search-empty",
                "Nothing found",
                $"No playable station matches “{term}”.");

            yield break;
        }

        yield return Ui.Text(
            "search-results-heading",
            results.Count == 1 ? "1 station" : $"{results.Count} stations",
            "subtitle");

        yield return StationCards.Grid("search-grid", results, state, "search");

        yield return Ui.Button(
            "search-clear",
            "Clear search",
            PluginActionIntent.Navigate(RadioRoutes.SearchRoot),
            variant: "secondary");
    }
}
