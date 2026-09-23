# A form submitted through the view posts neither `surface` nor `input`

**Repo:** `nomercy-app-web`
**Found:** 2026-09-22, against `master` at `c821ca0a5817ae4483629954638e9d0ae6cd3e09`.
**Raised:** not yet. `gh issue list -R NoMercy-Entertainment/nomercy-app-web --state all
--search "postView surface" / "form submit route" / "view surface input"` — no results.

> **Status (2026-09-23):** still open, and still latent for this plugin specifically. Tasks
> 1-6 moved this plugin's whole view layer onto `PluginViews` and gave it a real route
> table, but nothing in that work reads `Surface` anywhere
> (`grep -rn "Surface" src/NoMercy.Plugin.InternetRadio` — no hits outside this note). The
> one `PluginViews.Form` this plugin builds (`SettingsView.cs`) still does not branch on it,
> so a TV or phone submitting that form still silently gets the desktop shape back — the
> cost remains real for any plugin that does branch, and unchanged for this one.

## What happens

The initial GET for a plugin's view sends three query parameters:

```js
// src/views/Plugins/PluginScreen.vue:238
`plugins/${pluginId.value}/view?route=${encodeURIComponent(pluginRoute.value)}&surface=${surface}&input=${currentInputDevice()}`,
```

with a comment right above it explaining why: "Beside the surface, not instead of it: the
surface says where the app runs and this says how it is reached."

Every POST back through the same view — a form submit (`PluginActionType.SubmitForm`) and a
file/folder pick (`PluginActionType.PickFile`/`PickFolder`) — goes through one shared
helper that sends only `route`:

```js
// src/lib/plugin/actionInterpreter.ts:50-59
async function postView(
    context: PluginActionContext,
    route: string,
    body: { action: string; values: Record<string, unknown> },
): Promise<void> {
    const { data } = await serverClient()
        .post<{ data: unknown }>(`plugins/${context.pluginId}/view?route=${encodeURIComponent(route)}`, body);
    ...
}
```

No `&surface=`, no `&input=`. The POST body itself is shaped correctly and matches the
server's `PluginViewInputDto` (`values`/`action`,
`nomercy-media-server` `src/NoMercy.Api/DTOs/Dashboard/PluginViewInputDto.cs`) — this is
specifically the two query parameters, not the body.

Server-side, `PluginUiController.Submit` reads `surface` from the query with the same
`PluginSurface.IsKnown(surface) ? surface! : PluginSurface.Web` fallback the GET path uses
(`PluginUiController.cs`, `Render`), so an omitted `surface` silently becomes `"web"" for
every submission — including ones made from a TV or a phone.

## Reproduction (read, not reproduced against a live server)

1. Open a plugin's page on any non-web surface (TV, phone) so the initial GET's `surface`
   query differs from the default.
2. Submit a form on that page, or pick a file/folder.
3. Inspect the network request: `POST plugins/<id>/view?route=<route>` — no `surface`, no
   `input`.
4. Expected impact: a plugin whose view branches on `request.Caller.Surface` inside its
   form-submission handler sees `"web"` regardless of the real surface, and a plugin that
   would branch on the input device (mouse vs. D-pad) for the next screen it returns has
   nothing to branch on.

## What it costs

For a plugin that renders differently per surface (the SDK's own justification for sending
`surface` at all — "some views differ... a grid of posters on a TV where the desktop shows
a table"), the page returned by a form submission on a TV or phone can silently revert to
the desktop/web shape, because the server was never told otherwise. This plugin's forms
happen not to branch on surface today, so the practical cost here is currently latent, not
a symptom users can see yet — but the gap is in code every plugin's `submitForm` and
`pickFile`/`pickFolder` handlers go through.

## Proposed fix (proposal, not decided)

Have `postView()` accept and forward the same `surface`/`input` values `PluginScreen.vue`
already computes and passes to the GET, e.g. pass them down through `PluginActionContext`
so `actionInterpreter.ts` does not need to re-derive them, and append both query parameters
in `postView()` exactly as the GET does.
