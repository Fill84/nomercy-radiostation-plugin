// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class BrowseViewTests
{
    private static RadioStation Station(string id, string genre = "Ambient", int popularity = 1) =>
        new()
        {
            Id = id,
            Name = $"Station {id}",
            StreamUrl = $"https://example.com/{id}",
            LogoUrl = "https://example.com/logo.png",
            Genre = genre,
            Country = "NL",
            Popularity = popularity,
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
    // PluginComponentType.Card now equals PluginComponentType.Container and selecting
    // by type returns every container on the page - including ones with no Action,
    // which is what turned "the first card" into a null dereference.
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

    // The whole point of the plugin: one click and it is playing.
    [Fact]
    public void CardsPlayTheStationRatherThanNavigatingToIt()
    {
        PluginView view = BrowseView.Build(Catalog(Station("a")));

        PluginComponent card = Cards(view).Should().ContainSingle().Subject;

        card.Action!.Type.Should().Be(PluginActionType.PlayMedia);
        card.Action.Payload["streamUrl"].Should().Be("https://example.com/a");
        card.Action.Payload["title"].Should().Be("Station a");
        card.Action.Payload["cover"].Should().Be("https://example.com/logo.png");
    }

    [Fact]
    public void GenreButtonsNavigateToTheirGenreRoute()
    {
        PluginView view = BrowseView.Build(Catalog(Station("a", "Ambient"), Station("b", "Rock")));

        IEnumerable<PluginComponent> buttons = AllNodes(view)
            .Where(node => node.Component == PluginComponentType.Button
                && node.Action?.Type == PluginActionType.Navigate);

        buttons.Select(button => button.Action!.Payload["route"])
            .Should().Contain(RadioRoutes.Genre("ambient"))
            .And.Contain(RadioRoutes.Genre("rock"))
            .And.Contain(RadioRoutes.AllStations);
    }

    [Fact]
    public void OffersNoChipForAGenreWithNoStations()
    {
        PluginView view = BrowseView.Build(Catalog(Station("a", "Ambient")));

        AllNodes(view).Select(node => node.Action?.Payload.GetValueOrDefault("route"))
            .Should().NotContain(RadioRoutes.Genre("jazz"));
    }

    [Fact]
    public void ShowsTheMostPopularStationsFirst()
    {
        PluginView view = BrowseView.Build(
            Catalog(Station("quiet", "Ambient", 1), Station("loud", "Rock", 99)));

        Cards(view).First().Action!.Payload["title"].Should().Be("Station loud");
    }

    // An empty catalogue has to explain itself. A blank grid reads as a broken plugin.
    [Fact]
    public void ExplainsItselfWhenThereAreNoStations()
    {
        PluginView view = BrowseView.Build(StationCatalog.Empty(lastFetchFailed: true));

        AllNodes(view).Should().Contain(node => node.Component == PluginComponentType.EmptyState);
    }

    [Fact]
    public void OffersARetryWhenThereAreNoStations()
    {
        PluginView view = BrowseView.Build(StationCatalog.Empty(lastFetchFailed: true));

        AllNodes(view).Should().Contain(node =>
            node.Action != null && node.Action.Type == PluginActionType.RefreshView);
    }

    // The renderer only knows title/subtitle/caption; anything else silently reads as
    // body text, which is how the torrent plugin lost its section headings.
    [Fact]
    public void UsesOnlyTextVariantsTheRendererKnows()
    {
        PluginView view = BrowseView.Build(Catalog(Station("a")));

        AllNodes(view)
            .Where(node => node.Component == PluginComponentType.Text)
            .Select(node => node.Props.GetValueOrDefault("variant") as string)
            .Should().OnlyContain(variant =>
                variant == null || variant == "title" || variant == "subtitle" || variant == "caption");
    }

    // Two nodes with the same id make the client's keyed render ambiguous. A
    // catalogue-level id collision (a real possibility - see StationCatalog) has to
    // be caught here too, since Popular() would otherwise hand the view two stations
    // that both produce "station-card-a".
    [Fact]
    public void EveryNodeHasAUniqueId()
    {
        RadioStation duplicate = new()
        {
            Id = "a",
            Name = "Station A Duplicate",
            StreamUrl = "https://example.com/a-duplicate",
            LogoUrl = "https://example.com/logo.png",
            Genre = "Ambient",
            Country = "NL",
            Popularity = 1,
        };

        PluginView view = BrowseView.Build(
            Catalog(Station("a", "Ambient"), Station("b", "Rock"), Station("c", "Jazz"), duplicate));

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }

    // Static content. A poll interval here is every open tab re-fetching for nothing.
    [Fact]
    public void DoesNotAskTheClientToPoll()
    {
        BrowseView.Build(Catalog(Station("a"))).RefreshInterval.Should().Be(0);
    }
}
