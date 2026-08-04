// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class AllStationsViewTests
{
    private static RadioStation Station(string id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            StreamUrl = $"https://example.com/{id}",
            Genre = "Ambient",
            Country = "NL",
            BitrateKbps = 128,
            Codec = "MP3",
        };

    private static StationCatalog Catalog(params RadioStation[] stations) =>
        StationCatalog.Create(stations, CatalogSource.Fetched, DateTimeOffset.UtcNow);

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

    // By id, not by component type. The design system collapsed Container, List, Row,
    // Grid, Card, Detail, Form and Table onto the single NMCard component, so
    // PluginComponentType.Table now equals PluginComponentType.Container and a search
    // by type matches every container on the page.
    private static PluginComponent Table(PluginView view) =>
        AllNodes(view).Single(node => node.Id == "all-table");

    // The table renders a header row ahead of its body rows, so the stations start at
    // index 1. Skipping it here keeps every assertion below about actual stations.
    private static List<PluginComponent> Rows(PluginView view) =>
        Table(view).Items.Skip(1).ToList();

    // A body row no longer carries the authored cells as flat props: the table turns
    // each one into a Cell holding a Text node at a derived id. This reads the value
    // back out of the place the renderer actually puts it.
    private static string CellText(PluginComponent row, string columnKey) =>
        Flatten(row)
            .Single(node => node.Id == $"{row.Id}-{columnKey}-value")
            .Props.GetValueOrDefault("text") as string ?? string.Empty;

    // A row supplies its cells by column key, so a column the rows never fill renders
    // as a blank stripe down the table.
    [Fact]
    public void EveryColumnIsFilledByEveryRow()
    {
        PluginView view = AllStationsView.Build(Catalog(Station("a", "Alpha FM")));

        PluginComponent table = Table(view);
        PluginComponent header = table.Items[0];

        // The column list is no longer a prop to read back - the table spends it
        // building the header. So the header is what says how many columns there are,
        // and a row that fills every one of them has a cell for each with words in it.
        // A column no row fills still renders its cell, as empty text: that is exactly
        // the blank stripe this test exists to catch, so an assertion that the cell
        // merely exists would no longer catch anything.
        foreach (PluginComponent row in table.Items.Skip(1))
        {
            row.Items.Should().HaveSameCount(header.Items);
            row.Items.Should().OnlyContain(cell =>
                Flatten(cell).Any(node =>
                    !string.IsNullOrEmpty(node.Props.GetValueOrDefault("text") as string)));
        }
    }

    // This table is the browse-by-detail surface: the grids play, this one inspects.
    [Fact]
    public void RowsNavigateToTheStationDetailPage()
    {
        PluginView view = AllStationsView.Build(Catalog(Station("a", "Alpha FM")));

        PluginComponent row = Rows(view).Should().ContainSingle().Subject;

        row.Action!.Type.Should().Be(PluginActionType.Navigate);
        row.Action.Payload["route"].Should().Be(RadioRoutes.Station("a"));
    }

    [Fact]
    public void ListsEveryStationSortedByName()
    {
        PluginView view = AllStationsView.Build(
            Catalog(Station("b", "Zulu FM"), Station("a", "Alpha FM")));

        Rows(view).Select(row => CellText(row, "name")).Should().Equal("Alpha FM", "Zulu FM");
    }

    // radio-browser reports 0 for "unknown", which the model stores as null. Rendering
    // that as "0 kbps" would claim a silent stream.
    [Fact]
    public void ShowsAnUnknownBitrateAsAnEmDashRatherThanZero()
    {
        RadioStation unknown = Station("a", "Alpha FM") with { BitrateKbps = null };

        PluginComponent row = Rows(AllStationsView.Build(Catalog(unknown))).Single();

        CellText(row, "bitrate").Should().Be("—");
    }

    [Fact]
    public void OffersAWayBack()
    {
        PluginView view = AllStationsView.Build(Catalog(Station("a", "Alpha FM")));

        AllNodes(view).Should().Contain(node =>
            node.Action != null
            && node.Action.Type == PluginActionType.Navigate
            && (string)node.Action.Payload["route"]! == RadioRoutes.Browse);
    }

    [Fact]
    public void ExplainsItselfWhenThereAreNoStations()
    {
        PluginView view = AllStationsView.Build(StationCatalog.Empty());

        AllNodes(view).Should().Contain(node => node.Component == PluginComponentType.EmptyState);
    }

    [Fact]
    public void EveryNodeHasAUniqueId()
    {
        PluginView view = AllStationsView.Build(
            Catalog(Station("a", "Alpha FM"), Station("b", "Bravo FM")));

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }
}
