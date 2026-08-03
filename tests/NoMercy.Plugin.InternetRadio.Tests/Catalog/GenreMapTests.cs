// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public class GenreMapTests
{
    [Fact]
    public void Sections_HaveUniqueSlugsSoARouteResolvesToOne()
    {
        GenreMap.Sections.Select(section => section.Slug)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Sections_HaveUniqueLabels()
    {
        GenreMap.Sections.Select(section => section.Label)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Sections_SlugsAreUrlSafe()
    {
        GenreMap.Sections.Should().OnlyContain(section => section.Slug == StationGates.Slugify(section.Label));
    }

    [Theory]
    [InlineData("ambient,atmospheric,chillout,drone", "Ambient")]
    [InlineData("dance,edm,electronic", "Dance & Electronic")]
    [InlineData("jazz,smooth jazz", "Jazz")]
    [InlineData("HIP HOP,rap", "Hip Hop")]
    public void Resolve_MapsATagListOntoItsSection(string tags, string expected)
    {
        GenreMap.Resolve(tags).Should().Be(expected);
    }

    // Section order is the priority order. A station tagged both is a real case -
    // "ambient,chillout" is the single commonest pair in the database - and it has to
    // land in exactly one section, deterministically, or the same station appears
    // twice in the browse page.
    [Fact]
    public void Resolve_PicksTheEarliestMatchingSection()
    {
        GenreMap.Resolve("chillout,ambient").Should().Be("Ambient");
    }

    // Several of the pinned Tomorrowland records carry no tags at all. They must
    // still land somewhere routable rather than dropping out of the genre pages.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("something,nobody,mapped")]
    public void Resolve_FallsBackToOtherRatherThanNull(string? tags)
    {
        GenreMap.Resolve(tags).Should().Be(GenreMap.Other);
    }

    [Fact]
    public void Resolve_IgnoresSurroundingWhitespaceOnATag()
    {
        GenreMap.Resolve("  rock ,  pop ").Should().Be("Rock");
    }

    // Substring matching would put "rockabilly" in Rock and "poparazzi" in Pop.
    [Fact]
    public void Resolve_MatchesAWholeTagAndNotASubstring()
    {
        GenreMap.Resolve("rockabilly").Should().Be(GenreMap.Other);
    }

    [Fact]
    public void BySlug_FindsASectionAndIsCaseInsensitive()
    {
        GenreMap.BySlug("drum-bass")!.Label.Should().Be("Drum & Bass");
        GenreMap.BySlug("AMBIENT")!.Label.Should().Be("Ambient");
    }

    [Fact]
    public void BySlug_ReturnsNullForAnUnknownSlug()
    {
        GenreMap.BySlug("no-such-genre").Should().BeNull();
    }
}
