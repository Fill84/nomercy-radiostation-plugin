// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// One station as a card, shared by the browse and genre grids so the two screens
// cannot drift into behaving differently for the same station.
public static class StationCards
{
    /// <summary>How many stations the browse page's "Popular" grid shows.</summary>
    public const int PopularCount = 18;

    /// <summary>
    /// A card whose action is playMedia. Not navigate: the client turns this straight
    /// into playTrack(), so one click is listening — which is the entire job, and the
    /// one path that works while both inbound plugin transports are broken.
    /// </summary>
    public static PluginComponent Play(RadioStation station) =>
        PluginViews.Card(
            $"station-card-{station.Id}",
            station.Name,
            Subtitle(station),
            station.LogoUrl,
            PluginActionIntent.PlayMedia(
                station.StreamUrl,
                station.Name,
                // The player shows this where a track's artist would go; the genre is
                // the most useful thing a live stream has to put there.
                station.Genre,
                station.LogoUrl
            )
        );

    /// <summary>Genre and country, whichever of them is known. Null when neither is.</summary>
    public static string? Subtitle(RadioStation station)
    {
        string[] parts =
            [.. new[] { station.Genre, station.Country }.Where(part => !string.IsNullOrWhiteSpace(part))!];

        return parts.Length > 0 ? string.Join(" · ", parts) : null;
    }
}
