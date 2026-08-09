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

    // One name per axis, read by the form here and by SearchRequest on the way
    // back in. A field whose name the controller does not bind arrives as null and
    // silently narrows nothing, which looks exactly like a filter that matched.
    public const string GenreFieldName = "genre";
    public const string CountryFieldName = "country";
    public const string LanguageFieldName = "language";

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

            Field(term, state),
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
    public static PluginComponent Field(string term) => Field(term, UserState.Empty);

    /// <summary>
    /// The whole form, carrying whatever this viewer last asked for.
    ///
    /// Four fields rather than one box: the database is indexed on all of them and
    /// combines them, and a name-only search means a listener can only find a
    /// station they could already name. Nobody can name a station in a database
    /// this size - they know they want ambient, or something Japanese.
    ///
    /// Every field is optional and blank means "not filtered", so the form reads as
    /// four ways to narrow rather than four things to fill in.
    /// </summary>
    public static PluginComponent Field(string term, UserState state) =>
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
                Placeholder = "Any name",
            },
            new PluginFormField
            {
                Name = GenreFieldName,
                Label = "Genre",
                Type = PluginFormFieldType.Text,
                Value = state.LastGenre ?? string.Empty,
                Placeholder = "ambient, jazz, anime",
            },
            new PluginFormField
            {
                Name = CountryFieldName,
                Label = "Country",
                Type = PluginFormFieldType.Text,
                Value = state.LastCountry ?? string.Empty,
                Placeholder = "Japan",
            },
            new PluginFormField
            {
                Name = LanguageFieldName,
                Label = "Language",
                Type = PluginFormFieldType.Text,
                Value = state.LastLanguage ?? string.Empty,
                Placeholder = "japanese",
            });

    /// <summary>
    /// What a search turned up, or why it turned up nothing.
    ///
    /// Three ways to have no results and they must not look alike: nothing typed yet,
    /// unreachable, and nothing matched. Reporting an outage as an empty result set has the
    /// viewer retyping a search that was never the problem.
    /// </summary>
    public static IEnumerable<PluginComponent> Results(
        string term, IReadOnlyList<RadioStation> results, bool queryFailed, UserState state)
    {
        // Whether anything was asked for, not whether a name was typed. Keying on
        // the name alone meant a search by genre or country - which needs no name
        // at all - rendered as "nothing typed yet" while the stations it found sat
        // in the argument list unused.
        bool asked = term.Length > 0
            || queryFailed
            || results.Count > 0
            || !string.IsNullOrWhiteSpace(state.LastGenre)
            || !string.IsNullOrWhiteSpace(state.LastCountry)
            || !string.IsNullOrWhiteSpace(state.LastLanguage);

        if (!asked)
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
                term.Length > 0
                    ? $"No playable station matches “{term}”."
                    : "No playable station matches those filters.");

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
