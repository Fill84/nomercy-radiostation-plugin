// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Design;
using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// One station as a tile, shared by every grid so two screens cannot drift into behaving
// differently for the same station.
//
// Every tile is the same size because every tile asks for the same fraction of the row.
// That is the whole fix, and the bug it replaces was one value: NmBox.Width is an NMSize,
// and an NMSize is
//
//     ^(0|px|\d+(-\d)?|\d+/\d+|auto|full|available|content|min|max|screen)$
//
// so "13rem" is not a size. It did not fail loudly - it simply was not a match, so the
// width was dropped and each tile fell back to sizing itself from its own logo. A station
// with a 1000px cover drew a 1000px tile beside a 200px one, which is what the grid looked
// like. `full` was honoured all along, which is exactly why it looked like the field worked
// and the layout did not.
//
// Built from design-system nodes rather than PluginViews.Card and PluginViews.Grid.
// PluginViews.Grid is not a grid - it is Stack(id, "row", wrap: true), the identical call
// to PluginViews.Row - and PluginViews.Card hands the image straight to a box of
// `width: full`. Neither can size a tile, so this file does.
public static class StationCards
{
    /// <summary>How many stations the browse page's "Popular" grid shows.</summary>
    public const int PopularCount = 18;

    /// <summary>
    /// How much of a row one tile takes.
    ///
    /// A fraction, not a length: see the note above on NMSize. Six to a row leaves tiles
    /// large enough to read the name under, and the gap between them pushes the sixth onto
    /// the next line on a narrow window, which wraps evenly rather than cutting one off.
    /// </summary>
    public const string TileWidth = "1/6";

    /// <summary>Shown on the toggle when the station is not a favourite yet.</summary>
    public const string AddFavouriteLabel = "Add to favourites";

    /// <summary>Shown on the toggle when it is.</summary>
    public const string RemoveFavouriteLabel = "Remove from favourites";

    /// <summary>
    /// A row of tiles that wraps, each one the same fraction of the row as the last.
    /// </summary>
    public static PluginComponent Grid(
        string id,
        IEnumerable<RadioStation> stations,
        UserState state,
        string scope)
    {
        // A set built once, not a scan per card. A grid is eighteen tiles and a favourites
        // list is unbounded, so the naive form is quadratic in the two things here most
        // likely to grow.
        HashSet<string> favourites = [.. state.Favourites.Select(favourite => favourite.Id)];

        return new PluginComponent
        {
            Id = id,
            Component = PluginComponentType.Container,
            Design = new NMCardProps
            {
                Box = new NmBox
                {
                    Width = "full",
                    Direction = "row",
                    Wrap = "wrap",
                    Align = "start",
                    Gap = new NmGap { All = "4" },
                },
            },
            Items =
            [
                .. stations.Select(station =>
                    Tile(station, favourites.Contains(station.Id), scope)),
            ],
        };
    }

    /// <summary>
    /// One station: its cover, what it is, and a control to keep it.
    ///
    /// The card carries the play action, so a click anywhere on the cover or the name
    /// starts the station - one click is listening, which is the entire job. The favourite
    /// toggle is a SIBLING of that card and not a child of it: nested inside, keeping a
    /// station also played it, because the click landed on the thing carrying play.
    /// </summary>
    public static PluginComponent Tile(RadioStation station, bool isFavourite, string scope = "")
    {
        string id = Scoped(scope, station.Id);

        return new PluginComponent
        {
            Id = $"station-tile-{id}",
            Component = PluginComponentType.Container,
            Design = new NMCardProps
            {
                Box = new NmBox
                {
                    Width = TileWidth,
                    Direction = "column",
                    Gap = new NmGap { All = "2" },
                },
            },
            Items =
            [
                Card(station, id),
                PluginViews.Button(
                    $"station-favourite-{id}",
                    isFavourite ? RemoveFavouriteLabel : AddFavouriteLabel,
                    ToggleFavourite(station),
                    variant: isFavourite ? "primary" : "secondary"),
            ],
        };
    }

    private static PluginComponent Card(RadioStation station, string id)
    {
        List<PluginComponent> children = [];

        // A station with no drawable cover contributes no node at all rather than an empty
        // frame. What a gap looks like is the design system's call, not this plugin's.
        if (Cover(station, id) is { } cover)
        {
            children.Add(cover);
        }

        children.Add(PluginViews.Text($"station-card-{id}-title", station.Name, "subtitle"));

        if (Subtitle(station) is { } subtitle)
        {
            children.Add(PluginViews.Text($"station-card-{id}-meta", subtitle, "caption"));
        }

        return new PluginComponent
        {
            Id = $"station-card-{id}",
            Component = PluginComponentType.Card,
            Action = Play(station),
            Design = new NMCardProps
            {
                Padding = "3",
                Box = new NmBox
                {
                    // Fills the tile, which is what has the fraction. A second fraction
                    // here would be a sixth of a sixth.
                    Width = "full",
                    Direction = "column",
                    Gap = new NmGap { All = "2" },
                },
            },
            Items = children,
        };
    }

    /// <summary>
    /// The cover: as wide as the tile, and square whatever shape the logo is.
    ///
    /// `full` against the tile rather than a length of its own - a length is not an NMSize
    /// and gets dropped, which is how each logo ended up at its natural size. Square and
    /// cropped so a wide logo and a tall one occupy the same space.
    /// </summary>
    private static PluginComponent? Cover(RadioStation station, string id)
    {
        if (CoverUrl(station) is not { } direct)
        {
            return null;
        }

        // Same reason as the stream: img-src refuses the station's own host.
        string url = MediaProxy.Cover(station.Id) ?? direct;

        // Src belongs on the props record, not in the loose bag beside it. Setting
        // Props["src"] and leaving Design.Src null put the url in the bag and then let the
        // merge overwrite it with null - PluginComponent.Props applies the design record
        // last - so every cover reached the browser as an img with an alt and no source.
        return new PluginComponent
        {
            Id = $"station-cover-{id}",
            Component = PluginComponentType.Image,
            Design = new NMImageProps
            {
                Src = url,
                Alt = station.Name,
                AspectRatio = "square",
                Fit = "cover",
                Rounded = "lg",
                Border = false,
                Shadow = "none",
                Box = new NmBox { Width = "full" },
            },
        };
    }

    /// <summary>
    /// The key a media intent carries the station's own id under.
    ///
    /// Not part of PlayMedia's signature - the factory takes a url, a title, an artist and
    /// a cover, and nothing else - but the payload is an open dictionary, so this rides
    /// along beside them.
    ///
    /// It is here because the client has to identify a track somehow and, with no id in
    /// the payload, it builds one out of the stream url: `plugin:{pluginId}:{streamUrl}`.
    /// That identifier then goes into a CSS selector and into a route, and a url is legal
    /// in neither - which is what throws before any audio starts, and why no /stream/
    /// request ever reaches this server. See
    /// docs/upstream/2026-08-08-plugin-media-cannot-play.md.
    ///
    /// Sending it costs nothing and is ignored until the client reads it. A station's
    /// radio-browser uuid is also the honest key: it is what the relay routes on, and it
    /// does not change when a station moves its stream, which a url-derived id does - so
    /// history and resume state stop hanging off something that was never a key.
    /// </summary>
    public const string StationIdKey = "id";

    /// <summary>
    /// The play intent for a station, so every screen starts the same station the same way.
    /// </summary>
    public static PluginActionIntent Play(RadioStation station) =>
        WithStationId(PlayIntent(station), station);

    private static PluginActionIntent PlayIntent(RadioStation station) =>
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
        WithStationId(
            PluginActionIntent.Enqueue(
                MediaProxy.Stream(station.Id) ?? station.StreamUrl,
                station.Name,
                null,
                CoverUrl(station) is null ? null : MediaProxy.Cover(station.Id)),
            station);

    // Written into the payload after the factory built it rather than by hand-rolling the
    // intent here: the factory owns which keys a media intent carries and what they are
    // called, and a copy of that here would drift the first time it gains one.
    private static PluginActionIntent WithStationId(
        PluginActionIntent intent, RadioStation station)
    {
        intent.Payload[StationIdKey] = station.Id;

        return intent;
    }

    /// <summary>
    /// Adding or removing this station, as the toggle every tile and the station page draw.
    /// </summary>
    public static PluginActionIntent ToggleFavourite(RadioStation station) =>
        PluginActionIntent.CallPlugin(
            $"{InternetRadioController.ToggleFavouriteMethod}/{Uri.EscapeDataString(station.Id)}");

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
