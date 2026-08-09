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
        RespondAsync(plugin =>
            plugin.StoreSearchAsync(
                CurrentUserId(),
                request?.Query,
                request?.Genre,
                request?.Country,
                request?.Language,
                ct
            ));

    /// <summary>The one field the search form carries.</summary>
    public sealed class SearchRequest
    {
        public string? Query { get; init; }

        /// <summary>
        /// The rest of the form. Each is optional and they combine, so a listener
        /// can ask for a genre from a country without naming a station at all -
        /// which is most of what anyone wants from a database of this size.
        /// </summary>
        public string? Genre { get; init; }

        public string? Country { get; init; }

        public string? Language { get; init; }
    }

    /// <summary>
    /// The station's audio, relayed. See FetchStationMediaAsync for why the browser must
    /// not be sent the station's own url.
    /// </summary>
    [HttpGet(StreamRouteTemplate)]
    public Task<IActionResult> Stream(string stationId, CancellationToken ct) =>
        RelayAsync(stationId, cover: false, ct);

    public const string NowPlayingRouteTemplate = "nowplaying/{stationId}";
    public const string NowPlayingMethod = "nowplaying";

    /// <summary>
    /// What the station is playing right now, as it last announced over ICY.
    ///
    /// Null title rather than a 404 when nothing has been announced: a station that
    /// sends no metadata, and a station whose next block has not arrived yet, are the
    /// same thing to a listener, and neither is an error to draw.
    /// </summary>
    [HttpGet(NowPlayingRouteTemplate)]
    public IActionResult NowPlaying(string stationId)
    {
        if (pluginManager.GetPluginInstance(PluginId) is not InternetRadioPlugin plugin)
        {
            return NotFound();
        }

        string? announced = plugin.NowPlaying.Get(stationId);
        StreamTitle? parsed = announced is null ? null : StreamTitle.Parse(announced);

        // `title` stays the whole announced line so a client written against the
        // earlier shape keeps working; artist and track are additions beside it.
        return Status<object>(new
        {
            title = announced,
            artist = parsed?.Artist,
            track = parsed?.Track
        });
    }

    /// <summary>
    /// The station's own <c>icy-metaint</c>, when it agreed to send metadata.
    /// </summary>
    private static int? MetaInterval(HttpResponseMessage upstream)
    {
        if (!upstream.Headers.TryGetValues("icy-metaint", out IEnumerable<string>? values))
        {
            return null;
        }

        return int.TryParse(values.FirstOrDefault(), out int interval) && interval > 0
            ? interval
            : null;
    }

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

        await using Stream upstreamBody = await upstream.Content.ReadAsStreamAsync(ct);

        // A station that answered our Icy-MetaData ask splices its current track into
        // the audio every icy-metaint bytes. The browser must never see those bytes -
        // they are not audio - so they come out here, and the title they carry is
        // remembered for whoever asks what is playing.
        Stream body = MetaInterval(upstream) is { } interval
            ? new IcyMetadataStream(
                upstreamBody, interval, title => plugin.NowPlaying.Set(stationId, title))
            : upstreamBody;

        using IDisposable? listener = cover ? null : plugin.NowPlaying.Listen(stationId);

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
