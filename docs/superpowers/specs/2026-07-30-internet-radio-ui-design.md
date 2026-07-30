# Internet Radio — browse and play UI

**Date:** 2026-07-30
**Version this describes:** 1.0.2
**Status:** design, awaiting approval

## Why this exists

The plugin as shipped does nothing.

It implements exactly one hook, `IMediaSourcePlugin`, and returns its twelve
stations from `ScanAsync` as `MediaFile` records. That interface appears in the
whole of `nomercy-media-server` in two places: its own declaration file, and one
abstractions test. No subsystem calls it. There is no ingest path, no library
writer, no scan trigger — nothing consumes a media-source plugin. So every line
of `Plugin.cs`, `RadioStation.cs`, `RadioStations.cs` and the `stations.json`
override is unreachable, and installing the plugin gives a user no way to hear
anything.

This version replaces that dead hook with the one the server actually serves:
`IUiPlugin`. A plugin's declarative view is rendered by `PluginUiController` and
`views/Plugins/Host/index.vue`, and a card's `playMedia` intent is turned into
`playTrack()` by `lib/plugin/actionInterpreter.ts` — entirely client-side. Which
means a station can reach the built-in player without the library knowing
anything about it, and without the server growing a media-source path first.

It also fixes the manifest drift Stoney reported: tag `v1.0.1` points at commit
`2c57623`, whose `plugin.json` reads `1.0.0`. A server installing that release
reports 1.0.0, is told an update is available, and stays told forever.

## What the platform can and cannot do today

Verified against `nomercy-media-server@dev` and `nomercy-app-web@master` by
reading both sides of each path, not by assuming the contract is honoured.

| Path | Works | Evidence |
| --- | --- | --- |
| `IUiPlugin.NavEntries` → sidebar | yes | `PluginUiDescriptorDto.Navigation`, `Sidebar.vue` |
| `IUiPlugin.GetViewAsync` → render | yes | `PluginUiController.View`, `Host/index.vue` |
| `playMedia` / `enqueue` intent | yes | `actionInterpreter.ts` → `playTrack` |
| `navigate` intent, path-based | yes | `pluginRoutePath`, host's `pathMatch` |
| `openWebView` intent | yes | host's `webViewUrl` |
| `refreshView` intent, `refreshInterval` | yes | host's `refetch`, interval watch |
| `callPlugin` over REST | **no** | unversioned `PluginRouteConvention` vs `/api/v1` client — [server issue #26](https://github.com/NoMercy-Entertainment/nomercy-media-server/issues/26) |
| `callPlugin` over hub | **no** | nothing ever calls `IPluginHubRouter.Register`, so `RouteAsync` drops at its first lookup |
| `IMediaSourcePlugin.ScanAsync` | **no** | no consumer anywhere in the server |
| `PluginViewRequest.Query` from web | **no** | the host sends only `route`; query params never leave the browser |

Two consequences shape this design.

**There is no working inbound transport.** REST 404s per issue #26, and the hub
is not the workaround it looks like — `PluginHubRouter` keeps a handler
dictionary that nothing populates. So the UI cannot send the plugin anything.
Every screen here is therefore built from the view plus client-side intents
only, and the plugin stores no user-supplied state. Favourites and
custom-station editing are out of scope until at least one inbound path lands;
shipping forms that silently 404 is the claim the torrent plugin had to
retract in `3e466d4`, and it is not worth making twice.

**Routes must carry their state in the path.** The server populates
`PluginViewRequest.Query` faithfully, but the web host never sends query
parameters — it derives `route` from `route.params.pathMatch` and passes that
alone. So `/station?id=x` loses `id`, while `/station/{id}` arrives intact as
`pathMatch = "station/{id}"`. All routes below are path-based.

### A rendering detail the reference plugin gets wrong

`PluginText`'s variant vocabulary is `title`, `subtitle`, `caption`; anything
else falls through to body text. `nomercy-torrent-plugin`'s `SettingsView`
passes `"heading"`, `"subheading"` and `"body"`, so its section headings render
as ordinary paragraphs. The vocabulary is nowhere named in the abstractions —
unlike `PluginComponentType` and `PluginActionType`, which exist precisely to
stop this — so it is copied by anyone using that plugin as a template. This
design uses `title` / `subtitle` / `caption`, and the mismatch is reported
upstream (see *Upstream reports*).

## Screens

Five routes, each with one job and no overlap. Two nav mounts: the browse root
under Music, where someone looking for radio will look, and the settings page
under the plugin settings list.

### `/` — Browse

- `Text` title, and a `caption` naming how many stations and genres there are.
- `Row` of `Button`s, one per genre → `navigate("/genre/{slug}")`, plus an
  "All stations" button → `navigate("/all")`.
- `Text` subtitle "Popular".
- `Grid` of the most-played stations as `Card`s. **A card's action is
  `playMedia`** — one click and it is playing, which is the whole point of the
  plugin. Subtitle is `"{genre} · {country}"`, image is the station logo.

`PluginGrid` is `repeat(auto-fill, minmax(10rem, 1fr))` with square cover
images, so a logo grid is what it was built for.

### `/genre/{slug}` — One genre

Back `Button` (`arrowLeft`) → `/`, `Text` title with the genre name, a `caption`
count, and a `Grid` of that genre's stations as `playMedia` cards. An unknown
slug renders an `EmptyState` rather than an error — a stale bookmark is not a
failure worth reporting.

### `/all` — Every station, with the details

A `Table`: Name, Genre, Country, Bitrate, Codec. **Each row's action is
`navigate("/station/{id}")`**, so this is the surface for inspecting before
playing, as opposed to the grids, which play immediately. Splitting it this way
means neither surface has to carry two competing affordances per station.

### `/station/{id}` — One station

- `Detail` with the logo, the name, and a description composed from what is
  known: `"{genre} from {country}. {bitrate} kbps {codec}."`
- `Row` of `Button`s: **Play** (`playMedia`, icon `play`), **Add to queue**
  (`enqueue`, icon `playlistAdd`), and **Open homepage** (`openWebView`, icon
  `globe`) when a homepage is known.
- `Table` of the full record: genre, country, language, bitrate, codec, stream
  URL, and provenance — the radio-browser station UUID for a bundled station, or
  "user-supplied" for one from `stations.json`.
- `Row` of back `Button`s to `/all` and `/`.

Unknown id → `EmptyState`.

### `/settings` — Status and where to put your own stations

There is nothing to configure, so this page says what it is instead of
pretending otherwise:

- `Badge` reading either "Bundled catalogue" or "Custom catalogue" so it is
  clear which list is live.
- `caption` text with the plugin's real `DataFolderPath` and the
  `stations.json` filename, so nobody has to derive the dashless-GUID path from
  the README.
- A `Table` of station counts per genre — the one diagnostic worth having.
- A short `Text` stating that editable settings wait on the server's inbound
  plugin path, naming issue #26 and the hub gap.

Icons are checked against the Moooom set (`resources/icons/`), because
`pluginIcon()` silently substitutes `plugged` for a name the app does not have:
`portableRadio`, `play`, `playlistAdd`, `globe`, `arrowLeft`, `gridMasonry`,
`settings` all exist.

## Structure

Views are pure static functions — catalogue in, `PluginView` out, no
`IPluginContext` and no I/O — which is what makes them cheap to test
exhaustively. `InternetRadioPlugin` does the loading and route dispatch; the
views never learn where a station came from.

```
src/NoMercy.Plugin.InternetRadio/
  PluginIdentity.cs            id, name, description, version, assembly — one source of truth
  InternetRadioPlugin.cs       IUiPlugin: lifecycle, catalogue load, route dispatch
  Catalog/
    RadioStation.cs            one station; StationId, Slug
    StationCatalog.cs          embedded catalogue + optional override, grouped by genre
    CatalogSource.cs           bundled vs user-supplied, for the settings badge
    stations.json              embedded resource, generated (see Catalogue)
  Views/
    RadioRoutes.cs             the only place a route is parsed or built
    BrowseView.cs
    GenreView.cs
    AllStationsView.cs
    StationView.cs
    SettingsView.cs
tests/NoMercy.Plugin.InternetRadio.Tests/
  ManifestTests.cs             manifest agrees with PluginIdentity and the built assembly
  DiscoveryContractTests.cs    parameterless ctor, IUiPlugin, hooks match NavEntries
  PluginLifecycleTests.cs      Initialize / Dispose / view-after-dispose / bad config
  Catalog/StationCatalogTests.cs
  Catalog/CatalogDataTests.cs  every shipped station: https, unique id, required fields
  Views/*Tests.cs              one per view
  Routing/RadioRoutesTests.cs
  TestSupport/                 FakePluginContext, FakeConfiguration, RecordingLogger
scripts/
  fetch-abstractions.sh|.ps1   pack the contract to a local feed (from the torrent plugin)
  build-catalog.sh             regenerate stations.json from radio-browser.info
  verify-streams.sh            HEAD-check every stream URL
```

`GetViewAsync` never throws into the request pipeline: a view built after
`Dispose`, or from a config the host cannot read, returns a rendered
`EmptyState` and logs, because this page is the plugin's only diagnostic
surface and a failure that hides its own cause is worse than a visible one.

## Catalogue

Twelve hand-written stations is both too few to be useful and, as the
Tomorrowland URL fix showed, a maintenance liability — a URL nobody sourced is a
URL nobody can re-check.

So the catalogue becomes **generated data, not code**: `scripts/build-catalog.sh`
queries `radio-browser.info` per genre and writes `Catalog/stations.json`, which
ships as an embedded resource. Adding a station is a data change.

Selection gates, applied in the script:

- **`is_https=true` — mandatory, not a preference.** The web client is served
  over HTTPS, so an `http://` stream is blocked as mixed content and simply will
  not play. The current catalogue's BBC Radio 1 entry
  (`http://stream.live.vc.bbcmedia.co.uk/bbc_radio_one`) is unplayable in the
  browser today for exactly this reason.
- `hls=0` — HLS in a plain audio element only works in Safari.
- `lastcheckok=1`, `hidebroken=true` — radio-browser's own liveness signal.
- Deduplicated by resolved stream URL and by normalised name.
- Ordered by votes, capped per genre; roughly 14 genres, about 60 stations.
- Written in a deterministic order with a `source` and `generatedAt` header, so
  a regeneration produces a reviewable diff rather than a reshuffle.

The existing curated stations (SomaFM, Radio Paradise, the Tomorrowland set) are
looked up by name in radio-browser and kept **if they pass the same gates**, so
they carry a station UUID and provenance like everything else. Any that do not
pass are dropped and named in the script's output rather than carried on trust.

`CatalogDataTests` then asserts offline, over the shipped resource, that every
station is HTTPS, has a unique id, and has the fields the views require — so a
mixed-content regression fails the build instead of a user's player.

`scripts/verify-streams.sh` actually connects to each stream. It is a
development and `workflow_dispatch` tool, deliberately **not** on the push path:
a third party's outage must not turn a green build red.

### The user override stays compatible

`stations.json` in the plugin's data folder still replaces the bundled list, and
the bare-JSON-array shape the current README documents still parses. The
generated resource uses the richer `{ source, generatedAt, stations: [...] }`
object, and the loader accepts either. A station with no id gets a stable slug
derived from its name; if two stations resolve to the same id the first wins and
the loader logs the one it dropped, because a route that resolves to two
stations is worse than a catalogue that is visibly one short.

## Version integrity

The reported defect is that a tag and a manifest disagreed. One number, three
files, no guard.

1. `plugin.json`, `<Version>` in the csproj, and `PluginIdentity.Version` all
   read **1.0.2**. `ManifestTests` asserts they agree, so drift fails a test.
2. **CI gate:** on a `v*` tag, the build asserts `v{plugin.json version}` equals
   the tag and fails naming both if not. This is the part that makes shipping
   another 1.0.1-labelled-1.0.0 impossible.
3. **Post-release bump:** after a release publishes, CI patch-bumps all three
   files on the default branch and pushes `chore(release): open X.Y.Z for
   development [skip ci]`. Convenience, not the guard — the gate above is what
   protects the artifact.

## CI

Rebased on the torrent plugin's workflow, which is further along than this
repo's: shared `fetch-abstractions.sh` so CI and a developer's machine cannot
drift, `nuget.config` with `packageSourceMapping` pinning `NoMercy.*` to the
local feed, no `set -x` around the auth header, the token in a file rather than
an argv, a test step that gates the artifact, the staging allowlist checked
against `deps.json`, and the assertion that no host-owned assembly ships.

Three additions of our own:

- The version gate above.
- **sha256 of the zip**, printed, in the release notes, and machine-readable.
- **`repository.json` attached as a release asset** — a `PluginRepositoryManifest`
  naming this plugin, its version, the release asset's `downloadUrl`, the
  checksum, and a timestamp. This is what Stoney's catalogue needs in order to
  list the plugin; right now listing it means hand-writing that entry and
  hand-checking the version, which is how the 1.0.0/1.0.1 mismatch became his
  problem rather than ours.

## Manifest

```json
{
  "id": "b3d4f1a2-7c5e-4d8a-9f10-1c2b3a4d5e6f",
  "name": "Internet Radio",
  "description": "Browse and play internet radio stations in the built-in player.",
  "version": "1.0.2",
  "targetAbi": "10.0",
  "author": "NoMercy Community",
  "projectUrl": "https://forgejo.phillippepelzer.me/FiLL/nomercy-radiostation-plugin",
  "assembly": "NoMercy.Plugin.InternetRadio.dll",
  "autoEnabled": true,
  "capabilities": {
    "hooks": ["ui"],
    "rest": false,
    "ws": false,
    "ui": {
      "mounts": [
        { "section": "music",    "label": "Internet Radio", "icon": "portableRadio", "route": "/" },
        { "section": "settings", "label": "Internet Radio", "icon": "portableRadio", "route": "/settings" }
      ]
    }
  }
}
```

- **`id` is unchanged.** The host keys lifecycle state off it across restarts.
- **`hooks` is `["ui"]` and nothing else.** `PluginUiController.HasUi` requires
  the `ui` hook or the plugin is installed, enabled and invisible. `mediaSource`
  is removed because nothing consumes it, and a manifest is the thing an owner
  reviews at consent time — declaring a capability that does nothing is the
  false promise this standard exists to prevent. No user behaviour changes,
  because nothing called it.
- **`rest` and `ws` stay false.** No controller, no hub handler, nothing to
  declare. They flip when there is something behind them.
- **No `network` capability and no grants.** The client fetches the streams; the
  plugin never opens a socket, so it needs no host grant for any station.
- **`projectUrl` now points at this repository** instead of the media server. A
  catalogue entry uses it as the plugin's own page.
- **Name and description** drop "Provider" and the media-source claim, both of
  which described the hook being removed.

`autoEnabled` stays `true`: the only declared hook is `ui`, none of it is in
`PluginHookCapability.Elevated`, and there is no network access to consent to.

## Upstream reports

Four findings for `nomercy-media-server`, to file once approved. None is worked
around in this plugin: a hand-rolled versioned route or a private ingest path
would break the moment the real fix lands.

1. **`IPluginHubRouter.Register` is never called.** `PluginHubRouter` keeps a
   handler dictionary that nothing populates, so `RouteAsync` drops every
   message at its first lookup and `IPluginHubHandler` cannot receive anything.
   This matters most because the hub is the natural workaround for issue #26,
   and it is not available either — so there is currently no inbound path from
   a plugin's UI to the plugin.
2. **`IMediaSourcePlugin` has no consumer.** Declared, documented, exercised by
   an abstractions test, and called by nothing. A plugin author implements it
   and gets silence. Either wire it or say it is not yet live.
3. **`PluginText` has an unnamed variant vocabulary.** The web renderer knows
   `title`, `subtitle`, `caption`; everything else silently reads as body. This
   is precisely the failure `PluginComponentType` and `PluginActionType` exist
   to prevent, and it is already mis-hit by `nomercy-torrent-plugin`. Suggest a
   `PluginTextVariant` constants class and a `PluginViews.Text` doc comment.
4. **The web host never forwards `PluginViewRequest.Query`.** The server fills
   it in from the request, but `Host/index.vue` sends only `route`, so a plugin
   using query parameters loses them with no error. Either forward them or say
   in `PluginViewRequest` that path segments are the portable option.

## Out of scope

- **Favourites, custom-station editing, any write path.** No inbound transport
  exists. Revisit when issue #26 or the hub registration lands.
- **`IMediaSourcePlugin` / library ingest.** Roughly one file once the server
  grows a consumer; pointless before.
- **Now-playing / ICY metadata.** Needs the plugin to read the stream itself,
  which needs a network grant per station host — a poor trade for a track title,
  and better solved in the player.
- **Search.** A search box needs a round trip, and there is none. Genre
  navigation plus the `/all` table covers browsing without one.

## Definition of done

- `dotnet build -c Release -p:TreatWarningsAsErrors=true` clean.
- `dotnet test -c Release` green, including manifest, catalogue, routing and
  every view.
- `scripts/verify-streams.sh` run once by hand against the generated catalogue,
  with any dead station removed before release.
- Committed locally. Pushed and tagged `v1.0.2` only on explicit ask, then
  `ci-watch`.
