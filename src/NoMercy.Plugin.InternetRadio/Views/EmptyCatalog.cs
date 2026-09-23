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
            // The title WOULD resolve - EmptyState's title becomes a plain text leaf
            // nested under an NMContentHeader, the same shape a page title takes - but
            // it stays English anyway: every ViewFixtures.All() fixture builds a
            // non-empty StationCatalog, so this branch is never reached by
            // TranslationKeys.Drawn(), and a key nothing in the shared fixtures ever
            // draws is exactly the failure TranslationTests.EveryKeyInTheBundleIsOneAViewDraws
            // exists to catch. The message beside it stays English regardless - it
            // lands on an NMHelper's `helperText`, which NMRender never resolves.
            PluginViews.EmptyState("catalog-empty-state", "No stations", message),
            // Also not resolved: a button's visible label does draw through a resolved
            // text leaf, but PluginViews.Button copies the same string into `ariaLabel`
            // first, and that prop is bound raw. Keying the label would translate what
            // the eye reads and leave the key itself in the accessible name - worse for
            // a screen reader than shipping this one button in English. Every button and
            // icon-button label in this plugin is left English for the same reason.
            PluginViews.Button(
                "catalog-empty-retry",
                "Try again",
                PluginActionIntent.RefreshView()
            )
        );
    }
}
