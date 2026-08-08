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

    // A card is a RouterLink to the station, exactly as an artist card is. Playing,
    // queueing and keeping all live on the page it opens, which is where the app puts them
    // for its own media too.
    [Fact]
    public void CardsOpenTheStation()
    {
        PluginView view = BrowseView.Build(Catalog(Station("a")), UserState.Empty);

        PluginComponent card = AllNodes(view)
            .Should().ContainSingle(node => node.Id == "station-tile-popular-a").Subject;

        ((Dictionary<string, object?>)card.Props["data"]!)["link"]
            .Should().Be(AppRoutes.Station("a"));
    }

    [Fact]
    public void GenreButtonsNavigateToTheirGenreRoute()
    {
        PluginView view = BrowseView.Build(Catalog(Station("a", "Ambient"), Station("b", "Rock")), UserState.Empty);

        IEnumerable<PluginComponent> buttons = AllNodes(view)
            .Where(node => node.Component == Ui.ButtonComponent
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

        PluginComponent grid = AllNodes(view).Single(node => node.Id == "browse-popular-grid");

        grid.Items[0].Id.Should().Be("station-tile-popular-loud");
    }

    // An empty catalogue has to explain itself. A blank grid reads as a broken plugin.
    [Fact]
    public void ExplainsItselfWhenThereAreNoStations()
    {
        PluginView view = BrowseView.Build(StationCatalog.Empty(lastFetchFailed: true), UserState.Empty);

        AllNodes(view).Should().Contain(node => node.Component == Ui.EmptyStateComponent);
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
            .Where(node => node.Component == Ui.TextComponent)
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

    // High on the page. Since the curated list went, search is how all but a handful of
    // the fifty thousand stations in radio-browser are reachable at all.
    [Fact]
    public void PutsTheSearchBoxAboveTheGenreChips()
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

    // A station kept in the favourites row and shown again in the grid below it is the
    // same station, drawn the same way, linking to the same page.
    [Fact]
    public void ShowsTheSameStationTheSameWayEverywhereItAppears()
    {
        RadioStation station = Station("a");

        IEnumerable<object?> links = AllNodes(BrowseView.Build(Catalog(station), With(station)))
            .Where(node => node.Id.StartsWith("station-tile-", StringComparison.Ordinal))
            .Select(node => ((Dictionary<string, object?>)node.Props["data"]!)["link"]);

        links.Should().HaveCountGreaterThan(1).And.AllBeEquivalentTo(AppRoutes.Station("a"));
    }

    // A favourite the sweep no longer returns still has to render - that is the whole
    // reason the record is stored rather than the id.
    [Fact]
    public void ShowsAFavouriteThatIsNoLongerInTheCatalogue()
    {
        PluginView view = BrowseView.Build(Catalog(Station("still-here")), With(Station("gone")));

        AllNodes(view).Select(node => node.Id).Should().Contain("station-tile-fav-gone");
    }

    // Searching is a navigation, not a form: the term is spelled into the route, because
    // a submitted form posts an empty body in this client.
    [Fact]
    // The box itself, on the page you land on - not a button leading to one. And the
    // answer appears here, because after a submit the client refreshes the route it is
    // already on: answering elsewhere would put the results on a page nothing takes you to.
    public void OffersTheSearchBoxItselfAndAnswersOnTheSamePage()
    {
        PluginComponent form = AllNodes(BrowseView.Build(Catalog(Station("a")), UserState.Empty))
            .Single(node => node.Id == "search-form");

        form.Component.Should().Be(Ui.FormComponent);
        form.Action!.Payload["method"].Should().Be(InternetRadioController.SearchMethod);

        PluginView answered = BrowseView.Build(
            Catalog(Station("a")),
            new UserState { LastSearch = "groove" },
            [Station("found")]);

        AllNodes(answered).Select(node => node.Id)
            .Should().Contain("search-grid").And.Contain("station-tile-search-found");
    }
}
