#!/usr/bin/env pwsh
# Packs the plugin contract into a local NuGet feed.
# See fetch-abstractions.sh for why this exists, why it packs these two projects, why it
# uses the newest release tag and why it works in _server/. This is a twin of that script
# and has to stay one: it drifted once already and cost a Windows developer a feed with
# a package missing.

$ErrorActionPreference = 'Stop'

function Test-CanBuildNet10([string] $Candidate) {
    if (-not $Candidate) { return $false }
    try { $sdks = & $Candidate --list-sdks 2>$null } catch { return $false }
    return [bool]($sdks | Where-Object { $_ -like '10.*' })
}

$dotnet = @(
    # Built only when its base variable is set: HOME is unset on a plain Windows shell
    # (native PowerShell, not launched from Git Bash), and Join-Path throws on a null
    # base rather than passing it through for Where-Object to filter out below.
    if ($env:USERPROFILE) { Join-Path $env:USERPROFILE '.dotnet\dotnet.exe' }
    if ($env:HOME) { Join-Path $env:HOME '.dotnet/dotnet' }
    'dotnet'
) | Where-Object { $_ } | Where-Object { Test-CanBuildNet10 $_ } | Select-Object -First 1

if (-not $dotnet) { throw 'no dotnet SDK on this machine can build net10.0' }

$root   = Split-Path -Parent $PSScriptRoot
$server = if ($env:SERVER_DIR) { $env:SERVER_DIR } else { Join-Path $root '_server' }
$feed   = Join-Path $root '_nupkgs'
$remote = 'https://github.com/NoMercy-Entertainment/nomercy-media-server.git'

$ref = $env:SERVER_REF
if (-not $ref) {
    $ref = git ls-remote --tags --refs $remote 'v*' |
        ForEach-Object { ($_ -split 'refs/tags/')[1] } |
        Sort-Object { [version]($_.TrimStart('v')) } |
        Select-Object -Last 1
    if (-not $ref) { throw 'could not resolve the newest nomercy-media-server release tag' }
}

if (-not (Test-Path (Join-Path $server '.git'))) {
    New-Item -ItemType Directory -Force $server | Out-Null
    git -C $server init -q
    git -C $server remote add origin $remote
    if ($LASTEXITCODE -ne 0) { throw 'initialising the contract checkout failed' }
}

git -C $server sparse-checkout init --cone
git -C $server sparse-checkout set `
    src/NoMercy.PluginSdk.Abstractions src/NoMercy.PluginSdk.Mvc src/NoMercy.Events src/NoMercy.Design
if ($LASTEXITCODE -ne 0) { throw 'sparse-checkout set failed' }

git -C $server fetch --depth=1 --filter=blob:none origin $ref
if ($LASTEXITCODE -ne 0) { throw "fetching $ref failed" }
git -C $server checkout -q --force FETCH_HEAD
if ($LASTEXITCODE -ne 0) { throw "checking out $ref failed" }

if (Test-Path $feed) { Remove-Item -Recurse -Force $feed }
New-Item -ItemType Directory -Force $feed | Out-Null

foreach ($project in @('NoMercy.PluginSdk.Abstractions', 'NoMercy.PluginSdk.Mvc')) {
    $csproj = Join-Path $server "src/$project/$project.csproj"
    if (-not (Test-Path $csproj)) {
        throw "$project is not present at $ref - that ref predates plugin ABI 12"
    }

    & $dotnet pack $csproj -c Release -o $feed
    if ($LASTEXITCODE -ne 0) { throw "packing $project failed" }
}

$sha = git -C $server rev-parse HEAD
Set-Content -Path (Join-Path $feed 'CONTRACT') -Value "$ref $sha" -NoNewline

Get-ChildItem $feed -Filter *.nupkg | ForEach-Object { Write-Host "  $($_.Name)" }
Write-Host "contract packed from nomercy-media-server $ref ($sha)"
