// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

/// <param name="Section">The genre section.</param>
/// <param name="Count">How many stations are in it. Shown on the settings page.</param>
public sealed record GenreSummary(GenreSection Section, int Count);

// What every view is handed. Immutable, and built once per view request from
// whatever CatalogProvider resolved, so a view cannot accidentally do I/O and cannot
// see a catalogue change underneath it mid-render.
public sealed class StationCatalog
{
    private readonly Dictionary<string, RadioStation> _byId;
    private readonly Dictionary<string, List<RadioStation>> _byGenreSlug;

    private StationCatalog(
        IReadOnlyList<RadioStation> stations,
        CatalogSource source,
        DateTimeOffset? fetchedAt,
        bool lastFetchFailed
    )
    {
        Stations = stations;
        Source = source;
        FetchedAt = fetchedAt;
        LastFetchFailed = lastFetchFailed;

        _byId = new(StringComparer.OrdinalIgnoreCase);
        _byGenreSlug = new(StringComparer.OrdinalIgnoreCase);

        foreach (RadioStation station in stations)
        {
            // First wins. Deduplicate has already run for fetched stations, but a
            // user's stations.json is not gated, so this is where a collision in
            // their file is resolved rather than throwing during a page render.
            _byId.TryAdd(station.Id, station);

            string slug = StationGates.Slugify(station.Genre ?? GenreMap.Other);
            if (!_byGenreSlug.TryGetValue(slug, out List<RadioStation>? bucket))
            {
                bucket = [];
                _byGenreSlug[slug] = bucket;
            }

            bucket.Add(station);
        }
    }

    public IReadOnlyList<RadioStation> Stations { get; }
    public CatalogSource Source { get; }
    public DateTimeOffset? FetchedAt { get; }

    /// <summary>True when the most recent fetch attempt failed, whatever is on screen.</summary>
    public bool LastFetchFailed { get; }

    public int Count => Stations.Count;
    public bool IsEmpty => Stations.Count == 0;

    public static StationCatalog Create(
        IEnumerable<RadioStation> stations,
        CatalogSource source,
        DateTimeOffset? fetchedAt
    ) => new([.. stations], source, fetchedAt, lastFetchFailed: false);

    public static StationCatalog Empty(bool lastFetchFailed = false) =>
        new([], CatalogSource.Unavailable, fetchedAt: null, lastFetchFailed);

    /// <summary>
    /// The same catalogue, marked as having survived a failed refresh. Lets the
    /// settings page distinguish "cached because it is fresh" from "cached because
    /// the network is down".
    /// </summary>
    public StationCatalog WithFailedFetch() =>
        new([.. Stations], Source, FetchedAt, lastFetchFailed: true);

    public RadioStation? ById(string id) =>
        _byId.TryGetValue(id, out RadioStation? station) ? station : null;

    public IReadOnlyList<RadioStation> ByGenreSlug(string slug) =>
        _byGenreSlug.TryGetValue(slug, out List<RadioStation>? bucket) ? bucket : [];

    /// <summary>
    /// Only sections that have stations, in <see cref="GenreMap"/> order, with
    /// "Other" last. A chip leading to an empty page is worse than no chip.
    /// </summary>
    public IReadOnlyList<GenreSummary> Genres =>
        [
            .. GenreMap.Sections
                .Select(section => new GenreSummary(section, ByGenreSlug(section.Slug).Count))
                .Where(summary => summary.Count > 0),
            .. OtherSummary(),
        ];

    private IEnumerable<GenreSummary> OtherSummary()
    {
        string slug = StationGates.Slugify(GenreMap.Other);
        int count = ByGenreSlug(slug).Count;

        if (count > 0)
        {
            yield return new GenreSummary(new GenreSection(GenreMap.Other, GenreMap.Other, slug), count);
        }
    }

    /// <summary>Most-voted first. Ordering only — the number is never shown.</summary>
    public IReadOnlyList<RadioStation> Popular(int count) =>
        [.. Stations.OrderByDescending(station => station.Popularity).Take(count)];
}
