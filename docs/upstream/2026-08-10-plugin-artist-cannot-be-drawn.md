# A plugin's artist cannot be drawn: the entry is built without a link, and then linked

**Repo:** `nomercy-app-web`
**Found:** 2026-08-10, against `master` at `5448732` and the build on `app.nomercy.tv`.

## What happens

Play any Internet Radio station and let it announce a track with an artist. The moment the
artist reaches the player, the viewer gets:

> Something went wrong — A component error occurred. Please refresh if the issue persists.

and the artist line under the title in the player bar stays empty. In the console:

```
[Vue error] TypeError: Cannot read properties of undefined (reading 'path')
    at Object.A [as resolve]   (vue-core-*.js)   <- router.resolve
    at aa.fn                    (vue-core-*.js)   <- a computed
```

`router.resolve(undefined)`. A `RouterLink` was given no `to`.

## Why

The client builds a plugin track's artist entry itself, from a string, in two places — and
neither puts a `link` on it:

- `src/lib/plugin/actionInterpreter.ts`
  `artist_track: text(payload, 'artist') ? [{ id: 'plugin:<id>:artist', name, cover: null }] : []`
- `src/lib/plugin/pluginNowPlaying.ts`
  `...(announced.artist ? { artist_track: [{ name: announced.artist }] } : {})`

`src/components/MusicPlayer/components/TrackLinks.vue` then renders every entry as a link:

```vue
<RouterLink v-else :to="item.link" …>
```

`item.link` is `undefined`, `router.resolve` throws while the computed evaluates, and the
error boundary turns it into the toast.

`src/Layout/Desktop/components/Overlays/QueueTrackItem.vue` already guards the same data:

```vue
:to="song?.artist_track?.[0]?.link ?? '#'"
```

which is why the Now Playing panel draws the artist fine while the bar does not — same
data, one guard, one without.

## Why no plugin can work around it

The plugin never supplies the artist object. It sends a string, and the client constructs
the entry. There is no payload key that would put a `link` on it. So **every** artist a
plugin announces throws, regardless of what the plugin does. The only choices open to a
plugin are "no artist" or "the toast".

## What would fix it

Either of these, in `nomercy-app-web`:

1. Guard `TrackLinks.vue` the way `QueueTrackItem.vue` already does — an entry with no
   `link` is text, not a link. This is the smaller change and fixes every consumer at once.
2. Give the constructed entries a `link` in `actionInterpreter.ts` and
   `pluginNowPlaying.ts` — the plugin's own page is the honest target, and both files
   already know the plugin id.

## What this plugin does meanwhile

`InternetRadioController.NowPlaying` answers with the whole announced line under `track`
and sends no `artist` at all, so `artist_track` stays empty and nothing is linked. The
listener reads `DAVID GUETTA, JENNIFER LOPEZ - Save Me Tonight` where the track goes. The
split is ready in `IcyMetadata.Split` and goes back the day the client stops linking an
entry that has nowhere to go.

## A wrong turn worth recording

An earlier round measured this by removing the artist from **both** the play intent and the
announcement at once, saw the toast disappear, and — after `app-dev` appeared to show an
artist without a toast — concluded the diagnosis above was refuted. It was not. The
"refutation" compared Vite's compiled dev output against raw source files and read the
difference (and, in one direction, the sameness) as evidence about the code. `app-dev`
runs the same unguarded `TrackLinks.vue`. One measurement, two variables, and a comparison
of two things that were never comparable.
