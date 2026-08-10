// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json.Serialization;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// One entry in a list radio-browser publishes - a tag, a country, a language.
///
/// All three endpoints answer with the same two fields, so one shape reads all of
/// them rather than three near-identical records that could drift apart.
/// </summary>
public sealed class RadioBrowserFacet
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>How many stations carry it. What the list is ordered by.</summary>
    [JsonPropertyName("stationcount")]
    public int StationCount { get; init; }
}
