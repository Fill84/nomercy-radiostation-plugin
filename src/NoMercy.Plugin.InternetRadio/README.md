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
  brought back. That is how you reach anything outside those seventeen tags. You spell
  the name with on-screen keys rather than typing it into a box — see below for why —
  and the term lives in the address, so a search can be bookmarked and shared.
- **Favourites**, per user. Your list is yours; another viewer on the same server
  has their own. A favourite keeps the whole station record, so one you found by
  searching still works after the catalogue refreshes without it.
- Browse by genre, or scan every station in one table with bitrate and codec.
- Selecting a station plays it immediately in the built-in player. A station's own
  page also offers **Add to queue** and a link to its homepage.

## Searching, and why it looks like that

There is no text box. A plugin cannot be handed what you type: a plugin form posts an
empty body, and so does the design system's search field, and giving that field a route
to follow makes it ignore the route and leave the page. Three mechanisms, three
different failures, all of them in the client — written up in `docs/upstream/`.

What does work is the address. So the search page offers A–Z and 0–9 as keys, and each
one takes you to the same page with one more character in it. Two characters is usually
enough: `to` already finds Tomorrowland. If you would rather type, put the name straight
in the address bar after `/search/` — it is the same page.

On a TV this is the better control anyway. On a desktop it is a workaround, and it will
be replaced with a field the moment a client can hand a plugin what was typed.

## What it declares

| Capability | Why |
| --- | --- |
| `ui` | The six pages above. |
| `scheduledTask` | One job, `refresh`, daily at 04:00, which updates the catalogue. |
| `network` → `**` | **Any host.** See below — this is the widest grant a plugin can ask for, and it is asked for a reason. |
| `rest` | Three endpoints of its own: one to toggle a favourite, and two that relay a station's audio and logo through this server. Nothing else reaches them — this plugin's own pages are the only caller. |

It declares no `ws`, no library access and no secrets storage.

### Why it asks for any host

Your dashboard refuses media and images that do not come from a NoMercy host — that is
its Content-Security-Policy, and it is there for good reason. Every radio stream and every
station logo is on somebody else's domain, so handing them to your browser directly
produces silence and blank tiles.

So the server fetches them and passes them on: your browser only ever talks to your own
server. That is what needs the wide grant. radio-browser carries some fifty thousand
stations across thousands of hosts, and no list written in advance can cover them.

What this means in practice: **this plugin can make outbound requests to any address.** It
uses that to fetch station audio and artwork and nothing else, and its whole source is
readable — but you are trusting it with that, and you should know you are. If that is more
than you want to grant, do not install it.

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
settings page shows the path. It holds one entry per user: their favourites, and
nothing else. What you searched for is not stored — it is in the address bar.

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
