# A multi-segment plugin route arrives comma-joined

**Server:** `nomercy-media-server` v0.1.470 · **Client:** app.nomercy.tv, 2026-08-08

## What happens

A plugin route with more than one segment reaches `IUiPlugin.GetViewAsync` with its
segments joined by a comma instead of a slash.

Navigating to `/plugins/{id}/genre/ambient` — by clicking the plugin's own
`PluginActionIntent.Navigate("/genre/ambient")`, or by loading the URL directly — gives the
plugin:

```
PluginViewRequest.Route == "/genre,ambient"
```

Single-segment routes are unaffected: `/all` and `/settings` arrive as written.

## Why it matters

Every page a plugin serves below one segment is unreachable. For this plugin that was
every genre page and every station detail page — half the navigation — and the failure is
silent: the plugin sees a route it does not recognise and renders its own "no such page"
state, so it looks like the plugin is wrong rather than the transport.

Nothing in a plugin's test suite can catch it. Tests hand the parser the string the plugin
*builds*; the defect is in the string it *receives*. It took adding a log line to the
unknown-route branch to see it at all.

## Cause

`PluginUiController.View` binds `route` from the query string with `[FromQuery] string?`.
A repeated query parameter binds in ASP.NET as `StringValues`, and converting that to a
single `string` joins the values with `,`.

So the client is sending the segments as a repeated parameter — `?route=/genre&route=ambient`
— rather than one value. The join is ASP.NET behaving normally; the repetition is the bug.

## Where to fix it

Client side, by sending the route as one parameter. Failing that, the server could bind
`StringValues` explicitly and join on `/`, which would also make the endpoint robust to any
client that repeats the parameter.

A plugin cannot fix it properly. It can only guess that a comma means a separator, which is
what this plugin now does — safe here because a comma cannot occur in anything it routes on
(slugs are ASCII letters and digits; station ids are uuids), but not a general answer. A
plugin whose ids may contain a comma has no way to tell a separator from a value.

## Reproduction

1. Install any plugin declaring a two-segment route.
2. Navigate to it.
3. Log `PluginViewRequest.Route`.

Observed: `/genre,ambient`. Expected: `/genre/ambient`.
