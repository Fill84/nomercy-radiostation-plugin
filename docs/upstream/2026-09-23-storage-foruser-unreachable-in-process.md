# `IPluginStorage.ForUser` throws unconditionally on the in-process host

**Repo:** `nomercy-media-server`
**Found:** 2026-09-23, against `dev` at `4c16c0d09f4bcc2fa3a39ad2e791f52441cdd69b` (fetched
today).
**Raised:** not yet. `gh issue list -R NoMercy-Entertainment/nomercy-media-server --state all
--search "ForUser UserScopeRequired" / "PluginHostStorage userId" / "in-process storage
user"` — no results.

## What happens

`IPluginStorage.ForUser` is the per-caller storage scope a plugin uses to keep data that
belongs to one user rather than the server as a whole — the facade this plugin would need
for per-user favourites without keying its own file by user id. On the host's real
implementation, it answers or refuses based on whether a user id was supplied at
construction:

```csharp
// src/NoMercy.Plugins/Storage/PluginHostStorage.cs:112-120
public IPluginUserScope ForUser =>
    _userId is { } user
        ? new PluginUserDataScope(_pluginId, user, _databaseRoot)
        : throw new PluginRefusedException(
            PluginRefusalMessages.UserScopeRequired(
                _pluginId.ToString(),
                "IPluginStorage.ForUser"
            )
        );
```

`_userId` is an optional constructor parameter (`UserId? userId = null`,
`PluginHostStorage.cs:38`) — nothing requires a caller to supply one. The in-process host
never does. The one place `PluginHostStorage` is constructed for a live plugin is
`PluginContextFactory.HostStorage`:

```csharp
// src/NoMercy.Plugins/PluginContextFactory.cs:243-246
private IPluginStorage? HostStorage(Ulid pluginId) =>
    pluginsRoot is null || folderCatalog is null
        ? null
        : new PluginHostStorage(pluginId, pluginsRoot, folderCatalog, grantStore, quotas);
```

— five arguments, no `userId`. And `PluginContextFactory.Create`
(`PluginContextFactory.cs:57-64`), the one method that builds a plugin's context for every
request the in-process host serves, takes no caller/user id parameter at all: there is no
value available at this call site to thread through even if `HostStorage` wanted to. So
`context.Storage.ForUser` throws `PLUGIN_USER_SCOPE_REQUIRED` on every call, for every
plugin, on the in-process host — not sometimes, not for requests with no signed-in caller,
always.

## Read, not reproduced

I did not start a server or load a plugin. This is a static trace of the one constructor
call site for `PluginHostStorage` against the one property that reads `_userId`, confirming
no path threads a user id through on the in-process host.

## Reproduction (as written)

1. From any plugin running in-process (no `NoMercy.PluginHost` beside the server — the
   common case per the sibling finding on `IPluginRemoteLoader`), call
   `context.Storage.ForUser` from any request, signed-in caller or not.
2. Expected: `PluginRefusedException` with code `PLUGIN_USER_SCOPE_REQUIRED`, every time.

## What it costs

A plugin that wants a per-user store — the SDK's own sanctioned answer to "this data belongs
to one listener, not the server" — cannot have one on the host most plugins run on. This
plugin needed exactly that for per-user favourites and worked around it rather than use a
facade that always throws: it keys its own file by user id inside its private storage scope
instead of using `ForUser` at all. The design spec behind this branch records the same
finding as a follow-up, not fixed here, because fixing it means changing what the in-process
host passes into every plugin's context — out of scope for a documentation-only pass.

## Proposed fix (proposal, not decided)

Give `PluginContextFactory.Create` a way to receive the calling user's id per request (the
in-process host already knows it for a UI request — `PluginUiController` resolves
`User.UserId()` before building the plugin's `PluginCaller`) and thread it into
`HostStorage(pluginId, userId)` so `_userId` is populated whenever a request actually has a
caller, leaving the refusal for the genuinely caller-less case (a scheduled job) that the
property's own doc comment already describes as the honest answer.
