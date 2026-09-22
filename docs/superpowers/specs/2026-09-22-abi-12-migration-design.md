# Internet Radio 2.0 — onto plugin ABI 12.1

> Supersedes whatever `2026-07-30-internet-radio-ui-design.md` and
> `2026-08-08-discovery-search-favourites-design.md` say about the media relay, the
> caller's token, the station homepage and how the plugin is built. Everything else in
> them still holds unless contradicted here.

## Why this exists

v1.2.0 was built against `nomercy-media-server@4885d28c1`, plugin ABI 10.0. The server
release every owner is now offered, v0.1.526 (2026-09-22), is ABI 12.1, and its
`PluginAbi.Oldest` is 12.0. It refuses 1.2.0 at load and marks it Malfunctioned. **The
plugin is broken today for everyone who updates their server.**

ABI 12 is not a rename with a version bump. It also removed the host container this
plugin reached through, refuses a bearer token in a plugin URL, and changed the form in
which a view is told who is asking. Each of those breaks something the plugin does, and
one of them fails silently.

Verified against `nomercy-media-server` `dev@938ed8f86` and release tag `v0.1.526`, and
against `nomercy-app-web` `master@c821ca0a5`, all on 2026-09-22. A copy of the plugin
built against the packed ABI 12.1 SDK, with only the mechanical changes of step 2 below,
compiles and passes 357 of 360 tests; the three that do not are expected.

## What changed underneath the plugin

- **The contract moved.** `NoMercy.Plugins.Abstractions` is now
  `NoMercy.PluginSdk.Abstractions`, and `NoMercy.Plugins.Mvc` is `NoMercy.PluginSdk.Mvc`,
  both packed at `12.1.0`. `NoMercy.Events` and `NoMercy.Design` now ship *inside* the
  Abstractions package rather than beside it. The old project folders on `dev` hold only
  `bin/` and `obj/`, so `scripts/fetch-abstractions.*` fails before it packs anything.
- **Server `master` is not what owners run.** It stopped at v0.1.472 (ABI 10) on
  2026-08-10. Releases are cut on their own line of release commits; v0.1.526 is ABI 12.1
  and differs from `dev` in five SDK files. CI still builds against `master`.
- **`IPluginContext.Services` is gone** (`5c7a7e847`). `CallerBearerToken()` and
  `PublicBaseUrl()` in `InternetRadioPlugin.cs` read `IHttpContextAccessor` through it. There
  is no replacement, on purpose.
- **A token in a plugin URL is refused.** `PluginQueryTokenFilter` answers 401
  `TokenInUrl` to any `PluginControllerBase` request carrying `?token` or `?access_token`
  once `targetAbi` is 11 or higher (`PluginQueryTokenPolicy.RefusedFromAbiMajor`,
  `be25fddd6`). The player appends `access_token` to every stream URL itself, so the
  plugin's own `stream/{id}` and `cover/{id}` relay cannot work at 12.1 whatever the plugin
  does.
- **The host now mints playable URLs.** `IPluginContext.Media.Proxy.MintAsync` returns a
  relative `/api/v1/plugins/{id}/media/{ticket}`. The URL is bound to the calling user,
  valid for 15 minutes (checked when a stream *starts*, not while it plays), streamed
  without a timeout or buffering, and served by a controller the token filter does not
  cover. It can only be minted while a request is being served. It never asks the
  upstream for ICY metadata.
- **`media.proxy` needs two approvals from the owner.** The first is the hook in the
  manifest. That widens consent, so the plugin is disabled until the owner agrees again.
  The second is a runtime grant `capability.media.proxy`. Its scope is never read from
  `network.hosts` (`PluginCapabilityBroker.ScopesFor` returns hosts only for `network.*`).
- **A view is told who is asking in a different form.** `PluginViewRequest.UserId` used to
  be `User.UserId().ToString()`, the Guid form. It is now `Caller.Id.ToString()`, where
  `Caller.Id = new UserId(new Ulid(guid))`: the same sixteen bytes written as a 26-character
  Ulid. The controller still reads a Guid from `ClaimTypes.NameIdentifier`. So the
  favourite toggle writes under one key and every view reads under another. A favourite
  never appears, and every favourite and search saved by 1.x is invisible. Nothing errors.
- **`PluginControllerBase.Caller` is never populated.** The host never sets
  `HttpContext.Items[CallerItemKey]`, so reading it always throws `RouteAccessDenied`.
- **A plugin installed through the upload button is the owner's alone.**
  `PluginManager` marks every non-marketplace install as sideloaded, and
  `PluginAccessResolver` answers `None` for everyone but the owner on a sideloaded
  plugin. Members get no UI and a 403. A zip extracted by hand into `<server>/plugins/`
  carries no marker and stays shared.

What did **not** change, and stays as it is:

- **Scheduling.** `IScheduledTaskPlugin`, `PluginScheduledJob`, the `"scheduledTask"` hook
  and the daily refresh work unchanged.
- **Manifest shape.** Translations, `network.hosts: ["**", …]` and `ui.mounts` keep their
  object form.
- **Hooks.** The hooks `"ui"` and `"scheduledTask"` keep their names, which the runtime
  checks.
- **Components.** Every component tag the plugin sends is still rendered by the web client.
- **Play intents.** The shape of `PlayMedia` / `Enqueue` is unchanged.

## Decisions

Taken with the owner on 2026-09-22.

| Question | Decision |
| --- | --- |
| Release shape | One release, **2.0.0**, built in small verified steps on one branch. A major, because it refuses to run on an ABI 10 server and asks for consent again. |
| What to build against | The **newest `v*` release tag** of the server, resolved at build time; a release records the exact SHA as it does now. |
| Station logos | **`data:` URIs**, fetched and cached by the plugin, at most 16 KB each. |
| Now playing | Kept, through a **metadata connection of the plugin's own**, shared per station. |
| http stations | **Not yet.** https only, as now; admitting http is a separate step after this release proves the proxy. |
| Station homepage button | **Removed.** |
| media.proxy grant | Asked **once, for `*`**. |
| Distribution | **Extract by hand**, with an explicit warning not to use the upload button. |
| Stoney's `d8dc010` | Namespaces per folder **re-applied after** the migration; `RadioBrowserClient.All()` **not adopted**. |
| Other repositories' bugs | Verified first, then drafted as issues, and **raised only on the owner's approval**. |
| End-to-end test | On a **local server** on this machine, running the newest release. |

## Build and contract

### Building against a release (step 1)

`scripts/fetch-abstractions.sh` and `.ps1` resolve the newest `v*` tag of
`NoMercy-Entertainment/nomercy-media-server` with `git ls-remote --tags` and a version
sort. They pack two projects from it: `NoMercy.PluginSdk.Abstractions` and
`NoMercy.PluginSdk.Mvc`. `SERVER_REF` still pins any commit, so an older release stays
rebuildable with the command its notes hand out.

**The checkout moves.** The default server folder becomes `_server/` inside this
repository, which `.gitignore` already covers. Today the default is the sibling
`../nomercy-media-server`, on which the script runs `sparse-checkout` and
`git reset --hard`. That sibling is the owner's own working checkout of the server. The
sharing argument that justified the sibling is not worth that risk.

The sparse-checkout list must include the files the pack needs from the server root
(`Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `LICENSE`). Without
`.editorconfig`, the server's own analyzer fails its own code with NA0001.

CI drops `SERVER_BRANCH: master` and uses the same tag resolution for every build, not
only tag builds. The staging step's "must not ship" check becomes
`NoMercy.PluginSdk.*.dll`, `NoMercy.Events.dll` and `NoMercy.Design.dll`. All three are
still in the host's shared-assembly set.

### The contract itself (step 2)

- The csproj files reference `NoMercy.PluginSdk.Abstractions` and `NoMercy.PluginSdk.Mvc`
  at `12.*`, in both the plugin and the tests. The comments that explain `Version="*"` and
  the old names are rewritten to match.
- `using` lines move to the new namespaces in `src/` and `tests/`.
- `plugin.json`, the csproj `<Version>` and `PluginIdentity.Version` read `2.0.0`, and
  `targetAbi` reads `"12.1"`.
- `FakePluginContext` loses `EventBus` and `Services` and gains `Events`. Test view
  requests carry a `PluginCaller`. `ManifestTests` compares `PluginId.Value`, because the
  manifest's id is now typed.
- `Manifest_TargetAbiIsCompatibleWithTheShippedAbi` must pass against 12.1 unchanged.
- The hooks stay `"ui"` and `"scheduledTask"`. `nomercy-plugin verify` reports both as
  Blocked, but the runtime requires them (`PluginUiController`, `PluginCronRegistrar`),
  and the Automix plugin ships the same pair at 12.1. The mismatch belongs upstream (see
  below).
- `IScheduledTaskPlugin` stays. The NMP0016 descriptor says to move to `context.Scheduler`,
  but no analyzer emits it, and `context.Scheduler` throws `FacadeNotOnThisHost` on the
  in-process host.

## Whose favourites these are (step 3)

The key is decided in exactly one place, and it is the **Guid form**, which is what
every byte of 1.x user state is already stored under:

- **In a view:** `request.CallerId.Value.ToGuid().ToString()`.
- **In the controller:** the existing `Guid.TryParse` of `ClaimTypes.NameIdentifier`,
  unchanged. It must not read `PluginControllerBase.Caller`, which is never populated.

No stored data is rewritten. No Ulid-keyed state can exist: the plugin has never loaded
on an ABI 12 server.

A caller can no longer be absent from a view. The host refuses an anonymous caller with
403 before the plugin is called, so the `request.UserId is null` branches become a
`Guid.Empty` check.

Tests prove three things:
- A favourite toggled through the controller for one person appears in the view built
  for that person.
- State written by 1.x under a Guid key is read back.
- Converting a Guid to a Ulid and back round-trips to the same string.

## Playback through the host (step 4)

### Manifest and grant

Add `"media.proxy"` to `capabilities.hooks`. Keep `network.hosts: ["**", "*.api.radio-browser.info"]`:
the host's proxy client enforces the same allowlist, and the plugin's own requests
(catalogue, logos, metadata) still need it.

On `Initialize`, and again whenever minting is refused for lack of it, the plugin checks
`Grants.HasAsync(PluginGrantKind.ForCapability(PluginCapabilityNames.MediaProxy), PluginGrant.Everything)`.
When the grant is missing, it calls `RequestAsync` with the same kind and value and a
reason written for the owner. `RequestAsync` records the request and returns at once;
asking twice does not queue a second prompt.

### Minting without making views impure

Views stay pure `Build(...)` functions. Minting is asynchronous, needs the request, and
can be refused, so it happens before a view is built.

A small interface owned by the plugin, `IStationStreams`, takes the stations a view
will show and returns a stream URL per station id. It may also return nothing, together
with the reason. Its production implementation calls
`Context.Media.Proxy.MintAsync(new PluginProxyRequest { Links = [new() { Url = new(station.StreamUrl) }], Title = station.Name, Timeout = null })`.
The views receive the result as data.

Tests use a fake. This is also how the tests avoid `PluginMediaUrl`, which a plugin
assembly cannot construct.

- `StationCards.Play` and `Enqueue` put the minted URL in `streamUrl`. The `id`
  (station id) and `nowPlaying` payload keys stay exactly as they are: the web client
  derives the queue item's id and the now-playing poll path from them.
- An `artist` may be sent again. The web client's player bar no longer turns a linkless
  artist into an error toast (`nomercy-app-web@2a24f7a10`).
- Every view that carries a play or enqueue intent sets `RefreshInterval = 600`. A page
  left open for longer than the ticket's 15 minutes would otherwise offer a URL that
  answers 403, and the player would say nothing.

### When minting is refused

In that case there is no media proxy on the host, no grant yet, or a refusal:

- The view is still drawn: browsing, search and favourites keep working.
- Play and enqueue buttons are left out.
- One notice explains that the server owner still has to approve streaming for Internet
  Radio. English and Dutch strings sit in `lang/`.
- The plugin never falls back to the station's own URL. The browser's CSP blocks
  third-party media, which is why the relay existed in the first place.
- `PluginRefusedException` is caught at this one boundary and logged at most once per
  refusal code per hour, not once per card.

## Station logos as data URIs (step 5)

The web client cannot show a host-minted image. `CoverImage.vue` prefixes the server
base to a relative cover but adds no token, and the media controller is `[Authorize]`.
The plugin's own cover route is refused like the stream route. So the plugin fetches
logos itself and hands them over inline:

- Fetched through `Context.HttpClient`, which the manifest's `**` already allows.
- **When:** catalogue stations are fetched during the daily refresh job. A station seen
  for the first time in search results or favourites is fetched in the background after
  that view is served. Until a logo is cached, the card shows the default placeholder,
  exactly as a station without a logo does today.
- **Admitted only if** the response is at most **16 KB** and is an image by content, not
  only by header. Allowed types are PNG, JPEG, GIF, WebP, ICO and SVG. SVG is safe here
  because an `<img>` does not run its scripts. Anything else gets the placeholder, and
  that outcome is cached, so a rejected logo is not refetched on every view.
- Nothing is resized. That would need an image library in the plugin, and the size cap
  is what bounds the page instead.
- Cached in the plugin's data folder, keyed by station id, capped at 2,000 entries. The
  least recently used entries are evicted first.
- A logo on an `http://` address now works, because the browser only ever sees the
  `data:` URI. That is a side effect, not a change to which *stations* are admitted.
- The same `data:` URI is used on the card, on the station page and as the play intent's
  `cover`, so the player bar shows it too.

The end-to-end test measures the size of the largest page (all stations, with logos).
The 16 KB cap is revised if that page is unreasonable.

## Now playing (step 6)

The web client keeps polling `GET plugins/{id}/nowplaying/{stationId}`, as the play
intent's `nowPlaying` block tells it to. The route and its answer shape (`title`,
`artist`, `track`) stay. Only where the title comes from changes, because the host's
proxy never asks the station for metadata.

- A poll for a station renews a lease on one **shared** metadata connection for that
  station. The connection is opened through `Context.HttpClient` with `Icy-MetaData: 1`
  and read through the existing `IcyMetadataStream`. Audio bytes are discarded, and
  announced titles go into the existing `NowPlaying` store.
- The connection closes about 45 seconds after the last poll. The cost is one extra
  upstream connection per station being listened to, not one per listener.
- At most **8** metadata connections are open at once. A poll beyond that answers with a
  null title, the same answer as a station that announces nothing.
- A station that sends no `icy-metaint` is closed at once and remembered for an hour, so
  polling it does not reconnect every few seconds.
- The first answer after pressing play can be empty. The client already treats a null
  title as "nothing announced yet".
- A Shoutcast v1 `ICY 200 OK` status line still fails in .NET's HttpClient, as it did in
  the old relay. That is not a regression.

## What is removed or kept (step 7)

**Removed:**
- `Views/MediaProxy.cs`, `CallerBearerToken()`, `PublicBaseUrl()`, and the
  `Microsoft.AspNetCore.Http` import that served them.
- The controller routes `stream/{stationId}` and `cover/{stationId}`, with `RelayAsync`
  and `MetaInterval`.
- The station page's homepage button (`PluginActionIntent.OpenWebView`). The web client
  now replaces the whole plugin page with an "open external" card for any outside
  address, which is worse than no button. `StationGates.IsSafeExternalUrl` and its tests
  go too, if nothing else uses them.

**Kept:**
- The controller routes `favourites/toggle`, `search` and `nowplaying`. None of them
  carries a token in its URL.
- `StationGates`, still admitting https only.
- The search form as it works today, a `PluginForm` posting `CallPlugin`. `SubmitForm` is
  newer but not needed.
- The comma split in `RadioRoutes.Parse`. The web client no longer comma-joins routes,
  but the split costs nothing and protects against a client that still does. Its
  comment is rewritten to say so.

Comments that describe the old platform are corrected where they stand: the form
workaround in `RadioRoutes`, "seventeen genre GETs" wherever the budget is described,
and every mention of `Services` or the token.

## Documentation and release (step 8)

The root README, the plugin README and the release-notes template in CI say:

- **Minimum server:** v0.1.526 (plugin ABI 12.1). Older servers refuse 2.0.0, and newer
  ones refuse 1.x.
- **Install:** extract the zip into `<server>/plugins/` and restart. **Not through the
  upload button:** a plugin uploaded as a file is visible to the owner only, and members
  will not see the radio.
- **After updating:** enable the plugin again (it asks for a new permission), then
  approve its streaming request in the dashboard. Until both are done, stations browse
  but do not play.
- **Building:** the new `fetch-abstractions` behaviour, and that it no longer touches a
  sibling checkout.

Each `docs/upstream/` file is updated only after the upstream track below has confirmed
its status.

## Namespaces per folder (step 9)

This is the last code step, on top of the finished migration, as its own commit:

- `Catalog/` becomes `NoMercy.Plugin.InternetRadio.Catalog`.
- `Controllers/` becomes `.Controllers`.
- `State/` becomes `.State`.
- `Views/` becomes `.Views`.
- `InternetRadioPlugin` and `PluginIdentity` stay in the root namespace.

The commit message credits Stoney's `d8dc010` for the idea. Merging his commit instead
was measured: 21 conflicting files against the ABI rename, and it carries `All()` with
it.

`All()` is not adopted. Against our tests it:
- fails seven catalogue tests;
- swallows a genuine caller cancellation;
- discards every page already fetched when one page fails;
- cannot fit 50 sequential pages into the 20-second budget;
- orders by name instead of votes, which breaks deduplication and genre order.

A draft reply to Stoney explains this and proposes raising `GenreMap.PerGenreLimit`
instead. **The reply is shown to the owner and sent only on approval.**

## Verification

### Per step

Each step ends with `dotnet build -c Release` passing under `TreatWarningsAsErrors`, and
the full test suite green. New behaviour gets its test first:

- the user key, in the view and in the controller;
- minted URLs in `streamUrl`, with `id` and `nowPlaying` intact;
- the notice when minting is refused;
- `RefreshInterval` on every view with a play intent;
- the logo size cap and type sniffing, including the cached rejection;
- the metadata connection opening, being shared, closing after its lease, and the cap
  of 8.

The existing catalogue tests (cancellation, degraded fetch, stale cache) stay green
unchanged.

### End to end, before the release (step 10)

On a local server on this machine, running the newest release, with its own data folder,
one owner account and one member account. The plugin is installed by extracting the
release zip. Each item is evidenced, not asserted:

1. The plugin loads on ABI 12.1 and appears in both nav sections.
2. Consent and the `media.proxy` grant are requested and can be approved in the
   dashboard. This includes approving `*`, which the API accepts; the dashboard UI is
   unverified.
3. Browse, genre pages and search work.
4. A station plays through a minted URL: the request answers 200, audio keeps flowing,
   and the player reports playing.
5. Logos show on cards, on the station page and in the player bar.
6. Now playing updates while a station plays, and its connection closes after the
   player stops.
7. A favourite toggled on the station page shows on the favourites view at once, and
   after a restart.
8. **The member account sees and uses the radio.**
9. A page left open for more than 15 minutes still plays, because it has refreshed.
10. The size of the largest page with logos is measured and recorded.

The release is tagged only when all ten pass. A failure goes back to the step that owns
it.

## Upstream track

This runs beside the steps above and ships nothing to another repository on its own.
For each candidate:

1. Re-verify against the newest code of the repository it belongs to, and reproduce it
   at runtime where that is possible, on the step 10 server where it applies.
2. Search that repository's open **and closed** issues, so nothing is raised twice and
   nothing already fixed is raised at all.
3. Decide where it belongs: `nomercy-app-web`, the player packages, `nomercy-media-server`
   or `nomercy-plugins`.

Every candidate that survives becomes a full draft issue with five parts:
- what happens;
- the evidence, by file and line;
- how to reproduce it;
- what it costs a user or a plugin author;
- a proposed fix.

The drafts are put to the owner as one list, and **issues are raised only on approval**.
A candidate found to be already fixed is marked resolved in `docs/upstream/`, with the
fixing commit.

Candidates as of 2026-09-22:

| Where | Candidate |
| --- | --- |
| web client | A host-minted image cannot be shown: no token is added to a server-relative cover (`CoverImage.vue`, `PluginImage.vue`). |
| web client | `NMSearchInput` in a plugin tree fires on click and ignores Enter; `PluginSearchInput` is reachable from no payload. |
| web client | The list `TrackLinks.vue` still renders an unguarded `RouterLink`, reachable through `NMTrackRow` / `NMTopResultCard`. |
| web client | The tab title reads "Station - undefined - undefined" for a plugin item with no artist. |
| web client | A form submitted through the view sends neither `surface` nor `input`. |
| web client / server | `PluginComponentType.MediaCard` names a component the client deleted. |
| player packages | A failed load emits no error and the queue swallows the rejection, so a dead stream fails silently. |
| server | `PluginControllerBase.Caller` is never populated, so it always throws. |
| server | `PluginUiController.Render` hard-codes `PluginAccess.Owned` for every caller. |
| server | `nomercy-plugin verify` blocks the hooks `ui` and `scheduledTask`, which the runtime requires. |
| server | The SDK README says a plugin runs in its own process, which `dev` does not wire. |
| server | NMP0016 points plugins at `context.Scheduler`, which throws on the in-process host. |
| nomercy-plugins | #2: add that the entry must target ABI 12 and carry the Ulid id. |

## Out of scope

- **Admitting `http://` stations.** This is the next step once playback through the
  proxy is proven.
- **Out-of-process isolation.** It is not wired on the server (`IPluginRemoteLoader` is
  never registered). If it were, the host would not yet route UI, REST or cron to a
  remote plugin. The plugin keeps its in-process controller pattern and adds no new
  coupling to the host process.
- **A repository index of the plugin's own for dashboard updates.**
- **Moving search to `SubmitForm`, adopting `context.Scheduler`, and hub push for now
  playing.**
- **Any change to another repository.** See the upstream track.
