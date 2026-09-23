// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NoMercy.PluginSdk.Abstractions;
using NoMercy.PluginSdk.Mvc;
using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.State;
using NoMercy.Plugin.InternetRadio.Views;

namespace NoMercy.Plugin.InternetRadio.Controllers;

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
    /// because the client rendered the form as a card, with no &lt;form&gt; element to
    /// collect from. Fixed upstream: the form is a real PluginForm now, and its fields
    /// arrive as posted. See PluginViews.Form, which SearchView.Field builds it with.
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

    public const string NowPlayingRouteTemplate = "nowplaying/{stationId}";
    public const string NowPlayingMethod = "nowplaying";

    /// <summary>
    /// What the station is playing right now, as it last announced over ICY.
    ///
    /// Null title rather than a 404 when nothing has been announced: a station that
    /// sends no metadata, and a station whose next block has not arrived yet, are the
    /// same thing to a listener, and neither is an error to draw.
    ///
    /// Each poll also renews the lease on the station's metadata connection; see NowPlayingListener.
    /// </summary>
    [HttpGet(NowPlayingRouteTemplate)]
    public async Task<IActionResult> NowPlaying(string stationId, CancellationToken ct)
    {
        if (pluginManager.GetPluginInstance(PluginId) is not InternetRadioPlugin plugin)
        {
            return NotFound();
        }

        string? announced = await plugin.NowPlayingForAsync(stationId, ct);
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
    /// The same claim the server reads for its own controllers, so a plugin's idea of who
    /// is asking cannot disagree with the host's.
    ///
    /// Null rather than a shared fallback when the claim is missing or unparseable. The
    /// server's own helper answers Guid.Empty there, which for per-user data would mean
    /// every unauthenticated caller writing into one shared list - favourites belonging to
    /// nobody and visible to whoever landed in the same bucket.
    ///
    /// Not <c>PluginControllerBase.Caller</c>: the host never populates it, and reading it throws.
    /// </summary>
    private string? CurrentUserId() =>
        UserKey.FromClaim(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

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
