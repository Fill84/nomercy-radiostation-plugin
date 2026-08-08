# Internet Radio — pure discovery, search, and favourites

> Supersedes the seed-station sections of `2026-07-30-internet-radio-ui-design.md`.
> Everything that spec says about screens, gates, caching and version integrity still
> holds unless contradicted here.

## Why this exists

The catalogue is built from two sources: ten station UUIDs pinned in the source tree,
and a per-genre discovery sweep. The pinned ten are the owner's call to remove — a
curated list is somebody's taste compiled into a plugin, and four of the ten are the
same brand.

Removing them leaves two gaps that have to be filled at the same time, or the plugin
gets worse rather than cleaner. Discovery returns what is most voted for seventeen
tags; if a station is not in that sweep, nothing can reach it. So there must be search.
And once a station can be found but not kept, it is lost again on the next refresh. So
there must be favourites.

Covers are the third ask and the smallest: they already work. The gap is consistency,
not capability.

## What the platform can and cannot do today

Verified against `nomercy-media-server@b3ca391` (2026-08-07), and against the torrent
plugin, which is the worked example running in production.

- **A plugin can receive input.** `PluginActionType.CallPlugin` reaches the plugin over
  `PluginActionTransport.Rest` or `.Hub`. The July spec recorded both as broken; that is
  stale. The torrent plugin declares `"rest": true`, inherits `PluginControllerBase`, and
  its settings page adds and removes indexers through it.
- **A form's values arrive as a JSON body.** `PluginViews.Form(id, label, CallPlugin(method), fields)`
  posts the field values; the controller reads them with `[FromBody]`.
- **A button's action carries nothing but its path.** A `PluginForm`'s submit does not
  forward anything else the intent held, and a plain button has no body at all. Any state
  a button-triggered call needs must be in the method path.
- **A controller cannot redirect.** `PluginControllerBase` offers a `{ data }` envelope and
  a `{ status, data, message, args }` envelope. There is no "navigate there next". The
  client refreshes the view, so anything that must survive a submit lives server-side.
- **A view request carries no locale.** `PluginViewRequest` has `Route`, `Query`, `UserId`
  and `Surface`. Translation is the client's job, which is why translations are out of
  scope here — see below.
- **Images are fetched by the browser, not the plugin.** `PluginComponentType.Image` is
  `NMImage` and takes a URL. The manifest's network allowlist bounds
  `IPluginContext.HttpClient`; it has no bearing on what the client loads. Station logos
  on arbitrary third-party domains are therefore already legal and already working.

## What replaces the seeds

`SeedStations.cs`, `SeedTests.cs` and `scripts/resolve-seeds.py` are deleted.
`SeedStations.PerGenreLimit` moves to `GenreMap`, which is where the sweep's shape
already lives.

`RadioBrowserClient.ByUuidAsync` stays. It is no longer how the catalogue starts; it is
how a favourite is resolved when the station is not in the catalogue. See Favourites.

The catalogue becomes the sweep alone: seventeen genre sections, five stations each, an
upper bound of eighty-five before dedupe. The admission gates, the cache, the daily
refresh task and the user's `stations.json` override are unchanged.

The CI gate asserting no station data in the source tree gets strictly easier to satisfy:
after this, nothing but genre tags remains.

## Screens

### `/` — Browse

Top to bottom: the search field, then favourites, then genre chips, then the discovered
grid under a "Popular" heading.

"Popular" is now the best-voted of the sweep rather than a list of ours. Favourites render
with the same card as every other grid, and the section is absent — not empty — when the
user has none.

### `/search` — Results

Its own route, so the browser's back button returns the user to where they were instead of
discarding the query. Renders the stored query in the field and the live results beneath
it. An empty result set says so; a failed query says that instead, and the two must not
look alike.

### Unchanged

`/genre/{slug}`, `/all`, `/station/{id}` and `/settings` keep their shape. Each grows a
favourite toggle on the station card, and `/settings` grows a line reporting how many
favourites the current user has.

## Search

`RadioBrowserClient.SearchByNameAsync(term, limit, ct)` issues
`GET /json/stations/search?name={term}&limit={limit}&order=votes&reverse=true&hidebroken=true`
and puts every result through the same `StationGates.Admits` as discovery. A station that
cannot play is not a search result worth showing.

`limit` is **50**, requested from the API rather than trimmed after. The gates reject a
share of any response, so this is an upper bound on what is drawn and not a promise of
fifty rows. Fifty is a page worth scrolling; a larger number mostly buys results nobody
reaches past, at the cost of a bigger response on every keystroke-free submit.

A blank or whitespace-only term is not a query. It renders the empty-search state and
issues no request.

The flow, and why it is shaped this way:

1. The field submits to `CallPlugin("search")` with the term in the body.
2. The controller stores the term for the calling user and returns an outcome envelope.
3. The client refreshes. The plugin sees route `/search`, reads the stored term, runs the
   query live, and hands the results to `SearchView.Build(...)`.

**Only the term is stored, never the results.** Storing results would mean a second thing
that can go stale and a cache to invalidate, for the sake of one API call that
radio-browser exists to serve. The term is small, and re-running it means what is on screen
is always what the database says now.

The network call happens in `InternetRadioPlugin`, not in the view. Views stay pure
`Build(...)` methods — catalogue in, `PluginView` out, no I/O — which is what makes them
exhaustively testable and is the existing convention.

## Favourites

Per user, holding the **full station record**.

Storage is `DataFolderPath/user-state.json`, shaped
`{ "<userId>": { "favourites": [ RadioStation, … ], "lastSearch": "…" } }`.

One file, not two. The search term needs exactly the same thing favourites do — something
per-user that survives a form submit — and splitting them would mean two locks, two atomic
writes and two chances to get the same problem wrong differently.

The data folder rather than `IPluginConfiguration` because that is where `catalog-cache.json`
already lives, and because configuration is for a plugin's settings, not for user data that
grows without bound.

Writes take a lock and go through a temp file renamed into place. Two users can click at the
same moment, and a half-written favourites file loses every user's list, not one's.

**Why the whole record and not an id.** A station found by search is, by definition, usually
not in the sweep. Store only its id and the entry cannot be rendered tomorrow: no name, no
stream, no logo. The record is small and self-contained, and a favourite that survives its
source is the entire point.

**How a toggle resolves a station.** `POST favourites/toggle/{stationId}` is a button, so the
id in the path is all that arrives. The plugin resolves it in order:

1. the loaded catalogue,
2. the user's `stations.json` override,
3. `ByUuidAsync` against radio-browser.

Step 3 is why that call survives the seed removal. Without it, favouriting from search
stores an id pointing at nothing. Override-supplied stations have slug ids rather than
UUIDs, which is why step 2 comes first — a slug would never resolve at step 3.

A toggle on a station that resolves nowhere returns the `{ status, message }` envelope with
a failure status, which the client surfaces. It does not write a hollow entry, and it does
not return success on a favourite that was never stored — a toggle that silently does
nothing reads to the user as a broken button, and to the next reader of the file as data
loss.

Removing a favourite needs no resolution: the id is enough to drop the entry, so an unknown
id on removal is a no-op reported as success. Only adding needs the record.

## Covers

Already live end to end: `station.LogoUrl` comes from radio-browser's `favicon`, is passed
to `NMImage`, and the browser loads it. Nothing about fetching needs building.

The work is the gaps:

- Search results and favourites must use the same station card as the grids, so a cover
  appears everywhere a station does rather than only where the card was first written.
- A station with no `favicon`, or one that 404s, 403s or returns HTML, needs a deliberate
  placeholder. Six logos had already rotted this way, which is what
  `fix(ui): tiles hold their shape when a logo does not load` was for. That fix is the
  floor; the placeholder is the finish.

The plugin never fetches an image itself. It could not — the allowlist forbids it — and it
should not, because that would put every station's CDN behind the server's network identity.

## Manifest

- `"rest": true`, and `PluginHookCapability` unchanged otherwise.
- `NoMercy.Plugins.Mvc` joins the packed contract in `scripts/fetch-abstractions.*`, since
  `PluginControllerBase` lives there. The sibling checkout already materialises it for the
  torrent plugin, and the script now uses `sparse-checkout add`, so this costs nothing.
- `NoMercy.Plugins.Mvc.dll` must **not** ship in the zip. It is host-owned, exactly like
  `NoMercy.Plugins.Abstractions.dll`. The CI assertion that catches a host-owned assembly in
  the build output grows one name.
- The declared route set gains `/search`. No new UI mount: favourites live on `/`.

## Version

**1.1.0.** Features, not a patch. The three declarations — `plugin.json`, the csproj
`<Version>`, and `PluginIdentity.Version` — move together, and the CI tag gate already
enforces that they agree.

## Risk: consent on an existing install

Adding `rest` widens the capability set of a plugin that is already installed and already
approved on at least one server. Trust follows the repository the plugin came from
(`586be1c`), and a non-baseline plugin loads disabled until the owner approves it — but
whether *widening* an existing grant re-prompts, silently inherits, or fails closed is not
established.

**This must be answered before the release is tagged, not after.** A plugin that updates
into a state its consent record does not cover is a plugin that stops working on a server
where it used to work, and that is the failure mode this project exists to avoid.

## Out of scope

**Translations.** The catalogue is wired end to end — the manifest declares it, both
projects copy the files, CI stages them, and `TranslationTests` holds the chain together
with the host's own validator. But no view uses a key; every visible string is a literal.

Routing them through keys is deliberately deferred. `PluginViewRequest` carries no locale,
so the client resolves keys against the plugin's namespace — which means a view emitting
`stations.title` shows raw keys to users if that reading is wrong. The renderer is in
`app-web`, which is not in this workspace and has not been read. Shipping unverified keys
trades working English for a visible regression. It gets its own spec once the renderer's
behaviour can be observed.

**Sorting and filtering the grids.** Not asked for.

**Search history beyond the last term.** One stored term per user is what the refresh
mechanism needs; a history is a feature nobody requested.

## Definition of done

- No station identifier of any kind in the source tree — the CI grep passes with the genre
  tags as the only remaining data.
- Search finds a station that the sweep does not return, and it plays.
- A favourited search result still renders after the catalogue refreshes and its source
  sweep no longer contains it.
- Two users have different favourites, and neither sees the other's.
- Every station shown anywhere carries a cover or a deliberate placeholder.
- Build clean at `TreatWarningsAsErrors`, full suite green.
- The consent question above is answered in writing before a tag is pushed.
