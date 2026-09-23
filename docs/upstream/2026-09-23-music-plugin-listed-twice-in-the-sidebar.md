# A music plugin is listed twice in the sidebar

**Repo:** `nomercy-app-web`
**Found:** 2026-09-23, on the owner's own server (NoMercy MediaServer v0.1.526) with
Internet Radio 2.0.0 installed, and confirmed by reading `master` at
`c821ca0a5817ae4483629954638e9d0ae6cd3e09`.
**Raised:** not yet.

> **Status (2026-09-23):** still open, still triggered. Re-checked against
> `nomercy-app-web` `origin/master@00d7857ea2375a6de81f070d3a8a2faeaac26005` (no change to
> `Sidebar.vue` since the citation above) and this plugin's own `plugin.json`, which still
> declares one `section: "music"` mount and nothing else has changed that mount's shape.
> Tasks 1-6 gave this plugin a real route table and translated labels, neither of which
> touches which section a mount declares, so the duplicate is unaffected by this branch's
> work — see "Not fixed from here" below, which already said so.

## What happens

"Internet Radio" appears twice in the music sidebar: once among Start, Artists, Albums,
Genres and Songs you like, and once in a block below them. Both rows carry the same
label, the same icon and the same destination.

## Why

Two server endpoints publish the same entry, and `Sidebar.vue` renders both of them.

- `GET /setup/navigation` already carries plugin entries for the music section. The
  server builds that list itself — `LibraryNavigation.Build(...)` appends
  `musicPluginEntries` straight after the static `MusicPages`
  (`nomercy-media-server/src/NoMercy.Api/Services/LibraryNavigation.cs`), fed by
  `SetupController.PluginEntries(PluginKind.Music)`
  (`SetupController.cs:74-79`, `EntriesOf` at `:106-143`). In the client this is
  `libraryEntries` → `filteredNavItems`
  (`src/Layout/Desktop/components/Sidebar/Sidebar.vue:49-52,172-187,209-216`).
- `GET /plugins/ui` reports the same entry as `nav_entries`, which becomes
  `pluginsInSection('music')` (`src/store/plugins.ts:87-104`) and is rendered a second
  time by the `musicPlugins` block (`Sidebar.vue:31,419-427`).

The file states the rule it then breaks. `Sidebar.vue:28-30`:

> A plugin says which section it belongs in and the client decides where that is. The
> library section is not listed here: the server sends its entries, plugins included, so
> a plugin mounted there arrives already placed.

That is why there is no `libraryPlugins` or `videoPlugins` block — and why a library or
video mount is drawn once. Music is the one front-end section that has both.

## When it started

`e4955eab09a1ce09ad14c901c25284750adc8d99` (2026-09-16),
*"feat(plugins): render music plugin pages in the sidebar, scope browse to its surface"*.
Its message reads: *"musicPlugins was computed in Sidebar.vue but never rendered, so a
plugin mounted in the music section had nowhere to appear"* — `/setup/navigation` was
already delivering it. `633df675` (2026-09-18) later moved the block below the closing
`</div>`, which is why the duplicate now sits under the whole list rather than beside it.

Nothing on the server changed to cause this, and it is unrelated to the ABI 12 work: the
duplicate landed 34 days after this plugin's 1.2.0 release, and the media-server commits
in that window touch these files only for the SDK rename.

## Why the accompanying test does not catch it

`src/Layout/Desktop/components/Sidebar/Sidebar.spec.ts`, added with that commit, uses this
very plugin as its fixture, but mocks the client globally:

```js
vi.mock('@/lib/clients/useServerClient', () => ({
    default: () => ({ data: ref([]) }),
}));
```

so `/setup/navigation` is empty in every case. Each mechanism is covered alone; their
overlap cannot be.

## Who else is affected

- A settings-only mount (Automix) is safe: `/setup/navigation` never emits settings-kind
  entries.
- A library or video mount (Torrent Downloader) is safe: `Sidebar.vue` has no second block
  for those sections.
- Any plugin mounting in `music` is affected.

## Proposed fix (a proposal, not a patch)

Drop the `musicPlugins` block from `Sidebar.vue`, exactly as library and video already do,
and let `/setup/navigation` place the entry. A test that populates both sources and
asserts one row would keep it from coming back.

Worth considering on the server side as well, though it does not fix what the owner sees:
`PluginUiController.Browse` (`:103`) and `SetupController.EntriesOf` (`:111`) read
`IUiPlugin.NavEntries` with no fallback to `capabilities.ui.mounts`, while
`PluginUiDescriptorDto.Navigation` (`:83-89`) does fall back. Because of that asymmetry a
plugin cannot work around this by declaring no `NavEntries`: it would keep its sidebar
entry but lose route registration (the client then falls back to the legacy
`/plugins/{id}` host) and disappear from the Extensions page. Giving all three readers the
same fallback would make the manifest the single honest source.

## Not fixed from here

This is a client rendering bug. The plugin declares one music mount and one settings
mount, which is what it has always done, and there is no change on this side that fixes
it without breaking something else.
