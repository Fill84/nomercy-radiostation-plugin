// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using NoMercy.Design;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class StationCardsTests
{
    private static RadioStation Station(string? logo = null) =>
        new()
        {
            Id = "a",
            Name = "Example FM",
            StreamUrl = "https://example.com/stream",
            LogoUrl = logo,
        };

    private static IEnumerable<PluginComponent> Nodes(PluginComponent root) =>
        PluginNodes.Flatten(root);

    private static PluginComponent Toggle(PluginComponent row) =>
        Nodes(row).Single(node => node.Id == "station-favourite-a");

    // A button's label is a text leaf inside it, not a prop on it. Same shape the design
    // system gives a form field, and the reason the view tests were rewritten.
    private static string ToggleLabel(PluginComponent row) =>
        Nodes(row).Single(node => node.Id == "station-favourite-a-label")
            .Props["text"]!.ToString()!;

    [Fact]
    public void WithFavourite_PairsThePlayCardWithAToggle()
    {
        PluginComponent row = StationCards.WithFavourite(Station(), isFavourite: false);

        Nodes(row).Select(node => node.Id)
            .Should().Contain("station-card-a").And.Contain("station-favourite-a");
    }

    // The toggle is a sibling of the card, never a child: PluginViews.Card takes one
    // action and the card's is already playMedia. One click has to stay "listen to this".
    [Fact]
    public void WithFavourite_LeavesTheCardsOwnActionAlone()
    {
        PluginComponent row = StationCards.WithFavourite(Station(), isFavourite: false);

        PluginComponent card = Nodes(row).Single(node => node.Id == "station-card-a");

        card.Action!.Type.Should().Be(PluginActionType.PlayMedia);
    }

    [Fact]
    public void WithFavourite_TogglesThroughTheControllersOwnRoute()
    {
        PluginActionIntent action = Toggle(StationCards.WithFavourite(Station(), false)).Action!;

        action.Type.Should().Be(PluginActionType.CallPlugin);
        action.Payload["method"].Should().Be(
            $"{InternetRadioController.ToggleFavouriteMethod}/a");
    }

    // Readable without colour. A toggle whose only difference is a tint is unreadable to
    // a good share of viewers, and to everyone in a screenshot.
    [Fact]
    public void WithFavourite_LabelsEachStateDifferently()
    {
        ToggleLabel(StationCards.WithFavourite(Station(), false))
            .Should().Be(StationCards.AddFavouriteLabel);
        ToggleLabel(StationCards.WithFavourite(Station(), true))
            .Should().Be(StationCards.RemoveFavouriteLabel);
    }

    [Fact]
    public void CoverUrl_KeepsAnHttpsLogo()
    {
        StationCards.CoverUrl(Station("https://cdn.example.com/logo.png"))
            .Should().Be("https://cdn.example.com/logo.png");
    }

    // An http image on an https dashboard is blocked as mixed content and draws as a
    // broken icon, which reads as this plugin being broken rather than as a station's
    // logo having rotted. Same judgement the stream gates make, for the same reason.
    [Theory]
    [InlineData("http://cdn.example.com/logo.png")]
    [InlineData("/relative/logo.png")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CoverUrl_RefusesAnythingTheBrowserCannotDrawOverHttps(string? url)
    {
        StationCards.CoverUrl(Station(url)).Should().BeNull();
    }

    // A station with no drawable logo contributes no cover node at all, rather than an
    // empty one - and loses nothing else with it. A tile that quietly drops its title
    // when the logo rots is the real regression, and six logos had already rotted.
    [Fact]
    public void WithFavourite_LosesOnlyTheCoverWhenThereIsNone()
    {
        string[] withCover =
            [.. Nodes(StationCards.WithFavourite(Station("https://cdn.example.com/l.png"), false))
                .Select(node => node.Id)];
        string[] without =
            [.. Nodes(StationCards.WithFavourite(Station(), false)).Select(node => node.Id)];

        withCover.Should().Contain("station-cover-a");
        without.Should().BeEquivalentTo(withCover.Where(id => id != "station-cover-a"));
    }

    // The cap that stops one tile filling the screen. PluginViews.Card hands an image to
    // a card whose box is width:full and the image keeps its natural aspect, so a 400x400
    // logo drew about 830px tall and everything below it fell past the fold. The cover is
    // its own node now precisely so this is the plugin's decision.
    [Fact]
    public void Cover_IsSquareCroppedAndCapped()
    {
        PluginComponent cover = Nodes(
            StationCards.WithFavourite(Station("https://cdn.example.com/l.png"), false))
            .Single(node => node.Id == "station-cover-a");

        NMImageProps props = cover.Design.Should().BeOfType<NMImageProps>().Subject;

        props.AspectRatio.Should().Be("square");
        props.Fit.Should().Be("cover");
        props.Box!.MaxWidth.Should().Be(StationCards.CoverMaxWidth);
    }

    // A rejected logo must not reach the player either, or the now-playing panel shows
    // the broken icon the grid just refused.
    [Fact]
    public void Play_DoesNotHandARejectedCoverToThePlayer()
    {
        PluginComponent card = StationCards.Play(Station("http://cdn.example.com/logo.png"));

        card.Action!.Payload["cover"].Should().BeNull();
    }
}
