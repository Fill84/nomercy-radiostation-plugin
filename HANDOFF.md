# Handoff — Internet Radio plugin

Written 2026-08-09. Say "ga door" and start from **Priority 1**.

## The one-line state

The plugin looks and behaves like the rest of the app now; **radio audio still does not
play**, and that is the only thing that matters until it does.

---

## Priority 1 — radio does not produce sound

### What is established (evidence, not guesses)

- Library music **plays fine** in the same browser session. Clicked "How Many Times?" by
  Dyce from *Songs you like*, it played, timer advanced, sound confirmed by the owner. So
  the player, the device and the audio context all work.
- A radio station **never starts**: the track lands in Now Playing with its cover and queue
  entry, the transport shows `00:00 / 00:00`, and pressing Play (the plugin's button or the
  player's own) changes nothing.
- **No `/stream/` request ever reaches the server.** So the failure is before the audio
  element is given a source, not a failure to fetch it.
- The console shows **no media error at all**. Only `lyrics fetch failed` (a 404 with an
  empty body, cosmetic) and a repeating `Requested device switch to: 89d548f8-…`.
- The old blocker is **gone**: `TrackLinks` no longer throws, and the track id is now
  `plugin:{pluginId}:{stationUuid}` rather than the stream url. Both were fixed upstream and
  are live (see *Landed upstream*).

### Where to look next

The app's music player is `@nomercy-entertainment/nomercy-music-player`, configured in
`nomercy-app-web/src/store/audioPlayer.ts` with `backend: 'webaudio'`.

- `node_modules/@nomercy-entertainment/nomercy-music-player/dist/adapters/audio-backend/web-audio.js`
  — `load(url, opts)` sets `element.crossOrigin = 'anonymous'`, then `element.src = …` and
  `element.load()`, and **awaits `loadedmetadata`**. Two things worth proving:
  1. Is `load()` reached at all for a plugin item? Nothing requests the stream, which
     suggests it is not. Trace `playTrack` → `audioPlayer.item(song, {autoplay:true})` →
     whatever decides to call the backend. `dist/player/` and `dist/index.js` are the
     places; the package is public as `nomercy-music-player` if the source reads better.
  2. If it is reached, a **live stream never fires `loadedmetadata` reliably** and the
     awaited promise would hang forever with no error — which matches the symptoms exactly.
     A library track is finite and fires it immediately. That asymmetry is the strongest
     hypothesis on the table.
- The plugin's own item differs from a library item in: `url`/`path` is the relay url,
  there is no duration, no `folder`, no `libraryID`. Compare a working library
  `PlaylistItem` against the one `toPlaylistItem` builds in
  `nomercy-app-web/src/lib/plugin/actionInterpreter.ts`.

Fix belongs upstream (player package or app-web), on a branch, with a PR — same as the two
that already landed.

---

## Priority 2 — /all shows only a handful of stations

The owner asked for **virtual scrolling** so every station is reachable. `AllStationsView`
currently renders the whole catalogue into one `NMGrid`; the app's own grid does not
paginate. Look at how Music's own long lists do it before inventing anything.

---

## What is done and verified in the browser

| | |
| --- | --- |
| Search | A real form on the landing page, results underneath. "tomorrowland" → 4 stations, term stays in the box. `/search/{term}` renders the same page, so a search is shareable |
| Tiles | `NMGrid` + `NMMusicCard` — the components Films, Series and Music are drawn with. Uniform, responsive, covers load |
| `/all` | The same grid, sorted by name |
| Genres, favourites | Work |
| Station page | Play, Add to queue, favourite toggle, homepage, facts table |
| Playback | **No.** See Priority 1 |

## Landed upstream (both merged and live)

`nomercy-app-web` PR #16 (closed issue #15), two commits:

1. `TrackLinks` looked its own element up with
   `document.querySelector('#trackLink-' + type + '-' + id + '-' + suffix)`. A plugin's
   track id was built from its stream url, so that asked for
   `#trackLink-artists-plugin:<id>:https://…` — not a valid selector, so it threw during
   mount and no plugin could play anything. Replaced with a template ref. Also:
   `toPlaylistItem` now honours a plugin-supplied `id`, the lyrics path is encoded, and
   `CoverImage.makeQuery` appends with `&` when the url already has a query (that one was
   answering 401 on any cover carrying an access token).
2. `PluginNode` falls back to `getNmComponent`, so a plugin can draw `NMGrid`,
   `NMMusicCard` and the rest. Before this a plugin **could not** look like the app: those
   components are in `nmComponentMap`, which the plugin host never consulted.

The app-web checkout is at `../nomercy-app-web` (cloned, `yarn install` done,
`yarn type-check` clean). Branch `fix/plugin-media-playback` is merged.

## Two things worth knowing about this client

Both cost hours before they were understood:

- **`PluginComponentType` in the packed contract disagrees with the deployed client.** It
  maps `Container`, `List`, `Row`, `Grid`, `Card`, `Detail`, `Form` and `Table` all onto
  `"NMCard"`. The client keys its own components by `"PluginForm"`, `"PluginGrid"`, … , so
  everything sent under the contract's names was drawn as a design-system card. `Ui.cs` is
  the single place that names components, read off the running bundle. If a form or a table
  suddenly renders as a box again, look there first.
- **A plugin mounted by section lives at `/music/plugins/{id}`**, not `/plugins/{id}`. A
  `RouterLink` in a card must use the real mount; `PluginActionIntent.Navigate` is prefixed
  by the client and must not.

## Repos and how to deploy

| | |
| --- | --- |
| Plugin | `f:\DevProjects\NoMercyEntertainment-Developement\nomercy-radiostation-plugin` |
| Web app | `../nomercy-app-web` |
| Media server (contract source) | `../nomercy-media-server` |
| Remotes | `origin` (Forgejo) and `github` — both on `main`, both current |
| Last tag | `v1.1.0`. **Nothing tagged since**; manifest reads 1.1.1 |

Build and test with `"$USERPROFILE/.dotnet/dotnet.exe"` — the `dotnet` on PATH is 8.0 and
cannot build net10.0.

```sh
"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --nologo
```

Deploying to beast-unit **requires the server to be stopped** — a loaded plugin's assembly
is held open, the copy fails, the old build stays, and testing then happens against code
that is not running. Ask the owner to stop it, deploy, ask them to start it. Copy with
base64 over ssh (`scp` fails against this host) and compare md5 both sides:

```sh
DEST='$LOCALAPPDATA/NoMercy/plugins/NoMercy.Plugin.InternetRadio'
base64 -w0 NoMercy.Plugin.InternetRadio.dll > /tmp/b64.txt
ssh beast-unit "cat > /tmp/rp.b64" < /tmp/b64.txt
ssh beast-unit "base64 -d /tmp/rp.b64 > \"$DEST/NoMercy.Plugin.InternetRadio.dll\""
```

The torrent plugin has this as a script: `../nomercy-torrent-plugin/scripts/deploy-to-server.sh`.

**The build at `HEAD` is not deployed.** beast-unit runs the build from `4378970`; `bfb8202`
(the `/music/plugins` link fix) still needs a deploy.

## House rules

- Push only when asked. Never tag until the thing actually works for someone installing it.
- No self-references anywhere: not in commits, not in PRs, not in files.
- Upstream work goes on its own branch with a PR, never straight to the default branch.
- Every upstream finding gets written up in `docs/upstream/`.
