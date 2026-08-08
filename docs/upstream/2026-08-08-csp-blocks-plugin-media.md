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

- **`media-src`** has to allow the stream host, or plugin audio cannot play at all.
- **`img-src`** has to allow the logo host, or plugin artwork cannot draw.

Neither can be enumerated ahead of time: radio-browser carries some fifty thousand
stations across thousands of hosts. Realistic options, in the order we would suggest them:

1. **Proxy through the server.** The plugin host already mediates outbound calls through a
   declared allowlist. Serving `/api/v1/plugins/{id}/media?url=…` and `/image?url=…` from
   the server would keep both inside `'self'`, keep the existing consent model meaningful,
   and give the owner one place to see what a plugin reaches for. It also solves mixed
   content and dead-logo handling in one place.
2. **Widen the directives from the manifest.** A plugin already declares the hosts it may
   contact. The page could extend `media-src` and `img-src` with the granted hosts of
   loaded plugins — but a wildcard host like `*.api.radio-browser.info` does not cover the
   stream hosts that API points at, so this only helps plugins whose media lives on the
   same domain they call.
3. **Relax the directives.** Fastest, and the one we would argue against: it removes the
   protection for the whole dashboard to serve one plugin.

## Scope

Any plugin that plays or shows third-party media hits this. It is not specific to Internet
Radio; that plugin is only the first one to try.

## What this plugin does meanwhile

Nothing that helps. Streams and covers are handed to the client as URLs, because that is
what the contract's `playMedia` intent and `NMImage` take. The plugin renders, navigates,
searches and stores favourites correctly — and every station is silent, with a blank cover.
