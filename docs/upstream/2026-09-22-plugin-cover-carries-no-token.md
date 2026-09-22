# A host-minted, server-relative plugin cover is requested with no token

**Repo:** `nomercy-app-web`
**Found:** 2026-09-22, against `master` at `c821ca0a5817ae4483629954638e9d0ae6cd3e09`.
**Raised:** not yet. Searched `gh issue list -R NoMercy-Entertainment/nomercy-app-web
--state all --search "cover token" / "plugin image" / "server-relative" / "PluginImage"` —
no match; the only near-hit is #15 (closed, a different track-id defect) and #30 (closed,
video-player routing).

## What happens

A plugin's card or list item shows an image whose `url`/`cover` is a path the host minted
(server-relative, e.g. `/media-proxy/...`, requiring the caller's own session/token to
resolve) rather than an absolute URL. Two render paths carry it, and neither attaches a
token:

- `src/components/plugin/PluginImage.vue` — the only image component in the plugin
  component map (`pluginComponentMap.ts:22`). It takes a `url` prop and binds it straight
  to `<img :src="url">`:

  ```vue
  <!-- src/components/plugin/PluginImage.vue:30-38 -->
  <img
      v-if="url && !broken"
      :src="url"
      crossorigin="anonymous"
      ...
  >
  ```

  No prefixing, no query, no token — `url` is drawn exactly as the plugin tree supplied
  it. Nothing upstream of it normalises the value either: `PluginNode.vue`'s `bindings`
  computed just spreads `props.node.props` onto the resolved component
  (`src/components/plugin/PluginNode.vue:69-76`), so whatever the server sent is what the
  `<img>` tag requests.

- `src/components/MusicPlayer/components/CoverImage.vue`, used for the player bar's
  now-playing cover (a plugin's play intent can set `cover` — see
  `src/lib/plugin/actionInterpreter.ts:116`, `cover: text(payload, 'cover') ?? null`). This
  component DOES resolve a server-relative cover to an absolute URL:

  ```js
  // src/components/MusicPlayer/components/CoverImage.vue:118-121
  if (props.data?.cover?.startsWith('/')) {
      source.value = encodeURI(
          `${currentServer.value?.serverBaseUrl.replace(/\/$/, '')}${props.data?.cover}`,
      );
  ```

  but still appends no token. The component's own comment two functions below
  acknowledges tokens belong on plugin covers (`makeQuery()`, "A plugin's cover carries the
  caller's access token, so appending with `?` made the token read as `<jwt>?width=500`") —
  but that logic only fixes the query separator for a token the caller already embedded in
  the string. Nothing here mints or attaches one when the server instead sent a bare,
  relative path.

## Read, not reproduced

I did not start a server (out of scope for this task) or capture a live 401. This is a
static read of `origin/master` confirming neither component resolves a server-relative
plugin image against a token; I have not watched the request fail in a browser.

## Reproduction (as written)

1. A plugin UI node (`PluginImage`, or a `PluginCard` with an `image` prop) sets
   `url`/`image` to a server-relative path that the media proxy requires a caller token
   for, e.g. `/plugins/<id>/media-proxy/logo.png`.
2. The client renders `<img src="/plugins/<id>/media-proxy/logo.png">` with no token.
3. Expected: the browser requests the page's own origin plus that path, with no
   authorization the media proxy accepts, and the image 404s/401s and falls back to the
   broken-picture placeholder `PluginImage.vue` already draws for that case.

## What it costs

Any plugin whose UI or play-intent points at a host-minted, token-scoped image (the shape
the ABI 12 media-proxy grant flow produces) draws a broken-picture placeholder instead of
the image, with no way for the plugin to work around it — the plugin does not control how
the client requests the URL it was given.

## Proposed fix (proposal, not decided)

Give `PluginImage.vue` and `CoverImage.vue`'s server-relative branch the same treatment
`currentServer` already provides elsewhere for authenticated server requests: resolve a
server-relative image URL against `currentServer.value?.serverBaseUrl` (as `CoverImage.vue`
already does for the *path*) and append whatever the session's access token mechanism is,
using the `&`/`?` separator logic `makeQuery()` already has. A single shared resolver
function used by both components would keep the fix in one place instead of two.
