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

    // A row supplies its cells by column key, so a column the rows never fill renders
    // as a blank stripe down the table.
    [Fact]
    public void EveryColumnIsFilledByEveryRow()
    {
        PluginView view = AllStationsView.Build(Catalog(Station("a", "Alpha FM")));

        PluginComponent table = PluginNodes.Table(view);

        // A column no row fills still renders its cell, as empty text: that is exactly
        // the blank stripe this test exists to catch, so an assertion that the cell
        // merely exists would no longer catch anything.
        foreach (PluginComponent row in PluginNodes.Rows(table))
        {
            row.Items.Should().HaveSameCount(PluginNodes.HeaderRow(table).Items);

            foreach (string column in PluginNodes.Columns(table))
            {
                PluginNodes.Value(table, row, column).Should().NotBeEmpty();
            }
        }
    }

    // This table is the browse-by-detail surface: the grids play, this one inspects.
    [Fact]
    public void RowsNavigateToTheStationDetailPage()
    {
        PluginView view = AllStationsView.Build(Catalog(Station("a", "Alpha FM")));

        PluginComponent row = PluginNodes.Rows(PluginNodes.Table(view))
            .Should().ContainSingle().Subject;

        row.Action!.Type.Should().Be(PluginActionType.Navigate);
        row.Action.Payload["route"].Should().Be(RadioRoutes.Station("a"));
    }

    [Fact]
    public void ListsEveryStationSortedByName()
    {
        PluginView view = AllStationsView.Build(
            Catalog(Station("b", "Zulu FM"), Station("a", "Alpha FM")));

        PluginComponent table = PluginNodes.Table(view);

        PluginNodes.Rows(table)
            .Select(row => PluginNodes.Value(table, row, "Station"))
            .Should().Equal("Alpha FM", "Zulu FM");
    }

    // radio-browser reports 0 for "unknown", which the model stores as null. Rendering
    // that as "0 kbps" would claim a silent stream.
    [Fact]
    public void ShowsAnUnknownBitrateAsAnEmDashRatherThanZero()
    {
        RadioStation unknown = Station("a", "Alpha FM") with { BitrateKbps = null };

        PluginComponent table = PluginNodes.Table(AllStationsView.Build(Catalog(unknown)));

        PluginNodes.Value(table, PluginNodes.Rows(table).Single(), "Bitrate").Should().Be("—");
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
