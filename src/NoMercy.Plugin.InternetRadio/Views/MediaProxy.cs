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
    private static string? _token;

    public static void Remember(string? absoluteBase, string? bearerToken)
    {
        if (!string.IsNullOrWhiteSpace(absoluteBase))
        {
            _base = absoluteBase.TrimEnd('/');
        }

        _token = string.IsNullOrWhiteSpace(bearerToken) ? null : bearerToken;
    }

    /// <summary>Null when nothing has told us where this server lives yet.</summary>
    /// <summary>
    /// The audio, WITHOUT a token.
    ///
    /// The player appends its own access_token to whatever url it is given - it has to,
    /// since an audio element cannot send a header. Adding ours as well produced two
    /// access_token parameters, which ASP.NET joins with a comma, and the server then
    /// failed to decode the result as a JWT: "IDX14309: Unable to decode the
    /// initialization vector". The stream arrived and was refused for having a token that
    /// was two tokens.
    /// </summary>
    public static string? Stream(string stationId) =>
        Url(InternetRadioController.StreamMethod, stationId, withToken: false);

    /// <summary>
    /// The cover, WITH a token.
    ///
    /// An img element gets no such treatment - the app appended only width and type to
    /// ours and no credential, and the request came back 401. So this one has to carry it.
    /// </summary>
    public static string? Cover(string stationId) =>
        Url(InternetRadioController.CoverMethod, stationId, withToken: true);

    // The token travels in the query string, because it has to.
    //
    // An <audio> or <img> element cannot send an Authorization header, and the server's
    // AccessLogMiddleware refuses anything without a user claim before routing ever runs -
    // so the first version of these urls came back 401 and drew nothing. The host provides
    // for exactly this: TokenParamAuthMiddleware promotes ?token= and ?access_token= to a
    // header for every request, which is how the app plays its own library media too.
    //
    // It is the calling viewer's own token, read from the request this view is being
    // rendered for, so a url is never valid for anyone the viewer is not.
    private static string? Url(string method, string stationId, bool withToken)
    {
        if (_base is null)
        {
            return null;
        }

        string url =
            $"{_base}/api/v1/plugins/{PluginIdentity.Id}/{method}/{Uri.EscapeDataString(stationId)}";

        // The trailing parameter is not decoration. The now-playing panel appends its own
        // sizing to whatever url it is given without checking whether there is already a
        // query string on it, so ours came back as
        //
        //     ?access_token=<jwt>?width=500&type=avif
        //
        // and the token was then the jwt with "?width=500" glued to the end of it - a token
        // that is not a token, answered with 401, which is why the cover in the player was
        // blank while the same cover in the grid was fine. A last parameter with an empty
        // value absorbs the append: `size` becomes "?width=500" and access_token stays
        // exactly what it was.
        return !withToken || _token is null
            ? url
            : $"{url}?access_token={Uri.EscapeDataString(_token)}&size=";
    }
}
