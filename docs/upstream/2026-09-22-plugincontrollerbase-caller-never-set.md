# PluginControllerBase.Caller always throws: the host never populates it

**Repo:** `nomercy-media-server`
**Found:** 2026-09-22, against `dev` at `f217a0f365e407f831acb9d35bfcfa60367db6dd`.
**Raised:** not yet. `gh issue list -R NoMercy-Entertainment/nomercy-media-server
--state all --search "CallerItemKey" / "RouteAccessDenied" / "PluginControllerBase Caller"`
— the only hit (#66) is an unrelated plugin-template compile error.

## What happens

A plugin's REST controllers read who is calling through `PluginControllerBase.Caller`:

```csharp
// src/NoMercy.PluginSdk.Mvc/PluginControllerBase.cs:48-49
protected PluginCaller Caller =>
    HttpContext.Items[CallerItemKey] as PluginCaller
    ?? throw new PluginRefusedException(
        new PluginRefusal(
            PluginRefusalCodes.RouteAccessDenied,
            ...
```

with `CallerItemKey = "PluginCaller"` (line 62). Nothing in `nomercy-media-server` writes
to `HttpContext.Items["PluginCaller"]` (or the constant) anywhere:

```
$ git grep -n '\.Items\[' origin/dev -- '*.cs' | grep -iv test
.../ClientIpResolver.cs:43:    context.Items[ClientIpItemKey] = resolved;
.../PluginControllerBase.cs:49:    HttpContext.Items[CallerItemKey] as PluginCaller
```

— the only two `.Items[...]` writes/reads outside tests are an unrelated client-IP cache
and this read site itself. The one action filter that runs for every
`PluginControllerBase`-derived controller,
`src/NoMercy.Api/Plugins/PluginControllerCapabilityFilter.cs`, resolves access
(`accessResolver.Resolve(...)`) and either 404s, 403s, or calls `next()` — it never
constructs a `PluginCaller` or writes it to `HttpContext.Items` (read the whole file:
lines 44-85, no `PluginCaller` construction, no `.Items[` write). No other
`IAsyncActionFilter` for plugin controllers exists
(`git grep -n "IAsyncActionFilter" origin/dev -- 'src/NoMercy.Api/**/*.cs'` finds exactly
two: this one and `PluginQueryTokenFilter`, which is about ABI query tokens, not callers).

The only working `IPluginCallerAccessor`/`HttpPluginCallerAccessor` implementation in the
codebase is a *different* abstraction, read from `HttpContext.User` directly, used by
`PluginMediaProxy`/`PluginMediaFactory` — unrelated to `PluginControllerBase.Caller`.

So any REST controller that inherits `PluginControllerBase` and reads `this.Caller` throws
`PluginRefusedException(RouteAccessDenied, "...read Caller on a request the host did not
resolve a caller for.")` on every single request, unconditionally.

## Not caught by the test suite

No test anywhere exercises `PluginControllerBase.Caller` through
`HttpContext.Items[CallerItemKey]`:
`tests/NoMercy.Tests.Plugins/PluginCallerTests.cs` constructs `PluginCaller` records
directly and never touches `HttpContext.Items`; no other test file references
`CallerItemKey`.

## Read, not reproduced

I did not start a server (out of scope for this task). This is a static trace of every
write site to the exact `HttpContext.Items` key `PluginControllerBase.Caller` reads,
confirming there is none.

## What it costs

Any plugin whose REST controller (a `[HttpGet]`/`[HttpPost]` action on a
`PluginControllerBase` subclass — the pattern the SDK ships for a plugin's own API
surface, as distinct from its declarative UI views) reads `Caller` refuses every request
it serves, unconditionally, with a message that says "the host did not resolve a caller
for" a request that in fact came from a signed-in user the capability filter already
admitted one line earlier.

## Proposed fix (proposal, not decided)

Have `PluginControllerCapabilityFilter` (the one filter every `PluginControllerBase`
request already passes through, and which already resolves
`accessResolver.Resolve(pluginId, context.HttpContext.User.UserId())`) construct a
`PluginCaller` from that resolution — the same construction `PluginUiController.Render`
already performs for view requests — and write it to
`context.HttpContext.Items[PluginControllerBase.CallerItemKey]` before calling `next()`.
