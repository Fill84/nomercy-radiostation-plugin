// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// One ICY <c>StreamTitle</c>, split into who is playing and what.
///
/// Stations announce a single string, and the near-universal convention is
/// "Artist - Track". Splitting it here rather than in each client means every
/// surface - web, phone, TV, cast - draws the same two fields, and a client that
/// only wants the whole line still has <see cref="Raw"/>.
/// </summary>
public sealed record StreamTitle(string Raw, string? Artist, string Track)
{
    private static readonly string[] Separators = [" - ", " – ", " — "];

    /// <summary>
    /// Splits on the FIRST separator, not the last: a band with a dash in its name
    /// is rarer than a track with one, and "A - B - C" read the other way turns most
    /// of the artist into part of the title.
    /// </summary>
    public static StreamTitle Parse(string raw)
    {
        string trimmed = raw.Trim();

        foreach (string separator in Separators)
        {
            int at = trimmed.IndexOf(separator, StringComparison.Ordinal);
            if (at <= 0)
            {
                continue;
            }

            string artist = trimmed[..at].Trim();
            string track = trimmed[(at + separator.Length)..].Trim();

            // A line that is only a separator, or that has nothing on one side of it,
            // is not two fields. Announcing an empty artist is worse than announcing
            // none, so the whole line stays the track.
            if (artist.Length > 0 && track.Length > 0)
            {
                return new StreamTitle(trimmed, artist, track);
            }
        }

        // Nothing kept out of the track: parentheticals carry remix and live
        // information that belongs to the title, and a station's own chatter cannot
        // be told from them without guessing at the station's habits.
        return new StreamTitle(trimmed, null, trimmed);
    }
}
