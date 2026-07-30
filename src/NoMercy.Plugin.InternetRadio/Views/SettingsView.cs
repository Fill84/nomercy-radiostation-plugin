// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// Status, not settings.
//
// There is nothing to edit because there is nowhere to save to: the server's plugin
// REST routes are unversioned while the client posts to /api/v1 (issue #26), and the
// hub is not the alternative it looks like - nothing ever registers a plugin's hub
// handler, so IPluginHubHandler never receives anything. Rendering a form that
// silently 404s would be the false promise this plugin is meant to stop making.
//
// So this page answers the questions someone actually arrives with: where did these
// stations come from, how old are they, and how do I add my own.
public static class SettingsView
{
    private static IReadOnlyList<PluginTableColumn> Columns { get; } =
        [
            new() { Key = "genre", Label = "Genre" },
            new() { Key = "stations", Label = "Stations", Align = "right" },
        ];

    public static PluginView Build(
        StationCatalog catalog, string dataFolderPath, DateTimeOffset now, DateTimeOffset nextRefreshUtc)
    {
        List<PluginComponent> children =
        [
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
            PluginViews.Button(
                "settings-refresh",
                "Reload",
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
            children.Add(PluginViews.Text("settings-genres-heading", "Genres", "subtitle"));
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

        children.Add(PluginViews.Text("settings-own-heading", "Your own stations", "subtitle"));
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

        children.Add(PluginViews.Text("settings-editing-heading", "Why there is nothing to edit", "subtitle"));
        children.Add(
            PluginViews.Text(
                "settings-editing-body",
                "This page is read-only. A plugin cannot yet receive anything from its own UI on this "
                    + "server: plugin REST routes are served unversioned while the dashboard posts to "
                    + "/api/v1 (media-server issue #26), and the hub is not an alternative because "
                    + "plugin hub handlers are never registered. Editable settings arrive when either "
                    + "is fixed.",
                "caption"
            )
        );

        return PluginViews.Declarative(PluginViews.Container("settings-root", [.. children]));
    }

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
