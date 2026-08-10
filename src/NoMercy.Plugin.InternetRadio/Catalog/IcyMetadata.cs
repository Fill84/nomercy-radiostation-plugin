// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// What a station says is playing right now.
///
/// A live stream carries no track list. Instead the server interleaves a short text block
/// into the audio every <c>icy-metaint</c> bytes, but only for a listener who asked for it
/// with an <c>Icy-MetaData: 1</c> header. The relay deliberately does NOT ask - the browser
/// would receive those blocks in the middle of the audio and decode them as noise - so the
/// title is fetched here, on a second connection, and handed to the client as ordinary
/// JSON.
///
/// Everything except the read itself is a pure function, because the parsing is where the
/// surprises live: stations pad, quote, mis-encode and lie, and none of that is worth
/// discovering against a live socket.
/// </summary>
public static class IcyMetadata
{
    /// <summary>The header a listener sends to be told what is playing.</summary>
    public const string RequestHeader = "Icy-MetaData";

    /// <summary>The header a station answers with: how many audio bytes between blocks.</summary>
    public const string IntervalHeader = "icy-metaint";

    /// <summary>
    /// How many metadata blocks to wait through before giving up.
    ///
    /// The first block after connecting is very often empty - the station only writes a
    /// title when the track changes, and a listener who joins mid-song gets whatever was
    /// last written, which for some servers is nothing at all. Two more give the common
    /// case a chance without holding the request open.
    /// </summary>
    public const int MaxBlocks = 3;

    /// <summary>
    /// The largest interval worth honouring, so a station that answers with something
    /// absurd cannot make this read a gigabyte before it gives up.
    /// </summary>
    public const int MaxInterval = 64 * 1024;

    /// <summary>
    /// The title a station is announcing, or null when it announces nothing.
    ///
    /// Null rather than an exception for every ordinary failure: a station that does not
    /// support metadata, one that is between announcements, and one that simply did not
    /// answer are all "nothing to show" to the caller, and none of them is a fault worth
    /// a stack trace.
    /// </summary>
    public static async Task<string?> ReadStreamTitleAsync(
        HttpClient http, string streamUrl, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, streamUrl);
        request.Headers.TryAddWithoutValidation(RequestHeader, "1");

        using HttpResponseMessage response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        if (!TryReadInterval(response, out int interval))
        {
            return null;
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);

        for (int block = 0; block < MaxBlocks; block++)
        {
            if (!await SkipAsync(stream, interval, ct))
            {
                return null;
            }

            int length = stream.ReadByte();
            if (length < 0)
            {
                return null;
            }

            // Length is in sixteen-byte units, and zero means "nothing new since last
            // time" - which is not the end of the stream, just a quiet moment.
            if (length == 0)
            {
                continue;
            }

            byte[] raw = new byte[length * 16];
            if (!await FillAsync(stream, raw, ct))
            {
                return null;
            }

            if (ExtractStreamTitle(Encoding.UTF8.GetString(raw)) is { } title)
            {
                return title;
            }
        }

        return null;
    }

    /// <summary>The station's declared interval, when it declared a usable one.</summary>
    public static bool TryReadInterval(HttpResponseMessage response, out int interval)
    {
        interval = 0;

        if (!response.Headers.TryGetValues(IntervalHeader, out IEnumerable<string>? values)
            && !response.Content.Headers.TryGetValues(IntervalHeader, out values))
        {
            return false;
        }

        return int.TryParse(values.FirstOrDefault(), out interval)
            && interval > 0
            && interval <= MaxInterval;
    }

    /// <summary>
    /// The title out of a metadata block, or null when the block names none.
    ///
    /// A block looks like <c>StreamTitle='Artist - Track';StreamUrl='https://…';</c> and is
    /// padded with NUL bytes to a multiple of sixteen. Both parts are optional and their
    /// order is not promised, so this looks for the one field it needs rather than
    /// splitting on the separators.
    /// </summary>
    public static string? ExtractStreamTitle(string block)
    {
        const string key = "StreamTitle='";

        int start = block.IndexOf(key, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += key.Length;

        // Terminated by "';" rather than by the next quote: a title may legitimately
        // contain an apostrophe, and every station that writes one leaves it unescaped.
        int end = block.IndexOf("';", start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = block.IndexOf('\0', start);
        }

        if (end < 0)
        {
            end = block.Length;
        }

        string title = block[start..end].Trim('\0').Trim();

        return title.Length > 0 ? title : null;
    }

    /// <summary>
    /// A title split into who and what, the way a station writes it.
    ///
    /// The convention is "Artist - Track" and it is only a convention: plenty of stations
    /// announce a single line, and a few announce the track first. Splitting on the FIRST
    /// separator keeps a track whose own name contains a dash intact, which is the more
    /// common shape by far.
    /// </summary>
    public static (string? Artist, string Track) Split(string title)
    {
        int separator = title.IndexOf(" - ", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return (null, title);
        }

        string artist = title[..separator].Trim();
        string track = title[(separator + 3)..].Trim();

        // A separator with nothing after it is a station padding its line, not an artist
        // with no song. The whole line is then the better answer.
        return track.Length > 0 && artist.Length > 0 ? (artist, track) : (null, title);
    }

    private static async Task<bool> SkipAsync(Stream stream, int count, CancellationToken ct)
    {
        byte[] buffer = new byte[8192];
        int remaining = count;

        while (remaining > 0)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), ct);

            if (read <= 0)
            {
                return false;
            }

            remaining -= read;
        }

        return true;
    }

    private static async Task<bool> FillAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int filled = 0;

        while (filled < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(filled), ct);
            if (read <= 0)
            {
                return false;
            }

            filled += read;
        }

        return true;
    }
}
