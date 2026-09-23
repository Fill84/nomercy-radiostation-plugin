# Internet Radio

Browse and play internet radio stations in the NoMercy MediaServer's built-in
player.

Adds two entries to the dashboard: **Internet Radio** under Music, and a status
page under plugin settings.

## Requirements

NoMercy MediaServer v0.1.526 or newer. Older servers refuse 2.0.0, and newer
servers refuse 1.x.

## Install

**Install.** In the dashboard, add
`https://forgejo.phillippepelzer.me/FiLL/nomercy-radiostation-plugin/releases/download/<tag>/repository.json`
as a plugin repository, then install Internet Radio from it. The server checks the
download against the checksum in that file.

Installing by hand — extracting the zip into `<server>/plugins/` — skips that check, and
a plugin uploaded through the dashboard's upload button is visible to the server owner
alone. Use the repository.

## After installing or updating

- Enable the plugin in the dashboard.
- Approve the two runtime requests it makes on its first refresh (or the first
  station page you open, whichever comes first): **streaming**, so stations play
  through the server, and **network access**, so it can fetch station logos and
  read the track titles a station announces.

Until the plugin is enabled and streaming is approved, stations can be browsed but
not played — the station page says so. Approving streaming without the network
grant plays stations with no logos and no now-playing titles.

## What it fetches

- The station list from radio-browser.info, daily.
- Station logos, cached on the server, at most 16 KB each.
- While a station plays, one extra connection to that station that reads only its
  track titles.

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
- Every station is drawn with the components the app draws its own libraries with, so a
  shelf of stations is the same shelf Films, Series and Music use — the same grid, the
  same card, the same behaviour on every screen size.
- Selecting a station opens its page, which plays it in the built-in player and offers
  **Add to queue** and a favourite toggle. That is how a card behaves everywhere else
  in the app.
- A queued station's link is valid for fifteen minutes. A station that waits longer
  than that in the queue may not start; playing it again from its own page works.

## Searching

A box on the landing page, and the results appear under it. The term is also a route, so
`/search/tomorrowland` renders the same page — a search can be shared, bookmarked, or
typed straight into the address bar.

Two characters is usually enough: `to` already finds Tomorrowland.

## What it declares

| Capability | Why |
| --- | --- |
| `ui` | The six pages above. |
| `scheduledTask` | One job, `refresh`, daily at 04:00, which updates the catalogue. |
| `network` → `*.api.radio-browser.info` | The one host this plugin knows about at package time: the station directory it fetches the catalogue and search results from. |
| `rest` | Three endpoints of its own: one to toggle a favourite, one that runs the search form, and one that returns a station's current track title, read from its ICY metadata, while it plays. Nothing else reaches them — this plugin's own pages are the only caller. |

That is everything the manifest declares up front. It declares no `ws`, no library access
and no secrets storage. It does **not** declare `media.proxy` or a wider `network` grant —
see below.

### The two runtime requests

Every station is on a different, unlisted host — radio-browser carries some fifty
thousand stations across thousands of servers, and no list written in advance can cover
them. So rather than declare a wildcard in the manifest, this plugin asks the owner for
two things at runtime, the moment it has something to do — its first refresh, or the
first station page opened, whichever happens first — and shows the reason in the
dashboard like any other request:

| Runtime grant | Why | What you lose without it |
| --- | --- | --- |
| `media.proxy` = `*` | Lets the server mint a short-lived url, bound to the listener, for the station being played, so the browser never talks to the stream's own host directly. | Stations can be browsed but not played. The station page says so. |
| `network.host` = `*` | Your dashboard's Content-Security-Policy refuses media and images that do not come from a NoMercy host, so the server fetches each station's logo and reads the track titles it announces on the plugin's behalf, then passes them on — your browser still only ever talks to your own server. | Stations play, but with no logos and no now-playing titles. |

What this means in practice: **once both are approved, this plugin can make outbound
requests to any address**, to fetch station artwork and to read a playing station's
now-playing metadata — and nothing else; the audio itself streams through the first
grant, not this one. Its whole source is readable, but you are trusting it with that
reach, and you should know you are. If that is more than you want to grant, approve
neither and stations stay browsable-but-silent.

**You will need to enable it once.** A plugin that declares a network host is not
auto-enabled however `autoEnabled` is set, so the server starts it disabled until
you approve it in the dashboard. That is deliberate on the server's part, and
correct: this plugin calls a third-party API on a schedule.

## Stations it will not have

Only HTTPS, non-HLS streams are admitted to the catalogue. That is a filter at
admission time, not something the browser enforces during playback: streams play
through the server's own media proxy, so the browser only ever talks to your
server, never to the station directly. The filter stays anyway — a station this
plugin cannot mint a working stream for is worse listed than absent.

This is why **BBC Radio 1 and BBC Radio 6 Music are absent**: radio-browser carries
them only as HLS over `http://`. Earlier versions of this plugin shipped BBC URLs
that could never play at all for exactly that reason.

## Using your own station list

Drop a file named `stations.json` into the plugin's data folder — the settings page
shows you the exact path — and it replaces the fetched catalogue entirely:

```json
[
  {
    "name": "Local Jazz FM",
    "streamUrl": "https://example.com/jazz.aac",
    "logoUrl": "https://example.com/jazz.png",
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

The plugin id has not changed, but the manifest has: 2.0.0 declares a hook 1.0.x
never did, so the server asks you to enable it again even on a server that
already approved this plugin. See "After installing or updating" above — you
also need to approve the two runtime requests before stations play, with logos
and now-playing titles.

Your `stations.json` and `user-state.json`, if you have either, are unaffected
and still work exactly as before.

Note that this version serves REST endpoints, which 1.0.x did not — see the
table above for what they are.

## License

MIT.
