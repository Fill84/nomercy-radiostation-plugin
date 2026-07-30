// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

public enum RadioRouteKind
{
    Browse,
    Genre,
    AllStations,
    Station,
    Settings,

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

    private const string GenrePrefix = "genre";
    private const string StationPrefix = "station";

    public static string Genre(string slug) => $"/{GenrePrefix}/{Uri.EscapeDataString(slug)}";

    public static string Station(string id) => $"/{StationPrefix}/{Uri.EscapeDataString(id)}";

    public static RadioRoute Parse(string? route)
    {
        string[] segments = (route ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
