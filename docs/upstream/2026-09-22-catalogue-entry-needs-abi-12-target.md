# The catalogue has no version that targets ABI 12, and #2 never asked for one

**Repo:** `nomercy-plugins`
**Found:** 2026-09-22, against `master` at `0746563c65db60d10e437fa74635d80629745ef9`
(current `index.json`/`schema.json`, read via the GitHub contents API).
**Correction to the task brief:** issue #2 is not "already open" — it is **closed**
(`stateReason: COMPLETED`, closed 2026-08-13). This is a proposal for a new, small issue
or a comment reopening #2's scope, not a duplicate.

> **Status (2026-09-23):** still open, and now sharper. `index.json` at `nomercy-plugins`
> `master@0746563c65db60d10e437fa74635d80629745ef9` (re-read today via the GitHub contents
> API, unchanged since the original finding) still lists Internet Radio at 1.0.2/1.1.0/1.2.0,
> all `targetAbi: "10.0"`. This branch's `plugin.json` declares `"targetAbi": "12.1"`
> (`src/NoMercy.Plugin.InternetRadio/plugin.json:6`), and `nomercy-media-server`
> `dev@4c16c0d09f4bcc2fa3a39ad2e791f52441cdd69b` (fetched today) has since bumped
> `PluginAbi.Current` to `12.2` (`e82f3d80e`, "additive, backward-compatible") — so by the
> time 2.0.0 ships, the correct catalogue entry needs `targetAbi` reflecting whatever ABI the
> release actually targets, not `12.0` as this note originally assumed the next number would
> be. The publishing step this note asks for has not happened yet; it happens at release,
> which is out of scope for this task.

## What #2 already fixed, and what it didn't ask for

Live `index.json` today carries the entry #2's own closing comment proposed: correct Ulid
id, current name/description, and versions 1.0.2/1.1.0/1.2.0 with real checksums:

```json
{
  "id": "5KTKRT4Z2Y9P59Y40W5CX4TQKF",
  "name": "Internet Radio",
  "description": "Browse and play internet radio stations in the built-in player.",
  "versions": [
    { "version": "1.2.0", "targetAbi": "10.0", ... },
    { "version": "1.1.0", "targetAbi": "10.0", ... },
    { "version": "1.0.2", "targetAbi": "10.0", ... }
  ]
}
```

and `schema.json`'s `id` pattern is already a Ulid (`^[0-9A-HJKMNP-TV-Z]{26}$`), not the
`uuid` format #2 originally flagged. **So the id half of the task brief's candidate #14 is
already satisfied** — nothing further is needed there.

What is missing: every listed version's `targetAbi` is `"10.0"`. This plugin's ABI 12.1
build (branch `feat/abi-12` here, headed for a `2.0.0` release per this repo's own recent
`docs:` commit) has no entry in the catalogue at all yet — expected, since it has not been
released — but #2 never discussed ABI targeting as a requirement, and nothing else in this
repository's open issues does either (`gh issue list -R NoMercy-Entertainment/
nomercy-plugins --state all` lists exactly one issue: #2, closed).

## Why this needs saying now rather than assumed

`schema.json`'s `targetAbi` is optional (not in `version`'s `required` list), and
`nomercy-media-server` treats a missing/incompatible `targetAbi` as informative rather than
blocking at the catalogue level (`PluginQueryTokenPolicy.Accepts`, `PluginAbi.IsCompatible`
— both null-tolerant). Nothing enforces that a catalogue entry offered to an ABI-12 server
actually targets ABI 12. Compare the `Torrent Downloader` entry in the same `index.json`,
which already lists two versions correctly at `"targetAbi": "12.0"` alongside its older
`"10.0"` releases — proof the schema and the index already support mixed-ABI version lists
correctly when an author remembers to set the field per release.

## What it costs

When 2.0.0 ships, publishing its `repository.json`/release artifacts without also adding a
version entry to `index.json` with `"targetAbi": "12.0"` reproduces exactly the situation
#2 was originally filed over: a server on the new ABI sees a catalogue entry (1.2.0,
`targetAbi: "10.0"`) that either looks like the latest available version and is not
installable, or is silently filtered as incompatible with no newer alternative shown either
way, an owner running ABI 12.1 (like beast-unit today) has no correct catalogue path to the
version already running there.

## Proposed fix (proposal, not decided)

Add a comment to #2 (or a new, narrowly-scoped issue) stating plainly, ahead of the 2.0.0
release, that the catalogue update for that release must add a `2.0.0` version entry with
`"targetAbi": "12.0"` (or whatever the released ABI number is) alongside the existing
history, using the same `repository.json`-sourced checksum/download-URL pattern the 1.2.0
entry already follows — and that the Ulid `id` already in the index is correct and must not
be reverted.
