// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Design;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class StationCardsTests
{
    private static RadioStation Station(string? logo = null, string id = "a") =>
        new()
        {
            Id = id,
            Name = "Example FM",
            StreamUrl = "https://example.com/stream",
            Genre = "Ambient",
            Country = "NL",
            LogoUrl = logo,
        };

    private static UserState Keeping(params RadioStation[] stations) =>
        new() { Favourites = stations };

    private static Dictionary<string, object?> Data(PluginComponent card) =>
        card.Props["data"].Should().BeOfType<Dictionary<string, object?>>().Subject;

    // The whole point of the rewrite. PluginViews.Card sized itself from the image it was
    // given, so a 1000px logo drew a 1000px tile beside a 200px one; NMMusicCard is the
    // component the app's own Artists grid is built from and sizes every card the same.
    [Fact]
    public void Card_IsTheAppsOwnMusicCard()
    {
        StationCards.Card(Station(), isFavourite: false)
            .Component.Should().Be(NmAppComponents.MusicCard);
    }

    // Likewise the container: PluginViews.Grid is not a grid, it is Stack(id, "row",
    // wrap: true) - the identical call to PluginViews.Row - so nothing was laying the
    // tiles out at all.
    [Fact]
    public void Grid_IsTheAppsOwnGrid()
    {
        PluginComponent grid = StationCards.Grid(
            "g", [Station(id: "a"), Station(id: "b")], UserState.Empty, "scope");

        grid.Component.Should().Be(NmAppComponents.Grid);
        grid.Items.Should().HaveCount(2);
    }

    [Fact]
    public void Card_CarriesTheFieldsTheComponentRequires()
    {
        Dictionary<string, object?> data = Data(StationCards.Card(Station(), false));

        data["id"].Should().Be("a");
        data["name"].Should().Be("Example FM");
        data["link"].Should().NotBeNull();
    }

    // A plugin-relative route here is a dead link: `link` goes to the app's own router,
    // which has no idea which plugin drew the card. Navigate is the one that gets prefixed.
    [Fact]
    public void Card_LinksThroughTheAppsRouterAndNotThePluginsOwnRoute()
    {
        Data(StationCards.Card(Station(), false))["link"]
            .Should().Be($"/plugins/{PluginIdentity.Id}/station/a")
            .And.NotBe(RadioRoutes.Station("a"));
    }

    [Fact]
    public void Card_ReportsWhetherThisViewerKeptIt()
    {
        Data(StationCards.Card(Station(), isFavourite: true))["favorite"].Should().Be(true);
        Data(StationCards.Card(Station(), isFavourite: false))["favorite"].Should().Be(false);
    }

    [Fact]
    public void Grid_MarksOnlyTheStationsThisViewerKept()
    {
        PluginComponent grid = StationCards.Grid(
            "g", [Station(id: "a"), Station(id: "b")], Keeping(Station(id: "b")), "s");

        Data(grid.Items[0])["favorite"].Should().Be(false);
        Data(grid.Items[1])["favorite"].Should().Be(true);
    }

    // One station legitimately appears twice on one page - kept above, popular below - and
    // an unscoped id makes those the same node id in one payload.
    [Fact]
    public void Grid_ScopesCardIdsSoOnePageCanShowAStationTwice()
    {
        string kept = StationCards.Grid("g", [Station()], UserState.Empty, "fav").Items[0].Id;
        string popular = StationCards.Grid("g", [Station()], UserState.Empty, "popular").Items[0].Id;

        kept.Should().NotBe(popular);
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

    // A rejected logo must not reach the card either, or the grid draws the broken icon
    // the gate just refused.
    [Fact]
    public void Card_SendsNoCoverWhenTheLogoIsOneTheBrowserWouldRefuse()
    {
        Data(StationCards.Card(Station("http://cdn.example.com/l.png"), false))["cover"]
            .Should().BeNull();
    }

    // A rejected logo must not reach the player either, or the now-playing panel shows
    // the broken icon the grid just refused.
    [Fact]
    public void Play_DoesNotHandARejectedCoverToThePlayer()
    {
        StationCards.Play(Station("http://cdn.example.com/logo.png"))
            .Payload["cover"].Should().BeNull();
    }

    // The player builds an artist link, a route and a DOM id from this. A live stream has
    // no artist, and a genre there made the app route to nothing and then build a selector
    // out of a url - both of which surfaced as component-error toasts.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeitherPlayNorEnqueueSendsAnArtist(bool enqueue)
    {
        PluginActionIntent intent =
            enqueue ? StationCards.Enqueue(Station()) : StationCards.Play(Station());

        intent.Payload["artist"].Should().BeNull();
    }

    [Fact]
    public void ToggleFavourite_GoesThroughTheControllersOwnRoute()
    {
        PluginActionIntent action = StationCards.ToggleFavourite(Station());

        action.Type.Should().Be(PluginActionType.CallPlugin);
        action.Payload["method"].Should().Be(
            $"{InternetRadioController.ToggleFavouriteMethod}/a");
    }

    // Readable without colour. A toggle whose only difference is a tint is unreadable to
    // a good share of viewers, and to everyone in a screenshot.
    [Fact]
    public void TheTwoFavouriteLabelsDiffer()
    {
        StationCards.AddFavouriteLabel.Should().NotBe(StationCards.RemoveFavouriteLabel);
    }

    [Fact]
    public void Subtitle_JoinsWhatIsKnownAndIsNullWhenNothingIs()
    {
        StationCards.Subtitle(Station()).Should().Be("Ambient · NL");
        StationCards.Subtitle(new RadioStation
        {
            Id = "x", Name = "n", StreamUrl = "https://e.com/s",
        }).Should().BeNull();
    }
}
