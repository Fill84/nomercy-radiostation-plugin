// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using Microsoft.Extensions.Logging;

namespace NoMercy.Plugin.InternetRadio;

// Decides what the views actually see.
//
// The order is override, then fresh cache, then fetch, then stale cache, then empty.
// The last two are the point: a third party's outage must not empty a catalogue that
// was working a minute ago, so a stale cache is preferred to nothing and the failure
// is surfaced on the settings page rather than as a blank browse grid.
public sealed class CatalogProvider(
    RadioBrowserClient client,
    CatalogCache cache,
    string dataFolderPath,
    ILogger logger,
    TimeSpan? cacheTtl = null
)
{
    /// <summary>
    /// How long a cache is served without re-fetching. Longer than the refresh job's
    /// daily cadence, so the job is what normally refreshes and a view only fetches
    /// when the job has not run yet or has been failing.
    /// </summary>
    public static TimeSpan DefaultCacheTtl { get; } = TimeSpan.FromHours(36);

    private TimeSpan Ttl => cacheTtl ?? DefaultCacheTtl;

    public async Task<StationCatalog> GetAsync(CancellationToken ct)
    {
        if (StationOverrides.TryLoad(dataFolderPath, logger) is { } overrides)
        {
            return StationCatalog.Create(overrides, CatalogSource.UserOverride, fetchedAt: null);
        }

        CachedCatalog? cached = await cache.ReadAsync(ct);

        if (cached is not null && DateTimeOffset.UtcNow - cached.FetchedAt < Ttl)
        {
            return StationCatalog.Create(cached.Stations, CatalogSource.Cache, cached.FetchedAt);
        }

        return await FetchAsync(cached, ct);
    }

    /// <summary>
    /// Fetches whatever the cache says. This is what the scheduled job calls, and
    /// what the settings page's Refresh reaches once the cache has aged out.
    /// </summary>
    public async Task<StationCatalog> RefreshAsync(CancellationToken ct) =>
        await FetchAsync(await cache.ReadAsync(ct), ct);

    private async Task<StationCatalog> FetchAsync(CachedCatalog? fallback, CancellationToken ct)
    {
        List<RadioStation> collected = [];
        bool anythingFailed = false;

        // Seeds first, so a curated station wins the dedupe against the same station
        // rediscovered by the genre sweep.
        try
        {
            collected.AddRange(Convert(await client.GetByUuidsAsync(SeedStations.Uuids, ct)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            anythingFailed = true;
            logger.LogWarning(exception, "Internet Radio could not fetch its pinned stations.");
        }

        foreach (GenreSection section in GenreMap.Sections)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                collected.AddRange(
                    Convert(await client.SearchByTagAsync(section.Tag, SeedStations.PerGenreLimit, ct)));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // One genre failing costs one genre. Letting it abort the sweep would
                // throw away the seeds and sixteen other sections with it.
                anythingFailed = true;
                logger.LogWarning(
                    exception, "Internet Radio could not fetch the {Genre} stations.", section.Label);
            }
        }

        IReadOnlyList<RadioStation> stations = StationGates.Deduplicate(collected);

        if (stations.Count > 0)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            try
            {
                await cache.WriteAsync(stations, now, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A read-only or full data folder costs the cache, not the screen.
                logger.LogWarning(exception, "Internet Radio could not write its catalogue cache.");
            }

            return StationCatalog.Create(stations, CatalogSource.Fetched, now);
        }

        // Nothing came back. Anything already on disk is better than an empty grid,
        // however old it is.
        if (fallback is not null && fallback.Stations.Count > 0)
        {
            logger.LogWarning(
                "Internet Radio kept its cached catalogue from {FetchedAt} because the refresh returned nothing.",
                fallback.FetchedAt);

            return StationCatalog.Create(fallback.Stations, CatalogSource.Cache, fallback.FetchedAt)
                .WithFailedFetch();
        }

        logger.LogWarning("Internet Radio has no stations: the refresh failed and there is no cache.");
        return StationCatalog.Empty(lastFetchFailed: anythingFailed);
    }

    private static IEnumerable<RadioStation> Convert(IEnumerable<RadioBrowserStation> wire) =>
        wire.Where(StationGates.Admits).Select(station => new RadioStation
        {
            Id = station.StationUuid,
            Name = station.Name.Trim(),
            StreamUrl = StationGates.EffectiveUrl(station),
            LogoUrl = string.IsNullOrWhiteSpace(station.Favicon) ? null : station.Favicon,
            Homepage = string.IsNullOrWhiteSpace(station.Homepage) ? null : station.Homepage,
            Genre = GenreMap.Resolve(station.Tags),
            Country = string.IsNullOrWhiteSpace(station.CountryCode) ? null : station.CountryCode,
            Language = string.IsNullOrWhiteSpace(station.Language) ? null : station.Language,
            // radio-browser reports 0 for "unknown", which is not the same as a
            // zero-bitrate stream and must not render as "0 kbps".
            BitrateKbps = station.Bitrate > 0 ? station.Bitrate : null,
            Codec = string.IsNullOrWhiteSpace(station.Codec) ? null : station.Codec,
            Popularity = station.Votes,
        });
}
