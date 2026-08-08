// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

// Where the browser should ask for a station's audio and cover.
//
// The dashboard's Content-Security-Policy allows media and images from NoMercy's own
// hosts and nowhere else, so a station's own url reaches the browser and is refused. These
// point at this plugin's own endpoints instead, which sit on the server's origin - one the
// policy already allows.
//
// The base has to be absolute. A relative path would resolve against the page's origin
// (app.nomercy.tv), not the server the page is talking to, and land on nothing.
//
// Set once from the first request that carries an HttpContext, because the server's public
// address does not vary per user or per view - and read from many places deep inside the
// views, which is the only reason this is static rather than threaded through every Build.
// Null until then, and every caller falls back to the station's own url: unproxied audio
// is refused by the policy, but a plugin that renders nothing at all would be worse.
public static class MediaProxy
{
    private static string? _base;

    public static void Remember(string? absoluteBase)
    {
        if (!string.IsNullOrWhiteSpace(absoluteBase))
        {
            _base = absoluteBase.TrimEnd('/');
        }
    }

    /// <summary>Null when nothing has told us where this server lives yet.</summary>
    public static string? Stream(string stationId) => Url(InternetRadioController.StreamMethod, stationId);

    /// <inheritdoc cref="Stream" />
    public static string? Cover(string stationId) => Url(InternetRadioController.CoverMethod, stationId);

    private static string? Url(string method, string stationId) =>
        _base is null
            ? null
            : $"{_base}/api/v1/plugins/{PluginIdentity.Id}/{method}/{Uri.EscapeDataString(stationId)}";
}
