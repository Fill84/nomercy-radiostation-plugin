# The SDK still declares PluginMediaCard; the client deleted it

**Repos:** `nomercy-media-server` (declares the tag) and `nomercy-app-web` (deleted the
component) — filing against `nomercy-media-server` since the stale declaration is the
server-side SDK's.
**Found:** 2026-09-22, against `nomercy-media-server` `dev` at
`f217a0f365e407f831acb9d35bfcfa60367db6dd`, and `nomercy-app-web` `master` at
`c821ca0a5817ae4483629954638e9d0ae6cd3e09`.
**Raised:** not yet, and distinct from the closed `nomercy-media-server#27` (a different,
earlier mismatch: `Container`/`List`/`Row`/... collapsing onto `"NMCard"`; that issue never
mentions `MediaCard`). `gh issue list -R NoMercy-Entertainment/nomercy-media-server
--state all --search "MediaCard" / "PluginComponentType"` returns only #27.
`gh issue list -R NoMercy-Entertainment/nomercy-app-web --state all --search "MediaCard"`
— no results.

## What happens

`nomercy-app-web` deleted its media-card component on 2026-09-21:

```
$ git show --stat 48a805118
refactor(plugins): draw plugin screens with the components the app already has

A media card, a guide grid, a channel strip and a live control strip
were four components only plugins could use... The server builds all
four out of rows, buttons, cards and tables now, so this app draws
them with what it already had.

 src/components/plugin/PluginChannelStrip.vue | 43 ------
 ...
```

and today carries no reference to `"PluginMediaCard"` anywhere (`git grep -n
"PluginMediaCard" origin/master` — zero hits) and no entry for it in
`pluginComponentMap.ts` (which maps every tag the client draws to a component).

`nomercy-media-server`'s SDK still declares it as a valid, current tag:

```csharp
// src/NoMercy.PluginSdk.Abstractions/PluginComponentType.cs:70
public const string MediaCard = "PluginMediaCard";
```

included in the enumerated set every client is told to support:

```csharp
// same file, ~line 107 (the `All` set)
public static IReadOnlySet<string> All { get; } = new HashSet<string>(...) { ..., MediaCard, ... };
```

and still backed by a live record type, `PluginMediaCard.cs`.

## What actually sends the tag today

Nothing in `nomercy-media-server` outside the constant declaration and
`tests/NoMercy.Tests.Plugins/PluginUiContractTests.cs` references
`PluginComponentType.MediaCard` (`git grep -n "PluginComponentType.MediaCard" origin/dev`
— no hits beyond the test). So no shipped server-side view builder currently emits this
tag — the immediate blast radius is the contract test / kitchen sink / `nomercy-plugin
verify` tooling that checks a client against `PluginComponentType.All`, not a live plugin
screen. That is still real cost: any tool or future builder that trusts `All` as "every tag
a client is expected to render" will be told a client renders `PluginMediaCard`, and none
of the three clients do.

## Reproduction (read, not reproduced — no server started)

1. Build a plugin view containing a node with `component: "PluginMediaCard"`.
2. On the current client, `PluginNode.vue` resolves it via `getPluginComponent('PluginMediaCard')`
   (undefined — not in `pluginComponentMap.ts`) then
   `getPluginAppComponent('PluginMediaCard')` (also undefined), and falls through to the
   "unsupported component" notice (`PluginNode.vue`, the final `<div role="note">` branch).
3. Expected: the card the tag used to draw never appears; the viewer sees the
   `plugins.unsupported_component` placeholder text instead.

## What it costs

Low today (nothing ships the tag), but it is exactly the trap `nomercy-media-server#27`
described for a different set of tags: a contract that says a name is valid when no client
honours it, discovered only when a plugin author — or the verify tool, or the kitchen sink
— reaches for it.

## Proposed fix (proposal, not decided)

Either remove `MediaCard` from `PluginComponentType` (and `PluginMediaCard.cs`,
`PluginUiContractTests.cs`'s use of it) now that all three clients compose the equivalent
from rows/buttons/cards/tables, matching what `48a805118`'s commit message already
describes as done server-side — or, if `PluginMediaCard.cs` is meant to stay as a
server-side *builder* that emits the composed shape rather than the tag, change the
constant so it is never sent on the wire as `"PluginMediaCard"` and drop it from `All`.
