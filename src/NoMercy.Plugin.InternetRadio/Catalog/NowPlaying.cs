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

    private readonly ConcurrentDictionary<string, int> _listeners = new();

    /// <summary>
    /// Marks one listener as being on this station, and returns the handle that
    /// releases them. The title is forgotten when the last one leaves, so a station
    /// nobody is listening to stops reporting a song that stopped playing long ago -
    /// but a second listener leaving does not blank the title for the first, which
    /// is what clearing on every relay end did.
    /// </summary>
    public IDisposable Listen(string stationId)
    {
        _listeners.AddOrUpdate(stationId, 1, (_, count) => count + 1);

        return new Listener(this, stationId);
    }

    private void Release(string stationId)
    {
        int remaining = _listeners.AddOrUpdate(stationId, 0, (_, count) => count - 1);
        if (remaining > 0)
        {
            return;
        }

        _listeners.TryRemove(stationId, out _);
        _titles.TryRemove(stationId, out _);
    }

    private sealed class Listener(NowPlaying owner, string stationId) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            owner.Release(stationId);
        }
    }
}
