# NMP0016 tells plugins to move to context.Scheduler, which throws on the in-process host

**Repo:** `nomercy-media-server`
**Found:** 2026-09-22, against `dev` at `f217a0f365e407f831acb9d35bfcfa60367db6dd`.
**Raised:** not yet. `gh issue list -R NoMercy-Entertainment/nomercy-media-server
--state all --search "NMP0016" / "context.Scheduler FacadeNotOnThisHost" / "Scheduler
in-process"` — no results.

> **Status (2026-09-23):** still open upstream; this plugin does not trigger it today, by
> declining NMP0016's own advice rather than because the defect was fixed. It still
> implements `IScheduledTaskPlugin` (`InternetRadioPlugin.cs:21`,
> `public sealed class InternetRadioPlugin : IUiPlugin, IScheduledTaskPlugin`) and never
> calls `context.Scheduler` (`grep -rn "context.Scheduler" src/NoMercy.Plugin.InternetRadio`
> — no hits). The "what was right" audit named this deliberate. NMP0016 still fires on the
> obsolete interface every build; following it would break this plugin's scheduling on the
> in-process host, which is exactly what it runs on.

## What happens

The plugin analyzer's NMP0016 diagnostic tells authors:

```csharp
// src/NoMercy.PluginSdk.Analyzers/Generated/PluginAnalyzerDescriptors.cs:48
new("NMP0016", "IScheduledTaskPlugin is obsolete. Register jobs through context.Scheduler.", "scheduler"),
```

`IPluginContext.Scheduler` is a default interface member that throws unless the concrete
context implementation overrides it:

```csharp
// src/NoMercy.PluginSdk.Abstractions/IPluginContext.cs:253-258
IPluginScheduler Scheduler =>
    throw new PluginRefusedException(
        PluginRefusalMessages.FacadeNotOnThisHost(
            PluginId.ToString(),
            "IPluginContext.Scheduler"
        )
    );
```

`src/NoMercy.PluginHost/RemotePluginContext.cs:105` — the context used when a plugin runs
**out of process** — overrides it with a real implementation. `src/NoMercy.Plugins/
PluginContext.cs` — the context used when a plugin runs **in process** (`public class
PluginContext : IPluginContext`, line 23; not `partial`, no other declaration exists) —
does not override `Scheduler` at all, alongside several other facades
(`Settings`, `Cast`, `Marketplace`, `WebView`, `Browser`, `User`, `Users` are likewise
absent from its property list). So `context.Scheduler` on the in-process host falls
through to the interface default and throws `FacadeNotOnThisHost` unconditionally.

Per the related finding on `IPluginRemoteLoader`
(`2026-09-22-...` — see report), the in-process host is not a rare fallback: it is what
runs whenever no `NoMercy.PluginHost` executable is available beside the running server
binary.

## Read, not reproduced

I did not start a server or load a plugin. This is a static trace: the default interface
member's throwing body, and the one in-process context class that never overrides it.

## Reproduction (as written)

1. Take any plugin migrated off `IScheduledTaskPlugin` per NMP0016's own instruction,
   calling `context.Scheduler.Register(...)` (or any `IPluginScheduler` member) from its
   `Plugin` implementation.
2. Load it on a host where `PluginContext` (not `RemotePluginContext`) constructs the
   plugin's context — i.e. any install with no `NoMercy.PluginHost` beside the server.
3. Expected: `PluginRefusedException` with code `FacadeNotOnThisHost` on the first call to
   `context.Scheduler`.

## What it costs

A plugin author who follows the analyzer's own guidance — migrate off the marked-obsolete
`IScheduledTaskPlugin` onto `context.Scheduler` — ends up with a plugin that cannot
register a scheduled job at all on whichever host does not run it out of process, which per
the sibling finding is the common case. The diagnostic doesn't just fail to help; it
actively directs authors at the facade least likely to work for them today.

## Proposed fix (proposal, not decided)

Give `src/NoMercy.Plugins/PluginContext.cs` a real `Scheduler` implementation backed by the
in-process host's own job/cron system (the same one `PluginCronRegistrar` already talks to
for the `scheduledTask` hook), so `context.Scheduler` works wherever the hook itself does.
Until that lands, NMP0016's message and `IScheduledTaskPlugin`'s own obsolete-attribute text
should say so, rather than pointing at a facade that throws on the host most plugins run on.
