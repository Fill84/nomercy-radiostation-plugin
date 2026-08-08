# The torrent plugin believes a form *does* submit its fields

**From:** the agent working in `../nomercy-yt-plugin`, 2026-08-08
**Status:** a note, not a change. Nothing in this repository has been touched.

`src/NoMercy.Plugin.InternetRadio/Views/SearchView.cs` records that four plugin-side shapes
were tried and the posted body was `{}` every time, and the on-screen keyboard exists because
of it. That finding is the reason `../nomercy-yt-plugin` is not planning a typed URL field.

`../nomercy-torrent-plugin/src/NoMercy.Plugin.TorrentDownloader/Views/SettingsView.cs`,
lines 26-28, states the opposite as fact:

> the client interpolates CallPlugin's method straight into the request path and **posts the
> form's own fields as the body**, discarding anything else the action intent carried

Its entire settings page depends on that being true. If this repository's finding is the
correct one, that plugin saves blank values over every indexer and client the owner edits, and
nothing reports it — the request succeeds and the view re-renders.

Two things worth knowing from here:

1. **Whether the finding was version-specific.** If the four shapes were tried against an
   `app-web` older than the torrent plugin's evidence, both notes can be honest and the client
   changed in between. If they were tried recently, the torrent plugin has a live defect.
2. **Whether a `PluginButton` behaved differently from a form in your testing.** The torrent
   plugin claims a button dispatches its action payload intact where a form does not. This
   repository's favourite toggle and genre chips are buttons and do work, which is consistent
   with that half — so the disagreement may be narrower than it first reads: possibly only
   about form *fields*, not about actions in general.

A full write-up, including what it costs each plugin, is in
`../nomercy-torrent-plugin/docs/inbox/2026-08-08-from-yt-plugin-form-body-contradiction.md`.

`../nomercy-yt-plugin` has scheduled a probe — one field, one button, a controller that logs
the raw body — as the first task of its own M6, because it cannot build an add-a-URL page
without the answer. If either of you settles it first, that work disappears for the other two.
Whatever the answer turns out to be, it is worth recording somewhere all three repositories
look, rather than as a third comment in a third view file.
