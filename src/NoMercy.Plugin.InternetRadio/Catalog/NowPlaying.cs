// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Collections.Concurrent;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// The track each station is currently playing, as the station last announced it.
///
/// Held per station rather than per listener: a live stream is the same broadcast
/// for everyone, so two listeners on one station are hearing the same song and the
/// second one should see it immediately rather than waiting for the next
/// announcement. Entries live only as long as the process - a title that is no
/// longer being broadcast is worth nothing, so there is nothing here to persist.
/// </summary>
public sealed class NowPlaying
{
    private readonly ConcurrentDictionary<string, string> _titles = new();

    public void Set(string stationId, string title)
    {
        _titles[stationId] = title;
    }

    public string? Get(string stationId) =>
        _titles.TryGetValue(stationId, out string? title) ? title : null;

    /// <summary>
    /// Forgets a station's title. Called when its relay ends, so a station nobody is
    /// listening to stops reporting a song that stopped playing long ago.
    /// </summary>
    public void Clear(string stationId)
    {
        _titles.TryRemove(stationId, out _);
    }
}
