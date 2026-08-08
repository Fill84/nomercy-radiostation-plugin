# Internet Radio

Browse and play internet radio stations in the NoMercy MediaServer's built-in
player.

Adds two entries to the dashboard: **Internet Radio** under Music, and a status
page under plugin settings.

## What it does

- Fetches its station catalogue from [radio-browser.info](https://www.radio-browser.info/)
  — the most voted stations in each of seventeen genres. Nothing is pinned or
  curated: no station name, stream URL or logo exists anywhere in this plugin.
- **Search** every station radio-browser carries, not just the ones the genre sweep
  brought back. That is how you reach anything outside those seventeen tags.
- **Favourites**, per user. Your list is yours; another viewer on the same server
  has their own. A favourite keeps the whole station record, so one you found by
  searching still works after the catalogue refreshes without it.
- Browse by genre, or scan every station in one table with bitrate and codec.
- Selecting a station plays it immediately in the built-in player. A station's own
  page also offers **Add to queue** and a link to its homepage.

## What it declares

| Capability | Why |
| --- | --- |
| `ui` | The six pages above. |
| `scheduledTask` | One job, `refresh`, daily at 04:00, which updates the catalogue. |
| `network` → `*.api.radio-browser.info` | The only host it contacts. Streams are played by your client, not by the server. |
| `rest` | Two endpoints of its own: one to toggle a favourite, one to submit a search. Nothing else reaches them — a button on its own page is the only caller. |

It declares no `ws`, no library access and no secrets storage.

Station logos are loaded by your browser directly from wherever the station hosts
them, exactly as any image on a web page is. The server never fetches them, and the
network capability above does not cover them.

**You will need to enable it once.** A plugin that declares a network host is not
auto-enabled however `autoEnabled` is set, so the server starts it disabled until
you approve it in the dashboard. That is deliberate on the server's part, and
correct: this plugin calls a third-party API on a schedule.

## Stations it will not have

Only HTTPS, non-HLS streams are admitted. Your dashboard is served over HTTPS, so
a plain `http://` stream is blocked by the browser as mixed content and cannot
play at all — listing one would be listing something that does not work.

This is why **BBC Radio 1 and BBC Radio 6 Music are absent**: radio-browser carries
them only as HLS over `http://`. Earlier versions of this plugin shipped BBC URLs
that could never play in a browser for exactly that reason.

## Using your own station list

Drop a file named `stations.json` into the plugin's data folder — the settings page
shows you the exact path — and it replaces the fetched catalogue entirely:

```json
[
  {
    "name": "Local Jazz FM",
    "streamUrl": "https://example.com/jazz.aac",
    "logoUrl": "https://example.com/jazz.png",
    "homepage": "https://example.com/",
    "genre": "Jazz",
    "country": "US",
    "bitrateKbps": 256,
    "codec": "aac"
  }
]
```

Only `name` and `streamUrl` are required. Your file is used exactly as written and
is **not** filtered, so it is also how you add a station radio-browser does not
carry. If it cannot be parsed, the plugin logs a warning and fetches as normal.

## Where your favourites are kept

In `user-state.json` in the plugin's data folder, beside the catalogue cache — the
settings page shows the path. It holds one entry per user: their favourites and the
last thing they searched for.

Deleting the file loses every user's favourites and nothing else; the catalogue
rebuilds itself on the next refresh either way.

## Upgrading from 1.0.x

Nothing to do. The plugin id has not changed, and consent is recorded against that
id, so a server that already approved this plugin keeps running it without being
asked again. Your `stations.json`, if you have one, still replaces the catalogue
exactly as before.

Note that this version does serve REST endpoints, which 1.0.x did not — see the
table above for what they are.

## License

MIT.
