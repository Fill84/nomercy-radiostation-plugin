// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.PluginSdk.Abstractions;
using Xunit;
using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.State;
using NoMercy.Plugin.InternetRadio.Views;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class SettingsViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static RadioStation Station(string id, string genre = "Ambient") =>
        new() { Id = id, Name = $"Station {id}", StreamUrl = $"https://example.com/{id}", Genre = genre };

    private static IEnumerable<PluginComponent> AllNodes(PluginView view) => ViewFixtures.Flatten(view);

    private static string Text(PluginView view) =>
        string.Join(" ", AllNodes(view).SelectMany(node => node.Props.Values)
            .Where(value => value is string)
            .Select(value => (string)value!));

    private static readonly DateTimeOffset NextRefresh = Now.AddHours(4);

    private static PluginView Build(StationCatalog catalog) =>
        SettingsView.Build(catalog, "/data/plugins/data/abc", Now, NextRefresh, UserState.Empty);

    // The default arm of SourceBadge's switch emits a badge for anything, so merely
    // asserting a badge exists cannot catch two arms being swapped or mislabelled.
    // Pin the actual label and variant per source, including that a failed refresh
    // changes both for Cache - otherwise "Cached" and "Cached - refresh failed"
    // would be indistinguishable to this test.
    [Theory]
    [InlineData(CatalogSource.Fetched, false, "Fetched from radio-browser.info", PluginBadgeVariant.Success)]
    [InlineData(CatalogSource.Cache, false, "Cached", PluginBadgeVariant.Neutral)]
    [InlineData(CatalogSource.Cache, true, "Cached — refresh failed", PluginBadgeVariant.Warning)]
    [InlineData(CatalogSource.UserOverride, false, "Your own station list", PluginBadgeVariant.Info)]
    [InlineData(CatalogSource.Unavailable, false, "No stations", PluginBadgeVariant.Danger)]
    public void BadgesWhereTheStationsCameFrom(
        CatalogSource source, bool lastFetchFailed, string expectedLabel, string expectedVariant)
    {
        StationCatalog catalog = source == CatalogSource.Unavailable
            ? StationCatalog.Empty()
            : StationCatalog.Create([Station("a")], source, Now);

        if (lastFetchFailed)
        {
            catalog = catalog.WithFailedFetch();
        }

        PluginComponent badge = AllNodes(Build(catalog))
            .Where(node => node.Component == PluginComponentType.Badge)
            .Should().ContainSingle().Which;

        badge.Props["text"].Should().Be(expectedLabel);

        // NMBadge's own `variant` prop is its shape, not its meaning - PluginViews.Badge
        // always sends "solid" there. The meaning travels on the surface, where the
        // design system keeps semantic colour, so that is where this assertion has to
        // read it now.
        Dictionary<string, object?> surface = badge.Props["surface"]
            .Should().BeOfType<Dictionary<string, object?>>().Subject;
        surface["status"].Should().Be(expectedVariant);
    }

    // The first thing anyone wants when a station is missing.
    [Fact]
    public void SaysHowOldTheCatalogueIs()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a")], CatalogSource.Cache, Now - TimeSpan.FromHours(3));

        Text(Build(catalog)).Should().Contain("3 hours ago");
    }

    // "never" alone is satisfied by the always-rendered explanatory paragraph
    // ("...plugin hub handlers are never registered"), regardless of catalogue
    // state. Match the never-fetched sentence itself so this can only pass because
    // Age() actually took its null-FetchedAt branch.
    [Fact]
    public void SaysWhenTheCatalogueHasNeverBeenFetched()
    {
        Text(Build(StationCatalog.Empty())).Should().Contain("has never been fetched");
    }

    // "Refresh now" claimed to fetch. It re-rendered through GetAsync, which - with
    // a cache younger than the 36-hour TTL, always given the daily job - returns the
    // exact same cache untouched: a control that silently does nothing. The label
    // has to say what the button honestly does.
    [Fact]
    public void OffersAReloadRatherThanClaimingToRefresh()
    {
        PluginComponent button = AllNodes(Build(StationCatalog.Create([Station("a")], CatalogSource.Cache, Now)))
            .Should().ContainSingle(node => node.Action != null
                && node.Action.Type == PluginActionType.RefreshView)
            .Which;

        // One label, read by the eye and by a screen reader alike. It must not promise a
        // refresh: this button re-reads what is already cached, and a label saying
        // otherwise is the dishonesty this test exists to prevent. PluginViews.Button
        // carries it as `ariaLabel` and as the text of its own child leaf; ariaLabel is
        // the one prop guaranteed to be there without walking into Items. The value is
        // the translation key now, not the English word - NMButton only ever emits
        // aria-label when there is no visible label to take the accessible name from,
        // which this button always has, so the key travels here without ever reaching
        // a screen reader unresolved.
        button.Props["ariaLabel"].Should().Be("settings.reload");
        button.Props["ariaLabel"].Should().NotBe("Refresh now");
    }

    // NMButton draws an icon only from its iconLeft slot, never from a plain `icon`
    // prop - so Reload has to actually put one there to be seen at all.
    [Fact]
    public void ReloadCarriesItsIconInTheSlotNMButtonReadsFrom()
    {
        PluginComponent button = AllNodes(Build(StationCatalog.Create([Station("a")], CatalogSource.Cache, Now)))
            .Should().ContainSingle(node => node.Action != null
                && node.Action.Type == PluginActionType.RefreshView)
            .Which;

        PluginComponent icon = PluginNodes.IconLeft(button)!;

        icon.Component.Should().Be("NMIcon");
        icon.Props["icon"].Should().Be("portableRadio");
    }

    // The spec asks for both how old the catalogue is and when it next refreshes -
    // dropped from the plan for Task 9's page, this restores the second half.
    [Fact]
    public void SaysWhenTheRefreshJobNextRuns()
    {
        Text(Build(StationCatalog.Create([Station("a")], CatalogSource.Cache, Now)))
            .Should().Contain("Next automatic refresh");
    }

    // So nobody has to derive the dashless-GUID path from a README.
    [Fact]
    public void NamesTheDataFolderAndTheOverrideFile()
    {
        string text = Text(Build(StationCatalog.Create([Station("a")], CatalogSource.Fetched, Now)));

        text.Should().Contain("/data/plugins/data/abc");
        text.Should().Contain(StationOverrides.FileName);
    }

    [Fact]
    public void CountsTheStationsInEachGenre()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", "Ambient"), Station("b", "Ambient"), Station("c", "Rock")],
            CatalogSource.Fetched, Now);

        PluginComponent table = PluginNodes.Tables(Build(catalog)).First();

        PluginNodes.Rows(table).Should().HaveCount(2);
        PluginNodes.Rows(table).Should().ContainSingle(row =>
            PluginNodes.Value(table, row, "genre") == "Ambient"
            && PluginNodes.Value(table, row, "stations") == "2");
        PluginNodes.Rows(table).Should().ContainSingle(row =>
            PluginNodes.Value(table, row, "genre") == "Rock"
            && PluginNodes.Value(table, row, "stations") == "1");
    }

    // A stale catalogue has to explain itself, or it looks like the plugin simply
    // stopped finding new stations.
    [Fact]
    public void SaysSoWhenTheLastRefreshFailed()
    {
        StationCatalog catalog = StationCatalog
            .Create([Station("a")], CatalogSource.Cache, Now - TimeSpan.FromDays(4))
            .WithFailedFetch();

        Text(Build(catalog)).Should().Contain("could not be refreshed");
    }

    // The honest statement of why there is nothing to configure. Named so that when
    // the server is fixed, a search for the issue number finds this page.
    [Fact]
    public void ExplainsWhyThereIsNothingToEdit()
    {
        Text(Build(StationCatalog.Create([Station("a")], CatalogSource.Fetched, Now)))
            .Should().Contain("#26");
    }

    // The renderer only knows title/subtitle/caption; anything else used to silently
    // read as body text. A bad variant string no longer travels on the wire for this
    // test to catch directly - PluginViews.Text picks the component at build time
    // instead - so the same risk now shows up as an unrecognised component tag rather
    // than an unrecognised prop value.
    [Fact]
    public void UsesOnlyComponentsTheRendererKnows()
    {
        PluginView view = Build(StationCatalog.Create([Station("a")], CatalogSource.Fetched, Now));

        AllNodes(view).Select(node => node.Component)
            .Should().OnlyContain(component => RendererKnownComponents.Contains(component));
    }

    // PluginComponentType.All plus the two tags PluginViews synthesises for a heading
    // and the line under it - a plugin cannot address either by name, only PluginViews
    // can draw them.
    private static readonly string[] RendererKnownComponents =
    [
        .. PluginComponentType.All,
        "NMContentHeader",
        "NMHelper",
    ];

    [Fact]
    public void EveryNodeHasAUniqueId()
    {
        PluginView view = Build(StationCatalog.Create(
            [Station("a", "Ambient"), Station("b", "Rock")], CatalogSource.Fetched, Now));

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }
}
