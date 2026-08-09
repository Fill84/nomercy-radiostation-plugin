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
        UserState state,
        SearchFacets? facets = null)
    {
        List<PluginComponent> children =
        [
            Ui.Button(
                "search-back",
                "Back",
                PluginActionIntent.Navigate(RadioRoutes.Browse),
                icon: "arrowLeft"),

            Field(term, state, facets ?? new SearchFacets()),
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
        Field(term, state, new SearchFacets());

    /// <summary>
    /// The same form, offering the choices radio-browser actually has.
    ///
    /// A select when the list arrived, a text box when it did not. Typing a genre
    /// against a controlled vocabulary is guessing at spelling - "drum and bass"
    /// rather than "drum &amp; bass", "The Netherlands" rather than "Netherlands" -
    /// and a filter that silently matches nothing reads as an empty database.
    /// </summary>
    public static PluginComponent Field(string term, UserState state, SearchFacets facets) =>
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
            Choice(GenreFieldName, "Genre", state.LastGenre, facets.Genres, "ambient, jazz, anime"),
            Choice(CountryFieldName, "Country", state.LastCountry, facets.Countries, "Japan"),
            Choice(LanguageFieldName, "Language", state.LastLanguage, facets.Languages, "japanese"));

    /// <summary>
    /// One filter, as a list when there is a list and as a box when there is not.
    ///
    /// The blank first entry is what "not filtered" looks like in a select, and it
    /// has to be there: without it the first real choice is pre-selected and every
    /// search silently carries a filter nobody picked.
    /// </summary>
    private static PluginFormField Choice(
        string name,
        string label,
        string? current,
        IReadOnlyList<string> choices,
        string placeholder
    ) =>
        choices.Count == 0
            ? new PluginFormField
            {
                Name = name,
                Label = label,
                Type = PluginFormFieldType.Text,
                Value = current ?? string.Empty,
                Placeholder = placeholder,
            }
            : new PluginFormField
            {
                Name = name,
                Label = label,
                Type = PluginFormFieldType.Select,
                Value = current ?? string.Empty,
                Options =
                [
                    new PluginFormOption { Value = string.Empty, Label = $"Any {label.ToLowerInvariant()}" },
                    .. choices.Select(choice => new PluginFormOption { Value = choice, Label = choice }),
                ],
            };

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
