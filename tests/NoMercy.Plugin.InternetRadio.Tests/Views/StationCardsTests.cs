// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
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

    private static IEnumerable<PluginComponent> Nodes(PluginComponent root) =>
        PluginNodes.Flatten(root);

    private static PluginComponent CardOf(PluginComponent tile) => tile.Items[0];

    // The name is the whole thing. Sent as PluginComponentType.Grid this went out as
    // "NMCard" and the client drew a design-system card, so nothing ever laid the tiles
    // out - which is why every attempt to make them even by hand failed. The client's own
    // grid is auto-fill over a 10rem minimum and needs no help.
    [Fact]
    public void TheGridIsTheClientsGrid()
    {
        PluginComponent grid = StationCards.Grid(
            "g", [Station(id: "a"), Station(id: "b")], UserState.Empty, "s");

        grid.Component.Should().Be(Ui.GridComponent);
        grid.Items.Should().HaveCount(2);
    }

    [Fact]
    public void ATileIsACardTheClientCanDraw()
    {
        // The relay only knows where this server lives once a request has told it, and no
        // request has in a test - so the cover is told explicitly rather than left to
        // whichever other test ran first.
        MediaProxy.Remember("https://server.example", null);

        PluginComponent tile = StationCards.Tile(Station("https://c.example.com/l.png"), false);

        tile.Component.Should().Be(Ui.ContainerComponent);
        CardOf(tile).Component.Should().Be(Ui.CardComponent);
        CardOf(tile).Props["title"].Should().Be("Example FM");
        CardOf(tile).Props["subtitle"].Should().Be("Ambient · NL");
        CardOf(tile).Props["image"].Should().NotBeNull();
    }

    [Fact]
    public void OneClickOnTheCardPlaysTheStation()
    {
        PluginActionIntent action = CardOf(StationCards.Tile(Station(), false)).Action!;

        action.Type.Should().Be(PluginActionType.PlayMedia);
        action.Payload["title"].Should().Be("Example FM");
    }

    // Keeping a station must not also play it. Nested inside the card, the click would land
    // on the thing carrying the play action and do both.
    [Fact]
    public void TheFavouriteToggleIsASiblingOfTheCardAndNotInsideIt()
    {
        PluginComponent tile = StationCards.Tile(Station(), isFavourite: false);

        tile.Items.Select(node => node.Id)
            .Should().BeEquivalentTo(["station-card-a", "station-favourite-a"]);

        Nodes(CardOf(tile)).Select(node => node.Id).Should().NotContain("station-favourite-a");
    }

    // Readable without colour. A toggle whose only difference is a tint is unreadable to a
    // good share of viewers, and to everyone in a screenshot.
    [Fact]
    public void Grid_LabelsOnlyTheStationsThisViewerKeptAsKept()
    {
        PluginComponent grid = StationCards.Grid(
            "g", [Station(id: "a"), Station(id: "b")], Keeping(Station(id: "b")), "s");

        static string Label(PluginComponent tile) => tile.Items[1].Props["label"]!.ToString()!;

        Label(grid.Items[0]).Should().Be(StationCards.AddFavouriteLabel);
        Label(grid.Items[1]).Should().Be(StationCards.RemoveFavouriteLabel);
    }

    [Fact]
    public void ToggleFavourite_GoesThroughTheControllersOwnRoute()
    {
        PluginActionIntent action = StationCards.ToggleFavourite(Station());

        action.Type.Should().Be(PluginActionType.CallPlugin);
        action.Payload["method"].Should().Be($"{InternetRadioController.ToggleFavouriteMethod}/a");
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
    // just refused. The card loses its image and keeps everything else.
    [Fact]
    public void Tile_DrawsNoImageWhenTheLogoIsOneTheBrowserWouldRefuse()
    {
        PluginComponent card = CardOf(StationCards.Tile(Station("http://cdn.example.com/l.png"), false));

        card.Props["image"].Should().BeNull();
        card.Props["title"].Should().Be("Example FM");
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
