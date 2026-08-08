// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
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

    // A station with no usable logo keeps everything else. The art node is the only
    // difference: the design system omits it rather than drawing an empty box, which is
    // its call to make - what matters here is that nothing ELSE is lost with it, because
    // a card that quietly drops its title when the logo rots is the real regression.
    [Fact]
    public void WithFavourite_LosesNothingButTheArtWhenThereIsNoCover()
    {
        string[] withCover =
            [.. Nodes(StationCards.WithFavourite(Station("https://cdn.example.com/l.png"), false))
                .Select(node => node.Id)];
        string[] without =
            [.. Nodes(StationCards.WithFavourite(Station(), false)).Select(node => node.Id)];

        without.Should().BeEquivalentTo(withCover.Where(id => !id.EndsWith("-art", StringComparison.Ordinal)));
        withCover.Should().Contain("station-card-a-art");
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
