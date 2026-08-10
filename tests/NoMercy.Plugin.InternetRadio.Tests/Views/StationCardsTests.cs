// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

// The tiles are the app's own grid and the app's own card now, so most of what these tests
// used to assert - widths, boxes, nested nodes - is no longer this plugin's to decide, and
// pinning it would only reproduce something the app already does.
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

    private static Dictionary<string, object?> Data(PluginComponent card) =>
        card.Props["data"].Should().BeOfType<Dictionary<string, object?>>().Subject;

    // A station is not an artist. Left to its own music vocabulary the card read
    // "Artist" under every station on the shelf, with a bare bullet after it.
    [Fact]
    public void Tile_SaysWhereTheStationBroadcastsFromAndWhatItPlays()
    {
        Dictionary<string, object?> data = Data(StationCards.Tile(Station()));

        data["description"].Should().Be("NL • Ambient");
    }

    [Fact]
    public void Tile_LeavesTheLineOutWhenAStationSaysNeither()
    {
        RadioStation bare = Station() with { Country = null, Genre = null };

        Data(StationCards.Tile(bare))["description"].Should().Be(string.Empty);
    }

    // The grid Films, Series and Music are laid out with. A plugin cannot express its
    // responsive column counts and should not try: the point is that it is the same shelf.
    [Fact]
    public void TheGridIsTheAppsOwnGrid()
    {
        PluginComponent grid = StationCards.Grid(
            "g", [Station(id: "a"), Station(id: "b")], UserState.Empty, "s");

        grid.Component.Should().Be(Ui.MediaGridComponent);
        grid.Items.Should().HaveCount(2);
    }

    [Fact]
    public void ATileIsTheAppsOwnCard()
    {
        MediaProxy.Remember("https://server.example", null);

        PluginComponent tile = StationCards.Tile(Station("https://c.example.com/l.png"));

        tile.Component.Should().Be(Ui.MediaCardComponent);

        Dictionary<string, object?> data = Data(tile);

        data["name"].Should().Be("Example FM");
        data["link"].Should().Be(AppRoutes.Station("a"));
        data["cover"].Should().NotBeNull();
        // The artist branch draws a square framed logo; the album branch draws a record
        // sleeve, which a radio station is not.
        data["type"].Should().Be("artist");
    }

    // A plugin-relative route here is a dead link: the card is a RouterLink resolved by the
    // app's own router, which does not know which plugin drew it.
    [Fact]
    public void TheCardLinksThroughTheAppsRouter()
    {
        Data(StationCards.Tile(Station()))["link"]
            .Should().Be($"/music/plugins/{PluginIdentity.Id}/station/a")
            .And.NotBe(RadioRoutes.Station("a"));
    }

    // One station legitimately appears twice on one page - kept above, popular below - and
    // an unscoped id makes those the same node id in one payload.
    [Fact]
    public void Grid_ScopesTileIdsSoOnePageCanShowAStationTwice()
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

    // An http image on an https dashboard is blocked as mixed content and draws as a broken
    // icon, which reads as this plugin being broken rather than as a station's logo having
    // rotted. Same judgement the stream gates make, for the same reason.
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

    // A rejected logo must not reach the card, or the grid draws the broken icon the gate
    // just refused. The card has a placeholder of its own for that.
    [Fact]
    public void Tile_SendsNoCoverWhenTheLogoIsOneTheBrowserWouldRefuse()
    {
        Data(StationCards.Tile(Station("http://cdn.example.com/l.png")))["cover"]
            .Should().BeNull();
    }

    [Fact]
    public void Play_DoesNotHandARejectedCoverToThePlayer()
    {
        StationCards.Play(Station("http://cdn.example.com/logo.png"))
            .Payload["cover"].Should().BeNull();
    }

    // The player builds an artist link and a route from this. A live stream has no artist,
    // and a genre there made the app route to something that is not one.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeitherPlayNorEnqueueSendsAnArtist(bool enqueue)
    {
        PluginActionIntent intent =
            enqueue ? StationCards.Enqueue(Station()) : StationCards.Play(Station());

        intent.Payload["artist"].Should().BeNull();
    }

    // Without an id of its own the client builds a track id out of the stream url, and that
    // id then goes into a CSS selector and a route where a url is legal in neither.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothMediaIntentsCarryTheStationsOwnId(bool enqueue)
    {
        PluginActionIntent intent =
            enqueue ? StationCards.Enqueue(Station()) : StationCards.Play(Station());

        object? id = intent.Payload[StationCards.StationIdKey];

        id.Should().Be("a");
        id!.ToString().Should().NotContain("/").And.NotContain(":");
    }

    [Fact]
    public void ToggleFavourite_GoesThroughTheControllersOwnRoute()
    {
        PluginActionIntent action = StationCards.ToggleFavourite(Station());

        action.Type.Should().Be(PluginActionType.CallPlugin);
        action.Payload["method"].Should().Be($"{InternetRadioController.ToggleFavouriteMethod}/a");
    }

    [Fact]
    public void Subtitle_JoinsWhatIsKnownAndIsNullWhenNothingIs()
    {
        StationCards.Subtitle(Station()).Should().Be("Ambient · NL");
        StationCards.Subtitle(new RadioStation
        {
            Id = "x",
            Name = "n",
            StreamUrl = "https://e.com/s",
        }).Should().BeNull();
    }
}
