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


    [HttpPost(ToggleFavouriteRouteTemplate)]
    public Task<IActionResult> ToggleFavourite(string stationId, CancellationToken ct) =>
        RespondAsync(plugin => plugin.ToggleFavouriteAsync(CurrentUserId(), stationId, ct));

    // No search endpoint. Searching is a navigation, not a call: the term is spelled into
    // the route a character at a time, because a submitted form posts an empty body in this
    // client - PluginComponentType.Form maps to NMCard, so there is no form to collect. See
    // docs/upstream/2026-08-08-plugin-form-submits-empty-body.md.

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
