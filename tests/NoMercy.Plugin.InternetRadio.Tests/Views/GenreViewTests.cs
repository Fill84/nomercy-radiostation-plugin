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
        PluginView view = GenreView.Build(
            Catalog(Station("a", "Ambient"), Station("b", "Rock")), "ambient");

        AllNodes(view).Where(node => node.Component == PluginComponentType.Card)
            .Should().ContainSingle()
            .Which.Action!.Payload["title"].Should().Be("Station a");
    }

    [Fact]
    public void CardsPlayImmediately()
    {
        PluginView view = GenreView.Build(Catalog(Station("a", "Ambient")), "ambient");

        AllNodes(view).Single(node => node.Component == PluginComponentType.Card)
            .Action!.Type.Should().Be(PluginActionType.PlayMedia);
    }

    [Fact]
    public void OffersAWayBack()
    {
        PluginView view = GenreView.Build(Catalog(Station("a", "Ambient")), "ambient");

        AllNodes(view).Should().Contain(node =>
            node.Action != null
            && node.Action.Type == PluginActionType.Navigate
            && (string)node.Action.Payload["route"]! == RadioRoutes.Browse);
    }

    // A stale bookmark is not a failure worth reporting as one.
    [Fact]
    public void ShowsAnEmptyStateForAGenreThatDoesNotExist()
    {
        PluginView view = GenreView.Build(Catalog(Station("a", "Ambient")), "no-such-genre");

        AllNodes(view).Should().Contain(node => node.Component == PluginComponentType.EmptyState);
        AllNodes(view).Should().NotContain(node => node.Component == PluginComponentType.Card);
    }

    [Fact]
    public void EveryNodeHasAUniqueId()
    {
        PluginView view = GenreView.Build(
            Catalog(Station("a", "Ambient"), Station("b", "Ambient")), "ambient");

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }
}
