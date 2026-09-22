# SPDX-License-Identifier: MIT
#
# Copies a built plugin onto a running NoMercy server over ssh.
#
#   scripts\deploy-to-server.ps1
#   scripts\deploy-to-server.ps1 -Build
#   scripts\deploy-to-server.ps1 -DryRun
#   scripts\deploy-to-server.ps1 -Server other-host
#
# THE SERVER MUST BE STOPPED FIRST. A loaded plugin's assembly is held open by
# the host, so the copy fails and the old build stays in place - which looks
# exactly like a deploy that worked and changed nothing. Every file's hash is
# compared afterwards, because that is the only way to tell those two apart.
#
# Files travel as base64 through ssh rather than with scp: scp against this
# host fails where a plain ssh session works (OpenSSH 9 made scp speak SFTP by
# default and this host does not answer that).
[CmdletBinding()]
param(
    [string] $Server = $(if ($env:SERVER) { $env:SERVER } else { 'beast-unit' }),
    [string] $Configuration = 'Release',
    [string] $Framework = 'net10.0',
    [switch] $Build,
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = 'NoMercy.Plugin.InternetRadio'
$out = Join-Path $root "src\$project\bin\$Configuration\$Framework"

# Expanded on the far side, not here: it is that machine's profile.
$remoteDir = "`$LOCALAPPDATA/NoMercy/plugins/$project"

# The manifest travels with the assembly on purpose: the two carry the
# version independently, and updating one without the other leaves every
# server reporting a version it is not running. There is no Core.dll in this
# plugin, unlike its sibling.
#
# README.md and LICENSE are deliberately not copied. The host never reads
# them, and keeping the deploy to exactly what the host loads is what makes
# the hash check below meaningful.
$files = @(
    "$project.dll",
    "$project.deps.json",
    'plugin.json'
)

if ($Build) {
    function Test-CanBuildNet10([string] $Candidate) {
        if (-not $Candidate) { return $false }
        try { $sdks = & $Candidate --list-sdks 2>$null } catch { return $false }
        return [bool]($sdks | Where-Object { $_ -like '10.*' })
    }

    # Same selection as scripts/fetch-abstractions.ps1: the version decides,
    # not the location, because plain `dotnet` on PATH here is SDK 8.
    $dotnet = @(
        if ($env:USERPROFILE) { Join-Path $env:USERPROFILE '.dotnet\dotnet.exe' }
        if ($env:HOME) { Join-Path $env:HOME '.dotnet/dotnet' }
        'dotnet'
    ) | Where-Object { $_ } | Where-Object { Test-CanBuildNet10 $_ } | Select-Object -First 1

    if (-not $dotnet) { throw 'no dotnet SDK on this machine can build net10.0' }

    Write-Host "building $Configuration…"
    & $dotnet build (Join-Path $root "src\$project\$project.csproj") -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
}

if (-not (Test-Path $out)) { throw "nothing built at $out - run with -Build" }

# A typo'd -Server must not silently create a stray folder on some unrelated
# box - the host only ever loads plugins from a folder it already knows
# about, so an absent one means the wrong host, not a fresh install.
& ssh -o BatchMode=yes $Server "[ -d `"$remoteDir`" ]"
if ($LASTEXITCODE -ne 0) {
    throw "remote plugins folder does not exist: ${Server}:${remoteDir}`n  is -Server right, and is NoMercy installed there?"
}

$langDir = Join-Path $out 'lang'

if ($DryRun) {
    Write-Host "dry run - ${Server}:${remoteDir} exists, nothing will be written"
    foreach ($file in $files) {
        $path = Join-Path $out $file
        if (Test-Path $path) {
            Write-Host "  would copy $file -> $remoteDir/$file"
        } else {
            Write-Host "  would skip $file (not built)"
        }
    }
    if (Test-Path $langDir) {
        Get-ChildItem $langDir -Filter *.json | ForEach-Object {
            Write-Host "  would copy lang/$($_.Name) -> $remoteDir/lang/$($_.Name)"
        }
    } else {
        Write-Host '  would skip lang/*.json (not built)'
    }
    return
}

Write-Host "deploying to $Server…"

function Copy-OneFile([string] $LocalPath, [string] $RemotePath) {
    $localSum = (Get-FileHash $LocalPath -Algorithm MD5).Hash.ToLowerInvariant()
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($LocalPath))
    $temp = Join-Path ([IO.Path]::GetTempPath()) 'nm-deploy.b64'
    [IO.File]::WriteAllText($temp, $b64)

    Get-Content -Raw $temp | ssh -o BatchMode=yes $Server 'cat > /tmp/nm-deploy.b64'
    Remove-Item $temp -Force

    # tr first, because PowerShell terminates a pipe into a native command
    # with CRLF and GNU base64 rejects the CR outright - "base64: invalid
    # input", with an empty hash that reads exactly like a file the server
    # still holds open. The bash script redirects a file into ssh instead and
    # never grows the extra byte; stripping here means one remote command
    # serves both.
    $remote = ssh -o BatchMode=yes $Server "tr -d '\r' < /tmp/nm-deploy.b64 | base64 -d > `"$RemotePath`" && md5sum `"$RemotePath`""
    $remoteSum = ($remote -join '') -replace '[\\*]', '' -split ' ' | Select-Object -First 1

    # The hashes are the whole point. A busy file leaves the old bytes in
    # place and the copy still looks like it succeeded, so comparing is the
    # only way to know a deploy actually happened.
    if ($localSum -ne $remoteSum) {
        Write-Error "FAILED $RemotePath`n  local  $localSum`n  remote $remoteSum`n  Is the server still running? A loaded plugin's dll cannot be replaced."
    }

    Write-Host "  ok $(Split-Path -Leaf $RemotePath)  $localSum"
}

foreach ($file in $files) {
    $path = Join-Path $out $file
    if (-not (Test-Path $path)) { Write-Host "  skip $file (not built)"; continue }
    Copy-OneFile $path "$remoteDir/$file"
}

if (Test-Path $langDir) {
    ssh -o BatchMode=yes $Server "mkdir -p `"$remoteDir/lang`""
    Get-ChildItem $langDir -Filter *.json | ForEach-Object {
        Copy-OneFile $_.FullName "$remoteDir/lang/$($_.Name)"
    }
} else {
    Write-Host '  skip lang/*.json (not built)'
}

$deployedVersion = ssh -o BatchMode=yes $Server "grep -o '\"version\"[^,}]*' `"$remoteDir/plugin.json`"" 2>$null
if ($deployedVersion) {
    $deployedVersion = ($deployedVersion -split '"')[3]
} else {
    $deployedVersion = '<unknown>'
}

Write-Host ''
Write-Host "done - deployed version: $deployedVersion"
Write-Host 'start the server again.'
