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
    public const string SearchMethod = "search";
    public const string ClearSearchMethod = "search/clear";

    public const string StreamRouteTemplate = "stream/{stationId}";
    public const string StreamMethod = "stream";
    public const string CoverRouteTemplate = "cover/{stationId}";
    public const string CoverMethod = "cover";


    [HttpPost(ToggleFavouriteRouteTemplate)]
    public Task<IActionResult> ToggleFavourite(string stationId, CancellationToken ct) =>
        RespondAsync(plugin => plugin.ToggleFavouriteAsync(CurrentUserId(), stationId, ct));

    /// <summary>
    /// The search form's submit.
    ///
    /// The body is read raw and searched for the field rather than bound to a model. Two
    /// model shapes were tried - a positional record and a class with init-only properties,
    /// the latter copied from the torrent plugin where it demonstrably works - and both
    /// bound to null while the field on screen plainly held a term. Rather than guess at a
    /// third shape, this takes whatever arrives and looks for the field in it, at the top
    /// level or one level down, however it is cased.
    ///
    /// The raw body is logged once per submit. That is deliberate: a search that silently
    /// clears itself is indistinguishable on screen from one that ran and found nothing, so
    /// without this the next person to hit it has nothing to go on either.
    /// </summary>
    [HttpPost(SearchMethod)]
    public async Task<IActionResult> Search(CancellationToken ct)
    {
        using StreamReader reader = new(Request.Body);
        string body = await reader.ReadToEndAsync(ct);

        return await RespondAsync(plugin =>
            plugin.StoreSearchAsync(CurrentUserId(), FindQuery(body), body, ct));
    }

    /// <summary>
    /// The search term out of whatever the client sent.
    ///
    /// Case-insensitive, and one level deep, because a payload that wraps its fields is as
    /// likely as one that does not and neither is worth another round trip to find out.
    /// </summary>
    internal static string? FindQuery(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            return Find(document.RootElement, depth: 0);
        }
        catch (JsonException)
        {
            // Not JSON at all - a form-encoded body is the other thing a submit could be.
            foreach (string pair in body.Split('&'))
            {
                string[] parts = pair.Split('=', 2);

                if (parts.Length == 2
                    && parts[0].Equals(SearchView.FieldName, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(parts[1].Replace('+', ' '));
                }
            }

            return null;
        }
    }

    private static string? Find(JsonElement element, int depth)
    {
        if (element.ValueKind is not JsonValueKind.Object || depth > 1)
        {
            return null;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.NameEquals(SearchView.FieldName)
                || property.Name.Equals(SearchView.FieldName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind is JsonValueKind.String
                    ? property.Value.GetString()
                    : null;
            }
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (Find(property.Value, depth + 1) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    // Its own endpoint rather than the form submitting an empty value: a plain button
    // carries nothing but its path, and clearing is a button.
    [HttpPost(ClearSearchMethod)]
    public Task<IActionResult> ClearSearch(CancellationToken ct) =>
        RespondAsync(plugin => plugin.StoreSearchAsync(CurrentUserId(), null, ct));

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
