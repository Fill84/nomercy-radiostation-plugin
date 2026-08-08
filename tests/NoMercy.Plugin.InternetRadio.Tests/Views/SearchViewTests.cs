// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class SearchViewTests
{
    private static RadioStation Station(string id) =>
        new() { Id = id, Name = $"Station {id}", StreamUrl = $"https://example.com/{id}" };

    private static IEnumerable<string> Ids(PluginView view) =>
        PluginNodes.All(view).Select(node => node.Id);

    // The three empty states must be distinguishable. "We could not reach radio-browser"
    // and "there is no such station" ask the user to do different things, and one shared
    // "nothing found" has them retrying the search that was never the problem.
    [Fact]
    public void Build_WithNoTerm_InvitesASearch()
    {
        Ids(SearchView.Build(null, [], queryFailed: false)).Should().Contain("search-idle");
    }

    [Fact]
    public void Build_WithATermAndNoResults_SaysNothingMatched()
    {
        Ids(SearchView.Build("nothing", [], queryFailed: false)).Should().Contain("search-empty");
    }

    [Fact]
    public void Build_WhenTheQueryFailed_SaysSoInsteadOfClaimingNoResults()
    {
        IEnumerable<string> ids = Ids(SearchView.Build("anything", [], queryFailed: true));

        ids.Should().Contain("search-failed").And.NotContain("search-empty");
    }

    // A failed query outranks an empty term: an outage is worth reporting even when
    // there was nothing to search for, and silently showing the idle state would hide it.
    [Fact]
    public void Build_ReportsAFailureEvenWithNoTerm()
    {
        Ids(SearchView.Build(null, [], queryFailed: true)).Should().Contain("search-failed");
    }

    // A submitted query that vanishes from the box reads as a search that was lost.
    // Asserted on the input's own value rather than on any text on the page, because the
    // "nothing found" message quotes the term too - and would pass this on its own.
    [Fact]
    public void Build_KeepsTheTermInTheField()
    {
        PluginComponent input = PluginNodes.All(SearchView.Build("groove salad", [], false))
            .Single(node => node.Id == "search-form-query");

        input.Props["value"].Should().Be("groove salad");
    }

    [Fact]
    public void Build_LeavesTheFieldEmptyWhenNothingWasSearchedFor()
    {
        PluginComponent input = PluginNodes.All(SearchView.Build(null, [], false))
            .Single(node => node.Id == "search-form-query");

        input.Props.TryGetValue("value", out object? value);
        value.Should().BeNull();
    }

    [Fact]
    public void Build_RendersEachResultAsACard()
    {
        PluginView view = SearchView.Build("x", [Station("a"), Station("b")], false);

        Ids(view).Should().Contain("station-card-a").And.Contain("station-card-b");
    }

    // Results are the same card the grids use, so a station cannot behave one way when
    // browsed and another when searched for.
    [Fact]
    public void Build_ResultsPlayOnClickLikeEveryOtherGrid()
    {
        PluginComponent card = PluginNodes.All(SearchView.Build("x", [Station("a")], false))
            .Single(node => node.Id == "station-card-a");

        card.Props.Should().ContainKey("action");
    }

    // The way back, on every state. A search page with no exit is a dead end when the
    // query finds nothing.
    [Theory]
    [InlineData(null, false)]
    [InlineData("term", false)]
    [InlineData("term", true)]
    public void Build_AlwaysOffersTheWayBack(string? term, bool failed)
    {
        Ids(SearchView.Build(term, [], failed)).Should().Contain("search-back");
    }
}
