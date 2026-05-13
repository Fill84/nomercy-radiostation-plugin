# NoMercy.Plugin.InternetRadio

An official-style **`IMediaSourcePlugin`** for the
[NoMercy MediaServer](https://github.com/NoMercy-Entertainment/nomercy-media-server)
that exposes a curated list of internet radio stations as a music media
source. Each station becomes a `MediaFile` of `MediaType.Music`, with its
stream URL in `Path` and metadata (logo, homepage, genre, bitrate, codec,
country) in `Properties`.

## What's included

- `Plugin.cs` — the entry class implementing `IMediaSourcePlugin`.
- `RadioStation.cs` — immutable record describing a station.
- `RadioStations.cs` — twelve bundled stations (SomaFM Groove Salad,
  SomaFM Drone Zone, Radio Paradise, BBC Radio 1, BBC Radio 6 Music,
  NTS 1, KEXP, FIP, plus four Tomorrowland stations: One World Radio
  MP3, One World Radio HQ AAC, Anthems, Daybreak Sessions).
- `plugin.json` — the manifest the server reads at start-up.

## Plugin contract (recap)

The server's `PluginManager` (see
[`src/NoMercy.Plugins/PluginManager.cs`](https://github.com/NoMercy-Entertainment/nomercy-media-server/blob/dev/src/NoMercy.Plugins/PluginManager.cs))
scans every sub-directory of `<server>/plugins/` (skipping `data/` and
`configurations/`). For each one it:

1. Reads `plugin.json` (validated by `PluginManifestParser`).
2. Loads the DLL named by `assembly` into an isolated `AssemblyLoadContext`.
3. Reflects every public, non-abstract type implementing `IPlugin`.
4. Calls `Activator.CreateInstance` — so the class **must** have a
   parameterless constructor.
5. Calls `Initialize(IPluginContext)` once; the context exposes the event
   bus, DI container, logger, per-plugin data folder, and a JSON config
   helper.
6. Specialised interfaces (`IMediaSourcePlugin`, `IMetadataPlugin`,
   `IEncoderPlugin`, `IAuthPlugin`, `IScheduledTaskPlugin`) are picked up
   by the relevant subsystems via `PluginManager.GetPluginsOfType<T>()`.

This plugin implements `IMediaSourcePlugin`:

```csharp
Task<IEnumerable<MediaFile>> ScanAsync(string path, CancellationToken ct = default);
```

The `path` argument is normally a filesystem path. Because radio is
network-backed, this plugin re-purposes it as an optional case-insensitive
**genre filter**:

```csharp
await provider.ScanAsync("");           // all stations
await provider.ScanAsync("ambient");    // SomaFM Groove Salad + Drone Zone
await provider.ScanAsync("rock");       // Radio Paradise
```

## Building

Requires **.NET 10 SDK** (matches the server's `Directory.Build.props`).

```bash
dotnet restore
dotnet build -c Release
```

The output folder `bin/Release/net10.0/` contains both
`NoMercy.Plugin.InternetRadio.dll` and `plugin.json` — copy that folder
contents into a directory under the server's `plugins/`:

```
<server>/plugins/
└── NoMercy.Plugin.InternetRadio/
    ├── plugin.json
    └── NoMercy.Plugin.InternetRadio.dll
```

Restart the server. The plugin is auto-enabled (`autoEnabled: true` in
`plugin.json`) and you should see:

```
Internet Radio Provider v1.0.0 initialised with 8 station(s).
```

## Overriding the station list

You can replace the built-in list at runtime without recompiling. Drop a
file named `stations.json` into the plugin's data folder:

```
<server>/plugins/data/<pluginId-no-dashes>/stations.json
```

The pluginId is the manifest's `id` field with the dashes removed —
`b3d4f1a27c5e4d8a9f101c2b3a4d5e6f` for this build. The file format is a
JSON array of `RadioStation` records:

```json
[
  {
    "name": "Local Jazz FM",
    "streamUrl": "https://example.com/jazz.aac",
    "logoUrl": "https://example.com/jazz.png",
    "homepage": "https://example.com/",
    "genre": "Jazz",
    "country": "US",
    "bitrateKbps": 256,
    "codec": "aac"
  }
]
```

If parsing fails the plugin logs a warning and falls back to the built-in
defaults.

## Manifest (`plugin.json`)

```json
{
  "id": "b3d4f1a2-7c5e-4d8a-9f10-1c2b3a4d5e6f",
  "name": "Internet Radio Provider",
  "description": "Adds a curated list of internet radio stations as a music media source.",
  "version": "1.0.0",
  "targetAbi": "10.0",
  "author": "NoMercy Community",
  "projectUrl": "https://github.com/NoMercy-Entertainment/nomercy-media-server",
  "assembly": "NoMercy.Plugin.InternetRadio.dll",
  "autoEnabled": true
}
```

All fields except `targetAbi`, `author`, `projectUrl`, and `autoEnabled`
are required by `PluginManifestParser.Validate`. The `id` GUID must be
non-empty and stable across releases — `PluginManager` keys lifecycle
state off it.

## License

MIT.
