// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;
using NoMercy.Plugin.InternetRadio.Catalog;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public sealed class LogoCacheTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"nm-logos-{Guid.NewGuid():N}");

    public LogoCacheTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static RadioStation Station(string id, string? logo = "https://example.com/logo.png") =>
        new() { Id = id, Name = id, StreamUrl = "https://example.com/s", LogoUrl = logo };

    private const string Data = "data:image/png;base64,iVBORw0KGgo=";

    [Theory]
    [InlineData("https://example.com/l.png", true)]
    [InlineData("http://example.com/l.png", true)]
    [InlineData("/relative/l.png", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyAnAbsoluteWebAddressIsALogoSource(string? logo, bool admitted)
    {
        (LogoCache.SourceOf(Station("a", logo)) is not null).Should().Be(admitted);
    }

    [Fact]
    public void AnUnknownLogoIsAskedForOnceAndDrawsThePlaceholderMeanwhile()
    {
        LogoCache cache = new(_folder);

        cache.DataUriFor(Station("a")).Should().BeNull();
        cache.DataUriFor(Station("a")).Should().BeNull();

        cache.TakePending().Select(station => station.Id).Should().Equal("a");
        cache.TakePending().Should().BeEmpty();
    }

    [Fact]
    public void AStationWithoutALogoIsNeverAskedFor()
    {
        LogoCache cache = new(_folder);

        cache.DataUriFor(Station("a", logo: null)).Should().BeNull();

        cache.TakePending().Should().BeEmpty();
    }

    [Fact]
    public async Task AStoredLogoIsDrawnAndSurvivesARestart()
    {
        await new LogoCache(_folder).StoreAsync(Station("a"), Data, CancellationToken.None);

        LogoCache restarted = new(_folder);

        restarted.DataUriFor(Station("a")).Should().Be(Data);
        restarted.Has(Station("a")).Should().BeTrue();
        restarted.TakePending().Should().BeEmpty();
    }

    // A rejected logo is remembered, or every view would fetch it again.
    [Fact]
    public async Task ARejectedLogoIsRememberedAsNone()
    {
        LogoCache cache = new(_folder);
        await cache.StoreAsync(Station("a"), dataUri: null, CancellationToken.None);

        cache.DataUriFor(Station("a")).Should().BeNull();
        cache.Has(Station("a")).Should().BeTrue();
        cache.TakePending().Should().BeEmpty();
    }

    // A station that moved its logo gets the new one, not the old one forever.
    [Fact]
    public async Task ANewLogoAddressIsANewLogo()
    {
        LogoCache cache = new(_folder);
        await cache.StoreAsync(Station("a"), Data, CancellationToken.None);

        cache.DataUriFor(Station("a", "https://example.com/moved.png")).Should().BeNull();
        cache.TakePending().Select(station => station.Id).Should().Equal("a");
    }

    [Fact]
    public async Task TheLeastRecentlyUsedLogoIsEvictedPastTheCapacity()
    {
        LogoCache cache = new(_folder, capacity: 2);
        await cache.StoreAsync(Station("a"), Data, CancellationToken.None);
        await cache.StoreAsync(Station("b"), Data, CancellationToken.None);
        cache.DataUriFor(Station("a"));

        await cache.StoreAsync(Station("c"), Data, CancellationToken.None);

        cache.Has(Station("a")).Should().BeTrue();
        cache.Has(Station("b")).Should().BeFalse();
        cache.Has(Station("c")).Should().BeTrue();
        new LogoCache(_folder, capacity: 2).Has(Station("b")).Should().BeFalse();
    }

    // A cold entry (loaded from the directory listing at startup, not yet read) whose
    // file disappeared out from under it - another process, a manual cleanup, disk
    // trouble - must not surface as an exception through a view. It is simply unknown
    // again, exactly as if it had never been fetched.
    [Fact]
    public async Task ALogoWhoseFileVanishedIsAskedForAgainInsteadOfThrowing()
    {
        await new LogoCache(_folder).StoreAsync(Station("a"), Data, CancellationToken.None);

        LogoCache cache = new(_folder);

        string logoFile = Directory.GetFiles(Path.Combine(_folder, "logos"), "*.txt").Single();
        File.Delete(logoFile);

        cache.DataUriFor(Station("a")).Should().BeNull();
        cache.TakePending().Select(station => station.Id).Should().Equal("a");
    }
}
