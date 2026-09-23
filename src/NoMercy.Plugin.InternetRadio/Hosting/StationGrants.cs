// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using Microsoft.Extensions.Logging;
using NoMercy.PluginSdk.Abstractions;

namespace NoMercy.Plugin.InternetRadio.Hosting;

/// <summary>
/// What this plugin has to ask the owner for, asked before anything needs it.
///
/// The manifest names radio-browser and nothing else, because that is the only host this
/// plugin knows at package time. Everything else is a station: thousands of servers whose
/// addresses arrive with the catalogue. The SDK's answer to that is a runtime request the
/// owner sees in the dashboard, rather than a wildcard taken at install time.
///
/// Asked from the refresh job, not from a page. A request records and returns; it never
/// waits for a human, and asking twice for the same thing does not queue a second prompt.
/// Asking early is also what keeps an owner away from the host's own refusal text, which
/// tells an author to add the host to plugin.json - a line that does nothing for a
/// capability grant.
/// </summary>
public sealed class StationGrants(IPluginGrants grants, ILogger logger)
{
    private const string StreamingReason =
        "Internet Radio plays stations from thousands of different servers. "
        + "Allow it to stream them through this server so they can play in the app.";

    private const string StationsReason =
        "Internet Radio fetches each station's logo and the track titles it announces, "
        + "from the station itself. Their addresses come from the station directory and "
        + "cannot be listed in advance.";

    private int _asked;

    public async Task EnsureAsync(CancellationToken ct = default)
    {
        // Once per process. The host already ignores a repeat, so this only spares it the
        // round trip on every refresh.
        if (Interlocked.Exchange(ref _asked, 1) == 1)
        {
            return;
        }

        try
        {
            await AskUnlessHeldAsync(
                PluginGrantKind.ForCapability(PluginCapabilityNames.MediaProxy),
                PluginGrant.Everything,
                StreamingReason,
                ct);

            // Asked as PluginGrant.Everything, not per host: ConfigPluginGrantStore.Holds
            // compares a grant exactly or against "*" and does not glob, so a per-host
            // grant would have to be given station by station, forever, as the catalogue
            // grew. A wildcard asked once is the only version of this an owner would ever
            // actually approve.
            await AskUnlessHeldAsync(
                PluginGrantKind.NetworkHost, PluginGrant.Everything, StationsReason, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Asking is a courtesy on a host that mediates it. Failing to ask must not cost
            // the refresh its catalogue.
            logger.LogWarning(exception, "Internet Radio could not ask the owner for what it needs.");
        }
    }

    private async Task AskUnlessHeldAsync(string kind, string value, string reason, CancellationToken ct)
    {
        if (await grants.HasAsync(kind, value, ct))
        {
            return;
        }

        await grants.RequestAsync(kind, value, reason, ct);
    }
}
