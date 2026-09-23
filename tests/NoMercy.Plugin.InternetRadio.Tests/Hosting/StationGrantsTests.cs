// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Hosting;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using NoMercy.PluginSdk.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Hosting;

public class StationGrantsTests
{
    private sealed class RecordingGrants(params string[] held) : IPluginGrants
    {
        public List<(string Kind, string Value, string Reason)> Requested { get; } = [];

        public Task<bool> HasAsync(string kind, string value, CancellationToken ct = default) =>
            Task.FromResult(held.Contains($"{kind}={value}"));

        public Task<IReadOnlyList<string>> GetAsync(string kind, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task RequestAsync(string kind, string value, string reason, CancellationToken ct = default)
        {
            Requested.Add((kind, value, reason));
            return Task.CompletedTask;
        }
    }

    private static string MediaProxy => PluginGrantKind.ForCapability(PluginCapabilityNames.MediaProxy);

    [Fact]
    public async Task AsksForBothGrantsWhenItHoldsNeither()
    {
        RecordingGrants grants = new();

        await new StationGrants(grants, new RecordingLogger()).EnsureAsync(CancellationToken.None);

        grants.Requested.Select(request => (request.Kind, request.Value))
            .Should().BeEquivalentTo(new[]
            {
                (MediaProxy, PluginGrant.Everything),
                (PluginGrantKind.NetworkHost, PluginGrant.Everything),
            });
    }

    // The reason is what the owner reads in the dashboard. It has to say what the plugin
    // will actually do, in their language, or the decision is a guess.
    [Fact]
    public async Task EveryReasonSaysWhatItIsFor()
    {
        RecordingGrants grants = new();

        await new StationGrants(grants, new RecordingLogger()).EnsureAsync(CancellationToken.None);

        grants.Requested.Should().OnlyContain(request => request.Reason.Length > 40);
        grants.Requested.Should().Contain(request => request.Reason.Contains("play"));
        grants.Requested.Should().Contain(request => request.Reason.Contains("logo"));
    }

    [Fact]
    public async Task AsksForNothingItAlreadyHolds()
    {
        RecordingGrants grants = new($"{MediaProxy}={PluginGrant.Everything}");

        await new StationGrants(grants, new RecordingLogger()).EnsureAsync(CancellationToken.None);

        grants.Requested.Select(request => request.Kind).Should().Equal(PluginGrantKind.NetworkHost);
    }

    [Fact]
    public async Task AsksOnceEvenWhenTheRefreshRunsAgain()
    {
        RecordingGrants grants = new();
        StationGrants asking = new(grants, new RecordingLogger());

        await asking.EnsureAsync(CancellationToken.None);
        await asking.EnsureAsync(CancellationToken.None);

        grants.Requested.Should().HaveCount(2);
    }

    // A host that refuses the facade outright must not cost the refresh its catalogue.
    [Fact]
    public async Task AHostThatCannotBeAskedIsNotFatal()
    {
        await new StationGrants(new ThrowingGrants(), new RecordingLogger())
            .EnsureAsync(CancellationToken.None);
    }

    private sealed class ThrowingGrants : IPluginGrants
    {
        public Task<bool> HasAsync(string kind, string value, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetAsync(string kind, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task RequestAsync(string kind, string value, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
