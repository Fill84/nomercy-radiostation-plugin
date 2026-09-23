# `PluginViews.Button` writes `Props["icon"]`; `NMButton` has no `icon` prop and never reads it

**Repos:** `nomercy-media-server` (the SDK builder that writes the prop) and
`nomercy-app-web` (the component that never reads it) — filing against `nomercy-media-server`
since the SDK's own builder is the one shipping a prop its target component cannot use.
**Found:** 2026-09-23, against `nomercy-media-server` `dev@4c16c0d09f4bcc2fa3a39ad2e791f52441cdd69b`
and `nomercy-app-web` `origin/master@00d7857ea2375a6de81f070d3a8a2faeaac26005` (both fetched
today).
**Confirmed against this plugin's own output**, not just read: found during this branch's
Task 4 (moving this plugin's views onto `PluginViews`), when every button on every rendered
page lost its icon — arrowLeft, gridMasonry, play, playlistAdd, portableRadio, home — the
moment buttons started going through `PluginViews.Button` instead of this plugin's old
hand-rolled builder.
**Raised:** not yet. `gh issue list -R NoMercy-Entertainment/nomercy-media-server --state all
--search "PluginViews.Button icon" / "NMButton icon prop"` and the same against
`nomercy-app-web` — no results.

## What happens

`PluginViews.Button` puts the icon on the props bag under the key `"icon"`:

```csharp
// nomercy-media-server, src/NoMercy.PluginSdk.Abstractions/PluginViews.cs:210-227
public static PluginComponent Button(
    string id, string label, PluginActionIntent action,
    string? icon = null, string? variant = null
) =>
    new()
    {
        Id = id,
        Component = PluginComponentType.Button,   // = "NMButton"
        Props = new()
        {
            ["ariaLabel"] = label,
            ["icon"] = icon,
            ...
```

`PluginComponentType.Button` is the literal string `"NMButton"`
(`PluginComponentType.cs:38`) — one of the client's own design-system tags, resolved by
`PluginNode.vue`'s design-system path straight to the real `NMButton.vue`, not through the
legacy `Plugin*` component map. `NMButton`'s props have no `icon` field at all:

```ts
// nomercy-app-web, src/components/nm/props.ts:264-281
export interface NMButtonProps extends NMComponentBase {
    ariaLabel?: string;
    disabled?: boolean;
    full?: boolean;
    iconOnly?: boolean;
    layout?: 'hug' | 'full';
    loading?: boolean;
    size?: 'sm' | 'md' | 'lg' | 'xl' | '2xl';
    variant?: ...;
}
```

`NMButton.vue` draws an icon only from two named slots, filled from a node's
`props.slots` map by `NMRender.vue`, never from a scalar prop:

```vue
<!-- NMButton.vue:82,91 -->
<slot name="iconLeft" />
...
<slot name="iconRight" />
```

So every `icon` a plugin author passes to `PluginViews.Button` is silently discarded — not
refused, not warned about, just absent from what the button draws.

## Reproduction

1. Build a view with `PluginViews.Button(id, label, action, icon: "play")`.
2. Render it on the current client.
3. Observed: a button with a label and no icon.
4. Expected, per the parameter's own name and the builder taking it at all: an icon beside
   the label.

This plugin hit exactly this on every page the moment Task 4 moved its buttons onto
`PluginViews.Button` — confirmed by this branch's own payload-emitting test fixtures, not a
constructed example.

## What it costs

Any plugin author who reaches for `PluginViews.Button`'s documented `icon` parameter — the
SDK's own sanctioned button builder — ships buttons with no icon, with nothing in the
response, the schema, or the client's console flagging the mismatch. This plugin worked
around it rather than wait: `IconButton.Build`
(`src/NoMercy.Plugin.InternetRadio/Views/IconButton.cs`) takes `PluginViews.Button`'s own
output, drops the inert `icon` prop, and fills `props.slots.iconLeft` with an `NMIcon` node
itself — the one path `NMRender` actually reads an icon from.

## Proposed fix (proposal, not decided)

Have `PluginViews.Button` fill `props.slots.iconLeft` with an `NMIcon` node itself, the same
way this plugin's `IconButton.Build` does now, so every plugin using the SDK's own builder
gets a working icon without each one having to discover and route around the gap
separately. Once that lands, this plugin's `IconButton` helper becomes dead code to delete.
