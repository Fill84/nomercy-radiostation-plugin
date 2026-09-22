// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NoMercy.PluginSdk.Abstractions;
using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.Views;

namespace NoMercy.Plugin.InternetRadio.Playback;

/// <summary>
/// Streams through the host's media proxy.
///
/// The host mints a short-lived url bound to the listener, and fetches the station itself,
/// so the browser only ever talks to this server. That replaces this plugin's own relay,
/// which carried the viewer's token in the url - refused outright since ABI 11.
///
/// The mint is a delegate rather than the context so a test can stand in for it: a
/// PluginMediaUrl cannot be constructed outside the host, on purpose.
/// </summary>
public sealed class HostStationStreams(
    Func<PluginProxyRequest, CancellationToken, Task<Uri>> mint,
    IPluginGrants grants,
    ILogger logger
) : IStationStreams
{
    private static readonly TimeSpan LogEvery = TimeSpan.FromHours(1);

    private const string GrantReason =
        "Internet Radio plays stations from thousands of different servers. "
        + "Allow it to stream them through this server so they can play in the app.";

    /// <summary>
    /// A plugin-local code, not one <see cref="PluginRefusalCodes"/> defines: the host
    /// never sees this station, so there is no <see cref="PluginRefusal"/> to carry a code
    /// from. Covers a malformed or scheme-less StreamUrl - stations.json is deliberately
    /// not gated, see StationOverrides - and anything else the mint throws that is not
    /// itself a refusal. StationView maps it to the same generic "cannot play right now"
    /// notice as any other non-approval code.
    /// </summary>
    public const string StreamUnusable = "INTERNET_RADIO_STREAM_UNUSABLE";

    private int _askedForTheGrant;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastLogged = new();

    /// <summary>For the plugin: minting through the context it was handed.</summary>
    public static HostStationStreams For(IPluginContext context) =>
        new(
            async (request, ct) => (await context.Media.Proxy.MintAsync(request, ct)).Url,
            context.Grants,
            context.Logger);

    public async Task<StationStream> ForAsync(RadioStation station, CancellationToken ct)
    {
        // Checked before the mint is ever asked for anything: a station.json entry is
        // never gated, so this is the first thing here that has seen the url at all.
        if (!Uri.TryCreate(station.StreamUrl, UriKind.Absolute, out Uri? upstream))
        {
            LogUnusable(station.Id);
            return StationStream.Refused(StreamUnusable);
        }

        try
        {
            Uri url = await mint(
                new PluginProxyRequest
                {
                    Links = [new PluginProxyLink { Url = upstream }],
                    Title = station.Name,
                    // Null is no timeout. A live stream never finishes, and a timeout would
                    // cut every listener off at the same minute of every song.
                    Timeout = null,
                },
                ct);

            return StationStream.Playable(url.OriginalString);
        }
        catch (PluginRefusedException refused)
        {
            string code = refused.Refusal.Code;

            Log(refused.Refusal);

            if (code == PluginRefusalCodes.CapabilityScopeRefused)
            {
                await AskForTheGrantOnceAsync(ct);
            }

            return StationStream.Refused(code);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Anything else the mint throws - an ArgumentException from a request the
            // host itself rejects, say - must still leave the page drawable rather than
            // take the whole view down with it.
            LogUnusable(station.Id, exception);
            return StationStream.Refused(StreamUnusable);
        }
    }

    // Once per process. The host records a request and never queues a second prompt for
    // the same thing anyway; this only saves asking it on every page.
    private async Task AskForTheGrantOnceAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _askedForTheGrant, 1) == 1)
        {
            return;
        }

        try
        {
            await grants.RequestAsync(
                PluginGrantKind.ForCapability(PluginCapabilityNames.MediaProxy),
                PluginGrant.Everything,
                GrantReason,
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Asking is a courtesy. Failing to ask must not cost the viewer the page.
            logger.LogWarning(exception, "Internet Radio could not ask the owner to allow streaming.");
        }
    }

    // Once per code per hour: a refusal is the same answer for every station on every page,
    // and a line per card would bury everything else in the log.
    private void Log(PluginRefusal refusal)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (_lastLogged.TryGetValue(refusal.Code, out DateTimeOffset last) && now - last < LogEvery)
        {
            return;
        }

        _lastLogged[refusal.Code] = now;
        logger.LogWarning(
            "Internet Radio cannot play stations yet: {Code}. {What} {Fix}", refusal.Code, refusal.What, refusal.Fix);
    }

    // Same throttle, keyed on StreamUnusable rather than a host refusal's own code. Names
    // the station id rather than its url, so the owner can find the entry in
    // stations.json without a raw address sitting in the log.
    private void LogUnusable(string stationId, Exception? exception = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (_lastLogged.TryGetValue(StreamUnusable, out DateTimeOffset last) && now - last < LogEvery)
        {
            return;
        }

        _lastLogged[StreamUnusable] = now;
        logger.LogWarning(
            exception, "Internet Radio cannot play {StationId}: its stream url could not be used.", stationId);
    }
}
