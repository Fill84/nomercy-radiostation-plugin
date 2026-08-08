// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

// Searching is a box you type into. It only ever failed for one reason, and it was the
// component name: PluginComponentType.Form is "NMCard", so the form went out as a
// design-system card and the real PluginForm - which renders a <form>, collects its fields
// and posts them - was never reached. Every assertion about the field here is really an
// assertion that it is named the way the client names it.
public class SearchViewTests
{
    private static RadioStation Station(string id) =>
        new()
        {
            Id = id,
            Name = $"Station {id}",
            StreamUrl = $"https://example.com/{id}",
            Genre = "Ambient",
        };

    private static PluginView View(
        string term,
        IReadOnlyList<RadioStation>? results = null,
        bool failed = false,
        UserState? state = null) =>
        SearchView.Build(term, results ?? [], failed, state ?? UserState.Empty);

    private static IEnumerable<string> Ids(PluginView view) =>
        PluginNodes.All(view).Select(node => node.Id);

    private static PluginComponent Node(PluginView view, string id) =>
        PluginNodes.All(view).Single(node => node.Id == id);

    private static PluginFormField TheField(PluginView view) =>
        ((PluginFormField[])Node(view, "search-form").Props["fields"]!).Single();

    [Fact]
    public void TheFormIsTheOneTheClientCanActuallySubmit()
    {
        Node(View(string.Empty), "search-form").Component.Should().Be(Ui.FormComponent);
    }

    [Fact]
    public void TheFieldIsThereBeforeAnythingHasBeenSearchedFor()
    {
        PluginFormField field = TheField(View(string.Empty));

        field.Name.Should().Be(SearchView.FieldName);
        field.Type.Should().Be(PluginFormFieldType.Text);
    }

    // A submitted term that vanishes from the box reads as a search that was lost, and
    // correcting a typo should mean editing rather than retyping.
    [Fact]
    public void TheFieldHoldsWhateverIsBeingSearchedFor()
    {
        TheField(View("tomorrowland")).Value.Should().Be("tomorrowland");
    }

    [Fact]
    public void SubmittingGoesToTheControllersOwnRoute()
    {
        PluginActionIntent action = Node(View(string.Empty), "search-form").Action!;

        action.Type.Should().Be(PluginActionType.CallPlugin);
        action.Payload["method"].Should().Be(InternetRadioController.SearchMethod);
    }

    [Fact]
    public void WithNoTerm_ShowsNoResultsSectionAtAll()
    {
        Ids(View(string.Empty))
            .Should().NotContain("search-results-heading")
            .And.NotContain("search-empty")
            .And.NotContain("search-failed");
    }

    [Fact]
    public void ResultsAreDrawnAsAGridOfStations()
    {
        Ids(View("tom", [Station("found-a"), Station("found-b")]))
            .Should().Contain("search-grid")
            .And.Contain("station-tile-search-found-a")
            .And.Contain("station-tile-search-found-b");
    }

    [Fact]
    public void WithATermAndNoResults_SaysNothingMatched()
    {
        Ids(View("nothing")).Should().Contain("search-empty");
    }

    // "We could not reach radio-browser" and "there is no such station" ask the viewer to
    // do different things. Reporting an outage as an empty result set has them retyping the
    // search that was never the problem.
    [Fact]
    public void WhenTheQueryFailed_SaysSoInsteadOfClaimingNoResults()
    {
        Ids(View("anything", failed: true))
            .Should().Contain("search-failed").And.NotContain("search-empty");
    }

    // The box survives every state, or a failed search is a screen with no way to try a
    // different one.
    [Theory]
    [InlineData("", false)]
    [InlineData("tom", false)]
    [InlineData("tom", true)]
    public void TheBoxSurvivesEveryState(string term, bool failed)
    {
        Ids(View(term, failed: failed)).Should().Contain("search-form");
    }

    [Fact]
    public void ThereIsAWayBackToTheLandingPage()
    {
        Node(View("tom"), "search-back").Action!.Payload["route"].Should().Be(RadioRoutes.Browse);
    }

    // A result is the same tile as any grid, so a station cannot behave one way when
    // browsed and another when searched for.
    [Fact]
    public void AResultKnowsItIsAlreadyAFavourite()
    {
        PluginView view = View(
            "tom", [Station("found")], state: new UserState { Favourites = [Station("found")] });

        Node(view, "station-favourite-search-found-label").Props["text"]
            .Should().Be(StationCards.RemoveFavouriteLabel);
    }
}
