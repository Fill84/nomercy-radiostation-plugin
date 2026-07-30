// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests;

public sealed class PluginLifecycleTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"nm-radio-{Guid.NewGuid():N}");
    private readonly FakeHttpMessageHandler _handler = new();

    public PluginLifecycleTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private const string OneStation = """
        [{"stationuuid":"u1","name":"Example FM","url":"https://example.com/a",
          "url_resolved":"https://example.com/a","tags":"ambient","countrycode":"NL",
          "codec":"MP3","bitrate":128,"hls":0,"lastcheckok":1,"votes":5}]
        """;

    private InternetRadioPlugin Started()
    {
        _handler.Respond(OneStation);
        InternetRadioPlugin plugin = new();
        plugin.Initialize(new FakePluginContext(_folder, new HttpClient(_handler)));
        return plugin;
    }

    private static PluginViewRequest Request(string route) => new() { Route = route };

    [Fact]
    public async Task ServesTheBrowsePageAtTheRoot()
    {
        using InternetRadioPlugin plugin = Started();

        PluginView view = await plugin.GetViewAsync(Request("/"), CancellationToken.None);

        view.Components.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/all")]
    [InlineData("/settings")]
    [InlineData("/genre/ambient")]
    public async Task ServesEveryDeclaredRoute(string route)
    {
        using InternetRadioPlugin plugin = Started();

        PluginView view = await plugin.GetViewAsync(Request(route), CancellationToken.None);

        view.Components.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ServesAnEmptyStateForAnUnknownRoute()
    {
        using InternetRadioPlugin plugin = Started();

        PluginView view = await plugin.GetViewAsync(Request("/nope"), CancellationToken.None);

        view.Components.Should().Contain(node => node.Component == PluginComponentType.EmptyState);
    }

    // Initialize is synchronous with nowhere to await a fix, and a plugin that throws
    // from it fails to load. So it captures the context and does nothing else - the
    // first real work happens on a view or a tick.
    [Fact]
    public void InitializeDoesNoIoAndDoesNotThrow()
    {
        using InternetRadioPlugin plugin = new();

        plugin.Invoking(candidate =>
                candidate.Initialize(new FakePluginContext(_folder, new HttpClient(_handler))))
            .Should().NotThrow();

        _handler.Requests.Should().BeEmpty();
    }

    // A tick can only arrive after registration, so a missing context here is the
    // host calling out of order - worth surfacing rather than swallowing.
    [Fact]
    public async Task ThrowsWhenTickedBeforeInitialize()
    {
        using InternetRadioPlugin plugin = new();

        await plugin.Invoking(candidate => candidate.ExecuteAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RefreshTickFetchesAndWritesTheCache()
    {
        using InternetRadioPlugin plugin = Started();

        await plugin.ExecuteAsync(CancellationToken.None);

        File.Exists(Path.Combine(_folder, CatalogCache.FileName)).Should().BeTrue();
    }

    [Fact]
    public async Task ThrowsForAJobNameItDoesNotHave()
    {
        using InternetRadioPlugin plugin = Started();

        await plugin.Invoking(candidate => candidate.ExecuteAsync("nope", CancellationToken.None))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // A tick after Dispose is the host calling a plugin it already tore down.
    [Fact]
    public async Task ThrowsWhenTickedAfterDispose()
    {
        InternetRadioPlugin plugin = Started();
        plugin.Dispose();

        await plugin.Invoking(candidate => candidate.ExecuteAsync(CancellationToken.None))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    // A view request racing Dispose is NOT the same case: the host may still be
    // draining a page render while tearing down, so this answers with something
    // renderable rather than throwing into the request pipeline.
    [Fact]
    public async Task ServesARenderableViewAfterDispose()
    {
        InternetRadioPlugin plugin = Started();
        plugin.Dispose();

        PluginView view = await plugin.GetViewAsync(Request("/"), CancellationToken.None);

        view.Components.Should().Contain(node => node.Component == PluginComponentType.EmptyState);
    }

    // This page is the plugin's only diagnostic surface. Letting a failure throw
    // through it hides its own cause behind a broken page.
    [Fact]
    public async Task ServesAnErrorViewRatherThanThrowingWhenTheCatalogueCannotBeBuilt()
    {
        InternetRadioPlugin plugin = new();
        _handler.Fail(new HttpRequestException("down"));
        plugin.Initialize(new FakePluginContext(_folder, new HttpClient(_handler)));

        PluginView view = await plugin.GetViewAsync(Request("/"), CancellationToken.None);

        view.Components.Should().NotBeNullOrEmpty();
        plugin.Dispose();
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        InternetRadioPlugin plugin = Started();

        plugin.Dispose();
        plugin.Invoking(candidate => candidate.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void DisposeIsSafeBeforeInitialize()
    {
        InternetRadioPlugin plugin = new();

        plugin.Invoking(candidate => candidate.Dispose()).Should().NotThrow();
    }
}
