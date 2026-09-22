// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NoMercy.PluginSdk.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

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
        try
        {
            Uri url = await mint(
                new PluginProxyRequest
                {
                    Links = [new PluginProxyLink { Url = new Uri(station.StreamUrl) }],
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
}
