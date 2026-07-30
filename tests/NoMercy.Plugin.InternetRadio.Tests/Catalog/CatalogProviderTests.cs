// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public sealed class CatalogProviderTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"nm-radio-{Guid.NewGuid():N}");
    private readonly FakeHttpMessageHandler _handler = new();
    private readonly RecordingLogger _logger = new();

    public CatalogProviderTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private CatalogProvider Provider(TimeSpan? ttl = null, TimeSpan? fetchBudget = null) =>
        new(new RadioBrowserClient(new HttpClient(_handler)),
            new CatalogCache(_folder),
            _folder,
            _logger,
            ttl,
            fetchBudget);

    private static string Payload(string uuid, string name, string url, string tags = "ambient") =>
        $$"""
        [{"stationuuid":"{{uuid}}","name":"{{name}}","url":"{{url}}","url_resolved":"{{url}}",
          "tags":"{{tags}}","countrycode":"NL","codec":"MP3","bitrate":128,
          "hls":0,"lastcheckok":1,"votes":5}]
        """;

    [Fact]
    public async Task Fetches_WhenThereIsNoCache()
    {
        _handler.Respond(Payload("u1", "Example FM", "https://example.com/a"));

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Fetched);
        // ContainSingle rather than Stations[0]: this asserts there is exactly one
        // station with the expected genre, not incidentally which index the
        // implementation happened to put it at.
        catalog.Stations.Should().ContainSingle().Which.Genre.Should().Be("Ambient");
    }

    [Fact]
    public async Task WritesTheCacheAfterAFetch()
    {
        _handler.Respond(Payload("u1", "Example FM", "https://example.com/a"));

        await Provider().GetAsync(CancellationToken.None);

        File.Exists(Path.Combine(_folder, CatalogCache.FileName)).Should().BeTrue();
    }

    // A view is rendered on every navigation. Hitting the API per click would be
    // roughly eighteen requests a page.
    [Fact]
    public async Task ServesAFreshCacheWithoutTouchingTheNetwork()
    {
        _handler.Respond(Payload("u1", "Example FM", "https://example.com/a"));
        CatalogProvider provider = Provider();
        await provider.GetAsync(CancellationToken.None);
        int afterFirst = _handler.Requests.Count;

        StationCatalog catalog = await provider.GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Cache);
        _handler.Requests.Should().HaveCount(afterFirst);
    }

    [Fact]
    public async Task RefetchesWhenTheCacheIsOlderThanTheTtl()
    {
        _handler.Respond(Payload("u1", "Example FM", "https://example.com/a"));
        await new CatalogCache(_folder).WriteAsync(
            [new RadioStation { Id = "old", Name = "Old FM", StreamUrl = "https://example.com/old" }],
            DateTimeOffset.UtcNow - TimeSpan.FromDays(30),
            CancellationToken.None);

        StationCatalog catalog = await Provider(TimeSpan.FromHours(1)).GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Fetched);
    }

    // The one that matters when radio-browser is down. A working catalogue must not
    // be thrown away because a refresh failed.
    [Fact]
    public async Task ServesAStaleCacheWhenTheFetchFails()
    {
        await new CatalogCache(_folder).WriteAsync(
            [new RadioStation { Id = "old", Name = "Old FM", StreamUrl = "https://example.com/old" }],
            DateTimeOffset.UtcNow - TimeSpan.FromDays(30),
            CancellationToken.None);
        _handler.Fail(new HttpRequestException("down"));

        StationCatalog catalog = await Provider(TimeSpan.FromHours(1)).GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Cache);
        catalog.Stations.Should().ContainSingle().Which.Id.Should().Be("old");
        catalog.LastFetchFailed.Should().BeTrue();
    }

    [Fact]
    public async Task ReturnsEmptyAndFlagsTheFailureWhenThereIsNoCacheAndNoNetwork()
    {
        _handler.Fail(new HttpRequestException("down"));

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.IsEmpty.Should().BeTrue();
        catalog.Source.Should().Be(CatalogSource.Unavailable);
        catalog.LastFetchFailed.Should().BeTrue();
        _logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning);
    }

    // An HttpClient timeout raises TaskCanceledException - which derives from
    // OperationCanceledException - with an UNCANCELLED caller token: ct here is
    // CancellationToken.None throughout, exactly as it would be on a real timeout,
    // distinguishing this from PropagatesCancellation below where the caller's own
    // token is the one that fires. A hanging mirror is the commonest shape of an
    // outage, well inside HttpClient's 100s default timeout, and this must be
    // treated as a fetch failure - not rethrown past the stale-cache fallback.
    [Fact]
    public async Task ServesAStaleCacheWhenTheFetchTimesOut()
    {
        await new CatalogCache(_folder).WriteAsync(
            [new RadioStation { Id = "old", Name = "Old FM", StreamUrl = "https://example.com/old" }],
            DateTimeOffset.UtcNow - TimeSpan.FromDays(30),
            CancellationToken.None);
        _handler.Fail(new TaskCanceledException("timeout", new TimeoutException()));

        StationCatalog catalog = await Provider(TimeSpan.FromHours(1)).GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Cache);
        catalog.Stations.Should().ContainSingle().Which.Id.Should().Be("old");
        catalog.LastFetchFailed.Should().BeTrue();
    }

    [Fact]
    public async Task ReturnsEmptyRatherThanThrowingWhenTheFetchTimesOutAndThereIsNoCache()
    {
        _handler.Fail(new TaskCanceledException("timeout", new TimeoutException()));

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.IsEmpty.Should().BeTrue();
        catalog.Source.Should().Be(CatalogSource.Unavailable);
        catalog.LastFetchFailed.Should().BeTrue();
    }

    // The counterpart to the two tests above: a genuine caller cancellation - the
    // token passed to GetAsync itself firing - must still propagate rather than
    // being swallowed as a fetch failure.
    [Fact]
    public async Task PropagatesAGenuineCallerCancellation()
    {
        _handler.Respond(Payload("u1", "Example FM", "https://example.com/a"));
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await FluentActions
            .Awaiting(() => Provider().GetAsync(cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    // One genre failing must not lose the other sixteen and the seeds with them.
    [Fact]
    public async Task KeepsWhatSucceededWhenOneGenreQueryFails()
    {
        int call = 0;
        _handler.RespondPerRequest(_ =>
        {
            call++;
            return call == 2
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    { Content = new StringContent("boom") }
                : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            Payload($"u{call}", $"Station {call}", $"https://example.com/{call}"),
                            System.Text.Encoding.UTF8,
                            "application/json"),
                    };
        });

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Fetched);
        // NotBeEmpty() alone cannot fail if the sweep aborts on the first genre
        // failure, since the seed request (call 1) already satisfies it - the exact
        // count is what proves the other sixteen sections were not thrown away too.
        // 1 seed + every genre section except the one that 500s, each a distinct
        // station so none collide in Deduplicate.
        catalog.Stations.Should().HaveCount(1 + GenreMap.Sections.Count - 1);
    }

    [Fact]
    public async Task FlagsAFailedFetchWhenSeedsSucceedButEveryGenreQueryFails()
    {
        int call = 0;
        _handler.RespondPerRequest(_ =>
        {
            call++;
            return call == 1
                ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            Payload("u1", "Seed FM", "https://example.com/seed"),
                            System.Text.Encoding.UTF8,
                            "application/json"),
                    }
                : new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    { Content = new StringContent("boom") };
        });

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        // A clean Fetched with LastFetchFailed == false would tell the settings page
        // everything is fine while seventeen of eighteen requests just failed, and
        // this ten-ish-station result would silently overwrite a previously good,
        // much larger cache that the 36-hour TTL then serves with no indicator.
        catalog.Source.Should().Be(CatalogSource.Fetched);
        catalog.LastFetchFailed.Should().BeTrue();
        catalog.Stations.Should().ContainSingle();
    }

    // The scenario the whole-branch review found: an 80-station cache, a tick where
    // sixteen of seventeen genre queries 500, and a ~10-station degraded result that
    // must NOT quietly become the new cache for the next 36 hours.
    [Fact]
    public async Task PreservesAGoodCacheRatherThanOverwritingItWithADegradedFetch()
    {
        RadioStation[] goodCache =
            [.. Enumerable.Range(0, 5).Select(i =>
                new RadioStation
                {
                    Id = $"good-{i}",
                    Name = $"Good FM {i}",
                    StreamUrl = $"https://example.com/good-{i}",
                })];
        DateTimeOffset cachedAt = DateTimeOffset.UtcNow - TimeSpan.FromHours(20);
        await new CatalogCache(_folder).WriteAsync(goodCache, cachedAt, CancellationToken.None);

        int call = 0;
        _handler.RespondPerRequest(_ =>
        {
            call++;
            // Only the seed request (call 1) succeeds; every genre query fails, so
            // the sweep still collects one station - enough to reach the
            // stations.Count > 0 branch - while being unmistakably degraded next to
            // the five-station cache already on disk.
            return call == 1
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        Payload("degraded-1", "Degraded FM", "https://example.com/degraded-1"),
                        System.Text.Encoding.UTF8,
                        "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    { Content = new StringContent("boom") };
        });

        DateTimeOffset before = DateTimeOffset.UtcNow;
        StationCatalog catalog = await Provider(TimeSpan.FromHours(1)).GetAsync(CancellationToken.None);
        DateTimeOffset after = DateTimeOffset.UtcNow;

        // The old cache's STATIONS are what is on screen, marked as having survived
        // a failed refresh - not the one-station degraded result. But its
        // freshness is renewed to now: skipping the write entirely would leave
        // FetchedAt frozen at cachedAt, so the very next view - once again past a
        // now-stale TTL - would re-enter this same 18-request sweep, and every
        // view after that, forever. Renewing FetchedAt is what makes a degraded
        // refresh cost one sweep per TTL window rather than one sweep per view.
        catalog.Source.Should().Be(CatalogSource.Cache);
        catalog.FetchedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        catalog.LastFetchFailed.Should().BeTrue();
        catalog.Stations.Select(station => station.Id).Should().BeEquivalentTo(goodCache.Select(s => s.Id));

        // The cache file on disk carries the same renewed freshness, with the
        // fallback's stations preserved unchanged - not the degraded result, and
        // not the original stale FetchedAt either.
        CachedCatalog? onDisk = await new CatalogCache(_folder).ReadAsync(CancellationToken.None);
        onDisk!.FetchedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        onDisk.FetchedAt.Should().BeAfter(cachedAt);
        onDisk.Stations.Select(station => station.Id).Should().BeEquivalentTo(goodCache.Select(s => s.Id));
    }

    // The actual regression the whole-branch re-review found: skipping the write
    // entirely (the first version of this fix) never reset the TTL, so a second
    // render after a degraded sweep re-entered the same 18-request sweep instead of
    // being served from cache - unbounded repeated load on radio-browser. Pins that
    // a degraded sweep now costs one sweep per TTL window, not one per view.
    [Fact]
    public async Task ServesFromCacheWithoutAnotherSweepAfterADegradedFetchPreservedIt()
    {
        RadioStation[] goodCache =
            [.. Enumerable.Range(0, 5).Select(i =>
                new RadioStation
                {
                    Id = $"good-{i}",
                    Name = $"Good FM {i}",
                    StreamUrl = $"https://example.com/good-{i}",
                })];
        await new CatalogCache(_folder).WriteAsync(
            goodCache, DateTimeOffset.UtcNow - TimeSpan.FromHours(20), CancellationToken.None);

        int call = 0;
        _handler.RespondPerRequest(_ =>
        {
            call++;
            return call == 1
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        Payload("degraded-1", "Degraded FM", "https://example.com/degraded-1"),
                        System.Text.Encoding.UTF8,
                        "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    { Content = new StringContent("boom") };
        });

        CatalogProvider provider = Provider(TimeSpan.FromHours(1));

        StationCatalog first = await provider.GetAsync(CancellationToken.None);
        int requestsAfterDegradedSweep = _handler.Requests.Count;

        StationCatalog second = await provider.GetAsync(CancellationToken.None);

        second.Source.Should().Be(CatalogSource.Cache);
        second.Stations.Select(station => station.Id)
            .Should().BeEquivalentTo(first.Stations.Select(station => station.Id));
        // The regression: without renewing FetchedAt, this second call would find
        // the cache still past its TTL and run the full sweep again.
        _handler.Requests.Should().HaveCount(requestsAfterDegradedSweep);
    }

    // The other half of the same fix: when there is nothing usable to fall back on,
    // a degraded result is still better than nothing, and it still has to reach
    // disk - a degraded fetch must not become "never wrote a cache at all".
    [Fact]
    public async Task WritesADegradedFetchWhenThereIsNoUsableCacheToPreserve()
    {
        int call = 0;
        _handler.RespondPerRequest(_ =>
        {
            call++;
            return call == 1
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        Payload("u1", "Seed FM", "https://example.com/seed"),
                        System.Text.Encoding.UTF8,
                        "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    { Content = new StringContent("boom") };
        });

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Fetched);
        catalog.LastFetchFailed.Should().BeTrue();
        catalog.Stations.Should().ContainSingle();

        CachedCatalog? onDisk = await new CatalogCache(_folder).ReadAsync(CancellationToken.None);
        onDisk.Should().NotBeNull();
        onDisk!.Stations.Should().ContainSingle().Which.Id.Should().Be("u1");
    }

    // Two view requests racing a cold cache must trigger one sweep, not two - the
    // whole-branch review's "four Try Again clicks, ~90 in-flight requests" scenario.
    [Fact]
    public async Task ConcurrentGetAsyncCallsOnAColdCacheProduceOnlyOneSweep()
    {
        _handler.Gate = new TaskCompletionSource<bool>();
        _handler.Respond(Payload("u1", "Example FM", "https://example.com/a"));
        CatalogProvider provider = Provider();

        Task<StationCatalog> first = provider.GetAsync(CancellationToken.None);
        Task<StationCatalog> second = provider.GetAsync(CancellationToken.None);

        // Both calls have reached the network (the seed POST is recorded before the
        // handler blocks on Gate) but neither has been allowed to finish, so this is
        // the moment a second, independent sweep would have started if the calls
        // were not single-flighted.
        _handler.Requests.Should().ContainSingle();

        _handler.Gate.SetResult(true);
        StationCatalog[] results = await Task.WhenAll(first, second);

        results[0].Source.Should().Be(CatalogSource.Fetched);
        results[1].Source.Should().Be(CatalogSource.Fetched);
        // 1 seed request + one per genre section - exactly one sweep's worth,
        // whether or not the second caller happened to join before or after the
        // first genre query went out.
        _handler.Requests.Should().HaveCount(1 + GenreMap.Sections.Count);
    }

    // A hanging mirror must not hold a cold-start view open indefinitely: the sweep
    // has an overall budget, and exhausting it has to fall through to whatever the
    // fetch would otherwise resolve to - here, the stale cache - not hang or throw.
    [Fact]
    public async Task ReturnsTheStaleCacheRatherThanHangingWhenTheSweepExceedsItsBudget()
    {
        await new CatalogCache(_folder).WriteAsync(
            [new RadioStation { Id = "old", Name = "Old FM", StreamUrl = "https://example.com/old" }],
            DateTimeOffset.UtcNow - TimeSpan.FromDays(30),
            CancellationToken.None);
        _handler.Hang();

        // A safety net for the test itself, not part of what is under test: if the
        // budget is not actually enforced this would otherwise hang the test suite
        // rather than failing it.
        using CancellationTokenSource safetyNet = new(TimeSpan.FromSeconds(10));

        StationCatalog catalog = await Provider(TimeSpan.FromHours(1), TimeSpan.FromMilliseconds(50))
            .GetAsync(safetyNet.Token);

        catalog.Source.Should().Be(CatalogSource.Cache);
        catalog.Stations.Should().ContainSingle().Which.Id.Should().Be("old");
        catalog.LastFetchFailed.Should().BeTrue();
    }

    [Fact]
    public async Task DropsStationsThatFailTheGates()
    {
        int call = 0;
        _handler.RespondPerRequest(_ =>
        {
            call++;
            // http, so it would be blocked as mixed content in the browser - mixed in
            // with one good station so this cannot pass by the catalogue simply
            // being empty (which a provider unconditionally returning Empty() would
            // also satisfy).
            string payload = call == 1
                ? Payload("u1", "Good FM", "https://example.com/a")
                : Payload("u2", "Insecure FM", "http://example.com/b");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            };
        });

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Stations.Should().Contain(station => station.Name == "Good FM");
        catalog.Stations.Should().NotContain(station => station.Name == "Insecure FM");
    }

    // "No stations" and "the refresh is broken" must not share one flag: an empty
    // grid because every response legitimately returned nothing (a tag nobody uses,
    // or every row gate-rejected) must not read as an outage on the settings page.
    [Fact]
    public async Task DoesNotFlagAFailedFetchWhenEveryRequestSucceededButNothingWasAdmitted()
    {
        _handler.Respond("[]");

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.IsEmpty.Should().BeTrue();
        catalog.Source.Should().Be(CatalogSource.Unavailable);
        catalog.LastFetchFailed.Should().BeFalse();
    }

    [Fact]
    public async Task UsesTheUserOverrideInsteadOfTheNetwork()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_folder, StationOverrides.FileName),
            """[{"name":"My Station","streamUrl":"https://mine.example/stream"}]""");

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.UserOverride);
        catalog.Stations.Should().ContainSingle().Which.Name.Should().Be("My Station");
        _handler.Requests.Should().BeEmpty();
    }

    // Their file, their call. Gating it would silently delete their entries.
    [Fact]
    public async Task DoesNotGateTheUserOverride()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_folder, StationOverrides.FileName),
            """[{"name":"Plain HTTP","streamUrl":"http://mine.example/stream"}]""");

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Stations.Should().ContainSingle().Which.Name.Should().Be("Plain HTTP");
    }

    [Fact]
    public async Task GivesAnOverrideStationARoutableIdAndMarksItUserSupplied()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_folder, StationOverrides.FileName),
            """[{"name":"My Station!","streamUrl":"https://mine.example/stream"}]""");

        RadioStation station = (await Provider().GetAsync(CancellationToken.None)).Stations.Single();

        station.Id.Should().Be("my-station");
        station.IsUserSupplied.Should().BeTrue();
    }

    // A supplied id is a route segment (/station/{id}), same as a name-derived one.
    // Left verbatim, "my station/1" would produce an unroutable path and a dead
    // detail page - the id has to go through the same Slugify every other
    // id-producing path in this plugin already uses.
    [Fact]
    public async Task SlugifiesASuppliedOverrideIdSoItIsRoutable()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_folder, StationOverrides.FileName),
            """[{"id":"my station/1","name":"My Station","streamUrl":"https://mine.example/stream"}]""");

        RadioStation station = (await Provider().GetAsync(CancellationToken.None)).Stations.Single();

        station.Id.Should().Be("my-station-1");
    }

    // Popularity is ordering-only and never shown, so an explicit null for it must
    // not cost the user their entire file over a warning.
    [Fact]
    public async Task ToleratesAnExplicitNullPopularityInTheOverride()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_folder, StationOverrides.FileName),
            """[{"name":"My Station","streamUrl":"https://mine.example/stream","popularity":null}]""");

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.UserOverride);
        catalog.Stations.Should().ContainSingle().Which.Popularity.Should().Be(0);
    }

    [Fact]
    public async Task FallsBackToTheNetworkWhenTheOverrideIsUnparseable()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, StationOverrides.FileName), "{ not an array");
        _handler.Respond(Payload("u1", "Example FM", "https://example.com/a"));

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Fetched);
        _logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task RefreshAsyncAlwaysFetchesEvenWithAFreshCache()
    {
        _handler.Respond(Payload("u1", "Example FM", "https://example.com/a"));
        CatalogProvider provider = Provider();
        await provider.GetAsync(CancellationToken.None);
        int afterFirst = _handler.Requests.Count;

        StationCatalog catalog = await provider.RefreshAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Fetched);
        _handler.Requests.Count.Should().BeGreaterThan(afterFirst);
    }
}
