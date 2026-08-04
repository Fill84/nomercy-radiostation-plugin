// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using Microsoft.Extensions.Logging;
using NoMercy.Events;
using NoMercy.Plugins.Abstractions;

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
    // not - the manifest declares no library access, no secrets and no hub.
    public IEventBus EventBus => throw new NotSupportedException();
    public IServiceProvider Services => throw new NotSupportedException();
    public IPluginConfiguration Configuration => throw new NotSupportedException();
    public IPluginSecretStore Secrets => throw new NotSupportedException();
    public IPluginLibraryQuery Library => throw new NotSupportedException();
    public IPluginLibraryWriter? LibraryWriter => null;
    public IPluginGrants Grants => throw new NotSupportedException();
    public IPluginHubContext Hub => throw new NotSupportedException();

    public Task PublishAsync<T>(string name, T payload, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
