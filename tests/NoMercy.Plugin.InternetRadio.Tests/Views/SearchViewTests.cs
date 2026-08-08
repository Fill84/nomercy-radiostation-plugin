// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

// Searching is a keyboard now, not a form. The form could never work: a
// PluginComponentType.Form is an NMCard, so there is no form element in the DOM and a
// submit posts "{}" whatever the field holds. Every assertion here is about the term
// travelling in the route instead, which is the one channel this client does deliver.
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

    private static string Route(PluginView view, string id) =>
        Node(view, id).Action!.Payload["route"]!.ToString()!;

    [Fact]
    public void EveryLetterAndDigitIsAKey()
    {
        IEnumerable<string> ids = Ids(View(string.Empty));

        foreach (char key in SearchTerms.Letters + SearchTerms.Digits)
        {
            ids.Should().Contain($"search-key-{key}");
        }
    }

    // The one assertion the whole design rests on: a key does not submit anything, it
    // navigates to the route with one more character in it.
    [Fact]
    public void AKeyNavigatesToTheRouteWithThatCharacterAppended()
    {
        PluginComponent key = Node(View("tom"), "search-key-o");

        key.Action!.Type.Should().Be(PluginActionType.Navigate);
        key.Action.Payload["route"].Should().Be(RadioRoutes.Search("tomo"));
    }

    [Fact]
    public void TheSpelledTermIsAlwaysOnScreen()
    {
        Node(View("tomorrowland"), "search-spelled-text")
            .Props["text"]!.ToString().Should().Contain("tomorrowland");
    }

    // A viewer who cannot see what they spelled cannot tell a mistyped search from an
    // empty one, so the line is there before the first key too.
    [Fact]
    public void TheSpelledLineIsThereBeforeAnythingIsSpelled()
    {
        Ids(View(string.Empty)).Should().Contain("search-spelled");
    }

    [Fact]
    public void BackspaceGoesToTheRouteWithoutTheLastCharacter()
    {
        Route(View("tomo"), "search-backspace").Should().Be(RadioRoutes.Search("tom"));
    }

    [Fact]
    public void ClearGoesBackToTheEmptyKeyboard()
    {
        Route(View("tomo"), "search-clear").Should().Be(RadioRoutes.SearchRoot);
    }

    // A backspace over an empty term navigates to the page it is already on, which reads
    // as a dead button.
    [Fact]
    public void WithNothingSpelled_ThereIsNothingToBackspaceOrClear()
    {
        Ids(View(string.Empty))
            .Should().NotContain("search-backspace").And.NotContain("search-clear");
    }

    [Fact]
    public void SpaceIsAKeyToo()
    {
        Route(View("radio"), "search-key-space").Should().Be(RadioRoutes.Search("radio "));
    }

    // Sanitise refuses a doubled space, so the key would navigate to the page it is on.
    [Fact]
    public void SpaceIsAbsentWhenItWouldDoNothing()
    {
        Ids(View("radio ")).Should().NotContain("search-key-space");
    }

    // One letter matches thousands of stations and answers nothing.
    [Fact]
    public void OneCharacterAsksForAnotherRatherThanSearching()
    {
        IEnumerable<string> ids = Ids(View("t"));

        ids.Should().Contain("search-too-short")
            .And.NotContain("search-empty")
            .And.NotContain("search-grid");
    }

    [Fact]
    public void ResultsAreDrawnAsAGridOfStations()
    {
        Ids(View("tom", [Station("found-a"), Station("found-b")]))
            .Should().Contain("search-grid")
            .And.Contain("station-card-search-found-a")
            .And.Contain("station-card-search-found-b");
    }

    [Fact]
    public void WithATermAndNoResults_SaysNothingMatched()
    {
        Ids(View("nothing")).Should().Contain("search-empty");
    }

    // "We could not reach radio-browser" and "there is no such station" ask the viewer to
    // do different things. Reporting an outage as an empty result set has them respelling
    // the search that was never the problem.
    [Fact]
    public void WhenTheQueryFailed_SaysSoInsteadOfClaimingNoResults()
    {
        Ids(View("anything", failed: true))
            .Should().Contain("search-failed").And.NotContain("search-empty");
    }

    // The keyboard stays up through every state, or a failed search is a screen with no
    // way to try a different one.
    [Theory]
    [InlineData("", false)]
    [InlineData("t", false)]
    [InlineData("tom", false)]
    [InlineData("tom", true)]
    public void TheKeyboardSurvivesEveryState(string term, bool failed)
    {
        Ids(View(term, failed: failed)).Should().Contain("search-key-a");
    }

    [Fact]
    public void ThereIsAWayBackToTheLandingPage()
    {
        Route(View("tom"), "search-back").Should().Be(RadioRoutes.Browse);
    }

    // A result is the same card as any grid, so a station cannot behave one way when
    // browsed and another when searched for.
    [Fact]
    public void AResultKnowsItIsAlreadyAFavourite()
    {
        PluginView view = View(
            "tom",
            [Station("found")],
            state: new UserState { Favourites = [Station("found")] });

        Node(view, "station-card-search-found").Props["data"]
            .Should().BeOfType<Dictionary<string, object?>>()
            .Which["favorite"].Should().Be(true);
    }
}
