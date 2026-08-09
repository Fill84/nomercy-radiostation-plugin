# A failed media load says nothing, anywhere

**Where:** `@nomercy-entertainment/nomercy-player-core` 2.0.4 · `dist/core/mixins/queue.js`,
`dist/core/mixins/loading.js` · reproduced 2026-08-09
**Effect:** when a track fails to load, the player emits no event, logs nothing and throws
nothing a consumer can catch. The UI is left showing the track as current with the
transport frozen at 00:00. Every media failure looks identical, and looks like nothing.

## What happens

`item(target, { autoplay: true })` is the sanctioned way to start a track — the app's
`playTrack()` uses it. Its load continuation is:

```js
void this.load(currentItem, { source, startAt })
    .then(() => { if (opts?.autoplay) void this.play({ source: opts.source }); })
    .catch(() => { });          // queue.js
```

The `.catch(() => {})` is the only handler on that promise. `load()` re-throws on failure
(`loading.js`), so the rejection lands there and stops.

`load()` does not emit `error` on its way out, despite its own doc comment:

> Emits `mediaReady` after a successful load. On failure the error propagates via the
> `error` event AND re-throws.

It re-throws. It does not emit. The catch block restores phase and play state and then
`throw err` — there is no `emit('error', …)` on that path. So the documented escape hatch
does not exist, and the undocumented one is swallowed one frame later.

## What a consumer sees

Reproduced against the published 2.0.4 bundle in Edge, driving the real `WebAudioBackend`
through `queue()` + `item(item, { autoplay: true })`, with the media URL answering 401:

```
playtrack-401:element event=error readyState=0 networkState=3 dur=NaN err=4
playtrack-401 t=1s  playState=idle phase=ready elCurrentTime=0.00 paused=true readyState=0
playtrack-401 t=12s playState=idle phase=ready elCurrentTime=0.00 paused=true readyState=0
```

The element's own `error` fired with code 4. The player emitted nothing: no `error`, no
`loadPrevented`, no state that distinguishes this from a track nobody pressed play on. The
`item` event had already fired, so the track is sitting in Now Playing looking loaded.
With the app's `logLevel: 'warn'` there is not one line in the console.

The same harness on the same URL answering normally plays: `playState=playing`,
`phase=playing`, position advancing 0.77 → 1.77 → 2.77s. So this is purely the failure
path.

## Why it matters

This is not a cosmetic gap. A silent failure here is indistinguishable from every other
cause, so diagnosis has to start by eliminating the whole client stack by hand — which is
how a single 4xx on one URL turns into days of bisecting the player, the backend, the item
shape and the stream itself. A one-line `emit('error', …)` would have named it on the
first click.

It also means no consumer can build UI for it. The app cannot show "this station is
unreachable" because the app is never told.

## Suggested fix

In `load()`'s catch block, emit before re-throwing — the same
`makePlayerErrorEvent(playerErr, 'error', { kind: 'core' })` shape `loadQueue()` already
uses a few lines below:

```js
catch (err) {
    // …existing phase / play-state restore…
    const playerErr = err instanceof PlayerError
        ? err
        : resourceError('core:resource/media-load-failed', String(err));
    this.emit('error', makePlayerErrorEvent(playerErr, 'error', { kind: 'core' }));
    throw err;
}
```

`loadQueue()` in the same file already does exactly this for playlist fetches, so the
pattern and the imports are both present — media loads are the odd one out.

The `.catch(() => {})` in `queue.js` can then stay: it is there to stop an unhandled
rejection, and with the event emitted it is no longer hiding the only copy of the news.

## Reproduction

`queue()` + `item(item, { autoplay: true })` against any URL that answers 4xx, with
`backend: 'webaudio'`. Listen for `error` on the player: nothing arrives. The element's
own `error` event is the only signal, and it is inside the backend.
