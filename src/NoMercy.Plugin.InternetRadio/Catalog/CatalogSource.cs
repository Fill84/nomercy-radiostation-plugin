// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

// Where the stations on screen came from. Shown on the settings page, because "no
// stations" and "stations from a three-day-old cache" are different problems and the
// owner cannot tell them apart from the browse page.
public enum CatalogSource
{
    /// <summary>Nothing to show: no override, no cache, and no successful fetch.</summary>
    Unavailable,

    /// <summary>Fetched from radio-browser during this run.</summary>
    Fetched,

    /// <summary>Read from the on-disk cache written by an earlier fetch.</summary>
    Cache,

    /// <summary>The user's own stations.json, which replaces everything else.</summary>
    UserOverride,
}
