// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class GenreViewTests
{
    private static RadioStation Station(string id, string genre) =>
        new() { Id = id, Name = $"Station {id}", StreamUrl = $"https://example.com/{id}", Genre = genre };

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

    [Fact]
    public void ShowsOnlyThatGenresStations()
    {
        PluginView view = GenreView.Build(Catalog(Station("a", "Ambient"), Station("b", "Rock")), "ambient", UserState.Empty);

        AllNodes(view).Single(node => node.Id == "genre-grid")
            .Items.Select(tile => tile.Id)
            .Should().BeEquivalentTo(["station-tile-genre-a"]);
    }

    [Fact]
    public void CardsOpenTheStation()
    {
        PluginView view = GenreView.Build(Catalog(Station("a", "Ambient")), "ambient", UserState.Empty);

        PluginComponent card = AllNodes(view).Single(node => node.Id == "station-tile-genre-a");

        ((Dictionary<string, object?>)card.Props["data"]!)["link"]
            .Should().Be(AppRoutes.Station("a"));
    }

    [Fact]
    public void OffersAWayBack()
    {
        PluginView view = GenreView.Build(Catalog(Station("a", "Ambient")), "ambient", UserState.Empty);

        AllNodes(view).Should().Contain(node =>
            node.Action != null
            && node.Action.Type == PluginActionType.Navigate
            && (string)node.Action.Payload["route"]! == RadioRoutes.Browse);
    }

    // A stale bookmark is not a failure worth reporting as one.
    [Fact]
    public void ShowsAnEmptyStateForAGenreThatDoesNotExist()
    {
        PluginView view = GenreView.Build(Catalog(Station("a", "Ambient")), "no-such-genre", UserState.Empty);

        AllNodes(view).Should().Contain(node => node.Component == Ui.EmptyStateComponent);
        AllNodes(view).Should().NotContain(node => node.Id.StartsWith("station-play-", StringComparison.Ordinal));
    }

    // A catalogue-level id collision (a real possibility - see StationCatalog) has
    // to be caught here too, since ByGenreSlug would otherwise hand the view two
    // stations that both produce "station-card-a".
    [Fact]
    public void EveryNodeHasAUniqueId()
    {
        RadioStation duplicate = new()
        {
            Id = "a",
            Name = "Station A Duplicate",
            StreamUrl = "https://example.com/a-duplicate",
            Genre = "Ambient",
        };

        PluginView view = GenreView.Build(Catalog(Station("a", "Ambient"), Station("b", "Ambient"), duplicate), "ambient", UserState.Empty);

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }
}
