// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;
using NoMercy.Plugin.InternetRadio.Catalog;

namespace NoMercy.Plugin.InternetRadio.Views;

// What to show when there are no stations at all.
//
// A blank grid reads as a broken plugin, so this says which of the two things went
// wrong - the catalogue has not been fetched yet, or fetching it failed - and offers
// the retry. refreshView costs nothing: re-rendering re-runs the cache-first read,
// which fetches when there is nothing cached.
public static class EmptyCatalog
{
    public static PluginComponent Build(StationCatalog catalog)
    {
        string message = catalog.LastFetchFailed
            ? "The station list could not be fetched from radio-browser.info. "
              + "Check the server log for Internet Radio, and that the server can reach the internet."
            : "The station list has not been downloaded yet. This happens on the first run "
              + "and after the plugin's data folder is cleared.";

        return PluginViews.Container(
            "catalog-empty",
            // Not resolved, so kept in English: NMBadge's own `text` prop is bound to
            // the DOM exactly as this payload writes it - only a leaf whose component
            // is NMText resolves through the plugin's bundle. See NMRender.vue.
            PluginViews.Badge(
                "catalog-empty-badge",
                catalog.LastFetchFailed ? "Unavailable" : "Not downloaded yet",
                catalog.LastFetchFailed ? PluginBadgeVariant.Danger : PluginBadgeVariant.Info
            ),
            PluginViews.EmptyState("catalog-empty-state", "stations.empty", message),
            PluginViews.Button(
                "catalog-empty-retry",
                "stations.retry",
                PluginActionIntent.RefreshView()
            )
        );
    }
}
