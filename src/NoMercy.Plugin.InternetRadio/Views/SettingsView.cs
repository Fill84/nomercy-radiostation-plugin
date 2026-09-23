// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;
using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.State;

namespace NoMercy.Plugin.InternetRadio.Views;

// Status, not settings.
//
// This plugin ships a real REST controller (InternetRadioController, three routes)
// and declares "rest": true - the earlier gap that once blocked an editable page
// (the server's plugin REST routes mounted unversioned while the client posted to
// /api/v1) was fixed upstream on 2026-07-30, media-server issue #26. Nothing here
// is missing a controller to save through.
//
// What this page lacks is a setting worth having: there is nothing yet that this
// plugin decides which an owner would plausibly want to override from a form -
// the catalogue is fetched, not curated, and the one input an owner does have is
// already a file, not a field (stations.json, described below). So this page stays
// a read-only view of what the plugin decided, and answers the questions someone
// actually arrives with instead: where did these stations come from, how old are
// they, and how do I add my own.
public static class SettingsView
{
    private static IReadOnlyList<PluginTableColumn> Columns { get; } =
        [
            new() { Key = "genre", Label = "settings.genre" },
            new() { Key = "stations", Label = "settings.stations", Align = "right" },
        ];

    public static PluginView Build(
        StationCatalog catalog,
        string dataFolderPath,
        DateTimeOffset now,
        DateTimeOffset nextRefreshUtc,
        UserState state)
    {
        List<PluginComponent> children =
        [
            // The plugin's own name, not page chrome - left English the way a brand
            // name is, same as every other client that draws it. "settings.title"
            // exists, but only for the route table's nav/breadcrumb label ("Settings")
            // - see InternetRadioPlugin.Table - which is a different string from this
            // page's own heading.
            PluginViews.Text("settings-title", "Internet Radio", "title"),
            PluginViews.Row(
                "settings-status",
                SourceBadge(catalog),
                PluginViews.Text("settings-age", Age(catalog, now), "caption")
            ),
            PluginViews.Text("settings-next-refresh", NextRefresh(nextRefreshUtc, now), "caption"),
            // Labelled for what it honestly does. A cache younger than the 36-hour
            // TTL - always, given the daily job - means RefreshView re-renders the
            // same cache untouched, so this must not read as a button that forces a
            // fetch. "Refresh now" claimed exactly that and did nothing about it.
            IconButton.Build(
                "settings-refresh",
                "settings.reload",
                PluginActionIntent.RefreshView(),
                icon: "portableRadio"
            ),
            PluginViews.Text(
                "settings-refresh-caption",
                "Reloads this page from what is already cached. It does not force an early fetch - "
                    + "the catalogue itself only refreshes on the schedule above.",
                "caption"
            ),
        ];

        if (catalog.LastFetchFailed)
        {
            children.Add(
                PluginViews.Text(
                    "settings-refresh-failed",
                    "The catalogue could not be refreshed on the last attempt. "
                        + "Anything shown is from the cache. Check the server log for Internet Radio.",
                    "caption"
                )
            );
        }

        if (!catalog.IsEmpty)
        {
            children.Add(PluginViews.Text("settings-genres-heading", "settings.genres", "subtitle"));
            children.Add(
                PluginViews.Table(
                    "settings-genres",
                    Columns,
                    [
                        .. catalog.Genres.Select(genre =>
                            PluginViews.Row(
                                $"settings-genre-{genre.Section.Slug}",
                                new Dictionary<string, object?>
                                {
                                    ["genre"] = genre.Section.Label,
                                    ["stations"] = genre.Count.ToString(
                                        System.Globalization.CultureInfo.InvariantCulture),
                                }
                            )
                        ),
                    ]
                )
            );
        }

        // The one place an owner can see that favourites are being stored at all. The
        // singular is spelled out rather than pluralised with an "s": "1 favourite
        // stations" reads as a bug to whoever has exactly one.
        children.Add(PluginViews.Text(
            "settings-favourites-count",
            state.Favourites.Count == 1
                ? "1 favourite station."
                : $"{state.Favourites.Count} favourite stations.",
            "caption"));

        children.Add(PluginViews.Text("settings-own-heading", "settings.own", "subtitle"));
        children.Add(
            PluginViews.Text(
                "settings-own-body",
                $"Drop a file named {StationOverrides.FileName} into {dataFolderPath} to replace the "
                    + "fetched list entirely. It is a JSON array of stations, each needing at least a "
                    + "name and a streamUrl. Your file is used as written and is not filtered, so it is "
                    + "also the way to add a station radio-browser.info does not carry.",
                "caption"
            )
        );

        children.Add(PluginViews.Text("settings-editing-heading", "settings.editing", "subtitle"));
        children.Add(
            PluginViews.Text(
                "settings-editing-body",
                // Left English, like every other caption on this page: PluginViews.Text
                // renders "caption" as an NMHelper, whose helperText prop NMRender never
                // resolves through the plugin's bundle (see EmptyCatalog.Build for the
                // same rule stated once). Keying it would ship a raw key nobody ever sees
                // translated.
                "This page is read-only. It shows what the plugin has already decided - "
                    + "the catalogue, your favourites, and any stations.json override - "
                    + "rather than letting you change any of it here. There is nothing "
                    + "on this page to edit yet; use stations.json (above) for your own "
                    + "stations.",
                "caption"
            )
        );

        return PluginViews.Declarative(PluginViews.Container("settings-root", [.. children]));
    }

    // Every label here stays English: NMBadge's own `text` prop is bound to the DOM
    // exactly as this payload writes it - only a leaf whose component is NMText
    // resolves through the plugin's bundle. See NMRender.vue.
    private static PluginComponent SourceBadge(StationCatalog catalog)
    {
        (string Label, string Variant) badge = catalog.Source switch
        {
            CatalogSource.UserOverride => ("Your own station list", PluginBadgeVariant.Info),
            CatalogSource.Fetched => ("Fetched from radio-browser.info", PluginBadgeVariant.Success),
            CatalogSource.Cache when catalog.LastFetchFailed
                => ("Cached — refresh failed", PluginBadgeVariant.Warning),
            CatalogSource.Cache => ("Cached", PluginBadgeVariant.Neutral),
            _ => ("No stations", PluginBadgeVariant.Danger),
        };

        return PluginViews.Badge("settings-source", badge.Label, badge.Variant);
    }

    /// <summary>
    /// When the scheduled refresh job next runs. The cron schedule belongs to
    /// <see cref="InternetRadioPlugin"/> - this only formats a value it was handed,
    /// keeping Build a pure function of its arguments rather than reaching for the
    /// clock or a duplicated copy of the cron expression itself.
    /// </summary>
    private static string NextRefresh(DateTimeOffset nextRefreshUtc, DateTimeOffset now)
    {
        TimeSpan until = nextRefreshUtc - now;

        string relative = until switch
        {
            { TotalMinutes: < 2 } => "in under a minute",
            { TotalHours: < 1 } => $"in {(int)until.TotalMinutes} minutes",
            { TotalHours: < 2 } => "in 1 hour",
            _ => $"in {(int)Math.Ceiling(until.TotalHours)} hours",
        };

        // NOT nextRefreshUtc:HH:mm inside the interpolation: a custom date/time
        // format treats ':' as the CURRENT CULTURE's time separator, not a literal
        // colon - fi-FI renders "04.00" instead of "04:00". ToString with an
        // explicit InvariantCulture provider is what actually pins the separator.
        string time = nextRefreshUtc.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        return $"Next automatic refresh {relative}, at {time} UTC.";
    }

    private static string Age(StationCatalog catalog, DateTimeOffset now)
    {
        if (catalog.Source == CatalogSource.UserOverride)
        {
            return $"{catalog.Count} stations, read from your own {StationOverrides.FileName}.";
        }

        if (catalog.FetchedAt is not { } fetchedAt)
        {
            return "The station list has never been fetched.";
        }

        TimeSpan age = now - fetchedAt;

        string ago = age switch
        {
            { TotalMinutes: < 2 } => "just now",
            { TotalHours: < 1 } => $"{(int)age.TotalMinutes} minutes ago",
            { TotalHours: < 2 } => "1 hour ago",
            { TotalDays: < 1 } => $"{(int)age.TotalHours} hours ago",
            { TotalDays: < 2 } => "1 day ago",
            _ => $"{(int)age.TotalDays} days ago",
        };

        return $"{catalog.Count} stations, updated {ago}.";
    }
}
