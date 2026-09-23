# Capability grants do not glob; `PluginGrantKind.NetworkHost`'s own doc comment says they do

**Repo:** `nomercy-media-server`
**Found:** 2026-09-23, against `dev` at `4c16c0d09f4bcc2fa3a39ad2e791f52441cdd69b` (fetched
today; `PluginGrantStore.cs` and `PluginGrantKind.cs` unchanged since the earlier audits).
**Raised:** not yet. `gh issue list -R NoMercy-Entertainment/nomercy-media-server --state all
--search "ConfigPluginGrantStore glob" / "NetworkHost pattern" / "grant Holds wildcard"` —
no results.

## What happens

`PluginGrantKind.NetworkHost`'s doc comment tells a reader what kind of value this grant
takes:

```csharp
// src/NoMercy.PluginSdk.Abstractions/PluginGrantKind.cs:37-40
/// <summary>
/// One outbound host, granted after install because the manifest could not
/// know it. The value is a host pattern in the same glob the manifest uses.
/// </summary>
public const string NetworkHost = "network.host";
```

"The same glob the manifest uses" is `PluginScopeGlob`, which compiles `*` to `[^.]+` per
label — so a manifest-declared `*.api.radio-browser.info` matches `de1.api.radio-browser.info`
and `all.api.radio-browser.info` alike. But a runtime grant recorded through
`ConfigPluginGrantStore` is never checked with that glob. `Holds` does exact string equality
or a literal `"*"`:

```csharp
// src/NoMercy.Plugins/Capabilities/PluginGrantStore.cs:98-106
public bool Holds(Ulid pluginId, string kind, string value)
{
    IReadOnlyList<string> granted = Granted(pluginId, kind);

    return granted.Any(entry =>
        entry == PluginGrant.Everything
        || string.Equals(entry, value, StringComparison.OrdinalIgnoreCase)
    );
}
```

`PluginGrant.Everything` is the literal `"*"` (`PluginGrantKind.cs:75`) — matched as a whole
value, not compiled as a pattern. So an owner who reads the doc comment and grants
`*.example.com`, expecting it to behave like the manifest's own glob, gets a grant that
matches nothing except the literal string `"*.example.com"` — no host under that domain is
ever held, and `Holds` never says why.

## Confirmed by this plugin's own workaround

This plugin ran into exactly this gap and worked around it rather than trusting the doc
comment, with the reasoning recorded in its own source:

```csharp
// src/NoMercy.Plugin.InternetRadio/Hosting/StationGrants.cs:53-57
// Asked as PluginGrant.Everything, not per host: ConfigPluginGrantStore.Holds
// compares a grant exactly or against "*" and does not glob, so a per-host
// grant would have to be given station by station, forever, as the catalogue
// grew. A wildcard asked once is the only version of this an owner would ever
// actually approve.
await AskUnlessHeldAsync(
    PluginGrantKind.NetworkHost, PluginGrant.Everything, StationsReason, ct);
```

This plugin needs the equivalent of "any station host" — thousands of servers, unknowable at
package time — and the only value of `NetworkHost` that actually works today is the literal
`"*"`, which grants every host rather than the pattern an owner might reasonably expect to
scope. The doc comment's promise of manifest-style globbing was checked against the runtime
before this code was written, found false, and designed around.

## Reproduction

1. As the owner, grant `PluginGrantKind.NetworkHost` with value `*.example.com` for a plugin.
2. That plugin calls a facade that checks `grants.Holds(pluginId, PluginGrantKind.NetworkHost, "api.example.com")`.
3. Observed: `Holds` returns `false` — `"api.example.com"` does not equal `"*.example.com"`
   and is not the literal `"*"`.
4. Expected, per the doc comment on `NetworkHost`: a match, the same way the manifest's own
   `*.example.com` would match `api.example.com`.

## What it costs

An owner who grants a specific pattern, trusting the SDK's own description of how that grant
behaves, gets a grant that silently protects nothing the pattern was meant to cover — the
plugin keeps asking, or keeps failing, with no error pointing at the mismatch. The one way to
get a working per-domain-family grant today is the wildcard `"*"`, which is a strictly wider
grant than the pattern the owner thought they were giving.

## Proposed fix (proposal, not decided)

Either make `ConfigPluginGrantStore.Holds` compile stored `NetworkHost` values through
`PluginScopeGlob`, the same compiler the manifest's own hosts already use — so the doc
comment becomes true — or correct the doc comment to say a grant is compared exactly (or as
`"*"` for everything), and say plainly that a partial pattern is not supported today.
