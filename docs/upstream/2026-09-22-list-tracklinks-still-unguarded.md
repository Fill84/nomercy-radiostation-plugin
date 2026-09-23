# The list TrackLinks.vue still renders an unguarded RouterLink

**Repo:** `nomercy-app-web`
**Found:** 2026-09-22, against `master` at `c821ca0a5817ae4483629954638e9d0ae6cd3e09`.
**Raised:** not yet, and distinct from #23 (closed, fixed at `2a24f7a10`). `gh issue list
-R NoMercy-Entertainment/nomercy-app-web --state all --search "TrackLinks" / "NMTrackRow" /
"RouterLink undefined"` returns #23 and #15, neither of which touches this file.

> **Status (2026-09-23):** still open, unaffected by Tasks 1-6, and still reachable through
> this plugin. Every station this plugin plays announces its now-playing track as an artist
> string with no artist entity (`Views/StationStream.cs`, `Catalog/NowPlayingListener.cs`) —
> the exact "plugin-announced artist with no link" shape this note and its trigger both
> describe. Nothing in this work touches how the client chooses to draw that announcement;
> whether it reaches `NMTrackRow`/`NMTopResultCard` is entirely the client's call.

## What happens

`src/views/Music/List/components/TrackLinks.vue` is a second, separate `TrackLinks`
component from the one #23 fixed (`src/components/MusicPlayer/components/TrackLinks.vue`).
It renders the identical unguarded pattern #23 removed from the player bar:

```vue
<!-- src/views/Music/List/components/TrackLinks.vue:74 -->
<RouterLink
    :onkeyup="onkeyup"
    :to="item.link"
    ...
>
```

`item.link` comes from the same `Artist | Album` entries the closed issue described as
built without a `link` for a plugin-announced artist (`actionInterpreter.ts:123`,
`pluginNowPlaying.ts`). #23's fix (`2a24f7a10`) only touched
`src/components/MusicPlayer/components/TrackLinks.vue`:

```
$ git log origin/master --oneline -- src/components/MusicPlayer/components/TrackLinks.vue
2a24f7a10 fix(music-player): draw an artist with no link as text (#23)
$ git log origin/master --oneline -- src/views/Music/List/components/TrackLinks.vue
f075d6f51 fix(plugins): let plugin media play (#16)
505f5b59f feat: add RainbowLogo component with SVG design
```

— the List variant was never touched by the fix, and still has no `v-if="noLink ||
!item.link"` guard (the pattern #23 added to the other file).

## Why this one is reachable, not just theoretical

This `TrackLinks.vue` is imported and rendered by `NMTrackRow.vue` and
`NMTopResultCard.vue`:

```
src/components/NMTopResultCard.vue:8:import TrackLinks from '@/views/Music/List/components/TrackLinks.vue';
src/components/NMTrackRow.vue:25:import TrackLinks from '@/views/Music/List/components/TrackLinks.vue';
```

and both `NMTopResultCard` and `NMTrackRow` are on the allow-list of app components a
plugin tree may draw directly:

```
src/components/plugin/pluginAppComponents.ts:30: 'NMTopResultCard',
src/components/plugin/pluginAppComponents.ts:31: 'NMTrackRow',
```

(`PluginNode.vue`'s `resolved` computed falls back to `getPluginAppComponent(...)` for
exactly these names — see the design-system doc comment in
`src/components/plugin/PluginNode.vue:39-58`.)

So a plugin tree that draws an `NMTrackRow` or `NMTopResultCard` for a track whose artist
or album entry has no `link` — the same shape #23 fixed for the player bar — hits the same
`router.resolve(undefined)` production-only TypeError described there, and the same
app-wide error toast.

## Read, not reproduced

I did not start a server to trigger the toast from this specific path; the evidence above
is a static trace from the plugin node down to the unguarded `RouterLink`, matching #23's
own diagnosis byte for byte.

## Reproduction (as written)

1. Have a plugin UI tree render an `NMTrackRow` or `NMTopResultCard` node whose track data
   includes an `artist_track`/`album_track` entry built without a `link` (the shape
   `actionInterpreter.ts`/`pluginNowPlaying.ts` already produce for a plugin-announced
   artist).
2. On the production build (`vue-router`'s prod bundle throws where the dev bundle warns —
   see `2026-08-10-plugin-artist-cannot-be-drawn.md` for why this only reproduces there),
   expect the same `TypeError: Cannot read properties of undefined (reading 'path')` and
   the app's global error toast.

## What it costs

Any plugin using the two app components the platform specifically opened up for plugin
trees (`NMTrackRow`, `NMTopResultCard`) to show a track list can trigger the exact toast
#23 was raised and fixed to prevent — just through a second copy of the same bug the fix
did not reach.

## Proposed fix (proposal, not decided)

Apply #23's own fix here: give `src/views/Music/List/components/TrackLinks.vue`'s
`RouterLink` the same `v-if="noLink || !item.link"` text-only fallback, or better, make the
guard live in one place both `TrackLinks.vue` files can share, so a third copy elsewhere in
the tree cannot reintroduce it a third time.
