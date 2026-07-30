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
        Source = source;
        FetchedAt = fetchedAt;
        LastFetchFailed = lastFetchFailed;

        _byId = new(StringComparer.OrdinalIgnoreCase);
        _byGenreSlug = new(StringComparer.OrdinalIgnoreCase);

        // Station ids are unique by construction: this is the one pass that decides
        // what the catalogue contains, and Stations, _byId and _byGenreSlug are all
        // built from it together so the three can never disagree. First wins - the
        // same rule StationGates.Deduplicate uses for name/URL collisions - and a
        // dropped collision is silent, same as there. Fetched stations have already
        // been through Deduplicate and carry radio-browser UUIDs, so they cannot
        // collide here; a user's stations.json is deliberately not gated (see
        // StationGates), and StationOverrides falls back to Slugify(Name) for a
        // blank id, so two hand-written entries whose names slugify the same - or a
        // user station whose id happens to case-collide with a fetched one - are a
        // real way to reach this, not a theoretical one.
        List<RadioStation> kept = [];

        foreach (RadioStation station in stations)
        {
            if (!_byId.TryAdd(station.Id, station))
            {
                continue;
            }

            kept.Add(station);

            string slug = StationGates.Slugify(station.Genre ?? GenreMap.Other);
            if (!_byGenreSlug.TryGetValue(slug, out List<RadioStation>? bucket))
            {
                bucket = [];
                _byGenreSlug[slug] = bucket;
            }

            bucket.Add(station);
        }

        Stations = kept;
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
    /// Every genre bucket that actually has a station in it - not only the ones
    /// <see cref="GenreMap"/> knows about. A station.json owner is free to write
    /// any genre label they like (see <see cref="StationOverrides"/>, which passes
    /// it through unnormalised on purpose), and that label has to be reachable from
    /// here or it is only reachable via /all - the README-documented escape hatch
    /// would otherwise leave its own stations undiscoverable.
    /// <see cref="GenreMap.Sections"/> order first for the genres that are known,
    /// then any additional user-supplied genres sorted for determinism, then
    /// "Other" last. A chip leading to an empty page is worse than no chip.
    /// </summary>
    public IReadOnlyList<GenreSummary> Genres =>
        [
            .. GenreMap.Sections
                .Select(section => new GenreSummary(section, ByGenreSlug(section.Slug).Count))
                .Where(summary => summary.Count > 0),
            .. UserSuppliedGenreSummaries(),
            .. OtherSummary(),
        ];

    /// <summary>
    /// Buckets that exist but are neither a known <see cref="GenreMap"/> section nor
    /// "Other" - i.e. a user's own genre label from stations.json. The label is
    /// taken from the bucket's own stations rather than re-derived from the slug,
    /// so it renders exactly as the owner wrote it.
    /// </summary>
    private IEnumerable<GenreSummary> UserSuppliedGenreSummaries()
    {
        HashSet<string> knownSlugs = new(StringComparer.OrdinalIgnoreCase)
        {
            StationGates.Slugify(GenreMap.Other),
        };
        foreach (GenreSection section in GenreMap.Sections)
        {
            knownSlugs.Add(section.Slug);
        }

        return _byGenreSlug
            .Where(bucket => !knownSlugs.Contains(bucket.Key) && bucket.Value.Count > 0)
            .Select(bucket =>
            {
                string label = bucket.Value[0].Genre ?? GenreMap.Other;
                return new GenreSummary(new GenreSection(label, label, bucket.Key), bucket.Value.Count);
            })
            .OrderBy(summary => summary.Section.Label, StringComparer.OrdinalIgnoreCase);
    }

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
