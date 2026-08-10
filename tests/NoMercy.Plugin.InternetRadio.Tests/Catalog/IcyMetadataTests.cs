// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests;

// Every case here is a shape a real station sends. The read itself is a socket and is not
// worth simulating; the parsing is where stations disagree with each other, and with the
// convention they are all supposedly following.
public class IcyMetadataTests
{
    [Fact]
    public void ReadsTheTitleOutOfAnOrdinaryBlock()
    {
        IcyMetadata.ExtractStreamTitle("StreamTitle='Portishead - Roads';StreamUrl='';")
            .Should().Be("Portishead - Roads");
    }

    [Fact]
    public void IgnoresTheOtherFieldsAroundIt()
    {
        IcyMetadata.ExtractStreamTitle("StreamUrl='https://example.test';StreamTitle='Boards of Canada - Roygbiv';")
            .Should().Be("Boards of Canada - Roygbiv");
    }

    [Fact]
    public void KeepsAnApostropheInsideATitle()
    {
        // Terminated on "';" rather than the next quote, because every station that writes
        // an apostrophe leaves it unescaped and half the catalogue has one.
        IcyMetadata.ExtractStreamTitle("StreamTitle='Guns N' Roses - Sweet Child O' Mine';")
            .Should().Be("Guns N' Roses - Sweet Child O' Mine");
    }

    [Fact]
    public void SurvivesTheNulPaddingABlockIsFilledOutWith()
    {
        IcyMetadata.ExtractStreamTitle("StreamTitle='Air - La Femme d'Argent';\0\0\0\0\0")
            .Should().Be("Air - La Femme d'Argent");
    }

    [Fact]
    public void AnnouncesNothingWhenTheBlockHasNoTitle()
    {
        IcyMetadata.ExtractStreamTitle("StreamUrl='https://example.test';").Should().BeNull();
    }

    [Fact]
    public void AnnouncesNothingWhenTheTitleIsEmpty()
    {
        // A station between tracks writes the field and leaves it blank. That is not a
        // track called "".
        IcyMetadata.ExtractStreamTitle("StreamTitle='';").Should().BeNull();
    }

    [Fact]
    public void AnnouncesNothingForAnEmptyBlock()
    {
        IcyMetadata.ExtractStreamTitle(string.Empty).Should().BeNull();
    }

    [Fact]
    public void SplitsArtistFromTrack()
    {
        (string? artist, string track) = IcyMetadata.Split("Portishead - Roads");

        artist.Should().Be("Portishead");
        track.Should().Be("Roads");
    }

    [Fact]
    public void SplitsOnTheFirstSeparatorSoATrackKeepsItsOwnDash()
    {
        (string? artist, string track) =
            IcyMetadata.Split("Simon & Garfunkel - Bridge Over Troubled Water - Live");

        artist.Should().Be("Simon & Garfunkel");
        track.Should().Be("Bridge Over Troubled Water - Live");
    }

    [Fact]
    public void LeavesASingleLineWhole()
    {
        // Plenty of stations announce their own name, or a programme title, and calling
        // half of that an artist would be an invention.
        (string? artist, string track) = IcyMetadata.Split("The Breakfast Show");

        artist.Should().BeNull();
        track.Should().Be("The Breakfast Show");
    }

    [Fact]
    public void LeavesTheLineWholeWhenEitherSideIsEmpty()
    {
        IcyMetadata.Split("Radio Test - ").Artist.Should().BeNull();
        IcyMetadata.Split("Radio Test - ").Track.Should().Be("Radio Test - ");
        IcyMetadata.Split(" - Roads").Artist.Should().BeNull();
        IcyMetadata.Split(" - Roads").Track.Should().Be(" - Roads");
    }
}
