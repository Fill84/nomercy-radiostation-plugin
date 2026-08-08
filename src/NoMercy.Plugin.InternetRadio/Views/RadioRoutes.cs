// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

public enum RadioRouteKind
{
    Browse,
    Genre,
    AllStations,
    Station,
    Settings,
    Search,

    /// <summary>Anything else. Rendered as an empty state, never as an error.</summary>
    Unknown,
}

/// <param name="Value">The genre slug or station id, empty for the fixed routes.</param>
public sealed record RadioRoute(RadioRouteKind Kind, string Value);

// The only place a route is parsed or built.
//
// State travels in the PATH and never in a query string. The web host computes the
// route it asks for from its own `pathMatch` parameter and sends that alone, so a
// query string never leaves the browser: "/station?id=x" arrives here as "/station"
// with the id gone, and the page silently renders the wrong thing.
public static class RadioRoutes
{
    public const string Browse = "/";
    public const string AllStations = "/all";
    public const string Settings = "/settings";
    public const string Search = "/search";

    private const string GenrePrefix = "genre";
    private const string StationPrefix = "station";

    // `slug`/`id` must be non-empty for the built route to round-trip: an empty
    // value builds a trailing-slash path (e.g. "/station/"), and Parse's
    // RemoveEmptyEntries then drops that last segment, so it comes back Unknown
    // rather than Station/Genre with an empty Value. This is currently unreachable:
    // StationGates.Slugify never returns an empty string (it falls back to
    // "station"), GenreMap slugs are all derived through Slugify, and
    // StationOverrides falls back to Slugify(Name) whenever a user-supplied id is
    // blank. See Parse_TreatsARouteBuiltFromAnEmptyValueAsUnknown for the pinned
    // behaviour if that ever stops being true.
    public static string Genre(string slug) => $"/{GenrePrefix}/{Uri.EscapeDataString(slug)}";

    public static string Station(string id) => $"/{StationPrefix}/{Uri.EscapeDataString(id)}";

    public static RadioRoute Parse(string? route)
    {
        // Comma is a separator here, not a character in a value.
        //
        // A multi-segment route arrives as "/genre,ambient" rather than "/genre/ambient":
        // the client sends the segments as a repeated `route` query parameter, and ASP.NET
        // binds a repeated parameter into one comma-joined string. Every two-segment page
        // this plugin serves - every genre chip and every station - was a dead end because
        // of it, and it took reading a log line to see, because the tests hand Parse the
        // string this plugin BUILDS rather than the one it RECEIVES.
        //
        // Safe to treat as a separator: a comma cannot occur in a value we route on.
        // Slugify keeps only ASCII letters and digits, and a radio-browser id is a uuid.
        // Reported upstream - see docs/upstream/2026-08-08-route-comma-join.md.
        string[] segments = (route ?? string.Empty)
            .Split(['/', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
        {
            return new RadioRoute(RadioRouteKind.Browse, string.Empty);
        }

        if (segments.Length == 1)
        {
            return segments[0].ToLowerInvariant() switch
            {
                "all" => new RadioRoute(RadioRouteKind.AllStations, string.Empty),
                "settings" => new RadioRoute(RadioRouteKind.Settings, string.Empty),
                "search" => new RadioRoute(RadioRouteKind.Search, string.Empty),
                _ => Unknown,
            };
        }

        if (segments.Length == 2)
        {
            string value = Uri.UnescapeDataString(segments[1]);

            return segments[0].ToLowerInvariant() switch
            {
                GenrePrefix => new RadioRoute(RadioRouteKind.Genre, value),
                StationPrefix => new RadioRoute(RadioRouteKind.Station, value),
                _ => Unknown,
            };
        }

        return Unknown;
    }

    private static RadioRoute Unknown => new(RadioRouteKind.Unknown, string.Empty);
}
