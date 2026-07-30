// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

/// <param name="Tag">The radio-browser tag queried for discovery, and matched against a station's own tags.</param>
/// <param name="Label">What the user sees.</param>
/// <param name="Slug">The /genre/{slug} path segment.</param>
public sealed record GenreSection(string Tag, string Label, string Slug);

// radio-browser tags are free text and there are thousands of them, so browsing by
// raw tag is not navigation - it is a word cloud. These are the sections the browse
// page offers, and they are also exactly the queries the discovery sweep makes.
//
// ORDER IS PRIORITY. A station tagged "ambient,chillout" has to land in one section
// and only one, or it appears twice on the browse page; the earliest match wins.
public static class GenreMap
{
    /// <summary>Where a station lands when it carries no tag this plugin maps.</summary>
    public const string Other = "Other";

    public static IReadOnlyList<GenreSection> Sections { get; } =
        [
            Section("ambient", "Ambient"),
            Section("chillout", "Chillout"),
            Section("dance", "Dance & Electronic"),
            Section("house", "House"),
            Section("techno", "Techno"),
            Section("trance", "Trance"),
            Section("drum and bass", "Drum & Bass"),
            Section("jazz", "Jazz"),
            Section("classical", "Classical"),
            Section("rock", "Rock"),
            Section("metal", "Metal"),
            Section("indie", "Indie"),
            Section("pop", "Pop"),
            Section("hip hop", "Hip Hop"),
            Section("reggae", "Reggae"),
            Section("soul", "Soul & Funk"),
            Section("oldies", "Oldies"),
        ];

    private static GenreSection Section(string tag, string label) =>
        new(tag, label, StationGates.Slugify(label));

    /// <summary>
    /// The section a station belongs to, from its own tag list. Whole-tag matching,
    /// not substring: "rockabilly" is not Rock and "poparazzi" is not Pop.
    /// </summary>
    public static string Resolve(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return Other;
        }

        HashSet<string> stationTags = new(StringComparer.OrdinalIgnoreCase);
        foreach (string tag in tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            stationTags.Add(tag);
        }

        foreach (GenreSection section in Sections)
        {
            if (stationTags.Contains(section.Tag))
            {
                return section.Label;
            }
        }

        return Other;
    }

    public static GenreSection? BySlug(string slug) =>
        Sections.FirstOrDefault(section =>
            string.Equals(section.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
