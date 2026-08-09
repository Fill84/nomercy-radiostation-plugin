// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

// A plugin route as the app's own router sees it.
//
// Two different addresses for the same page, and mixing them up is a dead link.
// PluginActionIntent.Navigate takes the plugin-relative route ("/station/x") and the client
// prefixes it, because the client knows which plugin it is rendering. The app components -
// NMMusicCard and anything else that carries its own `link` - are handed straight to the
// app's router, which knows nothing about that context and needs the whole path.
public static class AppRoutes
{
    /// <summary>
    /// Where the host mounts this plugin's pages.
    ///
    /// A plugin placed by its kind lives under its section, not under the bare /plugins
    /// path: this one declares PluginUiSection.Music, so it is mounted at
    /// /music/plugins/{id} and that is where its own links have to point. Linking to
    /// /plugins/{id} lands on a route the section navigation does not know, which renders
    /// an empty page - the plugin is there, and nothing is drawn.
    /// </summary>
    public const string Mount = "/music/plugins";

    /// <summary>
    /// <paramref name="pluginRoute"/> as an absolute app path.
    /// </summary>
    public static string Of(string pluginRoute) =>
        $"{Mount}/{PluginIdentity.Id}/{pluginRoute.TrimStart('/')}";

    /// <summary>One station's page, for a card that navigates rather than acts.</summary>
    public static string Station(string stationId) => Of(RadioRoutes.Station(stationId));
}
