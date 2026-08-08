// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

// This page used to be a table of names and bitrates, and these tests were about columns
// and cells. It is the same tiles as every other screen now: recognising a station by its
// logo beats reading its name out of a row, and one screen behaving unlike all the others
// was the real cost of the old split.
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

    private static PluginView Build(params RadioStation[] stations) =>
        AllStationsView.Build(Catalog(stations), UserState.Empty);

    private static IEnumerable<PluginComponent> AllNodes(PluginView view) =>
        PluginNodes.All(view);

    private static PluginComponent Grid(PluginView view) =>
        AllNodes(view).Single(node => node.Id == "all-grid");

    [Fact]
    public void ListsEveryStationSortedByName()
    {
        PluginView view = Build(Station("c", "Charlie FM"), Station("a", "Alpha FM"), Station("b", "Bravo FM"));

        Grid(view).Items.Select(tile => tile.Id)
            .Should().Equal("station-tile-all-a", "station-tile-all-b", "station-tile-all-c");
    }

    // The same tile as every other screen, so a station cannot behave one way here and
    // another in a genre grid.
    [Fact]
    public void DrawsTheSameTilesAsEveryOtherScreen()
    {
        PluginComponent tile = Grid(Build(Station("a", "Alpha FM"))).Items.Single();

        tile.Should().BeEquivalentTo(
            StationCards.Tile(Station("a", "Alpha FM"), isFavourite: false, "all"),
            options => options.Excluding(node => node.Type == typeof(PluginActionIntent)));
    }

    [Fact]
    public void OneClickOpensTheStation()
    {
        PluginComponent card = Grid(Build(Station("a", "Alpha FM"))).Items.Single().Items[0];

        card.Action!.Type.Should().Be(PluginActionType.Navigate);
        card.Action.Payload["route"].Should().Be(RadioRoutes.Station("a"));
    }

    [Fact]
    public void SaysHowManyThereAre()
    {
        AllNodes(Build(Station("a", "Alpha FM"), Station("b", "Bravo FM")))
            .Single(node => node.Id == "all-count")
            .Props["value"]!.ToString().Should().Contain("2");
    }

    // An empty catalogue has to explain itself. A blank page reads as a broken plugin.
    [Fact]
    public void ExplainsItselfWhenThereAreNoStations()
    {
        PluginView view = AllStationsView.Build(StationCatalog.Empty(), UserState.Empty);

        AllNodes(view).Should().Contain(node => node.Component == Ui.EmptyStateComponent);
    }

    [Fact]
    public void OffersAWayBack()
    {
        AllNodes(Build(Station("a", "Alpha FM")))
            .Single(node => node.Id == "all-back")
            .Action!.Payload["route"].Should().Be(RadioRoutes.Browse);
    }
}
