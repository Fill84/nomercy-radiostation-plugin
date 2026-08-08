// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Net;
using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.State;

public class FavouriteResolverTests
{
    private const string KnownUuid = "960cf833-0601-11e8-ae97-52543be04c81";

    private static RadioStation Station(string id) =>
        new() { Id = id, Name = $"Station {id}", StreamUrl = $"https://example.com/{id}" };

    private static StationCatalog CatalogWith(params RadioStation[] stations) =>
        StationCatalog.Create(stations, CatalogSource.Fetched, DateTimeOffset.UtcNow);

    private static string Wire(string url) =>
        $$"""
        [{"stationuuid":"{{KnownUuid}}","name":"Found FM","url":"{{url}}",
          "url_resolved":"{{url}}","hls":0,"lastcheckok":1,"votes":3}]
        """;

    private static (FavouriteResolver Resolver, FakeHttpMessageHandler Handler) Build(
        StationCatalog catalog, string? body = null, bool fail = false)
    {
        FakeHttpMessageHandler handler = new();
        if (fail)
        {
            handler.Fail(new HttpRequestException("down"));
        }
        else
        {
            handler.Respond(body ?? "[]");
        }

        return (new FavouriteResolver(catalog, new RadioBrowserClient(new HttpClient(handler))), handler);
    }

    // The commonest case, and the one that must cost nothing: the station is on the page
    // the user is looking at, so it is already in the catalogue.
    [Fact]
    public async Task ResolveAsync_ReturnsTheCatalogueRecordWithoutAskingUpstream()
    {
        (FavouriteResolver resolver, FakeHttpMessageHandler handler) =
            Build(CatalogWith(Station("a")));

        (await resolver.ResolveAsync("a", CancellationToken.None))!.Id.Should().Be("a");
        handler.Requests.Should().BeEmpty();
    }

    // The search case: never in the sweep, so the catalogue cannot answer and
    // radio-browser has to. Without this a favourited search result stores an id
    // pointing at nothing.
    [Fact]
    public async Task ResolveAsync_FallsBackToUuidLookupForAStationTheCatalogueNeverSaw()
    {
        (FavouriteResolver resolver, FakeHttpMessageHandler handler) =
            Build(StationCatalog.Empty(), Wire("https://example.com/found"));

        RadioStation? resolved = await resolver.ResolveAsync(KnownUuid, CancellationToken.None);

        resolved.Should().NotBeNull();
        resolved!.Name.Should().Be("Found FM");
        handler.Requests.Should().ContainSingle();
    }

    // A stream refused on the browse page must not become favouritable from search.
    [Fact]
    public async Task ResolveAsync_RefusesAStationThatFailsTheGates()
    {
        (FavouriteResolver resolver, _) =
            Build(StationCatalog.Empty(), Wire("http://example.com/insecure"));

        (await resolver.ResolveAsync(KnownUuid, CancellationToken.None)).Should().BeNull();
    }

    // A user-supplied station has a slug id. Asking radio-browser about a slug cannot
    // succeed, so the request is not made at all.
    [Fact]
    public async Task ResolveAsync_NeverAsksUpstreamAboutASlug()
    {
        (FavouriteResolver resolver, FakeHttpMessageHandler handler) = Build(StationCatalog.Empty());

        (await resolver.ResolveAsync("somafm-groove-salad", CancellationToken.None))
            .Should().BeNull();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNullRatherThanThrowingWhenUpstreamFails()
    {
        (FavouriteResolver resolver, _) = Build(StationCatalog.Empty(), fail: true);

        (await resolver.ResolveAsync(KnownUuid, CancellationToken.None)).Should().BeNull();
    }

    // An id radio-browser simply does not know is an empty array, not an error.
    [Fact]
    public async Task ResolveAsync_ReturnsNullWhenUpstreamKnowsNothing()
    {
        (FavouriteResolver resolver, _) = Build(StationCatalog.Empty(), "[]");

        (await resolver.ResolveAsync(KnownUuid, CancellationToken.None)).Should().BeNull();
    }
}
