// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public sealed class CatalogCacheTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"nm-radio-{Guid.NewGuid():N}");

    public CatalogCacheTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static RadioStation Station(string id = "a") =>
        new() { Id = id, Name = "Example FM", StreamUrl = "https://example.com/a", Genre = "Ambient" };

    [Fact]
    public async Task RoundTripsWhatItWrote()
    {
        CatalogCache cache = new(_folder);
        DateTimeOffset fetchedAt = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

        await cache.WriteAsync([Station()], fetchedAt, CancellationToken.None);
        CachedCatalog? read = await cache.ReadAsync(CancellationToken.None);

        read.Should().NotBeNull();
        read!.FetchedAt.Should().Be(fetchedAt);
        read.Stations.Should().ContainSingle().Which.Name.Should().Be("Example FM");
    }

    [Fact]
    public async Task ReadsNullWhenThereIsNoCacheYet()
    {
        CatalogCache cache = new(_folder);

        (await cache.ReadAsync(CancellationToken.None)).Should().BeNull();
    }

    // A server killed mid-write leaves truncated JSON. That must read as "no cache"
    // and let the plugin re-fetch, not throw out of a view.
    [Fact]
    public async Task ReadsNullWhenTheCacheIsCorrupt()
    {
        CatalogCache cache = new(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, CatalogCache.FileName), "{ truncated");

        (await cache.ReadAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ReadsNullWhenTheCacheIsJsonNull()
    {
        CatalogCache cache = new(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, CatalogCache.FileName), "null");

        (await cache.ReadAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task WriteCreatesTheDataFolderIfTheHostHasNotYet()
    {
        string missing = Path.Combine(_folder, "nested", "deeper");
        CatalogCache cache = new(missing);

        await cache.WriteAsync([Station()], DateTimeOffset.UtcNow, CancellationToken.None);

        File.Exists(Path.Combine(missing, CatalogCache.FileName)).Should().BeTrue();
    }

    // Written to a temp file and moved into place, so a crash mid-write cannot leave
    // a half-written cache where a whole one used to be.
    [Fact]
    public async Task WriteReplacesAPreviousCacheAtomically()
    {
        CatalogCache cache = new(_folder);
        await cache.WriteAsync([Station("first")], DateTimeOffset.UnixEpoch, CancellationToken.None);
        await cache.WriteAsync([Station("second")], DateTimeOffset.UnixEpoch, CancellationToken.None);

        CachedCatalog? read = await cache.ReadAsync(CancellationToken.None);

        read!.Stations.Should().ContainSingle().Which.Id.Should().Be("second");
        Directory.GetFiles(_folder).Should().ContainSingle("no temp file should be left behind");
    }
}
