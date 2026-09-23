# `PluginViews.Button` always sends `ariaLabel`; `NMButton` only ever reads it when there is no visible label

**Repo:** `nomercy-media-server` (the SDK builder) and `nomercy-app-web` (the component that
conditionally ignores the value) — filing against `nomercy-media-server`, whose builder sends
a value unconditionally that the target component only reads sometimes.
**Found:** 2026-09-23, against `nomercy-media-server` `dev@4c16c0d09f4bcc2fa3a39ad2e791f52441cdd69b`
and `nomercy-app-web` `origin/master@00d7857ea2375a6de81f070d3a8a2faeaac26005` (both fetched
today).
**Related but separate from** `2026-09-23-button-icon-prop-nothing-renders.md`: that note is
a visible-breakage defect (an icon an author asked for never appears); this one is a
dead-payload defect (a field that is sent, accepted by the component's own prop type, and
then never read for a plugin-built button) — kept apart because they have different fixes
and different costs, and folding them would make the icon fix look like it also touches
accessibility wiring, which it does not.

## What happens

`PluginViews.Button` sets `ariaLabel` to the button's label on every call, unconditionally:

```csharp
// nomercy-media-server, src/NoMercy.PluginSdk.Abstractions/PluginViews.cs:210-227,238
public static PluginComponent Button(
    string id, string label, PluginActionIntent action,
    string? icon = null, string? variant = null
) =>
    new()
    {
        ...
        Props = new()
        {
            ["ariaLabel"] = label,
            ...
        },
        Items = [Leaf($"{id}-label", label)],   // always fills the default slot
        Action = action,
    };
```

Every button `PluginViews.Button` builds also fills `Items` with a `Leaf` carrying the same
label — the node always has default-slot content. `NMButtonProps.ariaLabel` exists and is
typed correctly (`src/components/nm/props.ts:266`, documented "Accessible label for
icon-only buttons"), but `NMButton.vue` only ever applies it when the button *needs* a name
of its own:

```vue
<!-- nomercy-app-web, src/components/nm/Button/NMButton.vue:55-56,76 -->
const label = computed(() => props.accessibility?.label ?? props.ariaLabel ?? undefined);
const needsName = computed(() => props.iconOnly === true || !slots.default);
...
:aria-label="needsName ? label : undefined"
```

`PluginViews.Button` never sets `iconOnly`, and its `Items` always populate the default
slot, so `needsName` is `false` for every button it builds. `aria-label` is never rendered
for a `PluginViews`-built button — the visible text itself is what a screen reader
announces, which is correct, but `ariaLabel` was sent for nothing.

## Reproduction

1. Build a view with `PluginViews.Button(id, "Play", action)`.
2. Inspect the rendered `<button>`.
3. Observed: no `aria-label` attribute (`needsName` is false; the visible "Play" text is the
   accessible name via the default slot).
4. Observed on the wire: the payload still carries `"ariaLabel": "Play"` — sent, accepted by
   the client's own prop type, silently dropped at render.

## What it costs

Nothing breaks — the accessible name is already correct through the visible label. This
plugin's own Task 5 review is a documented instance of the confusion this causes: a fix
round initially left several button labels untranslated on the (false) premise that
`ariaLabel` was the accessible name and therefore the visible label's translation did not
matter, which the review had to correct and re-key
(`.superpowers/sdd/2026-09-23-sdk-conformance/progress.md`, Task 5's first ruling: "NMButton
emits aria-label only when `needsName` ... and PluginViews.Button always fills the default
slot, so the accessible name is the visible resolved text; the premise for skipping them is
false"). The `ariaLabel` payload itself remains genuinely inert: bytes on every button
response that do nothing, and a name that reads as "the accessible label" to the next author
who assumes setting it changes what a screen reader announces for a labelled button. It does
not, for any button built through `PluginViews.Button`.

## Proposed fix (proposal, not decided)

Either have `PluginViews.Button` only set `ariaLabel` when it is actually needed (i.e. leave
it null when the button also has visible text, matching `NMButtonProps.ariaLabel`'s own
documented purpose of naming icon-only buttons), or document on `PluginViews.Button` itself
that `ariaLabel` is currently inert for a labelled button, so the next author does not read
its presence as meaningful.
