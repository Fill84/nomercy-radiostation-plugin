# NMSearchInput in a plugin tree fires its action on click, and Enter reaches no one

**Repo:** `nomercy-app-web`
**Found:** 2026-09-22, against `master` at `c821ca0a5817ae4483629954638e9d0ae6cd3e09`.
**Raised:** not yet. `gh issue list -R NoMercy-Entertainment/nomercy-app-web --state all
--search "NMSearchInput" / "search input" / "NMRender interactive"` — no results.

## What happens

`NMSearchInput` is one of the fifty-six design-system components a plugin tree can use
(`src/components/nm/registry.ts:50,110`), drawn through the generic wrapper `NMRender.vue`
when a plugin node's `component` matches an `NM_REGISTRY` key
(`src/components/plugin/PluginNode.vue:38`, `isDesignSystem`).

`NMRender.vue` treats every design-system component the same way: if the node carries an
`action` and the component does not itself declare an `activate` emit, `NMRender` makes the
component's whole root element clickable and Enter/Space-able:

```js
// src/components/nm/NMRender.vue:30-37
const emitsActivate = computed<boolean>(() => { ... });
const interactive = computed<boolean>(() => !!action.value && !emitsActivate.value);
```
```vue
<!-- src/components/nm/NMRender.vue:117-119 -->
@activate="fire"
@click="interactive ? fire() : undefined"
@keydown="interactive ? onKey($event) : undefined"
```

`NMSearchInput.vue` declares `defineEmits<{ search: [string]; clear: [] }>()` —
`search`/`clear`, never `activate` — so `emitsActivate` is false for it. If a plugin's
search node carries an `action` (the natural way to tell the host what to call on submit),
`interactive` becomes true and `NMRender` wraps the search box's root `<div>` with a click
handler that fires the action immediately, and a keydown handler that does the same on
Enter or Space anywhere in the box — including while the caller is still typing.

Meanwhile `NMSearchInput.vue`'s own Enter handling:

```vue
<!-- src/components/nm/SearchInput/NMSearchInput.vue:54 -->
@keydown.enter="emit('search', value)"
```

emits a `search` event with the typed value — but `NMRender.vue`'s template only listens
for `@action` on children slots (`src/components/nm/NMRender.vue:130,140`); it never binds
`@search` on the resolved component. So this `search` event, the only place the typed
value travels, reaches no listener at all.

## Net effect

- Clicking anywhere in the search box (e.g. to focus it) fires the plugin's action before
  a single character is typed.
- Pressing Enter fires the action too, through `NMRender`'s own `onKey`/`fire()` — but with
  the static `action.value` read from `props.node.props.action`, which carries whatever the
  server declared at build time, not the text the caller typed.
- The one channel that does carry the typed text — `NMSearchInput`'s own `search` emit — is
  never listened for.

So a plugin cannot build a working search box out of `NMSearchInput` in its own tree today:
the box submits early, submits without the query, and never submits what it actually holds.

## Reproduction (read, not reproduced against a live server)

1. Send a plugin UI tree containing an `NMSearchInput` node with a non-null `action`.
2. Click anywhere inside the rendered search box.
3. Expected: the box's `action` fires immediately, with no `input`/`query` value attached,
   before any text is typed.
4. Type a query and press Enter.
5. Expected: the box's static `action` fires again (same payload as step 3); the typed
   value is never delivered anywhere.

## What it costs

Any plugin that wants a search box drawn with the design system's own `NMSearchInput`
(rather than reaching for its own free-text mechanism) gets a component that submits
prematurely and never carries the query. It pushes plugin authors toward the workaround
already documented for form fields in general
(`docs/upstream/2026-08-08-plugin-form-submits-empty-body.md`): a fixed set of buttons
instead of free text.

## Proposed fix (proposal, not decided)

`NMRender.vue`'s `interactive` wrapping is right for a card or a button, wrong for an input
that already has its own submit semantics. Either:
- exempt components that declare a `search`/`submit`-shaped emit (not just `activate`) from
  the click/keydown wrapping, and forward that emit as the `action` payload's `input` field,
  or
- give `NMSearchInput` its own `activate` emit fired only from its existing
  `@keydown.enter` handler (never from a bare click on the container), carrying `value`.

Either way, the action a search box fires needs to carry what was typed — today it never
does.
