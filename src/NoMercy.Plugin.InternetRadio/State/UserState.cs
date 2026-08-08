// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json.Serialization;

namespace NoMercy.Plugin.InternetRadio;

// What the plugin remembers about one viewer.
//
// Favourites hold the whole station record, not an id. A station found by search is by
// definition usually not in the sweep, so an id alone would point at nothing the next
// day: no name, no stream, no cover. A favourite that outlives the sweep it was found
// in is the entire point of having favourites at all.
public sealed record UserState
{
    [JsonPropertyName("favourites")]
    public IReadOnlyList<RadioStation> Favourites { get; init; } = [];

    /// <summary>
    /// The term, never the results.
    ///
    /// A plugin controller answers with data and cannot tell the client where to navigate -
    /// after a callPlugin the client refreshes the route it is already on - so a submitted
    /// term has to survive that refresh somewhere, and this is that somewhere. Re-running
    /// the query when the page renders means what is on screen is what radio-browser says
    /// now, and there is no second copy of the results to go stale.
    ///
    /// /search/&lt;term&gt; does not need this: that term is in the route. This is only for
    /// the term that arrived by submit.
    /// </summary>
    [JsonPropertyName("lastSearch")]
    public string? LastSearch { get; init; }

    public static UserState Empty { get; } = new();
}
