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

    private CatalogProvider Provider(TimeSpan? ttl = null) =>
        new(new RadioBrowserClient(new HttpClient(_handler)),
            new CatalogCache(_folder),
            _folder,
            _logger,
            ttl);

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
        catalog.Stations.Should().NotBeEmpty();
        catalog.Stations[0].Genre.Should().Be("Ambient");
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
        catalog.Stations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DropsStationsThatFailTheGates()
    {
        // http, so it would be blocked as mixed content in the browser.
        _handler.Respond(Payload("u1", "Insecure FM", "http://example.com/a"));

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Stations.Should().NotContain(station => station.Name == "Insecure FM");
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
