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

    // By id, not by component type. The design system collapsed Container, List, Row,
    // Grid, Card, Detail, Form and Table onto the single NMCard component, so
    // PluginComponentType.Card now equals PluginComponentType.Container and selecting
    // by type returns every container on the page. That matters most for the empty
    // genre below: asserting "no Card" by type would fail on the page's own layout.
    // Stops at the card rather than filtering the flattened tree: a card builds its
    // face from children whose ids extend its own ("-art", "-heading", "-title"), so a
    // plain prefix match over every node counts one card four times.
    private static List<PluginComponent> Cards(PluginView view)
    {
        List<PluginComponent> cards = [];

        void Walk(PluginComponent node)
        {
            if (node.Id.StartsWith("station-card-", StringComparison.Ordinal))
            {
                cards.Add(node);
                return;
            }

            foreach (PluginComponent child in node.Items)
            {
                Walk(child);
            }
        }

        foreach (PluginComponent root in view.Components ?? [])
        {
            Walk(root);
        }

        return cards;
    }

    [Fact]
    public void ShowsOnlyThatGenresStations()
    {
        PluginView view = GenreView.Build(
            Catalog(Station("a", "Ambient"), Station("b", "Rock")), "ambient");

        Cards(view).Should().ContainSingle()
            .Which.Action!.Payload["title"].Should().Be("Station a");
    }

    [Fact]
    public void CardsPlayImmediately()
    {
        PluginView view = GenreView.Build(Catalog(Station("a", "Ambient")), "ambient");

        Cards(view).Single().Action!.Type.Should().Be(PluginActionType.PlayMedia);
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
        Cards(view).Should().BeEmpty();
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

        PluginView view = GenreView.Build(
            Catalog(Station("a", "Ambient"), Station("b", "Ambient"), duplicate), "ambient");

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }
}
