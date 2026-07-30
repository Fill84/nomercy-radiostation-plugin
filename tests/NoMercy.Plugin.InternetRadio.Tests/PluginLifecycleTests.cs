// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Microsoft.Extensions.Logging;
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

    // Shared with the Views/*Tests helpers of the same name: flattens the component
    // tree so a node id can be found regardless of how deep it is nested.
    private static IEnumerable<PluginComponent> Flatten(PluginComponent node)
    {
        yield return node;
        foreach (PluginComponent child in node.Items.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static IEnumerable<PluginComponent> AllNodes(PluginView view) =>
        (view.Components ?? []).SelectMany(Flatten);

    private static bool HasNode(PluginView view, string id) =>
        AllNodes(view).Any(node => node.Id == id);

    private static IEnumerable<string> AllText(PluginView view) =>
        AllNodes(view).SelectMany(node => node.Props.Values).OfType<string>();

    [Fact]
    public async Task ServesTheBrowsePageAtTheRoot()
    {
        using InternetRadioPlugin plugin = Started();

        PluginView view = await plugin.GetViewAsync(Request("/"), CancellationToken.None);

        view.Components.Should().NotBeNullOrEmpty();
    }

    // Each case asserts a node id unique to the view that route is supposed to
    // dispatch to, not merely that something rendered - a switch arm wired to the
    // wrong view still returns a non-empty tree, and "/all" and "/genre/ambient"
    // both rendering AllStationsView would otherwise pass silently. "/station/u1"
    // targets the fixture's own station id, so the station branch is exercised too;
    // it was missing from the previous version of this test entirely.
    [Theory]
    [InlineData("/", "browse-root")]
    [InlineData("/all", "all-root")]
    [InlineData("/settings", "settings-root")]
    [InlineData("/genre/ambient", "genre-root")]
    [InlineData("/station/u1", "station-root")]
    public async Task ServesEveryDeclaredRoute(string route, string expectedRootId)
    {
        using InternetRadioPlugin plugin = Started();

        PluginView view = await plugin.GetViewAsync(Request(route), CancellationToken.None);

        view.Components.Should().NotBeNullOrEmpty();
        HasNode(view, expectedRootId).Should()
            .BeTrue($"'{route}' should be routed to the view whose root node is '{expectedRootId}'");
    }

    [Fact]
    public async Task ServesAnEmptyStateForAnUnknownRoute()
    {
        using InternetRadioPlugin plugin = Started();

        PluginView view = await plugin.GetViewAsync(Request("/nope"), CancellationToken.None);

        // Not merely "some EmptyState exists" - the unknown-route branch's own node,
        // distinct from every other EmptyState this plugin can render (an empty
        // catalogue, a missing station, the disposed panel, the error panel).
        HasNode(view, "unknown-route").Should().BeTrue();
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

    // RefreshAsync deliberately does not consult StationOverrides - the job still
    // fetches and writes the cache regardless - but every view resolves through
    // GetAsync, which checks the override first. Without this, the job's log line
    // would say "Fetched" while the settings page says "UserOverride" for the same
    // catalogue state, which reads as a bug to anyone watching both.
    [Fact]
    public async Task RefreshTickLogLineNamesAnActiveOverrideDifferentlyFromAPlainFetch()
    {
        _handler.Respond(OneStation);
        FakePluginContext context = new(_folder, new HttpClient(_handler));
        using InternetRadioPlugin plugin = new();
        plugin.Initialize(context);

        await plugin.ExecuteAsync(CancellationToken.None);
        string plainMessage = context.Recorded.Entries
            .Should().ContainSingle(entry => entry.Level == LogLevel.Information)
            .Which.Message;

        File.WriteAllText(
            Path.Combine(_folder, StationOverrides.FileName),
            """[{"name":"Mine","streamUrl":"https://example.com/mine"}]"""
        );

        await plugin.ExecuteAsync(CancellationToken.None);
        string overrideMessage = context.Recorded.Entries
            .Where(entry => entry.Level == LogLevel.Information)
            .Last().Message;

        overrideMessage.Should().NotBe(plainMessage);
        overrideMessage.ToLowerInvariant().Should().Contain("override");
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

        // The disposed panel specifically, not any EmptyState - ServesAnErrorViewRatherThan...
        // below renders a different EmptyState (view-error-empty) for a different
        // reason, and the two must not be confused with each other.
        HasNode(view, "plugin-unavailable").Should().BeTrue();
    }

    // This page is the plugin's only diagnostic surface. Letting a failure throw
    // through it hides its own cause behind a broken page.
    //
    // An HttpRequestException from the handler will NOT reach here: CatalogProvider
    // catches every fetch-site failure that isn't the caller's own cancellation, and
    // CatalogCache.ReadAsync returns null rather than throwing on any read failure -
    // so GetAsync resolves to StationCatalog.Empty(lastFetchFailed: true) and never
    // throws. What does reach here is StationOverrides.TryLoad's Path.Combine call,
    // which sits outside its own try block (a known gap - see StationOverridesTests
    // and Task 7's review) and throws ArgumentNullException when DataFolderPath is
    // null, straight out of GetAsync.
    [Fact]
    public async Task ServesAnErrorViewRatherThanThrowingWhenTheCatalogueCannotBeBuilt()
    {
        ArgumentNullException expectedException = Assert.Throws<ArgumentNullException>(
            () => Path.Combine(null!, StationOverrides.FileName));

        InternetRadioPlugin plugin = new();
        FakePluginContext context = new(null!, new HttpClient(_handler));
        plugin.Initialize(context);

        PluginView view = await plugin.GetViewAsync(Request("/"), CancellationToken.None);

        HasNode(view, "view-error").Should().BeTrue();
        HasNode(view, "view-error-badge").Should().BeTrue();
        HasNode(view, "view-error-empty").Should().BeTrue();
        HasNode(view, "view-error-retry").Should().BeTrue();

        context.Recorded.Entries.Should().Contain(entry => entry.Level == LogLevel.Error);

        // The rendered text names what failed, never the exception detail.
        string.Join('\n', AllText(view)).Should().NotContain(expectedException.Message);

        plugin.Dispose();
    }

    // Unreachable given the host's ordering, but GetViewAsync's own rule is "never
    // throws except OperationCanceledException" - a request this early must land on
    // the same error view as any other failure to build one, not throw into the
    // request pipeline the way a tick this early correctly does.
    [Fact]
    public async Task ServesTheErrorViewRatherThanThrowingWhenRequestedBeforeInitialize()
    {
        using InternetRadioPlugin plugin = new();

        PluginView view = await plugin.GetViewAsync(Request("/"), CancellationToken.None);

        HasNode(view, "view-error").Should().BeTrue();
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
