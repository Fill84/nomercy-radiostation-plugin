// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>What a previous fetch wrote to disk.</summary>
public sealed record CachedCatalog
{
    [JsonPropertyName("fetchedAt")]
    public required DateTimeOffset FetchedAt { get; init; }

    [JsonPropertyName("stations")]
    public required List<RadioStation> Stations { get; init; }
}

// The only thing in this plugin that touches the data folder.
//
// Every read failure is null, never an exception: the cache is a convenience, and a
// truncated file - which is what a server killed mid-write leaves - has to mean
// "fetch again", not "the settings page throws".
public sealed class CatalogCache(string dataFolderPath)
{
    public const string FileName = "catalog-cache.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    private string Path => System.IO.Path.Combine(dataFolderPath, FileName);

    public async Task<CachedCatalog?> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        try
        {
            await using FileStream stream = File.OpenRead(Path);
            return await JsonSerializer.DeserializeAsync<CachedCatalog>(stream, JsonOptions, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Corrupt, truncated, or unreadable. Indistinguishable from absent as far
            // as the caller is concerned, and treating it that way is what makes the
            // next refresh fix it. The caller logs; this stays quiet so a cache miss
            // does not need an ILogger threaded into it.
            return null;
        }
    }

    public async Task WriteAsync(
        IReadOnlyList<RadioStation> stations,
        DateTimeOffset fetchedAt,
        CancellationToken ct
    )
    {
        Directory.CreateDirectory(dataFolderPath);

        // Written beside the target and moved into place. A crash partway through a
        // direct write would replace a whole cache with half of one, and the next
        // read would discard it - losing a good catalogue to a bad write.
        string temporary = $"{Path}.tmp";

        await using (FileStream stream = File.Create(temporary))
        {
            CachedCatalog payload = new() { FetchedAt = fetchedAt, Stations = [.. stations] };
            await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, ct);
        }

        File.Move(temporary, Path, overwrite: true);
    }
}
