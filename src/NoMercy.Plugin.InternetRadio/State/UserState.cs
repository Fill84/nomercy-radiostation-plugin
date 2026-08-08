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

    // No stored search term. There was one, back when searching was a form submit that had
    // to survive a refresh; the term now lives in the route itself, which is a better place
    // for it - a search is shareable, bookmarkable, and cannot go stale against a state
    // file. A `lastSearch` left over in an existing file is simply ignored.

    public static UserState Empty { get; } = new();
}
