// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class SettingsViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static RadioStation Station(string id, string genre = "Ambient") =>
        new() { Id = id, Name = $"Station {id}", StreamUrl = $"https://example.com/{id}", Genre = genre };

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

    // The design system moved a component's words out of a "label" prop: they are now
    // either a "text" prop or a Text child, depending on the component. Collecting them
    // wherever they sit keeps an assertion about what a node says from having to know
    // which of the two a given component happens to use.
    private static List<string> Texts(PluginComponent node) =>
        Flatten(node)
            .Select(child => child.Props.GetValueOrDefault("text") as string)
            .Where(text => text is not null)
            .Select(text => text!)
            .ToList();

    private static string Text(PluginView view) =>
        string.Join(" ", AllNodes(view).SelectMany(node => node.Props.Values)
            .Where(value => value is string)
            .Select(value => (string)value!));

    private static readonly DateTimeOffset NextRefresh = Now.AddHours(4);

    private static PluginView Build(StationCatalog catalog) =>
        SettingsView.Build(catalog, "/data/plugins/data/abc", Now, NextRefresh);

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

        // NMBadge's own "variant" is its shape, not its meaning - the helper always
        // sets it to "solid". The semantic the arms of SourceBadge actually choose
        // between travels on the surface, so that is what has to be pinned here; an
        // assertion on "variant" would now pass for every arm alike.
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

        // A button now carries its words twice: an "ariaLabel" prop for assistive
        // technology and a Text child for the eye. Both are asserted - a label that
        // said "Reload" to a screen reader and "Refresh now" on screen would be the
        // same dishonesty this test exists to prevent.
        button.Props["ariaLabel"].Should().Be("Reload");
        Texts(button).Should().Contain("Reload").And.NotContain("Refresh now");
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
            PluginNodes.Value(table, row, "Genre") == "Ambient"
            && PluginNodes.Value(table, row, "Stations") == "2");
        PluginNodes.Rows(table).Should().ContainSingle(row =>
            PluginNodes.Value(table, row, "Genre") == "Rock"
            && PluginNodes.Value(table, row, "Stations") == "1");
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

    [Fact]
    public void UsesOnlyTextVariantsTheRendererKnows()
    {
        PluginView view = Build(StationCatalog.Create([Station("a")], CatalogSource.Fetched, Now));

        AllNodes(view)
            .Where(node => node.Component == PluginComponentType.Text)
            .Select(node => node.Props.GetValueOrDefault("variant") as string)
            .Should().OnlyContain(variant =>
                variant == null || variant == "title" || variant == "subtitle" || variant == "caption");
    }

    [Fact]
    public void EveryNodeHasAUniqueId()
    {
        PluginView view = Build(StationCatalog.Create(
            [Station("a", "Ambient"), Station("b", "Rock")], CatalogSource.Fetched, Now));

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }
}
