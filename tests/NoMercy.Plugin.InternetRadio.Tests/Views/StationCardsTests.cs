// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.RegularExpressions;
using FluentAssertions;
using NoMercy.Design;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class StationCardsTests
{
    // The one rule that keeps the grid even. NmBox.Width is an NMSize, and this is the
    // pattern the contract defines it by - so "13rem" was never a width. It did not fail
    // loudly; it simply did not match, the width was dropped, and each tile fell back to
    // sizing itself from its own logo. That is why one tile was 1000px and the next 200px.
    private const string Size =
        @"^(0|px|\d+(-\d)?|\d+/\d+|auto|full|available|content|min|max|screen)$";

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

    private static NmBox? BoxOf(PluginComponent node) =>
        (node.Design as NMCardProps)?.Box ?? (node.Design as NMImageProps)?.Box;

    [Fact]
    public void EveryTileAsksForTheSameFractionOfTheRow()
    {
        PluginComponent grid = StationCards.Grid(
            "g", [Station(id: "a"), Station(id: "b")], UserState.Empty, "s");

        grid.Items.Should().HaveCount(2);
        grid.Items.Select(tile => BoxOf(tile)!.Width).Should().AllBe(StationCards.TileWidth);
    }

    [Fact]
    public void TheTileWidthIsAValidSizeAndNotALength()
    {
        StationCards.TileWidth.Should().MatchRegex(Size);
        Regex.IsMatch("13rem", Size).Should().BeFalse("that is the value that was dropped");
    }

    // Every box this plugin sets, not just the tile: one stray "11rem" left on the cover
    // puts the whole grid back to uneven, and no single assertion would show it.
    [Fact]
    public void EveryBoxThisPluginSetsCarriesAValidSize()
    {
        string[] sizes =
        [
            .. Nodes(StationCards.Grid(
                    "g", [Station("https://c.example.com/l.png")], UserState.Empty, "s"))
                .Select(BoxOf)
                .Where(box => box is not null)
                .SelectMany(box => new[] { box!.Width, box.Height, box.MinWidth, box.MaxWidth })
                .Where(value => value is not null)!,
        ];

        sizes.Should().NotBeEmpty();
        sizes.Should().OnlyContain(value => Regex.IsMatch(value, Size));
    }

    // The cover fills the tile rather than carrying a fraction of its own, or it would be
    // a sixth of a sixth.
    [Fact]
    public void TheCoverFillsTheTileAndIsSquareWhateverShapeTheLogoIs()
    {
        // Relayed through this server, because the dashboard's img-src refuses the
        // station's own host. The relay only learns this server's address from a request,
        // so the test supplies one rather than depending on whichever test ran first.
        MediaProxy.Remember("https://server.example", null);

        PluginComponent cover = Nodes(StationCards.Tile(Station("https://c.example.com/l.png"), false))
            .Single(node => node.Id == "station-cover-a");

        NMImageProps props = cover.Design.Should().BeOfType<NMImageProps>().Subject;

        // The one that made every cover an img with an alt and no source: Src lives on the
        // props record, and the loose bag beside it is overwritten by the merge.
        props.Src.Should().Be(
            $"https://server.example/api/v1/plugins/{PluginIdentity.Id}/cover/a");
        props.AspectRatio.Should().Be("square");
        props.Fit.Should().Be("cover");
        props.Box!.Width.Should().Be("full");
    }

    // Keeping a station must not also play it. Nested inside the card, the click landed on
    // the thing carrying the play action and did both.
    [Fact]
    public void TheFavouriteToggleIsASiblingOfTheCardAndNotInsideIt()
    {
        PluginComponent tile = StationCards.Tile(Station(), isFavourite: false);

        tile.Items.Select(node => node.Id)
            .Should().BeEquivalentTo(["station-card-a", "station-favourite-a"]);

        Nodes(tile.Items[0]).Select(node => node.Id)
            .Should().NotContain("station-favourite-a");
    }

    [Fact]
    public void OneClickOnTheCardPlaysTheStation()
    {
        PluginComponent card = StationCards.Tile(Station(), false).Items[0];

        card.Action!.Type.Should().Be(PluginActionType.PlayMedia);
        card.Action.Payload["title"].Should().Be("Example FM");
    }

    // Readable without colour. A toggle whose only difference is a tint is unreadable to a
    // good share of viewers, and to everyone in a screenshot.
    [Fact]
    public void Grid_LabelsOnlyTheStationsThisViewerKeptAsKept()
    {
        PluginComponent grid = StationCards.Grid(
            "g", [Station(id: "a"), Station(id: "b")], Keeping(Station(id: "b")), "s");

        static string Label(PluginComponent tile) =>
            PluginNodes.Flatten(tile)
                .Single(node => node.Id.StartsWith("station-favourite-", StringComparison.Ordinal)
                    && node.Id.EndsWith("-label", StringComparison.Ordinal))
                .Props["text"]!.ToString()!;

        Label(grid.Items[0]).Should().Be(StationCards.AddFavouriteLabel);
        Label(grid.Items[1]).Should().Be(StationCards.RemoveFavouriteLabel);
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
    public void ToggleFavourite_GoesThroughTheControllersOwnRoute()
    {
        PluginActionIntent action = StationCards.ToggleFavourite(Station());

        action.Type.Should().Be(PluginActionType.CallPlugin);
        action.Payload["method"].Should().Be($"{InternetRadioController.ToggleFavouriteMethod}/a");
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

    // A station with no drawable logo loses the cover and nothing else. A tile that quietly
    // drops its title when the logo rots is the real regression, and six had already rotted.
    [Fact]
    public void Tile_LosesOnlyTheCoverWhenThereIsNone()
    {
        string[] with =
            [.. Nodes(StationCards.Tile(Station("https://c.example.com/l.png"), false))
                .Select(node => node.Id)];
        string[] without = [.. Nodes(StationCards.Tile(Station(), false)).Select(node => node.Id)];

        with.Should().Contain("station-cover-a");
        without.Should().BeEquivalentTo(with.Where(id => id != "station-cover-a"));
    }

    // A rejected logo must not reach the player either, or the now-playing panel shows the
    // broken icon the grid just refused.
    [Fact]
    public void Play_DoesNotHandARejectedCoverToThePlayer()
    {
        StationCards.Play(Station("http://cdn.example.com/logo.png"))
            .Payload["cover"].Should().BeNull();
    }

    // The player builds an artist link, a route and a DOM id from this. A live stream has no
    // artist, and a genre there made the app route to nothing and then build a selector out
    // of a url - both of which surfaced as component-error toasts.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeitherPlayNorEnqueueSendsAnArtist(bool enqueue)
    {
        PluginActionIntent intent =
            enqueue ? StationCards.Enqueue(Station()) : StationCards.Play(Station());

        intent.Payload["artist"].Should().BeNull();
    }

    // The station's own id travels with the intent. Without one the client builds a track
    // id out of the stream url, and that id then goes into a CSS selector and a route where
    // a url is legal in neither - which is what stops playback before it starts.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothMediaIntentsCarryTheStationsOwnId(bool enqueue)
    {
        PluginActionIntent intent =
            enqueue ? StationCards.Enqueue(Station()) : StationCards.Play(Station());

        intent.Payload[StationCards.StationIdKey].Should().Be("a");
    }

    // A uuid, not a url: the point is an identifier that survives being put in a selector
    // and in a path, and that does not change when a station moves its stream.
    [Fact]
    public void TheIdItSendsIsNotDerivedFromTheStreamUrl()
    {
        object? id = StationCards.Play(Station()).Payload[StationCards.StationIdKey];

        id.Should().Be("a");
        id!.ToString().Should().NotContain("/").And.NotContain(":");
    }

    [Fact]
    public void Subtitle_JoinsWhatIsKnownAndIsNullWhenNothingIs()
    {
        StationCards.Subtitle(Station()).Should().Be("Ambient \u00b7 NL");
        StationCards.Subtitle(new RadioStation
        {
            Id = "x",
            Name = "n",
            StreamUrl = "https://e.com/s",
        }).Should().BeNull();
    }
}
