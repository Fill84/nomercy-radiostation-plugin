# PluginUiController.Render tells every plugin its caller is Owned

**Repo:** `nomercy-media-server`
**Found:** 2026-09-22, against `dev` at `f217a0f365e407f831acb9d35bfcfa60367db6dd`.
**Raised:** not yet. `gh issue list -R NoMercy-Entertainment/nomercy-media-server
--state all --search "PluginUiController Owned" / "PluginAccess.Owned hardcode" /
"caller access shared"` — no results.

## What happens

`PluginUiController.Render` resolves the caller's real access level before deciding
whether to serve the page at all:

```csharp
// src/NoMercy.Api/Controllers/V1/Dashboard/Plugins/PluginUiController.cs:261
if (AccessTo(id) == PluginAccess.None)
    return ForbiddenResponse(PluginAccessRefusal.For(id));
```

(`AccessTo` is `accessResolver.Resolve(pluginId, User.UserId())`, line 50.) But when it
then builds the `PluginCaller` the plugin's `GetViewAsync` actually receives, the access
field is not that resolution — it is a literal:

```csharp
// src/NoMercy.Api/Controllers/V1/Dashboard/Plugins/PluginUiController.cs:273-279
Caller = new PluginCaller(
    new UserId(new Ulid(User.UserId())),
    User.UserName(),
    User.Role() == "owner" ? PluginRole.Owner : PluginRole.Member,
    PluginAccess.Owned,
    ...
```

`PluginAccess` has three values: `Owned`, `Shared`, `None` (`PluginAccess.cs`). `None` is
already refused two lines above, so the only real values that can reach this constructor
are `Owned` and `Shared` — and every one of them is told `Owned`.

## What it costs

`PluginCaller.Access` is part of the wire contract a plugin's view-building code can read
(`PluginViewRequest.Caller`, serialized to JSON as `"owned"`/`"shared"`/`"none"`) — it is
the SDK's documented way for a plugin to tell "the server owner" apart from "someone the
owner shared this plugin with," e.g. to hide owner-only settings from a shared/family
account. Any plugin that branches on `caller.Access` to gate a UI section shows the
owner's view to every caller who passed the `None` check — a real over-exposure, not a
cosmetic mislabel, for any plugin (present or future) that trusts this field the way its
name and its enum's own doc comment ("What a caller may do with a plugin") say it can be
trusted.

## Read, not reproduced

I did not start a server. This is confirmed statically: `AccessTo(id)`'s result is read
once, for the `None` check, and never read again on this path — the value passed into
`PluginCaller`'s `Access` parameter is the literal `PluginAccess.Owned`.

## Reproduction (as written)

1. Grant a non-owner account `Shared` access to a UI plugin.
2. As that account, `GET /api/v{version}/plugins/{id}/view`.
3. Expected: the `PluginViewRequest.Caller.Access` the plugin receives reads `"owned"`
   rather than `"shared"`.

## Proposed fix (proposal, not decided)

Reuse the already-resolved `AccessTo(id)` value instead of the literal:

```csharp
PluginAccess access = AccessTo(id);
if (access == PluginAccess.None)
    return ForbiddenResponse(PluginAccessRefusal.For(id));
...
Caller = new PluginCaller(..., access, ...),
```

one resolution, read twice, instead of a second (wrong) answer hardcoded beside the first.
