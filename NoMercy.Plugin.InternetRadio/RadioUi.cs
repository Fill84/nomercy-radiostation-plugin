using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// The stations, as screens.
///
/// The plugin was a media source with no interface of its own, so its stations
/// were only reachable through whatever the app happened to show. This gives it
/// pages: a wall of stations, one station on its own, and a place to play it.
/// </summary>
public sealed class RadioUi : IPlugin, IUiPlugin
{
    private static readonly IReadOnlyList<RadioStation> Stations = RadioStations.Defaults;

    public Guid Id => Guid.Parse("b3d4f1a2-7c5e-4d8a-9f10-1c2b3a4d5e6f");

    public string Name => "Internet Radio Provider";

    public string Description => "Adds a curated list of internet radio stations as a music media source.";

    public Version Version => new(1, 0, 0);

    /// <summary>
    /// The pages this plugin serves. Declared so the server can list them and
    /// every client can register a route for each without opening one first.
    /// </summary>
    public static readonly PluginRouteTable Table = new(
        new PluginRoute
        {
            Name = "stations",
            Path = "/",
            Label = "stations.title",
            // Stations are mostly artwork, and a wall of tiles is a shape both a
            // pointer and a remote handle well.
            Layout = PluginLayout.Grid
        },
        new PluginRoute
        {
            Name = "station",
            Path = "/stations/:index",
            Label = "station.title",
            // One station, at a readable measure. `list-detail` is what this
            // page wants — the wall beside the station, so the viewer can move
            // on without going back — and it is not used yet because the design
            // system has no list-item component to build the list out of. A
            // layout is a promise about what the payload contains, and this
            // payload is one thing.
            Layout = PluginLayout.Standard,
            LayoutBySurface = { [PluginSurface.Tv] = PluginLayout.Immersive }
        });

    public PluginRouteTable Routes => Table;

    public IReadOnlyList<PluginNavEntry> NavEntries =>
    [
        new()
        {
            Section = PluginKind.Music,
            Label = "stations.title",
            Route = Table.PathTo("stations"),
            Icon = "radio"
        }
    ];

    public void Initialize(IPluginContext context)
    {
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public Task<PluginView> GetViewAsync(PluginViewRequest request, CancellationToken ct)
    {
        PluginRouteMatch? match = Table.Resolve(request.Route);

        // A route this plugin does not serve lands on its own front page. A
        // viewer following a stale link should find the stations, not a blank
        // screen that reads as a broken plugin.
        if (match?.Route.Name != "station")
            return Task.FromResult(StationWall(request.Surface));

        return Task.FromResult(
            int.TryParse(match.Param("index"), out int index) && index >= 0 && index < Stations.Count
                ? StationPage(index, request.Surface)
                : StationWall(request.Surface));
    }

    /// <summary>Every station, as tiles.</summary>
    private static PluginView StationWall(string surface)
    {
        List<PluginComponent> tiles = [];

        for (int index = 0; index < Stations.Count; index++)
        {
            RadioStation station = Stations[index];

            tiles.Add(new()
            {
                Id = $"station-{index}",
                Component = "NMCard",
                Props = new()
                {
                    ["box"] = new Dictionary<string, object?>
                    {
                        ["padding"] = new Dictionary<string, object?> { ["all"] = "3" }
                    }
                },
                // Relative: the plugin never writes the prefix it sits behind.
                Action = Table.GoTo("station", new Dictionary<string, string> { ["index"] = index.ToString() }),
                Items = [.. Tile($"art-{index}", station), .. Heading($"name-{index}", station.Name, station.Genre)]
            });
        }

        return new()
        {
            Layout = Table.Routes.First(route => route.Name == "stations").LayoutFor(surface),
            Components = [.. tiles]
        };
    }

    /// <summary>One station, with the thing that plays it.</summary>
    private static PluginView StationPage(int index, string surface)
    {
        RadioStation station = Stations[index];

        List<PluginComponent> parts =
        [
            .. Tile("art", station, "1/3"),
            .. Heading("heading", station.Name, station.Genre ?? station.Country),
            new()
            {
                Id = "play",
                Component = "NMButton",
                Props = new() { ["variant"] = "primary" },
                // A button reads its label from what is inside it, so the words
                // are a child. Setting only ariaLabel drew an empty square that
                // a screen reader announced and a person could not.
                Items = [Text("play-label", "station.play")],
                Action = PluginActionIntent.PlayMedia(station.StreamUrl, station.Name, cover: station.LogoUrl)
            }
        ];

        if (station.BitrateKbps is not null)
            parts.Add(new()
            {
                Id = "bitrate",
                Component = "NMBadge",
                Props = new()
                {
                    ["text"] = $"{station.BitrateKbps} kbps",
                    ["variant"] = "ghost"
                }
            });

        parts.Add(new()
        {
            Id = "back",
            Component = "NMButton",
            Props = new() { ["variant"] = "tertiary" },
            Items = [Text("back-label", "back")],
            Action = Table.GoTo("stations")
        });

        return new()
        {
            Layout = Table.Routes.First(route => route.Name == "station").LayoutFor(surface),
            Components =
            [
                new()
                {
                    Id = "station",
                    Component = "NMCard",
                    Props = new()
                    {
                        ["box"] = new Dictionary<string, object?>
                        {
                            ["padding"] = new Dictionary<string, object?> { ["all"] = "4" },
                            ["gap"] = new Dictionary<string, object?> { ["all"] = "3" }
                        }
                    },
                    Items = [.. parts]
                }
            ]
        };
    }

    /// <summary>
    /// A station's name, and what it plays under it.
    ///
    /// A header takes its title as a child rather than as a prop, so the words
    /// are text nodes. Passing them as props put them on the element as
    /// attributes and drew an empty header.
    /// </summary>
    private static IEnumerable<PluginComponent> Heading(string id, string title, string? subtitle)
    {
        yield return new()
        {
            Id = id,
            Component = "NMContentHeader",
            Items = [Text($"{id}-title", title)]
        };

        // A separate paragraph rather than a second line in the header: the
        // header lays its children out in one row, so both read as one run-on
        // sentence when they share it.
        if (!string.IsNullOrWhiteSpace(subtitle))
            yield return new()
            {
                Id = $"{id}-genre",
                Component = "NMHelper",
                Props = new() { ["helperText"] = subtitle }
            };
    }

    private static PluginComponent Text(string id, string value) =>
        new()
        {
            Id = id,
            Component = "NMText",
            Props = new() { ["text"] = value }
        };

    /// <summary>
    /// A station's logo, or nothing where it has none.
    ///
    /// A skeleton was drawn in its place at first, which is worse than nothing:
    /// a skeleton means "this is on its way", and a station that ships no logo
    /// never resolves. The tile is text-only instead, which is honest and what
    /// the grid handles anyway.
    /// </summary>
    private static IEnumerable<PluginComponent> Tile(string id, RadioStation station, string? width = null)
    {
        if (station.LogoUrl is null)
            yield break;

        Dictionary<string, object?> props = new()
        {
            ["src"] = station.LogoUrl,
            ["alt"] = station.Name,
            ["fit"] = "contain",
            // A tile has to hold its shape before the logo arrives and keep it
            // when the logo never does. Station logos rot — several in this
            // catalogue answer 403 or 404 today — and without a shape to
            // reserve, those cards collapsed to a grey sliver.
            ["aspectRatio"] = "square"
        };

        // On its own page the logo is one element among several rather than the
        // page itself, so it takes a share of the width instead of all of it.
        if (width is not null)
            props["box"] = new Dictionary<string, object?> { ["width"] = width };

        yield return new()
        {
            Id = id,
            Component = "NMImage",
            Props = props
        };
    }
}
