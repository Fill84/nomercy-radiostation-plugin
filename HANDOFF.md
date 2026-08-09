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
- No `/stream/` request ever reached the server — but that observation is from **2026-08-08,
  before the `TrackLinks` fix landed**, and it had a cause that no longer exists (the mount
  threw before the transport was reached). It has not been re-taken since, and the
  conclusion drawn from it — "the failure is before the element is given a source" — is
  not currently supported by anything. Treat it as unverified.
- The console shows **no media error at all**. Only `lyrics fetch failed` (a 404 with an
  empty body, cosmetic) and a repeating `Requested device switch to: 89d548f8-…`. This
  rules out nothing: a failed load is silent by design. See below.
- The old blocker is **gone**: `TrackLinks` no longer throws, and the track id is now
  `plugin:{pluginId}:{stationUuid}` rather than the stream url. Both were fixed upstream and
  are live (see *Landed upstream*).

### Ruled out by experiment, 2026-08-09 — do not re-tread

Both hypotheses this handoff proposed are **wrong**. They were tested against the real
published player, not reasoned about.

The harness: a node relay that pipes a live Icecast stream (SomaFM, 128k mp3) with the
exact response headers `RelayAsync` sets — status passed through, `Accept-Ranges: bytes`,
`Access-Control-Allow-Origin: *`, upstream content-type, body piped as it arrives — plus
the same audio captured once and served as a finite file with a `Content-Length`, as the
control. Driven in Edge (real codecs) against `nomercy-music-player.iife.js`, unmodified.

| Claim | Result |
| --- | --- |
| "A live stream never fires `loadedmetadata`" | **False.** Fires at +1478ms, `duration: Infinity` |
| "`backend.load()` hangs forever on a stream" | **False.** Resolves in 5ms, reaches `canplay` |
| "The plugin's item shape is the problem" | **False.** `plugin:{id}:{uuid}` id, `url === path`, no duration/folder/libraryID — plays |
| Full production path, `queue()` + `item(item, {autoplay:true})` | **Plays.** `playState=playing`, `phase=playing`, position 0.77 → 1.77 → 2.77s |

So the player, the backend, the item shape and live streams as such are all fine. The
failure is not in the client stack and not in the shape of what the plugin sends.

### What the failure actually looks like

The same harness with the URL answering **401** reproduces every reported symptom exactly:

```
element event=error readyState=0 networkState=3 err=4
t=1s  playState=idle phase=ready elCurrentTime=0.00 paused=true
t=12s playState=idle phase=ready elCurrentTime=0.00 paused=true
```

Track sits in Now Playing, transport frozen at 00:00, and **not one line in the console** —
because `item()` swallows the load rejection with `.catch(() => {})` and `load()` never
emits `error` despite its doc comment saying it does. Written up in
`docs/upstream/2026-08-09-plugin-media-failure-is-silent.md`.

That means "no media error in the console" is **not evidence of anything**. Any network
failure — 401, 403, 404, a CSP refusal — looks precisely like this.

### The one thing left to determine

The media request fails at the network layer. Which failure is still open, and it needs one
look at a real browser rather than more reading. On the music page, with a station clicked:

```js
const el = document.querySelector('#music-player audio') ?? document.querySelector('audio');
console.log(el.currentSrc, el.error, el.networkState, el.readyState);
```

- `currentSrc` with **no `?access_token=`** → `mediaAuthorization()` refused the URL, so the
  origin of `MediaProxy.Stream()` does not equal the origin of `currentServer.serverBaseUrl`.
  Confirmed independently: the server answers **401** to a token-less request on that
  route. `MediaProxy.Cover()` carries its own token, which is why covers load and audio
  does not — that asymmetry is deliberate and is the prime suspect.
- `currentSrc` pointing at the **station's own url** rather than this server → `PublicBaseUrl()`
  returned null and the fallback in `StationCards.PlayIntent` was taken; that url is
  http and/or off-origin, so CSP or mixed content blocks it before any request.
- `currentSrc` correct **with** a token → the relay itself is answering badly; take it from
  the Network tab.

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
