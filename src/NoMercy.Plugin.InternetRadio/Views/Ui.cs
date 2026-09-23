// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;

namespace NoMercy.Plugin.InternetRadio.Views;

// The two components the design system has no builder for.
//
// Everything else a view draws comes from PluginViews, which is the SDK's own builder and
// the only path the client resolves translations through. NMGrid and NMMusicCard are app
// components on the client's allow-list - the same shelf Music draws its own artists on -
// and a plugin cannot express them any other way.
public static class Ui
{
    // The components the app draws its own screens with. A plugin reaches these the same
    // way it reaches any other node - PluginNode resolves the plugin map first and then
    // the app's own - so a plugin's grid can be the grid, rather than a lookalike.
    public const string MediaGridComponent = "NMGrid";
    public const string MediaCardComponent = "NMMusicCard";

    /// <summary>
    /// The grid the app lays its own libraries out with: two columns on a phone, five to
    /// nine as the window widens, six on a television. A plugin cannot express that itself
    /// and should not try - the whole point is that a plugin's shelf is the same shelf.
    /// </summary>
    public static PluginComponent MediaGrid(string id, IEnumerable<PluginComponent> items) =>
        new()
        {
            Id = id,
            Component = MediaGridComponent,
            // On the props as well as the envelope, for the same reason the card
            // carries it: the app's NMGrid requires it there, and without it the
            // shelf fails to decode and draws nothing at all.
            Props = new() { ["id"] = id },
            Items = [.. items],
        };

    /// <summary>
    /// One tile in that grid, drawn exactly as an artist or an album is.
    ///
    /// It navigates rather than acting: the card is a RouterLink to `link`, which is how
    /// every card in the app behaves. Whatever you can do with the thing lives on the page
    /// it opens.
    /// </summary>
    public static PluginComponent MediaCard(
        string id,
        string name,
        string link,
        string? cover,
        string? type = null) =>
        new()
        {
            Id = id,
            Component = MediaCardComponent,
            Props = new()
            {
                // The id belongs on the props as well as inside data. The app's
                // NMMusicCard requires it there, and a card without it fails the
                // whole shelf's decode - the grid then arrives with no items,
                // draws nothing, and reports nothing anywhere.
                ["id"] = id,
                ["data"] = new Dictionary<string, object?>
                {
                    ["id"] = id,
                    ["name"] = name,
                    ["link"] = link,
                    ["cover"] = cover,
                    ["type"] = type,
                },
            },
        };
}
