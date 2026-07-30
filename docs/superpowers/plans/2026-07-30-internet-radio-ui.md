# Internet Radio UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn a plugin whose only hook nothing consumes into a working browse-and-play radio UI, with its station catalogue fetched live from radio-browser.info.

**Architecture:** The plugin implements `IUiPlugin` (five path-based routes rendered as declarative `PluginView` trees) and `IScheduledTaskPlugin` (one daily job that refreshes the catalogue). Station data is fetched from radio-browser.info — ten seed stations by pinned UUID plus per-genre discovery — filtered through admission gates, cached to the plugin's data folder, and read from cache by every view. Playback happens entirely client-side via `playMedia` action intents, so nothing depends on the server's inbound plugin transports, both of which are currently broken.

**Tech Stack:** C# / .NET 10, `NoMercy.Plugins.Abstractions`, xunit + FluentAssertions, Forgejo Actions.

**Spec:** `docs/superpowers/specs/2026-07-30-internet-radio-ui-design.md`

## Global Constraints

- **Target framework** `net10.0`. **SDK** pinned by `global.json` to `10.0.302`, `rollForward: latestFeature`.
- **`TreatWarningsAsErrors`** is true for the plugin project. The build step also passes `-p:TreatWarningsAsErrors=true` explicitly.
- **Version is 1.0.2** and must read identically in exactly three places: `src/NoMercy.Plugin.InternetRadio/plugin.json`, the csproj `<Version>`, and `PluginIdentity.Version`.
- **Plugin id is `b3d4f1a2-7c5e-4d8a-9f10-1c2b3a4d5e6f` and must never change** — the host keys lifecycle state off it across restarts.
- **No station name, stream URL, logo, genre or country may appear in the source tree.** The only station data permitted is the ten seed UUIDs in `SeedStations.cs`.
- **`PluginText` variants are `title`, `subtitle`, `caption` only.** Any other string silently renders as body text. Do not copy `heading`/`subheading`/`body` from `nomercy-torrent-plugin`.
- **Routes carry state in the path, never the query string.** The web host sends only `route`; query parameters never leave the browser.
- **Icons must exist in the Moooom set.** The ones used here are verified present: `portableRadio`, `play`, `playlistAdd`, `globe`, `arrowLeft`, `gridMasonry`, `settings`. An unknown name silently renders as `plugged`.
- **Every file gets the SPDX header:**
  ```csharp
  // SPDX-License-Identifier: MIT
  // Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84
  ```
- **Never ship `NoMercy.Plugins.Abstractions.dll` or `NoMercy.Events.dll`.** Both are host-owned; a second copy gives the load context two incompatible identities of the same types.
- **Commit messages:** Conventional Commits (`type(scope): description`). No attribution or co-author trailers.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/NoMercy.Plugin.InternetRadio/PluginIdentity.cs` | Id, name, description, version, assembly filename — one source of truth |
| `src/NoMercy.Plugin.InternetRadio/InternetRadioPlugin.cs` | `IUiPlugin` + `IScheduledTaskPlugin`: lifecycle, route dispatch, refresh job |
| `src/NoMercy.Plugin.InternetRadio/plugin.json` | The manifest the server reads |
| `Catalog/RadioStation.cs` | One station as this plugin uses it, with its stable route `Id` |
| `Catalog/RadioBrowserStation.cs` | Wire shape of a radio-browser station record |
| `Catalog/StationGates.cs` | Admission rules: HTTPS, non-HLS, checked-ok, dedupe, slugs |
| `Catalog/GenreMap.cs` | radio-browser tags → the browse page's genre sections |
| `Catalog/SeedStations.cs` | The ten pinned UUIDs and the per-genre limit |
| `Catalog/RadioBrowserClient.cs` | `byuuid` + per-genre search over `IPluginContext.HttpClient` |
| `Catalog/CatalogSource.cs` | Where the stations on screen came from |
| `Catalog/StationCatalog.cs` | The resolved catalogue: lookup by id, grouping by genre |
| `Catalog/CatalogCache.cs` | Read/write `catalog-cache.json` in the data folder |
| `Catalog/StationOverrides.cs` | The user's `stations.json`, which replaces everything |
| `Catalog/CatalogProvider.cs` | Override-wins, cache-first, fetch-on-empty |
| `Views/RadioRoutes.cs` | The only place a route is parsed or built |
| `Views/StationCards.cs` | One station as a play-on-click card, shared by both grids |
| `Views/EmptyCatalog.cs` | The "no stations" panel, shared so it reads the same everywhere |
| `Views/BrowseView.cs` | `/` — genre chips + popular grid |
| `Views/GenreView.cs` | `/genre/{slug}` — one genre's grid |
| `Views/AllStationsView.cs` | `/all` — metadata table, rows navigate to detail |
| `Views/StationView.cs` | `/station/{id}` — detail, play/enqueue/homepage, full record |
| `Views/SettingsView.cs` | `/settings` — provenance, cache age, refresh, diagnostics |

Views are pure static `Build(...)` methods: catalogue in, `PluginView` out, no `IPluginContext` and no I/O. That is what makes them cheap to test exhaustively, and it keeps the plugin class the only thing that touches the network or the disk.

---

### Task 1: Repo restructure and build scaffolding

Moves the project under `src/`, adds the test project, and brings over the build scaffolding from `nomercy-torrent-plugin` so CI and a developer's machine cannot drift. Ends with a green build and passing tests.

**Files:**
- Create: `global.json`, `nuget.config`, `LICENSE`, `.gitattributes`
- Create: `scripts/fetch-abstractions.sh`, `scripts/fetch-abstractions.ps1`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/NoMercy.Plugin.InternetRadio.Tests.csproj`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/BuildSanityTests.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/RadioStation.cs` (placeholder, rewritten in Task 3)
- Move: `NoMercy.Plugin.InternetRadio/` to `src/NoMercy.Plugin.InternetRadio/`
- Delete: `src/NoMercy.Plugin.InternetRadio/Plugin.cs`, `RadioStation.cs`, `RadioStations.cs`
- Modify: `.gitignore`, `nomercy-radiostation-plugin.sln`, the plugin csproj

**Interfaces:**
- Consumes: nothing.
- Produces: a solution with `src/NoMercy.Plugin.InternetRadio` and `tests/NoMercy.Plugin.InternetRadio.Tests`; `./scripts/fetch-abstractions.sh` populating `./_nupkgs`.

- [ ] **Step 1: Move the project under `src/`**

```bash
git mv NoMercy.Plugin.InternetRadio src/NoMercy.Plugin.InternetRadio
```

- [ ] **Step 2: Add `global.json`**

```json
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "latestFeature"
  }
}
```

- [ ] **Step 3: Add `nuget.config`**

The `packageSourceMapping` block is load-bearing, not decoration: `NoMercy.Plugins.Abstractions` is unclaimed on nuget.org, and the plugin's floating `Version="*"` would otherwise resolve the highest match across *every* enabled source.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="local" value="./_nupkgs" />
  </packageSources>

  <!--
    NoMercy.* resolves from the local feed and nowhere else. The contract is not
    published to nuget.org - which is why scripts/fetch-abstractions.* exists - so
    anyone publishing that name at a higher version would otherwise get their
    assembly compiled into this plugin in place of the host's real contract.
  -->
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="NoMercy.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

- [ ] **Step 4: Add `scripts/fetch-abstractions.sh`**

Only `NoMercy.Plugins.Abstractions` and `NoMercy.Events` are packed. `NoMercy.Plugins.Mvc` holds `PluginControllerBase`, which only a plugin serving REST inherits; this plugin declares `"rest": false`.

```sh
#!/usr/bin/env sh
# Packs NoMercy.Plugins.Abstractions into a local NuGet feed.
#
# The contract is not published to nuget.org, so we clone the server and pack it.
# NoMercy.Events must be packed too: it is a ProjectReference of the abstractions,
# so packing only the abstractions yields a package whose dependency cannot resolve.
#
# NoMercy.Plugins.Mvc is deliberately NOT packed - see the plan's Task 1.

set -eu

# CI puts the right SDK on PATH. On a Windows dev machine the `dotnet` on PATH is
# an older SDK that cannot build net10.0, and the usable one is a side-by-side
# install under the user profile, so prefer that when it is there.
if [ -x "${USERPROFILE:-}/.dotnet/dotnet.exe" ]; then
    dotnet="${USERPROFILE}/.dotnet/dotnet.exe"
elif [ -x "${HOME:-}/.dotnet/dotnet" ]; then
    dotnet="${HOME}/.dotnet/dotnet"
else
    dotnet=dotnet
fi

root=$(cd "$(dirname "$0")/.." && pwd)
server="$root/_server"
feed="$root/_nupkgs"
# A release must be rebuildable. SERVER_REF pins the contract to one commit; it
# defaults to a branch for day-to-day work, but CI sets it to a SHA for a tag
# build so the artifact is reproducible instead of "whatever dev happened to be".
ref="${SERVER_REF:-${SERVER_BRANCH:-dev}}"

if [ ! -d "$server" ]; then
    git clone --depth=1 --branch="${SERVER_BRANCH:-dev}" --filter=blob:none --no-checkout \
        https://github.com/NoMercy-Entertainment/nomercy-media-server.git "$server"
    git -C "$server" sparse-checkout init --cone
fi

# Applied on every run, not only on the initial clone: setting it once means adding
# a project to the list silently does nothing on a checkout that already exists.
git -C "$server" sparse-checkout set \
    src/NoMercy.Plugins.Abstractions src/NoMercy.Events

git -C "$server" fetch --depth=1 origin "$ref"
git -C "$server" reset --hard FETCH_HEAD

mkdir -p "$feed"

# MSB9008 about a missing NoMercy.Analyzers is expected under a sparse checkout.
# It is an analyzer reference; the package builds correctly without it.
"$dotnet" pack "$server/src/NoMercy.Events/NoMercy.Events.csproj" -c Release -o "$feed"
"$dotnet" pack "$server/src/NoMercy.Plugins.Abstractions/NoMercy.Plugins.Abstractions.csproj" -c Release -o "$feed"

find "$feed" -maxdepth 1 -name '*.nupkg' -print

echo "contract packed from nomercy-media-server $(git -C "$server" rev-parse HEAD)"
```

- [ ] **Step 5: Add `scripts/fetch-abstractions.ps1`**

```powershell
#!/usr/bin/env pwsh
# Packs NoMercy.Plugins.Abstractions into a local NuGet feed.
# See fetch-abstractions.sh for why this exists and why Mvc is not packed.

$ErrorActionPreference = 'Stop'

$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$root   = Split-Path -Parent $PSScriptRoot
$server = Join-Path $root '_server'
$feed   = Join-Path $root '_nupkgs'
$branch = if ($env:SERVER_BRANCH) { $env:SERVER_BRANCH } else { 'dev' }
$ref    = if ($env:SERVER_REF) { $env:SERVER_REF } else { $branch }

if (-not (Test-Path $server)) {
    git clone --depth=1 --branch=$branch --filter=blob:none --no-checkout `
        https://github.com/NoMercy-Entertainment/nomercy-media-server.git $server
    git -C $server sparse-checkout init --cone
}

git -C $server sparse-checkout set src/NoMercy.Plugins.Abstractions src/NoMercy.Events
git -C $server fetch --depth=1 origin $ref
git -C $server reset --hard FETCH_HEAD

New-Item -ItemType Directory -Force $feed | Out-Null

& $dotnet pack (Join-Path $server 'src\NoMercy.Events\NoMercy.Events.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'packing NoMercy.Events failed' }

& $dotnet pack (Join-Path $server 'src\NoMercy.Plugins.Abstractions\NoMercy.Plugins.Abstractions.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'packing NoMercy.Plugins.Abstractions failed' }

Get-ChildItem $feed -Filter *.nupkg | ForEach-Object { Write-Host "  $($_.Name)" }
```

- [ ] **Step 6: Add `LICENSE` and `.gitattributes`**

`LICENSE` is the standard MIT text beginning:

```
MIT License

Copyright (c) 2026 Phillippe Pelzer
```

`.gitattributes`:

```
* text=auto eol=lf
*.sln text eol=crlf
*.png binary
```

- [ ] **Step 7: Replace `.gitignore`**

```
bin/
obj/
.vs/
.idea/
*.user
*.suo
artifacts/
TestResults/
.claude/*
_server/
_nupkgs/
```

- [ ] **Step 8: Rewrite the plugin csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <LangVersion>latest</LangVersion>
        <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
        <AssemblyName>NoMercy.Plugin.InternetRadio</AssemblyName>
        <RootNamespace>NoMercy.Plugin.InternetRadio</RootNamespace>
        <Version>1.0.2</Version>
        <Authors>NoMercy Community</Authors>
        <Description>Browse and play internet radio stations in the built-in player.</Description>
    </PropertyGroup>

    <ItemGroup>
        <!--
            The host owns this assembly at runtime: it is in the server's shared-assembly
            set, so the plugin load context deliberately resolves it to the host's copy
            rather than one sitting next to the plugin.

            Do NOT set CopyLocalLockFileAssemblies=true to "gather dependencies" for
            packaging: it would drop NoMercy.Plugins.Abstractions.dll and NoMercy.Events.dll
            beside the plugin, and the load context would then hold two incompatible
            identities of the same types. That surfaces as an unrelated-looking cast error
            far from its cause. The CI packaging step asserts neither assembly ships.
        -->
        <PackageReference Include="NoMercy.Plugins.Abstractions" Version="*" />
    </ItemGroup>

    <ItemGroup>
        <None Update="plugin.json">
            <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        </None>
    </ItemGroup>

</Project>
```

- [ ] **Step 9: Create the test project**

`tests/NoMercy.Plugin.InternetRadio.Tests/NoMercy.Plugin.InternetRadio.Tests.csproj`. The linked `plugin.json` is what lets `ManifestTests` read the shipped manifest from `AppContext.BaseDirectory` in Task 2.

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <LangVersion>latest</LangVersion>
        <IsPackable>false</IsPackable>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="FluentAssertions" Version="[7.0.0,8.0.0)" />
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
        <PackageReference Include="xunit" Version="2.*" />
        <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\src\NoMercy.Plugin.InternetRadio\NoMercy.Plugin.InternetRadio.csproj" />
    </ItemGroup>

    <ItemGroup>
        <None Include="..\..\src\NoMercy.Plugin.InternetRadio\plugin.json"
              Link="plugin.json"
              CopyToOutputDirectory="PreserveNewest" />
    </ItemGroup>

</Project>
```

- [ ] **Step 10: Delete the dead media-source implementation**

Nothing in the server consumes `IMediaSourcePlugin` — it appears there only in its own declaration and one abstractions test. All three files go; `RadioStation` is rewritten in Task 3.

```bash
git rm src/NoMercy.Plugin.InternetRadio/Plugin.cs \
       src/NoMercy.Plugin.InternetRadio/RadioStation.cs \
       src/NoMercy.Plugin.InternetRadio/RadioStations.cs
```

- [ ] **Step 11: Add the placeholder `RadioStation`**

`src/NoMercy.Plugin.InternetRadio/Catalog/RadioStation.cs` — replaced in full by Task 3, present now only so the solution compiles:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

public sealed record RadioStation
{
    public required string Name { get; init; }
    public required string StreamUrl { get; init; }
}
```

- [ ] **Step 12: Write the sanity test**

`tests/NoMercy.Plugin.InternetRadio.Tests/BuildSanityTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests;

// Proves the test project resolves the plugin assembly and that the linked manifest
// is copied next to the test binary. Both are wiring that fails silently otherwise:
// a missing plugin.json makes every manifest assertion in Task 2 fail for a reason
// that has nothing to do with the manifest.
public class BuildSanityTests
{
    [Fact]
    public void PluginAssembly_IsReferenced()
    {
        typeof(RadioStation).Assembly.GetName().Name
            .Should().Be("NoMercy.Plugin.InternetRadio");
    }

    [Fact]
    public void Manifest_IsCopiedNextToTheTestBinary()
    {
        File.Exists(Path.Combine(AppContext.BaseDirectory, "plugin.json"))
            .Should().BeTrue("ManifestTests reads it from here");
    }
}
```

- [ ] **Step 13: Rebuild the solution file**

```bash
rm nomercy-radiostation-plugin.sln
dotnet new sln -n nomercy-radiostation-plugin
dotnet sln add src/NoMercy.Plugin.InternetRadio/NoMercy.Plugin.InternetRadio.csproj
dotnet sln add tests/NoMercy.Plugin.InternetRadio.Tests/NoMercy.Plugin.InternetRadio.Tests.csproj
```

- [ ] **Step 14: Pack the contract, build and test**

```bash
chmod +x scripts/fetch-abstractions.sh
./scripts/fetch-abstractions.sh
dotnet restore
dotnet build -c Release -p:TreatWarningsAsErrors=true
dotnet test -c Release --no-build
```

Expected: restore resolves `NoMercy.Plugins.Abstractions` from `_nupkgs`, build is clean, 2 tests pass.

- [ ] **Step 15: Commit**

```bash
git add -A
git commit -m "build: restructure to src/ + tests/ and adopt the plugin build standard"
```

---

### Task 2: Plugin identity, manifest 1.0.2, and the tests that pin them together

The reported defect — tag `v1.0.1` shipping a manifest reading `1.0.0` — was possible because one number lived in three files with no guard. This task creates the single source of truth and the tests that fail when they drift.

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/PluginIdentity.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/ManifestTests.cs`
- Modify: `src/NoMercy.Plugin.InternetRadio/plugin.json`

**Interfaces:**
- Consumes: nothing.
- Produces: `PluginIdentity.Id` (`Guid`), `.Name` and `.Description` (`const string`), `.Version` (`Version`), `.AssemblyFileName` (`const string`); `ManifestTests.LoadManifest()` returning `PluginManifest`, `internal` so later tests reuse it.

- [ ] **Step 1: Write `PluginIdentity.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

// The manifest and the IPlugin implementation must agree on all of this, and the host
// matches a loaded assembly to its manifest by id. A drift between the two is a plugin
// that either fails to load or loads as something it is not, so both sides read these
// constants and ManifestTests asserts they match the shipped json.
//
// The id is the one value here that must NEVER change: the host keys lifecycle state
// off it across restarts, so a new id is a new plugin as far as every installed server
// is concerned.
public static class PluginIdentity
{
    public static Guid Id { get; } = new("b3d4f1a2-7c5e-4d8a-9f10-1c2b3a4d5e6f");

    public const string Name = "Internet Radio";

    public const string Description =
        "Browse and play internet radio stations in the built-in player.";

    public static Version Version { get; } = new(1, 0, 2);

    public const string AssemblyFileName = "NoMercy.Plugin.InternetRadio.dll";
}
```

- [ ] **Step 2: Rewrite `plugin.json`**

```json
{
  "id": "b3d4f1a2-7c5e-4d8a-9f10-1c2b3a4d5e6f",
  "name": "Internet Radio",
  "description": "Browse and play internet radio stations in the built-in player.",
  "version": "1.0.2",
  "targetAbi": "10.0",
  "author": "NoMercy Community",
  "projectUrl": "https://forgejo.phillippepelzer.me/FiLL/nomercy-radiostation-plugin",
  "assembly": "NoMercy.Plugin.InternetRadio.dll",
  "autoEnabled": true,
  "capabilities": {
    "hooks": ["ui", "scheduledTask"],
    "rest": false,
    "ws": false,
    "network": { "hosts": ["*.api.radio-browser.info"] },
    "ui": {
      "mounts": [
        { "section": "music",    "label": "Internet Radio", "icon": "portableRadio", "route": "/" },
        { "section": "settings", "label": "Internet Radio", "icon": "portableRadio", "route": "/settings" }
      ]
    }
  }
}
```

- [ ] **Step 3: Write the failing manifest tests**

`tests/NoMercy.Plugin.InternetRadio.Tests/ManifestTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json;
using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests;

public class ManifestTests
{
    // Internal so every other test reads the manifest the same way rather than
    // duplicating the load - two readers that could disagree about which file is the
    // real one would defeat the point of asserting they agree.
    internal static PluginManifest LoadManifest()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "plugin.json");
        PluginManifest? manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path));

        manifest.Should().NotBeNull();
        return manifest!;
    }

    [Fact]
    public void Manifest_DeserialisesWithTheHostsOwnType()
    {
        PluginManifest manifest = LoadManifest();

        manifest.Id.Should().NotBeEmpty();
        manifest.Name.Should().NotBeNullOrWhiteSpace();
        manifest.Description.Should().NotBeNullOrWhiteSpace();
        manifest.Version.Should().NotBeNullOrWhiteSpace();
        manifest.Assembly.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Manifest_IdMatchesPluginIdentity()
    {
        LoadManifest().Id.Should().Be(PluginIdentity.Id);
    }

    [Fact]
    public void Manifest_NameAndDescriptionMatchPluginIdentity()
    {
        PluginManifest manifest = LoadManifest();

        manifest.Name.Should().Be(PluginIdentity.Name);
        manifest.Description.Should().Be(PluginIdentity.Description);
    }

    // The defect this repo actually shipped: v1.0.1 was tagged on a commit whose
    // manifest read 1.0.0, so an installed server reported 1.0.0 and was told an
    // update was available forever. CI gates the tag against this same value.
    [Fact]
    public void Manifest_VersionMatchesPluginIdentity()
    {
        Version.Parse(LoadManifest().Version).Should().Be(PluginIdentity.Version);
    }

    [Fact]
    public void Manifest_VersionIsExactly_1_0_2()
    {
        LoadManifest().Version.Should().Be("1.0.2");
    }

    [Fact]
    public void Manifest_AssemblyNameMatchesTheBuiltAssembly()
    {
        PluginManifest manifest = LoadManifest();

        manifest.Assembly.Should().Be(PluginIdentity.AssemblyFileName);
        File.Exists(Path.Combine(AppContext.BaseDirectory, manifest.Assembly)).Should().BeTrue();
    }

    [Fact]
    public void Manifest_TargetAbiIsCompatibleWithTheShippedAbi()
    {
        PluginAbi.IsCompatible(LoadManifest().TargetAbi).Should().BeTrue();
    }

    // PluginUiController.HasUi refuses to serve a view for a plugin that has not
    // declared the ui hook, so this one is the difference between a working plugin
    // and one that is installed, enabled and invisible.
    [Fact]
    public void Manifest_DeclaresExactlyTheHooksThisVersionImplements()
    {
        LoadManifest().Capabilities!.Hooks
            .Should().BeEquivalentTo(new[] { PluginHookCapability.Ui, PluginHookCapability.ScheduledTask });
    }

    // mediaSource is gone on purpose: the server consumes it nowhere, and a manifest
    // is what an owner reviews at consent time. Declaring a capability that does
    // nothing is a false promise.
    [Fact]
    public void Manifest_NoLongerDeclaresTheMediaSourceHookNothingConsumes()
    {
        LoadManifest().Capabilities!.Hooks
            .Should().NotContain(PluginHookCapability.MediaSource);
    }

    [Fact]
    public void Manifest_DeclaresNoElevatedHook()
    {
        LoadManifest().Capabilities!.Hooks
            .Should().NotContain(hook => PluginHookCapability.Elevated.Contains(hook));
    }

    // Both inbound transports are broken upstream (server issue #26 for REST; nothing
    // registers hub handlers), and nothing in this plugin implements either. Declaring
    // them would promise a save path that cannot work.
    [Fact]
    public void Manifest_DeclaresNeitherRestNorWs()
    {
        PluginCapabilities capabilities = LoadManifest().Capabilities!;

        capabilities.Rest.Should().BeFalse();
        capabilities.Ws.Should().BeFalse();
    }

    // Scoped to radio-browser's mirrors and nothing wider. The allowlist glob is
    // label-scoped - '*' matches within one label - so this covers all./de1./nl1.
    // and cannot broaden to another domain.
    [Fact]
    public void Manifest_DeclaresOnlyTheRadioBrowserHost()
    {
        LoadManifest().Capabilities!.Network!.Hosts
            .Should().Equal("*.api.radio-browser.info");
    }

    [Fact]
    public void Manifest_MountsBrowseUnderMusicAndSettingsUnderSettings()
    {
        List<PluginUiMount> mounts = LoadManifest().Capabilities!.Ui!.Mounts;

        mounts.Should().HaveCount(2);
        mounts.Should().ContainSingle(mount =>
            mount.Section == PluginUiSection.Music && mount.Route == "/");
        mounts.Should().ContainSingle(mount =>
            mount.Section == PluginUiSection.Settings && mount.Route == "/settings");
    }

    // pluginIcon() silently substitutes 'plugged' for a name the app does not have,
    // so a typo here is a nav entry the user cannot tell apart from any other.
    [Fact]
    public void Manifest_UsesAnIconThatExistsInTheMoooomSet()
    {
        LoadManifest().Capabilities!.Ui!.Mounts
            .Should().OnlyContain(mount => mount.Icon == "portableRadio");
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

```bash
dotnet test -c Release --filter FullyQualifiedName~ManifestTests
```

Expected: FAIL, because the old manifest declares `mediaSource`, version `1.0.0`, no capabilities block and no UI mounts.

- [ ] **Step 5: Build and run the full suite**

```bash
dotnet build -c Release -p:TreatWarningsAsErrors=true
dotnet test -c Release --no-build
```

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "fix(manifest): read 1.0.2, declare only ui + scheduledTask, pin identity in tests"
```

---

### Task 3: Station model, wire DTO, and the admission gates

The gates are where a stream that cannot play is refused. This used to be assertable over shipped data; now it is code, so it is tested hard. Pure functions, no network.

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/RadioBrowserStation.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/StationGates.cs`
- Rewrite: `src/NoMercy.Plugin.InternetRadio/Catalog/RadioStation.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/StationGatesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `RadioBrowserStation` — wire record with `StationUuid`, `Name`, `Url`, `UrlResolved`, `Homepage`, `Favicon`, `Tags`, `CountryCode`, `Language`, `Codec`, `Bitrate`, `Hls`, `LastCheckOk`, `Votes`.
  - `RadioStation` — `Id`, `Name`, `StreamUrl`, `LogoUrl`, `Homepage`, `Genre`, `Country`, `Language`, `BitrateKbps`, `Codec`, `Popularity`, `IsUserSupplied`.
  - `StationGates.Admits(RadioBrowserStation) : bool`
  - `StationGates.EffectiveUrl(RadioBrowserStation) : string`
  - `StationGates.Deduplicate(IEnumerable<RadioStation>) : IReadOnlyList<RadioStation>`
  - `StationGates.Slugify(string) : string`

- [ ] **Step 1: Write the failing gate tests**

`tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/StationGatesTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public class StationGatesTests
{
    private static RadioBrowserStation Wire(
        string url = "https://example.com/stream.mp3",
        int hls = 0,
        int lastCheckOk = 1,
        string name = "Example FM"
    ) =>
        new()
        {
            StationUuid = "11111111-2222-3333-4444-555555555555",
            Name = name,
            Url = url,
            UrlResolved = url,
            Hls = hls,
            LastCheckOk = lastCheckOk,
        };

    // The gate that matters most. The web client is served over HTTPS, so an http
    // stream is blocked as mixed content and never reaches the audio element. This
    // is not hypothetical - it is why the BBC entries this plugin used to ship could
    // never play.
    [Fact]
    public void Admits_RejectsPlainHttp()
    {
        StationGates.Admits(Wire(url: "http://example.com/stream.mp3")).Should().BeFalse();
    }

    [Fact]
    public void Admits_AcceptsHttps()
    {
        StationGates.Admits(Wire(url: "https://example.com/stream.mp3")).Should().BeTrue();
    }

    // HLS in a plain audio element only works in Safari, so an m3u8 is silence on
    // every other client.
    [Fact]
    public void Admits_RejectsHls()
    {
        StationGates.Admits(Wire(hls: 1)).Should().BeFalse();
    }

    [Fact]
    public void Admits_RejectsWhatRadioBrowserCouldNotCheck()
    {
        StationGates.Admits(Wire(lastCheckOk: 0)).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Admits_RejectsAMissingName(string? name)
    {
        StationGates.Admits(Wire(name: name!)).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/stream")]
    public void Admits_RejectsAnUnusableUrl(string url)
    {
        StationGates.Admits(Wire(url: url)).Should().BeFalse();
    }

    // url_resolved is what radio-browser followed redirects to and is the better
    // answer; url is the fallback for a record that has not been resolved yet.
    [Fact]
    public void EffectiveUrl_PrefersTheResolvedUrl()
    {
        RadioBrowserStation station = new()
        {
            StationUuid = "a",
            Name = "n",
            Url = "https://example.com/original",
            UrlResolved = "https://cdn.example.com/resolved",
        };

        StationGates.EffectiveUrl(station).Should().Be("https://cdn.example.com/resolved");
    }

    [Fact]
    public void EffectiveUrl_FallsBackToUrlWhenNothingWasResolved()
    {
        RadioBrowserStation station = new()
        {
            StationUuid = "a",
            Name = "n",
            Url = "https://example.com/original",
            UrlResolved = null,
        };

        StationGates.EffectiveUrl(station).Should().Be("https://example.com/original");
    }

    private static RadioStation Station(string id, string name, string url) =>
        new() { Id = id, Name = name, StreamUrl = url };

    // The seed set and the genre sweep overlap by design - a curated station is
    // usually also popular in its genre - so the same station arrives twice.
    [Fact]
    public void Deduplicate_DropsTheSameStreamTwice()
    {
        IReadOnlyList<RadioStation> result = StationGates.Deduplicate(
        [
            Station("a", "First", "https://example.com/stream"),
            Station("b", "Second", "https://example.com/stream"),
        ]);

        result.Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public void Deduplicate_TreatsATrailingSlashAndCasingAsTheSameStream()
    {
        IReadOnlyList<RadioStation> result = StationGates.Deduplicate(
        [
            Station("a", "First", "https://Example.com/Stream/"),
            Station("b", "Second", "https://example.com/Stream"),
        ]);

        result.Should().ContainSingle();
    }

    // Same station, different mirror host. Names collide even when URLs do not, and
    // two identical rows in the grid look like a bug to the user.
    [Fact]
    public void Deduplicate_DropsTheSameNameOnADifferentMirror()
    {
        IReadOnlyList<RadioStation> result = StationGates.Deduplicate(
        [
            Station("a", "SomaFM Groove Salad", "https://ice1.example.com/gs"),
            Station("b", "somafm  groove-salad!", "https://ice5.example.com/gs"),
        ]);

        result.Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public void Deduplicate_KeepsGenuinelyDifferentStations()
    {
        IReadOnlyList<RadioStation> result = StationGates.Deduplicate(
        [
            Station("a", "First", "https://example.com/one"),
            Station("b", "Second", "https://example.com/two"),
        ]);

        result.Should().HaveCount(2);
    }

    // First wins, so a seed keeps its place when the genre sweep finds it again.
    [Fact]
    public void Deduplicate_KeepsTheFirstOccurrence()
    {
        IReadOnlyList<RadioStation> result = StationGates.Deduplicate(
        [
            Station("seed", "Station", "https://example.com/s"),
            Station("discovered", "Station", "https://example.com/s"),
        ]);

        result.Should().ContainSingle().Which.Id.Should().Be("seed");
    }

    [Theory]
    [InlineData("SomaFM - Groove Salad", "somafm-groove-salad")]
    [InlineData("FIP  (hifi.aac)", "fip-hifi-aac")]
    [InlineData("  Radio  Paradise  ", "radio-paradise")]
    [InlineData("100% Hits!", "100-hits")]
    public void Slugify_ProducesAUrlSafeStableId(string name, string expected)
    {
        StationGates.Slugify(name).Should().Be(expected);
    }

    // A name with nothing slug-safe in it must still produce a routable id rather
    // than an empty string, which would collide with every other such station.
    [Fact]
    public void Slugify_NeverReturnsEmpty()
    {
        StationGates.Slugify("!!!").Should().NotBeEmpty();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test -c Release --filter FullyQualifiedName~StationGatesTests
```

Expected: FAIL — `RadioBrowserStation` and `StationGates` do not exist.

- [ ] **Step 3: Write `RadioBrowserStation.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json.Serialization;

namespace NoMercy.Plugin.InternetRadio;

// The wire shape of one radio-browser station record. Only the fields this plugin
// reads are declared; the API returns roughly forty and the rest are ignored.
//
// Every field is nullable or defaulted on purpose. This is a third party's JSON
// arriving over the network, and a record missing a field it has always sent must
// deserialise to something the gates can reject rather than throw during parsing -
// one malformed row would otherwise lose the whole response.
public sealed record RadioBrowserStation
{
    [JsonPropertyName("stationuuid")]
    public required string StationUuid { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("url_resolved")]
    public string? UrlResolved { get; init; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }

    [JsonPropertyName("favicon")]
    public string? Favicon { get; init; }

    [JsonPropertyName("tags")]
    public string? Tags { get; init; }

    [JsonPropertyName("countrycode")]
    public string? CountryCode { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonPropertyName("codec")]
    public string? Codec { get; init; }

    [JsonPropertyName("bitrate")]
    public int Bitrate { get; init; }

    /// <summary>1 when the stream is HLS. Unplayable outside Safari in a plain audio element.</summary>
    [JsonPropertyName("hls")]
    public int Hls { get; init; }

    /// <summary>
    /// radio-browser's own liveness flag. Trusted for discovery and nothing more:
    /// it reported a 404 Tomorrowland Anthems URL as healthy, which is how that
    /// station came to need submitting by hand. Declaration is not verification.
    /// </summary>
    [JsonPropertyName("lastcheckok")]
    public int LastCheckOk { get; init; }

    [JsonPropertyName("votes")]
    public int Votes { get; init; }
}
```

- [ ] **Step 4: Rewrite `RadioStation.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json.Serialization;

namespace NoMercy.Plugin.InternetRadio;

// One station as this plugin uses it, after the wire record has been through the
// gates. Separate from RadioBrowserStation so the views never see a field they
// must not render and never depend on a third party's field names.
public sealed record RadioStation
{
    /// <summary>
    /// Stable and URL-safe: it is a path segment in /station/{id}. A radio-browser
    /// UUID for a fetched station, a slug of the name for a user-supplied one.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("streamUrl")]
    public required string StreamUrl { get; init; }

    [JsonPropertyName("logoUrl")]
    public string? LogoUrl { get; init; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }

    /// <summary>The mapped section label, not the raw tag list. See GenreMap.</summary>
    [JsonPropertyName("genre")]
    public string? Genre { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    /// <summary>Null when radio-browser reports 0, which means "unknown", not "silent".</summary>
    [JsonPropertyName("bitrateKbps")]
    public int? BitrateKbps { get; init; }

    [JsonPropertyName("codec")]
    public string? Codec { get; init; }

    /// <summary>radio-browser votes. Ordering only - never shown.</summary>
    [JsonPropertyName("popularity")]
    public int Popularity { get; init; }

    /// <summary>True for a station from the user's stations.json. Shown as provenance.</summary>
    [JsonPropertyName("isUserSupplied")]
    public bool IsUserSupplied { get; init; }
}
```

- [ ] **Step 5: Write `StationGates.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text;

namespace NoMercy.Plugin.InternetRadio;

// What a station has to be before it is allowed into the catalogue.
//
// These are admission rules for DISCOVERED stations, applied to what radio-browser
// declares. They are not proof a stream works - see RadioBrowserStation.LastCheckOk
// for why that distinction is real. A user's own stations.json is deliberately not
// gated: a hand-written list is their call, and silently dropping their entries
// would be worse than letting one fail visibly in the player.
public static class StationGates
{
    /// <summary>
    /// url_resolved is what radio-browser followed redirects to, and is the better
    /// answer when it has one.
    /// </summary>
    public static string EffectiveUrl(RadioBrowserStation station) =>
        !string.IsNullOrWhiteSpace(station.UrlResolved) ? station.UrlResolved : station.Url ?? string.Empty;

    public static bool Admits(RadioBrowserStation station)
    {
        if (string.IsNullOrWhiteSpace(station.Name))
        {
            return false;
        }

        // HTTPS is mandatory, not preferred: the dashboard is served over HTTPS, so
        // the browser blocks an http stream as mixed content before it reaches the
        // player. A station that cannot play is worse than one that is absent,
        // because the absent one does not look like the plugin is broken.
        string url = EffectiveUrl(station);
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        // Silence on every client but Safari.
        if (station.Hls != 0)
        {
            return false;
        }

        return station.LastCheckOk == 1;
    }

    /// <summary>
    /// First occurrence wins, so a seed keeps its place when the genre sweep finds
    /// the same station again — which it routinely does, since a curated station is
    /// usually also a popular one.
    /// </summary>
    public static IReadOnlyList<RadioStation> Deduplicate(IEnumerable<RadioStation> stations)
    {
        HashSet<string> seenUrls = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seenNames = new(StringComparer.Ordinal);
        List<RadioStation> kept = [];

        foreach (RadioStation station in stations)
        {
            string url = station.StreamUrl.Trim().TrimEnd('/');
            string name = Slugify(station.Name);

            // Both keys, because the same station appears under different mirror
            // hosts (same name, different URL) and under different names for the
            // same stream (same URL, different name).
            if (!seenUrls.Add(url) || !seenNames.Add(name))
            {
                continue;
            }

            kept.Add(station);
        }

        return kept;
    }

    /// <summary>
    /// A lowercase, hyphen-separated, ASCII-safe form of a name. Used both as the
    /// dedupe key and as the route id for a user-supplied station, so it has to be
    /// stable for the same name and safe in a URL path segment.
    /// </summary>
    public static string Slugify(string name)
    {
        StringBuilder builder = new(name.Length);
        bool pendingSeparator = false;

        foreach (char character in name)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        // A name with nothing slug-safe in it still needs a routable id, and an
        // empty one would collide with every other such station.
        return builder.Length > 0 ? builder.ToString() : "station";
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet build -c Release -p:TreatWarningsAsErrors=true
dotnet test -c Release --no-build --filter FullyQualifiedName~StationGatesTests
```

Expected: PASS.

- [ ] **Step 7: Run the full suite**

```bash
dotnet test -c Release --no-build
```

Expected: all pass. `BuildSanityTests.PluginAssembly_IsReferenced` still compiles — `RadioStation` gained fields but kept its name and namespace.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(catalog): add the station model and the admission gates"
```

---

### Task 4: Genre sections and the pinned seed UUIDs

radio-browser stations carry free-text tags — thousands of distinct ones. `GenreMap` collapses them onto a fixed set of sections so the browse page has stable navigation. `SeedStations` holds the ten pinned UUIDs, which are the only station data allowed in the source tree.

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/GenreMap.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/SeedStations.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/GenreMapTests.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/SeedTests.cs`

**Interfaces:**
- Consumes: `StationGates.Slugify`.
- Produces:
  - `GenreSection` — record with `Tag`, `Label`, `Slug`.
  - `GenreMap.Sections : IReadOnlyList<GenreSection>`
  - `GenreMap.Other : string` (the `"Other"` label)
  - `GenreMap.Resolve(string? tags) : string`
  - `GenreMap.BySlug(string slug) : GenreSection?`
  - `SeedStations.Uuids : IReadOnlyList<string>`
  - `SeedStations.PerGenreLimit : int`

- [ ] **Step 1: Write the failing tests**

`tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/GenreMapTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public class GenreMapTests
{
    [Fact]
    public void Sections_HaveUniqueSlugsSoARouteResolvesToOne()
    {
        GenreMap.Sections.Select(section => section.Slug)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Sections_HaveUniqueLabels()
    {
        GenreMap.Sections.Select(section => section.Label)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Sections_SlugsAreUrlSafe()
    {
        GenreMap.Sections.Should().OnlyContain(section => section.Slug == StationGates.Slugify(section.Label));
    }

    [Theory]
    [InlineData("ambient,atmospheric,chillout,drone", "Ambient")]
    [InlineData("dance,edm,electronic", "Dance & Electronic")]
    [InlineData("jazz,smooth jazz", "Jazz")]
    [InlineData("HIP HOP,rap", "Hip Hop")]
    public void Resolve_MapsATagListOntoItsSection(string tags, string expected)
    {
        GenreMap.Resolve(tags).Should().Be(expected);
    }

    // Section order is the priority order. A station tagged both is a real case -
    // "ambient,chillout" is the single commonest pair in the database - and it has to
    // land in exactly one section, deterministically, or the same station appears
    // twice in the browse page.
    [Fact]
    public void Resolve_PicksTheEarliestMatchingSection()
    {
        GenreMap.Resolve("chillout,ambient").Should().Be("Ambient");
    }

    // Several of the pinned Tomorrowland records carry no tags at all. They must
    // still land somewhere routable rather than dropping out of the genre pages.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("something,nobody,mapped")]
    public void Resolve_FallsBackToOtherRatherThanNull(string? tags)
    {
        GenreMap.Resolve(tags).Should().Be(GenreMap.Other);
    }

    [Fact]
    public void Resolve_IgnoresSurroundingWhitespaceOnATag()
    {
        GenreMap.Resolve("  rock ,  pop ").Should().Be("Rock");
    }

    // Substring matching would put "rockabilly" in Rock and "poparazzi" in Pop.
    [Fact]
    public void Resolve_MatchesAWholeTagAndNotASubstring()
    {
        GenreMap.Resolve("rockabilly").Should().Be(GenreMap.Other);
    }

    [Fact]
    public void BySlug_FindsASectionAndIsCaseInsensitive()
    {
        GenreMap.BySlug("drum-bass")!.Label.Should().Be("Drum & Bass");
        GenreMap.BySlug("AMBIENT")!.Label.Should().Be("Ambient");
    }

    [Fact]
    public void BySlug_ReturnsNullForAnUnknownSlug()
    {
        GenreMap.BySlug("no-such-genre").Should().BeNull();
    }
}
```

`tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/SeedTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

// Only what can be asserted without a network. Whether each UUID still resolves and
// still passes the gates is checked by scripts/resolve-seeds.sh before a release -
// a unit test that reaches radio-browser would turn their outage into our red build.
public class SeedTests
{
    [Fact]
    public void Seeds_AreTheTenCuratedStations()
    {
        SeedStations.Uuids.Should().HaveCount(10);
    }

    [Fact]
    public void Seeds_AreWellFormedGuids()
    {
        SeedStations.Uuids.Should().OnlyContain(uuid => Guid.TryParse(uuid, out _));
    }

    // A duplicate would ask radio-browser for the same station twice and then rely on
    // dedupe to hide it, which is a silent way to be one station short of the ten.
    [Fact]
    public void Seeds_AreUnique()
    {
        SeedStations.Uuids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void PerGenreLimit_IsPositive()
    {
        SeedStations.PerGenreLimit.Should().BeGreaterThan(0);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test -c Release --filter "FullyQualifiedName~GenreMapTests|FullyQualifiedName~SeedTests"
```

Expected: FAIL — `GenreMap` and `SeedStations` do not exist.

- [ ] **Step 3: Write `GenreMap.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

/// <param name="Tag">The radio-browser tag queried for discovery, and matched against a station's own tags.</param>
/// <param name="Label">What the user sees.</param>
/// <param name="Slug">The /genre/{slug} path segment.</param>
public sealed record GenreSection(string Tag, string Label, string Slug);

// radio-browser tags are free text and there are thousands of them, so browsing by
// raw tag is not navigation - it is a word cloud. These are the sections the browse
// page offers, and they are also exactly the queries the discovery sweep makes.
//
// ORDER IS PRIORITY. A station tagged "ambient,chillout" has to land in one section
// and only one, or it appears twice on the browse page; the earliest match wins.
public static class GenreMap
{
    /// <summary>Where a station lands when it carries no tag this plugin maps.</summary>
    public const string Other = "Other";

    public static IReadOnlyList<GenreSection> Sections { get; } =
        [
            Section("ambient", "Ambient"),
            Section("chillout", "Chillout"),
            Section("dance", "Dance & Electronic"),
            Section("house", "House"),
            Section("techno", "Techno"),
            Section("trance", "Trance"),
            Section("drum and bass", "Drum & Bass"),
            Section("jazz", "Jazz"),
            Section("classical", "Classical"),
            Section("rock", "Rock"),
            Section("metal", "Metal"),
            Section("indie", "Indie"),
            Section("pop", "Pop"),
            Section("hip hop", "Hip Hop"),
            Section("reggae", "Reggae"),
            Section("soul", "Soul & Funk"),
            Section("oldies", "Oldies"),
        ];

    private static GenreSection Section(string tag, string label) =>
        new(tag, label, StationGates.Slugify(label));

    /// <summary>
    /// The section a station belongs to, from its own tag list. Whole-tag matching,
    /// not substring: "rockabilly" is not Rock and "poparazzi" is not Pop.
    /// </summary>
    public static string Resolve(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return Other;
        }

        HashSet<string> stationTags = new(StringComparer.OrdinalIgnoreCase);
        foreach (string tag in tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            stationTags.Add(tag);
        }

        foreach (GenreSection section in Sections)
        {
            if (stationTags.Contains(section.Tag))
            {
                return section.Label;
            }
        }

        return Other;
    }

    public static GenreSection? BySlug(string slug) =>
        Sections.FirstOrDefault(section =>
            string.Equals(section.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 4: Write `SeedStations.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

// The curated stations, pinned by radio-browser UUID and by nothing else. This is
// the ONLY station data in the source tree: no names, no stream URLs, no logos, no
// genres. All of that is fetched, so a station that changes its stream - as
// Tomorrowland Anthems did - is corrected upstream instead of here.
//
// Each was resolved by matching the exact stream URL this plugin used to hardcode,
// never by name similarity: an earlier pass that fell back to "most-voted station
// with a similar name" silently swapped Radio Paradise Rock Mix in for Main Mix.
//
// scripts/resolve-seeds.sh re-checks that every one of these still resolves and
// still passes the gates.
//
// Not here, and deliberately: BBC Radio 1 and BBC Radio 6 Music. radio-browser has
// 13 and 3 records for them respectively and every one is HLS over http, so there is
// nothing gate-passing to pin. They were unplayable in the browser before this
// change too - the URLs this plugin shipped were http - so nothing that worked was
// lost. Adding one back is one line, the day a usable record exists.
public static class SeedStations
{
    public static IReadOnlyList<string> Uuids { get; } =
        [
            "960cf833-0601-11e8-ae97-52543be04c81", // SomaFM - Groove Salad
            "960eb2e9-0601-11e8-ae97-52543be04c81", // SomaFM - Drone Zone
            "4aad9a26-15ef-4c13-a947-74c483181b4f", // Radio Paradise - Main Mix (the HTTPS ti-main-320)
            "a3dbc189-d23e-4308-803f-5aad26432b8c", // NTS Radio 1
            "445cbb3a-1c4e-49aa-a268-f5b6acfa8f2e", // KEXP 90.3 Seattle
            "a349e1e9-2844-443a-973b-09a02fa12c8e", // FIP - Radio France
            "9e31c4e7-03b6-4a80-a4e2-5977b023d32c", // Tomorrowland - One World Radio
            "93e04f4d-f964-453a-9c64-9dd7bc32f21d", // Tomorrowland - Anthems (submitted upstream by us)
            "c77644fa-5d0d-47f6-93ef-850805efefad", // Tomorrowland - Daybreak Sessions
            "d23f9ea2-80bd-4b43-b25c-31903bbbcaec", // Tomorrowland - bigFM One World Radio
        ];

    /// <summary>
    /// How many stations to take per genre. Seventeen sections at five each is an
    /// upper bound of eighty-five before dedupe, which is a browse page worth
    /// scrolling rather than one worth searching.
    /// </summary>
    public const int PerGenreLimit = 5;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet build -c Release -p:TreatWarningsAsErrors=true
dotnet test -c Release --no-build
```

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(catalog): add genre sections and the ten pinned seed UUIDs"
```

> **Known outcome, not a defect:** three of the four Tomorrowland records carry no
> tags in radio-browser, so `Resolve` places them in **Other**. They appear on the
> browse grid, the all-stations table and their own detail pages exactly like any
> other station — only their genre section is generic. The fix is to tag them
> upstream, which benefits every radio-browser consumer; hardcoding a genre here
> would put station data back in the source tree.

---

### Task 5: The radio-browser client and its failure modes

The only code in this plugin that touches the network. It is deliberately thin — two calls, no retry, no caching — so that the interesting behaviour (what happens when it fails) lives in one place that Task 6 owns.

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/RadioBrowserClient.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/TestSupport/FakeHttpMessageHandler.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/RadioBrowserClientTests.cs`

**Interfaces:**
- Consumes: `RadioBrowserStation`, `SeedStations.PerGenreLimit`.
- Produces:
  - `RadioBrowserClient(HttpClient http)`
  - `.GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken ct) : Task<IReadOnlyList<RadioBrowserStation>>`
  - `.SearchByTagAsync(string tag, int limit, CancellationToken ct) : Task<IReadOnlyList<RadioBrowserStation>>`
  - `RadioBrowserClient.BaseAddress : string`
  - `FakeHttpMessageHandler` with `.Respond(...)`, `.Fail(...)`, `.Requests`

- [ ] **Step 1: Write the fake handler**

`tests/NoMercy.Plugin.InternetRadio.Tests/TestSupport/FakeHttpMessageHandler.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Net;

namespace NoMercy.Plugin.InternetRadio.Tests.TestSupport;

// Every network failure this plugin has to survive, without a socket. The real
// HttpClient the host hands a plugin is wrapped in an allowlist handler that throws
// for a host the manifest never declared, so tests that reached the internet would
// be testing something the server does not do anyway.
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private Func<HttpRequestMessage, HttpResponseMessage>? _responder;

    public List<HttpRequestMessage> Requests { get; } = [];

    public void Respond(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        _responder = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    public void RespondPerRequest(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    public void Fail(Exception exception) => _responder = _ => throw exception;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(request);

        if (_responder is null)
        {
            throw new InvalidOperationException("the test did not arrange a response");
        }

        return Task.FromResult(_responder(request));
    }
}
```

- [ ] **Step 2: Write the failing client tests**

`tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/RadioBrowserClientTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Net;
using System.Text.Json;
using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public class RadioBrowserClientTests
{
    private const string OneStation = """
        [{
          "stationuuid": "960cf833-0601-11e8-ae97-52543be04c81",
          "name": "Example FM",
          "url": "https://example.com/a",
          "url_resolved": "https://cdn.example.com/a",
          "homepage": "https://example.com",
          "favicon": "https://example.com/logo.png",
          "tags": "ambient,chillout",
          "countrycode": "NL",
          "language": "english",
          "codec": "MP3",
          "bitrate": 128,
          "hls": 0,
          "lastcheckok": 1,
          "votes": 42
        }]
        """;

    private static (RadioBrowserClient Client, FakeHttpMessageHandler Handler) Build()
    {
        FakeHttpMessageHandler handler = new();
        HttpClient http = new(handler);
        return (new RadioBrowserClient(http), handler);
    }

    [Fact]
    public async Task GetByUuidsAsync_ReadsEveryFieldTheViewsNeed()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond(OneStation);

        IReadOnlyList<RadioBrowserStation> stations =
            await client.GetByUuidsAsync(["960cf833-0601-11e8-ae97-52543be04c81"], CancellationToken.None);

        RadioBrowserStation station = stations.Should().ContainSingle().Subject;
        station.Name.Should().Be("Example FM");
        station.UrlResolved.Should().Be("https://cdn.example.com/a");
        station.Favicon.Should().Be("https://example.com/logo.png");
        station.Tags.Should().Be("ambient,chillout");
        station.CountryCode.Should().Be("NL");
        station.Codec.Should().Be("MP3");
        station.Bitrate.Should().Be(128);
        station.LastCheckOk.Should().Be(1);
        station.Votes.Should().Be(42);
    }

    // One POST for all ten seeds rather than ten GETs. Verified against the live API
    // before this was designed: the endpoint takes a comma-separated uuids field.
    [Fact]
    public async Task GetByUuidsAsync_AsksForEverySeedInOneRequest()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond(OneStation);

        await client.GetByUuidsAsync(["aaa", "bbb", "ccc"], CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        HttpRequestMessage request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.ToString().Should().EndWith("/json/stations/byuuid");
        (await request.Content!.ReadAsStringAsync()).Should().Contain("aaa,bbb,ccc");
    }

    [Fact]
    public async Task GetByUuidsAsync_MakesNoRequestForAnEmptySeedList()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();

        IReadOnlyList<RadioBrowserStation> stations =
            await client.GetByUuidsAsync([], CancellationToken.None);

        stations.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchByTagAsync_QueriesTheTagExactlyAndLimitsIt()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond(OneStation);

        await client.SearchByTagAsync("drum and bass", 5, CancellationToken.None);

        string url = handler.Requests.Should().ContainSingle().Subject.RequestUri!.ToString();
        url.Should().Contain("/json/stations/search");
        // Exact matching, or "rock" also returns every station tagged "rockabilly".
        url.Should().Contain("tagExact=true");
        url.Should().Contain("tag=drum%20and%20bass");
        url.Should().Contain("limit=5");
        // Cheap server-side pre-filtering. The gates still run: this narrows the
        // response, it does not decide admission.
        url.Should().Contain("hidebroken=true");
        url.Should().Contain("is_https=true");
        url.Should().Contain("order=votes");
    }

    // radio-browser asks callers to identify themselves. Set per request rather than
    // on DefaultRequestHeaders: the HttpClient belongs to the host and is shared, so
    // mutating it would leak this plugin's identity onto another plugin's traffic.
    [Fact]
    public async Task Requests_IdentifyThePlugin()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond(OneStation);

        await client.SearchByTagAsync("ambient", 5, CancellationToken.None);

        handler.Requests[0].Headers.UserAgent.ToString().Should().Contain("NoMercy.Plugin.InternetRadio");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Throws_WhenTheApiReturnsAnError(HttpStatusCode status)
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond("nope", status);

        await FluentActions
            .Awaiting(() => client.SearchByTagAsync("ambient", 5, CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Throws_WhenTheTransportFails()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Fail(new HttpRequestException("dns is having a day"));

        await FluentActions
            .Awaiting(() => client.SearchByTagAsync("ambient", 5, CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Throws_WhenTheBodyIsNotJson()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond("<html>a captive portal, probably</html>");

        await FluentActions
            .Awaiting(() => client.SearchByTagAsync("ambient", 5, CancellationToken.None))
            .Should().ThrowAsync<JsonException>();
    }

    // An empty result is an answer, not a failure. A tag nobody uses returns [], and
    // that must not be treated the same way as the API being down - one means "no
    // stations here", the other means "do not throw the cache away".
    [Fact]
    public async Task ReturnsEmpty_WhenTheApiReturnsNoStations()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond("[]");

        IReadOnlyList<RadioBrowserStation> stations =
            await client.SearchByTagAsync("ambient", 5, CancellationToken.None);

        stations.Should().BeEmpty();
    }

    [Fact]
    public async Task ReturnsEmpty_WhenTheApiReturnsJsonNull()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond("null");

        IReadOnlyList<RadioBrowserStation> stations =
            await client.SearchByTagAsync("ambient", 5, CancellationToken.None);

        stations.Should().BeEmpty();
    }

    // A record missing fields it usually sends must still parse: every optional
    // property on the DTO is nullable or defaulted precisely so one sparse row does
    // not cost the whole response.
    [Fact]
    public async Task ParsesARecordMissingItsOptionalFields()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond("""[{"stationuuid":"a","name":"Bare FM"}]""");

        RadioBrowserStation station =
            (await client.SearchByTagAsync("ambient", 5, CancellationToken.None)).Should().ContainSingle().Subject;

        station.Name.Should().Be("Bare FM");
        station.Url.Should().BeNull();
        station.Bitrate.Should().Be(0);
    }

    [Fact]
    public async Task PropagatesCancellation()
    {
        (RadioBrowserClient client, FakeHttpMessageHandler handler) = Build();
        handler.Respond(OneStation);
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await FluentActions
            .Awaiting(() => client.SearchByTagAsync("ambient", 5, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test -c Release --filter FullyQualifiedName~RadioBrowserClientTests
```

Expected: FAIL — `RadioBrowserClient` does not exist.

- [ ] **Step 4: Write `RadioBrowserClient.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Net.Http.Json;
using System.Text.Json;

namespace NoMercy.Plugin.InternetRadio;

// The only thing in this plugin that reaches the network.
//
// Deliberately thin: two calls, no retry, no caching, no swallowing. It throws what
// went wrong and CatalogProvider decides what that means, because "the API is down"
// and "this tag has no stations" need opposite responses and only the caller knows
// whether it has a cache to fall back on.
//
// The HttpClient comes from IPluginContext and is bounded by the manifest's declared
// hosts, so a request to anywhere but *.api.radio-browser.info throws
// PluginNetworkDeniedException before it leaves the process. That is the enforcement
// point; this class does not re-implement it.
public sealed class RadioBrowserClient(HttpClient http)
{
    // 'all' is radio-browser's round-robin across its mirrors, which is what they ask
    // clients to use rather than pinning one. The manifest's *.api.radio-browser.info
    // covers it and every mirror it can hand back.
    public const string BaseAddress = "https://all.api.radio-browser.info";

    // radio-browser asks callers to identify themselves so they can contact whoever
    // is hammering them.
    private const string UserAgent =
        "NoMercy.Plugin.InternetRadio/1.0.2 (+https://forgejo.phillippepelzer.me/FiLL/nomercy-radiostation-plugin)";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// The pinned seeds, in one request. A POST because the uuid list is a body
    /// field, and ten GETs would be ten round trips for one screen.
    /// </summary>
    public async Task<IReadOnlyList<RadioBrowserStation>> GetByUuidsAsync(
        IReadOnlyList<string> uuids,
        CancellationToken ct
    )
    {
        if (uuids.Count == 0)
        {
            return [];
        }

        using HttpRequestMessage request = Request(HttpMethod.Post, "/json/stations/byuuid");
        request.Content = new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("uuids", string.Join(',', uuids))]
        );

        return await SendAsync(request, ct);
    }

    /// <summary>
    /// One genre's stations, most-voted first.
    /// <para>
    /// <c>tagExact</c> is on because substring matching puts every "rockabilly"
    /// station in Rock. <c>hidebroken</c> and <c>is_https</c> are server-side
    /// pre-filtering that keeps the response small; they do not decide admission —
    /// <see cref="StationGates"/> still runs over everything that comes back, and it
    /// has to, because radio-browser has been observed reporting a 404 stream as
    /// healthy.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<RadioBrowserStation>> SearchByTagAsync(
        string tag,
        int limit,
        CancellationToken ct
    )
    {
        string query = string.Join(
            '&',
            $"tag={Uri.EscapeDataString(tag)}",
            "tagExact=true",
            $"limit={limit.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            "order=votes",
            "reverse=true",
            "hidebroken=true",
            "is_https=true"
        );

        using HttpRequestMessage request = Request(HttpMethod.Get, $"/json/stations/search?{query}");
        return await SendAsync(request, ct);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path)
    {
        HttpRequestMessage request = new(method, $"{BaseAddress}{path}");

        // Per request, not on DefaultRequestHeaders: the HttpClient belongs to the
        // host and is shared, so mutating it would put this plugin's identity on
        // another plugin's traffic.
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        return request;
    }

    private async Task<IReadOnlyList<RadioBrowserStation>> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        using HttpResponseMessage response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        List<RadioBrowserStation>? stations =
            await response.Content.ReadFromJsonAsync<List<RadioBrowserStation>>(JsonOptions, ct);

        // A JSON `null` body parses to null rather than throwing. Empty is the honest
        // reading of it, and it keeps every caller off a null check.
        return stations ?? [];
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet build -c Release -p:TreatWarningsAsErrors=true
dotnet test -c Release --no-build --filter FullyQualifiedName~RadioBrowserClientTests
```

Expected: PASS.

- [ ] **Step 6: Run the full suite and commit**

```bash
dotnet test -c Release --no-build
git add -A
git commit -m "feat(catalog): add the radio-browser client"
```

---

### Task 6: The catalogue model and its on-disk cache

`StationCatalog` is what every view receives. `CatalogCache` is the only thing that touches the data folder.

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/CatalogSource.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/StationCatalog.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/CatalogCache.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/StationCatalogTests.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/CatalogCacheTests.cs`

**Interfaces:**
- Consumes: `RadioStation`, `GenreMap`, `StationGates.Slugify`.
- Produces:
  - `CatalogSource` enum — `Unavailable`, `Fetched`, `Cache`, `UserOverride`.
  - `GenreSummary(GenreSection Section, int Count)`.
  - `StationCatalog` — `.Stations`, `.Source`, `.FetchedAt` (`DateTimeOffset?`), `.LastFetchFailed` (`bool`), `.Count`, `.IsEmpty`, `.ById(string)`, `.ByGenreSlug(string)`, `.Genres`, `.Popular(int)`, `.Empty(bool lastFetchFailed = false)`, `.Create(IEnumerable<RadioStation>, CatalogSource, DateTimeOffset?)`.
  - `CatalogCache(string dataFolderPath)` — `.ReadAsync(CancellationToken)`, `.WriteAsync(IReadOnlyList<RadioStation>, DateTimeOffset, CancellationToken)`, `.FileName` const.
  - `CachedCatalog` — `.FetchedAt`, `.Stations`.

- [ ] **Step 1: Write the failing tests**

`tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/StationCatalogTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public class StationCatalogTests
{
    private static RadioStation Station(string id, string genre, int popularity = 0) =>
        new()
        {
            Id = id,
            Name = $"Station {id}",
            StreamUrl = $"https://example.com/{id}",
            Genre = genre,
            Popularity = popularity,
        };

    [Fact]
    public void ById_FindsAStationAndIsCaseInsensitive()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("AbC", "Ambient")], CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.ById("abc").Should().NotBeNull();
        catalog.ById("nope").Should().BeNull();
    }

    [Fact]
    public void ByGenreSlug_ReturnsOnlyThatGenre()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", "Ambient"), Station("b", "Rock"), Station("c", "Ambient")],
            CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.ByGenreSlug("ambient").Select(station => station.Id)
            .Should().BeEquivalentTo(new[] { "a", "c" });
    }

    [Fact]
    public void ByGenreSlug_ReturnsEmptyForAnUnknownSlug()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", "Ambient")], CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.ByGenreSlug("no-such-genre").Should().BeEmpty();
    }

    // Only genres that actually have stations, so the browse page never offers a chip
    // that leads to an empty page.
    [Fact]
    public void Genres_ListOnlyNonEmptySectionsWithTheirCounts()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", "Ambient"), Station("b", "Ambient"), Station("c", "Rock")],
            CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.Genres.Should().HaveCount(2);
        catalog.Genres.Single(genre => genre.Section.Label == "Ambient").Count.Should().Be(2);
        catalog.Genres.Should().NotContain(genre => genre.Section.Label == "Jazz");
    }

    // "Other" is a real destination - three of the four Tomorrowland records carry no
    // tags - so it has to be reachable rather than swallowed.
    [Fact]
    public void Genres_IncludeOtherWhenSomethingLandedThere()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", GenreMap.Other)], CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.Genres.Should().ContainSingle().Which.Section.Label.Should().Be(GenreMap.Other);
        catalog.ByGenreSlug(StationGates.Slugify(GenreMap.Other)).Should().ContainSingle();
    }

    [Fact]
    public void Popular_ReturnsTheMostVotedFirstAndCapsTheCount()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", "Ambient", 10), Station("b", "Rock", 99), Station("c", "Jazz", 50)],
            CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.Popular(2).Select(station => station.Id).Should().Equal("b", "c");
    }

    [Fact]
    public void Popular_ReturnsEverythingWhenAskedForMoreThanItHas()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", "Ambient")], CatalogSource.Fetched, DateTimeOffset.UnixEpoch);

        catalog.Popular(50).Should().HaveCount(1);
    }

    [Fact]
    public void Empty_IsUnavailableAndRemembersWhetherAFetchFailed()
    {
        StationCatalog.Empty().Source.Should().Be(CatalogSource.Unavailable);
        StationCatalog.Empty().IsEmpty.Should().BeTrue();
        StationCatalog.Empty(lastFetchFailed: true).LastFetchFailed.Should().BeTrue();
        StationCatalog.Empty().LastFetchFailed.Should().BeFalse();
    }
}
```

`tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/CatalogCacheTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public sealed class CatalogCacheTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"nm-radio-{Guid.NewGuid():N}");

    public CatalogCacheTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static RadioStation Station(string id = "a") =>
        new() { Id = id, Name = "Example FM", StreamUrl = "https://example.com/a", Genre = "Ambient" };

    [Fact]
    public async Task RoundTripsWhatItWrote()
    {
        CatalogCache cache = new(_folder);
        DateTimeOffset fetchedAt = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

        await cache.WriteAsync([Station()], fetchedAt, CancellationToken.None);
        CachedCatalog? read = await cache.ReadAsync(CancellationToken.None);

        read.Should().NotBeNull();
        read!.FetchedAt.Should().Be(fetchedAt);
        read.Stations.Should().ContainSingle().Which.Name.Should().Be("Example FM");
    }

    [Fact]
    public async Task ReadsNullWhenThereIsNoCacheYet()
    {
        CatalogCache cache = new(_folder);

        (await cache.ReadAsync(CancellationToken.None)).Should().BeNull();
    }

    // A server killed mid-write leaves truncated JSON. That must read as "no cache"
    // and let the plugin re-fetch, not throw out of a view.
    [Fact]
    public async Task ReadsNullWhenTheCacheIsCorrupt()
    {
        CatalogCache cache = new(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, CatalogCache.FileName), "{ truncated");

        (await cache.ReadAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ReadsNullWhenTheCacheIsJsonNull()
    {
        CatalogCache cache = new(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, CatalogCache.FileName), "null");

        (await cache.ReadAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task WriteCreatesTheDataFolderIfTheHostHasNotYet()
    {
        string missing = Path.Combine(_folder, "nested", "deeper");
        CatalogCache cache = new(missing);

        await cache.WriteAsync([Station()], DateTimeOffset.UtcNow, CancellationToken.None);

        File.Exists(Path.Combine(missing, CatalogCache.FileName)).Should().BeTrue();
    }

    // Written to a temp file and moved into place, so a crash mid-write cannot leave
    // a half-written cache where a whole one used to be.
    [Fact]
    public async Task WriteReplacesAPreviousCacheAtomically()
    {
        CatalogCache cache = new(_folder);
        await cache.WriteAsync([Station("first")], DateTimeOffset.UnixEpoch, CancellationToken.None);
        await cache.WriteAsync([Station("second")], DateTimeOffset.UnixEpoch, CancellationToken.None);

        CachedCatalog? read = await cache.ReadAsync(CancellationToken.None);

        read!.Stations.Should().ContainSingle().Which.Id.Should().Be("second");
        Directory.GetFiles(_folder).Should().ContainSingle("no temp file should be left behind");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test -c Release --filter "FullyQualifiedName~StationCatalogTests|FullyQualifiedName~CatalogCacheTests"
```

Expected: FAIL — the types do not exist.

- [ ] **Step 3: Write `CatalogSource.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

// Where the stations on screen came from. Shown on the settings page, because "no
// stations" and "stations from a three-day-old cache" are different problems and the
// owner cannot tell them apart from the browse page.
public enum CatalogSource
{
    /// <summary>Nothing to show: no override, no cache, and no successful fetch.</summary>
    Unavailable,

    /// <summary>Fetched from radio-browser during this run.</summary>
    Fetched,

    /// <summary>Read from the on-disk cache written by an earlier fetch.</summary>
    Cache,

    /// <summary>The user's own stations.json, which replaces everything else.</summary>
    UserOverride,
}
```

- [ ] **Step 4: Write `StationCatalog.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

/// <param name="Section">The genre section.</param>
/// <param name="Count">How many stations are in it. Shown on the settings page.</param>
public sealed record GenreSummary(GenreSection Section, int Count);

// What every view is handed. Immutable, and built once per view request from
// whatever CatalogProvider resolved, so a view cannot accidentally do I/O and cannot
// see a catalogue change underneath it mid-render.
public sealed class StationCatalog
{
    private readonly Dictionary<string, RadioStation> _byId;
    private readonly Dictionary<string, List<RadioStation>> _byGenreSlug;

    private StationCatalog(
        IReadOnlyList<RadioStation> stations,
        CatalogSource source,
        DateTimeOffset? fetchedAt,
        bool lastFetchFailed
    )
    {
        Stations = stations;
        Source = source;
        FetchedAt = fetchedAt;
        LastFetchFailed = lastFetchFailed;

        _byId = new(StringComparer.OrdinalIgnoreCase);
        _byGenreSlug = new(StringComparer.OrdinalIgnoreCase);

        foreach (RadioStation station in stations)
        {
            // First wins. Deduplicate has already run for fetched stations, but a
            // user's stations.json is not gated, so this is where a collision in
            // their file is resolved rather than throwing during a page render.
            _byId.TryAdd(station.Id, station);

            string slug = StationGates.Slugify(station.Genre ?? GenreMap.Other);
            if (!_byGenreSlug.TryGetValue(slug, out List<RadioStation>? bucket))
            {
                bucket = [];
                _byGenreSlug[slug] = bucket;
            }

            bucket.Add(station);
        }
    }

    public IReadOnlyList<RadioStation> Stations { get; }
    public CatalogSource Source { get; }
    public DateTimeOffset? FetchedAt { get; }

    /// <summary>True when the most recent fetch attempt failed, whatever is on screen.</summary>
    public bool LastFetchFailed { get; }

    public int Count => Stations.Count;
    public bool IsEmpty => Stations.Count == 0;

    public static StationCatalog Create(
        IEnumerable<RadioStation> stations,
        CatalogSource source,
        DateTimeOffset? fetchedAt
    ) => new([.. stations], source, fetchedAt, lastFetchFailed: false);

    public static StationCatalog Empty(bool lastFetchFailed = false) =>
        new([], CatalogSource.Unavailable, fetchedAt: null, lastFetchFailed);

    public RadioStation? ById(string id) =>
        _byId.TryGetValue(id, out RadioStation? station) ? station : null;

    public IReadOnlyList<RadioStation> ByGenreSlug(string slug) =>
        _byGenreSlug.TryGetValue(slug, out List<RadioStation>? bucket) ? bucket : [];

    /// <summary>
    /// Only sections that have stations, in <see cref="GenreMap"/> order, with
    /// "Other" last. A chip leading to an empty page is worse than no chip.
    /// </summary>
    public IReadOnlyList<GenreSummary> Genres =>
        [
            .. GenreMap.Sections
                .Select(section => new GenreSummary(section, ByGenreSlug(section.Slug).Count))
                .Where(summary => summary.Count > 0),
            .. OtherSummary(),
        ];

    private IEnumerable<GenreSummary> OtherSummary()
    {
        string slug = StationGates.Slugify(GenreMap.Other);
        int count = ByGenreSlug(slug).Count;

        if (count > 0)
        {
            yield return new GenreSummary(new GenreSection(GenreMap.Other, GenreMap.Other, slug), count);
        }
    }

    /// <summary>Most-voted first. Ordering only — the number is never shown.</summary>
    public IReadOnlyList<RadioStation> Popular(int count) =>
        [.. Stations.OrderByDescending(station => station.Popularity).Take(count)];
}
```

- [ ] **Step 5: Write `CatalogCache.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>What a previous fetch wrote to disk.</summary>
public sealed record CachedCatalog
{
    [JsonPropertyName("fetchedAt")]
    public required DateTimeOffset FetchedAt { get; init; }

    [JsonPropertyName("stations")]
    public required List<RadioStation> Stations { get; init; }
}

// The only thing in this plugin that touches the data folder.
//
// Every read failure is null, never an exception: the cache is a convenience, and a
// truncated file - which is what a server killed mid-write leaves - has to mean
// "fetch again", not "the settings page throws".
public sealed class CatalogCache(string dataFolderPath)
{
    public const string FileName = "catalog-cache.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    private string Path => System.IO.Path.Combine(dataFolderPath, FileName);

    public async Task<CachedCatalog?> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        try
        {
            await using FileStream stream = File.OpenRead(Path);
            return await JsonSerializer.DeserializeAsync<CachedCatalog>(stream, JsonOptions, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Corrupt, truncated, or unreadable. Indistinguishable from absent as far
            // as the caller is concerned, and treating it that way is what makes the
            // next refresh fix it. The caller logs; this stays quiet so a cache miss
            // does not need an ILogger threaded into it.
            return null;
        }
    }

    public async Task WriteAsync(
        IReadOnlyList<RadioStation> stations,
        DateTimeOffset fetchedAt,
        CancellationToken ct
    )
    {
        Directory.CreateDirectory(dataFolderPath);

        // Written beside the target and moved into place. A crash partway through a
        // direct write would replace a whole cache with half of one, and the next
        // read would discard it - losing a good catalogue to a bad write.
        string temporary = $"{Path}.tmp";

        await using (FileStream stream = File.Create(temporary))
        {
            CachedCatalog payload = new() { FetchedAt = fetchedAt, Stations = [.. stations] };
            await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, ct);
        }

        File.Move(temporary, Path, overwrite: true);
    }
}
```

- [ ] **Step 6: Run the tests, then the full suite, then commit**

```bash
dotnet build -c Release -p:TreatWarningsAsErrors=true
dotnet test -c Release --no-build
git add -A
git commit -m "feat(catalog): add the catalogue model and its on-disk cache"
```

Expected: all pass.

---

### Task 7: The catalogue provider — override wins, cache first, fetch on empty

The decision layer, and the only place that knows a stale catalogue beats an empty one. Everything here is tested against the fake handler and a temp folder; nothing reaches the network.

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/StationOverrides.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Catalog/CatalogProvider.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/TestSupport/RecordingLogger.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/CatalogProviderTests.cs`
- Modify: `src/NoMercy.Plugin.InternetRadio/Catalog/StationCatalog.cs` (adds `WithFailedFetch`, Step 6)

**Interfaces:**
- Consumes: `RadioBrowserClient`, `CatalogCache`, `StationGates`, `GenreMap`, `SeedStations`, `StationCatalog`.
- Produces:
  - `StationOverrides.TryLoad(string dataFolderPath, ILogger logger) : IReadOnlyList<RadioStation>?`, `StationOverrides.FileName` const.
  - `CatalogProvider(RadioBrowserClient client, CatalogCache cache, string dataFolderPath, ILogger logger, TimeSpan? cacheTtl = null)`
  - `.GetAsync(CancellationToken) : Task<StationCatalog>`
  - `.RefreshAsync(CancellationToken) : Task<StationCatalog>`
  - `CatalogProvider.DefaultCacheTtl : TimeSpan`
  - `RecordingLogger` implementing `ILogger`, with `.Entries`.

- [ ] **Step 1: Write `RecordingLogger`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using Microsoft.Extensions.Logging;

namespace NoMercy.Plugin.InternetRadio.Tests.TestSupport;

// Lets a test assert that a failure was reported without asserting on wording.
public sealed class RecordingLogger : ILogger
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) => Entries.Add((logLevel, formatter(state, exception), exception));
}
```

- [ ] **Step 2: Write the failing provider tests**

`tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/CatalogProviderTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public sealed class CatalogProviderTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"nm-radio-{Guid.NewGuid():N}");
    private readonly FakeHttpMessageHandler _handler = new();
    private readonly RecordingLogger _logger = new();

    public CatalogProviderTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private CatalogProvider Provider(TimeSpan? ttl = null) =>
        new(new RadioBrowserClient(new HttpClient(_handler)),
            new CatalogCache(_folder),
            _folder,
            _logger,
            ttl);

    private static string Payload(string uuid, string name, string url, string tags = "ambient") =>
        $$"""
        [{"stationuuid":"{{uuid}}","name":"{{name}}","url":"{{url}}","url_resolved":"{{url}}",
          "tags":"{{tags}}","countrycode":"NL","codec":"MP3","bitrate":128,
          "hls":0,"lastcheckok":1,"votes":5}]
        """;

    [Fact]
    public async Task Fetches_WhenThereIsNoCache()
    {
        _handler.Respond(Payload("u1", "Example FM", "https://example.com/a"));

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Fetched);
        catalog.Stations.Should().NotBeEmpty();
        catalog.Stations[0].Genre.Should().Be("Ambient");
    }

    [Fact]
    public async Task WritesTheCacheAfterAFetch()
    {
        _handler.Respond(Payload("u1", "Example FM", "https://example.com/a"));

        await Provider().GetAsync(CancellationToken.None);

        File.Exists(Path.Combine(_folder, CatalogCache.FileName)).Should().BeTrue();
    }

    // A view is rendered on every navigation. Hitting the API per click would be
    // roughly eighteen requests a page.
    [Fact]
    public async Task ServesAFreshCacheWithoutTouchingTheNetwork()
    {
        _handler.Respond(Payload("u1", "Example FM", "https://example.com/a"));
        CatalogProvider provider = Provider();
        await provider.GetAsync(CancellationToken.None);
        int afterFirst = _handler.Requests.Count;

        StationCatalog catalog = await provider.GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Cache);
        _handler.Requests.Should().HaveCount(afterFirst);
    }

    [Fact]
    public async Task RefetchesWhenTheCacheIsOlderThanTheTtl()
    {
        _handler.Respond(Payload("u1", "Example FM", "https://example.com/a"));
        await new CatalogCache(_folder).WriteAsync(
            [new RadioStation { Id = "old", Name = "Old FM", StreamUrl = "https://example.com/old" }],
            DateTimeOffset.UtcNow - TimeSpan.FromDays(30),
            CancellationToken.None);

        StationCatalog catalog = await Provider(TimeSpan.FromHours(1)).GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Fetched);
    }

    // The one that matters when radio-browser is down. A working catalogue must not
    // be thrown away because a refresh failed.
    [Fact]
    public async Task ServesAStaleCacheWhenTheFetchFails()
    {
        await new CatalogCache(_folder).WriteAsync(
            [new RadioStation { Id = "old", Name = "Old FM", StreamUrl = "https://example.com/old" }],
            DateTimeOffset.UtcNow - TimeSpan.FromDays(30),
            CancellationToken.None);
        _handler.Fail(new HttpRequestException("down"));

        StationCatalog catalog = await Provider(TimeSpan.FromHours(1)).GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Cache);
        catalog.Stations.Should().ContainSingle().Which.Id.Should().Be("old");
        catalog.LastFetchFailed.Should().BeTrue();
    }

    [Fact]
    public async Task ReturnsEmptyAndFlagsTheFailureWhenThereIsNoCacheAndNoNetwork()
    {
        _handler.Fail(new HttpRequestException("down"));

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.IsEmpty.Should().BeTrue();
        catalog.Source.Should().Be(CatalogSource.Unavailable);
        catalog.LastFetchFailed.Should().BeTrue();
        _logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning);
    }

    // One genre failing must not lose the other sixteen and the seeds with them.
    [Fact]
    public async Task KeepsWhatSucceededWhenOneGenreQueryFails()
    {
        int call = 0;
        _handler.RespondPerRequest(_ =>
        {
            call++;
            return call == 2
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    { Content = new StringContent("boom") }
                : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            Payload($"u{call}", $"Station {call}", $"https://example.com/{call}"),
                            System.Text.Encoding.UTF8,
                            "application/json"),
                    };
        });

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Fetched);
        catalog.Stations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DropsStationsThatFailTheGates()
    {
        // http, so it would be blocked as mixed content in the browser.
        _handler.Respond(Payload("u1", "Insecure FM", "http://example.com/a"));

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Stations.Should().NotContain(station => station.Name == "Insecure FM");
    }

    [Fact]
    public async Task UsesTheUserOverrideInsteadOfTheNetwork()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_folder, StationOverrides.FileName),
            """[{"name":"My Station","streamUrl":"https://mine.example/stream"}]""");

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.UserOverride);
        catalog.Stations.Should().ContainSingle().Which.Name.Should().Be("My Station");
        _handler.Requests.Should().BeEmpty();
    }

    // Their file, their call. Gating it would silently delete their entries.
    [Fact]
    public async Task DoesNotGateTheUserOverride()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_folder, StationOverrides.FileName),
            """[{"name":"Plain HTTP","streamUrl":"http://mine.example/stream"}]""");

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Stations.Should().ContainSingle().Which.Name.Should().Be("Plain HTTP");
    }

    [Fact]
    public async Task GivesAnOverrideStationARoutableIdAndMarksItUserSupplied()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_folder, StationOverrides.FileName),
            """[{"name":"My Station!","streamUrl":"https://mine.example/stream"}]""");

        RadioStation station = (await Provider().GetAsync(CancellationToken.None)).Stations.Single();

        station.Id.Should().Be("my-station");
        station.IsUserSupplied.Should().BeTrue();
    }

    [Fact]
    public async Task FallsBackToTheNetworkWhenTheOverrideIsUnparseable()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, StationOverrides.FileName), "{ not an array");
        _handler.Respond(Payload("u1", "Example FM", "https://example.com/a"));

        StationCatalog catalog = await Provider().GetAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Fetched);
        _logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task RefreshAsyncAlwaysFetchesEvenWithAFreshCache()
    {
        _handler.Respond(Payload("u1", "Example FM", "https://example.com/a"));
        CatalogProvider provider = Provider();
        await provider.GetAsync(CancellationToken.None);
        int afterFirst = _handler.Requests.Count;

        StationCatalog catalog = await provider.RefreshAsync(CancellationToken.None);

        catalog.Source.Should().Be(CatalogSource.Fetched);
        _handler.Requests.Count.Should().BeGreaterThan(afterFirst);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test -c Release --filter FullyQualifiedName~CatalogProviderTests
```

Expected: FAIL — `StationOverrides` and `CatalogProvider` do not exist.

- [ ] **Step 4: Write `StationOverrides.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NoMercy.Plugin.InternetRadio;

// The user's own station list, which replaces the fetched catalogue outright.
//
// Kept compatible with the bare JSON array the previous README documented, so an
// existing stations.json keeps working across this rewrite.
//
// Deliberately NOT put through StationGates. A hand-written list is the owner's
// call, and silently dropping their http entry would be worse than letting it fail
// visibly in the player - at least then they can see which one it was. This is also
// the escape hatch for anything radio-browser cannot supply, BBC included.
public static class StationOverrides
{
    public const string FileName = "stations.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

    /// <summary>
    /// The override list, or null when there is no usable one — in which case the
    /// caller fetches as normal.
    /// </summary>
    public static IReadOnlyList<RadioStation>? TryLoad(string dataFolderPath, ILogger logger)
    {
        string path = Path.Combine(dataFolderPath, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            List<RadioStation>? parsed =
                JsonSerializer.Deserialize<List<RadioStation>>(File.ReadAllText(path), JsonOptions);

            if (parsed is null)
            {
                return null;
            }

            List<RadioStation> valid =
            [
                .. parsed
                    .Where(station =>
                        !string.IsNullOrWhiteSpace(station.Name)
                        && !string.IsNullOrWhiteSpace(station.StreamUrl))
                    .Select(station => station with
                    {
                        // Their file need not carry an id, and a name is the only
                        // stable thing it is guaranteed to have.
                        Id = string.IsNullOrWhiteSpace(station.Id)
                            ? StationGates.Slugify(station.Name)
                            : station.Id,
                        Genre = string.IsNullOrWhiteSpace(station.Genre) ? GenreMap.Other : station.Genre,
                        IsUserSupplied = true,
                    }),
            ];

            return valid.Count > 0 ? valid : null;
        }
        catch (Exception exception)
        {
            // Named so the owner can find their typo, without echoing the file's
            // contents into the server log.
            logger.LogWarning(
                exception,
                "Internet Radio could not read {FileName}; using the fetched catalogue instead.",
                FileName
            );
            return null;
        }
    }
}
```

- [ ] **Step 5: Write `CatalogProvider.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using Microsoft.Extensions.Logging;

namespace NoMercy.Plugin.InternetRadio;

// Decides what the views actually see.
//
// The order is override, then fresh cache, then fetch, then stale cache, then empty.
// The last two are the point: a third party's outage must not empty a catalogue that
// was working a minute ago, so a stale cache is preferred to nothing and the failure
// is surfaced on the settings page rather than as a blank browse grid.
public sealed class CatalogProvider(
    RadioBrowserClient client,
    CatalogCache cache,
    string dataFolderPath,
    ILogger logger,
    TimeSpan? cacheTtl = null
)
{
    /// <summary>
    /// How long a cache is served without re-fetching. Longer than the refresh job's
    /// daily cadence, so the job is what normally refreshes and a view only fetches
    /// when the job has not run yet or has been failing.
    /// </summary>
    public static TimeSpan DefaultCacheTtl { get; } = TimeSpan.FromHours(36);

    private TimeSpan Ttl => cacheTtl ?? DefaultCacheTtl;

    public async Task<StationCatalog> GetAsync(CancellationToken ct)
    {
        if (StationOverrides.TryLoad(dataFolderPath, logger) is { } overrides)
        {
            return StationCatalog.Create(overrides, CatalogSource.UserOverride, fetchedAt: null);
        }

        CachedCatalog? cached = await cache.ReadAsync(ct);

        if (cached is not null && DateTimeOffset.UtcNow - cached.FetchedAt < Ttl)
        {
            return StationCatalog.Create(cached.Stations, CatalogSource.Cache, cached.FetchedAt);
        }

        return await FetchAsync(cached, ct);
    }

    /// <summary>
    /// Fetches whatever the cache says. This is what the scheduled job calls, and
    /// what the settings page's Refresh reaches once the cache has aged out.
    /// </summary>
    public async Task<StationCatalog> RefreshAsync(CancellationToken ct) =>
        await FetchAsync(await cache.ReadAsync(ct), ct);

    private async Task<StationCatalog> FetchAsync(CachedCatalog? fallback, CancellationToken ct)
    {
        List<RadioStation> collected = [];
        bool anythingFailed = false;

        // Seeds first, so a curated station wins the dedupe against the same station
        // rediscovered by the genre sweep.
        try
        {
            collected.AddRange(Convert(await client.GetByUuidsAsync(SeedStations.Uuids, ct)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            anythingFailed = true;
            logger.LogWarning(exception, "Internet Radio could not fetch its pinned stations.");
        }

        foreach (GenreSection section in GenreMap.Sections)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                collected.AddRange(
                    Convert(await client.SearchByTagAsync(section.Tag, SeedStations.PerGenreLimit, ct)));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // One genre failing costs one genre. Letting it abort the sweep would
                // throw away the seeds and sixteen other sections with it.
                anythingFailed = true;
                logger.LogWarning(
                    exception, "Internet Radio could not fetch the {Genre} stations.", section.Label);
            }
        }

        IReadOnlyList<RadioStation> stations = StationGates.Deduplicate(collected);

        if (stations.Count > 0)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            try
            {
                await cache.WriteAsync(stations, now, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A read-only or full data folder costs the cache, not the screen.
                logger.LogWarning(exception, "Internet Radio could not write its catalogue cache.");
            }

            return StationCatalog.Create(stations, CatalogSource.Fetched, now);
        }

        // Nothing came back. Anything already on disk is better than an empty grid,
        // however old it is.
        if (fallback is not null && fallback.Stations.Count > 0)
        {
            logger.LogWarning(
                "Internet Radio kept its cached catalogue from {FetchedAt} because the refresh returned nothing.",
                fallback.FetchedAt);

            return StationCatalog.Create(fallback.Stations, CatalogSource.Cache, fallback.FetchedAt)
                .WithFailedFetch();
        }

        logger.LogWarning("Internet Radio has no stations: the refresh failed and there is no cache.");
        return StationCatalog.Empty(lastFetchFailed: anythingFailed);
    }

    private static IEnumerable<RadioStation> Convert(IEnumerable<RadioBrowserStation> wire) =>
        wire.Where(StationGates.Admits).Select(station => new RadioStation
        {
            Id = station.StationUuid,
            Name = station.Name.Trim(),
            StreamUrl = StationGates.EffectiveUrl(station),
            LogoUrl = string.IsNullOrWhiteSpace(station.Favicon) ? null : station.Favicon,
            Homepage = string.IsNullOrWhiteSpace(station.Homepage) ? null : station.Homepage,
            Genre = GenreMap.Resolve(station.Tags),
            Country = string.IsNullOrWhiteSpace(station.CountryCode) ? null : station.CountryCode,
            Language = string.IsNullOrWhiteSpace(station.Language) ? null : station.Language,
            // radio-browser reports 0 for "unknown", which is not the same as a
            // zero-bitrate stream and must not render as "0 kbps".
            BitrateKbps = station.Bitrate > 0 ? station.Bitrate : null,
            Codec = string.IsNullOrWhiteSpace(station.Codec) ? null : station.Codec,
            Popularity = station.Votes,
        });
}
```

- [ ] **Step 6: Add `WithFailedFetch` to `StationCatalog`**

The provider needs to serve a cache while recording that the refresh behind it failed, so the settings page can say so. Add to `StationCatalog`:

```csharp
    /// <summary>
    /// The same catalogue, marked as having survived a failed refresh. Lets the
    /// settings page distinguish "cached because it is fresh" from "cached because
    /// the network is down".
    /// </summary>
    public StationCatalog WithFailedFetch() =>
        new([.. Stations], Source, FetchedAt, lastFetchFailed: true);
```

- [ ] **Step 7: Run the tests, full suite, and commit**

```bash
dotnet build -c Release -p:TreatWarningsAsErrors=true
dotnet test -c Release --no-build
git add -A
git commit -m "feat(catalog): resolve the catalogue with override, cache and fetch precedence"
```

Expected: all pass.

---

### Task 8: Routes

Every route is a path, never a query string: the web host derives `route` from `pathMatch` and sends only that, so `/station?id=x` arrives as `/station` with `x` gone. One file parses and builds them, so the view that links and the dispatcher that resolves cannot drift.

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/Views/RadioRoutes.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/Views/RadioRoutesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `RadioRouteKind` enum — `Browse`, `Genre`, `AllStations`, `Station`, `Settings`, `Unknown`.
  - `RadioRoute(RadioRouteKind Kind, string Value)`.
  - `RadioRoutes.Parse(string? route) : RadioRoute`
  - `RadioRoutes.Browse`, `.AllStations`, `.Settings` — `const string`
  - `RadioRoutes.Genre(string slug) : string`, `.Station(string id) : string`

- [ ] **Step 1: Write the failing tests**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class RadioRoutesTests
{
    [Theory]
    [InlineData("/", RadioRouteKind.Browse)]
    [InlineData("", RadioRouteKind.Browse)]
    [InlineData(null, RadioRouteKind.Browse)]
    [InlineData("/all", RadioRouteKind.AllStations)]
    [InlineData("/settings", RadioRouteKind.Settings)]
    public void Parse_ResolvesTheFixedRoutes(string? route, RadioRouteKind expected)
    {
        RadioRoutes.Parse(route).Kind.Should().Be(expected);
    }

    [Fact]
    public void Parse_ReadsTheGenreSlugFromThePath()
    {
        RadioRoute parsed = RadioRoutes.Parse("/genre/drum-bass");

        parsed.Kind.Should().Be(RadioRouteKind.Genre);
        parsed.Value.Should().Be("drum-bass");
    }

    [Fact]
    public void Parse_ReadsTheStationIdFromThePath()
    {
        RadioRoute parsed = RadioRoutes.Parse("/station/960cf833-0601-11e8-ae97-52543be04c81");

        parsed.Kind.Should().Be(RadioRouteKind.Station);
        parsed.Value.Should().Be("960cf833-0601-11e8-ae97-52543be04c81");
    }

    [Theory]
    [InlineData("/all/")]
    [InlineData("/settings/")]
    [InlineData("//")]
    public void Parse_ToleratesATrailingSlash(string route)
    {
        RadioRoutes.Parse(route).Kind.Should().NotBe(RadioRouteKind.Unknown);
    }

    [Fact]
    public void Parse_IsCaseInsensitiveOnTheSegmentNames()
    {
        RadioRoutes.Parse("/Settings").Kind.Should().Be(RadioRouteKind.Settings);
        RadioRoutes.Parse("/GENRE/rock").Kind.Should().Be(RadioRouteKind.Genre);
    }

    [Theory]
    [InlineData("/nope")]
    [InlineData("/genre")]          // no slug
    [InlineData("/station")]        // no id
    [InlineData("/genre//")]        // empty slug
    [InlineData("/station/a/b")]    // an id cannot contain a slash
    public void Parse_ReportsAnythingElseAsUnknown(string route)
    {
        RadioRoutes.Parse(route).Kind.Should().Be(RadioRouteKind.Unknown);
    }

    // The builders and the parser are two halves of the same agreement, and a link
    // that does not parse is a dead end the user finds before any test does.
    [Fact]
    public void Builders_ProduceRoutesTheParserResolves()
    {
        RadioRoutes.Parse(RadioRoutes.Genre("ambient")).Should()
            .Be(new RadioRoute(RadioRouteKind.Genre, "ambient"));
        RadioRoutes.Parse(RadioRoutes.Station("abc")).Should()
            .Be(new RadioRoute(RadioRouteKind.Station, "abc"));
        RadioRoutes.Parse(RadioRoutes.Browse).Kind.Should().Be(RadioRouteKind.Browse);
        RadioRoutes.Parse(RadioRoutes.AllStations).Kind.Should().Be(RadioRouteKind.AllStations);
        RadioRoutes.Parse(RadioRoutes.Settings).Kind.Should().Be(RadioRouteKind.Settings);
    }

    // A user-supplied station id is a slug of their station name, so it can contain
    // anything they typed. It has to survive the round trip through a URL path.
    [Fact]
    public void Station_EscapesAnIdSoItSurvivesThePath()
    {
        string route = RadioRoutes.Station("a b/c");

        RadioRoutes.Parse(route).Value.Should().Be("a b/c");
    }
}
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test -c Release --filter FullyQualifiedName~RadioRoutesTests
```

Expected: FAIL — `RadioRoutes` does not exist.

- [ ] **Step 3: Write `RadioRoutes.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

public enum RadioRouteKind
{
    Browse,
    Genre,
    AllStations,
    Station,
    Settings,

    /// <summary>Anything else. Rendered as an empty state, never as an error.</summary>
    Unknown,
}

/// <param name="Value">The genre slug or station id, empty for the fixed routes.</param>
public sealed record RadioRoute(RadioRouteKind Kind, string Value);

// The only place a route is parsed or built.
//
// State travels in the PATH and never in a query string. The web host computes the
// route it asks for from its own `pathMatch` parameter and sends that alone, so a
// query string never leaves the browser: "/station?id=x" arrives here as "/station"
// with the id gone, and the page silently renders the wrong thing.
public static class RadioRoutes
{
    public const string Browse = "/";
    public const string AllStations = "/all";
    public const string Settings = "/settings";

    private const string GenrePrefix = "genre";
    private const string StationPrefix = "station";

    public static string Genre(string slug) => $"/{GenrePrefix}/{Uri.EscapeDataString(slug)}";

    public static string Station(string id) => $"/{StationPrefix}/{Uri.EscapeDataString(id)}";

    public static RadioRoute Parse(string? route)
    {
        string[] segments = (route ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
        {
            return new RadioRoute(RadioRouteKind.Browse, string.Empty);
        }

        if (segments.Length == 1)
        {
            return segments[0].ToLowerInvariant() switch
            {
                "all" => new RadioRoute(RadioRouteKind.AllStations, string.Empty),
                "settings" => new RadioRoute(RadioRouteKind.Settings, string.Empty),
                _ => Unknown,
            };
        }

        if (segments.Length == 2)
        {
            string value = Uri.UnescapeDataString(segments[1]);

            return segments[0].ToLowerInvariant() switch
            {
                GenrePrefix => new RadioRoute(RadioRouteKind.Genre, value),
                StationPrefix => new RadioRoute(RadioRouteKind.Station, value),
                _ => Unknown,
            };
        }

        return Unknown;
    }

    private static RadioRoute Unknown => new(RadioRouteKind.Unknown, string.Empty);
}
```

- [ ] **Step 4: Run tests and commit**

```bash
dotnet build -c Release -p:TreatWarningsAsErrors=true
dotnet test -c Release --no-build
git add -A
git commit -m "feat(views): add path-based routing"
```

Expected: all pass.

---

### Task 9: The browse and genre grids

The two screens where a click starts playback. A card's action is `playMedia`, which the web client turns straight into `playTrack()` — no server round trip, so this works despite both inbound transports being broken.

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/Views/StationCards.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Views/EmptyCatalog.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Views/BrowseView.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Views/GenreView.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/Views/BrowseViewTests.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/Views/GenreViewTests.cs`

**Interfaces:**
- Consumes: `StationCatalog`, `RadioRoutes`, `GenreMap`.
- Produces:
  - `StationCards.Play(RadioStation) : PluginComponent`
  - `EmptyCatalog.Build(StationCatalog) : PluginComponent`
  - `StationCards.Subtitle(RadioStation) : string?`
  - `StationCards.PopularCount : int`
  - `BrowseView.Build(StationCatalog) : PluginView`
  - `GenreView.Build(StationCatalog, string slug) : PluginView`

- [ ] **Step 1: Write the failing tests**

`tests/NoMercy.Plugin.InternetRadio.Tests/Views/BrowseViewTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class BrowseViewTests
{
    private static RadioStation Station(string id, string genre = "Ambient", int popularity = 1) =>
        new()
        {
            Id = id,
            Name = $"Station {id}",
            StreamUrl = $"https://example.com/{id}",
            LogoUrl = "https://example.com/logo.png",
            Genre = genre,
            Country = "NL",
            Popularity = popularity,
        };

    private static StationCatalog Catalog(params RadioStation[] stations) =>
        StationCatalog.Create(stations, CatalogSource.Fetched, DateTimeOffset.UtcNow);

    private static IEnumerable<PluginComponent> Flatten(PluginComponent node)
    {
        yield return node;
        foreach (PluginComponent child in node.Items.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static IEnumerable<PluginComponent> AllNodes(PluginView view) =>
        (view.Components ?? []).SelectMany(Flatten);

    // The whole point of the plugin: one click and it is playing.
    [Fact]
    public void CardsPlayTheStationRatherThanNavigatingToIt()
    {
        PluginView view = BrowseView.Build(Catalog(Station("a")));

        PluginComponent card = AllNodes(view)
            .Should().ContainSingle(node => node.Component == PluginComponentType.Card).Subject;

        card.Action!.Type.Should().Be(PluginActionType.PlayMedia);
        card.Action.Payload["streamUrl"].Should().Be("https://example.com/a");
        card.Action.Payload["title"].Should().Be("Station a");
        card.Action.Payload["cover"].Should().Be("https://example.com/logo.png");
    }

    [Fact]
    public void GenreButtonsNavigateToTheirGenreRoute()
    {
        PluginView view = BrowseView.Build(Catalog(Station("a", "Ambient"), Station("b", "Rock")));

        IEnumerable<PluginComponent> buttons = AllNodes(view)
            .Where(node => node.Component == PluginComponentType.Button
                && node.Action?.Type == PluginActionType.Navigate);

        buttons.Select(button => button.Action!.Payload["route"])
            .Should().Contain(RadioRoutes.Genre("ambient"))
            .And.Contain(RadioRoutes.Genre("rock"))
            .And.Contain(RadioRoutes.AllStations);
    }

    [Fact]
    public void OffersNoChipForAGenreWithNoStations()
    {
        PluginView view = BrowseView.Build(Catalog(Station("a", "Ambient")));

        AllNodes(view).Select(node => node.Action?.Payload.GetValueOrDefault("route"))
            .Should().NotContain(RadioRoutes.Genre("jazz"));
    }

    [Fact]
    public void ShowsTheMostPopularStationsFirst()
    {
        PluginView view = BrowseView.Build(
            Catalog(Station("quiet", "Ambient", 1), Station("loud", "Rock", 99)));

        AllNodes(view).Where(node => node.Component == PluginComponentType.Card)
            .First().Action!.Payload["title"].Should().Be("Station loud");
    }

    // An empty catalogue has to explain itself. A blank grid reads as a broken plugin.
    [Fact]
    public void ExplainsItselfWhenThereAreNoStations()
    {
        PluginView view = BrowseView.Build(StationCatalog.Empty(lastFetchFailed: true));

        AllNodes(view).Should().Contain(node => node.Component == PluginComponentType.EmptyState);
    }

    [Fact]
    public void OffersARetryWhenThereAreNoStations()
    {
        PluginView view = BrowseView.Build(StationCatalog.Empty(lastFetchFailed: true));

        AllNodes(view).Should().Contain(node =>
            node.Action != null && node.Action.Type == PluginActionType.RefreshView);
    }

    // The renderer only knows title/subtitle/caption; anything else silently reads as
    // body text, which is how the torrent plugin lost its section headings.
    [Fact]
    public void UsesOnlyTextVariantsTheRendererKnows()
    {
        PluginView view = BrowseView.Build(Catalog(Station("a")));

        AllNodes(view)
            .Where(node => node.Component == PluginComponentType.Text)
            .Select(node => node.Props.GetValueOrDefault("variant") as string)
            .Should().OnlyContain(variant =>
                variant == null || variant == "title" || variant == "subtitle" || variant == "caption");
    }

    // Two nodes with the same id make the client's keyed render ambiguous.
    [Fact]
    public void EveryNodeHasAUniqueId()
    {
        PluginView view = BrowseView.Build(
            Catalog(Station("a", "Ambient"), Station("b", "Rock"), Station("c", "Jazz")));

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }

    // Static content. A poll interval here is every open tab re-fetching for nothing.
    [Fact]
    public void DoesNotAskTheClientToPoll()
    {
        BrowseView.Build(Catalog(Station("a"))).RefreshInterval.Should().Be(0);
    }
}
```

`tests/NoMercy.Plugin.InternetRadio.Tests/Views/GenreViewTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class GenreViewTests
{
    private static RadioStation Station(string id, string genre) =>
        new() { Id = id, Name = $"Station {id}", StreamUrl = $"https://example.com/{id}", Genre = genre };

    private static StationCatalog Catalog(params RadioStation[] stations) =>
        StationCatalog.Create(stations, CatalogSource.Fetched, DateTimeOffset.UtcNow);

    private static IEnumerable<PluginComponent> Flatten(PluginComponent node)
    {
        yield return node;
        foreach (PluginComponent child in node.Items.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static IEnumerable<PluginComponent> AllNodes(PluginView view) =>
        (view.Components ?? []).SelectMany(Flatten);

    [Fact]
    public void ShowsOnlyThatGenresStations()
    {
        PluginView view = GenreView.Build(
            Catalog(Station("a", "Ambient"), Station("b", "Rock")), "ambient");

        AllNodes(view).Where(node => node.Component == PluginComponentType.Card)
            .Should().ContainSingle()
            .Which.Action!.Payload["title"].Should().Be("Station a");
    }

    [Fact]
    public void CardsPlayImmediately()
    {
        PluginView view = GenreView.Build(Catalog(Station("a", "Ambient")), "ambient");

        AllNodes(view).Single(node => node.Component == PluginComponentType.Card)
            .Action!.Type.Should().Be(PluginActionType.PlayMedia);
    }

    [Fact]
    public void OffersAWayBack()
    {
        PluginView view = GenreView.Build(Catalog(Station("a", "Ambient")), "ambient");

        AllNodes(view).Should().Contain(node =>
            node.Action != null
            && node.Action.Type == PluginActionType.Navigate
            && (string)node.Action.Payload["route"]! == RadioRoutes.Browse);
    }

    // A stale bookmark is not a failure worth reporting as one.
    [Fact]
    public void ShowsAnEmptyStateForAGenreThatDoesNotExist()
    {
        PluginView view = GenreView.Build(Catalog(Station("a", "Ambient")), "no-such-genre");

        AllNodes(view).Should().Contain(node => node.Component == PluginComponentType.EmptyState);
        AllNodes(view).Should().NotContain(node => node.Component == PluginComponentType.Card);
    }

    [Fact]
    public void EveryNodeHasAUniqueId()
    {
        PluginView view = GenreView.Build(
            Catalog(Station("a", "Ambient"), Station("b", "Ambient")), "ambient");

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }
}
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test -c Release --filter "FullyQualifiedName~BrowseViewTests|FullyQualifiedName~GenreViewTests"
```

Expected: FAIL — the views do not exist.

- [ ] **Step 3: Write `StationCards.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// One station as a card, shared by the browse and genre grids so the two screens
// cannot drift into behaving differently for the same station.
public static class StationCards
{
    /// <summary>How many stations the browse page's "Popular" grid shows.</summary>
    public const int PopularCount = 18;

    /// <summary>
    /// A card whose action is playMedia. Not navigate: the client turns this straight
    /// into playTrack(), so one click is listening — which is the entire job, and the
    /// one path that works while both inbound plugin transports are broken.
    /// </summary>
    public static PluginComponent Play(RadioStation station) =>
        PluginViews.Card(
            $"station-card-{station.Id}",
            station.Name,
            Subtitle(station),
            station.LogoUrl,
            PluginActionIntent.PlayMedia(
                station.StreamUrl,
                station.Name,
                // The player shows this where a track's artist would go; the genre is
                // the most useful thing a live stream has to put there.
                station.Genre,
                station.LogoUrl
            )
        );

    /// <summary>Genre and country, whichever of them is known. Null when neither is.</summary>
    public static string? Subtitle(RadioStation station)
    {
        string[] parts =
            [.. new[] { station.Genre, station.Country }.Where(part => !string.IsNullOrWhiteSpace(part))!];

        return parts.Length > 0 ? string.Join(" · ", parts) : null;
    }
}
```

- [ ] **Step 4: Write `BrowseView.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// The landing screen: what genres exist, and the most popular stations ready to play.
//
// Deliberately NOT one grid per genre. That would put every station on one page and
// make the genre routes redundant; this page answers "what is there and give me
// something now", and the genre pages answer "show me all of one kind".
public static class BrowseView
{
    public static PluginView Build(StationCatalog catalog)
    {
        if (catalog.IsEmpty)
        {
            return PluginViews.Declarative(EmptyCatalog.Build(catalog));
        }

        List<PluginComponent> children =
        [
            PluginViews.Text("browse-title", "Internet Radio", "title"),
            PluginViews.Text(
                "browse-summary",
                $"{catalog.Count} stations across {catalog.Genres.Count} genres. Pick one and it plays.",
                "caption"
            ),
            GenreChips(catalog),
            PluginViews.Text("browse-popular-heading", "Popular", "subtitle"),
            PluginViews.Grid(
                "browse-popular-grid",
                [.. catalog.Popular(StationCards.PopularCount).Select(StationCards.Play)]
            ),
        ];

        return PluginViews.Declarative(PluginViews.Container("browse-root", [.. children]));
    }

    private static PluginComponent GenreChips(StationCatalog catalog)
    {
        List<PluginComponent> chips =
        [
            .. catalog.Genres.Select(genre =>
                PluginViews.Button(
                    $"browse-genre-{genre.Section.Slug}",
                    $"{genre.Section.Label} ({genre.Count})",
                    PluginActionIntent.Navigate(RadioRoutes.Genre(genre.Section.Slug))
                )
            ),
            PluginViews.Button(
                "browse-all",
                "All stations",
                PluginActionIntent.Navigate(RadioRoutes.AllStations),
                icon: "gridMasonry"
            ),
        ];

        return PluginViews.Row("browse-genres", [.. chips]);
    }
}
```

- [ ] **Step 5: Write `EmptyCatalog.cs`**

Shared by browse and the genre page so an empty catalogue explains itself the same way everywhere.

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// What to show when there are no stations at all.
//
// A blank grid reads as a broken plugin, so this says which of the two things went
// wrong - the catalogue has not been fetched yet, or fetching it failed - and offers
// the retry. refreshView costs nothing: re-rendering re-runs the cache-first read,
// which fetches when there is nothing cached.
public static class EmptyCatalog
{
    public static PluginComponent Build(StationCatalog catalog)
    {
        string message = catalog.LastFetchFailed
            ? "The station list could not be fetched from radio-browser.info. "
              + "Check the server log for Internet Radio, and that the server can reach the internet."
            : "The station list has not been downloaded yet. This happens on the first run "
              + "and after the plugin's data folder is cleared.";

        return PluginViews.Container(
            "catalog-empty",
            PluginViews.Badge(
                "catalog-empty-badge",
                catalog.LastFetchFailed ? "Unavailable" : "Not downloaded yet",
                catalog.LastFetchFailed ? PluginBadgeVariant.Danger : PluginBadgeVariant.Info
            ),
            PluginViews.EmptyState("catalog-empty-state", "No stations", message),
            PluginViews.Button(
                "catalog-empty-retry",
                "Try again",
                PluginActionIntent.RefreshView()
            )
        );
    }
}
```

- [ ] **Step 6: Write `GenreView.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// One genre, every station in it, each ready to play.
public static class GenreView
{
    public static PluginView Build(StationCatalog catalog, string slug)
    {
        IReadOnlyList<RadioStation> stations = catalog.ByGenreSlug(slug);

        if (stations.Count == 0)
        {
            // A stale bookmark or a genre that emptied out between refreshes. Not an
            // error - the way back is what is actually useful here.
            return PluginViews.Declarative(
                PluginViews.Container(
                    "genre-root",
                    BackToBrowse,
                    PluginViews.EmptyState(
                        "genre-empty",
                        "No stations in this genre",
                        "It may have been renamed or emptied since this page was last opened."
                    )
                )
            );
        }

        string label = GenreMap.BySlug(slug)?.Label ?? stations[0].Genre ?? GenreMap.Other;

        return PluginViews.Declarative(
            PluginViews.Container(
                "genre-root",
                BackToBrowse,
                PluginViews.Text("genre-title", label, "title"),
                PluginViews.Text("genre-count", $"{stations.Count} stations", "caption"),
                PluginViews.Grid("genre-grid", [.. stations.Select(StationCards.Play)])
            )
        );
    }

    private static PluginComponent BackToBrowse =>
        PluginViews.Button(
            "genre-back",
            "All genres",
            PluginActionIntent.Navigate(RadioRoutes.Browse),
            icon: "arrowLeft"
        );
}
```

- [ ] **Step 7: Run tests and commit**

```bash
dotnet build -c Release -p:TreatWarningsAsErrors=true
dotnet test -c Release --no-build
git add -A
git commit -m "feat(views): add the browse and genre grids"
```

Expected: all pass.

---

### Task 10: The all-stations table and the station detail page

The two screens for inspecting rather than playing. The table's rows navigate; the detail page is where play, enqueue and the homepage live.

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/Views/AllStationsView.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Views/StationView.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/Views/AllStationsViewTests.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/Views/StationViewTests.cs`

**Interfaces:**
- Consumes: `StationCatalog`, `RadioStation`, `RadioRoutes`, `EmptyCatalog`.
- Produces:
  - `AllStationsView.Build(StationCatalog) : PluginView`
  - `StationView.Build(StationCatalog, string id) : PluginView`

- [ ] **Step 1: Write the failing tests**

`tests/NoMercy.Plugin.InternetRadio.Tests/Views/AllStationsViewTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class AllStationsViewTests
{
    private static RadioStation Station(string id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            StreamUrl = $"https://example.com/{id}",
            Genre = "Ambient",
            Country = "NL",
            BitrateKbps = 128,
            Codec = "MP3",
        };

    private static StationCatalog Catalog(params RadioStation[] stations) =>
        StationCatalog.Create(stations, CatalogSource.Fetched, DateTimeOffset.UtcNow);

    private static IEnumerable<PluginComponent> Flatten(PluginComponent node)
    {
        yield return node;
        foreach (PluginComponent child in node.Items.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static IEnumerable<PluginComponent> AllNodes(PluginView view) =>
        (view.Components ?? []).SelectMany(Flatten);

    private static PluginComponent Table(PluginView view) =>
        AllNodes(view).Single(node => node.Component == PluginComponentType.Table);

    // A row supplies its cells by column key, so a column the rows never fill renders
    // as a blank stripe down the table.
    [Fact]
    public void EveryColumnIsFilledByEveryRow()
    {
        PluginView view = AllStationsView.Build(Catalog(Station("a", "Alpha FM")));

        PluginComponent table = Table(view);
        List<PluginTableColumn> columns = (List<PluginTableColumn>)table.Props["columns"]!;

        foreach (PluginComponent row in table.Items)
        {
            foreach (PluginTableColumn column in columns)
            {
                row.Props.Should().ContainKey(column.Key);
            }
        }
    }

    // This table is the browse-by-detail surface: the grids play, this one inspects.
    [Fact]
    public void RowsNavigateToTheStationDetailPage()
    {
        PluginView view = AllStationsView.Build(Catalog(Station("a", "Alpha FM")));

        PluginComponent row = Table(view).Items.Should().ContainSingle().Subject;

        row.Action!.Type.Should().Be(PluginActionType.Navigate);
        row.Action.Payload["route"].Should().Be(RadioRoutes.Station("a"));
    }

    [Fact]
    public void ListsEveryStationSortedByName()
    {
        PluginView view = AllStationsView.Build(
            Catalog(Station("b", "Zulu FM"), Station("a", "Alpha FM")));

        Table(view).Items.Select(row => row.Props["name"]).Should().Equal("Alpha FM", "Zulu FM");
    }

    // radio-browser reports 0 for "unknown", which the model stores as null. Rendering
    // that as "0 kbps" would claim a silent stream.
    [Fact]
    public void ShowsAnUnknownBitrateAsAnEmDashRatherThanZero()
    {
        RadioStation unknown = Station("a", "Alpha FM") with { BitrateKbps = null };

        PluginComponent row = Table(AllStationsView.Build(Catalog(unknown))).Items.Single();

        row.Props["bitrate"].Should().Be("—");
    }

    [Fact]
    public void OffersAWayBack()
    {
        PluginView view = AllStationsView.Build(Catalog(Station("a", "Alpha FM")));

        AllNodes(view).Should().Contain(node =>
            node.Action != null
            && node.Action.Type == PluginActionType.Navigate
            && (string)node.Action.Payload["route"]! == RadioRoutes.Browse);
    }

    [Fact]
    public void ExplainsItselfWhenThereAreNoStations()
    {
        PluginView view = AllStationsView.Build(StationCatalog.Empty());

        AllNodes(view).Should().Contain(node => node.Component == PluginComponentType.EmptyState);
    }

    [Fact]
    public void EveryNodeHasAUniqueId()
    {
        PluginView view = AllStationsView.Build(
            Catalog(Station("a", "Alpha FM"), Station("b", "Bravo FM")));

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }
}
```

`tests/NoMercy.Plugin.InternetRadio.Tests/Views/StationViewTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class StationViewTests
{
    private static RadioStation Full =>
        new()
        {
            Id = "a",
            Name = "Alpha FM",
            StreamUrl = "https://example.com/a",
            LogoUrl = "https://example.com/logo.png",
            Homepage = "https://example.com",
            Genre = "Ambient",
            Country = "NL",
            Language = "english",
            BitrateKbps = 128,
            Codec = "MP3",
        };

    private static StationCatalog Catalog(params RadioStation[] stations) =>
        StationCatalog.Create(stations, CatalogSource.Fetched, DateTimeOffset.UtcNow);

    private static IEnumerable<PluginComponent> Flatten(PluginComponent node)
    {
        yield return node;
        foreach (PluginComponent child in node.Items.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static IEnumerable<PluginComponent> AllNodes(PluginView view) =>
        (view.Components ?? []).SelectMany(Flatten);

    private static PluginComponent? ActionOfType(PluginView view, string type) =>
        AllNodes(view).FirstOrDefault(node => node.Action?.Type == type);

    [Fact]
    public void OffersPlayAndEnqueueForTheStream()
    {
        PluginView view = StationView.Build(Catalog(Full), "a");

        PluginComponent play = ActionOfType(view, PluginActionType.PlayMedia)!;
        play.Action!.Payload["streamUrl"].Should().Be("https://example.com/a");
        play.Action.Payload["title"].Should().Be("Alpha FM");

        ActionOfType(view, PluginActionType.Enqueue).Should().NotBeNull();
    }

    [Fact]
    public void OffersTheHomepageWhenThereIsOne()
    {
        PluginView view = StationView.Build(Catalog(Full), "a");

        ActionOfType(view, PluginActionType.OpenWebView)!
            .Action!.Payload["entryUrl"].Should().Be("https://example.com");
    }

    // A button that opens nothing is worse than no button.
    [Fact]
    public void OmitsTheHomepageButtonWhenThereIsNoHomepage()
    {
        PluginView view = StationView.Build(Catalog(Full with { Homepage = null }), "a");

        ActionOfType(view, PluginActionType.OpenWebView).Should().BeNull();
    }

    [Fact]
    public void ShowsTheFullRecordIncludingTheStreamUrl()
    {
        PluginView view = StationView.Build(Catalog(Full), "a");

        PluginComponent table = AllNodes(view).Single(node => node.Component == PluginComponentType.Table);
        IEnumerable<object?> values = table.Items.Select(row => row.Props["value"]);

        values.Should().Contain("Ambient").And.Contain("NL").And.Contain("https://example.com/a");
    }

    [Fact]
    public void NamesTheProvenanceOfAFetchedStation()
    {
        PluginView view = StationView.Build(Catalog(Full), "a");

        AllNodes(view).SelectMany(node => node.Props.Values)
            .Should().Contain(value => value != null && value.ToString()!.Contains("radio-browser"));
    }

    [Fact]
    public void NamesTheProvenanceOfAUserSuppliedStation()
    {
        PluginView view = StationView.Build(Catalog(Full with { IsUserSupplied = true }), "a");

        AllNodes(view).SelectMany(node => node.Props.Values)
            .Should().Contain(value => value != null && value.ToString()!.Contains(StationOverrides.FileName));
    }

    // A station can vanish between a page being opened and a link being followed -
    // the catalogue refreshes underneath it.
    [Fact]
    public void ShowsAnEmptyStateForAStationThatIsNoLongerThere()
    {
        PluginView view = StationView.Build(Catalog(Full), "gone");

        AllNodes(view).Should().Contain(node => node.Component == PluginComponentType.EmptyState);
        ActionOfType(view, PluginActionType.PlayMedia).Should().BeNull();
    }

    [Fact]
    public void RendersAStationMissingEveryOptionalField()
    {
        RadioStation bare = new() { Id = "b", Name = "Bare FM", StreamUrl = "https://example.com/b" };

        PluginView view = StationView.Build(Catalog(bare), "b");

        ActionOfType(view, PluginActionType.PlayMedia).Should().NotBeNull();
        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryNodeHasAUniqueId()
    {
        PluginView view = StationView.Build(Catalog(Full), "a");

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }
}
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test -c Release --filter "FullyQualifiedName~AllStationsViewTests|FullyQualifiedName~StationViewTests"
```

Expected: FAIL — the views do not exist.

- [ ] **Step 3: Write `AllStationsView.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// Every station with its metadata, as a table whose rows lead to the detail page.
//
// The split is deliberate: the grids play on click, this inspects on click. Putting
// both affordances on one surface means every station needs two hit targets, and a
// card is one.
public static class AllStationsView
{
    /// <summary>Shown where a value is not known. Never "0", which would be a claim.</summary>
    private const string Unknown = "—";

    private static IReadOnlyList<PluginTableColumn> Columns { get; } =
        [
            new() { Key = "name", Label = "Station" },
            new() { Key = "genre", Label = "Genre" },
            new() { Key = "country", Label = "Country" },
            new() { Key = "bitrate", Label = "Bitrate", Align = "right" },
            new() { Key = "codec", Label = "Codec" },
        ];

    public static PluginView Build(StationCatalog catalog)
    {
        if (catalog.IsEmpty)
        {
            return PluginViews.Declarative(
                PluginViews.Container("all-root", BackToBrowse, EmptyCatalog.Build(catalog))
            );
        }

        IEnumerable<PluginComponent> rows = catalog
            .Stations.OrderBy(station => station.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(Row);

        return PluginViews.Declarative(
            PluginViews.Container(
                "all-root",
                BackToBrowse,
                PluginViews.Text("all-title", "All stations", "title"),
                PluginViews.Text(
                    "all-hint",
                    "Select a station to see its details and play it.",
                    "caption"
                ),
                PluginViews.Table("all-table", Columns, [.. rows], "No stations.")
            )
        );
    }

    private static PluginComponent Row(RadioStation station) =>
        PluginViews.Row(
            $"all-row-{station.Id}",
            new Dictionary<string, object?>
            {
                ["name"] = station.Name,
                ["genre"] = station.Genre ?? Unknown,
                ["country"] = station.Country ?? Unknown,
                // Formatted here rather than sent as a number with a Bytes/Rate cell
                // type: neither of those means kbps, and both would be relabelled by
                // the client into something this is not.
                ["bitrate"] = station.BitrateKbps is { } kbps ? $"{kbps} kbps" : Unknown,
                ["codec"] = station.Codec ?? Unknown,
            },
            PluginActionIntent.Navigate(RadioRoutes.Station(station.Id))
        );

    private static PluginComponent BackToBrowse =>
        PluginViews.Button(
            "all-back",
            "Back",
            PluginActionIntent.Navigate(RadioRoutes.Browse),
            icon: "arrowLeft"
        );
}
```

- [ ] **Step 4: Write `StationView.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// One station: what it is, and the three things you can do with it.
public static class StationView
{
    private static IReadOnlyList<PluginTableColumn> Columns { get; } =
        [
            new() { Key = "field", Label = "Field", Width = "12rem" },
            new() { Key = "value", Label = "Value" },
        ];

    public static PluginView Build(StationCatalog catalog, string id)
    {
        RadioStation? station = catalog.ById(id);

        if (station is null)
        {
            // The catalogue refreshes underneath an open page, so a link followed a
            // minute later can point at a station that is no longer listed.
            return PluginViews.Declarative(
                PluginViews.Container(
                    "station-root",
                    BackToAll,
                    PluginViews.EmptyState(
                        "station-missing",
                        "Station not found",
                        "It may have been removed from the catalogue since this page was opened."
                    )
                )
            );
        }

        return PluginViews.Declarative(
            PluginViews.Container(
                "station-root",
                BackToAll,
                PluginViews.Detail(
                    $"station-detail-{station.Id}",
                    station.Name,
                    Description(station),
                    station.LogoUrl,
                    Actions(station),
                    Facts(station)
                )
            )
        );
    }

    /// <summary>
    /// Composed only from what is known, so a sparse station reads as a short
    /// sentence rather than one full of blanks.
    /// </summary>
    private static string? Description(RadioStation station)
    {
        List<string> sentences = [];

        string where = station.Country is { } country ? $" from {country}" : string.Empty;
        if (station.Genre is { } genre)
        {
            sentences.Add($"{genre}{where}.");
        }
        else if (station.Country is { } only)
        {
            sentences.Add($"Broadcasting from {only}.");
        }

        string quality = string.Join(
            ' ',
            new[]
            {
                station.BitrateKbps is { } kbps ? $"{kbps} kbps" : null,
                station.Codec,
            }.Where(part => !string.IsNullOrWhiteSpace(part))
        );

        if (!string.IsNullOrWhiteSpace(quality))
        {
            sentences.Add($"{quality}.");
        }

        return sentences.Count > 0 ? string.Join(' ', sentences) : null;
    }

    private static PluginComponent Actions(RadioStation station)
    {
        List<PluginComponent> buttons =
        [
            PluginViews.Button(
                $"station-play-{station.Id}",
                "Play",
                PluginActionIntent.PlayMedia(
                    station.StreamUrl, station.Name, station.Genre, station.LogoUrl),
                icon: "play"
            ),
            PluginViews.Button(
                $"station-enqueue-{station.Id}",
                "Add to queue",
                PluginActionIntent.Enqueue(
                    station.StreamUrl, station.Name, station.Genre, station.LogoUrl),
                icon: "playlistAdd"
            ),
        ];

        // Only when there is somewhere to go. A button that opens nothing is worse
        // than an absent one.
        if (!string.IsNullOrWhiteSpace(station.Homepage))
        {
            buttons.Add(
                PluginViews.Button(
                    $"station-homepage-{station.Id}",
                    "Open homepage",
                    PluginActionIntent.OpenWebView(station.Homepage),
                    icon: "globe"
                )
            );
        }

        return PluginViews.Row($"station-actions-{station.Id}", [.. buttons]);
    }

    private static PluginComponent Facts(RadioStation station)
    {
        List<(string Field, string? Value)> facts =
        [
            ("Genre", station.Genre),
            ("Country", station.Country),
            ("Language", station.Language),
            ("Bitrate", station.BitrateKbps is { } kbps ? $"{kbps} kbps" : null),
            ("Codec", station.Codec),
            // Shown in full. It is the first thing worth having when a station will
            // not play, and the table scrolls horizontally rather than truncating.
            ("Stream", station.StreamUrl),
            ("Source", Provenance(station)),
        ];

        IEnumerable<PluginComponent> rows = facts
            .Where(fact => !string.IsNullOrWhiteSpace(fact.Value))
            .Select(fact =>
                PluginViews.Row(
                    $"station-fact-{station.Id}-{StationGates.Slugify(fact.Field)}",
                    new Dictionary<string, object?> { ["field"] = fact.Field, ["value"] = fact.Value }
                )
            );

        return PluginViews.Table($"station-facts-{station.Id}", Columns, [.. rows]);
    }

    private static string Provenance(RadioStation station) =>
        station.IsUserSupplied
            ? $"Your own {StationOverrides.FileName}"
            : $"radio-browser.info ({station.Id})";

    private static PluginComponent BackToAll =>
        PluginViews.Button(
            "station-back",
            "All stations",
            PluginActionIntent.Navigate(RadioRoutes.AllStations),
            icon: "arrowLeft"
        );
}
```

- [ ] **Step 5: Run tests and commit**

```bash
dotnet build -c Release -p:TreatWarningsAsErrors=true
dotnet test -c Release --no-build
git add -A
git commit -m "feat(views): add the all-stations table and station detail page"
```

Expected: all pass.

---

### Task 11: The settings page

Nothing here is editable, because there is no inbound transport to save through. So the page says what it is: where the stations came from, how old they are, and where to put your own list.

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/Views/SettingsView.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/Views/SettingsViewTests.cs`

**Interfaces:**
- Consumes: `StationCatalog`, `CatalogSource`, `StationOverrides`, `RadioRoutes`.
- Produces: `SettingsView.Build(StationCatalog catalog, string dataFolderPath, DateTimeOffset now) : PluginView`

`now` is a parameter, not `DateTimeOffset.UtcNow`, so the "3 hours ago" text is testable without freezing the clock.

- [ ] **Step 1: Write the failing tests**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class SettingsViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static RadioStation Station(string id, string genre = "Ambient") =>
        new() { Id = id, Name = $"Station {id}", StreamUrl = $"https://example.com/{id}", Genre = genre };

    private static IEnumerable<PluginComponent> Flatten(PluginComponent node)
    {
        yield return node;
        foreach (PluginComponent child in node.Items.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static IEnumerable<PluginComponent> AllNodes(PluginView view) =>
        (view.Components ?? []).SelectMany(Flatten);

    private static string Text(PluginView view) =>
        string.Join(" ", AllNodes(view).SelectMany(node => node.Props.Values)
            .Where(value => value is string)
            .Select(value => (string)value!));

    private static PluginView Build(StationCatalog catalog) =>
        SettingsView.Build(catalog, "/data/plugins/data/abc", Now);

    [Theory]
    [InlineData(CatalogSource.Fetched)]
    [InlineData(CatalogSource.Cache)]
    [InlineData(CatalogSource.UserOverride)]
    [InlineData(CatalogSource.Unavailable)]
    public void BadgesWhereTheStationsCameFrom(CatalogSource source)
    {
        StationCatalog catalog = source == CatalogSource.Unavailable
            ? StationCatalog.Empty()
            : StationCatalog.Create([Station("a")], source, Now);

        AllNodes(Build(catalog))
            .Should().Contain(node => node.Component == PluginComponentType.Badge);
    }

    // The first thing anyone wants when a station is missing.
    [Fact]
    public void SaysHowOldTheCatalogueIs()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a")], CatalogSource.Cache, Now - TimeSpan.FromHours(3));

        Text(Build(catalog)).Should().Contain("3 hours ago");
    }

    [Fact]
    public void SaysWhenTheCatalogueHasNeverBeenFetched()
    {
        Text(Build(StationCatalog.Empty())).Should().Contain("never");
    }

    [Fact]
    public void OffersARefresh()
    {
        AllNodes(Build(StationCatalog.Create([Station("a")], CatalogSource.Cache, Now)))
            .Should().Contain(node => node.Action != null
                && node.Action.Type == PluginActionType.RefreshView);
    }

    // So nobody has to derive the dashless-GUID path from a README.
    [Fact]
    public void NamesTheDataFolderAndTheOverrideFile()
    {
        string text = Text(Build(StationCatalog.Create([Station("a")], CatalogSource.Fetched, Now)));

        text.Should().Contain("/data/plugins/data/abc");
        text.Should().Contain(StationOverrides.FileName);
    }

    [Fact]
    public void CountsTheStationsInEachGenre()
    {
        StationCatalog catalog = StationCatalog.Create(
            [Station("a", "Ambient"), Station("b", "Ambient"), Station("c", "Rock")],
            CatalogSource.Fetched, Now);

        PluginComponent table = AllNodes(Build(catalog))
            .First(node => node.Component == PluginComponentType.Table);

        table.Items.Should().HaveCount(2);
        table.Items.Should().Contain(row =>
            (string)row.Props["genre"]! == "Ambient" && (string)row.Props["stations"]! == "2");
    }

    // A stale catalogue has to explain itself, or it looks like the plugin simply
    // stopped finding new stations.
    [Fact]
    public void SaysSoWhenTheLastRefreshFailed()
    {
        StationCatalog catalog = StationCatalog
            .Create([Station("a")], CatalogSource.Cache, Now - TimeSpan.FromDays(4))
            .WithFailedFetch();

        Text(Build(catalog)).Should().Contain("could not be refreshed");
    }

    // The honest statement of why there is nothing to configure. Named so that when
    // the server is fixed, a search for the issue number finds this page.
    [Fact]
    public void ExplainsWhyThereIsNothingToEdit()
    {
        Text(Build(StationCatalog.Create([Station("a")], CatalogSource.Fetched, Now)))
            .Should().Contain("#26");
    }

    [Fact]
    public void UsesOnlyTextVariantsTheRendererKnows()
    {
        PluginView view = Build(StationCatalog.Create([Station("a")], CatalogSource.Fetched, Now));

        AllNodes(view)
            .Where(node => node.Component == PluginComponentType.Text)
            .Select(node => node.Props.GetValueOrDefault("variant") as string)
            .Should().OnlyContain(variant =>
                variant == null || variant == "title" || variant == "subtitle" || variant == "caption");
    }

    [Fact]
    public void EveryNodeHasAUniqueId()
    {
        PluginView view = Build(StationCatalog.Create(
            [Station("a", "Ambient"), Station("b", "Rock")], CatalogSource.Fetched, Now));

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }
}
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test -c Release --filter FullyQualifiedName~SettingsViewTests
```

Expected: FAIL — `SettingsView` does not exist.

- [ ] **Step 3: Write `SettingsView.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// Status, not settings.
//
// There is nothing to edit because there is nowhere to save to: the server's plugin
// REST routes are unversioned while the client posts to /api/v1 (issue #26), and the
// hub is not the alternative it looks like - nothing ever registers a plugin's hub
// handler, so IPluginHubHandler never receives anything. Rendering a form that
// silently 404s would be the false promise this plugin is meant to stop making.
//
// So this page answers the questions someone actually arrives with: where did these
// stations come from, how old are they, and how do I add my own.
public static class SettingsView
{
    private static IReadOnlyList<PluginTableColumn> Columns { get; } =
        [
            new() { Key = "genre", Label = "Genre" },
            new() { Key = "stations", Label = "Stations", Align = "right" },
        ];

    public static PluginView Build(StationCatalog catalog, string dataFolderPath, DateTimeOffset now)
    {
        List<PluginComponent> children =
        [
            PluginViews.Text("settings-title", "Internet Radio", "title"),
            PluginViews.Row(
                "settings-status",
                SourceBadge(catalog),
                PluginViews.Text("settings-age", Age(catalog, now), "caption")
            ),
            PluginViews.Button(
                "settings-refresh",
                "Refresh now",
                PluginActionIntent.RefreshView(),
                icon: "portableRadio"
            ),
        ];

        if (catalog.LastFetchFailed)
        {
            children.Add(
                PluginViews.Text(
                    "settings-refresh-failed",
                    "The catalogue could not be refreshed on the last attempt. "
                        + "Anything shown is from the cache. Check the server log for Internet Radio.",
                    "caption"
                )
            );
        }

        if (!catalog.IsEmpty)
        {
            children.Add(PluginViews.Text("settings-genres-heading", "Genres", "subtitle"));
            children.Add(
                PluginViews.Table(
                    "settings-genres",
                    Columns,
                    [
                        .. catalog.Genres.Select(genre =>
                            PluginViews.Row(
                                $"settings-genre-{genre.Section.Slug}",
                                new Dictionary<string, object?>
                                {
                                    ["genre"] = genre.Section.Label,
                                    ["stations"] = genre.Count.ToString(
                                        System.Globalization.CultureInfo.InvariantCulture),
                                }
                            )
                        ),
                    ]
                )
            );
        }

        children.Add(PluginViews.Text("settings-own-heading", "Your own stations", "subtitle"));
        children.Add(
            PluginViews.Text(
                "settings-own-body",
                $"Drop a file named {StationOverrides.FileName} into {dataFolderPath} to replace the "
                    + "fetched list entirely. It is a JSON array of stations, each needing at least a "
                    + "name and a streamUrl. Your file is used as written and is not filtered, so it is "
                    + "also the way to add a station radio-browser.info does not carry.",
                "caption"
            )
        );

        children.Add(PluginViews.Text("settings-editing-heading", "Why there is nothing to edit", "subtitle"));
        children.Add(
            PluginViews.Text(
                "settings-editing-body",
                "This page is read-only. A plugin cannot yet receive anything from its own UI on this "
                    + "server: plugin REST routes are served unversioned while the dashboard posts to "
                    + "/api/v1 (media-server issue #26), and the hub is not an alternative because "
                    + "plugin hub handlers are never registered. Editable settings arrive when either "
                    + "is fixed.",
                "caption"
            )
        );

        return PluginViews.Declarative(PluginViews.Container("settings-root", [.. children]));
    }

    private static PluginComponent SourceBadge(StationCatalog catalog)
    {
        (string Label, string Variant) badge = catalog.Source switch
        {
            CatalogSource.UserOverride => ("Your own station list", PluginBadgeVariant.Info),
            CatalogSource.Fetched => ("Fetched from radio-browser.info", PluginBadgeVariant.Success),
            CatalogSource.Cache when catalog.LastFetchFailed
                => ("Cached — refresh failed", PluginBadgeVariant.Warning),
            CatalogSource.Cache => ("Cached", PluginBadgeVariant.Neutral),
            _ => ("No stations", PluginBadgeVariant.Danger),
        };

        return PluginViews.Badge("settings-source", badge.Label, badge.Variant);
    }

    private static string Age(StationCatalog catalog, DateTimeOffset now)
    {
        if (catalog.Source == CatalogSource.UserOverride)
        {
            return $"{catalog.Count} stations, read from your own {StationOverrides.FileName}.";
        }

        if (catalog.FetchedAt is not { } fetchedAt)
        {
            return "The station list has never been fetched.";
        }

        TimeSpan age = now - fetchedAt;

        string ago = age switch
        {
            { TotalMinutes: < 2 } => "just now",
            { TotalHours: < 1 } => $"{(int)age.TotalMinutes} minutes ago",
            { TotalHours: < 2 } => "1 hour ago",
            { TotalDays: < 1 } => $"{(int)age.TotalHours} hours ago",
            { TotalDays: < 2 } => "1 day ago",
            _ => $"{(int)age.TotalDays} days ago",
        };

        return $"{catalog.Count} stations, updated {ago}.";
    }
}
```

- [ ] **Step 4: Run tests and commit**

```bash
dotnet build -c Release -p:TreatWarningsAsErrors=true
dotnet test -c Release --no-build
git add -A
git commit -m "feat(views): add the settings status page"
```

Expected: all pass.

---

### Task 12: The plugin class

The class the server loads. It owns the lifecycle, dispatches routes, and runs the daily refresh — and it is the only thing that touches `IPluginContext`.

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/InternetRadioPlugin.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/TestSupport/FakePluginContext.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/PluginLifecycleTests.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/DiscoveryContractTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `InternetRadioPlugin : IUiPlugin, IScheduledTaskPlugin`, with a public parameterless constructor.

- [ ] **Step 1: Write `FakePluginContext`**

Only the members this plugin reads are real; the rest throw if touched, so a test that quietly depends on something the plugin should not use fails loudly.

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using Microsoft.Extensions.Logging;
using NoMercy.Events;
using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio.Tests.TestSupport;

public sealed class FakePluginContext(string dataFolderPath, HttpClient httpClient) : IPluginContext
{
    public ILogger Logger { get; } = new RecordingLogger();
    public string DataFolderPath { get; } = dataFolderPath;
    public HttpClient HttpClient { get; } = httpClient;
    public Guid PluginId => PluginIdentity.Id;

    public RecordingLogger Recorded => (RecordingLogger)Logger;

    // Not used by this plugin. Throwing rather than returning a null object means a
    // test cannot accidentally pass while the plugin reaches for something it should
    // not - the manifest declares no library access, no secrets and no hub.
    public IEventBus EventBus => throw new NotSupportedException();
    public IServiceProvider Services => throw new NotSupportedException();
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

- [ ] **Step 2: Write `DiscoveryContractTests`**

These pin the steps the server's `PluginManager` actually performs, none of which an ordinary unit test exercises — a test constructs the plugin with `new`, and the server does not.

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Reflection;
using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests;

// The discovery contract:
//   1. Scan <server>/plugins/<folder>/plugin.json.
//   2. Load the assembly named by the manifest's "assembly" field.
//   3. Reflect every public, non-abstract type assignable to IPlugin.
//   4. Instantiate with Activator.CreateInstance - so it MUST have a public
//      parameterless constructor.
//   5. Call Initialize(IPluginContext) once.
//   6. Pick up specialised interfaces by reflecting the TYPE.
//
// The failure these guard against is the plugin refusing to load at all, on a real
// server, while every other test passes: give the entry class a constructor
// parameter - the obvious move the day it wants something injected - and step 4
// throws MissingMethodException and nothing here would otherwise notice.
public class DiscoveryContractTests
{
    private static IReadOnlyList<Type> DiscoverablePluginTypes =>
        [
            .. typeof(InternetRadioPlugin).Assembly.GetTypes()
                .Where(type => type.IsPublic && !type.IsAbstract && typeof(IPlugin).IsAssignableFrom(type)),
        ];

    [Fact]
    public void Assembly_ExposesExactlyOneDiscoverablePluginType()
    {
        // Two would load a second plugin under the same manifest id; none would load
        // nothing and report no error.
        DiscoverablePluginTypes.Should().ContainSingle()
            .Which.Should().Be<InternetRadioPlugin>();
    }

    [Fact]
    public void EntryType_HasAPublicParameterlessConstructor()
    {
        typeof(InternetRadioPlugin)
            .GetConstructor(BindingFlags.Public | BindingFlags.Instance, binder: null, types: [], modifiers: null)
            .Should().NotBeNull("the server instantiates plugins with Activator.CreateInstance");
    }

    [Fact]
    public void EntryType_CanBeCreatedTheWayTheServerCreatesIt()
    {
        object? created = Activator.CreateInstance(typeof(InternetRadioPlugin));

        created.Should().BeOfType<InternetRadioPlugin>();
        ((IDisposable)created!).Dispose();
    }

    // Step 6 reflects the type, not the manifest, so a hook declared in plugin.json
    // with no matching interface is silently never picked up.
    [Fact]
    public void EntryType_ImplementsEveryInterfaceItsManifestClaims()
    {
        foreach (string hook in ManifestTests.LoadManifest().Capabilities?.Hooks ?? [])
        {
            Type expected = hook switch
            {
                PluginHookCapability.Ui => typeof(IUiPlugin),
                PluginHookCapability.ScheduledTask => typeof(IScheduledTaskPlugin),
                PluginHookCapability.MediaSource => typeof(IMediaSourcePlugin),
                PluginHookCapability.Metadata => typeof(IMetadataPlugin),
                PluginHookCapability.Auth => typeof(IAuthPlugin),
                PluginHookCapability.Encoder => typeof(IEncoderPlugin),
                _ => typeof(IPlugin),
            };

            expected.IsAssignableFrom(typeof(InternetRadioPlugin)).Should()
                .BeTrue($"plugin.json declares '{hook}', so the entry type must implement {expected.Name}");
        }
    }

    [Fact]
    public void EntryType_IdMatchesTheManifestTheServerKeysLifecycleOn()
    {
        using InternetRadioPlugin plugin = new();

        plugin.Id.Should().Be(ManifestTests.LoadManifest().Id);
    }

    // PluginUiDescriptorDto prefers the instance's NavEntries over the manifest's
    // mounts, so these two are separate declarations of the same fact and nothing
    // else would catch them drifting.
    [Fact]
    public void NavEntries_AgreeWithTheManifestMounts()
    {
        using InternetRadioPlugin plugin = new();
        List<PluginUiMount> mounts = ManifestTests.LoadManifest().Capabilities!.Ui!.Mounts;

        plugin.NavEntries.Should().HaveCount(mounts.Count);

        foreach (PluginUiMount mount in mounts)
        {
            plugin.NavEntries.Should().ContainSingle(entry =>
                entry.Section == mount.Section
                && entry.Route == mount.Route
                && entry.Label == mount.Label
                && entry.Icon == mount.Icon);
        }
    }

    [Fact]
    public void Jobs_AreNamedAndCarryACronExpression()
    {
        using InternetRadioPlugin plugin = new();

        plugin.Jobs.Should().NotBeEmpty();
        plugin.Jobs.Should().OnlyContain(job =>
            !string.IsNullOrWhiteSpace(job.Name) && !string.IsNullOrWhiteSpace(job.CronExpression));
    }

    // The host may read Jobs while registering the plugin, which can happen before
    // Initialize. Throwing there fails registration outright.
    [Fact]
    public void Jobs_AreReadableBeforeInitialize()
    {
        using InternetRadioPlugin plugin = new();

        plugin.Invoking(candidate => candidate.Jobs.ToList()).Should().NotThrow();
        plugin.Invoking(candidate => _ = candidate.CronExpression).Should().NotThrow();
    }
}
```

- [ ] **Step 3: Write `PluginLifecycleTests`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests;

public sealed class PluginLifecycleTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"nm-radio-{Guid.NewGuid():N}");
    private readonly FakeHttpMessageHandler _handler = new();

    public PluginLifecycleTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private const string OneStation = """
        [{"stationuuid":"u1","name":"Example FM","url":"https://example.com/a",
          "url_resolved":"https://example.com/a","tags":"ambient","countrycode":"NL",
          "codec":"MP3","bitrate":128,"hls":0,"lastcheckok":1,"votes":5}]
        """;

    private InternetRadioPlugin Started()
    {
        _handler.Respond(OneStation);
        InternetRadioPlugin plugin = new();
        plugin.Initialize(new FakePluginContext(_folder, new HttpClient(_handler)));
        return plugin;
    }

    private static PluginViewRequest Request(string route) => new() { Route = route };

    [Fact]
    public async Task ServesTheBrowsePageAtTheRoot()
    {
        using InternetRadioPlugin plugin = Started();

        PluginView view = await plugin.GetViewAsync(Request("/"), CancellationToken.None);

        view.Components.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/all")]
    [InlineData("/settings")]
    [InlineData("/genre/ambient")]
    public async Task ServesEveryDeclaredRoute(string route)
    {
        using InternetRadioPlugin plugin = Started();

        PluginView view = await plugin.GetViewAsync(Request(route), CancellationToken.None);

        view.Components.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ServesAnEmptyStateForAnUnknownRoute()
    {
        using InternetRadioPlugin plugin = Started();

        PluginView view = await plugin.GetViewAsync(Request("/nope"), CancellationToken.None);

        view.Components.Should().Contain(node => node.Component == PluginComponentType.EmptyState);
    }

    // Initialize is synchronous with nowhere to await a fix, and a plugin that throws
    // from it fails to load. So it captures the context and does nothing else - the
    // first real work happens on a view or a tick.
    [Fact]
    public void InitializeDoesNoIoAndDoesNotThrow()
    {
        using InternetRadioPlugin plugin = new();

        plugin.Invoking(candidate =>
                candidate.Initialize(new FakePluginContext(_folder, new HttpClient(_handler))))
            .Should().NotThrow();

        _handler.Requests.Should().BeEmpty();
    }

    // A tick can only arrive after registration, so a missing context here is the
    // host calling out of order - worth surfacing rather than swallowing.
    [Fact]
    public async Task ThrowsWhenTickedBeforeInitialize()
    {
        using InternetRadioPlugin plugin = new();

        await plugin.Invoking(candidate => candidate.ExecuteAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RefreshTickFetchesAndWritesTheCache()
    {
        using InternetRadioPlugin plugin = Started();

        await plugin.ExecuteAsync(CancellationToken.None);

        File.Exists(Path.Combine(_folder, CatalogCache.FileName)).Should().BeTrue();
    }

    [Fact]
    public async Task ThrowsForAJobNameItDoesNotHave()
    {
        using InternetRadioPlugin plugin = Started();

        await plugin.Invoking(candidate => candidate.ExecuteAsync("nope", CancellationToken.None))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // A tick after Dispose is the host calling a plugin it already tore down.
    [Fact]
    public async Task ThrowsWhenTickedAfterDispose()
    {
        InternetRadioPlugin plugin = Started();
        plugin.Dispose();

        await plugin.Invoking(candidate => candidate.ExecuteAsync(CancellationToken.None))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    // A view request racing Dispose is NOT the same case: the host may still be
    // draining a page render while tearing down, so this answers with something
    // renderable rather than throwing into the request pipeline.
    [Fact]
    public async Task ServesARenderableViewAfterDispose()
    {
        InternetRadioPlugin plugin = Started();
        plugin.Dispose();

        PluginView view = await plugin.GetViewAsync(Request("/"), CancellationToken.None);

        view.Components.Should().Contain(node => node.Component == PluginComponentType.EmptyState);
    }

    // This page is the plugin's only diagnostic surface. Letting a failure throw
    // through it hides its own cause behind a broken page.
    [Fact]
    public async Task ServesAnErrorViewRatherThanThrowingWhenTheCatalogueCannotBeBuilt()
    {
        InternetRadioPlugin plugin = new();
        _handler.Fail(new HttpRequestException("down"));
        plugin.Initialize(new FakePluginContext(_folder, new HttpClient(_handler)));

        PluginView view = await plugin.GetViewAsync(Request("/"), CancellationToken.None);

        view.Components.Should().NotBeNullOrEmpty();
        plugin.Dispose();
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        InternetRadioPlugin plugin = Started();

        plugin.Dispose();
        plugin.Invoking(candidate => candidate.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void DisposeIsSafeBeforeInitialize()
    {
        InternetRadioPlugin plugin = new();

        plugin.Invoking(candidate => candidate.Dispose()).Should().NotThrow();
    }
}
```

- [ ] **Step 4: Run to verify failure**

```bash
dotnet test -c Release --filter "FullyQualifiedName~PluginLifecycleTests|FullyQualifiedName~DiscoveryContractTests"
```

Expected: FAIL — `InternetRadioPlugin` does not exist.

- [ ] **Step 5: Write `InternetRadioPlugin.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using Microsoft.Extensions.Logging;
using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// The class the server loads.
//
// Two contracts, one lifecycle. IUiPlugin serves five routes; IScheduledTaskPlugin
// keeps the catalogue current so a view never waits on the network when it can help
// it. Everything that touches IPluginContext lives here: the views are pure
// functions and the provider takes what it needs as arguments.
public sealed class InternetRadioPlugin : IUiPlugin, IScheduledTaskPlugin
{
    /// <summary>The single job's name. It appears in the server's job list as plugin:{id}:refresh.</summary>
    public const string RefreshJobName = "refresh";

    /// <summary>
    /// Daily, at a quiet hour. radio-browser is a volunteer-run service and this
    /// plugin has no reason to poll it harder than the catalogue actually changes.
    /// </summary>
    private const string DefaultCron = "0 4 * * *";

    private IPluginContext? _context;
    private CatalogProvider? _provider;
    private bool _disposed;

    // Field-initialised so Dispose has something to cancel even when the host
    // disposes a plugin whose load never completed. Every tick links this into the
    // token it runs under, which is what makes "Dispose cancels in-flight work" real
    // rather than aspirational.
    private readonly CancellationTokenSource _lifecycleCts = new();

    public string Name => PluginIdentity.Name;
    public string Description => PluginIdentity.Description;
    public Guid Id => PluginIdentity.Id;
    public Version Version => PluginIdentity.Version;

    // Captures the context and nothing else. No I/O, no network, no config read: a
    // plugin that throws from here fails to load, and Initialize is synchronous with
    // nowhere to await a fix. Real work belongs on the first view or the first tick.
    public void Initialize(IPluginContext context)
    {
        _context = context;
    }

    private IPluginContext Context =>
        _context ?? throw new InvalidOperationException("the plugin was used before Initialize");

    private CatalogProvider Provider =>
        _provider ??= new CatalogProvider(
            new RadioBrowserClient(Context.HttpClient),
            new CatalogCache(Context.DataFolderPath),
            Context.DataFolderPath,
            Context.Logger
        );

    // === IScheduledTaskPlugin ==============================================

    public string CronExpression => DefaultCron;

    // Read before Initialize by the host while it registers the plugin, so this must
    // not reach for the context. A constant cadence is the honest answer anyway:
    // there is no setting to read, because there is no way to save one.
    public IReadOnlyList<PluginScheduledJob> Jobs { get; } =
        [new PluginScheduledJob(RefreshJobName, DefaultCron)];

    public Task ExecuteAsync(CancellationToken ct = default) => ExecuteAsync(RefreshJobName, ct);

    public async Task ExecuteAsync(string jobName, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (jobName != RefreshJobName)
        {
            throw new ArgumentOutOfRangeException(nameof(jobName), jobName, "Unknown job name.");
        }

        IPluginContext context = Context;
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(ct, _lifecycleCts.Token);

        StationCatalog catalog = await Provider.RefreshAsync(linked.Token);

        context.Logger.LogInformation(
            "Internet Radio refreshed its catalogue: {Count} stations from {Source}.",
            catalog.Count,
            catalog.Source
        );
    }

    // === IUiPlugin =========================================================

    // One entry per manifest mount. DiscoveryContractTests asserts the two agree,
    // since PluginUiDescriptorDto prefers this over the manifest and nothing else
    // would catch them drifting.
    public IReadOnlyList<PluginNavEntry> NavEntries { get; } =
        [
            new PluginNavEntry
            {
                Section = PluginUiSection.Music,
                Label = PluginIdentity.Name,
                Icon = "portableRadio",
                Route = RadioRoutes.Browse,
            },
            new PluginNavEntry
            {
                Section = PluginUiSection.Settings,
                Label = PluginIdentity.Name,
                Icon = "portableRadio",
                Route = RadioRoutes.Settings,
            },
        ];

    public async Task<PluginView> GetViewAsync(PluginViewRequest request, CancellationToken ct)
    {
        // A view request racing Dispose is not the caller's bug the way a tick is:
        // the host may still be draining a page render while tearing down. Answer
        // with something renderable instead of throwing into the request pipeline.
        if (_disposed)
        {
            return PluginViews.Declarative(
                PluginViews.EmptyState(
                    "plugin-unavailable",
                    "Internet Radio is unavailable",
                    "This plugin is disabled or is being unloaded."
                )
            );
        }

        IPluginContext context = Context;
        RadioRoute route = RadioRoutes.Parse(request.Route);

        // Resolved before the switch so every route sees the same catalogue, and the
        // failure below covers building it as well as rendering from it.
        try
        {
            StationCatalog catalog = await Provider.GetAsync(ct);

            return route.Kind switch
            {
                RadioRouteKind.Browse => BrowseView.Build(catalog),
                RadioRouteKind.Genre => GenreView.Build(catalog, route.Value),
                RadioRouteKind.AllStations => AllStationsView.Build(catalog),
                RadioRouteKind.Station => StationView.Build(catalog, route.Value),
                RadioRouteKind.Settings => SettingsView.Build(
                    catalog, context.DataFolderPath, DateTimeOffset.UtcNow),
                _ => PluginViews.Declarative(
                    PluginViews.EmptyState(
                        "unknown-route",
                        "Nothing here",
                        "This version of Internet Radio has no page at that address."
                    )
                ),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // These pages are the plugin's only diagnostic surface, so a failure that
            // throws through them hides its own cause: the owner sees a broken panel
            // instead of learning what went wrong. The rendered text names what
            // failed and never the exception detail.
            context.Logger.LogError(exception, "Internet Radio could not build the view for {Route}.", request.Route);

            return PluginViews.Declarative(
                PluginViews.Container(
                    "view-error",
                    PluginViews.Badge("view-error-badge", "Unavailable", PluginBadgeVariant.Danger),
                    PluginViews.EmptyState(
                        "view-error-empty",
                        "This page could not be built",
                        "Check the server log for Internet Radio."
                    ),
                    PluginViews.Button("view-error-retry", "Try again", PluginActionIntent.RefreshView())
                )
            );
        }
    }

    // Null-safe before Initialize (the host may dispose a plugin whose load failed)
    // and idempotent (a double dispose is not worth throwing over).
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifecycleCts.Cancel();
        _lifecycleCts.Dispose();
    }
}
```

- [ ] **Step 6: Run tests and commit**

```bash
dotnet build -c Release -p:TreatWarningsAsErrors=true
dotnet test -c Release --no-build
git add -A
git commit -m "feat: serve the radio UI and refresh the catalogue on a schedule"
```

Expected: all pass.

---

### Task 13: The seed-checking script and the READMEs

`resolve-seeds.sh` is the only thing that verifies a stream really works, which matters because radio-browser's own liveness flag has been observed reporting a 404 as healthy.

**Files:**
- Create: `scripts/resolve-seeds.sh`
- Rewrite: `src/NoMercy.Plugin.InternetRadio/README.md` (ships beside the DLL)
- Create: `README.md` (repository root, for someone building or contributing)

**Interfaces:**
- Consumes: `SeedStations.Uuids` (read out of the C# source by the script).
- Produces: nothing the code depends on.

- [ ] **Step 1: Write `scripts/resolve-seeds.sh`**

```sh
#!/usr/bin/env sh
# Checks every pinned seed UUID: that radio-browser still has it, that it still
# passes the plugin's admission gates, and that its stream actually answers.
#
# That last check is the point. radio-browser's lastcheckok is a claim, not a fact:
# it reported Tomorrowland Anthems' OWR_DAB.mp3 as healthy while the URL was a 404,
# which is why that station had to be resubmitted. Nothing else in this repository
# connects to a stream.
#
# Run before tagging a release. Deliberately NOT on the push path - a station's
# outage is not a reason for this repository's build to go red.

set -eu

SEEDS_FILE="$(cd "$(dirname "$0")/.." && pwd)/src/NoMercy.Plugin.InternetRadio/Catalog/SeedStations.cs"
API="https://all.api.radio-browser.info"
UA="nomercy-radiostation-plugin/1.0.2"

command -v jq >/dev/null 2>&1 || { echo "jq is required" >&2; exit 1; }

# Read the UUIDs out of the C# rather than keeping a second copy here: two lists that
# could disagree would make this script's PASS meaningless.
uuids=$(grep -oE '"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"' "$SEEDS_FILE" \
        | tr -d '"' | sort -u)

if [ -z "$uuids" ]; then
    echo "no seed uuids found in $SEEDS_FILE" >&2
    exit 1
fi

count=$(printf '%s\n' "$uuids" | wc -l | tr -d ' ')
echo "checking $count seed stations"
echo

joined=$(printf '%s\n' "$uuids" | paste -sd, -)
records=$(curl -fsS -m 60 -A "$UA" -X POST "$API/json/stations/byuuid" -d "uuids=$joined")

failures=0

for uuid in $uuids; do
    record=$(printf '%s' "$records" | jq -c --arg u "$uuid" '.[] | select(.stationuuid == $u)')

    if [ -z "$record" ]; then
        printf 'MISSING  %s  (radio-browser no longer has this station)\n' "$uuid"
        failures=$((failures + 1))
        continue
    fi

    name=$(printf '%s' "$record" | jq -r '.name')
    url=$(printf '%s' "$record" | jq -r '.url_resolved // .url')
    hls=$(printf '%s' "$record" | jq -r '.hls')
    ok=$(printf '%s' "$record" | jq -r '.lastcheckok')

    gate=""
    case "$url" in https://*) ;; *) gate="$gate not-https" ;; esac
    [ "$hls" = "0" ] || gate="$gate hls"
    [ "$ok" = "1" ] || gate="$gate not-checked"

    if [ -n "$gate" ]; then
        printf 'GATED    %-42s %s (%s)\n' "$name" "$uuid" "$gate"
        failures=$((failures + 1))
        continue
    fi

    # The part radio-browser cannot be trusted for. A range request takes the first
    # couple of kilobytes and hangs up rather than streaming indefinitely.
    status=$(curl -s -m 20 -A "$UA" -L -r 0-2047 -o /dev/null -w '%{http_code}' "$url" || echo 000)

    case "$status" in
        200|206) printf 'OK       %-42s %s\n' "$name" "$uuid" ;;
        *)
            printf 'DEAD     %-42s %s (stream returned %s)\n' "$name" "$uuid" "$status"
            failures=$((failures + 1))
            ;;
    esac
done

echo
if [ "$failures" -gt 0 ]; then
    echo "$failures of $count seeds need attention before release" >&2
    exit 1
fi

echo "all $count seeds resolve, pass the gates, and answer"
```

- [ ] **Step 2: Make it executable and run it**

```bash
chmod +x scripts/resolve-seeds.sh
./scripts/resolve-seeds.sh
```

Expected: `OK` for all ten. Any `DEAD` or `GATED` line must be resolved before release — by finding the station's current record on radio-browser and repinning, or by submitting the working stream there as was done for Anthems.

- [ ] **Step 3: Rewrite the plugin README**

`src/NoMercy.Plugin.InternetRadio/README.md` — this one ships inside the zip, beside the DLL, so it answers what the thing does and what it declares, not how to build it.

````markdown
# Internet Radio

Browse and play internet radio stations in the NoMercy MediaServer's built-in
player.

Adds two entries to the dashboard: **Internet Radio** under Music, and a
read-only status page under plugin settings.

## What it does

- Fetches its station catalogue from [radio-browser.info](https://www.radio-browser.info/)
  — ten curated stations pinned by id, plus the most popular stations in each of
  seventeen genres.
- Browse by genre, or scan every station in one table with bitrate and codec.
- Selecting a station plays it immediately in the built-in player. A station's own
  page also offers **Add to queue** and a link to its homepage.

## What it declares

| Capability | Why |
| --- | --- |
| `ui` | The five pages above. |
| `scheduledTask` | One job, `refresh`, daily at 04:00, which updates the catalogue. |
| `network` → `*.api.radio-browser.info` | The only host it contacts. Streams are played by your client, not by the server. |

It declares no `rest`, no `ws`, no library access and no secrets storage.

**You will need to enable it once.** A plugin that declares a network host is not
auto-enabled however `autoEnabled` is set, so the server starts it disabled until
you approve it in the dashboard. That is deliberate on the server's part, and
correct: this plugin calls a third-party API on a schedule.

## Stations it will not have

Only HTTPS, non-HLS streams are admitted. Your dashboard is served over HTTPS, so
a plain `http://` stream is blocked by the browser as mixed content and cannot
play at all — listing one would be listing something that does not work.

This is why **BBC Radio 1 and BBC Radio 6 Music are absent**: radio-browser carries
them only as HLS over `http://`. Earlier versions of this plugin shipped BBC URLs
that could never play in a browser for exactly that reason.

## Using your own station list

Drop a file named `stations.json` into the plugin's data folder — the settings page
shows you the exact path — and it replaces the fetched catalogue entirely:

```json
[
  {
    "name": "Local Jazz FM",
    "streamUrl": "https://example.com/jazz.aac",
    "logoUrl": "https://example.com/jazz.png",
    "homepage": "https://example.com/",
    "genre": "Jazz",
    "country": "US",
    "bitrateKbps": 256,
    "codec": "aac"
  }
]
```

Only `name` and `streamUrl` are required. Your file is used exactly as written and
is **not** filtered, so it is also how you add a station radio-browser does not
carry. If it cannot be parsed, the plugin logs a warning and fetches as normal.

## There is nothing to configure

The settings page is read-only, and not by choice. A plugin cannot currently
receive anything from its own UI on this server: plugin REST routes are served
unversioned while the dashboard posts to `/api/v1`
([media-server issue #26](https://github.com/NoMercy-Entertainment/nomercy-media-server/issues/26)),
and the hub is not an alternative because plugin hub handlers are never registered.
Favourites and station editing arrive when either is fixed.

## License

MIT.
````

- [ ] **Step 4: Write the repository README**

`README.md` at the repository root — for someone building or contributing.

````markdown
# nomercy-radiostation-plugin

An `IUiPlugin` for [NoMercy MediaServer](https://github.com/NoMercy-Entertainment/nomercy-media-server)
that browses and plays internet radio. See
[the plugin's own README](src/NoMercy.Plugin.InternetRadio/README.md) for what it
does and how to use it.

## Building

Requires the .NET 10 SDK (pinned in `global.json`).

`NoMercy.Plugins.Abstractions` is not published to nuget.org, so it is cloned and
packed into a local feed first. `nuget.config` already points at that feed, and
`packageSourceMapping` pins `NoMercy.*` to it so nobody can publish that name on
nuget.org and get their assembly compiled in instead.

```bash
./scripts/fetch-abstractions.sh     # or scripts/fetch-abstractions.ps1
dotnet restore
dotnet build -c Release -p:TreatWarningsAsErrors=true
dotnet test -c Release --no-build
```

## Layout

| Path | |
| --- | --- |
| `src/NoMercy.Plugin.InternetRadio/Catalog/` | Fetching, gating and caching the station list |
| `src/NoMercy.Plugin.InternetRadio/Views/` | Pure `Build(...)` functions returning a `PluginView` |
| `tests/` | xunit + FluentAssertions; no test touches the network |
| `scripts/resolve-seeds.sh` | Checks the pinned stations still resolve and still answer |
| `docs/superpowers/` | The design spec and this implementation plan |

## No station data in the source tree

Names, stream URLs, logos, genres and countries are all fetched at runtime. The
only station data committed here is ten radio-browser UUIDs in
`Catalog/SeedStations.cs`.

That is not tidiness. A hardcoded URL is one nobody re-checks: this repository has
already had to correct Tomorrowland URLs once, and shipped BBC streams over `http://`
that could never play in a browser. Anything wrong with a station is now fixed
upstream at radio-browser, where the fix reaches everyone.

Run `scripts/resolve-seeds.sh` before tagging a release.

## Releasing

CI builds on every push and creates a Forgejo release on a `v*` tag.

**The manifest version must match the tag.** The build asserts
`v{plugin.json version} == {tag}` and fails naming both if not — `v1.0.1` was once
tagged on a commit whose manifest read `1.0.0`, so every server that installed it
reported the wrong version and was told an update was available forever. After a
release publishes, CI opens the next patch version on the default branch.

A release carries the plugin zip, its SHA-256, and a `repository.json` that a plugin
catalogue can point at directly.

## License

MIT.
````

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "docs: document the plugin, and add the seed verification script"
```

---

### Task 14: CI — the version gate, the checksum, and the catalogue manifest

The build that makes another 1.0.1-labelled-1.0.0 impossible.

**Files:**
- Rewrite: `.forgejo/workflows/build.yml`

**Interfaces:**
- Consumes: `scripts/fetch-abstractions.sh`, `src/NoMercy.Plugin.InternetRadio/plugin.json`.
- Produces: a release with `NoMercy.Plugin.InternetRadio-{version}.zip`, its SHA-256, and `repository.json`.

- [ ] **Step 1: Replace the workflow**

```yaml
name: build

# Builds, tests and packages the Internet Radio plugin.
#
# The plugin builds against NoMercy.Plugins.Abstractions, which is NOT published to
# nuget.org. scripts/fetch-abstractions.sh clones the server and packs it into a
# local feed that the committed nuget.config points at. The script is shared with
# local development on purpose, so CI and a developer's machine cannot drift.
#
# Build, test and release run in one job so we don't need the artifact
# upload/download API, which the act_runner routes through the internal
# `forgejo:3000` hostname the per-workflow network can't resolve.

on:
  push:
    branches: [ main, master ]
    tags:     [ 'v*' ]
  pull_request:
  workflow_dispatch:

permissions:
  contents: write

env:
  PLUGIN_NAME: NoMercy.Plugin.InternetRadio
  PLUGIN_DIR: src/NoMercy.Plugin.InternetRadio
  SERVER_BRANCH: dev
  DOTNET_CHANNEL: "10.0"
  DOTNET_NOLOGO: "1"
  DOTNET_CLI_TELEMETRY_OPTOUT: "1"
  # Public URL — the runner's default GITHUB_SERVER_URL is an internal hostname
  # (http://forgejo:3000) the workflow container can't resolve.
  PUBLIC_FORGEJO_URL: https://forgejo.phillippepelzer.me

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      # Runs on a host runner, so root is not a given and the image may already
      # carry these. Install only what is missing, and only if we can.
      - name: Install base tooling
        run: |
          set -eu
          missing=""
          for tool in git curl zip unzip jq; do
            command -v "$tool" >/dev/null 2>&1 || missing="$missing $tool"
          done

          if [ -z "$missing" ]; then
            echo "all required tools present"
            exit 0
          fi

          echo "missing:$missing"
          if [ "$(id -u)" = "0" ]; then
            SUDO=""
          elif command -v sudo >/dev/null 2>&1; then
            SUDO="sudo"
          else
            echo "cannot install$missing - not root and no sudo available" >&2
            exit 1
          fi

          $SUDO apt-get update -y
          $SUDO apt-get install -y --no-install-recommends ca-certificates libicu-dev $missing

      - name: Install .NET SDK ${{ env.DOTNET_CHANNEL }}
        run: |
          set -eux
          curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
          chmod +x /tmp/dotnet-install.sh
          /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --install-dir "$HOME/.dotnet"
          echo "DOTNET_ROOT=$HOME/.dotnet" >> "$GITHUB_ENV"
          echo "$HOME/.dotnet" >> "$GITHUB_PATH"
          "$HOME/.dotnet/dotnet" --info

      # Manual checkout instead of actions/checkout: the action clones from
      # $GITHUB_SERVER_URL, which the runner sets to an internal hostname.
      - name: Checkout plugin (via public URL)
        env:
          TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          # NO `set -x` IN THIS STEP. The auth header is base64 of the token, and the
          # runner's secret masking only redacts the token's literal text - it cannot
          # recognise an encoded form. With xtrace on, the token lands in the build
          # log in full, which for a public repo is a publicly readable credential.
          set -eu
          : "${TOKEN:?missing GITHUB_TOKEN}"
          git config --global --add safe.directory "$GITHUB_WORKSPACE"
          cd "$GITHUB_WORKSPACE"
          shopt -s dotglob nullglob
          rm -rf -- *
          git init -q .
          git remote add origin "${PUBLIC_FORGEJO_URL}/${GITHUB_REPOSITORY}.git"

          # Written to a file rather than passed with `git -c`: an argument is visible
          # in the host's process list, which on a self-hosted runner means any
          # co-tenant process. Removed immediately afterwards.
          umask 077
          printf 'Authorization: basic %s\n' \
            "$(printf 'x-access-token:%s' "$TOKEN" | base64 -w0)" > /tmp/gh-auth
          git config --local http.extraHeader "$(cat /tmp/gh-auth)"
          rm -f /tmp/gh-auth

          git fetch --depth=1 origin "$GITHUB_SHA"
          git config --local --unset-all http.extraHeader
          git checkout -q FETCH_HEAD
          git log --oneline -1

      # THE GATE. This repository shipped v1.0.1 from a commit whose manifest read
      # 1.0.0: every server that installed it reported the wrong version and was told
      # an update was available forever. Nothing checked, so nothing failed.
      #
      # Runs before the build, so a mismatched tag costs seconds rather than a full
      # build and a published-then-deleted release.
      - name: Assert the manifest version matches the tag
        if: startsWith(github.ref, 'refs/tags/v')
        run: |
          set -eu
          MANIFEST_VERSION=$(jq -r '.version' "$PLUGIN_DIR/plugin.json")
          TAG="${GITHUB_REF_NAME}"

          if [ "v$MANIFEST_VERSION" != "$TAG" ]; then
            echo "::error::tag $TAG does not match plugin.json version $MANIFEST_VERSION" >&2
            echo "the manifest and the tag must agree, or an installed server reports" >&2
            echo "the wrong version and is offered an update it already has." >&2
            echo "fix: set \"version\": \"${TAG#v}\" in $PLUGIN_DIR/plugin.json," >&2
            echo "     <Version> in the csproj, and PluginIdentity.Version - then retag." >&2
            exit 1
          fi

          echo "tag $TAG matches manifest version $MANIFEST_VERSION"

      - name: Pack the plugin contract to a local feed
        id: contract
        run: |
          set -eu
          chmod +x scripts/fetch-abstractions.sh
          ./scripts/fetch-abstractions.sh

          # Recorded so a release names the exact contract it was built against.
          # "built against @dev" says nothing a year later.
          SERVER_SHA=$(git -C _server rev-parse HEAD)
          echo "sha=$SERVER_SHA" >> "$GITHUB_OUTPUT"

      - name: Restore
        run: dotnet restore

      # TreatWarningsAsErrors belongs on the step that compiles. Passing it to
      # `dotnet test --no-build` does nothing: that command compiles nothing.
      - name: Build (Release, warnings as errors)
        run: dotnet build -c Release --no-restore -p:TreatWarningsAsErrors=true

      # A plugin that fails its own tests must not produce a release artifact.
      - name: Test (Release)
        run: dotnet test -c Release --no-build --logger "console;verbosity=normal"

      # The constraint that stops station data creeping back into the source tree.
      # Only the radio-browser API base and documentation links may contain a URL.
      - name: Assert no station data is hardcoded
        run: |
          set -eu
          if grep -rn --include='*.cs' -E 'https?://' "$PLUGIN_DIR" \
             | grep -v 'radio-browser' \
             | grep -v 'forgejo.phillippepelzer.me' \
             | grep -v 'github.com'; then
            echo "::error::a URL that is not the API base or a documentation link" >&2
            echo "station data is fetched, never committed - see Catalog/SeedStations.cs" >&2
            exit 1
          fi
          echo "no hardcoded station URLs"

      - name: Stage the plugin directory
        id: stage
        run: |
          set -eux
          BIN="$PLUGIN_DIR/bin/Release/net10.0"
          STAGING="$PWD/_stage/$PLUGIN_NAME"
          mkdir -p "$STAGING"

          cp "$BIN/$PLUGIN_NAME.dll"      "$STAGING/"
          cp "$BIN/$PLUGIN_NAME.deps.json" "$STAGING/"
          cp "$PLUGIN_DIR/plugin.json"     "$STAGING/"
          # The plugin's own README, not the repository's: what belongs beside an
          # installed DLL is what the thing does, not how to build it.
          cp "$PLUGIN_DIR/README.md"       "$STAGING/"
          cp LICENSE                       "$STAGING/"

          # Deliberately NOT shipped: NoMercy.Plugins.Abstractions.dll and
          # NoMercy.Events.dll. Both are the host's and live in its shared-assembly
          # set. Shipping a copy gives the load context two incompatible identities of
          # the same types, and the failure looks like an unrelated cast error far
          # from its cause.
          #
          # Checked against $BIN, not $STAGING: staging holds exactly the five files
          # copied above by name, so neither could ever appear there and the check
          # could never fire.
          if ls "$BIN" | grep -E '^NoMercy\.(Plugins\.Abstractions|Events)\.dll$'; then
            echo "::error::a host-owned assembly was copied into the build output" >&2
            echo "see the PackageReference comment in the plugin csproj" >&2
            exit 1
          fi

          # The staged list is an allowlist, so an assembly added later would be named
          # in deps.json, missing from the zip, and the plugin would fail to load with
          # a FileNotFoundException while CI went green.
          #
          # Scoped to assemblies built from THIS repo, which deps.json distinguishes:
          # a project reference gets a bare filename key, a package gets a path key
          # like lib/net10.0/Foo.dll. Only the first group is ours to ship.
          command -v jq >/dev/null 2>&1 || { echo "jq is required" >&2; exit 1; }

          PROJECT_ASSEMBLIES=$(jq -r '.targets[] | to_entries[] | .value.runtime // {} | keys[]' \
                                "$STAGING/$PLUGIN_NAME.deps.json" | grep -v '/' | sort -u)
          if [ -z "$PROJECT_ASSEMBLIES" ]; then
            echo "::error::deps.json listed no project-built assemblies - its shape changed" >&2
            exit 1
          fi

          MISSING=""
          for dep in $PROJECT_ASSEMBLIES; do
            [ -f "$STAGING/$dep" ] || MISSING="$MISSING $dep"
          done
          if [ -n "$MISSING" ]; then
            echo "::error::deps.json names project-built assemblies missing from the artifact:$MISSING" >&2
            exit 1
          fi

          REF="${GITHUB_REF_NAME:-}"
          case "$REF" in
            v*) VERSION="${REF#v}" ;;
            *)  VERSION="dev-${GITHUB_SHA:0:8}" ;;
          esac

          ZIP="$PLUGIN_NAME-$VERSION.zip"
          (cd "$PWD/_stage" && zip -r "../$ZIP" "$PLUGIN_NAME")

          # A catalogue needs this to verify a download, and it is what makes a
          # listing checkable rather than trusted.
          SHA=$(sha256sum "$ZIP" | cut -d' ' -f1)

          echo "version=$VERSION" >> "$GITHUB_OUTPUT"
          echo "zip=$ZIP"         >> "$GITHUB_OUTPUT"
          echo "sha=$SHA"         >> "$GITHUB_OUTPUT"
          echo "sha256 $SHA  $ZIP"

      # One URL a plugin catalogue can point at, instead of someone hand-writing the
      # entry and hand-checking the version - which is exactly how the 1.0.0/1.0.1
      # mismatch became somebody else's problem.
      - name: Build repository.json
        if: startsWith(github.ref, 'refs/tags/v')
        run: |
          set -eu
          MANIFEST="$PLUGIN_DIR/plugin.json"
          ZIP="${{ steps.stage.outputs.zip }}"
          DOWNLOAD="${PUBLIC_FORGEJO_URL}/${GITHUB_REPOSITORY}/releases/download/${GITHUB_REF_NAME}/${ZIP}"

          jq -n \
            --slurpfile manifest "$MANIFEST" \
            --arg version "${{ steps.stage.outputs.version }}" \
            --arg download "$DOWNLOAD" \
            --arg checksum "sha256:${{ steps.stage.outputs.sha }}" \
            --arg timestamp "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
            '{
              name: "NoMercy Internet Radio",
              url: $download,
              plugins: [{
                id: $manifest[0].id,
                name: $manifest[0].name,
                description: $manifest[0].description,
                author: $manifest[0].author,
                projectUrl: $manifest[0].projectUrl,
                versions: [{
                  version: $version,
                  targetAbi: $manifest[0].targetAbi,
                  downloadUrl: $download,
                  checksum: $checksum,
                  timestamp: $timestamp
                }]
              }]
            }' > repository.json

          cat repository.json

      - name: Create Forgejo release & attach assets
        if: startsWith(github.ref, 'refs/tags/v')
        env:
          TOKEN: ${{ secrets.GITHUB_TOKEN }}
          CONTRACT_SHA: ${{ steps.contract.outputs.sha }}
        run: |
          set -euo pipefail
          ZIP="${{ steps.stage.outputs.zip }}"
          SHA="${{ steps.stage.outputs.sha }}"
          TAG="${GITHUB_REF_NAME}"
          REPO="${GITHUB_REPOSITORY}"
          API="${PUBLIC_FORGEJO_URL%/}/api/v1"

          BODY=$(cat <<EOF
          ## Internet Radio — ${TAG}

          Browse and play internet radio stations in the NoMercy MediaServer's built-in player.

          ### Install
          1. Extract \`${ZIP}\` into \`<server>/plugins/\`.
          2. Restart the server.
          3. **Enable the plugin in the dashboard.** It declares a network host, so the
             server starts it disabled until you approve it.

          ### Verify
          \`\`\`
          sha256  ${SHA}
          \`\`\`

          Stations are fetched from radio-browser.info and refreshed daily; nothing is
          bundled. HTTPS, non-HLS streams only — an http stream is blocked by the browser
          as mixed content and cannot play.

          Built and tested against \`NoMercy-Entertainment/nomercy-media-server@${CONTRACT_SHA}\`.
          To rebuild this exact artifact: \`SERVER_REF=${CONTRACT_SHA} ./scripts/fetch-abstractions.sh\`
          then \`dotnet build -c Release\`.
          EOF
          )

          # The token goes in a config file, not on the command line: a curl argument
          # is visible in the host's process list to any co-tenant process.
          umask 077
          CURL_CFG=$(mktemp)
          trap 'rm -f "$CURL_CFG"' EXIT
          printf 'header = "Authorization: token %s"\n' "$TOKEN" > "$CURL_CFG"

          REL_JSON=$(curl -fsSL --config "$CURL_CFG" -X POST \
            -H "Content-Type: application/json" \
            "$API/repos/$REPO/releases" \
            -d "$(jq -n --arg tag "$TAG" --arg body "$BODY" \
                  '{tag_name:$tag, name:$tag, body:$body, draft:false, prerelease:false}')")

          REL_ID=$(echo "$REL_JSON" | jq -r '.id')
          if [ -z "$REL_ID" ] || [ "$REL_ID" = "null" ]; then
            echo "::error::release creation returned no id - refusing to upload an asset" >&2
            exit 1
          fi

          for asset in "$ZIP" repository.json; do
            echo "uploading $asset"
            curl -fsSL --config "$CURL_CFG" -X POST \
              -H "Content-Type: application/octet-stream" \
              --data-binary "@$asset" \
              "$API/repos/$REPO/releases/$REL_ID/assets?name=$(basename "$asset")"
          done

          echo "Release published: $TAG"

      # Opens the next patch version so the default branch is never sitting on a
      # version that has already shipped.
      #
      # This is convenience, not the guard: by the time it runs the artifact is
      # published, so it cannot protect anything. The gate at the top of this job is
      # what makes a mismatched release impossible.
      - name: Open the next patch version
        if: startsWith(github.ref, 'refs/tags/v')
        env:
          TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          set -eu
          RELEASED="${GITHUB_REF_NAME#v}"
          MAJOR=$(echo "$RELEASED" | cut -d. -f1)
          MINOR=$(echo "$RELEASED" | cut -d. -f2)
          PATCH=$(echo "$RELEASED" | cut -d. -f3)
          NEXT="$MAJOR.$MINOR.$((PATCH + 1))"

          echo "opening $NEXT for development"

          # The tag build checked out a detached FETCH_HEAD, so the edits have to be
          # made ON the default branch - editing here first and then checking out
          # would simply discard them.
          BRANCH="${GITHUB_EVENT_REPOSITORY_DEFAULT_BRANCH:-main}"
          git config user.name  "forgejo-actions"
          git config user.email "actions@noreply.localhost"

          umask 077
          printf 'Authorization: basic %s\n' \
            "$(printf 'x-access-token:%s' "$TOKEN" | base64 -w0)" > /tmp/gh-auth
          git config --local http.extraHeader "$(cat /tmp/gh-auth)"
          rm -f /tmp/gh-auth

          git fetch --depth=1 origin "$BRANCH"
          git checkout -q -B "$BRANCH" FETCH_HEAD

          # All three, or ManifestTests fails on the very next push.
          jq --arg v "$NEXT" '.version = $v' "$PLUGIN_DIR/plugin.json" > /tmp/manifest.json
          mv /tmp/manifest.json "$PLUGIN_DIR/plugin.json"
          sed -i "s|<Version>$RELEASED</Version>|<Version>$NEXT</Version>|" \
            "$PLUGIN_DIR/$PLUGIN_NAME.csproj"
          sed -i "s|new($MAJOR, $MINOR, $PATCH)|new($MAJOR, $MINOR, $((PATCH + 1)))|" \
            "$PLUGIN_DIR/PluginIdentity.cs"

          git add "$PLUGIN_DIR/plugin.json" "$PLUGIN_DIR/$PLUGIN_NAME.csproj" \
                  "$PLUGIN_DIR/PluginIdentity.cs"

          if git diff --cached --quiet; then
            echo "already at $NEXT - nothing to do"
          else
            # [skip ci] or this push retriggers the workflow for no reason.
            git commit -m "chore(release): open $NEXT for development [skip ci]"
            git push origin "HEAD:$BRANCH"
          fi

          git config --local --unset-all http.extraHeader
```

- [ ] **Step 2: Verify the gate locally**

The gate is the point of this task, so prove it fires before trusting it.

```bash
# Should print the version and succeed.
jq -r '.version' src/NoMercy.Plugin.InternetRadio/plugin.json

# Simulate the comparison the workflow makes, with a deliberately wrong tag.
MANIFEST_VERSION=$(jq -r '.version' src/NoMercy.Plugin.InternetRadio/plugin.json)
TAG="v9.9.9"
[ "v$MANIFEST_VERSION" != "$TAG" ] && echo "gate correctly rejects $TAG"

TAG="v$MANIFEST_VERSION"
[ "v$MANIFEST_VERSION" = "$TAG" ] && echo "gate correctly accepts $TAG"
```

- [ ] **Step 3: Verify the hardcoded-URL check passes**

```bash
grep -rn --include='*.cs' -E 'https?://' src/NoMercy.Plugin.InternetRadio \
  | grep -v 'radio-browser' | grep -v 'forgejo.phillippepelzer.me' | grep -v 'github.com' \
  && echo "FAIL: a URL slipped in" || echo "OK: no hardcoded station URLs"
```

Expected: `OK`.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "ci: gate the tag against the manifest, publish a checksum and repository.json"
```

---

### Task 15: File the upstream findings (requires the boss's go-ahead)

Four things were found while building this that are the server's or the web client's,
not this plugin's. None is worked around here: a hand-rolled versioned route or a
private ingest path would break the moment the real fix lands.

**This task is outward-facing — do not file anything until explicitly approved.**

**Files:** none in this repository.

- [ ] **Step 1: Confirm approval before filing**

- [ ] **Step 2: File each finding on `NoMercy-Entertainment/nomercy-media-server`**

1. **`IPluginHubRouter.Register` is never called.** `PluginHubRouter` keeps a handler
   dictionary that nothing populates, so `RouteAsync` drops every message at its first
   lookup and `IPluginHubHandler` cannot receive anything. It matters most because the
   hub is the natural workaround for issue #26 and is not available either — so there
   is currently *no* inbound path from a plugin's UI to the plugin.
2. **`IMediaSourcePlugin` has no consumer.** It appears in the whole server only in its
   own declaration and one abstractions test. A plugin author implements it and gets
   silence. Either wire it or say it is not yet live. (This plugin's previous release
   implemented only that hook, which is why it did nothing at all.)
3. **`PluginText` has an unnamed variant vocabulary.** The web renderer knows `title`,
   `subtitle` and `caption`; anything else silently reads as body text. This is exactly
   the failure `PluginComponentType` and `PluginActionType` exist to prevent, and
   `nomercy-torrent-plugin` already mis-hits it — its settings headings render as
   paragraphs. Suggest a `PluginTextVariant` constants class plus a doc comment on
   `PluginViews.Text`.
4. **The web host never forwards `PluginViewRequest.Query`.** The server populates it
   from the request, but `Host/index.vue` sends only `route`, so a plugin using query
   parameters loses them with no error. Either forward them, or state in
   `PluginViewRequest` that path segments are the portable option.

- [ ] **Step 3: Link the filed issues into the spec's *Upstream reports* section**

---

## Final verification

Run before declaring the work done. This is the spec's definition of done.

- [ ] **All green from clean**

```bash
rm -rf _server _nupkgs
./scripts/fetch-abstractions.sh
dotnet restore
dotnet build -c Release -p:TreatWarningsAsErrors=true
dotnet test -c Release --no-build
```

- [ ] **All ten seeds resolve and answer**

```bash
./scripts/resolve-seeds.sh
```

Expected: ten `OK` lines, including all four Tomorrowland stations.

- [ ] **No station data in the source tree**

```bash
grep -rn --include='*.cs' -E 'https?://' src/NoMercy.Plugin.InternetRadio \
  | grep -v 'radio-browser' | grep -v 'forgejo.phillippepelzer.me' | grep -v 'github.com'
```

Expected: no output.

- [ ] **The three version declarations agree, and read 1.0.2**

```bash
jq -r '.version' src/NoMercy.Plugin.InternetRadio/plugin.json
grep -o '<Version>[^<]*</Version>' src/NoMercy.Plugin.InternetRadio/*.csproj
grep -o 'new(1, 0, 2)' src/NoMercy.Plugin.InternetRadio/PluginIdentity.cs
```

- [ ] **Push and tag only on explicit ask**, then watch CI.

```bash
git push origin main
git tag v1.0.2 && git push origin v1.0.2
```
