// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public sealed class StreamTitleTests
{
    [Fact]
    public void AnAnnouncedLineSplitsIntoWhoIsPlayingAndWhat()
    {
        StreamTitle parsed = StreamTitle.Parse("KEVIN DE VRIES, MAU P - Metro (Played by Kevin De Vries Mainstage TL BE 26) ");

        Assert.Equal("KEVIN DE VRIES, MAU P", parsed.Artist);
        Assert.Equal("Metro (Played by Kevin De Vries Mainstage TL BE 26)", parsed.Track);
    }

    [Fact]
    public void ATrackWithItsOwnDashKeepsAllOfIt()
    {
        StreamTitle parsed = StreamTitle.Parse("Artist - Song - Live Version");

        Assert.Equal("Artist", parsed.Artist);
        Assert.Equal("Song - Live Version", parsed.Track);
    }

    [Fact]
    public void ALineWithNoSeparatorIsAllTrackAndNoArtist()
    {
        StreamTitle parsed = StreamTitle.Parse("  Station jingle  ");

        Assert.Null(parsed.Artist);
        Assert.Equal("Station jingle", parsed.Track);
    }

    [Fact]
    public void NothingOnOneSideOfTheDashIsNotTwoFields()
    {
        StreamTitle parsed = StreamTitle.Parse("- Metro");

        Assert.Null(parsed.Artist);
        Assert.Equal("- Metro", parsed.Track);
    }

    [Fact]
    public void ADashedHyphenSeparatorSplitsTheSameWay()
    {
        StreamTitle parsed = StreamTitle.Parse("Artist – Song");

        Assert.Equal("Artist", parsed.Artist);
        Assert.Equal("Song", parsed.Track);
    }

    [Fact]
    public void ARemixParentheticalBelongsToTheTitle()
    {
        StreamTitle parsed = StreamTitle.Parse("Thomas Lemmer - Space Travels (Ambient Remix)");

        Assert.Equal("Space Travels (Ambient Remix)", parsed.Track);
    }
}
