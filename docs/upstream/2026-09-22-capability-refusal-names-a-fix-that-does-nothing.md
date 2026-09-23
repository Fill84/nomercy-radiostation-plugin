# The media.proxy refusal tells a plugin author to edit plugin.json; that edit does nothing

**Repo:** `nomercy-media-server`
**Found:** 2026-09-22, against `dev` at `f217a0f365e407f831acb9d35bfcfa60367db6dd`.
**Confirmed in production**, not just read: beast-unit, NoMercy MediaServer v0.1.526, today
(2026-09-22) — see the exact logged line below.
**Raised:** not yet. `gh issue list -R NoMercy-Entertainment/nomercy-media-server
--state all --search "media.proxy scope" / "CapabilityScopeRefused" / "ScopesFor" /
"streamtheworld"` — no results.

> **Status (2026-09-23):** still open upstream, and still reachable here. This plugin no
> longer walks an owner into it the way it did: `StationGrants.EnsureAsync`
> (`src/NoMercy.Plugin.InternetRadio/Hosting/StationGrants.cs:47-51`) asks for `media.proxy`
> from the refresh job, before any station page is built, with our own accurate reason —
> so in the ordinary case an owner sees our request and grants it before a mint is ever
> refused. But the fallback is unchanged: `HostStationStreams.Log`
> (`src/NoMercy.Plugin.InternetRadio/Playback/HostStationStreams.cs:106-107`) still logs
> the refusal's own `{Fix}` text verbatim — the same misleading "add it to plugin.json"
> line — whenever a mint is refused before the owner has acted (or after they revoke), so
> the defect is less likely to be hit, not eliminated.

## What happens

Playing an Internet Radio station on beast-unit today logs:

```
Internet Radio cannot play stations yet: PLUGIN_CAPABILITY_SCOPE_REFUSED. The plugin
used media.proxy on playerservices.streamtheworld.com, which the owner did not grant.
Add playerservices.streamtheworld.com to the media.proxy entry in plugin.json and
publish…
```

That text is generated unconditionally by `CapabilityScopeRefused`, for every capability:

```csharp
// src/NoMercy.PluginSdk.Abstractions/PluginRefusalMessages.cs:459-473
public static PluginRefusal CapabilityScopeRefused(string plugin, string capability, string scope)
{
    ...
    return new PluginRefusal(
        PluginRefusalCodes.CapabilityScopeRefused,
        plugin,
        $"The plugin used {capability} on {scope}, which the owner did not grant.",
        $"{capability} is declared and granted, but not for {scope}. ...",
        $"Add {scope} to the {capability} entry in plugin.json and publish the new version, so the owner is asked about it. Docs: ...",
        PluginRefusalSeverity.Blocked
    );
}
```

But whether adding a host to the manifest can ever satisfy the check depends on
`PluginCapabilityBroker.ScopesFor`, and for `media.proxy` it cannot:

```csharp
// src/NoMercy.Plugins/Capabilities/PluginCapabilityBroker.cs:135-146
private static IReadOnlyList<string> ScopesFor(PluginCapabilities capabilities, string capability)
{
    if (capability == PluginCapabilityNames.NetworkDiscover)
        return capabilities.Network?.Protocols ?? [];

    return capability.StartsWith("network.", StringComparison.Ordinal)
        ? capabilities.Network?.Hosts ?? []
        : [];
}
```

For any capability that is not `network.discover` or does not start with `"network."` —
`media.proxy` included — `ScopesFor` always returns `[]`. Back in `Check()`:

```csharp
// PluginCapabilityBroker.cs:113-120
scopeDeclared = PluginScopeGlob.AnyMatches(ScopesFor(capabilities, capability), scopeValue);
```

`AnyMatches([], scopeValue)` is always false, so `scopeDeclared` is always false for
`media.proxy`, **no matter what hosts the manifest's `media.proxy` entry lists**. The only
way `Check()` can succeed for a `media.proxy` scope is the runtime grant path:

```csharp
// PluginCapabilityBroker.cs:79-88
if (scopeValue is not null && !scopeDeclared
    && !grants.Holds(pluginId, PluginGrantKind.ForCapability(capability), scopeValue))
    return Refuse(pluginId, PluginRefusalMessages.CapabilityScopeRefused(who, capability, scopeValue));
```

— an owner-approved grant, not a manifest edit. The comment directly above
`CapabilityScopeRefused`'s own definition names this exact trap: "Reported as one code, an
author edits the manifest and wonders why nothing changed" — describing the *intended*
distinction between "not declared" and "not granted," while the message text itself still
tells every reader, regardless of which capability triggered it, to edit the manifest.

## Reproduction

1. A plugin declares `media.proxy` with any host list in its manifest, and asks for a host
   the owner has not separately granted at runtime.
2. `PluginCapabilityBroker.Check()` refuses with `CapabilityScopeRefused`.
3. The refusal's `Fix` text says to add the host to the manifest's `media.proxy` entry and
   publish a new version.
4. Doing exactly that changes nothing: `ScopesFor("media.proxy", ...)` still returns `[]`,
   `scopeDeclared` is still false, and the plugin is refused again on the republished
   version — reproduced live on beast-unit with the log line quoted above.

## What it costs

A plugin author who trusts the refusal's own stated fix ships a new version, and it still
does not work — for the exact reason the surrounding code comments already anticipated but
the message text does not reflect. For this plugin specifically, this blocked playback in
production until the real route (an owner-approved runtime grant through the dashboard's
capability/grant flow) was found by reading the broker's source rather than the message.

## Proposed fix (proposal, not decided)

Two independent fixes, either sufficient on its own:
- Make `CapabilityScopeRefused`'s `Fix` text conditional on whether the capability is one
  `ScopesFor` can ever satisfy from the manifest (i.e. `network.*`/`network.discover`) —
  for everything else, say to ask the owner to grant it at runtime instead of naming a
  manifest edit.
- Or extend `ScopesFor` to read `media.proxy`'s own host list from the manifest (it already
  has one — this plugin's `plugin.json` declares `media.proxy` hosts), so the documented
  manifest-edit route actually works for it, the same way it does for `network.*`.
