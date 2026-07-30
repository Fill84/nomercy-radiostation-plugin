// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

// The curated stations, pinned by radio-browser UUID and by nothing else. This is
// the ONLY station data in the source tree: no names, no stream URLs, no logos, no
// genres. All of that is fetched, so a station that changes its stream - as
// Tomorrowland Anthems did - is corrected upstream instead of here.
//
// Each was resolved by matching the exact stream URL this plugin used to hardcode,
// never by name similarity: an earlier pass that fell back to "most-voted station
// with a similar name" silently swapped Radio Paradise Rock Mix in for Main Mix.
//
// scripts/resolve-seeds.py re-checks that every one of these still resolves and
// still passes the gates.
//
// Not here, and deliberately: BBC Radio 1 and BBC Radio 6 Music. radio-browser has
// 13 and 3 records for them respectively and every one is HLS over http, so there is
// nothing gate-passing to pin. They were unplayable in the browser before this
// change too - the URLs this plugin shipped were http - so nothing that worked was
// lost. Adding one back is one line, the day a usable record exists.
public static class SeedStations
{
    public static IReadOnlyList<string> Uuids { get; } =
        [
            "960cf833-0601-11e8-ae97-52543be04c81", // SomaFM - Groove Salad
            "960eb2e9-0601-11e8-ae97-52543be04c81", // SomaFM - Drone Zone
            "4aad9a26-15ef-4c13-a947-74c483181b4f", // Radio Paradise - Main Mix (the HTTPS ti-main-320)
            "a3dbc189-d23e-4308-803f-5aad26432b8c", // NTS Radio 1
            "445cbb3a-1c4e-49aa-a268-f5b6acfa8f2e", // KEXP 90.3 Seattle
            "a349e1e9-2844-443a-973b-09a02fa12c8e", // FIP - Radio France
            "9e31c4e7-03b6-4a80-a4e2-5977b023d32c", // Tomorrowland - One World Radio
            "93e04f4d-f964-453a-9c64-9dd7bc32f21d", // Tomorrowland - Anthems (submitted upstream by us)
            "c77644fa-5d0d-47f6-93ef-850805efefad", // Tomorrowland - Daybreak Sessions
            "d23f9ea2-80bd-4b43-b25c-31903bbbcaec", // Tomorrowland - bigFM One World Radio
        ];

    /// <summary>
    /// How many stations to take per genre. Seventeen sections at five each is an
    /// upper bound of eighty-five before dedupe, which is a browse page worth
    /// scrolling rather than one worth searching.
    /// </summary>
    public const int PerGenreLimit = 5;
}
