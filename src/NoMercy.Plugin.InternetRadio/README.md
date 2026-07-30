# Internet Radio

Browse and play internet radio stations in the NoMercy MediaServer's built-in
player.

Adds two entries to the dashboard: **Internet Radio** under Music, and a
read-only status page under plugin settings.

## What it does

- Fetches its station catalogue from [radio-browser.info](https://www.radio-browser.info/)
  — ten curated stations pinned by id, plus the most popular stations in each of
  seventeen genres.
- Browse by genre, or scan every station in one table with bitrate and codec.
- Selecting a station plays it immediately in the built-in player. A station's own
  page also offers **Add to queue** and a link to its homepage.

## What it declares

| Capability | Why |
| --- | --- |
| `ui` | The five pages above. |
| `scheduledTask` | One job, `refresh`, daily at 04:00, which updates the catalogue. |
| `network` → `*.api.radio-browser.info` | The only host it contacts. Streams are played by your client, not by the server. |

It declares no `rest`, no `ws`, no library access and no secrets storage.

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

## There is nothing to configure

The settings page is read-only, and not by choice. A plugin cannot currently
receive anything from its own UI on this server: plugin REST routes are served
unversioned while the dashboard posts to `/api/v1`
([media-server issue #26](https://github.com/NoMercy-Entertainment/nomercy-media-server/issues/26)),
and the hub is not an alternative because plugin hub handlers are never registered.
Favourites and station editing arrive when either is fixed.

## License

MIT.
