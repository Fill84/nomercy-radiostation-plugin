# `scripts/fetch-abstractions.ps1` cannot run on a plain Windows shell

**From:** the agent working in `../nomercy-yt-plugin`, 2026-08-08
**Status:** a note, not a change. Nothing in this repository has been touched.
**Confidence:** observed, not inferred — it failed on the first run of the copied script.

## What happens

```
Join-Path: scripts\fetch-abstractions.ps1:27
Line |
  27 |      (Join-Path $env:HOME '.dotnet/dotnet')
     |                 ~~~~~~~~~
     | Cannot bind argument to parameter 'Path' because it is null.
```

The script exits 1 before it packs anything, so `dotnet restore` then fails on
`NoMercy.Plugins.Abstractions` with a wall of CS0246 that names no file the script owns —
which is exactly the failure the script's own header warns about, arriving through a different
door.

## Why

`$env:HOME` is not set on Windows unless something like Git Bash set it for that shell, and
`Join-Path` **throws** on a null base rather than returning nothing. The array is built before
`Where-Object { $_ }` ever runs, so the null-filter never gets the chance to drop it.

The `.sh` twin already avoids this: it writes `"${HOME:-}"`. So the two scripts do not behave
the same on the machine that most needs the PowerShell one, which is the drift the header
comment explicitly asks readers to prevent.

## The fix that worked

In `../nomercy-yt-plugin/scripts/fetch-abstractions.ps1`, this is the whole change:

```powershell
$dotnet = @(
    $(if ($env:USERPROFILE) { Join-Path $env:USERPROFILE '.dotnet\dotnet.exe' })
    $(if ($env:HOME) { Join-Path $env:HOME '.dotnet/dotnet' })
    'dotnet'
) | Where-Object { $_ } | Where-Object { Test-CanBuildNet10 $_ } | Select-Object -First 1
```

`$env:USERPROFILE` is guarded for the same reason in reverse: it is unset everywhere that is
not Windows, so the unguarded form would throw on Linux and macOS instead.

Nothing else in the script needed changing. The version probe below it is correct and does its
job — on this workspace the `dotnet` on `PATH` is 8.0.413 and cannot build `net10.0`, while
`~/.dotnet` holds 10.0.302, and the probe picks the right one once it is allowed to run.

## How to reproduce

Run `pwsh scripts/fetch-abstractions.ps1` from a PowerShell session that has not had `HOME` set
— a fresh `pwsh` on Windows, not one launched from Git Bash. If it packs four `.nupkg` files,
your shell has `HOME` set and the defect is hidden rather than absent; check with `$env:HOME`.
