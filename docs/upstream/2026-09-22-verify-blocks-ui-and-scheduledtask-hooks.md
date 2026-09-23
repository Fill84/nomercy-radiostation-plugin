# `nomercy-plugin verify` blocks the two hooks the runtime treats as baseline

**Repo:** `nomercy-media-server` (the `tools/nomercy-plugin` CLI and the SDK it ships
against)
**Found:** 2026-09-22, against `dev` at `f217a0f365e407f831acb9d35bfcfa60367db6dd`.
**Raised:** not yet. `gh issue list -R NoMercy-Entertainment/nomercy-media-server
--state all --search "nomercy-plugin verify hook" / "ManifestScan" / "verify blocks ui" /
"scheduledTask capability"` — no match (`#19` is an unrelated capability-gate feature).

> **Status (2026-09-23):** still open, and this plugin would be Blocked by it too, not just
> Automix. `plugin.json` declares `"hooks": ["ui", "scheduledTask", "media.proxy"]`
> (`src/NoMercy.Plugin.InternetRadio/plugin.json:19`) — the same two names this bug rejects.
> It has not bitten us in CI only because `.forgejo/workflows/build.yml` never runs
> `nomercy-plugin verify` at all (`grep -n verify .forgejo/workflows/build.yml` — one
> unrelated comment hit). See
> `2026-09-23-verify-rejects-the-hooks-the-runtime-requires.md` for the same defect
> re-verified today with Automix's own manifest as live proof that this is not a theoretical
> edge case.

## What happens

`nomercy-plugin verify`'s manifest scan validates every declared hook against the
capability vocabulary:

```csharp
// tools/nomercy-plugin/Verification/ManifestScan.cs:122-128
foreach (string hook in manifest.Capabilities?.Hooks ?? [])
    if (PluginCapabilityVocabulary.ByName(hook) is null)
        findings.Add(
            new PluginRefusal(
                PluginRefusalCodes.ManifestInvalid,
                who,
                $"The manifest declares '{hook}', which is not a capability this server knows.",
                ...
                PluginRefusalSeverity.Blocked
```

`PluginCapabilityVocabulary` (`src/NoMercy.PluginSdk.Abstractions/Generated/
PluginCapabilityVocabulary.cs`) is the **capability** list — dotted, scoped names like
`network.fetch`, `media.proxy`, `ui.mount`, `scheduler`. It contains no entry named
literally `"ui"` and no entry named literally `"scheduledTask"`.

The actual **hook** vocabulary — a different list, for a different field — is
`PluginHookCapability` (`src/NoMercy.PluginSdk.Abstractions/PluginHookCapability.cs:17-31`),
which defines exactly those two bare names as hooks a plugin declares, and puts both in its
`Baseline` set (hooks that need no elevated consent):

```csharp
public const string ScheduledTask = "scheduledTask";
public const string Ui = "ui";
...
public static IReadOnlySet<string> Baseline { get; } = new HashSet<string>(...)
{ MediaSource, Metadata, Ui, ScheduledTask };
```

and both are read back by real runtime consumers under exactly those strings:
`PluginUiController.cs:370`, `PluginCapabilityGuard.DeclaresHook(info.Capabilities,
PluginHookCapability.Ui)`, and `PluginCronRegistrar.cs:80`,
`PluginCapabilityGuard.DeclaresHook(capabilities, PluginHookCapability.ScheduledTask)`.

So `ManifestScan.cs` checks a manifest's `Hooks` list against the wrong vocabulary. Every
plugin that declares `"ui"` (to get a dashboard page at all) or `"scheduledTask"` (to run
on a schedule) — the exact two hooks the runtime classifies as baseline, harmless, and
common — gets a `Blocked`-severity finding from `nomercy-plugin verify` saying the name is
"not a capability this server knows," even though the server's own runtime recognises and
requires it.

## Reproduction

1. `manifest.Capabilities.Hooks = ["ui"]` (or `["scheduledTask"]`), otherwise valid.
2. Run `nomercy-plugin verify` (or call `ManifestScan` directly, as the unit-testable
   entry point).
3. Observed: a `Blocked` `PluginRefusal` with code `ManifestInvalid` naming `'ui'` (or
   `'scheduledTask'`) as not a known capability.
4. Expected: no finding — both are declared, real, baseline hooks.

## What it costs

Any UI plugin or scheduled-task plugin — which, per `PluginHookCapability.Baseline`'s own
doc comment, is meant to be the *unremarkable* case, requiring no owner prompt — fails
`nomercy-plugin verify` before it can be published or checked in CI, on grounds the
verifier's own findings text actively misdirects: "Use a name from the capability list" is
not the right fix, since these two are correctly hooks, not capabilities.

## Proposed fix (proposal, not decided)

Validate `manifest.Capabilities.Hooks` against `PluginHookCapability`'s known set (both
`Baseline` and `Elevated`, since either is a legitimate hook to declare), not against
`PluginCapabilityVocabulary`. The two lists serve genuinely different fields on the
manifest and should not be checked against each other's vocabulary.
