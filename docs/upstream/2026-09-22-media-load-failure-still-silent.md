# Re-verified: a failed media load still emits no error, against player-core 2.2.1

**Repo:** `nomercy-player-core`
**Supersedes nothing** — this re-verifies and updates the version reference for
`docs/upstream/2026-08-09-plugin-media-failure-is-silent.md`, whose full diagnosis,
reproduction and suggested fix still apply verbatim. See that file for the complete
write-up; this note only records what changed and what didn't between then and now.
**Found:** 2026-09-22, against the installed copy at
`nomercy-app-web/node_modules/@nomercy-entertainment/nomercy-player-core` **2.2.1**
(package.json's declared version; no git repository for `nomercy-player-core` exists on
this machine to cite a commit — see the task's own note that the source repo is not a
checked-out sibling here).
**Raised:** not yet. `gh issue list -R NoMercy-Entertainment/nomercy-player-core
--state all --search "load error swallow" / "queue catch" / "silent"`, and an unfiltered
`--state all` listing — all empty; the repository currently has no issues on record.

> **Status (2026-09-23):** still open, unaffected by Tasks 1-6. `node_modules/@nomercy-entertainment/nomercy-player-core/package.json`
> in `nomercy-app-web` `origin/master@00d7857ea2375a6de81f070d3a8a2faeaac26005` still pins
> `2.2.1`, unchanged since this note's last check. Nothing in this branch's work touches
> playback, the queue, or error handling on either side of the SDK boundary, so a failed
> load still reaches no error path for any station this plugin queues.

## What changed since 2.0.4

Nothing material to this defect. The original doc cites `dist/core/mixins/queue.js` and
`dist/core/mixins/loading.js` at 2.0.4; the same two files at 2.2.1 still have the same
shape:

```js
// dist/core/mixins/loading.js — load()'s catch block (2.2.1)
catch (err) {
    if (this._phase === 'loading' && RESUMABLE_PHASES_FOR_ERROR.includes(priorPhase)) {
        this._transitionPhase(priorPhase);
    }
    if (isLatest() && this._playState === PlayState.LOADING) {
        this._playState = priorPlayState;
    }
    throw err;                 // no emit('error', ...) on this path
}
```

```js
// dist/core/mixins/queue.js — item()'s doLoad() (2.2.1)
void this.load(currentItem, { source: opts?.source, startAt: opts?.startAt })
    .then(() => { ... })
    .catch(() => { });         // swallows load()'s rejection with no trace
```

`loadQueue()` in the same 2.2.1 `loading.js` still does emit `error` on its own failure
path, confirming the pattern and the imports the original doc's suggested fix points at are
still present and still asymmetric with `load()`.

## Disposition

Confirmed, not reported. Read against the installed dist bundle only — I did not start a
player or reproduce the browser-side symptom again (the original doc's Edge/`WebAudioBackend`
reproduction stands as the last live confirmation, at 2.0.4). Treat this file and
`2026-08-09-plugin-media-failure-is-silent.md` together as the ready-to-file issue text for
`nomercy-player-core`; update the version reference in the older file to 2.2.1 (or cite
both) if filing today.
