// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public sealed class StationQueryTests
{
    [Fact]
    public void AnEmptyQueryAsksForTheMostVotedStations()
    {
        string query = new StationQuery().ToQueryString(30);

        query.Should().Contain("limit=30")
            .And.Contain("order=votes")
            .And.Contain("reverse=true")
            .And.Contain("hidebroken=true");

        // A blank field is a filter nobody filled in. Sent as an empty string it
        // matches nothing, and the page reads as a database with no stations.
        query.Should().NotContain("name=").And.NotContain("tag=").And.NotContain("country=");
    }

    [Fact]
    public void EveryAxisTravelsTogether()
    {
        string query = new StationQuery
        {
            Name = "anison",
            Tag = "anime",
            Country = "Japan",
            Language = "japanese",
            Codec = "MP3",
            MinBitrate = 128,
        }.ToQueryString(50);

        query.Should().Contain("name=anison")
            .And.Contain("tag=anime")
            .And.Contain("country=Japan")
            .And.Contain("language=japanese")
            .And.Contain("codec=MP3")
            .And.Contain("bitrateMin=128");
    }

    [Fact]
    public void AValueThatNeedsEscapingSurvivesIt()
    {
        string query = new StationQuery { Tag = "drum & bass", Country = "Côte d'Ivoire" }
            .ToQueryString(10);

        query.Should().Contain("tag=drum%20%26%20bass");
        query.Should().Contain("country=C%C3%B4te%20d%27Ivoire");
    }

    [Fact]
    public void SurroundingSpaceIsNotPartOfWhatWasAskedFor()
    {
        new StationQuery { Name = "  anison  " }.ToQueryString(10)
            .Should().Contain("name=anison");
    }

    [Fact]
    public void ABitrateOfZeroIsNoFilterAtAll()
    {
        new StationQuery { MinBitrate = 0 }.ToQueryString(10)
            .Should().NotContain("bitrateMin");
    }
}
