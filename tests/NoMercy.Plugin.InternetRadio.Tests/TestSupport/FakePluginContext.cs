// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using Microsoft.Extensions.Logging;
using NoMercy.PluginSdk.Abstractions;
using NoMercy.Plugin.InternetRadio.Playback;

namespace NoMercy.Plugin.InternetRadio.Tests.TestSupport;

public sealed class FakePluginContext(string dataFolderPath, HttpClient httpClient) : IPluginContext
{
    public ILogger Logger { get; } = new RecordingLogger();
    public string DataFolderPath { get; } = dataFolderPath;
    public HttpClient HttpClient { get; } = httpClient;
    public Ulid PluginId => PluginIdentity.Id;

    public RecordingLogger Recorded => (RecordingLogger)Logger;

    // Not used by this plugin. Throwing rather than returning a null object means a
    // test cannot accidentally pass while the plugin reaches for something it should
    // not. Every facade this class does not name - Media among them - keeps the
    // interface's own default, which throws PluginRefusedException exactly as a host
    // without that facade does.
    public IPluginEvents Events => throw new NotSupportedException();
    public IPluginConfiguration Configuration => throw new NotSupportedException();
    public IPluginSecretStore Secrets => throw new NotSupportedException();
    public IPluginLibraryQuery Library => throw new NotSupportedException();
    public IPluginLibraryWriter? LibraryWriter => null;

    // Grants has no interface default - unlike Media, a host always has an answer for
    // "what has the owner allowed", even when it is "nothing yet" - so this cannot join
    // the throwing facades above. HostStationStreams.For captures it up front, whether or
    // not Media is offered, so a fake that throws here would fail the station page before
    // ForAsync's own refusal handling ever runs.
    public IPluginGrants Grants { get; } = new NoGrants();

    public IPluginHubContext Hub => throw new NotSupportedException();

    public Task PublishAsync<T>(string name, T payload, CancellationToken ct = default) =>
        throw new NotSupportedException();

    // Nothing has ever been granted and nothing is asked for twice: fine defaults for a
    // plugin whose own tests exercise the grant-request path through HostStationStreams
    // directly, with their own recording stand-in.
    private sealed class NoGrants : IPluginGrants
    {
        public Task<bool> HasAsync(string kind, string value, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<string>> GetAsync(string kind, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task RequestAsync(string kind, string value, string reason, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
