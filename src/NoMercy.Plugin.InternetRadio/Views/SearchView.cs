// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Design;
using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// Searching, spelled out on screen rather than typed into a field.
//
// A typed field cannot be used at all. PluginComponentType.Form maps to NMCard, so a
// PluginViews.Form renders as a card - the a11y tree shows role=button with the input and
// the submit nested inside it - and there is no form element for anything to collect. Four
// plugin-side shapes were tried and the posted body was "{}" every time, because the term
// never leaves the browser. See docs/upstream/2026-08-08-plugin-form-submits-empty-body.md.
//
// What does arrive is a path segment: the genre chips and the favourite toggle both prove
// it. So the term is spelled a character at a time, each key a navigation to the route that
// has one more character in it. That is not a workaround grafted onto a broken design - it
// is the interaction a remote-controlled ten-foot interface wants anyway, and it makes a
// search bookmarkable and shareable for free, which a field never was.
public static class SearchView
{
    /// <summary>How many keys are drawn per row before it wraps.</summary>
    private const int KeysPerRow = 13;

    /// <summary>
    /// Where the keyboard is drawn: the ten-foot surface, and only there.
    ///
    /// It is the right control for a remote and the wrong one for a machine with a keyboard
    /// attached. `hidden_on` is not merely visual - a component hidden on a surface is not
    /// focusable there either - so this keeps thirty-six keys out of D-pad traversal on the
    /// surfaces that do not want them.
    /// </summary>
    private static readonly List<string> KeyboardHiddenOn =
        [NmSurfaceKind.Web, NmSurfaceKind.Mobile];

    /// <summary>The mirror of <see cref="KeyboardHiddenOn"/>, for what replaces it.</summary>
    private static readonly List<string> TypingHiddenOn = [NmSurfaceKind.Tv];

    public static PluginView Build(
        string term,
        IReadOnlyList<RadioStation> results,
        bool queryFailed,
        UserState state)
    {
        List<PluginComponent> children =
        [
            PluginViews.Button(
                "search-back",
                "Back",
                PluginActionIntent.Navigate(RadioRoutes.Browse),
                icon: "arrowLeft"),

            Spelled(term),
        ];

        children.Add(Typing(term));
        children.AddRange(Keyboard(term));
        children.Add(Controls(term));
        children.AddRange(Results(term, results, queryFailed, state));

        return PluginViews.Declarative(PluginViews.Container("search-root", [.. children]));
    }

    /// <summary>
    /// What has been spelled, always on screen.
    ///
    /// Shown even while empty, and as its own line rather than as a heading over the keys:
    /// tapping a key changes only this text and the results, so a viewer who cannot see
    /// what they have spelled has no way to tell a mistyped search from an empty one.
    /// </summary>
    private static PluginComponent Spelled(string term) =>
        PluginViews.Text(
            "search-spelled",
            term.Length == 0
                ? "Tap the letters to spell a station name."
                : $"Searching for “{term}”",
            "subtitle");

    /// <summary>
    /// What a machine with a real keyboard gets instead of the on-screen one.
    ///
    /// Hidden on tv, where the keys are the better control. NMSearchInput is the design
    /// system's own field rather than a PluginFormField, because a plugin form is an NMCard
    /// and submits nothing at all - see the note at the top of this file.
    ///
    /// Whether THIS one submits anything is the open question, and the action below is how
    /// it gets answered rather than guessed at: it posts to an endpoint that does nothing
    /// but write the body it received to the log. If the typed value is in there, typing
    /// can be wired up properly; if the body is empty again, that is the second independent
    /// confirmation that this client has no channel for input values, and the keys come
    /// back on every surface.
    /// </summary>
    private static PluginComponent Typing(string term) =>
        new()
        {
            Id = "search-input",
            Component = NmComponents.SearchInput,
            Action = PluginActionIntent.CallPlugin(InternetRadioController.SubmitMethod),
            Design = new NMSearchInputProps
            {
                Placeholder = "Type a station name",
                Value = term,
                Box = new NmBox { Width = "full", HiddenOn = TypingHiddenOn },
            },
        };

    private static IEnumerable<PluginComponent> Keyboard(string term)
    {
        // Full at MaxLength: the keys stay on screen but stop adding, because a row of
        // keys that vanishes mid-search is a screen that looks like it crashed.
        bool full = term.Length >= SearchTerms.MaxLength;

        foreach ((char[] keys, int index) in Rows())
        {
            yield return KeyRow($"search-keys-{index}", [.. keys.Select(key => Key(term, key, full))]);
        }
    }

    /// <summary>
    /// A row of keys.
    ///
    /// Built here rather than with PluginViews.Row because the box has to carry
    /// <see cref="KeyboardHiddenOn"/> as well as the layout, and a Design record replaces
    /// the whole box the factory put in the loose bag rather than merging with it - so
    /// naming one field there would silently drop the direction and the wrap.
    /// </summary>
    private static PluginComponent KeyRow(string id, List<PluginComponent> keys) =>
        new()
        {
            Id = id,
            Component = PluginComponentType.Row,
            Design = new NMCardProps
            {
                Box = new NmBox
                {
                    Width = "full",
                    Direction = "row",
                    Wrap = "wrap",
                    Gap = new NmGap { All = "2" },
                    HiddenOn = KeyboardHiddenOn,
                },
            },
            Items = keys,
        };

    private static IEnumerable<(char[] Keys, int Index)> Rows()
    {
        char[] all = [.. SearchTerms.Letters, .. SearchTerms.Digits];

        return all
            .Chunk(KeysPerRow)
            .Select((keys, index) => (keys, index));
    }

    private static PluginComponent Key(string term, char key, bool full) =>
        PluginViews.Button(
            $"search-key-{key}",
            key.ToString().ToUpperInvariant(),
            PluginActionIntent.Navigate(
                RadioRoutes.Search(full ? term : SearchTerms.Append(term, key))),
            variant: "secondary");

    /// <summary>
    /// Space, backspace and clear — the three keys that are not a character.
    ///
    /// All three are absent until there is something to act on. A backspace over an empty
    /// term navigates to the page it is already on, which reads as a dead button.
    /// </summary>
    private static PluginComponent Controls(string term)
    {
        List<PluginComponent> controls = [];

        if (term.Length > 0)
        {
            // Only when the last character is not already a space: Sanitise refuses a
            // doubled space, so the key would otherwise do nothing.
            if (term[^1] != ' ' && term.Length < SearchTerms.MaxLength)
            {
                controls.Add(PluginViews.Button(
                    "search-key-space",
                    "Space",
                    PluginActionIntent.Navigate(RadioRoutes.Search(SearchTerms.Append(term, ' '))),
                    variant: "secondary"));
            }

            controls.Add(PluginViews.Button(
                "search-backspace",
                "Backspace",
                PluginActionIntent.Navigate(RadioRoutes.Search(SearchTerms.Backspace(term))),
                icon: "arrowLeft",
                variant: "secondary"));

            controls.Add(PluginViews.Button(
                "search-clear",
                "Clear",
                PluginActionIntent.Navigate(RadioRoutes.SearchRoot),
                variant: "secondary"));
        }

        // Space and backspace belong to the keys, so they go where the keys go. Clear is
        // useful on every surface, but it is only ever drawn beside them, so it travels
        // with the row rather than being singled out.
        return KeyRow("search-controls", controls);
    }

    /// <summary>
    /// What a search turned up, or why it turned up nothing.
    ///
    /// Four ways to have no results, and they must not look alike: too short to run,
    /// unreachable, nothing matched, and a term nobody has spelled yet. Reporting an outage
    /// as an empty result set has the viewer retyping a search that was never the problem.
    /// </summary>
    private static IEnumerable<PluginComponent> Results(
        string term, IReadOnlyList<RadioStation> results, bool queryFailed, UserState state)
    {
        if (term.Length == 0)
        {
            yield break;
        }

        if (term.Length < SearchTerms.MinLength)
        {
            yield return PluginViews.Text(
                "search-too-short",
                $"Spell at least {SearchTerms.MinLength} characters.",
                "caption");

            yield break;
        }

        if (queryFailed)
        {
            yield return PluginViews.EmptyState(
                "search-failed",
                "Search is unavailable",
                "radio-browser did not answer. Try again in a moment.");

            yield break;
        }

        if (results.Count == 0)
        {
            yield return PluginViews.EmptyState(
                "search-empty",
                "Nothing found",
                $"No playable station matches “{term}”.");

            yield break;
        }

        yield return PluginViews.Text(
            "search-results-heading",
            results.Count == 1 ? "1 station" : $"{results.Count} stations",
            "subtitle");

        yield return StationCards.Grid("search-grid", results, state, "search");
    }
}
