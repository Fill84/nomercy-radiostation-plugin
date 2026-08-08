// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Net;
using System.Text.Json;
using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public class RadioBrowserClientTests
{
    private const string OneStation = """
        [{
          "stationuuid": "960cf833-0601-11e8-ae97-52543be04c81",
          "name": "Example FM",
          "url": "https://example.com/a",
          "url_resolved": "https://cdn.example.com/a",
          "homepage": "https://example.com",
          "favicon": "https://example.com/logo.png",
          "tags": "ambient,chillout",
          "countrycode": "NL",
          "language": "english",
          "codec": "MP3",
          "bitrate": 128,
          "hls": 0,
          "lastcheckok": 1,
          "votes": 42
        }]
        """;

    private static (RadioBrowserClient Client, FakeHttpMessageHandler Handler) Build()
    {
        FakeHttpMessageHandler handler = new();
        HttpClient http = new(handler);
        return (new RadioBrowserClient(http), handler);
    }

    [Fact]
    public async Task GetByUuidsAsync_ReadsEveryFieldTheViewsNeed()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond(OneStation);

        IReadOnlyList<RadioBrowserStation> stations =
            await client.GetByUuidsAsync(["960cf833-0601-11e8-ae97-52543be04c81"], CancellationToken.None);

        RadioBrowserStation station = stations.Should().ContainSingle().Subject;
        station.Name.Should().Be("Example FM");
        station.Url.Should().Be("https://example.com/a");
        station.UrlResolved.Should().Be("https://cdn.example.com/a");
        station.Homepage.Should().Be("https://example.com");
        station.Favicon.Should().Be("https://example.com/logo.png");
        station.Tags.Should().Be("ambient,chillout");
        station.CountryCode.Should().Be("NL");
        station.Language.Should().Be("english");
        station.Codec.Should().Be("MP3");
        station.Bitrate.Should().Be(128);
        // Gate-critical: a broken [JsonPropertyName("hls")] would ship silently and
        // every HLS stream - unplayable outside Safari in a plain audio element -
        // would be admitted instead of rejected.
        station.Hls.Should().Be(0);
        station.LastCheckOk.Should().Be(1);
        station.Votes.Should().Be(42);
    }

    // One POST for all ten seeds rather than ten GETs. Verified against the live API
    // before this was designed: the endpoint takes a comma-separated uuids field.
    [Fact]
    public async Task GetByUuidsAsync_AsksForEverySeedInOneRequest()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond(OneStation);

        await client.GetByUuidsAsync(["aaa", "bbb", "ccc"], CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        HttpRequestMessage request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsoluteUri.Should().EndWith("/json/stations/byuuid");
        // Decoded rather than compared against the raw wire form: this tests the
        // requirement (every seed reaches the server, comma-joined), not which of
        // the equally-valid form encodings of a comma the encoder happened to pick.
        Uri.UnescapeDataString(handler.Bodies[0]).Should().Contain("uuids=aaa,bbb,ccc");
    }

    [Fact]
    public async Task GetByUuidsAsync_MakesNoRequestForAnEmptySeedList()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();

        IReadOnlyList<RadioBrowserStation> stations =
            await client.GetByUuidsAsync([], CancellationToken.None);

        stations.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchByTagAsync_QueriesTheTagExactlyAndLimitsIt()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond(OneStation);

        await client.SearchByTagAsync("drum and bass", 5, CancellationToken.None);

        string url = handler.Requests.Should().ContainSingle().Subject.RequestUri!.AbsoluteUri;
        url.Should().Contain("/json/stations/search");
        // Exact matching, or "rock" also returns every station tagged "rockabilly".
        url.Should().Contain("tagExact=true");
        url.Should().Contain("tag=drum%20and%20bass");
        url.Should().Contain("limit=5");
        // Cheap server-side pre-filtering. The gates still run: this narrows the
        // response, it does not decide admission.
        url.Should().Contain("hidebroken=true");
        url.Should().Contain("is_https=true");
        url.Should().Contain("order=votes");
        // order=votes alone sorts ascending - least popular first. Without reverse,
        // "most-voted first" (the whole point of this ordering) silently inverts.
        url.Should().Contain("reverse=true");
    }

    // radio-browser asks callers to identify themselves. Set per request rather than
    // on DefaultRequestHeaders: the HttpClient belongs to the host and is shared, so
    // mutating it would leak this plugin's identity onto another plugin's traffic.
    [Fact]
    public async Task Requests_LeaveTheirIdentityToTheHost()
    {
        // The host stamps the owner's configured user agent and this plugin's
        // attribution on the way out. A plugin setting its own would be choosing
        // what a third party sees the owner's server as.
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond(OneStation);

        await client.SearchByTagAsync("ambient", 5, CancellationToken.None);

        handler.Requests[0].Headers.UserAgent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Throws_WhenTheApiReturnsAnError(HttpStatusCode status)
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond("nope", status);

        await FluentActions
            .Awaiting(() => client.SearchByTagAsync("ambient", 5, CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Throws_WhenTheTransportFails()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Fail(new HttpRequestException("dns is having a day"));

        await FluentActions
            .Awaiting(() => client.SearchByTagAsync("ambient", 5, CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Throws_WhenTheBodyIsNotJson()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond("<html>a captive portal, probably</html>");

        await FluentActions
            .Awaiting(() => client.SearchByTagAsync("ambient", 5, CancellationToken.None))
            .Should().ThrowAsync<JsonException>();
    }

    // A real captive portal does not lie about its media type - it serves text/html,
    // truthfully, unlike Respond() above which always labels the body
    // application/json. Checked empirically: the ReadFromJsonAsync(JsonSerializerOptions,
    // CancellationToken) overload this client uses does not inspect Content-Type at
    // all, so a truthfully-labelled text/html body fails exactly the same way as a
    // mislabelled one - JsonException, not NotSupportedException. That is worth
    // pinning down with a test of its own rather than assuming it, because it means
    // Task 7 gets to treat "bad JSON" and "not JSON at all" as one case, and a future
    // change to add real Content-Type checking here would be a deliberate,
    // test-visible decision instead of a silent behaviour change.
    [Fact]
    public async Task Throws_WhenTheBodyIsHtmlRatherThanJson()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.RespondPerRequest(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "<html>a captive portal, probably</html>",
                System.Text.Encoding.UTF8,
                "text/html"
            ),
        });

        await FluentActions
            .Awaiting(() => client.SearchByTagAsync("ambient", 5, CancellationToken.None))
            .Should().ThrowAsync<JsonException>();
    }

    // An empty result is an answer, not a failure. A tag nobody uses returns [], and
    // that must not be treated the same way as the API being down - one means "no
    // stations here", the other means "do not throw the cache away".
    [Fact]
    public async Task ReturnsEmpty_WhenTheApiReturnsNoStations()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond("[]");

        IReadOnlyList<RadioBrowserStation> stations =
            await client.SearchByTagAsync("ambient", 5, CancellationToken.None);

        stations.Should().BeEmpty();
    }

    [Fact]
    public async Task ReturnsEmpty_WhenTheApiReturnsJsonNull()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond("null");

        IReadOnlyList<RadioBrowserStation> stations =
            await client.SearchByTagAsync("ambient", 5, CancellationToken.None);

        stations.Should().BeEmpty();
    }

    // A record missing fields it usually sends must still parse: every optional
    // property on the DTO is nullable or defaulted precisely so one sparse row does
    // not cost the whole response.
    [Fact]
    public async Task ParsesARecordMissingItsOptionalFields()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond("""[{"stationuuid":"a","name":"Bare FM"}]""");

        RadioBrowserStation station =
            (await client.SearchByTagAsync("ambient", 5, CancellationToken.None)).Should().ContainSingle().Subject;

        station.Name.Should().Be("Bare FM");
        station.Url.Should().BeNull();
        station.Bitrate.Should().Be(0);
    }

    // stationuuid and name used to be `required`, so System.Text.Json enforced them
    // during deserialization itself and one row missing either threw JsonException
    // out of this client - costing the whole response, all ten seeds or an entire
    // genre, to one malformed record. Both are nullable now precisely so admission
    // (StationGates.Admits), not parsing, is what rejects a row like this.
    [Fact]
    public async Task ParsesTheGoodRowsWhenAnotherRowIsMissingARequiredField()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond("""
            [
              {"stationuuid":"a","name":"Good FM","url":"https://example.com/a","hls":0,"lastcheckok":1},
              {"url":"https://example.com/bad","hls":0,"lastcheckok":1}
            ]
            """);

        IReadOnlyList<RadioBrowserStation> stations =
            await client.SearchByTagAsync("ambient", 5, CancellationToken.None);

        stations.Should().HaveCount(2);
        stations.Should().Contain(station => station.Name == "Good FM");
        stations.Should().Contain(station => station.StationUuid == null);
    }

    [Fact]
    public async Task PropagatesCancellation()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond(OneStation);
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await FluentActions
            .Awaiting(() => client.SearchByTagAsync("ambient", 5, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SearchByNameAsync_AsksForTheMostVotedPlayableMatches()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond("[]");

        await client.SearchByNameAsync("groove salad", 50, CancellationToken.None);

        Uri asked = handler.Requests.Should().ContainSingle().Subject.RequestUri!;
        asked.AbsolutePath.Should().Be("/json/stations/search");
        asked.Query.Should().Contain("name=groove%20salad")
            .And.Contain("limit=50")
            .And.Contain("order=votes")
            .And.Contain("reverse=true")
            .And.Contain("hidebroken=true");
    }

    // Searching by name must not carry tagExact, which would make every query an exact
    // tag match and return nothing for a partial station name.
    [Fact]
    public async Task SearchByNameAsync_DoesNotSendTagFilters()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond("[]");

        await client.SearchByNameAsync("soma", 50, CancellationToken.None);

        handler.Requests.Should().ContainSingle().Subject
            .RequestUri!.Query.Should().NotContain("tag");
    }

    // radio-browser having a bad minute is a search that reports itself as failed, not
    // an exception escaping into the view that renders it.
    [Fact]
    public async Task SearchByNameAsync_ThrowsOnAFailedResponseSoTheCallerCanReportIt()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Fail(new HttpRequestException("down"));

        await FluentActions
            .Awaiting(() => client.SearchByNameAsync("x", 50, CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
    }
}
