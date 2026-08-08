// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Design;
using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// One station as a tile, shared by every grid so two screens cannot drift into behaving
// differently for the same station.
//
// Drawn with NMMusicCard inside NMGrid - the same two components the app's own Artists and
// Albums screens are built from - rather than with PluginViews.Card and PluginViews.Grid.
// Those two are why the tiles were all different sizes: PluginViews.Grid is not a grid at
// all (it is Stack(id, "row", wrap: true), the same call as Row), and PluginViews.Card
// hands the image its natural size, so a 1000px logo drew a 1000px tile next to a 200px
// one. Sizing a card is the design system's job, and NMGrid already does it; a plugin
// reaching for tokens to fix the layout by hand was the wrong layer to fix it at.
//
// The card carries a link rather than an action, because that is what NMMusicCard is: its
// context_menu_items take a string action identifier, not a PluginActionIntent, so a plugin
// cannot hang play or favourite off it. Both live on the station page the card opens, which
// is also where they belong - it is the screen with room to say what you are about to play.
public static class StationCards
{
    /// <summary>How many stations the browse page's "Popular" grid shows.</summary>
    public const int PopularCount = 18;

    /// <summary>
    /// A grid of stations, sized and spaced by the app rather than by this plugin.
    /// </summary>
    public static PluginComponent Grid(
        string id,
        IEnumerable<RadioStation> stations,
        UserState state,
        string scope)
    {
        // A set built once, not a scan per card. A grid is eighteen cards and a favourites
        // list is unbounded, so the naive form is quadratic in the two things here most
        // likely to grow.
        HashSet<string> favourites = [.. state.Favourites.Select(favourite => favourite.Id)];

        return new PluginComponent
        {
            Id = id,
            Component = NmAppComponents.Grid,
            Items =
            [
                .. stations.Select(station =>
                    Card(station, favourites.Contains(station.Id), scope)),
            ],
        };
    }

    /// <summary>
    /// One station, as the app draws an artist.
    ///
    /// The props are the wrapper the contract names: an id, and a `data` object carrying
    /// the card's own fields. Written as a dictionary with the wire names spelled out
    /// because there is no props record for the app components - NmAppComponents is names
    /// only, deliberately, since their props carry database-shaped data a plugin has no
    /// business being handed.
    /// </summary>
    public static PluginComponent Card(RadioStation station, bool isFavourite, string scope = "")
    {
        string id = Scoped(scope, station.Id);

        return new PluginComponent
        {
            Id = $"station-card-{id}",
            Component = NmAppComponents.MusicCard,
            Props = new()
            {
                ["data"] = new Dictionary<string, object?>
                {
                    ["id"] = station.Id,
                    ["name"] = station.Name,
                    // Absolute, not plugin-relative: this goes to the app's own router,
                    // which does not know which plugin drew the card. See AppRoutes.
                    ["link"] = AppRoutes.Station(station.Id),
                    ["cover"] = CoverUrl(station) is null ? null : MediaProxy.Cover(station.Id),
                    ["favorite"] = isFavourite,
                    ["description"] = Subtitle(station),
                    ["type"] = "radio",
                },
            },
        };
    }

    /// <summary>
    /// The play intent for a station, so every screen starts the same station the same way.
    /// </summary>
    public static PluginActionIntent Play(RadioStation station) =>
        PluginActionIntent.PlayMedia(
            // Through this plugin's own endpoint when we know where this server lives.
            // The station's own url is refused by the dashboard's media-src, so the direct
            // url is a fallback that plays nothing - kept only so a view still renders.
            MediaProxy.Stream(station.Id) ?? station.StreamUrl,
            station.Name,
            // No artist. The player does not merely print this - it builds an artist LINK
            // from it, resolves a route for it, and derives a DOM id from the track id to
            // anchor it. A live stream has no artist, and putting the genre there made the
            // app try to route to a genre that does not exist ("Cannot read properties of
            // undefined (reading 'path')") and then build the selector
            // "#trackLink-artists-plugin:<id>:https://…/stream/…" - invalid, because a url
            // has colons and slashes in it. Those two were every "Something went wrong"
            // toast on the page.
            null,
            CoverUrl(station) is null ? null : MediaProxy.Cover(station.Id));

    /// <summary>Queueing a station, built from the same relayed urls as <see cref="Play"/>.</summary>
    public static PluginActionIntent Enqueue(RadioStation station) =>
        PluginActionIntent.Enqueue(
            MediaProxy.Stream(station.Id) ?? station.StreamUrl,
            station.Name,
            null,
            CoverUrl(station) is null ? null : MediaProxy.Cover(station.Id));

    /// <summary>
    /// Adding or removing this station, as the toggle the station page draws.
    /// </summary>
    public static PluginActionIntent ToggleFavourite(RadioStation station) =>
        PluginActionIntent.CallPlugin(
            $"{InternetRadioController.ToggleFavouriteMethod}/{Uri.EscapeDataString(station.Id)}");

    /// <summary>Shown on the toggle when the station is not a favourite yet.</summary>
    public const string AddFavouriteLabel = "Add to favourites";

    /// <summary>Shown on the toggle when it is.</summary>
    public const string RemoveFavouriteLabel = "Remove from favourites";

    /// <summary>
    /// A node id, qualified by the section it is drawn in.
    ///
    /// One station legitimately appears twice on the browse page — kept in the favourites
    /// row and popular in the grid below it — and unqualified ids made those two the same
    /// node id in one payload. A client keying on id then has two elements claiming to be
    /// the same thing, which is a real bug in the browser and an invisible one here.
    /// </summary>
    private static string Scoped(string scope, string stationId) =>
        string.IsNullOrEmpty(scope) ? stationId : $"{scope}-{stationId}";

    /// <summary>
    /// The station's logo, or null when the browser could not draw it anyway.
    ///
    /// The same judgement StationGates makes about a stream, for the same reason: the
    /// dashboard is served over https, so an http image is blocked as mixed content and
    /// renders as a broken icon — which reads as this plugin being broken rather than as a
    /// station's logo having rotted. Six of them had already rotted to 404, 403 or an HTML
    /// page, so this is the ordinary case and not the exotic one.
    /// </summary>
    public static string? CoverUrl(RadioStation station)
    {
        if (string.IsNullOrWhiteSpace(station.LogoUrl))
        {
            return null;
        }

        return Uri.TryCreate(station.LogoUrl, UriKind.Absolute, out Uri? parsed)
            && parsed.Scheme == Uri.UriSchemeHttps
            ? station.LogoUrl
            : null;
    }

    /// <summary>Genre and country, whichever of them is known. Null when neither is.</summary>
    public static string? Subtitle(RadioStation station)
    {
        string[] parts =
            [.. new[] { station.Genre, station.Country }.Where(part => !string.IsNullOrWhiteSpace(part))!];

        return parts.Length > 0 ? string.Join(" · ", parts) : null;
    }
}
