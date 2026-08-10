// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text;
using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public sealed class IcyMetadataStreamTests
{
    /// <summary>
    /// Builds a body the way a station does: interval bytes of audio, then a length
    /// byte, then the padded block, repeating.
    /// </summary>
    private static byte[] Body(int interval, params string?[] blocks)
    {
        List<byte> bytes = [];
        byte tone = 1;

        foreach (string? block in blocks)
        {
            for (int index = 0; index < interval; index++)
            {
                bytes.Add(tone);
                tone = unchecked((byte)(tone + 1));
            }

            if (block is null)
            {
                bytes.Add(0);
                continue;
            }

            byte[] payload = Encoding.Latin1.GetBytes(block);
            int padded = (payload.Length + 15) / 16 * 16;

            bytes.Add((byte)(padded / 16));
            bytes.AddRange(payload);
            bytes.AddRange(new byte[padded - payload.Length]);
        }

        return [.. bytes];
    }

    private static async Task<(byte[] Audio, List<string> Titles)> ReadAll(
        byte[] body, int interval, int readSize = 7)
    {
        List<string> titles = [];
        await using IcyMetadataStream stream = new(
            new MemoryStream(body), interval, titles.Add);

        List<byte> audio = [];
        byte[] buffer = new byte[readSize];

        while (await stream.ReadAsync(buffer) is var read && read > 0)
        {
            audio.AddRange(buffer[..read]);
        }

        return ([.. audio], titles);
    }

    [Fact]
    public async Task TheMetadataBlockNeverReachesTheCaller()
    {
        const int interval = 64;
        byte[] body = Body(interval, "StreamTitle='Groove Salad';", null, "StreamTitle='Drone Zone';");

        (byte[] audio, List<string> titles) = await ReadAll(body, interval);

        // Three intervals of audio and not one byte more: a block that leaks through
        // lands inside the MP3 frames, which is heard as a click and read by a
        // decoder as corruption.
        audio.Length.Should().Be(interval * 3);
        titles.Should().Equal("Groove Salad", "Drone Zone");
    }

    [Fact]
    public async Task TheAudioComesBackByteForByte()
    {
        const int interval = 32;
        byte[] body = Body(interval, "StreamTitle='One';");

        (byte[] audio, _) = await ReadAll(body, interval, readSize: 5);

        // The reads deliberately do not divide the interval, so a boundary lands
        // mid-buffer - which is where an off-by-one in the accounting shows up.
        audio.Should().Equal(Enumerable.Range(1, interval).Select(value => (byte)value));
    }

    [Fact]
    public void ATitleKeepsAnApostropheThatIsPartOfTheName()
    {
        byte[] block = Encoding.Latin1.GetBytes("StreamTitle='Guns N' Roses - Patience';StreamUrl='';");

        IcyMetadataStream.ReadTitle(block).Should().Be("Guns N' Roses - Patience");
    }

    [Fact]
    public void ABlockWithNoTitleReportsNothing()
    {
        IcyMetadataStream.ReadTitle(Encoding.Latin1.GetBytes("StreamUrl='';")).Should().BeNull();
        IcyMetadataStream.ReadTitle(Encoding.Latin1.GetBytes("StreamTitle='';")).Should().BeNull();
    }

    [Fact]
    public async Task AStationThatHangsUpInsideABlockEndsTheStream()
    {
        const int interval = 16;
        byte[] whole = Body(interval, "StreamTitle='Cut';");

        // Truncated one byte into the block, which is the station dropping the
        // connection mid-announcement rather than anything the listener did.
        byte[] truncated = whole[..(interval + 2)];

        (byte[] audio, List<string> titles) = await ReadAll(truncated, interval);

        audio.Length.Should().Be(interval);
        titles.Should().BeEmpty();
    }
}

public sealed class NowPlayingTests
{
    [Fact]
    public void OneListenerLeavingDoesNotBlankTheTitleForTheOthers()
    {
        NowPlaying nowPlaying = new();

        IDisposable first = nowPlaying.Listen("station");
        IDisposable second = nowPlaying.Listen("station");
        nowPlaying.Set("station", "Sounds From The Ground - Blink");

        first.Dispose();

        // Still on air for the listener who stayed. Clearing on every relay end
        // meant one browser tab closing wiped the title in every other one.
        nowPlaying.Get("station").Should().Be("Sounds From The Ground - Blink");

        second.Dispose();

        nowPlaying.Get("station").Should().BeNull("nobody is listening to it any more");
    }

    [Fact]
    public void ReleasingTwiceCountsOnce()
    {
        NowPlaying nowPlaying = new();

        IDisposable first = nowPlaying.Listen("station");
        using IDisposable second = nowPlaying.Listen("station");
        nowPlaying.Set("station", "On air");

        first.Dispose();
        first.Dispose();

        nowPlaying.Get("station").Should().Be("On air");
    }
}
