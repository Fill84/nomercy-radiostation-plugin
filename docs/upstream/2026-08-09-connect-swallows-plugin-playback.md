# NoMercy Connect swallows a plugin's playback

**Where:** `nomercy-app-web` · `src/lib/MusicPlayer/plugins/musicConnectPlugin.ts`,
`src/lib/clients/socketClient/musicSocket.ts` · found 2026-08-09
**Effect:** with Connect enabled, no plugin can start audio. The station loads, buffers
fully, and then sits at 00:00 in silence. Nothing anywhere says why.

## What the browser reports

Station clicked, Now Playing filled, transport frozen. The audio element itself:

```json
{ "ready": 4, "net": 1, "err": null, "paused": true, "t": 0,
  "vol": 1, "muted": false, "buffered": 1 }
```

`readyState 4` is HAVE_ENOUGH_DATA. The media is fetched, decoded, buffered and ready;
the volume is up and nothing is muted. It is simply **paused**. Nobody ever pressed start.

## Why

Connect does not play locally and tell the server afterwards. It **cancels** the local
action and routes the command to the MusicHub, then lets the server's echoed frame drive
the player:

```ts
// Passive: produce no audio — cancel the local action, let the server's echoed
// frame drive us.
this.opts.ensureActiveDevice();
event.preventDefault();
event.delay(this.opts.sendToHub(command, data));
```

That works for the library because the server is authoritative over it: it can look the
track up and broadcast a frame that starts playback. **A plugin's stream exists only in
the client.** The server has never heard of it, so there is no frame to echo — and the
local play was already cancelled waiting for one. The command is lost.

The inbound half is the same mistake in reverse: `handleMusicPlayerState` reconciles this
device to whatever the hub says, which while a plugin station plays means stopping it and
replacing the queue with tracks nobody asked for.

## Why it took so long to see

`preventDefault()` is not an error. The `playPrevented` event it emits has no listener.
The only visible trace was one line that reads like noise:

```
Requested device switch to: 89d548f8-…
```

That is `ensureActiveDevice()`, called from the passive branch — and it is called from
transport buttons too, so it looks like ordinary device chatter. It is the only signal
that a play was cancelled.

Because of that silence the whole stack below was eliminated first, by experiment: the
player package, the WebAudio backend, live streams as such, the plugin's item shape, the
relay, auth, the token, the origin, CSP, `Range` requests, HTTP/2. All fine. Two of those
had a real bug of their own (see `2026-08-09-plugin-media-failure-is-silent.md` and the
relay fix in `6247bf2`), which is the only reason the search was worth it.

## The fix

Both plugin item builders key an item as `plugin:{pluginId}:{…}`. That prefix marks media
the hub cannot represent:

- **Outbound** — `guardTransport` and `guardSeek` return early for a plugin item. It plays
  here and is not announced. A device that cannot hand this track to anything else must
  not give up its own copy of it.
- **Inbound** — `handleMusicPlayerState` skips a frame while a plugin item is current. A
  device playing plugin media is off the hub's map until it plays something the hub knows.

Read from `currentSong` rather than `player.item()`: the player call is a getter, but it is
also an observed interaction, and counting it broke two device-transfer tests.

Landed on `fix/plugin-track-not-routed-to-hub` (`ab9e237`), with regression coverage in
`3c11ad4`.

## What is still worth fixing upstream

A cancelled transport command should be visible. `playPrevented` exists and carries a
reason; nothing listens to it. One listener that logs — or better, tells the listener that
this device is not the active one — would have made this a five-minute diagnosis instead
of a multi-day one.
