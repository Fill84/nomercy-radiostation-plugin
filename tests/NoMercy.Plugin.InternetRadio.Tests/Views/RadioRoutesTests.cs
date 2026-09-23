// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;
using NoMercy.PluginSdk.Abstractions;
using NoMercy.Plugin.InternetRadio.Views;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class RadioRoutesTests
{
    [Theory]
    [InlineData("/", RadioRouteKind.Browse)]
    [InlineData("", RadioRouteKind.Browse)]
    [InlineData(null, RadioRouteKind.Browse)]
    [InlineData("/all", RadioRouteKind.AllStations)]
    [InlineData("/settings", RadioRouteKind.Settings)]
    public void Parse_ResolvesTheFixedRoutes(string? route, RadioRouteKind expected)
    {
        RadioRoutes.Parse(route).Kind.Should().Be(expected);
    }

    [Fact]
    public void Parse_ReadsTheGenreSlugFromThePath()
    {
        RadioRoute parsed = RadioRoutes.Parse("/genre/drum-bass");

        parsed.Kind.Should().Be(RadioRouteKind.Genre);
        parsed.Value.Should().Be("drum-bass");
    }

    [Fact]
    public void Parse_ReadsTheStationIdFromThePath()
    {
        RadioRoute parsed = RadioRoutes.Parse("/station/960cf833-0601-11e8-ae97-52543be04c81");

        parsed.Kind.Should().Be(RadioRouteKind.Station);
        parsed.Value.Should().Be("960cf833-0601-11e8-ae97-52543be04c81");
    }

    [Theory]
    [InlineData("/all/")]
    [InlineData("/settings/")]
    [InlineData("//")]
    public void Parse_ToleratesATrailingSlash(string route)
    {
        RadioRoutes.Parse(route).Kind.Should().NotBe(RadioRouteKind.Unknown);
    }

    [Fact]
    public void Parse_IsCaseInsensitiveOnTheSegmentNames()
    {
        RadioRoutes.Parse("/Settings").Kind.Should().Be(RadioRouteKind.Settings);
        RadioRoutes.Parse("/GENRE/rock").Kind.Should().Be(RadioRouteKind.Genre);

        // The segment NAME ("genre") is matched case-insensitively, but the VALUE
        // that follows it is not touched - it must survive with its case intact.
        RadioRoute mixedCase = RadioRoutes.Parse("/GENRE/RockOn");
        mixedCase.Kind.Should().Be(RadioRouteKind.Genre);
        mixedCase.Value.Should().Be("RockOn");
    }

    [Theory]
    [InlineData("/nope")]
    [InlineData("/genre")]          // no slug
    [InlineData("/station")]        // no id
    [InlineData("/genre//")]        // empty slug
    [InlineData("/station/a/b")]    // an id cannot contain a slash
    public void Parse_ReportsAnythingElseAsUnknown(string route)
    {
        RadioRoutes.Parse(route).Kind.Should().Be(RadioRouteKind.Unknown);
    }

    // The builders and the parser are two halves of the same agreement, and a link
    // that does not parse is a dead end the user finds before any test does.
    [Fact]
    public void Builders_ProduceRoutesTheParserResolves()
    {
        RadioRoutes.Parse(RadioRoutes.Genre("ambient")).Should()
            .Be(new RadioRoute(RadioRouteKind.Genre, "ambient"));
        RadioRoutes.Parse(RadioRoutes.Station("abc")).Should()
            .Be(new RadioRoute(RadioRouteKind.Station, "abc"));
        RadioRoutes.Parse(RadioRoutes.Browse).Kind.Should().Be(RadioRouteKind.Browse);
        RadioRoutes.Parse(RadioRoutes.AllStations).Kind.Should().Be(RadioRouteKind.AllStations);
        RadioRoutes.Parse(RadioRoutes.Settings).Kind.Should().Be(RadioRouteKind.Settings);
    }

    // A user-supplied station id is a slug of their station name, so it can contain
    // anything they typed. It has to survive the round trip through a URL path.
    [Fact]
    public void Station_EscapesAnIdSoItSurvivesThePath()
    {
        string route = RadioRoutes.Station("a b/c");

        RadioRoutes.Parse(route).Value.Should().Be("a b/c");
    }

    // Documents a known limitation rather than pinning a desired behaviour: an
    // empty slug/id builds a trailing-slash path ("/station/"), and Parse's
    // RemoveEmptyEntries drops that last segment, so the round trip lands on
    // Unknown instead of Station/Genre with an empty Value. This is why callers
    // must never pass an empty id or slug to Genre/Station - see the comment on
    // those builders for why no live caller can produce one today.
    [Fact]
    public void Parse_TreatsARouteBuiltFromAnEmptyValueAsUnknown()
    {
        RadioRoutes.Parse(RadioRoutes.Station("")).Kind.Should().Be(RadioRouteKind.Unknown);
        RadioRoutes.Parse(RadioRoutes.Genre("")).Kind.Should().Be(RadioRouteKind.Unknown);
    }

    // The shape a route actually arrives in, which is not the shape this plugin builds.
    // The client sends the segments as a repeated `route` query parameter and ASP.NET
    // binds those into one comma-joined string, so every two-segment page was a dead end.
    // These pin the received form; the ones above pin the built form, and the gap between
    // them is exactly what a full test suite failed to notice.
    [Theory]
    [InlineData("/genre,ambient", "ambient")]
    [InlineData("genre,ambient", "ambient")]
    [InlineData("/genre,drum-bass", "drum-bass")]
    public void Parse_ReadsAGenreRouteJoinedWithACommaByTheClient(string route, string expected)
    {
        RadioRoute parsed = RadioRoutes.Parse(route);

        parsed.Kind.Should().Be(RadioRouteKind.Genre);
        parsed.Value.Should().Be(expected);
    }

    [Fact]
    public void Parse_ReadsAStationRouteJoinedWithACommaByTheClient()
    {
        RadioRoute parsed = RadioRoutes.Parse("/station,960cf833-0601-11e8-ae97-52543be04c81");

        parsed.Kind.Should().Be(RadioRouteKind.Station);
        parsed.Value.Should().Be("960cf833-0601-11e8-ae97-52543be04c81");
    }

    // Both forms have to keep working: the built one is what this plugin puts in its own
    // links, and the received one is what comes back.
    [Fact]
    public void Parse_StillReadsTheSlashFormThisPluginBuilds()
    {
        RadioRoutes.Parse(RadioRoutes.Genre("drum-bass")).Value.Should().Be("drum-bass");
    }

    // The table is what the host registers, and RadioRoutes.Parse is what answers. Two
    // lists of paths drift the moment one is edited alone: a page the host advertises and
    // the plugin cannot parse is a dead link in the sidebar.
    [Fact]
    public void EveryDeclaredRouteIsOneTheParserUnderstands()
    {
        foreach (PluginRoute route in InternetRadioPlugin.Table.Routes)
        {
            string path = route.Path
                .Replace(":term", "jazz")
                .Replace(":slug", "ambient")
                .Replace(":id", "u1");

            RadioRoutes.Parse(path).Kind.Should().NotBe(RadioRouteKind.Unknown, $"'{route.Path}' is declared");
        }
    }

    [Fact]
    public void EveryPageTheParserAnswersIsDeclared()
    {
        IEnumerable<RadioRouteKind> declared = InternetRadioPlugin.Table.Routes
            .Select(route => RadioRoutes.Parse(
                route.Path.Replace(":term", "jazz").Replace(":slug", "ambient").Replace(":id", "u1")).Kind);

        declared.Should().Contain(
        [
            RadioRouteKind.Browse,
            RadioRouteKind.AllStations,
            RadioRouteKind.Search,
            RadioRouteKind.Genre,
            RadioRouteKind.Station,
            RadioRouteKind.Settings,
        ]);
    }

    // Settings is the owner's page. The host hides an owner route from a member rather
    // than offering one that answers 403.
    [Fact]
    public void OnlyTheSettingsPageIsForTheOwner()
    {
        InternetRadioPlugin.Table.Routes
            .Where(route => route.Access == PluginRouteAccess.Owner)
            .Select(route => route.Path)
            .Should().Equal(RadioRoutes.Settings);
    }

    // A grid of station tiles is a grid; the settings page is a form. Without a layout
    // every page falls back to "standard", which is what we shipped before.
    [Fact]
    public void TheTilePagesAskForAGridAndSettingsForAForm()
    {
        PluginRoute Route(string path) => InternetRadioPlugin.Table.Routes.Single(route => route.Path == path);

        Route(RadioRoutes.Browse).Layout.Should().Be(PluginLayout.Grid);
        Route(RadioRoutes.AllStations).Layout.Should().Be(PluginLayout.Grid);
        Route(RadioRoutes.Settings).Layout.Should().Be(PluginLayout.Form);
    }

    [Fact]
    public void EveryRouteIsLabelledByAKey()
    {
        InternetRadioPlugin.Table.Routes.Should().OnlyContain(route =>
            !string.IsNullOrWhiteSpace(route.Label) && !route.Label!.Contains(' '));
    }
}
