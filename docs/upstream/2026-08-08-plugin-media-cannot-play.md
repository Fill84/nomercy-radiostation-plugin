# Plugin media cannot play: the track id is a url

**Where:** app.nomercy.tv · observed 2026-08-08 against server v0.1.470
**Effect:** no plugin can play audio. Not "plays badly" — the player throws while mounting
and never requests the stream at all.

## What happens

Clicking a station raises `PlayMedia`. The now-playing panel fills in correctly: title,
cover, queue entry. Then:

```
[Vue error] SyntaxError: Failed to execute 'querySelector' on 'Document':
'#trackLink-artists-plugin:5KTKRT4Z2Y9P59Y40W5CX4TQKF:https://<server>:7626/api/v1/plugins/5KTKRT4Z2Y9P59Y40W5CX4TQKF/stream/9e31c4e7-…-player'
is not a valid selector.
    at TrackLinks-BJYOtUPH.js
```

The server's access log records the cover requests for that station and **no `/stream/`
request at any point**, before or after pressing play. The transport is never reached; the
component that would start it has already thrown. On screen this is a "Something went
wrong — a component error occurred" toast, a play button that stays a play button, and
00:00 / 00:00.

## Why

`PluginActionIntent.PlayMedia` carries `streamUrl`, `title`, `artist` and `cover`. There is
no id on it, so the client synthesises one:

```
plugin:{pluginId}:{streamUrl}
```

That identifier contains a url, and a url contains `:` and `/`. Both are invalid in a CSS
id selector unescaped, and both are path separators when the id is interpolated into a
route. So the same id breaks in two places:

1. `TrackLinks` builds `'#trackLink-artists-' + id + '-player'` and calls `querySelector`
   with it. Throws — this is the one that stops playback.
2. The lyrics fetch builds `/api/v1/music/tracks/{id}/lyrics`, which becomes
   `/music/tracks/plugin:<pluginId>:https://…/stream/<uuid>/lyrics`. Answers 404, and the
   404 body then fails `response.json()` with "Unexpected end of JSON input".

No plugin can avoid this. Every stream url is a url.

Passing `artist: null` does not avoid it either — the artists slot is rendered regardless,
and the selector is built from the track id, not from the artist.

## What fixes it

Either end works; the second is better.

**Client, minimal.** Escape at the use sites: `document.querySelector('#' +
CSS.escape(trackId) + '-player')`, and `encodeURIComponent(trackId)` in the lyrics path.
This unblocks playback today without a contract change.

**Contract, properly.** Add an `id` to `PlayMedia` and `Enqueue` so a plugin supplies a
stable, opaque identifier and the client never has to invent one from a url. A radio
station already has one (`9e31c4e7-03b6-4a80-a4e2-5977b023d32c`); it is what the relay
routes on. An id derived from a stream url is also unstable by nature — it changes when the
url does, so history, favourites and resume state all key on something that was never meant
to be a key.

Worth doing both: the escape stops the crash, the id stops the class of bug.

## Two smaller ones found alongside

**The now-playing cover appends to a url that already has a query.** It requests
`{cover}?width=500&type=avif` without checking for an existing `?`, so a url carrying an
access token arrives as `?access_token=<jwt>?width=500&type=avif`. The token is then the
jwt with `?width=500` glued to it, and the request is answered 401 — a blank cover in the
player while the same cover renders in the grid. Worked around here by ending the url with
a spare empty parameter that absorbs the append; the real fix is to append with `&` when a
query string is already present.

**`hidden_on` is not honoured on web.** A component with
`box.hidden_on: ["web", "mobile"]` still rendered on web. It matters beyond appearance: the
contract says a component hidden on a surface is not focusable there, which is what D-pad
traversal depends on.
