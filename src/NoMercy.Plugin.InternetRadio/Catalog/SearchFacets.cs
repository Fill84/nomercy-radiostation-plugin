// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// The choices the search form offers, as radio-browser publishes them.
///
/// Fetched once and kept: tags, countries and languages are a controlled
/// vocabulary that changes over days, and asking for three lists on every render
/// of a page a listener opens repeatedly is rude to a volunteer-run service.
///
/// A failed fetch leaves the lists empty rather than throwing, and the form falls
/// back to a plain text box for that field - a filter you can still type is worth
/// more than a page that will not draw.
/// </summary>
public sealed class SearchFacets
{
    /// <summary>How many entries each list offers.</summary>
    public const int Limit = 120;

    private static readonly TimeSpan Ttl = TimeSpan.FromHours(12);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    public IReadOnlyList<string> Genres { get; private set; } = [];
    public IReadOnlyList<string> Countries { get; private set; } = [];
    public IReadOnlyList<string> Languages { get; private set; } = [];

    public bool IsStale(DateTimeOffset now) => now - _fetchedAt > Ttl;

    public async Task RefreshAsync(RadioBrowserClient client, DateTimeOffset now, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Re-checked inside the gate: three renders arriving together would
            // otherwise each fetch the same three lists.
            if (!IsStale(now))
            {
                return;
            }

            Genres = await NamesAsync(client, "tags", ct);
            Countries = await NamesAsync(client, "countries", ct);
            Languages = await NamesAsync(client, "languages", ct);
            _fetchedAt = now;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<IReadOnlyList<string>> NamesAsync(
        RadioBrowserClient client, string facet, CancellationToken ct)
    {
        IReadOnlyList<RadioBrowserFacet> entries = await client.GetFacetAsync(facet, Limit, ct);

        return [.. entries.Select(entry => entry.Name!.Trim()).Where(name => name.Length > 0)];
    }
}
