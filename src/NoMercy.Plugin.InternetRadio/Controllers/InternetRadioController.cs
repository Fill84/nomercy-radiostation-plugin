// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    public const string NowPlayingRouteTemplate = "nowplaying/{stationId}";
    public const string NowPlayingMethod = "nowplaying";


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
    /// What this station is announcing right now.
    ///
    /// The client polls this only because the play intent told it to, and only while the
    /// station is the item on air. The route shape - method then resource id - is the
    /// client's, not ours: it builds the url from the item's own id.
    ///
    /// Answers 200 with no data rather than 404 when the station is announcing nothing.
    /// A station between tracks is the ordinary case, and a 404 for it puts a red line in
    /// the console every thirty seconds for something that is working correctly.
    /// </summary>
    [HttpGet(NowPlayingRouteTemplate)]
    public async Task<IActionResult> NowPlaying(string stationId, CancellationToken ct)
    {
        if (pluginManager.GetPluginInstance(PluginId) is not InternetRadioPlugin plugin)
        {
            return NotFound();
        }

        if (await plugin.NowPlayingAsync(stationId, ct) is not { } announced)
        {
            return Status<object?>(null);
        }

        // `title` is the whole announced line, `artist` and `track` its two halves. The
        // client reads `track ?? title`, so a station that announces one line still gets a
        // title, and one that names an artist gets both on their own lines.
        return Status(new
        {
            title = announced.Artist is null
                ? announced.Track
                : $"{announced.Artist} - {announced.Track}",
            artist = announced.Artist,
            track = announced.Track,
        });
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

        // A live stream has no end, and a response that is only ever written to - never
        // started, never flushed - is delivered when the body completes. For a cover that
        // is the same instant. For a stream it is never: the browser holds an open request
        // that yields no headers and no bytes, so the audio element sits at readyState 0
        // with no metadata, no duration and - this is the cruel part - no error either.
        // On screen that is a station in Now Playing stuck at 00:00 / 00:00 in silence,
        // which is indistinguishable from a dozen other faults and was read as all of
        // them in turn. Starting the response sends the headers now.
        await Response.StartAsync(ct);

        await using Stream body = await upstream.Content.ReadAsStreamAsync(ct);

        // 8 KB, not CopyToAsync's 80 KB. At 128 kbps a full 80 KB buffer is five seconds
        // of audio withheld before the first byte moves, and the element wants frames
        // sooner than that to call the source playable.
        byte[] buffer = new byte[8192];
        long relayed = 0;

        try
        {
            int read;
            while ((read = await body.ReadAsync(buffer, ct)) > 0)
            {
                await Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);

                // Per chunk, because the point of a relay is that the listener hears the
                // stream now rather than whenever a buffer somewhere upstream fills.
                await Response.Body.FlushAsync(ct);

                if (relayed == 0)
                {
                    plugin.RelayLogger.LogInformation(
                        "Internet Radio relay: {Kind} for {StationId} - first {Bytes} bytes reached the client.",
                        cover ? "cover" : "stream", stationId, read);
                }

                relayed += read;
            }
        }
        catch (OperationCanceledException)
        {
            // The listener pressed stop or navigated away. Ordinary, not a fault: a live
            // stream only ever ends because someone stopped listening.
            plugin.RelayLogger.LogInformation(
                "Internet Radio relay: {Kind} for {StationId} - listener disconnected after {Bytes} bytes.",
                cover ? "cover" : "stream", stationId, relayed);
        }

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
