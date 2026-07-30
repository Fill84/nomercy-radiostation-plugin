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
| Manifest-declared outbound hosts | yes, no prompt | `PluginNetworkAllowlistHandler` allows "the union of the manifest's static hosts and whatever the owner has granted since" |
| `autoEnabled` with a `network` capability | **no** | `PluginConsentService.IsBaseline` returns false when `Network is not null`, so `PluginLoader` starts it `Disabled` until the owner consents |

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
  URL, and provenance — the radio-browser station UUID for a fetched station, or
  "user-supplied" for one from `stations.json`.
- `Row` of back `Button`s to `/all` and `/`.

Unknown id → `EmptyState`.

### `/settings` — Status and where to put your own stations

There is nothing to configure, so this page says what it is instead of
pretending otherwise:

- `Badge` for where the catalogue came from — fetched, served from cache, or a
  user override — so it is clear which list is live.
- **How old the catalogue is**, and when the refresh job next runs. This is the
  first thing anyone will want when a station is missing.
- A **Refresh now** `Button` (`refreshView`). It costs nothing: re-rendering
  re-runs the cache-first read, which fetches when the cache is empty or stale.
- `caption` text with the plugin's real `DataFolderPath` and the `stations.json`
  filename, so nobody has to derive the dashless-GUID path from the README.
- A `Table` of station counts per genre — the one diagnostic worth having.
- When the last fetch failed, a `Badge` and the failure's shape (not the
  exception text), so a stale catalogue explains itself.
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
  InternetRadioPlugin.cs       IUiPlugin + IScheduledTaskPlugin: lifecycle, route dispatch, refresh job
  Catalog/
    RadioStation.cs            one station; StationId, Slug
    StationCatalog.cs          the resolved catalogue, grouped by genre
    CatalogSource.cs           fetched / cached / user override, for the settings badge
    SeedStations.cs            the ten pinned UUIDs, and nothing else about them
    RadioBrowserClient.cs      byuuid + per-genre search, over IPluginContext.HttpClient
    StationGates.cs            https / non-HLS / checked-ok / dedupe — the admission rules
    GenreMap.cs                radio-browser tags -> the browse page's genre sections
    CatalogCache.cs            read/write catalog-cache.json in the data folder
    CatalogProvider.cs         cache-first, fetch-on-empty, override-wins
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
  Catalog/StationGatesTests.cs http rejected, HLS rejected, checkok, dedupe
  Catalog/RadioBrowserClientTests.cs   500 / timeout / bad JSON / empty / all-rejected
  Catalog/CatalogProviderTests.cs      cache-first, fetch-on-empty, stale-beats-empty, override-wins
  Catalog/SeedTests.cs         the pinned UUIDs are well-formed and unique (offline)
  Catalog/GenreMapTests.cs
  Views/*Tests.cs              one per view
  Routing/RadioRoutesTests.cs
  TestSupport/                 FakePluginContext, FakeConfiguration, FakeHttpMessageHandler,
                               RecordingLogger
scripts/
  fetch-abstractions.sh|.ps1   pack the contract to a local feed (from the torrent plugin)
  resolve-seeds.sh             re-check the pinned UUIDs still exist and still pass the gates
```

The network client, the gates and the provider are separate on purpose: the
gates are where a mixed-content stream is refused, and they are worth testing
without a socket in sight.

`GetViewAsync` never throws into the request pipeline: a view built after
`Dispose`, or from a config the host cannot read, returns a rendered
`EmptyState` and logs, because this page is the plugin's only diagnostic
surface and a failure that hides its own cause is worse than a visible one.

## Catalogue

**No station data is hardcoded.** Every name, stream URL, logo, genre, country,
bitrate and codec comes from `radio-browser.info` at runtime. A hand-written URL
is a URL nobody can re-check, which is how the Tomorrowland fix became necessary
and how BBC Radio 1 came to ship a stream that cannot play at all.

The only station data in the source tree is **ten UUIDs** — see *Seed stations*
below. No URLs, no logos, no names.

### How the catalogue is built at runtime

1. **Seeds** — one `POST /json/stations/byuuid` with all ten pinned UUIDs
   returns the curated stations with current data. Verified: one request, all
   seeds, HTTP 200.
2. **Discovery** — one `GET /json/stations/search?tagList=…` per configured
   genre, ordered by votes, so the catalogue is broad without anyone curating
   it.
3. **Gates**, applied to everything including seeds:
   - **HTTPS only — mandatory, not a preference.** The web client is served over
     HTTPS, so an `http://` stream is blocked as mixed content and will not
     play. This is not hypothetical: the current catalogue's BBC entries are
     `http://` and are unplayable in the browser today.
   - **Not HLS** — HLS in a plain audio element only works in Safari.
   - `lastcheckok == 1` and `hidebroken=true` — radio-browser's own liveness
     signal.
   - A name and a resolved URL must both be present.
4. **Dedupe** by resolved stream URL, then by normalised name.
5. **Genre** comes from the station's `tags`, mapped onto the configured genre
   list so the browse page has stable sections rather than a thousand raw tags.
6. **Cache** the result to `catalog-cache.json` in the plugin's data folder,
   with the fetch timestamp.

`GetViewAsync` reads the cache and never the network — a view is rendered on
every navigation, and a screen that makes fifteen API calls per click is not a
screen. A `refresh` scheduled job rebuilds the cache daily.

### Cold start and failure

- **Cache present:** served immediately, however old. Stale beats empty.
- **Cache empty (first run):** one bounded fetch inline, so the first visit
  works instead of waiting for the cron tick.
- **Fetch fails and no cache:** an `EmptyState` saying so, with a **Retry**
  button (`refreshView`), and the failure logged. The page never renders a
  spinner forever or an empty grid that reads as "no stations exist".
- **Fetch fails but a cache exists:** the cache is served and the settings page
  shows how old it is. A third party's outage must not empty a working
  catalogue.

Because the plugin now talks to the network, the failure modes are tested with a
fake `HttpMessageHandler`: HTTP 500, a timeout, malformed JSON, an empty array,
and a payload where every station fails the gates. The gate filtering itself is
tested the same way, which is what stops a mixed-content regression — the
assertion moved from shipped data to the code that admits it.

### Seed stations — the curated ten

Resolved against radio-browser by matching **the exact stream URL** we already
had, never by name popularity. An early attempt that fell back to "most-voted
station with a similar name" silently substituted Radio Paradise *Rock Mix* for
*Main Mix*, which is the same inventing this design exists to stop. Where a URL
matched nothing that passes the gates, the station is dropped and named here
rather than quietly replaced.

| Station | radio-browser UUID | Note |
| --- | --- | --- |
| SomaFM — Groove Salad | `960cf833-0601-11e8-ae97-52543be04c81` | 47,433 votes; canonical entry |
| SomaFM — Drone Zone | `960eb2e9-0601-11e8-ae97-52543be04c81` | |
| Radio Paradise — Main Mix | `4aad9a26-15ef-4c13-a947-74c483181b4f` | **corrected**: `ti-main-320`, the HTTPS Main Mix. Our `stream.radioparadise.com/aac-320` is recorded as `http://` |
| NTS Radio 1 | `a3dbc189-d23e-4308-803f-5aad26432b8c` | the HTTPS record of the same stream |
| KEXP 90.3 FM Seattle | `445cbb3a-1c4e-49aa-a268-f5b6acfa8f2e` | |
| FIP — Radio France | `a349e1e9-2844-443a-973b-09a02fa12c8e` | no logo in radio-browser; view handles a missing image |
| Tomorrowland — One World Radio | `9e31c4e7-03b6-4a80-a4e2-5977b023d32c` | |
| Tomorrowland — Anthems | `5f3fa761-76be-4672-98fd-c5e71771834d` | `OWR_DAB.mp3`; the `_ADP` variant we had is not in radio-browser |
| Tomorrowland — Daybreak Sessions | `c77644fa-5d0d-47f6-93ef-850805efefad` | |
| Tomorrowland — bigFM One World Radio | `d23f9ea2-80bd-4b43-b25c-31903bbbcaec` | |

**Dropped: BBC Radio 1 and BBC Radio 6 Music.** radio-browser carries 13 and 3
records for them respectively, and every one is HLS over `http://`. There is no
gate-passing record to pin.

This costs nothing that worked. Both stations are `http://` in the current
catalogue too, so they are already blocked as mixed content in the browser — the
plugin has never been able to play either one. They can return the moment a
gate-passing record exists, which is a one-line change because the seed list is
just UUIDs.

### Keeping the seeds honest

`scripts/resolve-seeds.sh` re-runs the resolution: it checks each pinned UUID
still exists, still passes the gates, and reports what its stream URL is now. It
is a development and `workflow_dispatch` tool, deliberately **not** on the push
path — a third party's outage must not turn a green build red. `SeedTests`
asserts offline only what can be asserted offline: the UUIDs are well-formed and
unique.

### The user override stays compatible

`stations.json` in the plugin's data folder still replaces the fetched catalogue
entirely, and the bare-JSON-array shape the current README documents still
parses. It is also the escape hatch for anything radio-browser cannot supply —
BBC included, for anyone who has an HTTPS URL for it.

A station with no id gets a stable slug derived from its name; if two stations
resolve to the same id the first wins and the loader logs the one it dropped,
because a route that resolves to two stations is worse than a catalogue that is
visibly one short.

An override skips the network entirely and is **not** gate-filtered — a
hand-written list is the user's own call, and silently dropping their entries
would be worse than letting an `http://` URL fail visibly in the player. The
settings page badges an override as such.

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
    "hooks": ["ui", "scheduledTask"],
    "rest": false,
    "ws": false,
    "network": { "hosts": ["*.api.radio-browser.info"] },
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
- **`ui` is required or the plugin is invisible.** `PluginUiController.HasUi`
  refuses to serve a view for a plugin that has not declared it, so this is not
  decoration.
- **`scheduledTask`** carries the daily catalogue refresh. Consumed for real, by
  `PluginCronRegistrar`.
- **`mediaSource` is removed** because nothing consumes it. A manifest is what an
  owner reviews at consent time, and declaring a capability that does nothing is
  the false promise this standard exists to prevent. No user behaviour changes,
  because nothing ever called it.
- **`network.hosts` names radio-browser's mirrors.**
  `PluginNetworkAllowlistHandler` allows the union of the manifest's hosts and
  any later grant, so declaring the host here is sufficient and **no runtime
  grant prompt is needed**. The glob is label-scoped — `*` matches within one
  label — so `*.api.radio-browser.info` covers `all.`, `de1.`, `nl1.` and the
  rest, and nothing wider.
- **`rest` and `ws` stay false.** No controller, no hub handler, nothing behind
  them. They flip when there is.
- **`projectUrl` now points at this repository** instead of the media server. A
  catalogue entry uses it as the plugin's own page.
- **Name and description** drop "Provider" and the media-source claim, both of
  which described the hook being removed.

### `autoEnabled` no longer takes effect on first install

`PluginConsentService.IsBaseline` returns false when `Network is not null`, and
`PluginLoader` starts a non-baseline plugin `Disabled` regardless of
`autoEnabled` until the owner consents from the dashboard.

So this is the one real cost of fetching live: **the owner has to enable the
plugin once.** It is also the correct behaviour — a plugin that calls a
third-party API on a schedule should be something the owner agreed to, not
something that starts on its own. `autoEnabled` stays `true` so that consent is
the only step, and the value is honoured on every load after it.

The alternative — shipping the catalogue as embedded data — would keep the plugin
baseline and prompt-free, at the cost of a station list that goes stale between
releases and cannot be corrected without one. Live data is worth one click.

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
- `dotnet test -c Release` green, including the manifest, the gates, the network
  failure modes, the provider's cache behaviour, routing and every view.
- `scripts/resolve-seeds.sh` run once by hand: all ten UUIDs resolve and pass the
  gates.
- No station name, stream URL, logo or genre appears anywhere in the source tree
  — grep for `http` under `src/` returns only documentation and the API base.
- Committed locally. Pushed and tagged `v1.0.2` only on explicit ask, then
  `ci-watch`.
