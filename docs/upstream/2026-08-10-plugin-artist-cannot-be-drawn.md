# A plugin's artist cannot be drawn: the entry is built without a link, and then linked

**Repo:** `nomercy-app-web`
**Found:** 2026-08-10, against `master` at `7a58104` and the build on `app.nomercy.tv`.
**Fixed:** branch `fix/plugin-artist-without-link`, commit `21229a7`. Not deployed.

## What happens

Play any Internet Radio station. The viewer gets

> Something went wrong — A component error occurred. Please refresh if the issue persists.

shortly after pressing play, and again on every track the station announces. The artist
line under the title in the player bar stays empty. In the console:

```
[Vue error] TypeError: Cannot read properties of undefined (reading 'path')
    at Object.A [as resolve]   <- router.resolve
```

`router.resolve(undefined)`. A `RouterLink` was given no `to`.

The two symptoms — "on play" and "on every artist change" — are one defect. The poll that
asks what is on air runs immediately on the player's `item` event rather than after a
delay (`audioPlayer.ts`, `void tick().finally(schedule)`), and the plugin declares a 2s
first interval. So the first announcement carrying an artist lands at or within a couple of
seconds of pressing play, and every announcement after it repeats the throw.

## Why

The client builds a plugin track's artist entry itself, from a string, in two places — and
neither puts a `link` on it:

- `src/lib/plugin/actionInterpreter.ts`
  `artist_track: text(payload, 'artist') ? [{ id: 'plugin:<id>:artist', name, cover: null }] : []`
- `src/lib/plugin/pluginNowPlaying.ts`
  `...(announced.artist ? { artist_track: [{ name: announced.artist }] } : {})`

`src/components/MusicPlayer/components/TrackLinks.vue` then rendered every entry as a link:

```vue
<RouterLink v-else :to="item.link" …>
```

`item.link` is `undefined`, `router.resolve` throws while the link's computed evaluates,
and `app.config.errorHandler` in `src/setupApp.ts` turns it into the toast.

`src/Layout/Desktop/components/Overlays/QueueTrackItem.vue` already guards the same data:

```vue
:to="song?.artist_track?.[0]?.link ?? '#'"
```

which is why the Now Playing panel drew the artist fine while the bar did not — same data,
one guard, one without.

## Why no plugin can work around it

The plugin never supplies the artist object. It sends a string, and the client constructs
the entry. There is no payload key that would put a `link` on it. So **every** artist a
plugin announces threw, regardless of what the plugin did. The only choices open to a
plugin were "no artist" or "the toast".

## Why it only ever appeared in production

This is the part that cost days, and it is worth stating plainly, because it makes a local
reproduction impossible and every dev-mode observation misleading.

**Two independent halves of the symptom are production-only.**

1. `vue-router` (5.1.0) recovers from an invalid location in the development build and
   throws in the production one. From `dist/vue-router.esm-browser.js`:

   ```js
   if (!isRouteLocation(rawLocation)) {
     warn(`router.resolve() was passed an invalid location. This will fail in production.…`);
     return resolve({});
   }
   let matcherLocation;
   if (rawLocation.path != null) { … }
   ```

   In `dist/vue-router.esm-browser.prod.js` the guard is stripped and the same code reads
   `if(e.path!=null)` with `e === undefined` — the `reading 'path'` TypeError.

2. `setupApp.ts` fires the toast only outside dev; in dev it logs to the console.

So the same code, run locally, warns quietly and draws the artist. Anyone comparing a dev
build against the deployed app is comparing two different programs.

## The fix

`TrackLinks.vue` now renders an entry with no `link` as text rather than as a link — the
component already had that branch for its `noLink` prop, so the change is the condition:

```vue
<span v-if="noLink || !item.link" …>
```

One place instead of two construction sites, and it holds for any consumer. Covered by
`src/components/MusicPlayer/components/TrackLinks.spec.ts`, which mounts an entry that
carries no link and asserts no `<a>` is drawn. That spec needs a DOM and the Vue plugin, so
it joins the `vitest.nm.config.ts` project.

Verified: `vue-tsc --noEmit` clean, 268 tests in the nm project pass, 472 in the node
project. `src/store/audioPlayerVisualizer.test.ts` fails on this branch and on a clean
checkout of `master` alike — pre-existing, unrelated.

## What this means for the plugin

Nothing to change. `InternetRadioController.NowPlaying` answers with `title`, `artist` and
`track`, `StreamTitle.Parse` supplies the split, and once this branch is deployed the
artist draws as text under the title with no toast. Until it is deployed, a plugin sending
an `artist` still raises the toast on the built client — the only workaround available to
the plugin is to send no `artist` and let the whole announced line ride in `track`.

## A wrong turn worth recording

An earlier round measured this by removing the artist from **both** the play intent and the
announcement at once, saw the toast disappear, and — after `app-dev` appeared to show an
artist without a toast — concluded the diagnosis was refuted. It was not. `app-dev` runs
the development build, where neither half of the symptom exists (above). One measurement,
two variables, and a comparison of two things that were never comparable.
