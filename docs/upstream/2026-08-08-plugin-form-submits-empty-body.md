# A PluginViews.Form submit posts an empty body

**Where:** app.nomercy.tv, plugin UI host · observed 2026-08-08 against server v0.1.470
**Effect:** a plugin cannot receive anything a viewer types. For this plugin that is
search, which is the only way to reach a station outside the seventeen-tag discovery
sweep — some fifty thousand of them.

## What happens

`PluginViews.Form(id, "Search", CallPlugin("search"), field)` renders correctly: the label,
the input and the submit button all appear, and the input holds what the viewer types (the
a11y tree shows `textbox "Search every station on radio-browser": tomorrowland`).

On submit, the plugin's endpoint is reached — the route resolves, the user claim is
present, the action runs. The body is:

```
{}
```

Every time. No field, under any name.

## What was ruled out, and how

The plugin logs the raw body on every submit that yields nothing, which is what finally
made this visible. Four plugin-side changes were made and the body was `{}` after each:

1. `[FromBody]` bound to a positional record — `record SearchRequest(string? Query)`.
2. `[FromBody]` bound to a class with init-only properties, copied from the torrent
   plugin's `SaveSettingsRequest`, which **does** deliver its values in production.
3. No model at all: the body read raw and searched for the field at the top level and one
   level down, case-insensitively, with a form-encoded fallback.
4. The field given `Value = ""` instead of `null` and `Required = true` — the only two
   differences from the torrent plugin's working fields.

So this is not model binding, not casing, not the content type, and not the field's
initial value. The request carries no fields.

## The likely cause: the form renders as a button

The accessibility tree for the rendered form is:

```
button "Station name tomorrowland Search"
  text     "Station name"
  textbox  "Search every station on radio-browser": tomorrowland
  button   "Search"
```

The form's container has role `button`, with the submit button nested inside it. So a
`PluginComponentType.Form` is not rendered as a `<form>` element at all, and a button inside
a button is invalid HTML besides.

That accounts for the empty body exactly: with no form element, whatever the submit handler
collects its values from — `new FormData(formEl)`, a ref, a registered field set — has
nothing registered against it, so it posts an empty object. It also explains why the field
still displays and holds text: rendering the input is independent of collecting it.

Worth checking whether the outer role is deliberate (the whole card clickable) or a fallback
for an unhandled component type.

## Why the torrent plugin works and this does not

Not established, and worth knowing. Both use the same factory. The differences left are:

- Its method strings are PascalCase (`SaveSettings`); this plugin's is `search`. A method
  name is interpolated into the request path and should have no bearing on the body.
- Its forms carry six to nine fields; this one carries a single field.
- Its forms sit on a settings page reached through a nav mount; this one sits on the
  plugin's landing page.

A single-field form is the most suspicious of the three, and the cheapest for someone with
the client source to confirm.

## What a plugin can and cannot work around

A button's method path **is** delivered reliably — that is how this plugin's favourite
toggle works, and how the torrent plugin passes an indexer's index (`SaveIndexer/{index}`).
So anything a plugin can express as a fixed set of choices still works.

Free text does not. There is no other way for a plugin to receive a typed string: a form
submit is the only mechanism the contract offers, and it arrives empty. Search stays broken
until this is fixed in the client.

## Reproduction

1. Render `PluginViews.Form` with one `PluginFormFieldType.Text` field.
2. Type into it and submit.
3. Read the request body in the plugin's endpoint.

Observed: `{}`. Expected: the field, keyed by its `Name`.
