// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json.Serialization;

namespace NoMercy.Plugin.InternetRadio;

// The wire shape of one radio-browser station record. Only the fields this plugin
// reads are declared; the API returns roughly forty and the rest are ignored.
//
// Every field is nullable or defaulted on purpose. This is a third party's JSON
// arriving over the network, and a record missing a field it has always sent must
// deserialise to something the gates can reject rather than throw during parsing -
// one malformed row would otherwise lose the whole response.
public sealed record RadioBrowserStation
{
    [JsonPropertyName("stationuuid")]
    public required string StationUuid { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("url_resolved")]
    public string? UrlResolved { get; init; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }

    [JsonPropertyName("favicon")]
    public string? Favicon { get; init; }

    [JsonPropertyName("tags")]
    public string? Tags { get; init; }

    [JsonPropertyName("countrycode")]
    public string? CountryCode { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonPropertyName("codec")]
    public string? Codec { get; init; }

    [JsonPropertyName("bitrate")]
    public int Bitrate { get; init; }

    /// <summary>1 when the stream is HLS. Unplayable outside Safari in a plain audio element.</summary>
    [JsonPropertyName("hls")]
    public int Hls { get; init; }

    /// <summary>
    /// radio-browser's own liveness flag. Trusted for discovery and nothing more:
    /// it reported a 404 Tomorrowland Anthems URL as healthy, which is how that
    /// station came to need submitting by hand. Declaration is not verification.
    /// </summary>
    [JsonPropertyName("lastcheckok")]
    public int LastCheckOk { get; init; }

    [JsonPropertyName("votes")]
    public int Votes { get; init; }
}
