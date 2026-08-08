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

    // The whole point of the plugin: one click and it is playing.
    [Fact]
    public void CardsPlayTheStationRatherThanNavigatingToIt()
    {
        PluginView view = BrowseView.Build(Catalog(Station("a")), UserState.Empty);

        // By id, not "the only card on the page": the search field is a form, and a form
        // draws as NMCard too, so IsCard alone now matches it as well.
        PluginComponent card = AllNodes(view)
            .Should().ContainSingle(node => node.Id == "station-card-popular-a").Subject;

        card.Action!.Type.Should().Be(PluginActionType.PlayMedia);
        card.Action.Payload["streamUrl"].Should().Be("https://example.com/a");
        card.Action.Payload["title"].Should().Be("Station a");
        card.Action.Payload["cover"].Should().Be("https://example.com/logo.png");
    }

    [Fact]
    public void GenreButtonsNavigateToTheirGenreRoute()
    {
        PluginView view = BrowseView.Build(Catalog(Station("a", "Ambient"), Station("b", "Rock")), UserState.Empty);

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
        PluginView view = BrowseView.Build(Catalog(Station("a", "Ambient")), UserState.Empty);

        AllNodes(view).Select(node => node.Action?.Payload.GetValueOrDefault("route"))
            .Should().NotContain(RadioRoutes.Genre("jazz"));
    }

    [Fact]
    public void ShowsTheMostPopularStationsFirst()
    {
        PluginView view = BrowseView.Build(
            Catalog(Station("quiet", "Ambient", 1), Station("loud", "Rock", 99)), UserState.Empty);

        AllNodes(view).Where(node => node.Id.StartsWith("station-card-", StringComparison.Ordinal))
            .First().Action!.Payload["title"].Should().Be("Station loud");
    }

    // An empty catalogue has to explain itself. A blank grid reads as a broken plugin.
    [Fact]
    public void ExplainsItselfWhenThereAreNoStations()
    {
        PluginView view = BrowseView.Build(StationCatalog.Empty(lastFetchFailed: true), UserState.Empty);

        AllNodes(view).Should().Contain(node => node.Component == PluginComponentType.EmptyState);
    }

    [Fact]
    public void OffersARetryWhenThereAreNoStations()
    {
        PluginView view = BrowseView.Build(StationCatalog.Empty(lastFetchFailed: true), UserState.Empty);

        AllNodes(view).Should().Contain(node =>
            node.Action != null && node.Action.Type == PluginActionType.RefreshView);
    }

    // The renderer only knows title/subtitle/caption; anything else silently reads as
    // body text, which is how the torrent plugin lost its section headings.
    [Fact]
    public void UsesOnlyTextVariantsTheRendererKnows()
    {
        PluginView view = BrowseView.Build(Catalog(Station("a")), UserState.Empty);

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
            Catalog(Station("a", "Ambient"), Station("b", "Rock"), Station("c", "Jazz"), duplicate), UserState.Empty);

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }

    // Static content. A poll interval here is every open tab re-fetching for nothing.
    [Fact]
    public void DoesNotAskTheClientToPoll()
    {
        BrowseView.Build(Catalog(Station("a")), UserState.Empty).RefreshInterval.Should().Be(0);
    }

    private static UserState With(params RadioStation[] favourites) =>
        new() { Favourites = favourites };

    // You asked for easy searching, and easy means on the screen you land on - not
    // behind a click. Since the curated list went, search is how most of the database
    // is reachable at all.
    [Fact]
    public void PutsTheSearchFieldAboveTheGenreChips()
    {
        List<string> ids = [.. AllNodes(BrowseView.Build(Catalog(Station("a")), UserState.Empty))
            .Select(node => node.Id)];

        ids.Should().Contain("search-form");
        ids.IndexOf("search-form").Should().BeLessThan(ids.IndexOf("browse-genres"));
    }

    [Fact]
    public void ShowsFavouritesBeforeTheGenreChips()
    {
        List<string> ids = [.. AllNodes(BrowseView.Build(Catalog(Station("a")), With(Station("a"))))
            .Select(node => node.Id)];

        ids.Should().Contain("browse-favourites");
        ids.IndexOf("browse-favourites").Should().BeLessThan(ids.IndexOf("browse-genres"));
    }

    // Absent, not empty. A heading over nothing reads as a screen that failed to load,
    // and everybody's first visit here has no favourites at all.
    [Fact]
    public void OmitsTheFavouritesSectionEntirelyWhenThereAreNone()
    {
        AllNodes(BrowseView.Build(Catalog(Station("a")), UserState.Empty))
            .Select(node => node.Id)
            .Should().NotContain("browse-favourites")
            .And.NotContain("browse-favourites-heading");
    }

    // The same station cannot read two ways on one screen: kept in the favourites row
    // and not-kept in the grid below it is the bug this catches.
    [Fact]
    public void ShowsAFavouritedStationAsFavouritedEverywhereItAppears()
    {
        RadioStation station = Station("a");

        IEnumerable<string?> labels = AllNodes(BrowseView.Build(Catalog(station), With(station)))
            .Where(node => node.Id.StartsWith("station-favourite-", StringComparison.Ordinal)
                && node.Id.EndsWith("-a-label", StringComparison.Ordinal))
            .Select(node => node.Props["text"]?.ToString());

        labels.Should().NotBeEmpty().And.AllBe(StationCards.RemoveFavouriteLabel);
    }

    // A favourite the sweep no longer returns still has to render - that is the whole
    // reason the record is stored rather than the id.
    [Fact]
    public void ShowsAFavouriteThatIsNoLongerInTheCatalogue()
    {
        PluginView view = BrowseView.Build(Catalog(Station("still-here")), With(Station("gone")));

        AllNodes(view).Select(node => node.Id).Should().Contain("station-card-fav-gone");
    }

    // The field carries the term back, so returning to the landing page does not look
    // like the search was thrown away.
    [Fact]
    public void KeepsTheLastSearchTermInTheField()
    {
        UserState state = new() { LastSearch = "groove salad" };

        AllNodes(BrowseView.Build(Catalog(Station("a")), state))
            .Single(node => node.Id == "search-form-query")
            .Props["value"].Should().Be("groove salad");
    }
}
