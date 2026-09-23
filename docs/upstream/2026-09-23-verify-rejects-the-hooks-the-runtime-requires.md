# `nomercy-plugin verify` rejects the hooks the runtime requires, and it is not theoretical: Automix's own manifest is Blocked by it

**Repo:** `nomercy-media-server` (the `tools/nomercy-plugin` CLI and the SDK it ships against)
**Extends:** `2026-09-22-verify-blocks-ui-and-scheduledtask-hooks.md`, which found the
mechanism first. This note re-verifies it today and adds the piece the original finding
did not have: a real manifest from the platform author's own reference plugin that this
scan actually Blocks, not a constructed example.
**Found:** 2026-09-23, against `nomercy-media-server` `dev@4c16c0d09f4bcc2fa3a39ad2e791f52441cdd69b`
(fetched today; `ManifestScan.cs` and `PluginCapabilityVocabulary`/`PluginHookCapability`
unchanged since the original finding at `f217a0f365e407f831acb9d35bfcfa60367db6dd` — no
commits touch either file between the two), and `nomercy-automix-plugin`
`origin/master@bd198a1502af646913c7732223c04c827f61459a` (fetched today).
**Raised:** not yet. Same search as the original finding, still no match.

## What happens

`ManifestScan.cs:122-128` checks every declared hook against `PluginCapabilityVocabulary`
(`src/NoMercy.PluginSdk.Abstractions/Generated/PluginCapabilityVocabulary.cs`) — the
**capability** list, dotted names like `network.fetch`, `media.proxy`, `ui.mount`. It has no
entry literally named `"ui"` or `"scheduledTask"`. The **hook** vocabulary is a different
type for a different manifest field, `PluginHookCapability`
(`src/NoMercy.PluginSdk.Abstractions/PluginHookCapability.cs:17-31`), which defines exactly
those two names and puts both in `Baseline` — the set the runtime treats as needing no
elevated consent — and both are read back under those exact strings by real consumers:
`PluginUiController.cs:370` and `PluginCronRegistrar.cs:80`. See the original note for the
full trace; nothing there has changed.

## Automix, not a hypothetical

`nomercy-automix-plugin`'s manifest — the platform author's own ABI 12.1 reference plugin,
the one the design spec behind this branch used throughout as "what following the SDK looks
like" — declares exactly these two hooks:

```json
// nomercy-automix-plugin, origin/master@bd198a1502af646913c7732223c04c827f61459a,
// src/NoMercy.Plugin.Automix/plugin.json
"capabilities": {
  "hooks": ["ui", "scheduledTask", "audioTools", "derivedAudio", "musicAnalysisWrite"],
  ...
}
```

Run against `ManifestScan`, this manifest gets two `Blocked` findings — one for `"ui"`, one
for `"scheduledTask"` — the exact two hooks a UI plugin with a scheduled refresh job cannot
avoid declaring. This plugin's own manifest declares the same two
(`src/NoMercy.Plugin.InternetRadio/plugin.json:19`,
`"hooks": ["ui", "scheduledTask", "media.proxy"]`) and would be Blocked the same way; it has
only escaped this in practice because `nomercy-plugin verify` is not part of this
repository's own CI (`.forgejo/workflows/build.yml` never invokes it).

## Reproduction

1. `manifest.Capabilities.Hooks = ["ui", "scheduledTask"]`, otherwise a valid manifest (both
   Automix's real one and this plugin's real one qualify as-is).
2. Run `nomercy-plugin verify` (or call `ManifestScan.Run` directly).
3. Observed: two `Blocked` `PluginRefusal`s, one per hook, each saying the name is "not a
   capability this server knows."
4. Expected: no finding for either — both are declared, real, baseline hooks the runtime
   requires for a UI plugin with a schedule.

## What it costs

Every UI plugin with a scheduled job — which per `PluginHookCapability.Baseline`'s own doc
comment is meant to be the *unremarkable* case — fails `nomercy-plugin verify` before
publishing, including the platform author's own reference plugin. A tool meant to catch real
problems before the marketplace does currently rejects the two most ordinary declarations a
plugin author can make, on wording ("not a capability this server knows") that sends them
toward editing the wrong list.

## Proposed fix (proposal, not decided)

Same as the original finding: validate `manifest.Capabilities.Hooks` against
`PluginHookCapability`'s known set (`Baseline` and `Elevated` together), not against
`PluginCapabilityVocabulary`. Worth doing before this branch's own manifest — or Automix's —
is ever run through `verify` in anger.
