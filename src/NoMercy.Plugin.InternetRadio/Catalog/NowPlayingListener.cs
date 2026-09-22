// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NoMercy.Plugin.InternetRadio.Catalog;

/// <summary>
/// Hears what a station is playing, for as long as someone is listening.
///
/// The host's media proxy streams the audio, and it never asks a station for metadata. So
/// the title has to come from a connection of this plugin's own: it asks with
/// Icy-MetaData: 1, keeps only the announcements, and throws the audio away.
///
/// One connection per station, shared by everyone listening to it. It lives on a lease
/// that every now-playing poll renews, so it closes shortly after the last listener's
/// player stops asking. A cap keeps a busy server from opening one per station in the
/// directory, and a station that announces nothing is left alone for an hour rather than
/// reconnected on every poll.
///
/// A Shoutcast v1 station answers "ICY 200 OK", which .NET's HttpClient cannot read. That
/// was true of the old relay too; such a station simply has no title.
/// </summary>
public sealed class NowPlayingListener(
    HttpClient http,
    NowPlaying titles,
    ILogger logger,
    NowPlayingListener.Limits? limits = null
) : IDisposable
{
    public sealed record Limits
    {
        public int MaxConnections { get; init; } = 8;
        public TimeSpan Lease { get; init; } = TimeSpan.FromSeconds(45);
        public TimeSpan QuietFor { get; init; } = TimeSpan.FromHours(1);
    }

    private readonly Limits _limits = limits ?? new Limits();
    private readonly ConcurrentDictionary<string, Connection> _open = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _quiet = new();
    private readonly Lock _opening = new();

    // Read outside the lock too, as a fast path - see the authoritative re-check
    // inside the lock in Touch, and Dispose taking the same lock around its own
    // read-and-write, for why an unsynchronized read here can never let a
    // connection start after Dispose has already run.
    private volatile bool _disposed;

    public int OpenCount => _open.Count;

    /// <summary>Someone is listening to this station: keep, or start, hearing it.</summary>
    public void Touch(RadioStation station)
    {
        if (_disposed)
        {
            return;
        }

        if (_open.TryGetValue(station.Id, out Connection? open))
        {
            open.Renew();
            return;
        }

        if (_quiet.TryGetValue(station.Id, out DateTimeOffset until) && until > DateTimeOffset.UtcNow)
        {
            return;
        }

        lock (_opening)
        {
            // The authoritative check: Dispose sets this and enumerates _open under
            // the same lock, so a Touch that reaches here after Dispose has run
            // cannot publish a connection Dispose already walked past and will
            // never cancel.
            if (_disposed)
            {
                return;
            }

            if (_open.TryGetValue(station.Id, out open))
            {
                open.Renew();
                return;
            }

            // Past the cap the poll simply gets no title - the same answer as a station
            // that announces nothing, which a player already shows gracefully.
            if (_open.Count >= _limits.MaxConnections)
            {
                return;
            }

            Connection connection = new(_limits.Lease);
            _open[station.Id] = connection;
            _ = HearAsync(station, connection);
        }
    }

    private async Task HearAsync(RadioStation station, Connection connection)
    {
        // The title is forgotten when this closes, so a station nobody hears any more
        // stops reporting a song that ended long ago.
        using IDisposable listening = titles.Listen(station.Id);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, station.StreamUrl);
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");

            using HttpResponseMessage response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, connection.Token);

            if (!response.IsSuccessStatusCode && IsTransientFailure(response))
            {
                // The station's own trouble right now, not a verdict that it never
                // announces titles - retried on the very next poll rather than parked
                // in quiet-for-an-hour alongside a station that plainly sends none.
                return;
            }

            if (!response.IsSuccessStatusCode || MetaInterval(response) is not { } interval)
            {
                _quiet[station.Id] = DateTimeOffset.UtcNow + _limits.QuietFor;
                return;
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(connection.Token);
            await using IcyMetadataStream audio = new(body, interval, title => titles.Set(station.Id, title));

            byte[] discarded = new byte[8192];
            while (await audio.ReadAsync(discarded, connection.Token) > 0)
            {
            }
        }
        catch (OperationCanceledException)
        {
            // The lease ran out, or the plugin is unloading. Both are how this ends.
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Internet Radio stopped hearing {Station}.", station.Id);
        }
        finally
        {
            _open.TryRemove(station.Id, out _);
            connection.Dispose();
        }
    }

    /// <summary>The station's own icy-metaint, when it agreed to send metadata.</summary>
    private static int? MetaInterval(HttpResponseMessage response) =>
        response.Headers.TryGetValues("icy-metaint", out IEnumerable<string>? values)
        && int.TryParse(values.FirstOrDefault(), out int interval)
        && interval > 0
            ? interval
            : null;

    /// <summary>A 5xx: the station's own trouble, worth trying again on the very next poll.</summary>
    private static bool IsTransientFailure(HttpResponseMessage response) =>
        (int)response.StatusCode is >= 500 and < 600;

    public void Dispose()
    {
        // Same lock Touch opens a connection under, so this either runs before a
        // Touch that is about to publish one (and then cancels it, since it is
        // already in _open by the time this enumerates) or after it (and _disposed
        // is what stops that Touch from publishing at all).
        lock (_opening)
        {
            _disposed = true;

            foreach (Connection connection in _open.Values)
            {
                connection.Cancel();
            }
        }
    }

    private sealed class Connection(TimeSpan lease) : IDisposable
    {
        private readonly CancellationTokenSource _cts = new(lease);

        public CancellationToken Token => _cts.Token;

        public void Renew()
        {
            try
            {
                _cts.CancelAfter(lease);
            }
            catch (ObjectDisposedException)
            {
                // Closed between the lookup and the renewal; the next poll opens a new one.
            }
        }

        public void Cancel()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose() => _cts.Dispose();
    }
}
