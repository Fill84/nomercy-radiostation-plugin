# The dashboard's CSP blocks everything an internet-radio plugin serves

**Where:** app.nomercy.tv, the page that hosts plugin UIs · observed 2026-08-08
**Effect:** no station can play, and no station logo can draw. For this plugin that is
its entire purpose.

## What the browser says

Playing any station:

```
Loading media from 'https://stream.brokenbeats.net/tune' violates the following
Content Security Policy directive: "media-src 'self' blob: data:
  https://nomercy.tv https://*.nomercy.tv https://*.nomercy.tv:*
  https://pub-a68768bb5b1045f296df9ea56bd53a7f.r2.dev
  https://430cc0dc9fcf3a1cf3258d165c15abf4.r2.cloudflarestorage.com".
The action has been blocked.
```

Drawing any cover, roughly seventy times on one page:

```
Loading the image '<URL>' violates the following Content Security Policy directive:
  "img-src 'self' blob: data: <fifteen nomercy-owned hosts>". The action has been blocked.
```

## Why a plugin cannot fix this

Both directives list only NoMercy's own hosts and its two R2 buckets. Every internet
radio stream and every station logo is, by definition, on somebody else's domain. There is
no value a plugin can put in a `playMedia` intent or an `NMImage` that satisfies either
directive.

This is separate from the plugin capability system and is not what
`"network": { "hosts": [...] }` governs. That capability bounds the plugin's own
`IPluginContext.HttpClient` — a server-side call. The stream and the logo are fetched by
the browser, from the page, and the page's CSP is what refuses them.

## What would have to change

Neither host set can be enumerated ahead of time: radio-browser carries some fifty
thousand stations across thousands of hosts, and the stream host is never the API host the
plugin declared. So widening the directives from the manifest does not help, and relaxing
them removes the dashboard's protection to serve one plugin.

Both directives are satisfied by the same answer — **the server fetches it, so the browser
sees `'self'`.**

### Images: the pipeline already exists

`BaseImageManager.ColorPalette(DownloadUrl client, string type, Uri path, download: true,
Size? maxDecodeSize)` already takes an arbitrary URL, downloads it, stores it and decodes
it to produce a palette. That is exactly what artwork does today, and it is why NoMercy's
own hosts and its R2 buckets are the ones `img-src` allows.

What is missing is a way in. `IPluginSystem` exposes capabilities by name —
`player`, `cast`, `downloads`, `notifications`, `library`, `tasks` — and there is no
`images`. Adding one fits that design exactly, which was written so "a host can grow a
capability without the contract moving at all":

```
system.Has("images")
system.InvokeAsync("images", new { url = "https://cdn.example.com/logo.png" })
  -> a path the client can draw, on a host img-src already allows
```

A plugin then hands the served path to `NMImage` instead of the third party's URL, and
gets the palette and the caching for free. The grant model stays meaningful: the host is
the one fetching, so the owner can see and refuse it.

### Audio: no equivalent exists

Nothing in the server relays a live third-party stream — the media pipeline encodes and
serves the library's own files. A pass-through proxy would be a new thing:

```
GET /api/v1/plugins/{id}/stream?url=…   ->  relays the audio, no transcoding
```

That keeps playback inside `'self'`, and it is the only approach that works for a
catalogue this size. The costs are real and worth stating: the server carries the
bandwidth for every listener, and a plugin-supplied URL reaching a proxy needs the same
scrutiny the network capability already applies — it should be bounded by the plugin's
granted hosts, not open to any URL a plugin invents.

Until one of these exists, a plugin that plays third-party audio cannot work on this
dashboard, however correct the rest of it is.

## Scope

Any plugin that plays or shows third-party media hits this. It is not specific to Internet
Radio; that plugin is only the first one to try.

## What this plugin does meanwhile

Nothing that helps. Streams and covers are handed to the client as URLs, because that is
what the contract's `playMedia` intent and `NMImage` take. The plugin renders, navigates,
searches and stores favourites correctly — and every station is silent, with a blank cover.
