// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Design;
using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// One station as a tile, shared by every grid so two screens cannot drift into behaving
// differently for the same station.
//
// Built from design-system nodes rather than PluginViews.Card, and that is the whole point
// of this file. The convenience factory gives a card a box of `width: full` and hands the
// image straight to it, so a 400x400 station logo drew about 830px tall on a desktop
// panel: one tile filled the viewport, its name sat below the fold, and eighteen stations
// became eighteen full-width blocks a screen apart. How wide a tile is has to be this
// plugin's decision, so it is made here.
public static class StationCards
{
    /// <summary>How many stations the browse page's "Popular" grid shows.</summary>
    public const int PopularCount = 18;

    /// <summary>
    /// How wide one tile is.
    ///
    /// Fixed rather than fluid, because the grid is a wrapping row: a tile with no width
    /// of its own takes the whole row, and the row then holds exactly one tile.
    /// </summary>
    public const string TileWidth = "13rem";

    /// <summary>Shown on the toggle when the station is not a favourite yet.</summary>
    public const string AddFavouriteLabel = "Add to favourites";

    /// <summary>Shown on the toggle when it is.</summary>
    public const string RemoveFavouriteLabel = "Remove from favourites";

    /// <summary>
    /// A tile: cover, name, where it is from, and a control to keep it.
    ///
    /// The tile itself carries the play action, so a click anywhere on it starts the
    /// station — one click is listening, which is the entire job. Keeping is a separate
    /// control beneath, because a component carries one action and that one is taken.
    ///
    /// The two favourite states differ by label and by variant, never by colour alone: a
    /// toggle whose only difference is a tint is unreadable to a good share of viewers,
    /// and to everyone in a screenshot. No icon — the Moooom set verified for this plugin
    /// has no heart, and pluginIcon() substitutes `plugged` for a name the app does not
    /// have rather than failing, so a guess would put a plug on every tile.
    /// </summary>
    public static PluginComponent WithFavourite(
        RadioStation station, bool isFavourite, string scope = "")
    {
        string id = Scoped(scope, station.Id);
        List<PluginComponent> children = [];

        // A station with no drawable cover contributes no node at all rather than an empty
        // frame. What a gap looks like is the design system's call, not this plugin's.
        if (Cover(station, scope) is { } cover)
        {
            children.Add(cover);
        }

        children.Add(PluginViews.Text($"station-card-{id}-title", station.Name, "subtitle"));

        if (Subtitle(station) is { } subtitle)
        {
            children.Add(PluginViews.Text($"station-card-{id}-meta", subtitle, "caption"));
        }

        children.Add(PluginViews.Button(
            $"station-favourite-{id}",
            isFavourite ? RemoveFavouriteLabel : AddFavouriteLabel,
            PluginActionIntent.CallPlugin(
                $"{InternetRadioController.ToggleFavouriteMethod}/{Uri.EscapeDataString(station.Id)}"),
            variant: isFavourite ? "primary" : "secondary"));

        return new PluginComponent
        {
            Id = $"station-card-{id}",
            Component = PluginComponentType.Card,
            Design = new NMCardProps
            {
                Padding = "3",
                Box = new NmBox
                {
                    Width = TileWidth,
                    Direction = "column",
                    Gap = new NmGap { All = "2" },
                },
            },
            Items = children,
            Action = Play(station),
        };
    }

    /// <summary>
    /// The play intent for a station, so the all-stations table and the tiles cannot drift
    /// into starting the same station two different ways.
    /// </summary>
    public static PluginActionIntent Play(RadioStation station) =>
        PluginActionIntent.PlayMedia(
            station.StreamUrl,
            station.Name,
            // The player shows this where a track's artist would go; the genre is the most
            // useful thing a live stream has to put there.
            station.Genre,
            // The full-size cover, not a tile-sized one: this goes to the now-playing
            // panel, which wants a real image.
            CoverUrl(station)
        );

    /// <summary>
    /// The cover: square and cropped, so a wide logo and a tall one occupy the same tile,
    /// and bounded by the tile rather than by the panel.
    /// </summary>
    private static PluginComponent? Cover(RadioStation station, string scope)
    {
        if (CoverUrl(station) is not { } url)
        {
            return null;
        }

        // Src belongs on the props record, not in the loose bag beside it. Setting
        // Props["src"] and leaving Design.Src null put the url in the bag and then let the
        // merge overwrite it with null - PluginComponent.Props applies the design record
        // last - so every cover reached the browser as an img with an alt and no source.
        // That is what the whole grid of alt text was.
        return new PluginComponent
        {
            Id = $"station-cover-{Scoped(scope, station.Id)}",
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
