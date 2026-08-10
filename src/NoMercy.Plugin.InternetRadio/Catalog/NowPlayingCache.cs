// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Collections.Concurrent;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// The last thing each station was heard to announce, kept briefly.
///
/// Reading a title means opening a second connection to the station and pulling audio
/// until its next metadata block - for SomaFM that is 45 KB. Every listening device polls
/// on its own timer, so without this a household with three devices on one station opens
/// three connections a minute to somebody else's server and downloads a megabyte an hour
/// to learn a line of text. Stations block clients for less, and being blocked costs the
/// listener the station, not us.
///
/// A miss is cached too. A station that announces nothing is the ordinary case, and
/// re-reading it on every poll is exactly the traffic this exists to prevent.
///
/// The clock is a parameter because a cache that cannot be tested for expiry is a cache
/// nobody can change later with any confidence.
/// </summary>
public sealed class NowPlayingCache(TimeSpan lifetime, Func<DateTimeOffset> clock)
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    private readonly record struct Entry(DateTimeOffset Stored, (string? Artist, string Track)? Value);

    /// <summary>How long an answer stays good. Shorter than the poll interval it serves.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(25);

    /// <summary>
    /// The stored answer, when there is one and it is still fresh.
    ///
    /// Two levels of "nothing" here, and they are different: no entry at all means ask the
    /// station, while an entry holding null means the station was asked recently and said
    /// nothing. Only the first is worth a connection.
    /// </summary>
    public bool TryGet(string stationId, out (string? Artist, string Track)? value)
    {
        value = null;

        if (!_entries.TryGetValue(stationId, out Entry entry))
        {
            return false;
        }

        if (clock() - entry.Stored >= lifetime)
        {
            _entries.TryRemove(stationId, out _);

            return false;
        }

        value = entry.Value;

        return true;
    }

    /// <summary>Remembers what a station said, including that it said nothing.</summary>
    public void Set(string stationId, (string? Artist, string Track)? value) =>
        _entries[stationId] = new Entry(clock(), value);

    /// <summary>How many stations are currently remembered. For tests and diagnostics.</summary>
    public int Count => _entries.Count;
}
