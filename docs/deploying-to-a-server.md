# Deploying a build to a running server

`scripts/deploy-to-server.sh` (or `.ps1`) copies a built plugin onto a NoMercy
server over ssh. It exists because the owner does not run a NoMercy server on
the development machine — this is how a build gets onto the one machine that
does, without a full install/uninstall cycle each time.

```sh
scripts/deploy-to-server.sh --build     # build Release, then copy
scripts/deploy-to-server.sh             # copy what is already built
scripts/deploy-to-server.sh --dry-run   # print the plan, touch nothing
SERVER=other-host scripts/deploy-to-server.sh
```

It copies the assembly, `deps.json`, `plugin.json`, and every file under
`lang/` into
`%LOCALAPPDATA%\NoMercy\plugins\NoMercy.Plugin.InternetRadio` on the far side,
creating `lang\` there if it does not already exist, and compares every
file's hash afterwards. `README.md` and `LICENSE` are not copied — the host
never reads them, and shipping only what the host loads is what makes the
hash check below mean something. A file the host ignores could drift without
anyone noticing, which defeats the point of checking at all.

## The server has to be stopped

A loaded plugin's assembly is held open by the host. Copying over it fails
with *Device or resource busy*, the old build stays in place, and the next
thing you do looks exactly like a deploy that worked and changed nothing —
you test the old code and draw conclusions from it. This costs an hour the
first time and ten minutes every time after.

So the loop is: **stop the server → deploy → start the server**. The scripts
do not stop or start the server themselves — that is the operator's call, not
a script's, since it also stops whatever else is watching that machine right
now. Each run prints a reminder at both ends instead. The scripts do compare
hashes and refuse to report success on a mismatch, which is what turns a
silent failure into a loud one.

## Why base64 over ssh instead of scp

Plain `scp` fails against this host because OpenSSH 9 made `scp` speak the
SFTP protocol by default, and this host does not answer it. `scp -O` uses the
old protocol and would work fine for a one-off copy by hand.

The scripts use base64 through a plain ssh session instead, because they also
verify what arrived: each file is hashed locally, piped over as base64,
decoded and hashed again on the far side, and the two are compared. At a few
hundred kilobytes the speed difference next to `scp -O` is not worth
maintaining a second code path just to skip the verification step.

## A typo'd SERVER must not create anything

Before touching any file, the script checks that the remote plugins folder
already exists and refuses to continue if it does not. Without that check, a
mistyped `SERVER` would either fail confusingly partway through or, worse,
`mkdir -p` a stray folder on some unrelated host. An absent plugins folder
means the wrong host or a NoMercy install that is not there yet — either way,
not something to fix by creating directories.

## Two things that are easy to get wrong

**Do not keep backups inside the plugins folder.** The host loads every
directory under `plugins/` as a plugin, so a copy of the previous build there
gets loaded as a second plugin with the same id. Keep backups somewhere else,
e.g. `%LOCALAPPDATA%\NoMercy\plugin-backups`.

**Send `plugin.json` with the assembly, and `lang/` with both.** The manifest
and the assembly carry the version independently — updating one without the
other leaves the server reporting a version it is not running, and the
update check then either nags forever or stays silent when it should not.
`lang/` is not optional either: `plugin.json` names a `translations.path` of
`lang`, and a mismatched or missing translation file changes what the UI
shows without changing what version is reported, which is a much quieter way
to end up running an inconsistent build.

## Confirming a deploy actually landed

Each run prints the version read back from the far side's `plugin.json` once
every file's hash has matched, so the last line of output says what is
actually sitting in the plugins folder now — not what the manifest read on
this machine, but what round-tripped through the copy.
