// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public class StationCatalogTests
{
    private static RadioStation Station(string id, string genre, int popularity = 0) =>
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
