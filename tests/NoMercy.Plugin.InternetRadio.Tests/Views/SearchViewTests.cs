// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

// Asserted through BrowseView, because that is where a search is actually answered. The
// previous version of these tests called SearchView.Build directly and passed on a page
// no sequence of clicks could reach: the form stored the term, the client refreshed the
// route it was already on, and the results were on a different one. Testing the section in
// isolation could never have caught that - only testing the page the field lives on can.
public class SearchViewTests
{
    private static RadioStation Station(string id, string genre = "Ambient") =>
        new()
        {
            Id = id,
            Name = $"Station {id}",
            StreamUrl = $"https://example.com/{id}",
            Genre = genre,
            Popularity = 1,
        };

    private static StationCatalog Catalog() =>
        StationCatalog.Create([Station("in-catalogue")], CatalogSource.Fetched, DateTimeOffset.UtcNow);

    private static UserState Searching(string? term) => new() { LastSearch = term };

    private static IEnumerable<string> Ids(PluginView view) =>
        PluginNodes.All(view).Select(node => node.Id);

    // The field is on the landing page, and it is where the answer appears.
    [Fact]
    public void TheFieldIsOnTheLandingPage()
    {
        Ids(BrowseView.Build(Catalog(), UserState.Empty)).Should().Contain("search-form");
    }

    [Fact]
    public void WithNoTerm_ShowsNoResultsSectionAtAll()
    {
        IEnumerable<string> ids = Ids(BrowseView.Build(Catalog(), UserState.Empty));

        ids.Should().NotContain("search-results-heading")
            .And.NotContain("search-empty")
            .And.NotContain("search-failed");
    }

    // The bug this whole change exists for: a term produces results on the same page.
    [Fact]
    public void WithResults_RendersThemOnTheSamePageAsTheField()
    {
        PluginView view = BrowseView.Build(
            Catalog(), Searching("groove"), [Station("found-a"), Station("found-b")]);

        Ids(view).Should().Contain("search-form")
            .And.Contain("search-results-heading")
            .And.Contain("station-card-search-found-a")
            .And.Contain("station-card-search-found-b");
    }

    [Fact]
    public void WithATermAndNoResults_SaysNothingMatched()
    {
        Ids(BrowseView.Build(Catalog(), Searching("nothing"), []))
            .Should().Contain("search-empty");
    }

    // "We could not reach radio-browser" and "there is no such station" ask the viewer to
    // do different things. Reporting an outage as an empty result set has them retrying
    // the search that was never the problem.
    [Fact]
    public void WhenTheQueryFailed_SaysSoInsteadOfClaimingNoResults()
    {
        IEnumerable<string> ids =
            Ids(BrowseView.Build(Catalog(), Searching("anything"), [], searchFailed: true));

        ids.Should().Contain("search-failed").And.NotContain("search-empty");
    }

    // Two grids of unrelated stations under one field is a page where it is not clear
    // which one answered you.
    [Fact]
    public void WhileSearching_PopularStepsAside()
    {
        Ids(BrowseView.Build(Catalog(), Searching("groove"), [Station("found")]))
            .Should().NotContain("browse-popular-grid");
    }

    [Fact]
    public void WithNoSearch_PopularIsBack()
    {
        Ids(BrowseView.Build(Catalog(), UserState.Empty)).Should().Contain("browse-popular-grid");
    }

    // A search you cannot get out of is a landing page you have lost.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EverySearchStateOffersAWayToClearIt(bool failed)
    {
        Ids(BrowseView.Build(Catalog(), Searching("x"), [], searchFailed: failed))
            .Should().Contain("search-clear");
    }

    [Fact]
    public void ClearingGoesThroughItsOwnEndpoint()
    {
        PluginComponent clear = PluginNodes
            .All(BrowseView.Build(Catalog(), Searching("x"), [Station("a")]))
            .Single(node => node.Id == "search-clear");

        clear.Action!.Type.Should().Be(PluginActionType.CallPlugin);
        clear.Action.Payload["method"].Should().Be(InternetRadioController.ClearSearchMethod);
    }

    // A submitted query that vanishes from the box reads as a search that was lost.
    [Fact]
    public void TheFieldKeepsTheTerm()
    {
        PluginNodes.All(BrowseView.Build(Catalog(), Searching("groove salad"), []))
            .Single(node => node.Id == "search-form-query")
            .Props["value"].Should().Be("groove salad");
    }

    // A result is the same tile as any grid, so a station cannot behave one way when
    // browsed and another when searched for.
    [Fact]
    public void ResultsCarryTheFavouriteToggleLikeEveryOtherTile()
    {
        Ids(BrowseView.Build(Catalog(), Searching("x"), [Station("found")]))
            .Should().Contain("station-favourite-search-found");
    }

    // An empty string, never null. The submitted body came back as "{}" with a null
    // initial value - no fields at all - and a null most likely never enters the client's
    // form model, so the submit has nothing to collect. Every field in the torrent
    // plugin's working form carries a string and is Required.
    [Fact]
    public void TheFieldIsNeverNullValuedAndIsRequired()
    {
        PluginComponent input = PluginNodes.All(BrowseView.Build(Catalog(), UserState.Empty))
            .Single(node => node.Id == "search-form-query");

        input.Props["value"].Should().Be(string.Empty);
        input.Props["required"].Should().Be(true);
    }
}
