// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Net;

namespace NoMercy.Plugin.InternetRadio.Tests.TestSupport;

// Every network failure this plugin has to survive, without a socket. The real
// HttpClient the host hands a plugin is wrapped in an allowlist handler that throws
// for a host the manifest never declared, so tests that reached the internet would
// be testing something the server does not do anyway.
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private Func<HttpRequestMessage, HttpResponseMessage>? _responder;

    public List<HttpRequestMessage> Requests { get; } = [];

    // Captured here, not read back off Requests[n].Content after the fact: the real
    // client disposes its HttpRequestMessage once SendAsync returns (correctly - the
    // body has been fully read by then), so anything that wants to assert on the body
    // has to capture it during the call, not after.
    public List<string> Bodies { get; } = [];

    public void Respond(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        _responder = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    public void RespondPerRequest(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    public void Fail(Exception exception) => _responder = _ => throw exception;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));

        if (_responder is null)
        {
            throw new InvalidOperationException("the test did not arrange a response");
        }

        return _responder(request);
    }
}
