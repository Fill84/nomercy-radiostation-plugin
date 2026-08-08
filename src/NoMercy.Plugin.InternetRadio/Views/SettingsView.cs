// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// Status, not settings.
//
// This version has nothing to edit because when it was built there was nowhere to
// save to. Both inbound transports were dead: the server's plugin REST routes were
// mounted unversioned while the client posts to /api/v1, and the hub is not the
// alternative it looks like - nothing registers a plugin's hub handler, so
// IPluginHubHandler never receives anything.
//
// The REST half was fixed upstream on 2026-07-30 (media-server issue #26, commit
// 37e5e7c), after this version was built - so a REST controller is now the way an
// editable page would carry its saves. This plugin does not ship one yet, and it
// declares "rest": false accordingly. The hub half is still open.
//
// Rendering a form with no controller behind it would be the false promise this
// plugin exists to stop making, so this page answers the questions someone actually
// arrives with instead: where did these stations come from, how old are they, and
// how do I add my own.
public static class SettingsView
{
    private static IReadOnlyList<PluginTableColumn> Columns { get; } =
        [
            new() { Key = "genre", Label = "Genre" },
            new() { Key = "stations", Label = "Stations", Align = "right" },
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
            Ui.Text("settings-title", "Internet Radio", "title"),
            Ui.Row(
                "settings-status",
                SourceBadge(catalog),
                Ui.Text("settings-age", Age(catalog, now), "caption")
            ),
            Ui.Text("settings-next-refresh", NextRefresh(nextRefreshUtc, now), "caption"),
            // Labelled for what it honestly does. A cache younger than the 36-hour
            // TTL - always, given the daily job - means RefreshView re-renders the
            // same cache untouched, so this must not read as a button that forces a
            // fetch. "Refresh now" claimed exactly that and did nothing about it.
            Ui.Button(
                "settings-refresh",
                "Reload",
                PluginActionIntent.RefreshView(),
                icon: "portableRadio"
            ),
            Ui.Text(
                "settings-refresh-caption",
                "Reloads this page from what is already cached. It does not force an early fetch - "
                    + "the catalogue itself only refreshes on the schedule above.",
                "caption"
            ),
        ];

        if (catalog.LastFetchFailed)
        {
            children.Add(
                Ui.Text(
                    "settings-refresh-failed",
                    "The catalogue could not be refreshed on the last attempt. "
                        + "Anything shown is from the cache. Check the server log for Internet Radio.",
                    "caption"
                )
            );
        }

        if (!catalog.IsEmpty)
        {
            children.Add(Ui.Text("settings-genres-heading", "Genres", "subtitle"));
            children.Add(
                Ui.Table(
                    "settings-genres",
                    Columns,
                    [
                        .. catalog.Genres.Select(genre =>
                            Ui.TableRow(
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
        children.Add(Ui.Text(
            "settings-favourites-count",
            state.Favourites.Count == 1
                ? "1 favourite station."
                : $"{state.Favourites.Count} favourite stations.",
            "caption"));

        children.Add(Ui.Text("settings-own-heading", "Your own stations", "subtitle"));
        children.Add(
            Ui.Text(
                "settings-own-body",
                $"Drop a file named {StationOverrides.FileName} into {dataFolderPath} to replace the "
                    + "fetched list entirely. It is a JSON array of stations, each needing at least a "
                    + "name and a streamUrl. Your file is used as written and is not filtered, so it is "
                    + "also the way to add a station radio-browser.info does not carry.",
                "caption"
            )
        );

        children.Add(Ui.Text("settings-editing-heading", "Why there is nothing to edit", "subtitle"));
        children.Add(
            Ui.Text(
                "settings-editing-body",
                "This page is read-only. When this version was built a plugin could not receive "
                    + "anything from its own UI on this server, so there was nowhere for a form to "
                    + "save to. The REST route that would carry it was fixed upstream on 2026-07-30 "
                    + "(media-server issue #26); this version predates that and ships no controller, "
                    + "so editable settings are a later release. Plugin hub handlers are still never "
                    + "registered, so that route remains unavailable.",
                "caption"
            )
        );

        return PluginViews.Declarative(Ui.Container("settings-root", [.. children]));
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

        return Ui.Badge("settings-source", badge.Label, badge.Variant);
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
