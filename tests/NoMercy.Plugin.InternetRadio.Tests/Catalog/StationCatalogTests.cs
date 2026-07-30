// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public class StationCatalogTests
{
    private static RadioStation Station(string id, string? genre = "Ambient", int popularity = 0) =>
        new()
        {
            Id = id,
            Name = $"Station {id}",
            StreamUrl = $"https://example.com/{id}",
            Genre = genre,
            Popularity = popularity,
        };

    [Fact]
    public void ById_FindsAStationAndIsCaseInsensitive()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("AbC", "Ambient")], CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.ById("abc").Should().NotBeNull();
        catalog.ById("nope").Should().BeNull();
    }

    [Fact]
    public void ByGenreSlug_ReturnsOnlyThatGenre()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", "Ambient"), Station("b", "Rock"), Station("c", "Ambient")],
            CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.ByGenreSlug("ambient").Select(station => station.Id)
            .Should().BeEquivalentTo(new[] { "a", "c" });
    }

    [Fact]
    public void ByGenreSlug_ReturnsEmptyForAnUnknownSlug()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", "Ambient")], CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.ByGenreSlug("no-such-genre").Should().BeEmpty();
    }

    // Only genres that actually have stations, so the browse page never offers a chip
    // that leads to an empty page.
    [Fact]
    public void Genres_ListOnlyNonEmptySectionsWithTheirCounts()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", "Ambient"), Station("b", "Ambient"), Station("c", "Rock")],
            CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.Genres.Should().HaveCount(2);
        catalog.Genres.Single(genre => genre.Section.Label == "Ambient").Count.Should().Be(2);
        catalog.Genres.Should().NotContain(genre => genre.Section.Label == "Jazz");
    }

    // House, Techno and Classical are chosen because their GenreMap.Sections order
    // (House, Techno, Classical) differs from both alphabetical order (Classical,
    // House, Techno) and this insertion order (Techno, Classical, Other, House) - so
    // an implementation that echoed insertion order, sorted alphabetically, or used
    // bucket-dictionary order would all fail this, where three genres that happened
    // to already agree with Sections order would not have caught the regression.
    [Fact]
    public void Genres_AreOrderedByGenreMapSectionsWithOtherLast()
    {
        StationCatalog catalog = StationCatalog.Create(
            [
                Station("a", "Techno"),
                Station("b", "Classical"),
                Station("c", GenreMap.Other),
                Station("d", "House"),
            ],
            CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.Genres.Select(genre => genre.Section.Label)
            .Should().Equal("House", "Techno", "Classical", GenreMap.Other);
    }

    // "Other" is a real destination - three of the four Tomorrowland records carry no
    // tags - so it has to be reachable rather than swallowed.
    [Fact]
    public void Genres_IncludeOtherWhenSomethingLandedThere()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", GenreMap.Other)], CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.Genres.Should().ContainSingle().Which.Section.Label.Should().Be(GenreMap.Other);
        catalog.ByGenreSlug(StationGates.Slugify(GenreMap.Other)).Should().ContainSingle();
    }

    // A radio-browser record with no tag this plugin maps has a null Genre, not an
    // empty string. It still has to land in "Other" and stay reachable everywhere -
    // not silently vanish because something upstream stopped coalescing it.
    [Fact]
    public void ANullGenreIsBucketedUnderOtherAndStaysReachable()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", genre: null)], CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.ByGenreSlug(StationGates.Slugify(GenreMap.Other)).Select(station => station.Id)
            .Should().ContainSingle().Which.Should().Be("a");
        catalog.Genres.Should().ContainSingle().Which.Section.Label.Should().Be(GenreMap.Other);
        catalog.ById("a").Should().NotBeNull();
    }

    // A user's stations.json is not gated, so two entries can genuinely collide on Id.
    // The first one - the one already on screen - has to keep winning rather than the
    // page's contents flipping depending on which duplicate the map happened to keep.
    [Fact]
    public void ById_KeepsTheFirstStationWhenTwoEntriesShareAnId()
    {
        RadioStation first = new()
        {
            Id = "dup",
            Name = "First",
            StreamUrl = "https://example.com/first",
            Genre = "Ambient",
        };
        RadioStation second = new()
        {
            Id = "dup",
            Name = "Second",
            StreamUrl = "https://example.com/second",
            Genre = "Rock",
        };

        StationCatalog catalog = StationCatalog.Create(
            [first, second], CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.ById("dup").Should().NotBeNull();
        catalog.ById("dup")!.Name.Should().Be("First");
    }

    [Fact]
    public void Popular_ReturnsTheMostVotedFirstAndCapsTheCount()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", "Ambient", 10), Station("b", "Rock", 99), Station("c", "Jazz", 50)],
            CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.Popular(2).Select(station => station.Id).Should().Equal("b", "c");
    }

    [Fact]
    public void Popular_ReturnsEverythingWhenAskedForMoreThanItHas()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", "Ambient")], CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.Popular(50).Should().HaveCount(1);
    }

    [Fact]
    public void Empty_IsUnavailableAndRemembersWhetherAFetchFailed()
    {
        StationCatalog.Empty().Source.Should().Be(CatalogSource.Unavailable);
        StationCatalog.Empty().IsEmpty.Should().BeTrue();
        StationCatalog.Empty(lastFetchFailed: true).LastFetchFailed.Should().BeTrue();
        StationCatalog.Empty().LastFetchFailed.Should().BeFalse();
    }
}
