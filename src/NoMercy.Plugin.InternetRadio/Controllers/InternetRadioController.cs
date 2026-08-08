// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NoMercy.Plugins.Abstractions;
using NoMercy.Plugins.Mvc;

namespace NoMercy.Plugin.InternetRadio;

// The other half of an agreement with the views. Every route template here is a constant
// the view reads too, so the method string a button posts to and the route that answers
// it cannot drift into two different literals.
//
// IPluginManager is the sanctioned way to reach the live plugin: it is a host singleton,
// and ASP.NET Core builds this controller per request from that same container.
// IPluginServiceRegistrator runs during a pre-build discovery pass, against an instance
// created before IPluginContext exists, so it has nothing live to hand out.
public sealed class InternetRadioController(IPluginManager pluginManager) : PluginControllerBase
{
    public const string ToggleFavouriteRouteTemplate = "favourites/toggle/{stationId}";
    public const string ToggleFavouriteMethod = "favourites/toggle";
    public const string StreamRouteTemplate = "stream/{stationId}";
    public const string StreamMethod = "stream";
    public const string CoverRouteTemplate = "cover/{stationId}";
    public const string CoverMethod = "cover";
    public const string PlayerRouteTemplate = "player/{stationId}";
    public const string PlayerMethod = "player";


    [HttpPost(ToggleFavouriteRouteTemplate)]
    public Task<IActionResult> ToggleFavourite(string stationId, CancellationToken ct) =>
        RespondAsync(plugin => plugin.ToggleFavouriteAsync(CurrentUserId(), stationId, ct));

    public const string SearchMethod = "search";

    /// <summary>
    /// The search box's submit.
    ///
    /// The client posts the form's collected fields as the request body and then refreshes
    /// the view, so the term is stored and the refreshed page runs it. Earlier versions of
    /// this arrived with an empty body every time - not because the binding was wrong, but
    /// because the form was being rendered as a card and there was no form to collect. See
    /// Ui.
    /// </summary>
    [HttpPost(SearchMethod)]
    public Task<IActionResult> Search([FromBody] SearchRequest? request, CancellationToken ct) =>
        RespondAsync(plugin => plugin.StoreSearchAsync(CurrentUserId(), request?.Query, ct));

    /// <summary>The one field the search form carries.</summary>
    public sealed class SearchRequest
    {
        public string? Query { get; init; }
    }

    /// <summary>
    /// The station's audio, relayed. See FetchStationMediaAsync for why the browser must
    /// not be sent the station's own url.
    /// </summary>
    [HttpGet(StreamRouteTemplate)]
    public Task<IActionResult> Stream(string stationId, CancellationToken ct) =>
        RelayAsync(stationId, cover: false, ct);

    /// <summary>The station's logo, relayed for the same reason.</summary>
    [HttpGet(CoverRouteTemplate)]
    public Task<IActionResult> Cover(string stationId, CancellationToken ct) =>
        RelayAsync(stationId, cover: true, ct);

    /// <summary>
    /// A page with an audio element on it, for the station page to embed.
    ///
    /// This exists because the dashboard's own player cannot play plugin media at all. It
    /// builds a track id as `plugin:{pluginId}:{streamUrl}` and then puts that id into a
    /// CSS selector; a url contains colons and slashes, so the selector is invalid and the
    /// component throws before any audio is requested - the server never sees a single
    /// /stream/ call. Nothing a plugin puts in the payload avoids it: even an empty
    /// streamUrl leaves `plugin:{pluginId}:`, and the colons alone are enough. See
    /// docs/upstream/2026-08-08-plugin-media-cannot-play.md.
    ///
    /// So this plugin serves its own player and the station page embeds it. It is a
    /// browser's built-in audio element and nothing more - no queue, no cast, no
    /// now-playing - and it goes away the day the dashboard's player works.
    /// </summary>
    [HttpGet(PlayerRouteTemplate)]
    public async Task<IActionResult> Player(string stationId, CancellationToken ct)
    {
        if (pluginManager.GetPluginInstance(PluginId) is not InternetRadioPlugin plugin)
        {
            return NotFound();
        }

        RadioStation? station = await plugin.ResolveStationAsync(stationId, ct);

        if (station is null)
        {
            return NotFound();
        }

        Response.ContentType = "text/html; charset=utf-8";

        return Content(PlayerPage.Html(station), "text/html");
    }

    private async Task<IActionResult> RelayAsync(string stationId, bool cover, CancellationToken ct)
    {
        if (pluginManager.GetPluginInstance(PluginId) is not InternetRadioPlugin plugin)
        {
            return NotFound();
        }

        HttpResponseMessage? upstream = await plugin.FetchStationMediaAsync(
            stationId, cover, Request.Headers.Range.ToString(), ct);

        if (upstream is null)
        {
            return NotFound();
        }

        // The upstream status is passed through, not normalised to 200: a player asking
        // for a byte range needs the 206 and the Content-Range that answers it, or it
        // cannot seek and will not buffer.
        Response.StatusCode = (int)upstream.StatusCode;

        if (upstream.Content.Headers.ContentRange is { } contentRange)
        {
            Response.Headers.ContentRange = contentRange.ToString();
        }

        Response.Headers.AcceptRanges = "bytes";

        // The client requests media with crossorigin="anonymous", which makes the browser
        // discard a response that does not say who may read it - so without this the bytes
        // arrive and the image still draws as broken, with nothing in any log to say why.
        // The dashboard is a different origin from the server it talks to, so this is the
        // ordinary case rather than the exotic one. Safe to allow any origin: the url is
        // useless without the caller's own token, which is what actually gates it.
        Response.Headers.AccessControlAllowOrigin = "*";

        Response.ContentType =
            upstream.Content.Headers.ContentType?.ToString()
            ?? (cover ? "application/octet-stream" : "audio/mpeg");

        await using Stream body = await upstream.Content.ReadAsStreamAsync(ct);
        await body.CopyToAsync(Response.Body, ct);

        return new EmptyResult();
    }

    /// <summary>
    /// The same claim the server reads for its own controllers, so a plugin's idea of who
    /// is asking cannot disagree with the host's.
    ///
    /// Null rather than a shared fallback when the claim is missing or unparseable. The
    /// server's own helper answers Guid.Empty there, which for per-user data would mean
    /// every unauthenticated caller writing into one shared list - favourites belonging to
    /// nobody and visible to whoever landed in the same bucket.
    /// </summary>
    private string? CurrentUserId()
    {
        string? claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(claim, out Guid userId) && userId != Guid.Empty
            ? userId.ToString()
            : null;
    }

    private async Task<IActionResult> RespondAsync(
        Func<InternetRadioPlugin, Task<PluginActionOutcome>> act)
    {
        if (pluginManager.GetPluginInstance(PluginId) is not InternetRadioPlugin plugin)
        {
            return NotFound();
        }

        PluginActionOutcome outcome = await act(plugin);

        return outcome.Succeeded
            ? Status<object?>(null, message: outcome.Message)
            : Status<object?>(null, status: "error", message: outcome.Message);
    }
}
