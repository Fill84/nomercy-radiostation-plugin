// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

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
}
