# Internet Radio 2.0 — following the SDK the platform actually ships

> Extends `2026-09-22-abi-12-migration-design.md` and corrects it where the two
> disagree. The version does not move: 2.0.0 has never been released, so this work
> belongs inside it rather than after it.

## Why this exists

The ABI 12 migration made the plugin load and play again. Testing it on a real server
then produced a list of thirteen defects said to live in other repositories. Thirteen is
not a believable number, and the owner said so. Three audits against the shipped SDK,
with Automix — the platform author's own ABI 12.1 plugin — as the reference, found that
most of the list was a consequence of this plugin not using the SDK it was written
against.

This spec is the repair. It is not new behaviour: browsing, playing, favourites, logos
and now playing all keep working exactly as they do today on beast-unit.

Verified against `nomercy-media-server` `dev@938ed8f86` (release `v0.1.526`),
`nomercy-app-web` `master@c821ca0a5`, `nomercy-automix-plugin` and
`nomercy-torrent-plugin`, on 2026-09-23.

## What we got wrong

- **We grant ourselves the whole internet at install time.** `plugin.json` declares
  `network.hosts: ["**"]`. `IPluginGrants`'s own doc names "wildcard everything" as one
  of the two bad answers the facade exists to replace. The torrent plugin names
  seventeen literal hosts and asks for the rest at runtime.
- **We told owners to install in a way that bypasses a security control.** "Extract the
  zip into `<server>/plugins/`" works *because* no `.sideloaded` marker is written
  (`PluginManager.cs:473-478`), which also skips the developer-mode gate
  (`PluginSideloadPolicy.Check`) and the catalogue's checksum. The release notes, both
  READMEs and the previous spec all carry that advice.
- **We ask for a capability only after being refused.** `HostStationStreams` requests the
  `media.proxy` grant on the first `CapabilityScopeRefused`. The torrent plugin's
  `HostGrants` checks `HasAsync` and asks up front, per host, before anything is refused.
  Asking late is what walks an owner into the host's misleading refusal text.
- **Our entire view layer is hand-rolled.** `Ui.cs` builds twelve `Plugin*` components by
  hand. `PluginViews` is the sanctioned builder and Automix uses nothing else
  (`StatusView.cs:46,50,124,143,181,198`). The comments in `Ui.cs:8-34` that justify the
  hand-rolled path describe a client that no longer exists: `PluginNode.vue:26-35`
  resolves all fifty-six NM components and names this plugin as the reason both
  vocabularies are still kept alive.
- **We declare no routes.** `IUiPlugin.Routes` returns the default empty table, so the
  host registers no named pages, every page falls back to the `standard` layout, and no
  page has a label. Automix declares one route with `Name`, `Path`, `Label`, `Access`,
  `Layout` and `Surfaces`.
- **We ship translations that cannot work.** `lang/en.json` and `lang/nl.json` carry four
  keys, none of them used. `NMText` resolves its text through the plugin's bundle
  (`NMRender.vue:100-106`); our `PluginText` prints it raw (`PluginText.vue:26`). The
  manifest promises translations the plugin never draws.
- **We set no `Access`, `kind` or `slot`.** Our settings page is offered to members who
  cannot use it. Automix marks its settings route, its nav entry and its manifest mount
  owner-only.
- **Dead weight.** `StationCards.TileWidth` has no reader, `StationCards.Grid` takes a
  `UserState` it never uses, and the card's `description` prop is not read by
  `NMMusicCard`.

Two claims from the earlier list are withdrawn outright: the search box ignoring Enter
(we never emit that component; `PluginForm` renders a real `<form>` that submits on
Enter) and the "undefined" tab title (the most likely cause is that we declare no
routes). A third, "out-of-process is not wired", was wrong when it was written:
`IPluginRemoteLoader` is registered.

## What was right

These survive all three audits and stay as they are, with the evidence recorded so the
next reader does not "fix" them:

- **Logos as `data:` URIs.** A host-minted image URL is served by `PluginMediaController`,
  which is `[Authorize]`, and an `<img>` cannot send a bearer header. All three client
  image components fail on it; `CoverImage.vue:95-98` handles `data:` explicitly.
- **Now playing as a poll.** The client's own contract is the `nowPlaying` block on the
  play intent. A hub push only reloads the *view*
  (`PluginScreen.vue:292: onPluginMessage(id, () => void load())`); it never reaches the
  player bar. Pushing would take the feature away.
- **Minting the stream when the station page is built**, with `RefreshInterval = 600`.
  `PluginMediaCard` says a card should carry an id rather than a URL — but
  `PluginMediaCard` and `PluginMediaRef` are referenced nowhere in the server outside
  their own declarations and nowhere in the client, and the client never calls
  `/plugins/{id}/live`. The sanctioned mechanism exists on paper only.
- **Reaching the live plugin through `IPluginManager.GetPluginInstance`** in the
  controller, and reading the caller from `ClaimTypes.NameIdentifier`. Both reference
  plugins do the same, and nothing populates `PluginControllerBase.Caller`.
- **`IScheduledTaskPlugin`.** Automix and the torrent plugin both use it; the in-process
  `PluginContext` implements no `Scheduler` facade.

## Decisions

Taken with the owner on 2026-09-23.

| Question | Decision |
| --- | --- |
| Version | **Stays 2.0.0.** It has never been released; this work goes inside it. |
| Station hosts | Manifest names **radio-browser only**. One runtime `network.host` grant for `*`, asked once, with a reason that says logos and track titles come from the stations themselves. |
| Capability grants | Asked **up front** from the refresh job, not after a refusal. |
| Install | An **own repository index**, published by CI; the owner adds the repository once and installs from the dashboard. The hand-extraction advice is removed and corrected. |
| View layer | **Fully onto `PluginViews`**, with a route table and working translations. |
| Live channels, hub push | **Out of scope.** The client renders neither. Recorded as follow-ups. |
| Order | **Everything before 2.0.0 is released.** |

## Trust: what we ask for, and when

`plugin.json` declares `network.hosts: ["*.api.radio-browser.info"]`. `PluginScopeGlob`
compiles `*` to `[^.]+`, so that pattern covers `all.`, `de1.` and every other mirror.
`**` is gone.

Everything else is asked for at runtime, in one place — a `PluginGrants` helper of our
own, modelled on the torrent plugin's `HostGrants` — called from the refresh job, which
runs without a caller and is where a request belongs:

- `PluginGrantKind.ForCapability(PluginCapabilityNames.MediaProxy)` with value `*`. Asked
  before the first station page is built rather than after a mint is refused. The value
  is `*` because `ConfigPluginGrantStore.Holds` compares exactly or against `*` and does
  not glob, so a per-host grant would have to be given station by station, forever.
- `PluginGrantKind.NetworkHost` with value `*`, for the plugin's own calls to stations:
  logos and ICY metadata. The reason names both, in the owner's language, and says the
  addresses cannot be known at package time.

Both are asked only when `HasAsync` says they are missing, and at most once per process.
A request is recorded, never awaited; the dashboard shows it under Plugins, and the
plugin keeps working without it — logos fall back to the card's placeholder, and now
playing reports nothing, exactly as they do for a station that offers neither.

## Install: the repository, not the folder

CI already writes a `repository.json` beside each release with the id, name, description,
author, project URL, version, `targetAbi`, download URL and checksum. `PluginRepositoryController`
downloads that `downloadUrl`, verifies the checksum and installs with
`fromMarketplace: true` (`:222-250`) — so no `.sideloaded` marker is written and members
see the plugin.

The work is to make that file match `PluginRepositoryManifest` exactly (a top-level
`name`, an optional `url`, and `plugins[]`), to point its `url` at the raw file on the
release, and to say so in the documentation:

> Add `https://forgejo.phillippepelzer.me/FiLL/nomercy-radiostation-plugin/releases/download/<tag>/repository.json`
> as a plugin repository, then install Internet Radio from the dashboard.

The README, the plugin README and the release notes lose the "extract into
`<server>/plugins/`" instruction. Each gains one sentence saying that installing by hand
skips the checks the server performs and leaves the plugin visible to the owner alone.
`2026-09-22-abi-12-migration-design.md` gets a dated correction at the point where it
gave that advice: the advice was wrong, this is what replaces it, and the reason it
looked right at the time was that it solved member visibility by accident.

## The view layer

**Components.** Everything in `Ui.cs` that hand-builds a `Plugin*` tag is deleted and
replaced by the matching `PluginViews` call: `Container`, `Text`, `Row`, `Grid`, `Card`,
`Detail`, `Button`, `Form`, `EmptyState`, `Table`, `Row` (table row), `Badge`. `Ui.cs`
keeps exactly two members — `MediaGrid` and `MediaCard` — because `NMGrid` and
`NMMusicCard` are app components on the client's allow-list
(`pluginAppComponents.ts:20-32`) and `PluginViews` has no equivalent. The file's comment
is rewritten to say that, and nothing more.

**Routes.** `IUiPlugin.Routes` returns a `PluginRouteTable` with seven routes:

| Name | Path | Label key | Layout | Access |
| --- | --- | --- | --- | --- |
| browse | `/` | `stations.title` | `grid` | shared |
| all | `/all` | `stations.all` | `grid` | shared |
| search | `/search` | `search.title` | `grid` | shared |
| search-term | `/search/:term` | `search.title` | `grid` | shared |
| genre | `/genre/:slug` | `genre.title` | `grid` | shared |
| station | `/station/:id` | `station.title` | `standard` | shared |
| settings | `/settings` | `settings.title` | `form` | **owner** |

`Surfaces` stays empty, which means every surface: the pages are plain lists and a
station page, and there is nothing in them a television cannot draw.
`RadioRoutes.Parse` stays as the plugin's own parser — the route table tells the host
what exists; it does not dispatch — but its paths and the table are asserted equal by a
test, so the two cannot drift.

**Navigation and mounts.** The music nav entry keeps its defaults (nav slot, shared,
every surface). The settings nav entry and the settings manifest mount both gain
`access: owner`; both mounts gain an explicit `kind` (`music`, `settings`) and the
settings mount `slot: section`, which is what `PluginSlot.For("settings")` allows. Labels
in the manifest become translation keys, as `nomercy-plugin verify` asks.

**Translations.** With `PluginViews`, `NMText` resolves text through the plugin's own
bundle, so every literal in a view becomes a key. `lang/en.json` and `lang/nl.json` carry
the full set. A test asserts that every key a view emits exists in both files, and that
neither file carries a key no view uses — the failure this replaces is a plugin that
ships a language file which silently does nothing.

**Dead weight.** `TileWidth`, the unused `UserState` parameter on `StationCards.Grid` and
the `description` prop `NMMusicCard` never reads are removed.

## What does not change

Playback, logos, now playing, favourites, the catalogue and the schedule keep their
current behaviour. The user-visible difference is that pages gain proper labels and
layouts, the settings page stops being offered to members, and the plugin is installed a
different way.

## Verification

Every existing test stays green. The suite gains:

- the route table against `RadioRoutes` (every declared path parses; every parsed kind is
  declared);
- `access: owner` on the settings route, the settings nav entry and the settings mount;
- the manifest declaring no `**` host;
- both grants asked once, up front, with `HasAsync` checked first, and the views still
  drawing when they are refused;
- every view emitting only `NM*` component names, plus `NMGrid`/`NMMusicCard`;
- translation keys and the two language files agreeing in both directions;
- `repository.json` deserialising into `PluginRepositoryManifest` with a checksum in the
  form the server verifies.

Then the same run on beast-unit as before: install through the repository (which also
proves the member case), browse, search, play, logos, now playing, favourites. The
release is tagged only when that passes.

## Follow-ups, not now

- **Live channels.** Publish stations as `PluginLiveChannel { Radio = true }` through
  `context.Media.Live` once the client renders them. That retires minting at render time
  and the fifteen-minute limit on a queued station.
- **Hub push for now playing**, once the client feeds a push into the player rather than
  only reloading the view.
- **Host-minted images**, once an `<img>` can carry the ticket.
- **`context.Storage.ForUser`** for favourites, once the in-process host passes a user id
  (today it throws `UserScopeRequired` unconditionally).

## Upstream notes to rewrite

The notes written on 2026-09-22 are corrected in the same pass: the two withdrawn claims
are deleted, the ones that survived are tightened with what the audits found (Automix's
own manifest is Blocked by `nomercy-plugin verify`; `PluginManifestSchema` contradicts the
runtime; `ConfigPluginGrantStore.Holds` does not glob while `PluginGrantKind.NetworkHost`
documents that it does; `Storage.ForUser` is unreachable in-process), and each one says
plainly whether this plugin still meets it after this work. Nothing is raised anywhere
without the owner's approval.
