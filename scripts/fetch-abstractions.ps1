#!/usr/bin/env pwsh
# Packs NoMercy.Plugins.Abstractions into a local NuGet feed.
# See fetch-abstractions.sh for why this exists and why Mvc is not packed.
#
# This is a twin of that script and has to stay one. It drifted once already - still
# packing two projects after the shell script had moved to three, and applying the
# sparse-checkout list only on the initial clone - so a Windows developer got a feed
# missing NoMercy.Design and a wall of CS0246 that named no file either script owns.

$ErrorActionPreference = 'Stop'

# The version decides, not the location: a side-by-side install without a 10.x SDK
# fails global.json resolution before it packs anything, and the `dotnet` on PATH is
# just as likely to be the usable one.
function Test-CanBuildNet10([string] $Candidate) {
    if (-not $Candidate) { return $false }
    try { $sdks = & $Candidate --list-sdks 2>$null } catch { return $false }
    return [bool]($sdks | Where-Object { $_ -like '10.*' })
}

$dotnet = @(
    (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe')
    (Join-Path $env:HOME '.dotnet/dotnet')
    'dotnet'
) | Where-Object { $_ } | Where-Object { Test-CanBuildNet10 $_ } | Select-Object -First 1

if (-not $dotnet) { throw 'no dotnet SDK on this machine can build net10.0' }

$root = Split-Path -Parent $PSScriptRoot
# Beside this repo, not inside it. One sibling serves every plugin in the workspace
# instead of each keeping its own copy. SERVER_DIR overrides it, which is how CI keeps
# the clone inside its own disposable workspace.
$server = if ($env:SERVER_DIR) { $env:SERVER_DIR } else { Join-Path (Split-Path -Parent $root) 'nomercy-media-server' }
$feed   = Join-Path $root '_nupkgs'
$branch = if ($env:SERVER_BRANCH) { $env:SERVER_BRANCH } else { 'master' }
# A release must be rebuildable. SERVER_REF pins the contract to one commit; it defaults
# to a branch for day-to-day work, but CI sets it to a SHA for a tag build so the
# artifact is reproducible instead of "whatever dev happened to be".
$ref    = if ($env:SERVER_REF) { $env:SERVER_REF } else { $branch }

if (-not (Test-Path $server)) {
    git clone --depth=1 --branch=$branch --filter=blob:none --no-checkout `
        https://github.com/NoMercy-Entertainment/nomercy-media-server.git $server
    if ($LASTEXITCODE -ne 0) { throw 'cloning nomercy-media-server failed' }
}

# A checkout that already exists may predate sparse-checkout, and `add` below refuses to
# run on one that was never initialised.
git -C $server sparse-checkout list *> $null
if ($LASTEXITCODE -ne 0) { git -C $server sparse-checkout init --cone }

# `add`, not `set`. This sibling serves every plugin in the workspace and each needs a
# different slice of it - the torrent plugin also materialises NoMercy.Plugins.Mvc, for
# the REST base class this plugin has no use for. `set` replaces the whole list, so
# whichever plugin packed last would strip the others' projects back out.
git -C $server sparse-checkout add `
    src/NoMercy.Plugins.Abstractions src/NoMercy.Events src/NoMercy.Design
if ($LASTEXITCODE -ne 0) { throw 'sparse-checkout add failed' }

git -C $server fetch --depth=1 origin $ref
if ($LASTEXITCODE -ne 0) { throw "fetching $ref failed" }
git -C $server reset --hard FETCH_HEAD
if ($LASTEXITCODE -ne 0) { throw 'resetting the contract checkout failed' }

# Emptied, not topped up. The package version comes from the server's own build props,
# which master stamps per release and dev leaves at an old number - so packing master
# after dev leaves 0.1.404 and 0.1.470 side by side in the feed. The plugin's reference
# floats, NuGet takes the highest, and the answer stops depending on the ref this script
# was asked for.
if (Test-Path $feed) { Remove-Item -Recurse -Force $feed }
New-Item -ItemType Directory -Force $feed | Out-Null

$abstractions = Join-Path $server 'src/NoMercy.Plugins.Abstractions/NoMercy.Plugins.Abstractions.csproj'
if (-not (Test-Path $abstractions)) {
    throw "NoMercy.Plugins.Abstractions is not present at $ref - nothing to build against"
}

# Dependency order, and each one only if the ref actually has it. SERVER_REF pins this
# script to any commit and a release's notes hand out that exact command, so it has to
# keep working on a ref from before a project existed: NoMercy.Design is not in the tree
# at all at 886a8b3, the commit v1.0.2 was built from. Packing it unconditionally would
# break the reproduction path for every release so far.
#
# MSB9008 about a missing NoMercy.Analyzers is expected under a sparse checkout.
# It is an analyzer reference; the package builds correctly without it.
foreach ($project in @('NoMercy.Events', 'NoMercy.Design', 'NoMercy.Plugins.Abstractions')) {
    $csproj = Join-Path $server "src/$project/$project.csproj"
    if (-not (Test-Path $csproj)) {
        Write-Host "skipping $project - not in the tree at $ref"
        continue
    }

    & $dotnet pack $csproj -c Release -o $feed
    if ($LASTEXITCODE -ne 0) { throw "packing $project failed" }
}

Get-ChildItem $feed -Filter *.nupkg | ForEach-Object { Write-Host "  $($_.Name)" }

Write-Host "contract packed from nomercy-media-server $(git -C $server rev-parse HEAD)"
