// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Security.Claims;
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
    public const string SearchMethod = "search";

    public sealed record SearchRequest(string? Query);

    [HttpPost(ToggleFavouriteRouteTemplate)]
    public Task<IActionResult> ToggleFavourite(string stationId, CancellationToken ct) =>
        RespondAsync(plugin => plugin.ToggleFavouriteAsync(CurrentUserId(), stationId, ct));

    [HttpPost(SearchMethod)]
    public Task<IActionResult> Search([FromBody] SearchRequest request, CancellationToken ct) =>
        RespondAsync(plugin => plugin.StoreSearchAsync(CurrentUserId(), request.Query, ct));

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
