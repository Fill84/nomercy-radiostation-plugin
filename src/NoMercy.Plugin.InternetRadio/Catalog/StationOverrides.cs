// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace NoMercy.Plugin.InternetRadio;

// The user's own station list, which replaces the fetched catalogue outright.
//
// Kept compatible with the bare JSON array the previous README documented, so an
// existing stations.json keeps working across this rewrite.
//
// Deliberately NOT put through StationGates. A hand-written list is the owner's
// call, and silently dropping their http entry would be worse than letting it fail
// visibly in the player - at least then they can see which one it was. This is also
// the escape hatch for anything radio-browser cannot supply, BBC included.
public static class StationOverrides
{
    public const string FileName = "stations.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

    /// <summary>
    /// The override list, or null when there is no usable one — in which case the
    /// caller fetches as normal.
    /// </summary>
    public static IReadOnlyList<RadioStation>? TryLoad(string dataFolderPath, ILogger logger)
    {
        string path = Path.Combine(dataFolderPath, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            // Deserialised into a DTO with nothing required, not straight into
            // RadioStation: System.Text.Json enforces a `required` member before
            // this method ever gets a chance to fill Id in below, so an entry that
            // - as intended - omits id would otherwise throw here instead of
            // falling through to the Id-generation this method exists to do.
            List<OverrideEntry>? parsed =
                JsonSerializer.Deserialize<List<OverrideEntry>>(File.ReadAllText(path), JsonOptions);

            if (parsed is null)
            {
                return null;
            }

            List<RadioStation> valid =
            [
                .. parsed
                    .Where(entry =>
                        !string.IsNullOrWhiteSpace(entry.Name)
                        && !string.IsNullOrWhiteSpace(entry.StreamUrl))
                    .Select(entry => new RadioStation
                    {
                        // Their file need not carry an id, and a name is the only
                        // stable thing it is guaranteed to have.
                        Id = string.IsNullOrWhiteSpace(entry.Id)
                            ? StationGates.Slugify(entry.Name!)
                            : entry.Id,
                        Name = entry.Name!,
                        StreamUrl = entry.StreamUrl!,
                        LogoUrl = entry.LogoUrl,
                        Homepage = entry.Homepage,
                        Genre = string.IsNullOrWhiteSpace(entry.Genre) ? GenreMap.Other : entry.Genre,
                        Country = entry.Country,
                        Language = entry.Language,
                        BitrateKbps = entry.BitrateKbps,
                        Codec = entry.Codec,
                        Popularity = entry.Popularity ?? 0,
                        IsUserSupplied = true,
                    }),
            ];

            return valid.Count > 0 ? valid : null;
        }
        catch (Exception exception)
        {
            // Named so the owner can find their typo, without echoing the file's
            // contents into the server log.
            logger.LogWarning(
                exception,
                "Internet Radio could not read {FileName}; using the fetched catalogue instead.",
                FileName
            );
            return null;
        }
    }

    // Mirrors RadioStation's wire shape field-for-field, but with nothing required:
    // the whole point of this file is that a hand-written entry only has to carry a
    // name and a stream URL, and JsonSerializer.Deserialize<RadioStation> would
    // refuse the input before that leniency could apply.
    //
    // Internal rather than private: a test asserts this mirror stays in sync with
    // RadioStation's own [JsonPropertyName] set, so a later property added to
    // RadioStation without a matching one here fails a build instead of silently
    // vanishing from every user's stations.json.
    internal sealed record OverrideEntry
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("streamUrl")]
        public string? StreamUrl { get; init; }

        [JsonPropertyName("logoUrl")]
        public string? LogoUrl { get; init; }

        [JsonPropertyName("homepage")]
        public string? Homepage { get; init; }

        [JsonPropertyName("genre")]
        public string? Genre { get; init; }

        [JsonPropertyName("country")]
        public string? Country { get; init; }

        [JsonPropertyName("language")]
        public string? Language { get; init; }

        [JsonPropertyName("bitrateKbps")]
        public int? BitrateKbps { get; init; }

        [JsonPropertyName("codec")]
        public string? Codec { get; init; }

        // Nullable, not int: an explicit "popularity": null in a hand-written file
        // must not throw and discard every other entry in it over one field that is
        // ordering-only and never shown.
        [JsonPropertyName("popularity")]
        public int? Popularity { get; init; }
    }
}
