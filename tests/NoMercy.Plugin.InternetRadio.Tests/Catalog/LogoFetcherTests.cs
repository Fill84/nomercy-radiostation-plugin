// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Net;
using System.Text;
using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using Xunit;
using NoMercy.Plugin.InternetRadio.Catalog;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public class LogoFetcherTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static LogoFetcher Fetcher(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        FakeHttpMessageHandler handler = new();
        handler.RespondPerRequest(respond);
        return new LogoFetcher(new HttpClient(handler));
    }

    private static HttpResponseMessage Bytes(byte[] body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new ByteArrayContent(body) };

    [Fact]
    public async Task ASmallImageBecomesADataUri()
    {
        LogoResult result = await Fetcher(_ => Bytes(Png)).FetchAsync("https://example.com/l.png", CancellationToken.None);

        result.DataUri.Should().Be($"data:image/png;base64,{Convert.ToBase64String(Png)}");
        result.IsFinal.Should().BeTrue();
    }

    // The cap is what keeps a page of logos a reasonable size; nothing is resized.
    [Fact]
    public async Task AnImageOverTheCapIsRefusedForGood()
    {
        byte[] big = [.. Png, .. new byte[LogoFetcher.MaxBytes]];

        LogoResult result = await Fetcher(_ => Bytes(big)).FetchAsync("https://example.com/l.png", CancellationToken.None);

        result.DataUri.Should().BeNull();
        result.IsFinal.Should().BeTrue();
    }

    [Fact]
    public async Task AnImageExactlyAtTheCapIsKept()
    {
        byte[] exact = [.. Png, .. new byte[LogoFetcher.MaxBytes - Png.Length]];

        LogoResult result = await Fetcher(_ => Bytes(exact)).FetchAsync("https://example.com/l.png", CancellationToken.None);

        result.DataUri.Should().StartWith("data:image/png;base64,");
    }

    [Fact]
    public async Task APageThatIsNotAnImageIsRefusedForGood()
    {
        LogoResult result = await Fetcher(_ => Bytes(Encoding.UTF8.GetBytes("<html>moved</html>")))
            .FetchAsync("https://example.com/l.png", CancellationToken.None);

        result.Should().Be(new LogoResult(null, IsFinal: true));
    }

    [Fact]
    public async Task AMissingLogoIsRefusedForGood()
    {
        LogoResult result = await Fetcher(_ => Bytes([], HttpStatusCode.NotFound))
            .FetchAsync("https://example.com/l.png", CancellationToken.None);

        result.Should().Be(new LogoResult(null, IsFinal: true));
    }

    // A server having a bad minute is not a verdict on the logo.
    [Fact]
    public async Task AServerErrorIsWorthTryingAgain()
    {
        LogoResult result = await Fetcher(_ => Bytes([], HttpStatusCode.BadGateway))
            .FetchAsync("https://example.com/l.png", CancellationToken.None);

        result.Should().Be(new LogoResult(null, IsFinal: false));
    }

    [Fact]
    public async Task ANetworkFailureIsWorthTryingAgain()
    {
        LogoResult result = await Fetcher(_ => throw new HttpRequestException("down"))
            .FetchAsync("https://example.com/l.png", CancellationToken.None);

        result.Should().Be(new LogoResult(null, IsFinal: false));
    }

    [Fact]
    public async Task TheCallersOwnCancellationStillCancels()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> fetch = () => Fetcher(_ => Bytes(Png)).FetchAsync("https://example.com/l.png", cts.Token);

        await fetch.Should().ThrowAsync<OperationCanceledException>();
    }
}
