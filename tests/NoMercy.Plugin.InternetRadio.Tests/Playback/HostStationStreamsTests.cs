// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using NoMercy.PluginSdk.Abstractions;
using Xunit;
using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.Playback;
using NoMercy.Plugin.InternetRadio.Views;

namespace NoMercy.Plugin.InternetRadio.Tests.Playback;

public class HostStationStreamsTests
{
    private static readonly RadioStation Station =
        new() { Id = "a", Name = "Alpha FM", StreamUrl = "https://example.com/live" };

    private static PluginRefusedException Refusal(string code) =>
        new(new PluginRefusal(code, "Internet Radio", "what", "why", "fix", PluginRefusalSeverity.Blocked));

    [Fact]
    public async Task AMintedUrlIsWhatThePlayerGets()
    {
        PluginProxyRequest? asked = null;
        HostStationStreams streams = new(
            (request, _) => { asked = request; return Task.FromResult(new Uri("/api/v1/plugins/x/media/t", UriKind.Relative)); },
            new RecordingLogger());

        StationStream stream = await streams.ForAsync(Station, CancellationToken.None);

        stream.Should().Be(StationStream.Playable("/api/v1/plugins/x/media/t"));
        asked!.Links.Should().ContainSingle().Which.Url.Should().Be(new Uri("https://example.com/live"));
        asked.Title.Should().Be("Alpha FM");
        // A live stream never ends; a timeout would cut every listener off mid-song.
        asked.Timeout.Should().BeNull();
    }

    // Asking belongs in StationGrants, before anything is refused. A refusal here is
    // reported and drawn; it does not start a conversation with the owner from inside a
    // page render.
    [Fact]
    public async Task ARefusalIsReportedAndAsksForNothing()
    {
        HostStationStreams streams = new(
            (_, _) => throw Refusal(PluginRefusalCodes.CapabilityScopeRefused), new RecordingLogger());

        StationStream stream = await streams.ForAsync(Station, CancellationToken.None);

        stream.Should().Be(StationStream.Refused(PluginRefusalCodes.CapabilityScopeRefused));
    }

    // A host without the media facade refuses too; that is not a grant the owner can give.
    [Fact]
    public async Task AnyOtherRefusalIsReportedAndAsksForNothing()
    {
        HostStationStreams streams = new(
            (_, _) => throw Refusal(PluginRefusalCodes.ContractVersionMismatch), new RecordingLogger());

        StationStream stream = await streams.ForAsync(Station, CancellationToken.None);

        stream.Should().Be(StationStream.Refused(PluginRefusalCodes.ContractVersionMismatch));
    }

    // stations.json is deliberately ungated (StationOverrides), so a malformed or
    // scheme-less StreamUrl reaches here without ever having been checked.
    [Fact]
    public async Task AMalformedStreamUrlIsRefusedWithoutMinting()
    {
        RadioStation malformed = new() { Id = "a", Name = "Alpha FM", StreamUrl = "not a url" };
        bool minted = false;
        HostStationStreams streams = new(
            (_, _) => { minted = true; return Task.FromResult(new Uri("/x", UriKind.Relative)); },
            new RecordingLogger());

        StationStream stream = await streams.ForAsync(malformed, CancellationToken.None);

        stream.Should().Be(StationStream.Refused(HostStationStreams.StreamUnusable));
        minted.Should().BeFalse();
    }

    // Anything the host's mint throws that is not itself a refusal - an ArgumentException
    // from a proxy request the host itself rejects, say - must still leave the page
    // drawable rather than take the whole view down with it.
    [Fact]
    public async Task AnyOtherMintFailureIsRefusedTooWithoutThrowing()
    {
        HostStationStreams streams = new(
            (_, _) => throw new InvalidOperationException("boom"),
            new RecordingLogger());

        StationStream stream = await streams.ForAsync(Station, CancellationToken.None);

        stream.Should().Be(StationStream.Refused(HostStationStreams.StreamUnusable));
    }

    // The one exception this must never swallow: the caller's own cancellation.
    [Fact]
    public async Task TheCallersOwnCancellationStillPropagates()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        HostStationStreams streams = new(
            (_, ct) => throw new OperationCanceledException(ct),
            new RecordingLogger());

        await streams.Invoking(candidate => candidate.ForAsync(Station, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
