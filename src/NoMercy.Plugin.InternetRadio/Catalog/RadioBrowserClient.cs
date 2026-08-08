// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Net.Http.Json;
using System.Text.Json;

namespace NoMercy.Plugin.InternetRadio;

// The only thing in this plugin that reaches the network.
//
// Deliberately thin: two calls, no retry, no caching, no swallowing. It throws what
// went wrong and CatalogProvider decides what that means, because "the API is down"
// and "this tag has no stations" need opposite responses and only the caller knows
// whether it has a cache to fall back on.
//
// The HttpClient comes from IPluginContext and is bounded by the manifest's declared
// hosts, so a request to anywhere but *.api.radio-browser.info throws
// PluginNetworkDeniedException before it leaves the process. That is the enforcement
// point; this class does not re-implement it.
public sealed class RadioBrowserClient(HttpClient http)
{
    // 'all' is radio-browser's round-robin across its mirrors, which is what they ask
    // clients to use rather than pinning one. The manifest's *.api.radio-browser.info
    // covers it and every mirror it can hand back.
    public const string BaseAddress = "https://all.api.radio-browser.info";

    // radio-browser asks callers to identify themselves so they can contact whoever
    // is hammering them. Built from PluginIdentity.Version rather than a literal, so
    // a release that bumps the csproj and plugin.json bumps this too - those three
    // are already required to agree (see ManifestTests), and a hand-copied literal
    // here would be a fourth place to remember and the one nothing checks.

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// The pinned seeds, in one request. A POST because the uuid list is a body
    /// field, and ten GETs would be ten round trips for one screen.
    /// </summary>
    public async Task<IReadOnlyList<RadioBrowserStation>> GetByUuidsAsync(
        IReadOnlyList<string> uuids,
        CancellationToken ct
    )
    {
        if (uuids.Count == 0)
        {
            return [];
        }

        using HttpRequestMessage request = Request(HttpMethod.Post, "/json/stations/byuuid");
        request.Content = new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("uuids", string.Join(',', uuids))]
        );

        return await SendAsync(request, ct);
    }

    /// <summary>
    /// One genre's stations, most-voted first.
    /// <para>
    /// <c>tagExact</c> is on because substring matching puts every "rockabilly"
    /// station in Rock. <c>hidebroken</c> and <c>is_https</c> are server-side
    /// pre-filtering that keeps the response small; they do not decide admission —
    /// <see cref="StationGates"/> still runs over everything that comes back, and it
    /// has to, because radio-browser has been observed reporting a 404 stream as
    /// healthy.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<RadioBrowserStation>> SearchByTagAsync(
        string tag,
        int limit,
        CancellationToken ct
    )
    {
        string query = string.Join(
            '&',
            $"tag={Uri.EscapeDataString(tag)}",
            "tagExact=true",
            $"limit={limit.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            "order=votes",
            "reverse=true",
            "hidebroken=true",
            "is_https=true"
        );

        using HttpRequestMessage request = Request(HttpMethod.Get, $"/json/stations/search?{query}");
        return await SendAsync(request, ct);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path)
    {
        HttpRequestMessage request = new(method, $"{BaseAddress}{path}");

        // No user agent here. The host sets it on the way out: the owner's
        // configured identity plus this plugin's attribution. A plugin choosing
        // what a third party sees would be choosing who the owner's server is.
        return request;
    }

    private async Task<IReadOnlyList<RadioBrowserStation>> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        using HttpResponseMessage response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        List<RadioBrowserStation>? stations =
            await response.Content.ReadFromJsonAsync<List<RadioBrowserStation>>(JsonOptions, ct);

        // A JSON `null` body parses to null rather than throwing. Empty is the honest
        // reading of it, and it keeps every caller off a null check.
        return stations ?? [];
    }
}
