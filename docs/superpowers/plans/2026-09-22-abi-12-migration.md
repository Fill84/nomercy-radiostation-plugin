# Internet Radio 2.0 (ABI 12.1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Internet Radio load and work fully on server plugin ABI 12.1 as release 2.0.0, without losing any user's favourites.

**Architecture:** The plugin is rebuilt against `NoMercy.PluginSdk.*` 12.1, packed from the newest server release tag. Playback moves from the plugin's own token-carrying relay to URLs the host mints through `Context.Media.Proxy`. Logos become `data:` URIs the plugin fetches and caches itself, and now-playing comes from a metadata connection of its own, shared per station. Views stay pure `Build(...)` functions. Everything that touches the host is resolved before a view is built and handed in as data.

**Tech Stack:** C# / .NET 10, `NoMercy.PluginSdk.Abstractions` + `NoMercy.PluginSdk.Mvc` 12.1, xunit 2 + FluentAssertions 7, Forgejo Actions.

**Spec:** `docs/superpowers/specs/2026-09-22-abi-12-migration-design.md`. Read it before starting any task. It says *why* each change exists, and this plan does not repeat that.

## Global Constraints

- **Branch:** all work happens on `feat/abi-12`. Commit locally after each task. **Never push** unless the owner asks.
- **Target framework** is `net10.0`. **On this machine, plain `dotnet` is SDK 8.0.413 and cannot build `net10.0`.** Run every command below as `"$USERPROFILE/.dotnet/dotnet.exe"` (Git Bash: `/c/Users/philp/.dotnet/dotnet.exe`). CI's bare `dotnet` is correct in CI.
- **Never run `scripts/fetch-abstractions.*` before Task 1 has changed its default folder.** The old default is `../nomercy-media-server`, the owner's own working server checkout, and the script runs `git reset --hard` on it. After Task 1 the default is `_server/` inside this repository.
- **`TreatWarningsAsErrors` is true** for both projects. A step "passes" only with 0 warnings and 0 errors.
- **Version is `2.0.0`** from Task 1 on. It must read identically in `src/NoMercy.Plugin.InternetRadio/plugin.json`, the csproj `<Version>` and `PluginIdentity.Version`. `targetAbi` is `"12.1"`.
- **The plugin id is `5KTKRT4Z2Y9P59Y40W5CX4TQKF` and must never change.**
- **No URL literal may appear in any `.cs` file under `src/`**, not even in a comment. CI greps `src/` for `https?://` and fails the build (only `radio-browser`, `forgejo.phillippepelzer.me` and `github.com` lines are allowed). Tests may use `example.com` URLs.
- **The user key is always the Guid "D" form** (`Guid.ToString()`, lowercase with dashes), produced only by `UserKey` (Task 2).
- **Views stay pure.** No `IPluginContext`, no network, no clock and no file access inside anything under `Views/`.
- **View strings stay literal English.** Do not introduce translation keys.
- **`PluginText` variants are `title`, `subtitle` and `caption` only.**
- **Never ship `NoMercy.PluginSdk.Abstractions.dll`, `NoMercy.PluginSdk.Mvc.dll`, `NoMercy.Events.dll`, `NoMercy.Design.dll`, `Newtonsoft.Json.dll` or `Ulid.dll`.** All of them are host-owned.
- **Every new file gets the SPDX header:**
  ```csharp
  // SPDX-License-Identifier: MIT
  // Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84
  ```
- **Comment style:** explain *why*, in full sentences, as the surrounding code does. No commented-out code.
- **Commit messages:** Conventional Commits, in the repository's existing voice. **No attribution, co-author or "generated" trailers of any kind.**
- **Nothing goes to another repository or to Stoney** without the owner's explicit approval (Task 11).

## File Structure

| File | Responsibility | Task |
| --- | --- | --- |
| `scripts/fetch-abstractions.sh`, `.ps1` | Pack `NoMercy.PluginSdk.*` from the newest server release tag into `_nupkgs`, in `_server/` | 1 |
| `.forgejo/workflows/build.yml` | Build against the same tag; the new host-owned assembly names | 1, 8 |
| `src/.../NoMercy.Plugin.InternetRadio.csproj`, `tests/.../*.Tests.csproj` | `NoMercy.PluginSdk.*` 12.* references | 1 |
| `src/.../plugin.json` | `targetAbi` 12.1, version 2.0.0; `media.proxy` hook | 1, 5 |
| `State/UserKey.cs` (new) | The one place a user id becomes the storage key | 2 |
| `Catalog/ImageSniffer.cs` (new) | What image type a byte prefix is, or none | 3 |
| `Catalog/LogoFetcher.cs` (new) | One logo URL into a `data:` URI, within 16 KB | 3 |
| `Catalog/LogoCache.cs` (new) | `IStationLogos`; logos on disk and in memory, 2,000 entries, LRU | 3 |
| `Views/IStationLogos.cs` (new) | What a view asks for a station's logo | 4 |
| `Views/StationCards.cs` | Tiles and media intents take their logo and stream as data | 4, 5 |
| `Views/{Browse,Genre,AllStations,Search}View.cs` | Pass `IStationLogos` through to the grid | 4 |
| `Views/StationStream.cs` (new) | A minted stream URL, or why there is none | 5 |
| `Playback/IStationStreams.cs`, `Playback/HostStationStreams.cs` (new) | Minting through `Context.Media.Proxy`; one grant request | 5 |
| `Views/StationView.cs` | Play only with a stream; notice otherwise; refresh every 600 s; no homepage button | 5 |
| `Catalog/NowPlayingListener.cs` (new) | Shared per-station ICY metadata connection with a lease | 6 |
| `Controllers/InternetRadioController.cs` | `nowplaying` drives the listener; `stream`/`cover` relay removed | 6, 7 |
| `InternetRadioPlugin.cs` | Wiring: user key, logos, streams, listener; relay and token code removed | 1, 2, 4, 5, 6, 7 |
| `Views/MediaProxy.cs` | **Deleted** | 7 |
| `README.md`, `src/.../README.md`, `docs/upstream/*` | Minimum server, install path, re-consent | 8 |
| every `Catalog/`, `Controllers/`, `State/`, `Views/`, `Playback/` file | Namespace per folder | 9 |

---

### Task 1: Build against ABI 12.1

This task puts the build onto the new contract and changes nothing about behaviour. It does remove the two `Services` reads, which no longer compile. `MediaProxy` then never learns a base URL, so its callers fall back as they already do. That intermediate state is never released: Task 5 replaces those callers.

**Files:**
- Modify: `scripts/fetch-abstractions.sh`, `scripts/fetch-abstractions.ps1`
- Modify: `.forgejo/workflows/build.yml` (env, "Pack the plugin contract" step, "Stage the plugin directory" check and comment)
- Modify: `src/NoMercy.Plugin.InternetRadio/NoMercy.Plugin.InternetRadio.csproj`, `tests/NoMercy.Plugin.InternetRadio.Tests/NoMercy.Plugin.InternetRadio.Tests.csproj`
- Modify: every `.cs` file with `using NoMercy.Plugins.Abstractions;` or `using NoMercy.Plugins.Mvc;`
- Modify: `src/NoMercy.Plugin.InternetRadio/plugin.json`, `src/NoMercy.Plugin.InternetRadio/PluginIdentity.cs`
- Modify: `src/NoMercy.Plugin.InternetRadio/InternetRadioPlugin.cs` (delete `CallerBearerToken`, `ReadCallerBearerToken`, `PublicBaseUrl`, the `MediaProxy.Remember(...)` call and `using Microsoft.AspNetCore.Http;`)
- Modify: `tests/.../TestSupport/FakePluginContext.cs`, `tests/.../PluginLifecycleTests.cs:42`, `tests/.../DiscoveryContractTests.cs:95`, `tests/.../ManifestTests.cs:30,40`

**Interfaces:**
- Produces: `_nupkgs/CONTRACT`, a one-line file `"<tag> <sha>"` naming the server release packed. CI and Task 10 read it.
- Produces: `TestSupport.TestCaller.For(Guid user)` returning a `PluginCaller`, used by Tasks 2, 5 and 6.

- [ ] **Step 1: Confirm the starting point is green**

Run:
```bash
cd /f/DevProjects/NoMercyEntertainment-Developement/nomercy-radiostation-plugin
git status --short && git branch --show-current
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true 2>&1 | tail -3
/c/Users/philp/.dotnet/dotnet.exe test -c Release --no-build 2>&1 | grep -E "Passed!|Failed!"
```
Expected: a clean tree on `feat/abi-12`, and `Passed!  - Failed: 0, Passed: 359, Skipped: 1`. This is against the old 0.1.470 feed still in `_nupkgs`.

- [ ] **Step 2: Rewrite `scripts/fetch-abstractions.sh`**

Replace the whole file with:

```sh
#!/usr/bin/env sh
# Packs the plugin contract into a local NuGet feed.
#
# The contract is not published to nuget.org, so we fetch the server and pack it. Since
# plugin ABI 12 it is two packages: NoMercy.PluginSdk.Abstractions, which carries
# NoMercy.Events and NoMercy.Design inside it, and NoMercy.PluginSdk.Mvc, which holds
# PluginControllerBase. The Mvc base class has its own assembly on purpose: it must keep
# one identity across the load-context boundary, and putting it in Abstractions would
# force an ASP.NET Core framework reference on every plugin.
#
# Which server: the newest release tag, because a tag is what an owner actually runs.
# `master` stopped at ABI 10 in August 2026 while releases moved on, and building against
# it produced a plugin every up-to-date server refused. SERVER_REF pins anything else -
# a tag or a commit - and a release's notes hand out that exact command.
#
# Where: _server/ inside this repository (already in .gitignore). It used to default to
# a sibling checkout of the server, which on a developer machine is their own working
# copy - and this script sparse-checks-out and hard-resets whatever it is pointed at.
# SERVER_DIR still overrides the location.

set -eu

# CI puts the right SDK on PATH. On a Windows dev machine the `dotnet` on PATH may be
# an older SDK that cannot build net10.0, and the usable one is a side-by-side install
# under the user profile. So the version decides, not the location.
can_build_net10() {
    [ -x "$1" ] || [ "$1" = dotnet ] || return 1
    "$1" --list-sdks 2>/dev/null | grep -q '^10\.'
}

for candidate in "${USERPROFILE:-}/.dotnet/dotnet.exe" "${HOME:-}/.dotnet/dotnet" dotnet; do
    if can_build_net10 "$candidate"; then
        dotnet="$candidate"
        break
    fi
done

if [ -z "${dotnet:-}" ]; then
    echo "no dotnet SDK on this machine can build net10.0" >&2
    exit 1
fi

root=$(cd "$(dirname "$0")/.." && pwd)
server="${SERVER_DIR:-$root/_server}"
feed="$root/_nupkgs"
remote=https://github.com/NoMercy-Entertainment/nomercy-media-server.git

ref="${SERVER_REF:-}"
if [ -z "$ref" ]; then
    # --refs drops the peeled ^{} lines; sort -V orders v0.1.99 before v0.1.100.
    ref=$(git ls-remote --tags --refs "$remote" 'v*' | sed 's#.*refs/tags/##' | sort -V | tail -n 1)
    if [ -z "$ref" ]; then
        echo "could not resolve the newest nomercy-media-server release tag" >&2
        exit 1
    fi
fi

if [ ! -d "$server/.git" ]; then
    mkdir -p "$server"
    git -C "$server" init -q
    git -C "$server" remote add origin "$remote"
fi

# `set`, not `add`: this checkout belongs to this repository alone now, so the list is
# exactly what this script needs and nothing another plugin left behind. Cone mode also
# keeps the root files - Directory.Build.props, Directory.Packages.props, .editorconfig,
# LICENSE - which the pack reads.
git -C "$server" sparse-checkout init --cone
git -C "$server" sparse-checkout set \
    src/NoMercy.PluginSdk.Abstractions src/NoMercy.PluginSdk.Mvc src/NoMercy.Events src/NoMercy.Design

git -C "$server" fetch --depth=1 --filter=blob:none origin "$ref"
git -C "$server" checkout -q --force FETCH_HEAD

# Emptied, not topped up: a leftover package of another version would win the floating
# reference and silently decide the contract instead of the ref asked for.
rm -rf "$feed"
mkdir -p "$feed"

for project in NoMercy.PluginSdk.Abstractions NoMercy.PluginSdk.Mvc; do
    csproj="$server/src/$project/$project.csproj"
    if [ ! -f "$csproj" ]; then
        echo "$project is not present at $ref - that ref predates plugin ABI 12" >&2
        exit 1
    fi

    # MSB9008 about a missing NoMercy.Analyzers is expected under a sparse checkout. It is
    # the server's own analyzer reference; the package builds correctly without it.
    "$dotnet" pack "$csproj" -c Release -o "$feed"
done

sha=$(git -C "$server" rev-parse HEAD)
printf '%s %s\n' "$ref" "$sha" > "$feed/CONTRACT"

find "$feed" -maxdepth 1 -name '*.nupkg' -print
echo "contract packed from nomercy-media-server $ref ($sha)"
```

- [ ] **Step 3: Rewrite `scripts/fetch-abstractions.ps1` as its twin**

Replace the whole file with:

```powershell
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
    (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe')
    (Join-Path $env:HOME '.dotnet/dotnet')
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
```

- [ ] **Step 4: Run the script and check what it produced**

First make sure the sibling checkout is untouched before and after:
```bash
git -C ../nomercy-media-server status --short | head -3; git -C ../nomercy-media-server rev-parse HEAD
./scripts/fetch-abstractions.sh
ls _nupkgs; cat _nupkgs/CONTRACT
git -C ../nomercy-media-server status --short | head -3; git -C ../nomercy-media-server rev-parse HEAD
```
Expected:
- `_nupkgs` holds `NoMercy.PluginSdk.Abstractions.12.1.0.nupkg` and `NoMercy.PluginSdk.Mvc.12.1.0.nupkg` (plus `.snupkg`).
- `CONTRACT` reads `v0.1.526 <sha>` or a newer tag.
- The sibling's status and HEAD are identical before and after.

Then run `pwsh scripts/fetch-abstractions.ps1` and confirm it produces the same `CONTRACT` line.

- [ ] **Step 5: Switch the package references**

In `src/NoMercy.Plugin.InternetRadio/NoMercy.Plugin.InternetRadio.csproj`, replace the two `PackageReference` elements and the comment blocks above them with:

```xml
        <!--
            The host owns these assemblies at runtime: they are in the server's shared-assembly
            set, so the plugin load context resolves them to the host's copies. Do NOT set
            CopyLocalLockFileAssemblies=true: a copy beside the plugin gives the load context two
            incompatible identities of the same types, which surfaces as a cast error far from
            its cause. The CI packaging step asserts none of them ships.

            12.*, floating within the major. Plugin ABI 12 renamed the SDK assembly and every
            namespace in it; a build against 11 or earlier resolves no type at all, and the
            server refuses such a plugin at load. packageSourceMapping in nuget.config confines
            NoMercy.* to the local feed, so this cannot resolve to someone else's package of the
            same name. The exact server commit a build used is recorded in _nupkgs/CONTRACT.
        -->
        <PackageReference Include="NoMercy.PluginSdk.Abstractions" Version="12.*" />

        <!--
            PluginControllerBase, which the favourite toggle, the search submit and the
            now-playing poll arrive through. Host-owned exactly like the abstractions.
        -->
        <PackageReference Include="NoMercy.PluginSdk.Mvc" Version="12.*" />
```

Change `<Version>1.2.1</Version>` to `<Version>2.0.0</Version>`.

In `tests/NoMercy.Plugin.InternetRadio.Tests/NoMercy.Plugin.InternetRadio.Tests.csproj`, replace the two `NoMercy.Plugins.*` references with:

```xml
        <PackageReference Include="NoMercy.PluginSdk.Abstractions" Version="12.*" />
        <PackageReference Include="NoMercy.PluginSdk.Mvc" Version="12.*" />
```

Keep the comment above them. Change its "Same floating version" sentence to "Same version range on purpose".

- [ ] **Step 6: Rename the namespaces**

```bash
grep -rl --include=*.cs -e 'NoMercy\.Plugins\.Abstractions' -e 'NoMercy\.Plugins\.Mvc' src tests \
  | xargs sed -i 's/NoMercy\.Plugins\.Abstractions/NoMercy.PluginSdk.Abstractions/g; s/NoMercy\.Plugins\.Mvc/NoMercy.PluginSdk.Mvc/g'
grep -rn --include=*.cs 'NoMercy\.Plugins\.' src tests
```
Expected: the last grep prints nothing.

- [ ] **Step 7: Manifest and identity to 2.0.0 / ABI 12.1**

In `src/NoMercy.Plugin.InternetRadio/plugin.json`, set `"version": "2.0.0"` and `"targetAbi": "12.1"`. In `PluginIdentity.cs`, set the version to `new Version(2, 0, 0)` (match however the file currently writes it).

- [ ] **Step 8: Remove the two `Services` reads**

In `InternetRadioPlugin.cs`:
- Delete the methods `CallerBearerToken`, `ReadCallerBearerToken` and `PublicBaseUrl`, with their doc comments.
- Delete the `MediaProxy.Remember(PublicBaseUrl(), CallerBearerToken());` line and the three comment lines above it in `GetViewAsync`.
- Delete `using Microsoft.AspNetCore.Http;`.

`MediaProxy` itself stays until Task 7.

- [ ] **Step 9: Build the plugin and read the errors**

Run: `/c/Users/philp/.dotnet/dotnet.exe build src/NoMercy.Plugin.InternetRadio -c Release -p:TreatWarningsAsErrors=true 2>&1 | grep -E "error|Warn|Error\(s\)"`

Expected: `0 Error(s)`. If `IPluginContext` reports another missing member, stop and compare against `_server/src/NoMercy.PluginSdk.Abstractions/IPluginContext.cs`. Do not guess.

- [ ] **Step 10: Bring the test support onto the new contract**

Replace the body of `tests/.../TestSupport/FakePluginContext.cs` below the `using` lines with:

```csharp
namespace NoMercy.Plugin.InternetRadio.Tests.TestSupport;

public sealed class FakePluginContext(string dataFolderPath, HttpClient httpClient) : IPluginContext
{
    public ILogger Logger { get; } = new RecordingLogger();
    public string DataFolderPath { get; } = dataFolderPath;
    public HttpClient HttpClient { get; } = httpClient;
    public Ulid PluginId => PluginIdentity.Id;

    public RecordingLogger Recorded => (RecordingLogger)Logger;

    // Not used by this plugin. Throwing rather than returning a null object means a
    // test cannot accidentally pass while the plugin reaches for something it should
    // not. Every facade this class does not name - Media among them - keeps the
    // interface's own default, which throws PluginRefusedException exactly as a host
    // without that facade does.
    public IPluginEvents Events => throw new NotSupportedException();
    public IPluginConfiguration Configuration => throw new NotSupportedException();
    public IPluginSecretStore Secrets => throw new NotSupportedException();
    public IPluginLibraryQuery Library => throw new NotSupportedException();
    public IPluginLibraryWriter? LibraryWriter => null;
    public IPluginGrants Grants => throw new NotSupportedException();
    public IPluginHubContext Hub => throw new NotSupportedException();

    public Task PublishAsync<T>(string name, T payload, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
```

Remove `using NoMercy.Events;` from that file.

Create `tests/NoMercy.Plugin.InternetRadio.Tests/TestSupport/TestCaller.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;

namespace NoMercy.Plugin.InternetRadio.Tests.TestSupport;

/// <summary>
/// A caller as the host builds one: the account's Guid, carried as a Ulid over the same
/// sixteen bytes (PluginUiController does exactly `new UserId(new Ulid(guid))`).
/// </summary>
public static class TestCaller
{
    public static PluginCaller For(Guid user) =>
        new(new UserId(new Ulid(user)), "Test", PluginRole.Member, PluginAccess.Shared, "en", PluginSurface.Web);

    public static PluginCaller Anyone { get; } = For(Guid.Parse("11111111-2222-3333-4444-555555555555"));
}
```

In `PluginLifecycleTests.cs`, change the request helper to:

```csharp
    private static PluginViewRequest Request(string route) => new() { Route = route, Caller = TestCaller.Anyone };
```

In `DiscoveryContractTests.cs:95` change `ManifestTests.LoadManifest().Id` to `ManifestTests.LoadManifest().Id.Value`. In `ManifestTests.cs:30` change `manifest.Id.Should().NotBe(default(Ulid));` to `manifest.Id.Value.Should().NotBe(default(Ulid));`, and at line 40 change it to `LoadManifest().Id.Value.Should().Be(PluginIdentity.Id);`.

- [ ] **Step 11: Build everything and run the suite**

```bash
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true 2>&1 | grep -E "Warn|Error\(s\)"
/c/Users/philp/.dotnet/dotnet.exe test -c Release --no-build 2>&1 | grep -E "Failed |Passed!|Failed!"
```
Expected: `0 Warning(s)`, `0 Error(s)`, `Failed: 0` and `Skipped: 1`.

The `StationView`/`StationCards` tests that call `MediaProxy.Remember(...)` still pass, because they set the static themselves.

- [ ] **Step 12: CI onto the same contract**

In `.forgejo/workflows/build.yml`:

1. Delete the `SERVER_BRANCH: master` env line. Keep `SERVER_DIR: _server`.
2. In the header comment, change `NoMercy.Plugins.Abstractions` to `NoMercy.PluginSdk.Abstractions`.
3. Replace the whole `run:` of the "Pack the plugin contract to a local feed" step with:

```yaml
        run: |
          set -eu
          chmod +x scripts/fetch-abstractions.sh

          # Every build - push, pull request and tag alike - packs the newest server release
          # tag, which is what an owner runs. The tag and its commit are recorded in
          # _nupkgs/CONTRACT, so a release names the exact contract it was built against.
          ./scripts/fetch-abstractions.sh

          read -r CONTRACT_REF CONTRACT_SHA < _nupkgs/CONTRACT
          echo "ref=$CONTRACT_REF" >> "$GITHUB_OUTPUT"
          echo "sha=$CONTRACT_SHA" >> "$GITHUB_OUTPUT"
```

4. In "Stage the plugin directory":
   - Replace the "Deliberately NOT shipped" comment's first paragraph so it lists `NoMercy.PluginSdk.Abstractions.dll`, `NoMercy.PluginSdk.Mvc.dll`, `NoMercy.Events.dll`, `NoMercy.Design.dll`, `Newtonsoft.Json.dll` and `Ulid.dll`.
   - Replace the check line with:

```yaml
          if ls "$BIN" | grep -E '^(NoMercy\.(PluginSdk\.(Abstractions|Mvc)|Plugins\.(Abstractions|Mvc)|Events|Design)|Newtonsoft\.Json|Ulid)\.dll$'; then
```

   - In the comment further down, change "like today's NoMercy.Plugins.Abstractions" to "like today's NoMercy.PluginSdk.Abstractions".

The old `Plugins.*` names stay in the pattern on purpose: a stale feed must never ship them either.

5. Confirm locally that the check does not fire on a real build:
```bash
ls src/NoMercy.Plugin.InternetRadio/bin/Release/net10.0 | grep -E '^(NoMercy\.(PluginSdk\.(Abstractions|Mvc)|Plugins\.(Abstractions|Mvc)|Events|Design)|Newtonsoft\.Json|Ulid)\.dll$' || echo "clean"
```
Expected: `clean`.

The release-notes text is rewritten in Task 8, not here.

- [ ] **Step 13: Commit**

```bash
git add scripts .forgejo/workflows/build.yml src tests
git status --short
git commit -m "build: onto plugin ABI 12.1, packed from the newest server release

The server every owner is offered now (v0.1.526) is ABI 12.1 and refuses a
plugin built for 10. The SDK is NoMercy.PluginSdk.* and carries Events and
Design inside it. fetch-abstractions now packs the newest release tag into
_server/ inside this repository instead of hard-resetting a sibling checkout,
and records the tag and commit in _nupkgs/CONTRACT.

IPluginContext.Services is gone, so the viewer's token and the server's
address can no longer be read; the relay that needed them is replaced in the
tasks that follow. Version 2.0.0."
```

---

### Task 2: One user key, in the Guid form

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/State/UserKey.cs`
- Modify: `src/NoMercy.Plugin.InternetRadio/Controllers/InternetRadioController.cs` (`CurrentUserId`)
- Modify: `src/NoMercy.Plugin.InternetRadio/InternetRadioPlugin.cs` (`GetViewAsync` user state read)
- Test: `tests/NoMercy.Plugin.InternetRadio.Tests/State/UserKeyTests.cs`, `tests/NoMercy.Plugin.InternetRadio.Tests/PluginLifecycleTests.cs`

**Interfaces:**
- Consumes: `TestCaller.For(Guid)` (Task 1).
- Produces: `UserKey.FromClaim(string? claim) : string?` and `UserKey.FromCaller(UserId caller) : string?`. Both return the lowercase Guid "D" form, or null for no one.

- [ ] **Step 1: Write the failing tests**

Create `tests/NoMercy.Plugin.InternetRadio.Tests/State/UserKeyTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.PluginSdk.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.State;

public class UserKeyTests
{
    private static readonly Guid Someone = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    // The whole point: the controller hears a Guid claim and a view hears a Ulid over the
    // same bytes. If these two ever disagree, a favourite is written where no page reads.
    [Fact]
    public void TheControllersClaimAndTheViewsCallerAreTheSameKey()
    {
        string? fromClaim = UserKey.FromClaim(Someone.ToString());
        string? fromCaller = UserKey.FromCaller(new UserId(new Ulid(Someone)));

        fromClaim.Should().NotBeNull();
        fromCaller.Should().Be(fromClaim);
    }

    // 1.x stored every user under Guid.ToString(). Anything else orphans their favourites.
    [Fact]
    public void TheKeyIsTheGuidFormOneXStoredUnder()
    {
        UserKey.FromCaller(new UserId(new Ulid(Someone))).Should().Be("0f8fad5b-d9cb-469f-a165-70867728950e");
        UserKey.FromClaim("0F8FAD5B-D9CB-469F-A165-70867728950E").Should().Be("0f8fad5b-d9cb-469f-a165-70867728950e");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void NoOneHasNoKey(string? claim)
    {
        UserKey.FromClaim(claim).Should().BeNull();
    }

    [Fact]
    public void AnEmptyCallerHasNoKey()
    {
        UserKey.FromCaller(UserId.Empty).Should().BeNull();
    }
}
```

Add to `PluginLifecycleTests.cs`, next to the other view tests:

```csharp
    // The regression 2.0 exists to prevent: the toggle arrives through the controller
    // with a Guid claim, the page is built for a Ulid caller over the same bytes, and the
    // favourite has to be on that page.
    [Fact]
    public async Task AFavouriteToggledThroughTheControllerShowsOnThatPersonsPage()
    {
        Guid user = Guid.NewGuid();
        using InternetRadioPlugin plugin = Started();

        await plugin.ToggleFavouriteAsync(UserKey.FromClaim(user.ToString()), "u1", CancellationToken.None);

        PluginView view = await plugin.GetViewAsync(
            new PluginViewRequest { Route = RadioRoutes.Browse, Caller = TestCaller.For(user) },
            CancellationToken.None);

        HasNode(view, "station-tile-fav-u1").Should().BeTrue();
    }

    // State written by 1.x, before any of this: keyed by Guid.ToString().
    [Fact]
    public async Task FavouritesSavedByOneXAreStillThere()
    {
        Guid user = Guid.NewGuid();
        using InternetRadioPlugin plugin = Started();
        UserStateStore store = new(_folder);
        await store.AddFavouriteAsync(
            user.ToString(),
            new RadioStation { Id = "kept", Name = "Kept FM", StreamUrl = "https://example.com/k" },
            CancellationToken.None);

        PluginView view = await plugin.GetViewAsync(
            new PluginViewRequest { Route = RadioRoutes.Browse, Caller = TestCaller.For(user) },
            CancellationToken.None);

        HasNode(view, "station-tile-fav-kept").Should().BeTrue();
    }
```

The browse page draws the favourites grid with scope `fav` (`BrowseView.cs:67`), so a kept station's tile id is `station-tile-fav-{id}`.

- [ ] **Step 2: Run them to verify they fail**

Run: `/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter "FullyQualifiedName~UserKeyTests|FullyQualifiedName~PluginLifecycleTests" 2>&1 | grep -E "error|Failed |Passed!|Failed!"`

Expected: a compile error, `The name 'UserKey' does not exist`.

- [ ] **Step 3: Implement `UserKey`**

Create `src/NoMercy.Plugin.InternetRadio/State/UserKey.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// The key a viewer's favourites and last search are stored under.
///
/// One place, because the host now names the same person two ways. The controller reads
/// the account's Guid from its claim; a view is handed a UserId, which is a Ulid over the
/// same sixteen bytes and prints as a different string. Reading each as it comes wrote a
/// favourite under one key and looked for it under the other, so a toggle appeared to
/// do nothing - and every favourite saved by 1.x, all under the Guid, vanished from view.
///
/// The Guid form wins because that is what every byte of existing state is keyed by.
/// </summary>
public static class UserKey
{
    /// <summary>From the controller's NameIdentifier claim. Null for no one.</summary>
    public static string? FromClaim(string? claim) =>
        Guid.TryParse(claim, out Guid id) ? Of(id) : null;

    /// <summary>From the caller a view is built for. Null for no one.</summary>
    public static string? FromCaller(UserId caller) =>
        caller == UserId.Empty ? null : Of(caller.Value.ToGuid());

    // Null rather than a shared bucket for the empty id: every anonymous caller writing
    // into one list would be favourites belonging to nobody, visible to everybody.
    private static string? Of(Guid id) => id == Guid.Empty ? null : id.ToString();
}
```

- [ ] **Step 4: Use it on both sides**

In `InternetRadioController.cs`, replace the body of `CurrentUserId()` with:

```csharp
    private string? CurrentUserId() =>
        UserKey.FromClaim(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
```

Keep its doc comment. Add one sentence to it: "Not `PluginControllerBase.Caller`: the host never populates it, and reading it throws."

In `InternetRadioPlugin.GetViewAsync`, replace the `UserState state = request.UserId is null ? ... ;` statement with:

```csharp
            // Through UserKey, never request.UserId: that is the Ulid spelling of the same
            // person, and state is keyed by the Guid spelling the controller writes.
            string? userKey = UserKey.FromCaller(request.CallerId);
            UserState state = userKey is null
                ? UserState.Empty
                : await StateStore.GetAsync(userKey, ct);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run the full suite:
```bash
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true 2>&1 | grep -E "Warn|Error\(s\)"
/c/Users/philp/.dotnet/dotnet.exe test -c Release --no-build 2>&1 | grep -E "Failed |Passed!|Failed!"
```
Expected: all pass, including the 8 new cases.

If `TheKeyIsTheGuidFormOneXStoredUnder` fails, `Ulid(Guid)` and `ToGuid()` do not round-trip in the packaged `Ulid` version. That is a real finding. Stop and report it: do not work around it.

- [ ] **Step 6: Commit**

```bash
git add src/NoMercy.Plugin.InternetRadio/State/UserKey.cs src/NoMercy.Plugin.InternetRadio/Controllers/InternetRadioController.cs src/NoMercy.Plugin.InternetRadio/InternetRadioPlugin.cs tests
git commit -m "fix(state): one key per person, in the form 1.x stored

ABI 12 hands a view its caller as a Ulid over the account's Guid bytes,
while the controller still reads the Guid claim. Each side keyed state the
way it heard it, so a favourite was written where no page looked and every
favourite saved by 1.x disappeared from view. UserKey turns both into the
Guid form existing state is already keyed by."
```

---

### Task 3: Logos as data URIs, the pieces

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/ImageSniffer.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/LogoFetcher.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Views/IStationLogos.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/LogoCache.cs`
- Test: `tests/.../Catalog/ImageSnifferTests.cs`, `tests/.../Catalog/LogoFetcherTests.cs`, `tests/.../Catalog/LogoCacheTests.cs`

**Interfaces:**
- Produces: `ImageSniffer.MediaType(ReadOnlySpan<byte>) : string?`.
- Produces: `LogoFetcher(HttpClient)` with `const int MaxBytes = 16384` and `Task<LogoResult> FetchAsync(string url, CancellationToken ct)`. `LogoResult` is a record `(string? DataUri, bool IsFinal)`. `IsFinal` is false only for a failure worth retrying (network, timeout, 5xx).
- Produces: `interface IStationLogos { string? DataUriFor(RadioStation station); }` and `sealed class NoStationLogos : IStationLogos` with `Instance`.
- Produces: `LogoCache(string dataFolderPath, int capacity = 2000) : IStationLogos` with:
  - `static string? SourceOf(RadioStation)` (an absolute http or https logo URL, or null);
  - `bool Has(RadioStation)`;
  - `IReadOnlyList<RadioStation> TakePending()`;
  - `Task StoreAsync(RadioStation, string? dataUri, CancellationToken)`.

- [ ] **Step 1: Write the failing sniffer tests**

Create `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/ImageSnifferTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text;
using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public class ImageSnifferTests
{
    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0 }, "image/gif")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61, 0 }, "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50, 0 }, "image/webp")]
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0x00, 1, 0 }, "image/x-icon")]
    public void RecognisesTheRasterFormatsByTheirOwnMagic(byte[] bytes, string expected)
    {
        ImageSniffer.MediaType(bytes).Should().Be(expected);
    }

    [Theory]
    [InlineData("<svg viewBox='0 0 1 1'></svg>")]
    [InlineData("\uFEFF  <?xml version='1.0'?><svg></svg>")]
    [InlineData("<!-- logo --><svg></svg>")]
    public void RecognisesAnSvgDocument(string text)
    {
        ImageSniffer.MediaType(Encoding.UTF8.GetBytes(text)).Should().Be("image/svg+xml");
    }

    // The ordinary failure: a logo url that rotted into the station's HTML home page,
    // which may well contain an inline svg icon somewhere in it.
    [Theory]
    [InlineData("<!DOCTYPE html><html><body><svg></svg></body></html>")]
    [InlineData("<html><svg></svg></html>")]
    [InlineData("Not Found")]
    [InlineData("")]
    public void RefusesAnythingThatIsNotAnImage(string text)
    {
        ImageSniffer.MediaType(Encoding.UTF8.GetBytes(text)).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter FullyQualifiedName~ImageSnifferTests 2>&1 | grep -E "error CS|Failed!|Passed!"`
Expected: `error CS0103: The name 'ImageSniffer' does not exist`.

- [ ] **Step 3: Implement `ImageSniffer`**

Create `src/NoMercy.Plugin.InternetRadio/Catalog/ImageSniffer.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// What kind of image a response is, judged by its bytes.
///
/// Not by its Content-Type. A logo url on a volunteer-edited directory rots in ordinary
/// ways - into the station's home page, a 200 "not found" page, a parked domain - and
/// every one of those still says it is whatever the server felt like saying. Handing any
/// of them to an img as a data: URI draws a broken icon that reads as this plugin being
/// broken.
/// </summary>
public static class ImageSniffer
{
    public static string? MediaType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0x01, 0x00]))
        {
            return "image/x-icon";
        }

        return IsSvgDocument(bytes) ? "image/svg+xml" : null;
    }

    // An svg document, not a document that happens to contain one. Safe to hand over:
    // an img element never runs an svg's scripts.
    private static bool IsSvgDocument(ReadOnlySpan<byte> bytes)
    {
        string head = Encoding.UTF8.GetString(bytes[..Math.Min(bytes.Length, 1024)])
            .TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

        if (head.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return (head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
                || head.StartsWith("<?xml", StringComparison.Ordinal)
                || head.StartsWith("<!--", StringComparison.Ordinal))
            && head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run the same filter. Expected: `Passed!` with 13 tests.

- [ ] **Step 5: Write the failing fetcher tests**

Create `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/LogoFetcherTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Net;
using System.Text;
using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public class LogoFetcherTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static LogoFetcher Fetcher(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        FakeHttpMessageHandler handler = new();
        handler.RespondPerRequest(respond);
        return new LogoFetcher(new HttpClient(handler));
    }

    private static HttpResponseMessage Bytes(byte[] body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new ByteArrayContent(body) };

    [Fact]
    public async Task ASmallImageBecomesADataUri()
    {
        LogoResult result = await Fetcher(_ => Bytes(Png)).FetchAsync("https://example.com/l.png", CancellationToken.None);

        result.DataUri.Should().Be($"data:image/png;base64,{Convert.ToBase64String(Png)}");
        result.IsFinal.Should().BeTrue();
    }

    // The cap is what keeps a page of logos a reasonable size; nothing is resized.
    [Fact]
    public async Task AnImageOverTheCapIsRefusedForGood()
    {
        byte[] big = [.. Png, .. new byte[LogoFetcher.MaxBytes]];

        LogoResult result = await Fetcher(_ => Bytes(big)).FetchAsync("https://example.com/l.png", CancellationToken.None);

        result.DataUri.Should().BeNull();
        result.IsFinal.Should().BeTrue();
    }

    [Fact]
    public async Task AnImageExactlyAtTheCapIsKept()
    {
        byte[] exact = [.. Png, .. new byte[LogoFetcher.MaxBytes - Png.Length]];

        LogoResult result = await Fetcher(_ => Bytes(exact)).FetchAsync("https://example.com/l.png", CancellationToken.None);

        result.DataUri.Should().StartWith("data:image/png;base64,");
    }

    [Fact]
    public async Task APageThatIsNotAnImageIsRefusedForGood()
    {
        LogoResult result = await Fetcher(_ => Bytes(Encoding.UTF8.GetBytes("<html>moved</html>")))
            .FetchAsync("https://example.com/l.png", CancellationToken.None);

        result.Should().Be(new LogoResult(null, IsFinal: true));
    }

    [Fact]
    public async Task AMissingLogoIsRefusedForGood()
    {
        LogoResult result = await Fetcher(_ => Bytes([], HttpStatusCode.NotFound))
            .FetchAsync("https://example.com/l.png", CancellationToken.None);

        result.Should().Be(new LogoResult(null, IsFinal: true));
    }

    // A server having a bad minute is not a verdict on the logo.
    [Fact]
    public async Task AServerErrorIsWorthTryingAgain()
    {
        LogoResult result = await Fetcher(_ => Bytes([], HttpStatusCode.BadGateway))
            .FetchAsync("https://example.com/l.png", CancellationToken.None);

        result.Should().Be(new LogoResult(null, IsFinal: false));
    }

    [Fact]
    public async Task ANetworkFailureIsWorthTryingAgain()
    {
        LogoResult result = await Fetcher(_ => throw new HttpRequestException("down"))
            .FetchAsync("https://example.com/l.png", CancellationToken.None);

        result.Should().Be(new LogoResult(null, IsFinal: false));
    }

    [Fact]
    public async Task TheCallersOwnCancellationStillCancels()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> fetch = () => Fetcher(_ => Bytes(Png)).FetchAsync("https://example.com/l.png", cts.Token);

        await fetch.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 6: Run to verify it fails**

Run: `/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter FullyQualifiedName~LogoFetcherTests 2>&1 | grep -E "error CS|Failed!|Passed!"`
Expected: `error CS0246: The type or namespace name 'LogoFetcher'`.

If `FakeHttpMessageHandler.RespondPerRequest` does not honour the token before calling the responder, `TheCallersOwnCancellationStillCancels` may pass through the fetcher's own check instead. Either way is acceptable, as long as it throws.

- [ ] **Step 7: Implement `LogoFetcher`**

Create `src/NoMercy.Plugin.InternetRadio/Catalog/LogoFetcher.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

/// <summary>What fetching one logo came to. Final unless it is worth trying again.</summary>
public sealed record LogoResult(string? DataUri, bool IsFinal);

/// <summary>
/// One station logo, fetched by the server and handed to the browser inline.
///
/// Inline because nothing else reaches the page. The dashboard's image policy refuses a
/// station's own host; this plugin's old relay route is refused for carrying a token in
/// its url (ABI 11 and up); and an image the host mints cannot be drawn by the web client,
/// which sends no credential for it. A data: URI needs none of them.
///
/// Nothing is resized - that would need an image library in the plugin. The cap is what
/// bounds the page instead: a grid of eighty logos at the cap is still a page, where eighty
/// full-size photographs would not be.
/// </summary>
public sealed class LogoFetcher(HttpClient http)
{
    public const int MaxBytes = 16 * 1024;

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    public async Task<LogoResult> FetchAsync(string url, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Budget);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            using HttpResponseMessage response =
                await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);

            if ((int)response.StatusCode >= 500)
            {
                return new LogoResult(null, IsFinal: false);
            }

            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaxBytes)
            {
                return new LogoResult(null, IsFinal: true);
            }

            // One byte past the cap is enough to know it is over, without reading the rest.
            byte[] buffer = new byte[MaxBytes + 1];
            int total = 0;

            await using Stream body = await response.Content.ReadAsStreamAsync(budget.Token);

            int read;
            while (total < buffer.Length
                   && (read = await body.ReadAsync(buffer.AsMemory(total), budget.Token)) > 0)
            {
                total += read;
            }

            if (total is 0 or > MaxBytes)
            {
                return new LogoResult(null, IsFinal: true);
            }

            ReadOnlySpan<byte> bytes = buffer.AsSpan(0, total);

            return ImageSniffer.MediaType(bytes) is { } type
                ? new LogoResult($"data:{type};base64,{Convert.ToBase64String(bytes)}", IsFinal: true)
                : new LogoResult(null, IsFinal: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            // A timeout is the budget firing, not the caller: the logo may simply be slow today.
            return new LogoResult(null, IsFinal: false);
        }
    }
}
```

- [ ] **Step 8: Run to verify it passes**

Run the same filter. Expected: `Passed!` with 8 tests.

- [ ] **Step 9: Write the failing cache tests**

Create `src/NoMercy.Plugin.InternetRadio/Views/IStationLogos.cs` first, because the tests name it:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// A station's logo, as a view may draw it: a data: URI, or null for the placeholder.
///
/// An interface so views stay pure functions. The plugin hands its cache in; a test hands
/// in whatever it needs; nothing under Views/ ever reaches the network for one.
/// </summary>
public interface IStationLogos
{
    string? DataUriFor(RadioStation station);
}

/// <summary>No logos: every card draws its own placeholder.</summary>
public sealed class NoStationLogos : IStationLogos
{
    public static NoStationLogos Instance { get; } = new();

    public string? DataUriFor(RadioStation station) => null;
}
```

Create `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/LogoCacheTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public sealed class LogoCacheTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"nm-logos-{Guid.NewGuid():N}");

    public LogoCacheTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static RadioStation Station(string id, string? logo = "https://example.com/logo.png") =>
        new() { Id = id, Name = id, StreamUrl = "https://example.com/s", LogoUrl = logo };

    private const string Data = "data:image/png;base64,iVBORw0KGgo=";

    [Theory]
    [InlineData("https://example.com/l.png", true)]
    [InlineData("http://example.com/l.png", true)]
    [InlineData("/relative/l.png", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyAnAbsoluteWebAddressIsALogoSource(string? logo, bool admitted)
    {
        (LogoCache.SourceOf(Station("a", logo)) is not null).Should().Be(admitted);
    }

    [Fact]
    public void AnUnknownLogoIsAskedForOnceAndDrawsThePlaceholderMeanwhile()
    {
        LogoCache cache = new(_folder);

        cache.DataUriFor(Station("a")).Should().BeNull();
        cache.DataUriFor(Station("a")).Should().BeNull();

        cache.TakePending().Select(station => station.Id).Should().Equal("a");
        cache.TakePending().Should().BeEmpty();
    }

    [Fact]
    public void AStationWithoutALogoIsNeverAskedFor()
    {
        LogoCache cache = new(_folder);

        cache.DataUriFor(Station("a", logo: null)).Should().BeNull();

        cache.TakePending().Should().BeEmpty();
    }

    [Fact]
    public async Task AStoredLogoIsDrawnAndSurvivesARestart()
    {
        await new LogoCache(_folder).StoreAsync(Station("a"), Data, CancellationToken.None);

        LogoCache restarted = new(_folder);

        restarted.DataUriFor(Station("a")).Should().Be(Data);
        restarted.Has(Station("a")).Should().BeTrue();
        restarted.TakePending().Should().BeEmpty();
    }

    // A rejected logo is remembered, or every view would fetch it again.
    [Fact]
    public async Task ARejectedLogoIsRememberedAsNone()
    {
        LogoCache cache = new(_folder);
        await cache.StoreAsync(Station("a"), dataUri: null, CancellationToken.None);

        cache.DataUriFor(Station("a")).Should().BeNull();
        cache.Has(Station("a")).Should().BeTrue();
        cache.TakePending().Should().BeEmpty();
    }

    // A station that moved its logo gets the new one, not the old one forever.
    [Fact]
    public async Task ANewLogoAddressIsANewLogo()
    {
        LogoCache cache = new(_folder);
        await cache.StoreAsync(Station("a"), Data, CancellationToken.None);

        cache.DataUriFor(Station("a", "https://example.com/moved.png")).Should().BeNull();
        cache.TakePending().Select(station => station.Id).Should().Equal("a");
    }

    [Fact]
    public async Task TheLeastRecentlyUsedLogoIsEvictedPastTheCapacity()
    {
        LogoCache cache = new(_folder, capacity: 2);
        await cache.StoreAsync(Station("a"), Data, CancellationToken.None);
        await cache.StoreAsync(Station("b"), Data, CancellationToken.None);
        cache.DataUriFor(Station("a"));

        await cache.StoreAsync(Station("c"), Data, CancellationToken.None);

        cache.Has(Station("a")).Should().BeTrue();
        cache.Has(Station("b")).Should().BeFalse();
        cache.Has(Station("c")).Should().BeTrue();
        new LogoCache(_folder, capacity: 2).Has(Station("b")).Should().BeFalse();
    }
}
```

- [ ] **Step 10: Run to verify it fails**

Run: `/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter FullyQualifiedName~LogoCacheTests 2>&1 | grep -E "error CS|Failed!|Passed!"`
Expected: `error CS0246: The type or namespace name 'LogoCache'`.

- [ ] **Step 11: Implement `LogoCache`**

Create `src/NoMercy.Plugin.InternetRadio/Catalog/LogoCache.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// Station logos as data: URIs, kept on disk so a restart does not fetch them all again.
///
/// A view only ever reads from here. A logo that is not here yet is noted as wanted and
/// the card draws its placeholder; the plugin fetches what is wanted after the page has
/// gone out, so no page ever waits on a logo server. A logo that was refused - too big,
/// not an image, gone - is stored as "none", or every page would fetch it again.
///
/// Keyed by station and logo address together, so a station that moves its logo gets the
/// new one. Bounded at <see cref="DefaultCapacity"/> entries, least recently used first
/// out; at the 16 KB fetch cap that is at most about 40 MB of text on disk.
/// </summary>
public sealed class LogoCache : IStationLogos
{
    public const int DefaultCapacity = 2000;

    private const string FolderName = "logos";

    private readonly string _folder;
    private readonly int _capacity;
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, RadioStation> _pending = new();
    private readonly Lock _evicting = new();
    private long _clock;

    public LogoCache(string dataFolderPath, int capacity = DefaultCapacity)
    {
        _folder = Path.Combine(dataFolderPath, FolderName);
        _capacity = capacity;
        Directory.CreateDirectory(_folder);

        // Oldest first, so the ones read back most recently are the ones that stay.
        foreach (FileInfo file in new DirectoryInfo(_folder).GetFiles("*.txt").OrderBy(file => file.LastWriteTimeUtc))
        {
            _entries[Path.GetFileNameWithoutExtension(file.Name)] = new Entry(null, Loaded: false, Tick());
        }

        Evict();
    }

    /// <summary>An absolute http or https address, or null. The browser never sees it.</summary>
    public static string? SourceOf(RadioStation station) =>
        Uri.TryCreate(station.LogoUrl, UriKind.Absolute, out Uri? parsed)
        && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp)
            ? station.LogoUrl
            : null;

    public string? DataUriFor(RadioStation station)
    {
        if (SourceOf(station) is not { } source)
        {
            return null;
        }

        string key = Key(station.Id, source);

        if (!_entries.TryGetValue(key, out Entry? entry))
        {
            _pending.TryAdd(key, station);
            return null;
        }

        if (!entry.Loaded)
        {
            entry = entry with { DataUri = Read(key), Loaded = true };
        }

        _entries[key] = entry with { LastUsed = Tick() };

        return entry.DataUri;
    }

    public bool Has(RadioStation station) =>
        SourceOf(station) is { } source && _entries.ContainsKey(Key(station.Id, source));

    /// <summary>Everything views asked for since the last call, each once.</summary>
    public IReadOnlyList<RadioStation> TakePending()
    {
        List<RadioStation> taken = [];

        foreach (string key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out RadioStation? station))
            {
                taken.Add(station);
            }
        }

        return taken;
    }

    /// <summary>A fetched logo, or null for one that was refused for good.</summary>
    public async Task StoreAsync(RadioStation station, string? dataUri, CancellationToken ct)
    {
        if (SourceOf(station) is not { } source)
        {
            return;
        }

        string key = Key(station.Id, source);

        // Written whole, then moved into place: a crash mid-write must not leave a half
        // data URI that draws as a broken image on every page from then on.
        string path = PathOf(key);
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporary, dataUri ?? string.Empty, ct);
        File.Move(temporary, path, overwrite: true);

        _entries[key] = new Entry(dataUri, Loaded: true, Tick());
        _pending.TryRemove(key, out _);

        Evict();
    }

    private void Evict()
    {
        lock (_evicting)
        {
            int excess = _entries.Count - _capacity;
            if (excess <= 0)
            {
                return;
            }

            foreach (string key in _entries.OrderBy(pair => pair.Value.LastUsed).Take(excess).Select(pair => pair.Key).ToList())
            {
                _entries.TryRemove(key, out _);
                File.Delete(PathOf(key));
            }
        }
    }

    private string? Read(string key)
    {
        string path = PathOf(key);
        if (!File.Exists(path))
        {
            return null;
        }

        string text = File.ReadAllText(path);
        return text.Length == 0 ? null : text;
    }

    private string PathOf(string key) => Path.Combine(_folder, $"{key}.txt");

    private long Tick() => Interlocked.Increment(ref _clock);

    // Hashed rather than used as-is: a station id and a url are not file names.
    private static string Key(string stationId, string source) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{stationId}\n{source}")))[..32];

    private sealed record Entry(string? DataUri, bool Loaded, long LastUsed);
}
```

- [ ] **Step 12: Run to verify it passes**

Run the same filter. Expected: `Passed!` with 11 tests. Then run the full suite. Expected: all green.

- [ ] **Step 13: Commit**

```bash
git add src/NoMercy.Plugin.InternetRadio/Catalog/ImageSniffer.cs src/NoMercy.Plugin.InternetRadio/Catalog/LogoFetcher.cs src/NoMercy.Plugin.InternetRadio/Catalog/LogoCache.cs src/NoMercy.Plugin.InternetRadio/Views/IStationLogos.cs tests
git commit -m "feat(logos): fetch and keep station logos as data URIs

Nothing else reaches the page on ABI 12: the image policy refuses a
station's host, the plugin's own relay is refused for its token, and the web
client cannot draw an image the host mints. A logo is admitted only when it
is an image by its bytes and at most 16 KB; a refusal is remembered, and the
cache keeps 2,000 of them, least recently used first out."
```

---

### Task 4: Logos on every card, fetched after the page

**Files:**
- Modify: `src/.../Views/StationCards.cs` (`Grid`, `Tile`; `CoverUrl` removed)
- Modify: `src/.../Views/BrowseView.cs`, `GenreView.cs`, `AllStationsView.cs`, `SearchView.cs` (a trailing `IStationLogos? logos = null` parameter, passed to every `StationCards.Grid`)
- Modify: `src/.../Views/StationView.cs` (the detail image from `logos`; `MediaProxy.Cover` gone)
- Modify: `src/.../InternetRadioPlugin.cs` (the `Logos` property, passing it to views, the background fetch, warming after refresh; `FetchStationMediaAsync` uses `LogoCache.SourceOf`)
- Test: `tests/.../Views/StationCardsTests.cs`, `tests/.../Views/StationViewTests.cs`, `tests/.../PluginLifecycleTests.cs`

**Interfaces:**
- Consumes: `IStationLogos`, `NoStationLogos`, `LogoCache`, `LogoFetcher`, `LogoResult` (Task 3).
- Produces: `StationCards.Grid(string id, IEnumerable<RadioStation> stations, UserState state, string scope, IStationLogos? logos = null)` and `StationCards.Tile(RadioStation station, string scope = "", IStationLogos? logos = null)`.
- Produces: `StationView.Build(RadioStation? station, UserState state, string? nowPlaying = null, IStationLogos? logos = null)`. Task 5 adds `StationStream? stream` before `logos`.
- Produces: `InternetRadioPlugin.FetchPendingLogosAsync(CancellationToken)`, internal for tests.

- [ ] **Step 1: Write the failing view tests**

In `tests/.../Views/StationCardsTests.cs`:
- Add this helper inside the class:

```csharp
    private sealed class FixedLogos(string? dataUri) : IStationLogos
    {
        public string? DataUriFor(RadioStation station) => dataUri;
    }
```

- Replace `ATileIsTheAppsOwnCard` with:

```csharp
    [Fact]
    public void ATileIsTheAppsOwnCard()
    {
        PluginComponent tile = StationCards.Tile(
            Station("https://c.example.com/l.png"), logos: new FixedLogos("data:image/png;base64,AA=="));

        tile.Component.Should().Be(Ui.MediaCardComponent);

        Dictionary<string, object?> data = Data(tile);

        data["name"].Should().Be("Example FM");
        data["link"].Should().Be(AppRoutes.Station("a"));
        data["cover"].Should().Be("data:image/png;base64,AA==");
        // The artist branch draws a square framed logo; the album branch draws a record
        // sleeve, which a radio station is not.
        data["type"].Should().Be("artist");
    }

    // Until the logo is fetched - and for a station that has none - the card draws its
    // own placeholder, never a broken image.
    [Fact]
    public void ATileWithoutAKnownLogoDrawsThePlaceholder()
    {
        Data(StationCards.Tile(Station("https://c.example.com/l.png")))["cover"].Should().BeNull();
    }

    [Fact]
    public void TheGridHandsEveryTileTheSameLogos()
    {
        PluginComponent grid = StationCards.Grid(
            "g", [Station(id: "a"), Station(id: "b")], UserState.Empty, "s", new FixedLogos("data:x"));

        grid.Items.Select(tile => Data(tile)["cover"]).Should().AllBe("data:x");
    }
```

- Delete `CoverUrl_KeepsAnHttpsLogo`, `CoverUrl_RefusesAnythingTheBrowserCannotDrawOverHttps`, `Tile_SendsNoCoverWhenTheLogoIsOneTheBrowserWouldRefuse` and `Play_DoesNotHandARejectedCoverToThePlayer`. The browser never sees a logo address any more. `LogoCacheTests.OnlyAnAbsoluteWebAddressIsALogoSource` now owns that judgement.

In `tests/.../Views/StationViewTests.cs`, add:

```csharp
    [Fact]
    public void TheDetailShowsTheLogoItIsHanded()
    {
        PluginView view = StationView.Build(Full, UserState.Empty, logos: new FixedLogos("data:image/png;base64,AA=="));

        Node(view, "station-detail-a").Props["image"].Should().Be("data:image/png;base64,AA==");
    }
```

Add the same `FixedLogos` helper class to `StationViewTests`.

- [ ] **Step 2: Run to verify they fail**

Run: `/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter "FullyQualifiedName~StationCardsTests|FullyQualifiedName~StationViewTests" 2>&1 | grep -E "error CS|Failed!|Passed!"`
Expected: compile errors about the `logos` parameter.

- [ ] **Step 3: Thread `logos` through the cards and views**

In `StationCards.cs`:

```csharp
    /// <summary>Stations in the app's own grid.</summary>
    public static PluginComponent Grid(
        string id,
        IEnumerable<RadioStation> stations,
        UserState state,
        string scope,
        IStationLogos? logos = null) =>
        Ui.MediaGrid(id, stations.Select(station => Tile(station, scope, logos)));

    /// <summary>
    /// One station, as the app draws an artist: a square cover, the name under it, and a
    /// link to its own page.
    /// </summary>
    public static PluginComponent Tile(RadioStation station, string scope = "", IStationLogos? logos = null) =>
        Ui.MediaCard(
            $"station-tile-{Scoped(scope, station.Id)}",
            station.Name,
            AppRoutes.Station(station.Id),
            // A data: URI or nothing. Null draws the card's own placeholder, which is also
            // what a station without a logo has always shown.
            (logos ?? NoStationLogos.Instance).DataUriFor(station),
            // Same treatment as an artist - a square, framed logo - rather than the album
            // branch, which draws a record sleeve this is not.
            type: "artist",
            description: StationSubtitle(station));
```

Delete `CoverUrl` and its doc comment.

`PlayIntent` and `Enqueue` still call `CoverUrl(station) is null ? null : MediaProxy.Cover(station.Id)`. For now, replace each of those expressions with `null`. Task 5 rebuilds both intents with the cover and the minted stream.

In `BrowseView.Build`, `GenreView.Build`, `AllStationsView.Build` and `SearchView.Build`, add a final parameter `IStationLogos? logos = null`, and pass `logos` as the last argument of every `StationCards.Grid(...)` call in that file. The call sites are listed at `BrowseView.cs:67,77`, `GenreView.cs:40`, `AllStationsView.cs:45` and `SearchView.cs:189`.

If a view reaches `Grid` through a private helper (`SearchView.cs:189` is inside an iterator), add the parameter to that helper too.

In `StationView.cs`, change the signature to:

```csharp
    public static PluginView Build(
        RadioStation? station, UserState state, string? nowPlaying = null, IStationLogos? logos = null)
```

Replace the `Ui.Detail(...)` image argument and its three-line comment with:

```csharp
                    // Inline, like every other logo this plugin draws: nothing else
                    // reaches an image element on this platform any more.
                    (logos ?? NoStationLogos.Instance).DataUriFor(station),
```

- [ ] **Step 4: Run to verify the view tests pass**

Run the same filter. Expected: `Passed!`.

- [ ] **Step 5: Write the failing lifecycle test for the fetch after the page**

Add to `PluginLifecycleTests.cs`:

```csharp
    // The page never waits on a logo server. It goes out with placeholders; the logos it
    // asked for are fetched afterwards and drawn on the next page.
    [Fact]
    public async Task LogosAreFetchedAfterThePageAndDrawnOnTheNext()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1];
        _handler.RespondPerRequest(request =>
            request.RequestUri!.AbsolutePath.EndsWith(".png", StringComparison.Ordinal)
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(png) }
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(OneStationWithLogo) });
        using InternetRadioPlugin plugin = new();
        plugin.Initialize(new FakePluginContext(_folder, new HttpClient(_handler)));

        PluginView first = await plugin.GetViewAsync(Request(RadioRoutes.AllStations), CancellationToken.None);
        Cover(first, "station-tile-all-u1").Should().BeNull();

        // The page itself starts a background fetch, and so does this loop; whichever gets
        // there first stores it. Waiting on the outcome rather than on either path keeps
        // this from racing the other.
        string? cover = null;
        for (int attempt = 0; attempt < 100 && cover is null; attempt++)
        {
            await plugin.FetchPendingLogosAsync(CancellationToken.None);
            cover = Cover(await plugin.GetViewAsync(Request(RadioRoutes.AllStations), CancellationToken.None), "station-tile-all-u1");
            if (cover is null)
            {
                await Task.Delay(50);
            }
        }

        cover.Should().StartWith("data:image/png;base64,");
    }

    private const string OneStationWithLogo = """
        [{"stationuuid":"u1","name":"Example FM","url":"https://example.com/a",
          "url_resolved":"https://example.com/a","favicon":"https://example.com/logo.png",
          "tags":"ambient","countrycode":"NL","codec":"MP3","bitrate":128,"hls":0,
          "lastcheckok":1,"votes":5}]
        """;

    private static string? Cover(PluginView view, string tileId) =>
        ((Dictionary<string, object?>)AllNodes(view).Single(node => node.Id == tileId).Props["data"]!)["cover"] as string;
```

Before writing this, check two things in the code, and adjust the test to what is actually there:
- that `favicon` is the radio-browser field mapped to `LogoUrl` (see `RadioBrowserStation.cs`);
- that `AllStationsView` scopes its tiles `all` (`AllStationsView.cs:45`).

- [ ] **Step 6: Run to verify it fails**

Run: `/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter FullyQualifiedName~LogosAreFetchedAfterThePage 2>&1 | grep -E "error CS|Failed!|Passed!"`
Expected: `error CS1061: 'InternetRadioPlugin' does not contain a definition for 'FetchPendingLogosAsync'`.

- [ ] **Step 7: Wire the cache into the plugin**

In `InternetRadioPlugin.cs`, add beside `StateStore`:

```csharp
    private LogoCache? _logos;

    private LogoCache Logos => _logos ??= new LogoCache(Context.DataFolderPath);

    // 0 or 1: whether a background logo fetch is running. One at a time is plenty - a
    // page of placeholders fills in on the next view either way.
    private int _fetchingLogos;

    /// <summary>
    /// Fetches every logo views asked for since the last run, and stores what came of it.
    ///
    /// Internal so a test can run it to completion instead of racing the background copy.
    /// A failure worth retrying is not stored, so the next view asks again.
    /// </summary>
    internal async Task FetchPendingLogosAsync(CancellationToken ct)
    {
        LogoFetcher fetcher = new(Context.HttpClient);

        while (Logos.TakePending() is { Count: > 0 } wanted)
        {
            await FetchLogosAsync(fetcher, wanted, ct);
        }
    }

    private async Task FetchLogosAsync(LogoFetcher fetcher, IEnumerable<RadioStation> stations, CancellationToken ct)
    {
        foreach (RadioStation station in stations)
        {
            ct.ThrowIfCancellationRequested();

            if (Logos.Has(station) || LogoCache.SourceOf(station) is not { } source)
            {
                continue;
            }

            LogoResult result = await fetcher.FetchAsync(source, ct);

            if (result.IsFinal)
            {
                await Logos.StoreAsync(station, result.DataUri, ct);
            }
        }
    }

    /// <summary>Starts a background fetch for whatever the page just asked for, unless one is running.</summary>
    private void FetchLogosInBackground()
    {
        if (Interlocked.CompareExchange(ref _fetchingLogos, 1, 0) != 0)
        {
            return;
        }

        CancellationToken lifetime = _lifecycleCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await FetchPendingLogosAsync(lifetime);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                // Unloading. The logos wait for the next start.
            }
            catch (Exception exception)
            {
                _context?.Logger.LogWarning(exception, "Internet Radio could not fetch station logos.");
            }
            finally
            {
                Volatile.Write(ref _fetchingLogos, 0);
            }
        });
    }
```

In `GetViewAsync`, pass `Logos` to every view that draws stations:
- `GenreView.Build(catalog, route.Value, state, Logos)`
- `AllStationsView.Build(catalog, state, Logos)`
- in `BuildBrowseAsync` and `BuildSearchAsync`, add `logos: Logos` to every `BrowseView.Build`/`SearchView.Build` call
- in `BuildStationAsync`, add `logos: Logos` to every `StationView.Build` call that has a station

Then kick off the background fetch once the view is built. Change the `return route.Kind switch { ... };` into:

```csharp
            PluginView view = route.Kind switch
            {
                // (the existing arms, with Logos passed as above)
            };

            FetchLogosInBackground();

            return view;
```

In `ExecuteAsync(string jobName, ...)`, after `StationCatalog catalog = await Provider.RefreshAsync(linked.Token);`, warm the catalogue's logos:

```csharp
        // The catalogue's own logos are fetched here, on the quiet hour, so the pages most
        // people open never show a placeholder. A failure costs logos, not the refresh.
        try
        {
            await FetchLogosAsync(new LogoFetcher(context.HttpClient), catalog.Stations, linked.Token);
        }
        catch (OperationCanceledException) when (linked.Token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            context.Logger.LogWarning(exception, "Internet Radio could not fetch the catalogue's logos.");
        }
```

In `FetchStationMediaAsync`, replace `StationCards.CoverUrl(station)` with `LogoCache.SourceOf(station)`. That method is deleted in Task 7.

- [ ] **Step 8: Run the whole suite**

```bash
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true 2>&1 | grep -E "Warn|Error\(s\)"
/c/Users/philp/.dotnet/dotnet.exe test -c Release --no-build 2>&1 | grep -E "Failed |Passed!|Failed!"
```
Expected: all green, including the new lifecycle test.

- [ ] **Step 9: Commit**

```bash
git add src tests
git commit -m "feat(logos): every card draws its logo inline

Views take their logos as data, so they stay pure; the plugin passes its
cache. A page goes out with placeholders for logos it has not seen and
fetches them afterwards, one run at a time; the catalogue's logos are
fetched on the nightly refresh so the pages most people open have them."
```

---

### Task 5: Play through a stream the host mints

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/Views/StationStream.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Playback/IStationStreams.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Playback/HostStationStreams.cs`
- Modify: `src/.../Views/StationCards.cs` (`Play`, `Enqueue`)
- Modify: `src/.../Views/StationView.cs` (actions, notice, refresh, no homepage button)
- Modify: `src/.../InternetRadioPlugin.cs` (`Streams`, `BuildStationAsync`)
- Modify: `src/.../plugin.json` (hooks)
- Modify: `src/.../Catalog/StationGates.cs` (`IsSafeExternalUrl` removed)
- Test: `tests/.../Playback/HostStationStreamsTests.cs`, `tests/.../Views/StationViewTests.cs`, `tests/.../Views/StationCardsTests.cs`, `tests/.../Views/StationCardsNowPlayingTests.cs`, `tests/.../ManifestTests.cs`, `tests/.../Catalog/StationGatesTests.cs`

**Interfaces:**
- Consumes: `IStationLogos` (Task 3), and `StationView.Build` from Task 4.
- Produces:
  - `sealed record StationStream(string? Url, string? RefusalCode)` with `IsPlayable`, `static Playable(string url)` and `static Refused(string code)`.
  - `interface IStationStreams { Task<StationStream> ForAsync(RadioStation station, CancellationToken ct); }`.
  - `HostStationStreams(Func<PluginProxyRequest, CancellationToken, Task<Uri>> mint, IPluginGrants grants, ILogger logger)`.
  - `StationCards.Play(RadioStation station, string streamUrl, string? cover)` and `StationCards.Enqueue(RadioStation station, string streamUrl, string? cover)`.
  - `StationView.Build(RadioStation? station, UserState state, string? nowPlaying = null, StationStream? stream = null, IStationLogos? logos = null)`.
  - `const int StationView.StreamRefreshSeconds = 600`.
  - `InternetRadioPlugin.Streams { internal set; }` for tests.

- [ ] **Step 1: Write the failing minting tests**

Create `tests/NoMercy.Plugin.InternetRadio.Tests/Playback/HostStationStreamsTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using NoMercy.PluginSdk.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Playback;

public class HostStationStreamsTests
{
    private static readonly RadioStation Station =
        new() { Id = "a", Name = "Alpha FM", StreamUrl = "https://example.com/live" };

    private sealed class RecordingGrants : IPluginGrants
    {
        public List<(string Kind, string Value)> Requested { get; } = [];

        public Task<bool> HasAsync(string kind, string value, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> GetAsync(string kind, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task RequestAsync(string kind, string value, string reason, CancellationToken ct = default)
        {
            reason.Should().NotBeNullOrWhiteSpace();
            Requested.Add((kind, value));
            return Task.CompletedTask;
        }
    }

    private static PluginRefusedException Refusal(string code) =>
        new(new PluginRefusal(code, "Internet Radio", "what", "why", "fix", PluginRefusalSeverity.Blocked));

    [Fact]
    public async Task AMintedUrlIsWhatThePlayerGets()
    {
        PluginProxyRequest? asked = null;
        HostStationStreams streams = new(
            (request, _) => { asked = request; return Task.FromResult(new Uri("/api/v1/plugins/x/media/t", UriKind.Relative)); },
            new RecordingGrants(),
            new RecordingLogger());

        StationStream stream = await streams.ForAsync(Station, CancellationToken.None);

        stream.Should().Be(StationStream.Playable("/api/v1/plugins/x/media/t"));
        asked!.Links.Should().ContainSingle().Which.Url.Should().Be(new Uri("https://example.com/live"));
        asked.Title.Should().Be("Alpha FM");
        // A live stream never ends; a timeout would cut every listener off mid-song.
        asked.Timeout.Should().BeNull();
    }

    [Fact]
    public async Task WithoutTheGrantItAsksTheOwnerOnceForEveryHost()
    {
        RecordingGrants grants = new();
        HostStationStreams streams = new(
            (_, _) => throw Refusal(PluginRefusalCodes.CapabilityScopeRefused), grants, new RecordingLogger());

        StationStream first = await streams.ForAsync(Station, CancellationToken.None);
        StationStream second = await streams.ForAsync(Station, CancellationToken.None);

        first.Should().Be(StationStream.Refused(PluginRefusalCodes.CapabilityScopeRefused));
        second.IsPlayable.Should().BeFalse();
        grants.Requested.Should().Equal(
            (PluginGrantKind.ForCapability(PluginCapabilityNames.MediaProxy), PluginGrant.Everything));
    }

    // A host without the media facade refuses too; that is not a grant the owner can give.
    [Fact]
    public async Task AnyOtherRefusalIsReportedAndAsksForNothing()
    {
        RecordingGrants grants = new();
        HostStationStreams streams = new(
            (_, _) => throw Refusal(PluginRefusalCodes.FacadeNotOnThisHost), grants, new RecordingLogger());

        StationStream stream = await streams.ForAsync(Station, CancellationToken.None);

        stream.Should().Be(StationStream.Refused(PluginRefusalCodes.FacadeNotOnThisHost));
        grants.Requested.Should().BeEmpty();
    }

    // Asking must never cost the viewer the page.
    [Fact]
    public async Task AFailedGrantRequestStillAnswersRefused()
    {
        HostStationStreams streams = new(
            (_, _) => throw Refusal(PluginRefusalCodes.CapabilityScopeRefused),
            new ThrowingGrants(),
            new RecordingLogger());

        StationStream stream = await streams.ForAsync(Station, CancellationToken.None);

        stream.IsPlayable.Should().BeFalse();
    }

    private sealed class ThrowingGrants : IPluginGrants
    {
        public Task<bool> HasAsync(string kind, string value, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task<IReadOnlyList<string>> GetAsync(string kind, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task RequestAsync(string kind, string value, string reason, CancellationToken ct = default) => throw new InvalidOperationException();
    }
}
```

Before running, check that `PluginRefusalCodes.FacadeNotOnThisHost` exists in `_server/src/NoMercy.PluginSdk.Abstractions/Generated/PluginRefusalCodes.cs`:

```bash
grep -n "FacadeNotOnThisHost\|CapabilityScopeRefused" _server/src/NoMercy.PluginSdk.Abstractions/Generated/PluginRefusalCodes.cs
```

If the facade code has a different name, use the constant `PluginRefusalMessages.FacadeNotOnThisHost(...)` actually produces. Read that method to find it.

- [ ] **Step 2: Run to verify it fails**

Run: `/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter FullyQualifiedName~HostStationStreamsTests 2>&1 | grep -E "error CS|Failed!|Passed!"`
Expected: `error CS0246: ... 'HostStationStreams'`.

- [ ] **Step 3: Implement the stream types**

Create `src/NoMercy.Plugin.InternetRadio/Views/StationStream.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// A url the player can play, or the refusal code that explains why there is none.
///
/// A view reads this as data. Minting one needs the request being served and the host,
/// and a view has neither.
/// </summary>
public sealed record StationStream(string? Url, string? RefusalCode)
{
    public bool IsPlayable => Url is not null;

    public static StationStream Playable(string url) => new(url, null);

    public static StationStream Refused(string code) => new(null, code);
}
```

Create `src/NoMercy.Plugin.InternetRadio/Playback/IStationStreams.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

/// <summary>A playable url for a station, minted for whoever is asking right now.</summary>
public interface IStationStreams
{
    Task<StationStream> ForAsync(RadioStation station, CancellationToken ct);
}
```

Create `src/NoMercy.Plugin.InternetRadio/Playback/HostStationStreams.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NoMercy.PluginSdk.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// Streams through the host's media proxy.
///
/// The host mints a short-lived url bound to the listener, and fetches the station itself,
/// so the browser only ever talks to this server. That replaces this plugin's own relay,
/// which carried the viewer's token in the url - refused outright since ABI 11.
///
/// The mint is a delegate rather than the context so a test can stand in for it: a
/// PluginMediaUrl cannot be constructed outside the host, on purpose.
/// </summary>
public sealed class HostStationStreams(
    Func<PluginProxyRequest, CancellationToken, Task<Uri>> mint,
    IPluginGrants grants,
    ILogger logger
) : IStationStreams
{
    private static readonly TimeSpan LogEvery = TimeSpan.FromHours(1);

    private const string GrantReason =
        "Internet Radio plays stations from thousands of different servers. "
        + "Allow it to stream them through this server so they can play in the app.";

    private int _askedForTheGrant;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastLogged = new();

    /// <summary>For the plugin: minting through the context it was handed.</summary>
    public static HostStationStreams For(IPluginContext context) =>
        new(
            async (request, ct) => (await context.Media.Proxy.MintAsync(request, ct)).Url,
            context.Grants,
            context.Logger);

    public async Task<StationStream> ForAsync(RadioStation station, CancellationToken ct)
    {
        try
        {
            Uri url = await mint(
                new PluginProxyRequest
                {
                    Links = [new PluginProxyLink { Url = new Uri(station.StreamUrl) }],
                    Title = station.Name,
                    // Null is no timeout. A live stream never finishes, and a timeout would
                    // cut every listener off at the same minute of every song.
                    Timeout = null,
                },
                ct);

            return StationStream.Playable(url.OriginalString);
        }
        catch (PluginRefusedException refused)
        {
            string code = refused.Refusal.Code;

            Log(refused.Refusal);

            if (code == PluginRefusalCodes.CapabilityScopeRefused)
            {
                await AskForTheGrantOnceAsync(ct);
            }

            return StationStream.Refused(code);
        }
    }

    // Once per process. The host records a request and never queues a second prompt for
    // the same thing anyway; this only saves asking it on every page.
    private async Task AskForTheGrantOnceAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _askedForTheGrant, 1) == 1)
        {
            return;
        }

        try
        {
            await grants.RequestAsync(
                PluginGrantKind.ForCapability(PluginCapabilityNames.MediaProxy),
                PluginGrant.Everything,
                GrantReason,
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Asking is a courtesy. Failing to ask must not cost the viewer the page.
            logger.LogWarning(exception, "Internet Radio could not ask the owner to allow streaming.");
        }
    }

    // Once per code per hour: a refusal is the same answer for every station on every page,
    // and a line per card would bury everything else in the log.
    private void Log(PluginRefusal refusal)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (_lastLogged.TryGetValue(refusal.Code, out DateTimeOffset last) && now - last < LogEvery)
        {
            return;
        }

        _lastLogged[refusal.Code] = now;
        logger.LogWarning(
            "Internet Radio cannot play stations yet: {Code}. {What} {Fix}", refusal.Code, refusal.What, refusal.Fix);
    }
}
```

- [ ] **Step 4: Run to verify the minting tests pass**

Run the same filter. Expected: `Passed!` with 4 tests.

- [ ] **Step 5: Write the failing station page tests**

In `tests/.../Views/StationViewTests.cs`:

1. Replace `OffersPlayAndEnqueueForTheStream` with:

```csharp
    private static readonly StationStream Minted = StationStream.Playable("/api/v1/plugins/x/media/t");

    [Fact]
    public void PlaysAndQueuesTheMintedStream()
    {
        PluginView view = StationView.Build(Full, UserState.Empty, stream: Minted);

        ActionOfType(view, PluginActionType.PlayMedia)!.Action!.Payload["streamUrl"]
            .Should().Be("/api/v1/plugins/x/media/t");
        ActionOfType(view, PluginActionType.Enqueue)!.Action!.Payload["streamUrl"]
            .Should().Be("/api/v1/plugins/x/media/t");
    }

    // A minted url is good for fifteen minutes. A page left open longer would offer one the
    // server refuses, and the player says nothing when that happens.
    [Fact]
    public void APlayablePageRefreshesBeforeItsStreamExpires()
    {
        StationView.Build(Full, UserState.Empty, stream: Minted).RefreshInterval
            .Should().Be(StationView.StreamRefreshSeconds)
            .And.BeLessThan(15 * 60);
    }

    [Fact]
    public void WithoutAStreamThereIsNothingToPressAndItSaysWhy()
    {
        PluginView view = StationView.Build(
            Full, UserState.Empty, stream: StationStream.Refused(PluginRefusalCodes.CapabilityScopeRefused));

        ActionOfType(view, PluginActionType.PlayMedia).Should().BeNull();
        ActionOfType(view, PluginActionType.Enqueue).Should().BeNull();
        Node(view, "station-stream-unavailable-a").Props["value"]
            .Should().BeOfType<string>().Which.Should().Contain("owner");
        // The favourite still works: keeping a station needs no stream.
        AllNodes(view).Should().Contain(node => node.Id == "station-favourite-a");
    }

    [Fact]
    public void APageWithoutAStreamDoesNotRefreshItself()
    {
        StationView.Build(Full, UserState.Empty).RefreshInterval.Should().Be(0);
    }

    // The client now replaces the whole plugin page with an "open external" card for any
    // outside address, which is worse than no button.
    [Fact]
    public void ThereIsNoHomepageButton()
    {
        ActionOfType(StationView.Build(Full, UserState.Empty, stream: Minted), PluginActionType.OpenWebView)
            .Should().BeNull();
    }
```

2. Delete `OffersTheHomepageWhenThereIsOne`, `OmitsTheHomepageButtonWhenThereIsNoHomepage` and `OmitsTheHomepageButtonWhenTheHomepageIsNotAnHttpUrl`, with their comments.

3. Replace every remaining `MediaProxy.Remember(...)` line in this file with nothing.

In `tests/.../Views/StationCardsTests.cs`, change the two theories that build intents to use the new signatures:
- `enqueue ? StationCards.Enqueue(Station(), "/m", null) : StationCards.Play(Station(), "/m", null)`

Then add:

```csharp
    [Fact]
    public void TheMediaIntentsCarryTheStreamAndTheLogoTheyAreHanded()
    {
        PluginActionIntent play = StationCards.Play(Station(), "/api/v1/plugins/x/media/t", "data:image/png;base64,AA==");

        play.Payload["streamUrl"].Should().Be("/api/v1/plugins/x/media/t");
        play.Payload["cover"].Should().Be("data:image/png;base64,AA==");
    }
```

In `tests/.../Views/StationCardsNowPlayingTests.cs`, change `StationCards.Play(Station())` to `StationCards.Play(Station(), "/m", null)`, and `StationCards.Enqueue(Station())` to `StationCards.Enqueue(Station(), "/m", null)`.

Before relying on the payload key names `streamUrl` and `cover`, confirm them in `_server/src/NoMercy.PluginSdk.Abstractions/PluginActionIntent.cs` (the `PlayMedia` factory). If they differ, use the real names in both tests and code.

In `tests/.../ManifestTests.cs`, change `Manifest_DeclaresExactlyTheHooksThisVersionImplements` to expect:

```csharp
            .Should().BeEquivalentTo(new[]
            {
                PluginHookCapability.Ui,
                PluginHookCapability.ScheduledTask,
                PluginCapabilityNames.MediaProxy,
            });
```

Add one sentence to its comment: "media.proxy is how a station plays at all since ABI 11 refused a token in a plugin url."

In `tests/.../Catalog/StationGatesTests.cs`, delete `IsSafeExternalUrl_AcceptsHttpAndHttps` and `IsSafeExternalUrl_RejectsAnythingElse` with their data rows.

- [ ] **Step 6: Run to verify they fail**

Run: `/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter "FullyQualifiedName~StationViewTests|FullyQualifiedName~StationCards|FullyQualifiedName~ManifestTests" 2>&1 | grep -E "error CS|Failed |Failed!|Passed!"`
Expected: compile errors on `stream:` and on the three-argument `Play`/`Enqueue`.

- [ ] **Step 7: Rebuild the intents, the station page and the manifest**

In `StationCards.cs`, replace `Play`, `PlayIntent` and `Enqueue` with:

```csharp
    /// <summary>
    /// The play intent for a station, so every screen starts the same station the same way.
    ///
    /// The url is one the host minted for this listener; the cover is inline. Neither is
    /// the station's own address, which the browser would refuse.
    /// </summary>
    public static PluginActionIntent Play(RadioStation station, string streamUrl, string? cover) =>
        WithNowPlaying(
            WithStationId(PluginActionIntent.PlayMedia(streamUrl, station.Name, null, cover), station));

    /// <summary>Queueing a station, from the same minted url and cover as <see cref="Play"/>.</summary>
    public static PluginActionIntent Enqueue(RadioStation station, string streamUrl, string? cover) =>
        WithNowPlaying(
            WithStationId(PluginActionIntent.Enqueue(streamUrl, station.Name, null, cover), station));
```

The artist argument stays `null`, as before. A station announces its track through now-playing, not through the item. Keep the reason in a one-line comment at `Play`: "No artist on the item: a live stream's artist changes with every track and arrives through now-playing." The old paragraph about the toast is obsolete (the client has since been fixed), so do not carry it over.

In `StationView.cs`:

1. Add at the top of the class:

```csharp
    /// <summary>
    /// How often a page with a stream on it asks for itself again, in seconds.
    ///
    /// A minted stream url is good for fifteen minutes; ten leaves room for a slow page. A
    /// page left open longer than the url lasts would offer one the server refuses - and
    /// the player says nothing when a stream fails to load.
    /// </summary>
    public const int StreamRefreshSeconds = 600;
```

2. Change the full `Build` signature to:

```csharp
    public static PluginView Build(
        RadioStation? station,
        UserState state,
        string? nowPlaying = null,
        StationStream? stream = null,
        IStationLogos? logos = null)
```

3. In that method, compute the cover once, `string? cover = (logos ?? NoStationLogos.Instance).DataUriFor(station);`, use `cover` as the `Ui.Detail` image, and pass `stream` and `cover` to `Actions`.

4. Wrap the returned view so a playable page refreshes. Replace `return PluginViews.Declarative(Ui.Container("station-root", ...));` for a found station with:

```csharp
        PluginComponent page = Ui.Container(
            "station-root",
            BackButtons,
            Ui.Detail(
                $"station-detail-{station.Id}",
                station.Name,
                Description(station),
                cover,
                Actions(station, state.Favourites.Any(kept => kept.Id == station.Id), stream, cover),
                Facts(station, nowPlaying)
            )
        );

        return stream is { IsPlayable: true }
            ? PluginViews.Declarative(StreamRefreshSeconds, page)
            : PluginViews.Declarative(page);
```

5. Replace `Actions` with:

```csharp
    // This page is where playing, queueing and keeping a station live.
    private static PluginComponent Actions(
        RadioStation station, bool isFavourite, StationStream? stream, string? cover)
    {
        List<PluginComponent> items = [];

        if (stream is { IsPlayable: true, Url: { } url })
        {
            items.Add(Ui.Button($"station-play-{station.Id}", "Play", StationCards.Play(station, url, cover), icon: "play"));
            items.Add(
                Ui.Button(
                    $"station-enqueue-{station.Id}",
                    "Add to queue",
                    StationCards.Enqueue(station, url, cover),
                    icon: "playlistAdd"
                )
            );
        }
        else
        {
            // No fallback to the station's own address: the browser refuses it, so a
            // button with it would be a button that does nothing.
            items.Add(Ui.Text($"station-stream-unavailable-{station.Id}", Unavailable(stream), "caption"));
        }

        // Two states that differ by label and by variant, never by colour alone: a
        // toggle whose only difference is a tint is unreadable to a good share of
        // viewers, and to everyone in a screenshot.
        items.Add(
            Ui.Button(
                $"station-favourite-{station.Id}",
                isFavourite ? StationCards.RemoveFavouriteLabel : StationCards.AddFavouriteLabel,
                StationCards.ToggleFavourite(station),
                variant: isFavourite ? "primary" : "secondary"
            )
        );

        return Ui.Row($"station-actions-{station.Id}", [.. items]);
    }

    // What the viewer can do about it depends on why: a missing approval is the owner's
    // to give, anything else is this server's to fix.
    private static string Unavailable(StationStream? stream) =>
        stream?.RefusalCode is PluginRefusalCodes.CapabilityScopeRefused
            or PluginRefusalCodes.CapabilityNotDeclared
            ? "Playing is waiting for the server owner to approve streaming for Internet Radio."
            : "This server cannot play stations right now. The server owner can find the reason in the server log.";
```

`Ui.Text` puts its text under the `value` prop (`Ui.cs:137`), which is what the test reads. Check that line. If the prop name differs, fix the test to match `Ui.Text`, not the other way round.

6. In `StationGates.cs`, delete `IsSafeExternalUrl` and its doc comment. If `using System.Diagnostics.CodeAnalysis;` becomes unused, delete it too.

7. In `plugin.json`, set `"hooks": ["ui", "scheduledTask", "media.proxy"]`.

- [ ] **Step 8: Mint on the station page**

In `InternetRadioPlugin.cs`, add beside `Logos`:

```csharp
    private IStationStreams? _streams;

    /// <summary>
    /// Where a station page gets its playable url. Settable so a test can stand in for the
    /// host; the plugin itself always uses the context's media proxy.
    /// </summary>
    internal IStationStreams Streams
    {
        get => _streams ??= HostStationStreams.For(Context);
        set => _streams = value;
    }
```

In `BuildStationAsync`, each branch that has a station now mints before building. Replace the three `return StationView.Build(x, state, NowPlaying.Get(id)...)` statements with one local function at the top of the method:

```csharp
        async Task<PluginView> Page(RadioStation? station) =>
            station is null
                ? StationView.Build(station: null, state)
                : StationView.Build(
                    station, state, NowPlaying.Get(id), await Streams.ForAsync(station, ct), Logos);
```

Use it in every branch:
- `return await Page(known);`
- `return await Page(recent);`
- `return await Page(await resolver.ResolveAsync(id, ct));`
- the catch branch keeps `return StationView.Build(station: null, state);`

- [ ] **Step 9: Lifecycle test with the real refusal path**

Add to `PluginLifecycleTests.cs`:

```csharp
    // FakePluginContext does not name Media, so it throws the interface's own refusal -
    // exactly what a host without the facade does. The page must still draw.
    [Fact]
    public async Task AStationPageStillDrawsWhenTheHostCannotMint()
    {
        using InternetRadioPlugin plugin = Started();

        PluginView view = await plugin.GetViewAsync(Request(RadioRoutes.Station("u1")), CancellationToken.None);

        HasNode(view, "station-detail-u1").Should().BeTrue();
        HasNode(view, "station-stream-unavailable-u1").Should().BeTrue();
        HasNode(view, "station-play-u1").Should().BeFalse();
    }

    [Fact]
    public async Task AStationPagePlaysWhatTheHostMints()
    {
        using InternetRadioPlugin plugin = Started();
        plugin.Streams = new FixedStreams(StationStream.Playable("/api/v1/plugins/x/media/t"));

        PluginView view = await plugin.GetViewAsync(Request(RadioRoutes.Station("u1")), CancellationToken.None);

        HasNode(view, "station-play-u1").Should().BeTrue();
        view.RefreshInterval.Should().Be(StationView.StreamRefreshSeconds);
    }

    private sealed class FixedStreams(StationStream stream) : IStationStreams
    {
        public Task<StationStream> ForAsync(RadioStation station, CancellationToken ct) => Task.FromResult(stream);
    }
```

Check `RadioRoutes.Station(string)` exists with that name before using it. `StationCardsTests` already uses `RadioRoutes.Station("a")`.

- [ ] **Step 10: Run the whole suite**

```bash
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true 2>&1 | grep -E "Warn|Error\(s\)"
/c/Users/philp/.dotnet/dotnet.exe test -c Release --no-build 2>&1 | grep -E "Failed |Passed!|Failed!"
grep -rn "MediaProxy\.Stream\|MediaProxy\.Cover\|CoverUrl\|IsSafeExternalUrl\|OpenWebView" src
```
Expected:
- build and tests green;
- the grep prints only `Views/MediaProxy.cs` itself and `InternetRadioPlugin.FetchStationMediaAsync`/controller lines, which Task 7 deletes. No view still uses them.

- [ ] **Step 11: Commit**

```bash
git add src tests
git commit -m "feat(playback): play the url the host mints, and say why when there is none

A station page asks Context.Media.Proxy for a url bound to the listener, and
the play and queue intents carry it. ABI 11 refuses a token in a plugin url,
which is what the old relay depended on. Without the owner's streaming grant
the page asks for it once, shows why nothing plays, and keeps its favourite
button. A playable page refreshes every ten minutes, inside the fifteen a
minted url lasts. The homepage button is gone: the client now replaces the
whole page with an external-link card for it. The manifest declares
media.proxy."
```

---

### Task 6: Now playing through a connection of our own

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/NowPlayingListener.cs`
- Modify: `src/.../Controllers/InternetRadioController.cs` (`NowPlaying` action)
- Modify: `src/.../InternetRadioPlugin.cs` (the listener, `NowPlayingForAsync`, disposal)
- Test: `tests/.../Catalog/NowPlayingListenerTests.cs`, `tests/.../PluginLifecycleTests.cs`

**Interfaces:**
- Consumes: `NowPlaying` (existing: `Set`, `Get`, `Listen`), `IcyMetadataStream(Stream, int, Action<string>)` (existing).
- Produces:
  - `NowPlayingListener(HttpClient http, NowPlaying titles, ILogger logger, NowPlayingListener.Limits? limits = null) : IDisposable`, with `Touch(RadioStation)` and `int OpenCount`.
  - `record Limits { int MaxConnections = 8; TimeSpan Lease = 45 s; TimeSpan QuietFor = 1 h; }`.
  - `InternetRadioPlugin.NowPlayingForAsync(string stationId, CancellationToken) : Task<string?>`.

- [ ] **Step 1: Write the failing listener tests**

Create `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/NowPlayingListenerTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Net;
using System.Text;
using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public sealed class NowPlayingListenerTests : IDisposable
{
    private readonly FakeHttpMessageHandler _handler = new();
    private readonly NowPlaying _titles = new();
    private NowPlayingListener? _listener;

    public void Dispose() => _listener?.Dispose();

    private static RadioStation Station(string id) =>
        new() { Id = id, Name = id, StreamUrl = $"https://example.com/{id}" };

    private NowPlayingListener Listener(NowPlayingListener.Limits? limits = null) =>
        _listener = new NowPlayingListener(new HttpClient(_handler), _titles, new RecordingLogger(), limits);

    // Sixteen bytes of "audio", one metadata block announcing the title, then a live
    // stream's silence: a body that never ends until the reader gives up.
    private static HttpResponseMessage Announcing(string title)
    {
        byte[] text = Encoding.UTF8.GetBytes($"StreamTitle='{title}';");
        int blocks = (text.Length + 15) / 16;
        byte[] metadata = new byte[1 + blocks * 16];
        metadata[0] = (byte)blocks;
        text.CopyTo(metadata, 1);

        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StreamContent(new EndlessStream([.. new byte[16], .. metadata])),
        };
        response.Headers.TryAddWithoutValidation("icy-metaint", "16");
        return response;
    }

    private static HttpResponseMessage Silent() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream([])) };

    private static async Task Eventually(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(50);
        }

        condition().Should().BeTrue();
    }

    [Fact]
    public async Task AnAnnouncedTitleReachesNowPlaying()
    {
        _handler.RespondPerRequest(_ => Announcing("Artist - Track"));

        Listener().Touch(Station("a"));

        await Eventually(() => _titles.Get("a") == "Artist - Track");
    }

    // One connection per station, not one per listener or per poll.
    [Fact]
    public async Task PollingAgainSharesTheOneConnection()
    {
        _handler.RespondPerRequest(_ => Announcing("x"));
        NowPlayingListener listener = Listener();

        listener.Touch(Station("a"));
        await Eventually(() => _titles.Get("a") is not null);
        listener.Touch(Station("a"));
        listener.Touch(Station("a"));

        _handler.Requests.Should().HaveCount(1);
        listener.OpenCount.Should().Be(1);
    }

    [Fact]
    public async Task AStationNobodyPollsIsLetGoAndItsTitleForgotten()
    {
        _handler.RespondPerRequest(_ => Announcing("x"));
        NowPlayingListener listener = Listener(new() { Lease = TimeSpan.FromMilliseconds(300) });

        listener.Touch(Station("a"));
        await Eventually(() => _titles.Get("a") is not null);

        await Eventually(() => listener.OpenCount == 0);
        _titles.Get("a").Should().BeNull();
    }

    [Fact]
    public async Task NoMoreThanTheCapAreOpenAtOnce()
    {
        _handler.RespondPerRequest(_ => Announcing("x"));
        NowPlayingListener listener = Listener(new() { MaxConnections = 1 });

        listener.Touch(Station("a"));
        listener.Touch(Station("b"));
        await Eventually(() => _titles.Get("a") is not null);

        listener.OpenCount.Should().Be(1);
        _titles.Get("b").Should().BeNull();
    }

    // A station that sends no metadata is not reconnected on every poll.
    [Fact]
    public async Task AStationThatAnnouncesNothingIsLeftAloneForAWhile()
    {
        _handler.RespondPerRequest(_ => Silent());
        NowPlayingListener listener = Listener();

        listener.Touch(Station("a"));
        await Eventually(() => listener.OpenCount == 0);
        listener.Touch(Station("a"));
        await Task.Delay(200);

        _handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task TheRequestAsksForMetadata()
    {
        _handler.RespondPerRequest(_ => Announcing("x"));

        Listener().Touch(Station("a"));
        await Eventually(() => _handler.Requests.Count == 1);

        _handler.Requests[0].Headers.GetValues("Icy-MetaData").Should().Equal("1");
    }

    /// <summary>Hands out its bytes, then waits for cancellation like a live stream.</summary>
    private sealed class EndlessStream(byte[] prefix) : Stream
    {
        private int _position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_position < prefix.Length)
            {
                int count = Math.Min(buffer.Length, prefix.Length - _position);
                prefix.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }

            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
```

Check two things in `TestSupport/FakeHttpMessageHandler.cs`:
- that `Requests` records each request before the responder runs;
- that it does not dispose the request.

If it reads or clones the request differently, adapt the two header and count assertions to what it records.

- [ ] **Step 2: Run to verify it fails**

Run: `/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter FullyQualifiedName~NowPlayingListenerTests 2>&1 | grep -E "error CS|Failed!|Passed!"`
Expected: `error CS0246: ... 'NowPlayingListener'`.

- [ ] **Step 3: Implement `NowPlayingListener`**

Create `src/NoMercy.Plugin.InternetRadio/Catalog/NowPlayingListener.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// Hears what a station is playing, for as long as someone is listening.
///
/// The host's media proxy streams the audio, and it never asks a station for metadata. So
/// the title has to come from a connection of this plugin's own: it asks with
/// Icy-MetaData: 1, keeps only the announcements, and throws the audio away.
///
/// One connection per station, shared by everyone listening to it. It lives on a lease
/// that every now-playing poll renews, so it closes shortly after the last listener's
/// player stops asking. A cap keeps a busy server from opening one per station in the
/// directory, and a station that announces nothing is left alone for an hour rather than
/// reconnected on every poll.
///
/// A Shoutcast v1 station answers "ICY 200 OK", which .NET's HttpClient cannot read. That
/// was true of the old relay too; such a station simply has no title.
/// </summary>
public sealed class NowPlayingListener(
    HttpClient http,
    NowPlaying titles,
    ILogger logger,
    NowPlayingListener.Limits? limits = null
) : IDisposable
{
    public sealed record Limits
    {
        public int MaxConnections { get; init; } = 8;
        public TimeSpan Lease { get; init; } = TimeSpan.FromSeconds(45);
        public TimeSpan QuietFor { get; init; } = TimeSpan.FromHours(1);
    }

    private readonly Limits _limits = limits ?? new Limits();
    private readonly ConcurrentDictionary<string, Connection> _open = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _quiet = new();
    private readonly Lock _opening = new();
    private bool _disposed;

    public int OpenCount => _open.Count;

    /// <summary>Someone is listening to this station: keep, or start, hearing it.</summary>
    public void Touch(RadioStation station)
    {
        if (_disposed)
        {
            return;
        }

        if (_open.TryGetValue(station.Id, out Connection? open))
        {
            open.Renew();
            return;
        }

        if (_quiet.TryGetValue(station.Id, out DateTimeOffset until) && until > DateTimeOffset.UtcNow)
        {
            return;
        }

        lock (_opening)
        {
            if (_open.TryGetValue(station.Id, out open))
            {
                open.Renew();
                return;
            }

            // Past the cap the poll simply gets no title - the same answer as a station
            // that announces nothing, which a player already shows gracefully.
            if (_open.Count >= _limits.MaxConnections)
            {
                return;
            }

            Connection connection = new(_limits.Lease);
            _open[station.Id] = connection;
            _ = HearAsync(station, connection);
        }
    }

    private async Task HearAsync(RadioStation station, Connection connection)
    {
        // The title is forgotten when this closes, so a station nobody hears any more
        // stops reporting a song that ended long ago.
        using IDisposable listening = titles.Listen(station.Id);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, station.StreamUrl);
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");

            using HttpResponseMessage response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, connection.Token);

            if (!response.IsSuccessStatusCode || MetaInterval(response) is not { } interval)
            {
                _quiet[station.Id] = DateTimeOffset.UtcNow + _limits.QuietFor;
                return;
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(connection.Token);
            await using IcyMetadataStream audio = new(body, interval, title => titles.Set(station.Id, title));

            byte[] discarded = new byte[8192];
            while (await audio.ReadAsync(discarded, connection.Token) > 0)
            {
            }
        }
        catch (OperationCanceledException)
        {
            // The lease ran out, or the plugin is unloading. Both are how this ends.
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Internet Radio stopped hearing {Station}.", station.Id);
        }
        finally
        {
            _open.TryRemove(station.Id, out _);
            connection.Dispose();
        }
    }

    /// <summary>The station's own icy-metaint, when it agreed to send metadata.</summary>
    private static int? MetaInterval(HttpResponseMessage response) =>
        response.Headers.TryGetValues("icy-metaint", out IEnumerable<string>? values)
        && int.TryParse(values.FirstOrDefault(), out int interval)
        && interval > 0
            ? interval
            : null;

    public void Dispose()
    {
        _disposed = true;

        foreach (Connection connection in _open.Values)
        {
            connection.Cancel();
        }
    }

    private sealed class Connection(TimeSpan lease) : IDisposable
    {
        private readonly CancellationTokenSource _cts = new(lease);

        public CancellationToken Token => _cts.Token;

        public void Renew()
        {
            try
            {
                _cts.CancelAfter(lease);
            }
            catch (ObjectDisposedException)
            {
                // Closed between the lookup and the renewal; the next poll opens a new one.
            }
        }

        public void Cancel()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose() => _cts.Dispose();
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run the same filter. Expected: `Passed!` with 6 tests. Run it three times in a row: these tests depend on timing and must not flake.

```bash
for i in 1 2 3; do /c/Users/philp/.dotnet/dotnet.exe test -c Release --no-build --filter FullyQualifiedName~NowPlayingListenerTests 2>&1 | grep -E "Failed!|Passed!"; done
```

- [ ] **Step 5: Drive it from the poll**

In `InternetRadioPlugin.cs`, add:

```csharp
    private NowPlayingListener? _listener;

    private NowPlayingListener Listener =>
        _listener ??= new NowPlayingListener(Context.HttpClient, NowPlaying, Context.Logger);

    /// <summary>
    /// What the station is announcing, and a renewed lease on hearing it.
    ///
    /// The player polls this while the station plays, so the poll itself is what keeps
    /// the metadata connection open. The first answer after pressing play is usually
    /// empty: the station has not announced anything on the new connection yet.
    /// </summary>
    public async Task<string?> NowPlayingForAsync(string stationId, CancellationToken ct)
    {
        try
        {
            if (await ResolveStationAsync(stationId, ct) is { } station)
            {
                Listener.Touch(station);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A poll that cannot resolve the station still answers with whatever is known.
            _context?.Logger.LogDebug(exception, "Internet Radio could not resolve {Station} for now playing.", stationId);
        }

        return NowPlaying.Get(stationId);
    }
```

In `Dispose()`, before `_lifecycleCts.Cancel();`, add `_listener?.Dispose();`.

In `InternetRadioController.cs`, replace the `NowPlaying` action with:

```csharp
    [HttpGet(NowPlayingRouteTemplate)]
    public async Task<IActionResult> NowPlaying(string stationId, CancellationToken ct)
    {
        if (pluginManager.GetPluginInstance(PluginId) is not InternetRadioPlugin plugin)
        {
            return NotFound();
        }

        string? announced = await plugin.NowPlayingForAsync(stationId, ct);
        StreamTitle? parsed = announced is null ? null : StreamTitle.Parse(announced);

        // `title` stays the whole announced line so a client written against the
        // earlier shape keeps working; artist and track are additions beside it.
        return Status<object>(new
        {
            title = announced,
            artist = parsed?.Artist,
            track = parsed?.Track
        });
    }
```

Keep its existing doc comment. Add: "Each poll also renews the lease on the station's metadata connection; see NowPlayingListener."

- [ ] **Step 6: Lifecycle test**

Add to `PluginLifecycleTests.cs`:

```csharp
    [Fact]
    public async Task PollingNowPlayingOpensTheStationsMetadataConnection()
    {
        using InternetRadioPlugin plugin = Started();

        await plugin.NowPlayingForAsync("u1", CancellationToken.None);

        await Task.Delay(200);
        _handler.Requests.Should().Contain(request =>
            request.RequestUri!.ToString() == "https://example.com/a"
            && request.Headers.Contains("Icy-MetaData"));
    }
```

`Started()` answers every request with the station JSON. That response has no `icy-metaint`, so the connection opens, is marked quiet and closes. That is enough to prove the poll drives it.

- [ ] **Step 7: Run the whole suite, then commit**

```bash
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true 2>&1 | grep -E "Warn|Error\(s\)"
/c/Users/philp/.dotnet/dotnet.exe test -c Release --no-build 2>&1 | grep -E "Failed |Passed!|Failed!"
git add src tests
git commit -m "feat(nowplaying): hear the station on a connection of our own

The host's proxy streams the audio but never asks a station for metadata.
Each now-playing poll now renews a lease on one shared connection per
station that reads only the ICY announcements, at most eight at once. It
closes 45 seconds after the last poll, and a station that announces nothing
is left alone for an hour."
```

---

### Task 7: Remove the old relay

**Files:**
- Delete: `src/NoMercy.Plugin.InternetRadio/Views/MediaProxy.cs`
- Modify: `src/.../Controllers/InternetRadioController.cs` (delete `StreamRouteTemplate`, `StreamMethod`, `CoverRouteTemplate`, `CoverMethod`, the `Stream` and `Cover` actions, `RelayAsync` and `MetaInterval`; fix the now-unused `using` lines)
- Modify: `src/.../InternetRadioPlugin.cs` (delete `FetchStationMediaAsync` and `RelayLogger`)
- Modify: `src/.../Views/RadioRoutes.cs` (comments only)
- Modify: tests that still reference any of these

- [ ] **Step 1: Delete, then let the compiler list what referenced it**

```bash
git rm src/NoMercy.Plugin.InternetRadio/Views/MediaProxy.cs
```

Make the controller and plugin deletions listed above. Then:

```bash
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true 2>&1 | grep -E "error" | sort -u
```

Fix each error by deleting the reference, never by re-adding the code. Test lines that only set up `MediaProxy` are deleted. A test that asserted relay behaviour (a `stream/` or `cover/` url) is deleted together with its comment.

- [ ] **Step 2: Correct the comments that describe the old platform**

Run:
```bash
grep -rn -i "relay\|MediaProxy\|access_token\|Services\|seventeen genre\|rendered as a card\|IHttpContextAccessor" src --include=*.cs
```

For each hit, rewrite the comment to say what is true now, or delete it if it only explains removed code. In particular:
- `InternetRadioController`: the class comment and the `Search` action comment ("rendered as a card" → the form is a real `PluginForm`, fixed upstream).
- `RadioRoutes.Parse`: the comma split stays. Its comment becomes: the web client no longer comma-joins routes (fixed in nomercy-app-web), and the split is kept because it costs nothing and protects against a client that still does.
- `CatalogProvider.DefaultFetchBudget` and `RadioBrowserClient`: any claim that no longer matches the code.

- [ ] **Step 3: Build, test, and prove nothing of the relay is left**

```bash
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true 2>&1 | grep -E "Warn|Error\(s\)"
/c/Users/philp/.dotnet/dotnet.exe test -c Release --no-build 2>&1 | grep -E "Failed |Passed!|Failed!"
grep -rn "stream/{stationId}\|cover/{stationId}\|FetchStationMediaAsync\|RelayLogger\|MediaProxy" src tests
grep -rn --include='*.cs' -E 'https?://' src | grep -v 'radio-browser' | grep -v 'forgejo.phillippepelzer.me' | grep -v 'github.com' || echo "no hardcoded urls"
```
Expected: green, the first grep prints nothing, and the second prints `no hardcoded urls`.

- [ ] **Step 4: Commit**

```bash
git add -A src tests
git commit -m "refactor(playback): remove the relay the host replaced

The stream and cover routes, the static that held the viewer's token and
the fetch behind them are gone: ABI 11 refuses the token they depended on,
and the host's media proxy and inline logos do their job. Comments that
described the old platform now describe this one."
```

---

### Task 8: Documentation and release notes for 2.0.0

**Files:**
- Modify: `README.md`, `src/NoMercy.Plugin.InternetRadio/README.md`
- Modify: `.forgejo/workflows/build.yml` (the release body in "Create Forgejo release & attach assets")

This task does **not** touch `docs/upstream/`. Those files change only in Task 11, after verification.

- [ ] **Step 1: Root README**

In `README.md`:
- Under "Building": the script now packs the newest server release tag into `_server/` inside this repository, and never touches a sibling checkout. `SERVER_REF` pins another tag or commit.
- Replace "the catalogue is discovered by querying the most voted stations per genre" only if it is no longer true. It still is, so keep it.
- Add a "Requirements" line: NoMercy MediaServer **v0.1.526 or newer** (plugin ABI 12.1).

- [ ] **Step 2: Plugin README (ships inside the zip)**

Read `src/NoMercy.Plugin.InternetRadio/README.md` first. Then add or replace sections so it says, in this order:

1. **Requirements:** NoMercy MediaServer v0.1.526 or newer. Older servers refuse 2.0.0, and newer servers refuse 1.x.
2. **Install:** extract the zip into `<server>/plugins/` and restart the server. **Do not use the dashboard's upload button:** a plugin uploaded as a file is visible to the server owner only, and members will not see Internet Radio.
3. **After installing or updating:**
   - enable the plugin in the dashboard (2.0.0 asks for one new permission: streaming through the server);
   - approve its streaming request.
   - Until both are done, stations can be browsed but not played. The station page says so.
4. **What it fetches:**
   - the station list from radio-browser.info, daily;
   - station logos, cached on the server, at most 16 KB each;
   - while a station plays, one extra connection to that station that reads only its track titles.

Remove anything that mentions the homepage button, the relay, `access_token` or tokens in urls.

- [ ] **Step 3: Release body in CI**

In `.forgejo/workflows/build.yml`, replace the `### Install` section and the paragraph under `### Verify` in the `BODY` heredoc with:

```
          ### Requires
          NoMercy MediaServer **v0.1.526 or newer** (plugin ABI 12.1). Older servers refuse
          this release; newer servers refuse 1.x.

          ### Install
          1. Extract \`${ZIP}\` into \`<server>/plugins/\`.
          2. Restart the server.
          3. **Enable the plugin in the dashboard**, then **approve its streaming request**.
             Until both are done, stations can be browsed but not played.

          **Do not install it with the dashboard's upload button.** A plugin uploaded as a
          file is visible to the server owner only, and members will not see it.

          ### Verify
          \`\`\`
          sha256  ${SHA}
          \`\`\`

          Stations are fetched from radio-browser.info and refreshed daily; nothing is
          bundled. Streams play through the server's own media proxy. HTTPS, non-HLS
          stations only in this release.

          Built and tested against \`NoMercy-Entertainment/nomercy-media-server\` ${CONTRACT_REF} (\`${CONTRACT_SHA}\`).
          To rebuild this exact artifact: \`SERVER_REF=${CONTRACT_SHA} ./scripts/fetch-abstractions.sh\`
          then \`dotnet build -c Release\`.
```

In that step's `env:`, add `CONTRACT_REF: ${{ steps.contract.outputs.ref }}` beside `CONTRACT_SHA`.

Check that the heredoc's indentation matches the surrounding lines exactly: YAML block scalars break on a single mis-indented line. Validate the file:

```bash
python -c "import yaml,sys; yaml.safe_load(open('.forgejo/workflows/build.yml')); print('yaml ok')"
```

If PyYAML is not installed, run `pip install pyyaml` into the local Python 3.12 first. It is a local check tool only.

- [ ] **Step 4: Commit**

```bash
git add README.md src/NoMercy.Plugin.InternetRadio/README.md .forgejo/workflows/build.yml
git commit -m "docs: what 2.0.0 needs from a server, and how to install it so members see it"
```

---

### Task 9: A namespace per folder

This re-applies the idea of Stoney's `d8dc010` on top of the migration. It does not merge his commit (see the spec).

**Files:** every `.cs` file under `src/.../Catalog`, `Controllers`, `State`, `Views` and `Playback`, plus the tests that use them.

- [ ] **Step 1: Set the namespaces**

```bash
cd src/NoMercy.Plugin.InternetRadio
for folder in Catalog Controllers State Views Playback; do
  for file in "$folder"/*.cs; do
    sed -i "s/^namespace NoMercy\.Plugin\.InternetRadio;$/namespace NoMercy.Plugin.InternetRadio.$folder;/" "$file"
  done
done
grep -L "^namespace NoMercy.Plugin.InternetRadio\.\(Catalog\|Controllers\|State\|Views\|Playback\);" Catalog/*.cs Controllers/*.cs State/*.cs Views/*.cs Playback/*.cs
cd ../..
```
Expected: the last grep prints nothing. `InternetRadioPlugin.cs` and `PluginIdentity.cs` stay in `NoMercy.Plugin.InternetRadio`.

- [ ] **Step 2: Add the usings, then remove the ones nothing needs**

Add these five lines after the existing `using` block of every `.cs` file under `src/NoMercy.Plugin.InternetRadio` and `tests/NoMercy.Plugin.InternetRadio.Tests`:

```csharp
using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.Controllers;
using NoMercy.Plugin.InternetRadio.Playback;
using NoMercy.Plugin.InternetRadio.State;
using NoMercy.Plugin.InternetRadio.Views;
```

Test files also need `using NoMercy.Plugin.InternetRadio;` for `InternetRadioPlugin` and `PluginIdentity`, if they are not already inside that namespace.

Build. A file's own namespace in its using list is harmless, and the compiler accepts it. Then remove the unused ones:

```bash
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true 2>&1 | grep -E "Warn|Error\(s\)"
/c/Users/philp/.dotnet/dotnet.exe format style --diagnostics IDE0005 --severity info
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true 2>&1 | grep -E "Warn|Error\(s\)"
```

If `dotnet format` does not remove IDE0005 without `GenerateDocumentationFile`, add `-p:GenerateDocumentationFile=true` to a throwaway build to surface the diagnostics. Do not commit that property. Remove the unused lines by hand from the list it prints.

- [ ] **Step 3: Check nothing outside the plugin depended on a full type name**

The host finds the controller by scanning the assembly, not by name, and `plugin.json` names only the assembly. Confirm the manifest and CI name no type:

```bash
grep -n "NoMercy.Plugin.InternetRadio\." src/NoMercy.Plugin.InternetRadio/plugin.json .forgejo/workflows/build.yml
```
Expected: only the assembly/DLL name `NoMercy.Plugin.InternetRadio.dll`, and no type.

- [ ] **Step 4: Test and commit**

```bash
/c/Users/philp/.dotnet/dotnet.exe test -c Release --no-build 2>&1 | grep -E "Failed |Passed!|Failed!"
git add -A src tests
git commit -m "refactor: a namespace per folder

Catalog, Controllers, Playback, State and Views each get their own
namespace, so a type's namespace says where it lives. The idea is Stoney's,
from d8dc010 on his nm-component-ui branch; it is re-applied here on top of
the ABI 12 migration rather than merged, because that commit also replaces
the per-genre catalogue sweep, which this project is not adopting."
```

---

### Task 10: End to end on a local server

Nothing is tagged until every item below is evidenced. The owner must be present for steps 3 and 4 (sign-in and a member account).

**Files:** none in the repository. Everything lives in the scratchpad: `C:/Users/philp/AppData/Local/Temp/claude/f--DevProjects-NoMercyEntertainment-Developement-nomercy-radiostation-plugin/<session>/scratchpad/e2e/`. Call it `$E2E` below.

- [ ] **Step 1: Get the release the contract was packed from**

```bash
read -r TAG SHA < _nupkgs/CONTRACT; echo "$TAG $SHA"
mkdir -p "$E2E/app"
gh release download "$TAG" -R NoMercy-Entertainment/nomercy-media-server -p 'NoMercyMediaServer-windows-x64.exe' -p 'NoMercyMediaServer-windows-x64.exe.sha256' -D "$E2E"
(cd "$E2E" && sha256sum -c NoMercyMediaServer-windows-x64.exe.sha256)
```
Expected: `OK`. If the `.sha256` file format does not suit `sha256sum -c`, compare the hashes by eye and record both.

- [ ] **Step 2: Stage the plugin exactly as the release zip lays it out**

```bash
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true
P="$E2E/app/plugins/NoMercy.Plugin.InternetRadio"; B=src/NoMercy.Plugin.InternetRadio/bin/Release/net10.0
mkdir -p "$P" && cp "$B/NoMercy.Plugin.InternetRadio.dll" "$B/NoMercy.Plugin.InternetRadio.deps.json" src/NoMercy.Plugin.InternetRadio/plugin.json src/NoMercy.Plugin.InternetRadio/README.md LICENSE "$P/" && cp -r src/NoMercy.Plugin.InternetRadio/lang "$P/"
ls "$P"
```

This is the same file list as CI's "Stage the plugin directory" step. No `.sideloaded` marker is written, which is exactly the "extract by hand" install.

- [ ] **Step 3: Start the isolated server**

The owner's real installation lives in `%LOCALAPPDATA%\NoMercy` and must not be touched.

1. Pick a free port: `netstat -ano | grep -E ":7727 " || echo free`.
2. Start the server in the background with its own app path and port:

```bash
NOMERCY_APP_PATH="$(cygpath -w "$E2E/app")" "$E2E/NoMercyMediaServer-windows-x64.exe" --internal-port 7727 --external-port 7727 > "$E2E/server.log" 2>&1 &
```

3. Ask the owner to sign in to this server, which registers it to their account. Then have them open it in the web app.
4. Record the server version from the log.
5. Confirm the plugin loaded with ABI 12.1 and not as Malfunctioned:

```bash
grep -i -E "internet radio|5KTKRT4Z2Y9P59Y40W5CX4TQKF|abi" "$E2E/server.log" | head -20
```

- [ ] **Step 4: Walk the checklist, and record evidence for each item**

Use the Chrome DevTools MCP tools (`navigate_page`, `take_snapshot`, `list_network_requests`, `take_screenshot`) against the web app while it is connected to this server. Write each result, with its evidence (screenshot path, network request status, log line), into `$E2E/results.md`.

1. The plugin appears under Music and under Settings.
2. The dashboard asks for consent. After enabling, playing a station shows the "waiting for the server owner" notice. The owner approves the `media.proxy` request, which may need `*` approved through the API if the dashboard UI does not offer it: record which. After approval, the station page shows Play.
3. Browse, a genre page, All stations and a search each draw stations.
4. Play:
   - the network log shows `GET /api/v1/plugins/5KTKRT4Z2Y9P59Y40W5CX4TQKF/media/<ticket>` with status 200 and a growing size;
   - the player shows it playing;
   - no request goes to the station's own host from the browser.
5. Logos show on cards (after one reload), on the station page and in the player bar.
6. Now playing updates within about 30 s on a station that announces titles. After stopping playback, the server's connection to that station closes within about a minute: check with `netstat` or the log.
7. Toggle a favourite on the station page. It appears on the browse page, and still does after restarting the server.
8. Sign in as the member account. Internet Radio is in its sidebar, and a station plays.
9. Leave a station page open for 16 minutes, then press Play. It plays.
10. Measure the All stations page payload: `GET .../plugins/<id>/view?route=...` response size in the network panel. Record it. If it exceeds 3 MB, stop and discuss the logo cap with the owner before tagging.

- [ ] **Step 5: Stop the server and keep the record**

Stop the background server process. Copy `results.md` into the task report. Do not commit it: it contains the owner's account details.

A failed item goes back to the task that owns it (1–9), with a failing test first where one can be written.

---

### Task 11: The upstream track, and the reply to Stoney

This task can run alongside Tasks 2–10. It changes no plugin code. **It sends nothing anywhere without the owner's approval of the exact text.**

**Files:**
- Create: `docs/upstream/2026-09-22-<slug>.md`, one per confirmed candidate
- Modify: existing `docs/upstream/*.md` (a status line, once confirmed)

- [ ] **Step 1: Make every repository current, without touching anyone's work**

For `nomercy-app-web`, `nomercy-media-server`, and whichever repository holds the player packages (find it: `ls ..`; `grep -l player-core ../*/package.json`):

```bash
git -C <repo> status --short | head; git -C <repo> fetch --all --prune
```

Read from `origin/<default branch>` with `git show` or `git grep`. Never check out or pull over someone's working tree. The `nomercy-media-server` checkout is fast-forwarded with `git pull --ff-only` **only** if its tree is clean.

- [ ] **Step 2: Verify each candidate from the spec's table**

For each of the 13 candidates:

1. Re-read the cited code at the newest commit. Record the commit hash.
2. Reproduce it at runtime where possible, on the Task 10 server, with a screenshot or network capture as evidence.
3. Search the target repository's open **and** closed issues:
   - `gh issue list -R NoMercy-Entertainment/<repo> --state all --search "<keywords>"`
   - for nomercy-plugins, also read #2 in full.
4. Decide exactly one outcome:
   - **confirmed, not reported**;
   - **already reported** (link it);
   - **already fixed** (record the commit);
   - **not a bug** (say why).

Write the outcome table into the task report.

- [ ] **Step 3: Draft a full issue for each "confirmed, not reported"**

Write each draft as `docs/upstream/2026-09-22-<slug>.md`, in the shape the existing files use:

- the repository;
- what happens;
- evidence by file:line at the recorded commit;
- steps to reproduce;
- what it costs a user or a plugin author;
- a proposed fix, marked as a proposal.

Do not overstate. What was read but not reproduced is labelled "read, not reproduced".

- [ ] **Step 4: Update the existing upstream notes**

For each existing `docs/upstream/*.md` whose status Step 2 established, add a dated status line at the top. Examples:
- `**Status (2026-09-22):** fixed in nomercy-app-web@2a24f7a10.`
- `still open, reported as #N.`

Change nothing else in them.

- [ ] **Step 5: Draft the reply to Stoney**

Write the draft into the task report, not a file. It should be short, friendly and concrete:
- the namespace-per-folder idea was taken, with a link to the Task 9 commit once pushed, or the hash;
- why `All()` was not adopted: the seven failing `CatalogProviderTests`, the swallowed caller cancellation, the all-or-nothing paging, the 20-second budget, and ordering by name instead of votes;
- the suggestion: raise `GenreMap.PerGenreLimit` for more stations per genre.

- [ ] **Step 6: Put it all to the owner**

Present the outcome table, every issue draft and the Stoney draft **with the AskUserQuestion tool**. Create an issue (`gh issue create -R NoMercy-Entertainment/<repo> --title ... --body-file docs/upstream/<file>.md`) or send the reply **only** for the items the owner approved, exactly as approved. Record each created issue's URL in its `docs/upstream` file.

- [ ] **Step 7: Commit the notes**

```bash
git add docs/upstream
git commit -m "docs(upstream): what ABI 12 left open elsewhere, verified"
```

---

## Before tagging 2.0.0

- [ ] Tasks 1–10 are complete, and Task 10's ten items are all evidenced.
- [ ] `git log --oneline main..feat/abi-12` reads as a clean story. No commit carries an attribution trailer: `git log main..feat/abi-12 --format=%B | grep -i -E "co-authored|generated with|claude|anthropic" || echo clean`.
- [ ] Merging to `main`, pushing, and tagging `v2.0.0` happen **only on the owner's explicit instruction**. After a push, watch the Forgejo run through the API (see the global instructions) until it is green. A red run is fixed before anything else.
