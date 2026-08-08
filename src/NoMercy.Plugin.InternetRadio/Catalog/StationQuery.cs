// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Globalization;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// What a listener is looking for, on the axes radio-browser indexes.
///
/// Every field is optional and they combine: a query naming a tag and a country
/// asks for stations that are both, which is what a listener means when they
/// pick two filters. An empty query asks for the most-voted stations overall,
/// which is the sensible thing to show before anyone has typed anything.
/// </summary>
public sealed record StationQuery
{
    public string? Name { get; init; }

    /// <summary>The genre. radio-browser calls these tags; a listener calls them genres.</summary>
    public string? Tag { get; init; }

    public string? Country { get; init; }
    public string? Language { get; init; }
    public string? Codec { get; init; }

    /// <summary>Lowest acceptable bitrate, in kbps.</summary>
    public int? MinBitrate { get; init; }

    /// <summary>
    /// How the answer is sorted. Votes by default, which is radio-browser's own
    /// idea of what is worth hearing and the only ordering that reads as curated
    /// rather than arbitrary.
    /// </summary>
    public string Order { get; init; } = "votes";

    public bool Descending { get; init; } = true;

    internal string ToQueryString(int limit)
    {
        List<string> parts =
        [
            $"limit={limit.ToString(CultureInfo.InvariantCulture)}",
            $"order={Uri.EscapeDataString(Order)}",
            $"reverse={(Descending ? "true" : "false")}",
            "hidebroken=true",
            "is_https=true",
        ];

        Add(parts, "name", Name);
        Add(parts, "tag", Tag);
        Add(parts, "country", Country);
        Add(parts, "language", Language);
        Add(parts, "codec", Codec);

        if (MinBitrate is { } bitrate and > 0)
        {
            parts.Add($"bitrateMin={bitrate.ToString(CultureInfo.InvariantCulture)}");
        }

        return string.Join('&', parts);
    }

    // A blank field is a filter the listener did not fill in, not a filter for
    // the empty string - sending it matches nothing and the page reads as a
    // database with no stations in it.
    private static void Add(List<string> parts, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{key}={Uri.EscapeDataString(value.Trim())}");
        }
    }
}
