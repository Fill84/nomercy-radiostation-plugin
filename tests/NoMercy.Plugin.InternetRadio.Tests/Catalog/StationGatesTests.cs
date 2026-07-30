// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public class StationGatesTests
{
    private static RadioBrowserStation Wire(
        string url = "https://example.com/stream.mp3",
        int hls = 0,
        int lastCheckOk = 1,
        string name = "Example FM"
    ) =>
        new()
        {
            StationUuid = "11111111-2222-3333-4444-555555555555",
            Name = name,
            Url = url,
            UrlResolved = url,
            Hls = hls,
            LastCheckOk = lastCheckOk,
        };

    // The gate that matters most. The web client is served over HTTPS, so an http
    // stream is blocked as mixed content and never reaches the audio element. This
    // is not hypothetical - it is why the BBC entries this plugin used to ship could
    // never play.
    [Fact]
    public void Admits_RejectsPlainHttp()
    {
        StationGates.Admits(Wire(url: "http://example.com/stream.mp3")).Should().BeFalse();
    }

    [Fact]
    public void Admits_AcceptsHttps()
    {
        StationGates.Admits(Wire(url: "https://example.com/stream.mp3")).Should().BeTrue();
    }

    // HLS in a plain audio element only works in Safari, so an m3u8 is silence on
    // every other client.
    [Fact]
    public void Admits_RejectsHls()
    {
        StationGates.Admits(Wire(hls: 1)).Should().BeFalse();
    }

    [Fact]
    public void Admits_RejectsWhatRadioBrowserCouldNotCheck()
    {
        StationGates.Admits(Wire(lastCheckOk: 0)).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Admits_RejectsAMissingName(string? name)
    {
        StationGates.Admits(Wire(name: name!)).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/stream")]
    public void Admits_RejectsAnUnusableUrl(string url)
    {
        StationGates.Admits(Wire(url: url)).Should().BeFalse();
    }

    // url_resolved is what radio-browser followed redirects to and is the better
    // answer; url is the fallback for a record that has not been resolved yet.
    [Fact]
    public void EffectiveUrl_PrefersTheResolvedUrl()
    {
        RadioBrowserStation station = new()
        {
            StationUuid = "a",
            Name = "n",
            Url = "https://example.com/original",
            UrlResolved = "https://cdn.example.com/resolved",
        };

        StationGates.EffectiveUrl(station).Should().Be("https://cdn.example.com/resolved");
    }

    [Fact]
    public void EffectiveUrl_FallsBackToUrlWhenNothingWasResolved()
    {
        RadioBrowserStation station = new()
        {
            StationUuid = "a",
            Name = "n",
            Url = "https://example.com/original",
            UrlResolved = null,
        };

        StationGates.EffectiveUrl(station).Should().Be("https://example.com/original");
    }

    private static RadioStation Station(string id, string name, string url) =>
        new() { Id = id, Name = name, StreamUrl = url };

    // The seed set and the genre sweep overlap by design - a curated station is
    // usually also popular in its genre - so the same station arrives twice.
    [Fact]
    public void Deduplicate_DropsTheSameStreamTwice()
    {
        IReadOnlyList<RadioStation> result = StationGates.Deduplicate(
        [
            Station("a", "First", "https://example.com/stream"),
            Station("b", "Second", "https://example.com/stream"),
        ]);

        result.Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public void Deduplicate_TreatsATrailingSlashAndCasingAsTheSameStream()
    {
        IReadOnlyList<RadioStation> result = StationGates.Deduplicate(
        [
            Station("a", "First", "https://Example.com/Stream/"),
            Station("b", "Second", "https://example.com/Stream"),
        ]);

        result.Should().ContainSingle();
    }

    // Same station, different mirror host. Names collide even when URLs do not, and
    // two identical rows in the grid look like a bug to the user.
    [Fact]
    public void Deduplicate_DropsTheSameNameOnADifferentMirror()
    {
        IReadOnlyList<RadioStation> result = StationGates.Deduplicate(
        [
            Station("a", "SomaFM Groove Salad", "https://ice1.example.com/gs"),
            Station("b", "somafm  groove-salad!", "https://ice5.example.com/gs"),
        ]);

        result.Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public void Deduplicate_KeepsGenuinelyDifferentStations()
    {
        IReadOnlyList<RadioStation> result = StationGates.Deduplicate(
        [
            Station("a", "First", "https://example.com/one"),
            Station("b", "Second", "https://example.com/two"),
        ]);

        result.Should().HaveCount(2);
    }

    // First wins, so a seed keeps its place when the genre sweep finds it again.
    [Fact]
    public void Deduplicate_KeepsTheFirstOccurrence()
    {
        IReadOnlyList<RadioStation> result = StationGates.Deduplicate(
        [
            Station("seed", "Station", "https://example.com/s"),
            Station("discovered", "Station", "https://example.com/s"),
        ]);

        result.Should().ContainSingle().Which.Id.Should().Be("seed");
    }

    [Theory]
    [InlineData("SomaFM - Groove Salad", "somafm-groove-salad")]
    [InlineData("FIP  (hifi.aac)", "fip-hifi-aac")]
    [InlineData("  Radio  Paradise  ", "radio-paradise")]
    [InlineData("100% Hits!", "100-hits")]
    public void Slugify_ProducesAUrlSafeStableId(string name, string expected)
    {
        StationGates.Slugify(name).Should().Be(expected);
    }

    // A name with nothing slug-safe in it must still produce a routable id rather
    // than an empty string, which would collide with every other such station.
    [Fact]
    public void Slugify_NeverReturnsEmpty()
    {
        StationGates.Slugify("!!!").Should().NotBeEmpty();
    }
}
