// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using NoMercy.PluginSdk.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Playback;

public class HostStationStreamsTests
{
    private static readonly RadioStation Station =
        new() { Id = "a", Name = "Alpha FM", StreamUrl = "https://example.com/live" };

    private sealed class RecordingGrants : IPluginGrants
    {
        public List<(string Kind, string Value)> Requested { get; } = [];

        public Task<bool> HasAsync(string kind, string value, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> GetAsync(string kind, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task RequestAsync(string kind, string value, string reason, CancellationToken ct = default)
        {
            reason.Should().NotBeNullOrWhiteSpace();
            Requested.Add((kind, value));
            return Task.CompletedTask;
        }
    }

    private static PluginRefusedException Refusal(string code) =>
        new(new PluginRefusal(code, "Internet Radio", "what", "why", "fix", PluginRefusalSeverity.Blocked));

    [Fact]
    public async Task AMintedUrlIsWhatThePlayerGets()
    {
        PluginProxyRequest? asked = null;
        HostStationStreams streams = new(
            (request, _) => { asked = request; return Task.FromResult(new Uri("/api/v1/plugins/x/media/t", UriKind.Relative)); },
            new RecordingGrants(),
            new RecordingLogger());

        StationStream stream = await streams.ForAsync(Station, CancellationToken.None);

        stream.Should().Be(StationStream.Playable("/api/v1/plugins/x/media/t"));
        asked!.Links.Should().ContainSingle().Which.Url.Should().Be(new Uri("https://example.com/live"));
        asked.Title.Should().Be("Alpha FM");
        // A live stream never ends; a timeout would cut every listener off mid-song.
        asked.Timeout.Should().BeNull();
    }

    [Fact]
    public async Task WithoutTheGrantItAsksTheOwnerOnceForEveryHost()
    {
        RecordingGrants grants = new();
        HostStationStreams streams = new(
            (_, _) => throw Refusal(PluginRefusalCodes.CapabilityScopeRefused), grants, new RecordingLogger());

        StationStream first = await streams.ForAsync(Station, CancellationToken.None);
        StationStream second = await streams.ForAsync(Station, CancellationToken.None);

        first.Should().Be(StationStream.Refused(PluginRefusalCodes.CapabilityScopeRefused));
        second.IsPlayable.Should().BeFalse();
        grants.Requested.Should().Equal(
            (PluginGrantKind.ForCapability(PluginCapabilityNames.MediaProxy), PluginGrant.Everything));
    }

    // A host without the media facade refuses too; that is not a grant the owner can give.
    [Fact]
    public async Task AnyOtherRefusalIsReportedAndAsksForNothing()
    {
        RecordingGrants grants = new();
        HostStationStreams streams = new(
            (_, _) => throw Refusal(PluginRefusalCodes.ContractVersionMismatch), grants, new RecordingLogger());

        StationStream stream = await streams.ForAsync(Station, CancellationToken.None);

        stream.Should().Be(StationStream.Refused(PluginRefusalCodes.ContractVersionMismatch));
        grants.Requested.Should().BeEmpty();
    }

    // Asking must never cost the viewer the page.
    [Fact]
    public async Task AFailedGrantRequestStillAnswersRefused()
    {
        HostStationStreams streams = new(
            (_, _) => throw Refusal(PluginRefusalCodes.CapabilityScopeRefused),
            new ThrowingGrants(),
            new RecordingLogger());

        StationStream stream = await streams.ForAsync(Station, CancellationToken.None);

        stream.IsPlayable.Should().BeFalse();
    }

    private sealed class ThrowingGrants : IPluginGrants
    {
        public Task<bool> HasAsync(string kind, string value, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task<IReadOnlyList<string>> GetAsync(string kind, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task RequestAsync(string kind, string value, string reason, CancellationToken ct = default) => throw new InvalidOperationException();
    }
}
