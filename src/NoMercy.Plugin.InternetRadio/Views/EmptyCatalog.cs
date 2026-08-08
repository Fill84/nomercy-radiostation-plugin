// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

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

        return Ui.Container(
            "catalog-empty",
            Ui.Badge(
                "catalog-empty-badge",
                catalog.LastFetchFailed ? "Unavailable" : "Not downloaded yet",
                catalog.LastFetchFailed ? PluginBadgeVariant.Danger : PluginBadgeVariant.Info
            ),
            Ui.EmptyState("catalog-empty-state", "No stations", message),
            Ui.Button(
                "catalog-empty-retry",
                "Try again",
                PluginActionIntent.RefreshView()
            )
        );
    }
}
