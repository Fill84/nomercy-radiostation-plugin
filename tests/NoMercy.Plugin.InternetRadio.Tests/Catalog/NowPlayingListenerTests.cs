// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Net;
using System.Text;
using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public sealed class NowPlayingListenerTests : IDisposable
{
    private readonly FakeHttpMessageHandler _handler = new();
    private readonly NowPlaying _titles = new();
    private NowPlayingListener? _listener;

    public void Dispose() => _listener?.Dispose();

    private static RadioStation Station(string id) =>
        new() { Id = id, Name = id, StreamUrl = $"https://example.com/{id}" };

    private NowPlayingListener Listener(NowPlayingListener.Limits? limits = null) =>
        _listener = new NowPlayingListener(new HttpClient(_handler), _titles, new RecordingLogger(), limits);

    // Sixteen bytes of "audio", one metadata block announcing the title, then a live
    // stream's silence: a body that never ends until the reader gives up.
    private static HttpResponseMessage Announcing(string title)
    {
        byte[] text = Encoding.UTF8.GetBytes($"StreamTitle='{title}';");
        int blocks = (text.Length + 15) / 16;
        byte[] metadata = new byte[1 + blocks * 16];
        metadata[0] = (byte)blocks;
        text.CopyTo(metadata, 1);

        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StreamContent(new EndlessStream([.. new byte[16], .. metadata])),
        };
        response.Headers.TryAddWithoutValidation("icy-metaint", "16");
        return response;
    }

    private static HttpResponseMessage Silent() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream([])) };

    private static async Task Eventually(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(50);
        }

        condition().Should().BeTrue();
    }

    [Fact]
    public async Task AnAnnouncedTitleReachesNowPlaying()
    {
        _handler.RespondPerRequest(_ => Announcing("Artist - Track"));

        Listener().Touch(Station("a"));

        await Eventually(() => _titles.Get("a") == "Artist - Track");
    }

    // One connection per station, not one per listener or per poll.
    [Fact]
    public async Task PollingAgainSharesTheOneConnection()
    {
        _handler.RespondPerRequest(_ => Announcing("x"));
        NowPlayingListener listener = Listener();

        listener.Touch(Station("a"));
        await Eventually(() => _titles.Get("a") is not null);
        listener.Touch(Station("a"));
        listener.Touch(Station("a"));

        _handler.Requests.Should().HaveCount(1);
        listener.OpenCount.Should().Be(1);
    }

    [Fact]
    public async Task AStationNobodyPollsIsLetGoAndItsTitleForgotten()
    {
        _handler.RespondPerRequest(_ => Announcing("x"));
        NowPlayingListener listener = Listener(new() { Lease = TimeSpan.FromMilliseconds(300) });

        listener.Touch(Station("a"));
        await Eventually(() => _titles.Get("a") is not null);

        await Eventually(() => listener.OpenCount == 0);
        _titles.Get("a").Should().BeNull();
    }

    [Fact]
    public async Task NoMoreThanTheCapAreOpenAtOnce()
    {
        _handler.RespondPerRequest(_ => Announcing("x"));
        NowPlayingListener listener = Listener(new() { MaxConnections = 1 });

        listener.Touch(Station("a"));
        listener.Touch(Station("b"));
        await Eventually(() => _titles.Get("a") is not null);

        listener.OpenCount.Should().Be(1);
        _titles.Get("b").Should().BeNull();
    }

    // A station that sends no metadata is not reconnected on every poll.
    [Fact]
    public async Task AStationThatAnnouncesNothingIsLeftAloneForAWhile()
    {
        _handler.RespondPerRequest(_ => Silent());
        NowPlayingListener listener = Listener();

        listener.Touch(Station("a"));
        await Eventually(() => listener.OpenCount == 0);
        listener.Touch(Station("a"));
        await Task.Delay(200);

        _handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task TheRequestAsksForMetadata()
    {
        _handler.RespondPerRequest(_ => Announcing("x"));

        Listener().Touch(Station("a"));
        await Eventually(() => _handler.Requests.Count == 1);

        _handler.Requests[0].Headers.GetValues("Icy-MetaData").Should().Equal("1");
    }

    /// <summary>Hands out its bytes, then waits for cancellation like a live stream.</summary>
    private sealed class EndlessStream(byte[] prefix) : Stream
    {
        private int _position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_position < prefix.Length)
            {
                int count = Math.Min(buffer.Length, prefix.Length - _position);
                prefix.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }

            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
