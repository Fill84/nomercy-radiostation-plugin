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
    TimeSpan? cacheTtl = null,
    TimeSpan? fetchBudget = null
)
{
    /// <summary>
    /// How long a cache is served without re-fetching. Longer than the refresh job's
    /// daily cadence, so the job is what normally refreshes and a view only fetches
    /// when the job has not run yet or has been failing.
    /// </summary>
    public static TimeSpan DefaultCacheTtl { get; } = TimeSpan.FromHours(36);

    /// <summary>
    /// The overall wall-clock budget for one sweep (one POST plus seventeen GETs).
    /// A cold-start view runs this inline on the request thread with no other
    /// deadline, so an unbounded sweep against a hanging mirror is an unbounded
    /// request. ~20s is generous for a healthy radio-browser mirror and still short
    /// enough that a request thread does not hang for the request's own lifetime.
    /// </summary>
    public static TimeSpan DefaultFetchBudget { get; } = TimeSpan.FromSeconds(20);

    private TimeSpan Ttl => cacheTtl ?? DefaultCacheTtl;
    private TimeSpan FetchBudget => fetchBudget ?? DefaultFetchBudget;

    // Single-flight for the sweep itself: a cold cache with several concurrent view
    // requests must start ONE sweep, not one per request. Guards only the shared
    // in-flight Task reference, never held across an await, so it is safe to take
    // inside an async method.
    private readonly Lock _fetchGate = new();
    private Task<StationCatalog>? _inFlightFetch;

    public async Task<StationCatalog> GetAsync(CancellationToken ct)
    {
        if (StationOverrides.TryLoad(dataFolderPath, logger) is { } overrides)
        {
            return StationCatalog.Create(overrides, CatalogSource.UserOverride, fetchedAt: null);
        }

        CachedCatalog? cached = await cache.ReadAsync(ct, logger);

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
        await FetchAsync(await cache.ReadAsync(ct, logger), ct);

    /// <summary>
    /// Joins an in-flight sweep if one is already running, rather than starting a
    /// second. The caller whose token started the sweep governs its cancellation;
    /// a caller that only joined observes the same outcome, including that
    /// caller's own cancellation - an accepted tradeoff of sharing one Task rather
    /// than threading every joiner's token into the sweep individually.
    /// </summary>
    private Task<StationCatalog> FetchAsync(CachedCatalog? fallback, CancellationToken ct)
    {
        lock (_fetchGate)
        {
            if (_inFlightFetch is { IsCompleted: false } inFlight)
            {
                return inFlight;
            }

            return _inFlightFetch = RunFetchAsync(fallback, ct);
        }
    }

    private async Task<StationCatalog> RunFetchAsync(CachedCatalog? fallback, CancellationToken ct)
    {
        try
        {
            return await SweepAsync(fallback, ct);
        }
        finally
        {
            // Safe unconditionally: nobody else can have replaced _inFlightFetch
            // while this method's own Task was still incomplete, since a new caller
            // only starts a fresh sweep when the shared one has already completed -
            // see FetchAsync above.
            lock (_fetchGate)
            {
                _inFlightFetch = null;
            }
        }
    }

    private async Task<StationCatalog> SweepAsync(CachedCatalog? fallback, CancellationToken ct)
    {
        // Bounds the whole sweep, not each request: seventeen genre queries each
        // allowed the full budget would still let a hanging mirror keep a cold-start
        // view open for minutes. Linked into the caller's own token so a genuine
        // caller cancellation still wins immediately, and the per-request catches
        // below already distinguish the two - see their comments.
        using CancellationTokenSource budgetCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budgetCts.CancelAfter(FetchBudget);
        CancellationToken fetchCt = budgetCts.Token;

        List<RadioStation> collected = [];
        bool anythingFailed = false;

        // Seeds first, so a curated station wins the dedupe against the same station
        // rediscovered by the genre sweep.
        try
        {
            collected.AddRange(Convert(await client.GetByUuidsAsync(SeedStations.Uuids, fetchCt)));
        }
        // An HttpClient timeout raises TaskCanceledException, which derives from
        // OperationCanceledException but is NOT a caller cancellation - it carries
        // its own internal token, distinct from ct, and ct.IsCancellationRequested
        // is false. The same is true when the budget above is what fired: fetchCt is
        // cancelled but ct is not. Rethrowing unconditionally would let a hanging
        // mirror (the commonest shape of an outage, well within HttpClient's 100s
        // default timeout) escape GetAsync entirely and skip the stale-cache
        // fallback below - worse than the empty grid this design exists to avoid.
        // Only rethrow when it is genuinely this call's own token that fired.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
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
                    Convert(await client.SearchByTagAsync(section.Tag, SeedStations.PerGenreLimit, fetchCt)));
            }
            // See the identical guard on the seed fetch above: a per-request timeout
            // - or the overall budget expiring - must be treated as this genre's
            // failure, not as this call's own cancellation.
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // One genre failing costs one genre. Letting it abort the sweep would
                // throw away the seeds and sixteen other sections with it. Once the
                // budget has expired every remaining request fails the same way,
                // fast (the token is already cancelled), so this still terminates
                // promptly rather than hanging - and the settings page still gets an
                // honest LastFetchFailed rather than a page that simply hung.
                anythingFailed = true;
                logger.LogWarning(
                    exception, "Internet Radio could not fetch the {Genre} stations.", section.Label);
            }
        }

        IReadOnlyList<RadioStation> stations = StationGates.Deduplicate(collected);

        if (stations.Count > 0)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            // A degraded sweep (some but not all requests failed) must never
            // overwrite a good cache's STATIONS with a smaller result: 70 good
            // stations must not be replaced by 10. The fallback's stations win
            // instead, marked as having survived a failed refresh.
            //
            // But the fallback still has to be rewritten with a fresh fetchedAt,
            // not left untouched: skipping the write entirely never resets the
            // TTL, and GetAsync re-enters this same 18-request sweep on every
            // single view once the cache is past its 36-hour TTL - unbounded
            // repeated load on a volunteer-run service, and worse than the
            // once-a-day degraded catalogue this design replaced. Renewing
            // fetchedAt costs one sweep per TTL window, the same as a healthy
            // refresh, while losing no station the fallback had.
            //
            // Deliberately NOT gated on stations.Count < fallback.Stations.Count:
            // count is a poor proxy for quality - a sweep can legitimately return
            // fewer stations - and comparing counts would reintroduce the clobber
            // whenever a degraded result happened to be larger.
            if (anythingFailed && fallback is { Stations.Count: > 0 })
            {
                logger.LogWarning(
                    "Internet Radio kept the {CachedCount} stations already in its cache instead of "
                        + "a degraded refresh that only returned {FetchedCount}, and renewed the "
                        + "cache's freshness so the next view is served from it rather than "
                        + "re-sweeping.",
                    fallback.Stations.Count,
                    stations.Count);

                try
                {
                    await cache.WriteAsync(fallback.Stations, now, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // A read-only or full data folder costs the cache, not the
                    // screen - same tolerance as the plain-write path below. This
                    // caller's in-memory result is unaffected either way, though a
                    // write failure here does mean the next view re-sweeps.
                    logger.LogWarning(exception, "Internet Radio could not write its catalogue cache.");
                }

                return StationCatalog.Create(fallback.Stations, CatalogSource.Cache, now)
                    .WithFailedFetch();
            }

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

            // Some stations came back, but if the seed fetch or a genre query failed
            // along the way this is a degraded result, not a clean one - the settings
            // page has to say so.
            StationCatalog fetched = StationCatalog.Create(stations, CatalogSource.Fetched, now);
            return anythingFailed ? fetched.WithFailedFetch() : fetched;
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

        // No cache to fall back on either. Distinguish an outage from a legitimately
        // empty result - every request succeeding but admitting nothing (every row
        // gate-rejected, or a mirror answering `200 []`) is not the same problem as
        // the refresh actually failing, and the settings page must not claim an
        // outage that did not happen.
        if (anythingFailed)
        {
            logger.LogWarning("Internet Radio has no stations: the refresh failed and there is no cache.");
        }
        else
        {
            logger.LogWarning(
                "Internet Radio has no stations: every request succeeded but returned nothing admissible, "
                    + "and there is no cache.");
        }

        return StationCatalog.Empty(lastFetchFailed: anythingFailed);
    }

    private static IEnumerable<RadioStation> Convert(IEnumerable<RadioBrowserStation> wire)
    {
        foreach (RadioBrowserStation station in wire)
        {
            if (!StationGates.Admits(station))
            {
                continue;
            }

            // Admits has already rejected a station with no stationuuid or name, but
            // that gate runs against a different type and the compiler cannot carry
            // that guarantee across the call - these patterns narrow the two wire
            // fields RadioStation requires non-null, rather than asserting it with `!`.
            if (station.StationUuid is not { } uuid || station.Name is not { } name)
            {
                continue;
            }

            yield return new RadioStation
            {
                Id = uuid,
                Name = name.Trim(),
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
            };
        }
    }
}
