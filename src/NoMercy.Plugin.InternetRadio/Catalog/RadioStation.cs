// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json.Serialization;

namespace NoMercy.Plugin.InternetRadio;

// One station as this plugin uses it, after the wire record has been through the
// gates. Separate from RadioBrowserStation so the views never see a field they
// must not render and never depend on a third party's field names.
public sealed record RadioStation
{
    /// <summary>
    /// Stable and URL-safe: it is a path segment in /station/{id}. A radio-browser
    /// UUID for a fetched station, a slug of the name for a user-supplied one.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("streamUrl")]
    public required string StreamUrl { get; init; }

    [JsonPropertyName("logoUrl")]
    public string? LogoUrl { get; init; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }

    /// <summary>The mapped section label, not the raw tag list. See GenreMap.</summary>
    [JsonPropertyName("genre")]
    public string? Genre { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    /// <summary>Null when radio-browser reports 0, which means "unknown", not "silent".</summary>
    [JsonPropertyName("bitrateKbps")]
    public int? BitrateKbps { get; init; }

    [JsonPropertyName("codec")]
    public string? Codec { get; init; }

    /// <summary>radio-browser votes. Ordering only - never shown.</summary>
    [JsonPropertyName("popularity")]
    public int Popularity { get; init; }

    /// <summary>True for a station from the user's stations.json. Shown as provenance.</summary>
    [JsonPropertyName("isUserSupplied")]
    public bool IsUserSupplied { get; init; }
}
