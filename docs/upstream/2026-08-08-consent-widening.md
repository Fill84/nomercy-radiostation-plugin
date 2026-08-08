# Widening a consented plugin's capabilities

**Question:** v1.1.0 adds `"rest": true` to a plugin already installed and already
approved on at least one server. Does an update whose manifest declares a capability the
stored grant does not cover re-prompt, inherit silently, or fail to load?

**Answer: it inherits silently, and nothing breaks.** Checked against
`nomercy-media-server` at `v0.1.470` (`9011e74`).

## Why

`PluginConsentService` records consent **per plugin id and nothing else**:

```csharp
public bool HasConsent(Ulid pluginId) => store.Contains(pluginId);
public void GrantConsent(Ulid pluginId) => store.Add(pluginId);
```

The store is a set of ids. It holds no record of which capabilities were approved, so
there is nothing for a widened manifest to fail to match.

And the plugin was never baseline to begin with:

```csharp
if (capabilities.Rest || capabilities.Ws || capabilities.Network is not null)
    return false;
```

`"network": { "hosts": ["*.api.radio-browser.info"] }` has been in this manifest since
1.0.2, so `IsBaseline` already returned false. `scheduledTask` is not in `BaselineHooks`
either, which would have done it on its own.

So the consent requirement is unchanged by this release: it was required before, it is
required after, and an install that granted it keeps it. The plugin id
(`5KTKRT4Z2Y9P59Y40W5CX4TQKF`) is what the record is keyed on and does not change.

**A server that already runs this plugin will keep running it after the update, with no
prompt and no manual step.**

## What this means for the release

Nothing blocks. The risk recorded in the design spec is closed.

One thing an owner should still be told, and the release notes say it: the plugin now
serves REST endpoints on the host, which it did not before. That is a real change in what
the plugin does, even though it is not a change in what the server asks about.

## Worth reporting upstream

Consent being id-only means **any** capability a plugin adds after approval is inherited
without being asked. An owner who approved this plugin for outbound access to
radio-browser has, by updating, also approved it to serve REST endpoints — and would have
approved `ws`, or a second network host, the same way.

That is defensible for a plugin from a trusted repository, where trust follows the
repository rather than the file (`586be1c`). It is less obviously right for one added from
elsewhere: the prompt an owner answered was about a narrower plugin than the one they end
up running, and nothing tells them it widened.

A capability fingerprint stored alongside the id would close it — consent recorded as "this
id, with this capability set", re-prompting when the set grows rather than when it merely
changes. Not this plugin's call to make, and not urgent, but it is the gap that made this
question worth asking.

Filed as an observation, not a defect: the current behaviour is a deliberate design that
favours updates not breaking, and this release depends on it.
