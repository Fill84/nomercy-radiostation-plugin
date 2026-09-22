# A plugin item with no artist/album sets the tab title to "Station - undefined - undefined"

**Repo:** `nomercy-app-web`
**Found:** 2026-09-22, against `master` at `c821ca0a5817ae4483629954638e9d0ae6cd3e09`.
**Raised:** not yet. `gh issue list -R NoMercy-Entertainment/nomercy-app-web --state all
--search "undefined title" / "tab title" / "setTitle"` — no match (one unrelated hit, #23).

## What happens

When the player mounts a new item, the desktop player sets the document (tab) title from
the item's name, artist and album, unconditionally:

```js
// src/components/MusicPlayer/MusicPlayerDesktop.vue:109-113
setTitle(
    `${item.name} - ${item.artist_track.at(0)?.name} - ${
        item.album_track.at(0)?.name
    }`,
);
```

For a plugin item that has no artist/album yet — a radio station before its first
now-playing answer, whose `artist_track`/`album_track` are empty arrays — `.at(0)?.name` is
`undefined`, and template-literal interpolation turns that into the literal string
`"undefined"`. So the tab reads:

```
Station - undefined - undefined
```

until the plugin's first now-playing poll lands and `store/audioPlayer.ts:254` overwrites
the title with `announced.artist`. For Internet Radio specifically, that first answer can
be seconds away (see `2026-08-10-plugin-artist-cannot-be-drawn.md` on the same poll
timing), so the window is real but short; for a plugin whose now-playing never answers
during a session, the tab never recovers.

## Reproduction

1. Play any plugin-sourced item whose `artist_track`/`album_track` are empty at mount time
   (a freshly-selected radio station, before the first now-playing tick).
2. Read `document.title`.
3. Observed: `<station name> - undefined - undefined`. Expected: just the station name, or
   the station name and the site title, with no artist/album segment when there is none.

## What it costs

Nothing functional breaks, but every plugin-sourced item with no immediate artist/album
shows a title a viewer reads as broken — in the browser tab, in any OS-level "now playing"
surface that mirrors `document.title`, and in a screenshot or bug report a viewer might
send in that says "the app is showing undefined".

## Proposed fix (proposal, not decided)

Build the title from only the parts that exist, e.g.:

```js
setTitle([item.name, item.artist_track.at(0)?.name, item.album_track.at(0)?.name]
    .filter(Boolean)
    .join(' - '));
```

so a station with no artist or album yet reads as just its name, and gains the rest as it
becomes known — the same shape `setTitle()` in `lib/utils/string.ts` already uses for its
own "no argument" fallback.
