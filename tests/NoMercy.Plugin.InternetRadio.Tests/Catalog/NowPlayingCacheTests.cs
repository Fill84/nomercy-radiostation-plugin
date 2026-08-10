// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests;

public class NowPlayingCacheTests
{
    private static (NowPlayingCache Cache, Action<TimeSpan> Advance) Build(TimeSpan lifetime)
    {
        DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        NowPlayingCache cache = new(lifetime, () => now);

        return (cache, span => now = now.Add(span));
    }

    [Fact]
    public void KnowsNothingAboutAStationItHasNotHeard()
    {
        (NowPlayingCache cache, _) = Build(TimeSpan.FromSeconds(25));

        cache.TryGet("station", out _).Should().BeFalse();
    }

    [Fact]
    public void AnswersFromMemoryWhileTheEntryIsFresh()
    {
        (NowPlayingCache cache, Action<TimeSpan> advance) = Build(TimeSpan.FromSeconds(25));
        cache.Set("station", ("Portishead", "Roads"));

        advance(TimeSpan.FromSeconds(24));

        cache.TryGet("station", out (string? Artist, string Track)? value).Should().BeTrue();
        value!.Value.Artist.Should().Be("Portishead");
        value.Value.Track.Should().Be("Roads");
    }

    [Fact]
    public void ForgetsOnceTheLifetimeHasPassed()
    {
        (NowPlayingCache cache, Action<TimeSpan> advance) = Build(TimeSpan.FromSeconds(25));
        cache.Set("station", ("Portishead", "Roads"));

        advance(TimeSpan.FromSeconds(25));

        cache.TryGet("station", out _).Should().BeFalse();
        cache.Count.Should().Be(0, "an expired entry is dropped rather than left to accumulate");
    }

    [Fact]
    public void RemembersThatAStationAnnouncedNothing()
    {
        // The distinction the cache exists for: "not asked recently" costs a connection to
        // somebody else's server, "asked and it said nothing" must not.
        (NowPlayingCache cache, _) = Build(TimeSpan.FromSeconds(25));
        cache.Set("quiet", null);

        cache.TryGet("quiet", out (string? Artist, string Track)? value).Should().BeTrue();
        value.Should().BeNull();
    }

    [Fact]
    public void KeepsStationsApart()
    {
        (NowPlayingCache cache, _) = Build(TimeSpan.FromSeconds(25));
        cache.Set("a", ("Air", "La Femme d'Argent"));
        cache.Set("b", ("The Breakfast Show", "The Breakfast Show"));

        cache.TryGet("a", out (string? Artist, string Track)? first).Should().BeTrue();
        cache.TryGet("b", out (string? Artist, string Track)? second).Should().BeTrue();

        first!.Value.Track.Should().Be("La Femme d'Argent");
        second!.Value.Artist.Should().Be("The Breakfast Show");
        second.Value.Track.Should().Be("The Breakfast Show");
    }

    [Fact]
    public void ANewerAnswerReplacesTheOneBeforeIt()
    {
        (NowPlayingCache cache, Action<TimeSpan> advance) = Build(TimeSpan.FromSeconds(25));
        cache.Set("station", ("Portishead", "Roads"));

        advance(TimeSpan.FromSeconds(20));
        cache.Set("station", ("Boards of Canada", "Roygbiv"));

        // The clock restarts with the new answer, so a station that keeps announcing is
        // not re-read every twenty-five seconds regardless.
        advance(TimeSpan.FromSeconds(20));

        cache.TryGet("station", out (string? Artist, string Track)? value).Should().BeTrue();
        value!.Value.Track.Should().Be("Roygbiv");
    }
}
