// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.PluginSdk.Abstractions;
using Xunit;
using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.Controllers;
using NoMercy.Plugin.InternetRadio.Views;

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

    private sealed class FixedLogos(string? dataUri) : IStationLogos
    {
        public string? DataUriFor(RadioStation station) => dataUri;
    }

    // The grid Films, Series and Music are laid out with. A plugin cannot express its
    // responsive column counts and should not try: the point is that it is the same shelf.
    [Fact]
    public void TheGridIsTheAppsOwnGrid()
    {
        PluginComponent grid = StationCards.Grid(
            "g", [Station(id: "a"), Station(id: "b")], "s");

        grid.Component.Should().Be(Ui.MediaGridComponent);
        grid.Items.Should().HaveCount(2);
    }

    [Fact]
    public void ATileIsTheAppsOwnCard()
    {
        PluginComponent tile = StationCards.Tile(
            Station("https://c.example.com/l.png"), logos: new FixedLogos("data:image/png;base64,AA=="));

        tile.Component.Should().Be(Ui.MediaCardComponent);

        Dictionary<string, object?> data = Data(tile);

        data["name"].Should().Be("Example FM");
        data["link"].Should().Be(AppRoutes.Station("a"));
        data["cover"].Should().Be("data:image/png;base64,AA==");
        // The artist branch draws a square framed logo; the album branch draws a record
        // sleeve, which a radio station is not.
        data["type"].Should().Be("artist");
    }

    // Until the logo is fetched - and for a station that has none - the card draws its
    // own placeholder, never a broken image.
    [Fact]
    public void ATileWithoutAKnownLogoDrawsThePlaceholder()
    {
        Data(StationCards.Tile(Station("https://c.example.com/l.png")))["cover"].Should().BeNull();
    }

    [Fact]
    public void TheGridHandsEveryTileTheSameLogos()
    {
        PluginComponent grid = StationCards.Grid(
            "g", [Station(id: "a"), Station(id: "b")], "s", new FixedLogos("data:x"));

        // Cast to string rather than left as object: FluentAssertions only offers AllBe on a
        // string collection, not on the object-typed values a props dictionary holds.
        grid.Items.Select(tile => (string?)Data(tile)["cover"]).Should().AllBe("data:x");
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
        string kept = StationCards.Grid("g", [Station()], "fav").Items[0].Id;
        string popular = StationCards.Grid("g", [Station()], "popular").Items[0].Id;

        kept.Should().NotBe(popular);
    }

    // The player builds an artist link and a route from this. A live stream has no artist,
    // and a genre there made the app route to something that is not one.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeitherPlayNorEnqueueSendsAnArtist(bool enqueue)
    {
        PluginActionIntent intent =
            enqueue ? StationCards.Enqueue(Station(), "/m", null) : StationCards.Play(Station(), "/m", null);

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
            enqueue ? StationCards.Enqueue(Station(), "/m", null) : StationCards.Play(Station(), "/m", null);

        object? id = intent.Payload[StationCards.StationIdKey];

        id.Should().Be("a");
        id!.ToString().Should().NotContain("/").And.NotContain(":");
    }

    [Fact]
    public void TheMediaIntentsCarryTheStreamAndTheLogoTheyAreHanded()
    {
        PluginActionIntent play = StationCards.Play(Station(), "/api/v1/plugins/x/media/t", "data:image/png;base64,AA==");

        play.Payload["streamUrl"].Should().Be("/api/v1/plugins/x/media/t");
        play.Payload["cover"].Should().Be("data:image/png;base64,AA==");
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
