# SDK Conformance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Internet Radio 2.0.0 use the SDK the platform ships — narrow hosts with runtime grants, an install route that keeps the server's checks, `PluginViews` with a route table, and translations that work — without changing what a listener sees.

**Architecture:** The manifest stops granting the whole internet and one helper asks for what the plugin actually needs, up front, from the refresh job. `IUiPlugin.Routes` declares the seven pages with labels, layouts and access. `Ui.cs` shrinks to the two app components the SDK has no equivalent for; every other component comes from `PluginViews`, which is also what makes the shipped language files resolve. CI publishes a repository index so the server installs the plugin with its checksum checked and members can see it.

**Tech Stack:** C# / .NET 10, `NoMercy.PluginSdk.Abstractions` + `NoMercy.PluginSdk.Mvc` 12.1, xunit 2 + FluentAssertions 7, Forgejo Actions.

**Spec:** `docs/superpowers/specs/2026-09-23-sdk-conformance-design.md`. Read it before starting any task; it says why each change exists and what was deliberately left alone.

## Global Constraints

- **Branch:** all work happens on `feat/abi-12`, on top of commit `bfe092e`. Commit locally after each task. **Never push** unless the owner asks.
- **The version stays `2.0.0`.** It has never been released. Do not bump anything.
- **Run dotnet as `/c/Users/philp/.dotnet/dotnet.exe`** (Git Bash). Plain `dotnet` here is SDK 8 and cannot build net10.0.
- **Never start a NoMercy server on this machine**, in any form. Testing against a real server happens by deploying to the owner's server with `scripts/deploy-to-server.sh`, and only when the owner asks.
- **Never run `scripts/fetch-abstractions.*` with `SERVER_DIR` unset to a sibling path.** Its default `_server/` is correct; leave it alone.
- **TreatWarningsAsErrors is true** for both projects. 0 warnings, 0 errors is the bar.
- **The plugin id is `5KTKRT4Z2Y9P59Y40W5CX4TQKF` and never changes.**
- **No URL literal may appear in any `.cs` file under `src/`**, not even in a comment; CI greps for `https?://` and allows only lines containing `radio-browser`, `forgejo.phillippepelzer.me` or `github.com`. Tests may use `example.com`.
- **Views stay pure.** Nothing under `Views/` may touch `IPluginContext`, the network, the clock or the file system.
- **SPDX header on every new file:**
  ```csharp
  // SPDX-License-Identifier: MIT
  // Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84
  ```
- **Comments explain why, in full sentences, as the surrounding code does. No commented-out code.** A comment that no longer describes the code is a defect: rewrite it or delete it.
- **Commit messages:** Conventional Commits, in the repository's voice. **No attribution, co-author or "generated" trailers of any kind.**
- **The SDK is readable at `_server/src/NoMercy.PluginSdk.Abstractions/`.** When this plan names a member, verify it there before using it; if it differs, follow the SDK and say so in your report.
- **Reference plugin:** `../nomercy-automix-plugin/src/NoMercy.Plugin.Automix/` is the platform author's own ABI 12.1 plugin. When in doubt about house style for the SDK, copy it. `../nomercy-torrent-plugin` is NOT an independent reference for view code — its `Ui.cs` was copied from this repository.

## File Structure

| File | Responsibility | Task |
| --- | --- | --- |
| `src/.../plugin.json` | Narrow hosts; `license`, `docs`; `kind`, `access`, `slot` on mounts; label keys | 1 |
| `src/.../Hosting/StationGrants.cs` (new) | Asks for the two grants the plugin needs, once, before they are refused | 2 |
| `src/.../InternetRadioPlugin.cs` | Calls the grants helper from the refresh job; declares `Routes`; owner access on the settings nav entry | 2, 3 |
| `src/.../Playback/HostStationStreams.cs` | Stops asking lazily; keeps refusing gracefully | 2 |
| `src/.../Views/RadioRoutes.cs` | Gains the route table's paths as constants it already owns | 3 |
| `src/.../Views/Ui.cs` | Shrinks to `MediaGrid` + `MediaCard` | 4 |
| `src/.../Views/{Browse,AllStations,Genre,Search,Station,Settings}View.cs`, `EmptyCatalog.cs`, `StationCards.cs` | Built with `PluginViews` | 4 |
| `src/.../lang/en.json`, `lang/nl.json` | Every string a view or a mount draws | 5 |
| `.forgejo/workflows/build.yml` | `repository.json` in the shape the server reads | 6 |
| `README.md`, `src/.../README.md`, `docs/superpowers/specs/2026-09-22-abi-12-migration-design.md` | The repository install route; the correction of the hand-extraction advice | 6 |

---

### Task 1: The manifest asks for what it needs

**Files:**
- Modify: `src/NoMercy.Plugin.InternetRadio/plugin.json`
- Test: `tests/NoMercy.Plugin.InternetRadio.Tests/ManifestTests.cs`

**Interfaces:**
- Produces: a manifest whose `network.hosts` is `["*.api.radio-browser.info"]` and whose mounts carry `kind`, `access` and (settings) `slot`. Task 2 relies on the narrowed hosts; Task 5 relies on the label keys.

- [ ] **Step 1: Write the failing tests**

Add to `ManifestTests.cs`:

```csharp
    // The wildcard is what IPluginGrants exists to replace: a plugin that cannot name its
    // hosts at package time asks the owner at runtime instead. radio-browser is the one
    // host this plugin does know, and `*` matches within one label, so this covers all.,
    // de1. and every other mirror.
    [Fact]
    public void Manifest_NamesRadioBrowserAndNothingWider()
    {
        LoadManifest().Capabilities!.Network!.Hosts
            .Should().Equal("*.api.radio-browser.info");
    }

    // A settings page is the owner's. Offering it to a member is offering a page that
    // answers 403.
    [Fact]
    public void Manifest_KeepsTheSettingsMountForTheOwner()
    {
        PluginUiMount settings = LoadManifest().Capabilities!.Ui!.Mounts
            .Single(mount => mount.Section == PluginUiSection.Settings);

        settings.Access.Should().Be(PluginRouteAccess.Owner);
        settings.Kind.Should().Be("settings");
        settings.Slot.Should().Be(PluginSlot.Section);
    }

    [Fact]
    public void Manifest_DeclaresTheMusicMountAsMusic()
    {
        PluginUiMount music = LoadManifest().Capabilities!.Ui!.Mounts
            .Single(mount => mount.Section == PluginUiSection.Music);

        music.Access.Should().Be(PluginRouteAccess.Shared);
        music.Kind.Should().Be("music");
    }

    // The CLI's manifest scan reports a label with a space in it as Degraded and asks for
    // a translation key: the label is drawn in the viewer's own language.
    [Fact]
    public void Manifest_NamesItsMountsByTranslationKey()
    {
        LoadManifest().Capabilities!.Ui!.Mounts
            .Should().OnlyContain(mount => !mount.Label.Contains(' '));
    }

    [Fact]
    public void Manifest_CarriesItsLicenceAndDocs()
    {
        PluginManifest manifest = LoadManifest();

        manifest.License.Should().Be("MIT");
        manifest.Docs.Should().NotBeNullOrWhiteSpace();
    }
```

Before writing these, confirm in `_server/src/NoMercy.PluginSdk.Abstractions/PluginCapabilities.cs` that `PluginUiMount` really has `Kind`, `Slot` and `Access`, and their types; and in `PluginManifest.cs` that `License` and `Docs` exist with those names. If a name differs, follow the SDK and say so in your report.

- [ ] **Step 2: Run them to verify they fail**

Run: `/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter FullyQualifiedName~ManifestTests 2>&1 | grep -E "error|Failed |Passed!|Failed!"`
Expected: the five new tests fail (the hosts test on the `**` entry, the others on missing fields).

- [ ] **Step 3: Change the manifest**

`src/NoMercy.Plugin.InternetRadio/plugin.json` becomes:

```json
{
  "id": "5KTKRT4Z2Y9P59Y40W5CX4TQKF",
  "name": "Internet Radio",
  "description": "Browse and play internet radio stations in the built-in player.",
  "version": "2.0.0",
  "targetAbi": "12.1",
  "author": "NoMercy Community",
  "license": "MIT",
  "docs": "https://forgejo.phillippepelzer.me/FiLL/nomercy-radiostation-plugin/src/branch/main/src/NoMercy.Plugin.InternetRadio/README.md",
  "projectUrl": "https://forgejo.phillippepelzer.me/FiLL/nomercy-radiostation-plugin",
  "assembly": "NoMercy.Plugin.InternetRadio.dll",
  "autoEnabled": true,
  "translations": {
    "source": "en",
    "locales": ["en", "nl"],
    "path": "lang"
  },
  "capabilities": {
    "hooks": ["ui", "scheduledTask", "media.proxy"],
    "rest": true,
    "ws": false,
    "network": { "hosts": ["*.api.radio-browser.info"] },
    "ui": {
      "mounts": [
        {
          "section": "music",
          "kind": "music",
          "label": "stations.title",
          "icon": "portableRadio",
          "route": "/",
          "access": "shared"
        },
        {
          "section": "settings",
          "kind": "settings",
          "slot": "section",
          "label": "settings.title",
          "icon": "portableRadio",
          "route": "/settings",
          "access": "owner"
        }
      ]
    }
  }
}
```

The station hosts the plugin reaches for logos and ICY metadata are not listed: Task 2 asks the owner for them at runtime, which is what the SDK's own documentation calls the honest answer for a host that is not known at package time.

- [ ] **Step 4: Run the manifest tests**

Run the same filter. Expected: all pass.

`DiscoveryContractTests` asserts the nav entries and the mounts agree. The labels now differ (mounts carry keys, `NavEntries` still carries `PluginIdentity.Name`), so that test will fail. Do NOT weaken it: Task 3 rewrites the nav entries, and the two come back into agreement there. If it fails now, note it in your report and continue; if it does not fail, read it and say why.

- [ ] **Step 5: Run the whole suite, then commit**

```bash
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true 2>&1 | grep -E "Warn|Error\(s\)"
/c/Users/philp/.dotnet/dotnet.exe test -c Release --no-build 2>&1 | grep -E "Failed |Passed!|Failed!"
git add src/NoMercy.Plugin.InternetRadio/plugin.json tests
git commit -m "fix(manifest): name the one host we know, and ask for the rest later

network.hosts said ** - every host on the internet, granted at install time
by a plugin that cannot name what it will reach. The SDK's own answer to that
is to ask the owner at runtime, which the next commit does. The mounts also
gain the kind, slot and access they always meant, and labels become keys."
```

A failing `DiscoveryContractTests` is expected here and is fixed in Task 3; say so in the commit only if it is the only failure.

---

### Task 2: Ask for the grants before they are refused

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/Hosting/StationGrants.cs`
- Modify: `src/NoMercy.Plugin.InternetRadio/InternetRadioPlugin.cs`, `src/NoMercy.Plugin.InternetRadio/Playback/HostStationStreams.cs`
- Test: `tests/NoMercy.Plugin.InternetRadio.Tests/Hosting/StationGrantsTests.cs`, `tests/.../Playback/HostStationStreamsTests.cs`

**Interfaces:**
- Consumes: `IPluginGrants` (`HasAsync`, `RequestAsync`), `PluginGrantKind.ForCapability`, `PluginGrantKind.NetworkHost`, `PluginGrant.Everything`, `PluginCapabilityNames.MediaProxy`.
- Produces: `StationGrants(IPluginGrants grants, ILogger logger)` with `Task EnsureAsync(CancellationToken ct)`, called once per refresh. `HostStationStreams` keeps `StreamUnusable` and its refusal handling but no longer requests anything.

- [ ] **Step 1: Write the failing tests**

Create `tests/NoMercy.Plugin.InternetRadio.Tests/Hosting/StationGrantsTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Hosting;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using NoMercy.PluginSdk.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Hosting;

public class StationGrantsTests
{
    private sealed class RecordingGrants(params string[] held) : IPluginGrants
    {
        public List<(string Kind, string Value, string Reason)> Requested { get; } = [];

        public Task<bool> HasAsync(string kind, string value, CancellationToken ct = default) =>
            Task.FromResult(held.Contains($"{kind}={value}"));

        public Task<IReadOnlyList<string>> GetAsync(string kind, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task RequestAsync(string kind, string value, string reason, CancellationToken ct = default)
        {
            Requested.Add((kind, value, reason));
            return Task.CompletedTask;
        }
    }

    private static string MediaProxy => PluginGrantKind.ForCapability(PluginCapabilityNames.MediaProxy);

    [Fact]
    public async Task AsksForBothGrantsWhenItHoldsNeither()
    {
        RecordingGrants grants = new();

        await new StationGrants(grants, new RecordingLogger()).EnsureAsync(CancellationToken.None);

        grants.Requested.Select(request => (request.Kind, request.Value))
            .Should().BeEquivalentTo(new[]
            {
                (MediaProxy, PluginGrant.Everything),
                (PluginGrantKind.NetworkHost, PluginGrant.Everything),
            });
    }

    // The reason is what the owner reads in the dashboard. It has to say what the plugin
    // will actually do, in their language, or the decision is a guess.
    [Fact]
    public async Task EveryReasonSaysWhatItIsFor()
    {
        RecordingGrants grants = new();

        await new StationGrants(grants, new RecordingLogger()).EnsureAsync(CancellationToken.None);

        grants.Requested.Should().OnlyContain(request => request.Reason.Length > 40);
        grants.Requested.Should().Contain(request => request.Reason.Contains("play"));
        grants.Requested.Should().Contain(request => request.Reason.Contains("logo"));
    }

    [Fact]
    public async Task AsksForNothingItAlreadyHolds()
    {
        RecordingGrants grants = new($"{MediaProxy}={PluginGrant.Everything}");

        await new StationGrants(grants, new RecordingLogger()).EnsureAsync(CancellationToken.None);

        grants.Requested.Select(request => request.Kind).Should().Equal(PluginGrantKind.NetworkHost);
    }

    [Fact]
    public async Task AsksOnceEvenWhenTheRefreshRunsAgain()
    {
        RecordingGrants grants = new();
        StationGrants asking = new(grants, new RecordingLogger());

        await asking.EnsureAsync(CancellationToken.None);
        await asking.EnsureAsync(CancellationToken.None);

        grants.Requested.Should().HaveCount(2);
    }

    // A host that refuses the facade outright must not cost the refresh its catalogue.
    [Fact]
    public async Task AHostThatCannotBeAskedIsNotFatal()
    {
        await new StationGrants(new ThrowingGrants(), new RecordingLogger())
            .EnsureAsync(CancellationToken.None);
    }

    private sealed class ThrowingGrants : IPluginGrants
    {
        public Task<bool> HasAsync(string kind, string value, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetAsync(string kind, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task RequestAsync(string kind, string value, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
```

And in `HostStationStreamsTests.cs`, replace `WithoutTheGrantItAsksTheOwnerOnceForEveryHost` with:

```csharp
    // Asking belongs in StationGrants, before anything is refused. A refusal here is
    // reported and drawn; it does not start a conversation with the owner from inside a
    // page render.
    [Fact]
    public async Task ARefusalIsReportedAndAsksForNothing()
    {
        RecordingGrants grants = new();
        HostStationStreams streams = new(
            (_, _) => throw Refusal(PluginRefusalCodes.CapabilityScopeRefused), grants, new RecordingLogger());

        StationStream stream = await streams.ForAsync(Station, CancellationToken.None);

        stream.Should().Be(StationStream.Refused(PluginRefusalCodes.CapabilityScopeRefused));
        grants.Requested.Should().BeEmpty();
    }
```

Delete `AFailedGrantRequestStillAnswersRefused` and the `ThrowingGrants` class from that file if nothing else uses them.

- [ ] **Step 2: Run to verify they fail**

Run: `/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter "FullyQualifiedName~StationGrantsTests|FullyQualifiedName~HostStationStreamsTests" 2>&1 | grep -E "error CS|Failed |Passed!|Failed!"`
Expected: `error CS0246: ... 'StationGrants'`.

- [ ] **Step 3: Write `StationGrants`**

Create `src/NoMercy.Plugin.InternetRadio/Hosting/StationGrants.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using Microsoft.Extensions.Logging;
using NoMercy.PluginSdk.Abstractions;

namespace NoMercy.Plugin.InternetRadio.Hosting;

/// <summary>
/// What this plugin has to ask the owner for, asked before anything needs it.
///
/// The manifest names radio-browser and nothing else, because that is the only host this
/// plugin knows at package time. Everything else is a station: thousands of servers whose
/// addresses arrive with the catalogue. The SDK's answer to that is a runtime request the
/// owner sees in the dashboard, rather than a wildcard taken at install time.
///
/// Asked from the refresh job, not from a page. A request records and returns; it never
/// waits for a human, and asking twice for the same thing does not queue a second prompt.
/// Asking early is also what keeps an owner away from the host's own refusal text, which
/// tells an author to add the host to plugin.json - a line that does nothing for a
/// capability grant.
/// </summary>
public sealed class StationGrants(IPluginGrants grants, ILogger logger)
{
    private const string StreamingReason =
        "Internet Radio plays stations from thousands of different servers. "
        + "Allow it to stream them through this server so they can play in the app.";

    private const string StationsReason =
        "Internet Radio fetches each station's logo and the track titles it announces, "
        + "from the station itself. Their addresses come from the station directory and "
        + "cannot be listed in advance.";

    private int _asked;

    public async Task EnsureAsync(CancellationToken ct = default)
    {
        // Once per process. The host already ignores a repeat, so this only spares it the
        // round trip on every refresh.
        if (Interlocked.Exchange(ref _asked, 1) == 1)
        {
            return;
        }

        try
        {
            await AskUnlessHeldAsync(
                PluginGrantKind.ForCapability(PluginCapabilityNames.MediaProxy),
                PluginGrant.Everything,
                StreamingReason,
                ct);

            await AskUnlessHeldAsync(
                PluginGrantKind.NetworkHost, PluginGrant.Everything, StationsReason, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Asking is a courtesy on a host that mediates it. Failing to ask must not cost
            // the refresh its catalogue.
            logger.LogWarning(exception, "Internet Radio could not ask the owner for what it needs.");
        }
    }

    private async Task AskUnlessHeldAsync(string kind, string value, string reason, CancellationToken ct)
    {
        if (await grants.HasAsync(kind, value, ct))
        {
            return;
        }

        await grants.RequestAsync(kind, value, reason, ct);
    }
}
```

Both values are `PluginGrant.Everything` on purpose: `ConfigPluginGrantStore.Holds` compares exactly or against `*` and does not glob, so a per-host grant would have to be given station by station, forever. Write that reason as a comment on the `NetworkHost` call.

- [ ] **Step 4: Call it from the refresh job, and stop asking from the page**

In `InternetRadioPlugin.cs`, beside the other lazily-built collaborators:

```csharp
    private StationGrants? _grants;

    private StationGrants Grants => _grants ??= new StationGrants(Context.Grants, Context.Logger);
```

In `ExecuteAsync(string jobName, ...)`, as the first thing after the linked token is created and before `Provider.RefreshAsync`:

```csharp
        // Before the catalogue, so the owner is asked once, early, and not by a page that
        // was already refused.
        await Grants.EnsureAsync(linked.Token);
```

In `Playback/HostStationStreams.cs`: delete `AskForTheGrantOnceAsync`, the `_askedForTheGrant` field, the `GrantReason` constant and the call to it in the `PluginRefusedException` handler. Keep the throttled logging, `StreamUnusable`, and everything else. The constructor keeps its `IPluginGrants` parameter only if something still uses it — if nothing does, remove the parameter and update `For(IPluginContext)` and the tests accordingly, and say so in your report.

- [ ] **Step 5: Run the tests**

```bash
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true 2>&1 | grep -E "Warn|Error\(s\)"
/c/Users/philp/.dotnet/dotnet.exe test -c Release --no-build 2>&1 | grep -E "Failed |Passed!|Failed!"
```
Expected: green, except `DiscoveryContractTests` if Task 1 left it failing.

- [ ] **Step 6: Commit**

```bash
git add src/NoMercy.Plugin.InternetRadio/Hosting/StationGrants.cs src/NoMercy.Plugin.InternetRadio/InternetRadioPlugin.cs src/NoMercy.Plugin.InternetRadio/Playback/HostStationStreams.cs tests
git commit -m "feat(grants): ask the owner up front, not after a refusal

The plugin asked for media.proxy only once a mint had already been refused,
which is how an owner met the host's advice to add a host to plugin.json - a
line that does nothing for a capability grant. Both grants are now asked from
the refresh job, once, with reasons that say what they are for."
```

---

### Task 3: Declare the pages

**Files:**
- Modify: `src/NoMercy.Plugin.InternetRadio/Views/RadioRoutes.cs`, `src/NoMercy.Plugin.InternetRadio/InternetRadioPlugin.cs`
- Test: `tests/NoMercy.Plugin.InternetRadio.Tests/Views/RadioRoutesTests.cs`, `tests/.../DiscoveryContractTests.cs`

**Interfaces:**
- Consumes: `PluginRouteTable`, `PluginRoute` (`Name`, `Path`, `Label`, `Access`, `Layout`, `Surfaces`), `PluginLayout`, `PluginRouteAccess`, `PluginNavEntry`.
- Produces: `InternetRadioPlugin.Table` (a `static readonly PluginRouteTable`) and `Routes => Table`; nav entries carrying `Access` and label keys. Task 5 uses the label keys.

- [ ] **Step 1: Write the failing tests**

Add to `RadioRoutesTests.cs`:

```csharp
    // The table is what the host registers, and RadioRoutes.Parse is what answers. Two
    // lists of paths drift the moment one is edited alone: a page the host advertises and
    // the plugin cannot parse is a dead link in the sidebar.
    [Fact]
    public void EveryDeclaredRouteIsOneTheParserUnderstands()
    {
        foreach (PluginRoute route in InternetRadioPlugin.Table.All)
        {
            string path = route.Path
                .Replace(":term", "jazz")
                .Replace(":slug", "ambient")
                .Replace(":id", "u1");

            RadioRoutes.Parse(path).Kind.Should().NotBe(RadioRouteKind.Unknown, $"'{route.Path}' is declared");
        }
    }

    [Fact]
    public void EveryPageTheParserAnswersIsDeclared()
    {
        IEnumerable<RadioRouteKind> declared = InternetRadioPlugin.Table.All
            .Select(route => RadioRoutes.Parse(
                route.Path.Replace(":term", "jazz").Replace(":slug", "ambient").Replace(":id", "u1")).Kind);

        declared.Should().Contain(
        [
            RadioRouteKind.Browse,
            RadioRouteKind.AllStations,
            RadioRouteKind.Search,
            RadioRouteKind.Genre,
            RadioRouteKind.Station,
            RadioRouteKind.Settings,
        ]);
    }

    // Settings is the owner's page. The host hides an owner route from a member rather
    // than offering one that answers 403.
    [Fact]
    public void OnlyTheSettingsPageIsForTheOwner()
    {
        InternetRadioPlugin.Table.All
            .Where(route => route.Access == PluginRouteAccess.Owner)
            .Select(route => route.Path)
            .Should().Equal(RadioRoutes.Settings);
    }

    // A grid of station tiles is a grid; the settings page is a form. Without a layout
    // every page falls back to "standard", which is what we shipped before.
    [Fact]
    public void TheTilePagesAskForAGridAndSettingsForAForm()
    {
        PluginRoute Route(string path) => InternetRadioPlugin.Table.All.Single(route => route.Path == path);

        Route(RadioRoutes.Browse).Layout.Should().Be(PluginLayout.Grid);
        Route(RadioRoutes.AllStations).Layout.Should().Be(PluginLayout.Grid);
        Route(RadioRoutes.Settings).Layout.Should().Be(PluginLayout.Form);
    }

    [Fact]
    public void EveryRouteIsLabelledByAKey()
    {
        InternetRadioPlugin.Table.All.Should().OnlyContain(route =>
            !string.IsNullOrWhiteSpace(route.Label) && !route.Label!.Contains(' '));
    }
```

Check `PluginRouteTable`'s member for enumerating routes before using `.All` — read `_server/src/NoMercy.PluginSdk.Abstractions/PluginRouteTable.cs` and use whatever it exposes (it may be `All`, `Routes`, or an indexer/`On(surface)`); use that name consistently in the tests and the code.

In `DiscoveryContractTests.cs`, the test that asserts the nav entries and the mounts agree must now compare label keys as well. Read it and update it so it compares `Section`, `Route`, `Icon`, `Label` and `Access` — it is the only thing that catches the two drifting.

- [ ] **Step 2: Run to verify they fail**

Run: `/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter "FullyQualifiedName~RadioRoutesTests|FullyQualifiedName~DiscoveryContractTests" 2>&1 | grep -E "error CS|Failed |Passed!|Failed!"`
Expected: a compile error on `InternetRadioPlugin.Table`.

- [ ] **Step 3: Declare the table**

In `InternetRadioPlugin.cs`, above `NavEntries`:

```csharp
    /// <summary>
    /// The pages this plugin has, as the host advertises them.
    ///
    /// Without this the host registers no named page: every view falls back to the
    /// standard layout, no page has a title of its own, and the client reaches them all
    /// through one wildcard. RadioRoutes.Parse still decides what a request means - this
    /// is the list, not the dispatcher - and RadioRoutesTests holds the two together.
    /// </summary>
    public static readonly PluginRouteTable Table = new(
        new PluginRoute
        {
            Name = "browse",
            Path = RadioRoutes.Browse,
            Label = "stations.title",
            Layout = PluginLayout.Grid,
        },
        new PluginRoute
        {
            Name = "all",
            Path = RadioRoutes.AllStations,
            Label = "stations.all",
            Layout = PluginLayout.Grid,
        },
        new PluginRoute
        {
            Name = "search",
            Path = RadioRoutes.Search,
            Label = "search.title",
            Layout = PluginLayout.Grid,
        },
        new PluginRoute
        {
            Name = "search-term",
            Path = $"{RadioRoutes.Search}/:term",
            Label = "search.title",
            Layout = PluginLayout.Grid,
        },
        new PluginRoute
        {
            Name = "genre",
            Path = $"{RadioRoutes.Genre}/:slug",
            Label = "genre.title",
            Layout = PluginLayout.Grid,
        },
        new PluginRoute
        {
            Name = "station",
            Path = $"{RadioRoutes.Station}/:id",
            Label = "station.title",
            Layout = PluginLayout.Standard,
        },
        new PluginRoute
        {
            Name = "settings",
            Path = RadioRoutes.Settings,
            Label = "settings.title",
            Layout = PluginLayout.Form,
            Access = PluginRouteAccess.Owner,
        }
    );

    public PluginRouteTable Routes => Table;
```

Read `RadioRoutes.cs` first: use its own constants for the paths, and if it has no constant for a prefix (genre, station, search), add one there rather than writing the string twice. `Surfaces` is left empty on every route, which means every surface — these are lists and a detail page, and there is nothing in them a television cannot draw.

Then update `NavEntries` so both entries carry the label key their mount now carries, and the settings entry carries `Access = PluginRouteAccess.Owner`:

```csharp
    // One entry per manifest mount; DiscoveryContractTests asserts the two agree.
    public IReadOnlyList<PluginNavEntry> NavEntries { get; } =
        [
            new PluginNavEntry
            {
                Section = PluginUiSection.Music,
                Label = "stations.title",
                Icon = "portableRadio",
                Route = RadioRoutes.Browse,
            },
            new PluginNavEntry
            {
                Section = PluginUiSection.Settings,
                Label = "settings.title",
                Icon = "portableRadio",
                Route = RadioRoutes.Settings,
                Access = PluginRouteAccess.Owner,
            },
        ];
```

- [ ] **Step 4: Run the tests**

Run the same filter, then the full suite. Expected: green, including `DiscoveryContractTests`.

If `PluginRouteTable`'s constructor throws on a duplicate path or name, read the message: two of our routes may normalise to the same path. Fix the table, not the constructor's expectation.

- [ ] **Step 5: Commit**

```bash
git add src/NoMercy.Plugin.InternetRadio tests
git commit -m "feat(ui): declare the pages this plugin has

IUiPlugin.Routes was the default empty table, so the host registered no named
page, every view drew with the standard layout and no page had a title. The
seven pages now carry a label key, a layout and, for settings, owner access -
the same shape Automix declares."
```

---

### Task 4: Build views with the SDK's own components

**Files:**
- Modify: `src/NoMercy.Plugin.InternetRadio/Views/Ui.cs` (shrinks), `BrowseView.cs`, `AllStationsView.cs`, `GenreView.cs`, `SearchView.cs`, `StationView.cs`, `SettingsView.cs`, `EmptyCatalog.cs`, `StationCards.cs`
- Test: every file under `tests/NoMercy.Plugin.InternetRadio.Tests/Views/`

**Interfaces:**
- Consumes: `PluginViews.{Container,Text,Row,Grid,Card,Detail,Button,Form,EmptyState,Table,Row,Badge}` and `PluginComponentType`.
- Produces: `Ui.MediaGrid(string id, IEnumerable<PluginComponent> items)` and `Ui.MediaCard(...)` only. Task 5 relies on every drawn string arriving through `PluginViews.Text` or a component prop the client translates.

- [ ] **Step 1: Write the failing test that pins the rule**

Add `tests/NoMercy.Plugin.InternetRadio.Tests/Views/ComponentVocabularyTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Views;
using NoMercy.PluginSdk.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

/// <summary>
/// Every component a view emits has to be one the client draws. The plugin used to build
/// its own `Plugin*` tags, which the client kept a second, older map alive for - and which
/// no translation ever reached, because only the design system's own text nodes are
/// resolved through the plugin's language bundle.
/// </summary>
public class ComponentVocabularyTests
{
    // PluginForm and PluginWebView are the SDK's own exceptions: the client keys its form
    // on exactly that name. NMGrid and NMMusicCard are app components on the client's
    // allow-list, which PluginViews has no equivalent for.
    private static readonly string[] Allowed =
    [
        PluginComponentType.Form,
        Ui.MediaGridComponent,
        Ui.MediaCardComponent,
    ];

    public static TheoryData<string, PluginView> EveryView() => ViewFixtures.All();

    [Theory]
    [MemberData(nameof(EveryView))]
    public void NoViewEmitsAHandRolledComponent(string name, PluginView view)
    {
        IEnumerable<string> offenders = ViewFixtures.Flatten(view)
            .Select(node => node.Component)
            .Where(component => component.StartsWith("Plugin", StringComparison.Ordinal))
            .Where(component => !Allowed.Contains(component))
            .Distinct();

        offenders.Should().BeEmpty($"{name} should draw with PluginViews");
    }
}
```

Create `tests/NoMercy.Plugin.InternetRadio.Tests/Views/ViewFixtures.cs` holding `All()` — one entry per view, built from the same station and state fixtures the existing view tests use — and `Flatten(PluginView)`, moved out of whichever test file already has it. Read `EmitPayloadsForRendering.cs` first: it already builds every screen, and its list is the one to reuse rather than write a second copy.

- [ ] **Step 2: Run to verify it fails**

Run: `/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter FullyQualifiedName~ComponentVocabularyTests 2>&1 | grep -E "error CS|Failed |Passed!"`
Expected: fails, listing `PluginContainer`, `PluginText` and the rest.

- [ ] **Step 3: Shrink `Ui.cs`**

Delete every member except `MediaGrid`, `MediaCard`, and the two component-name constants they use. Replace the file's header comment with what is true now:

```csharp
// The two components the design system has no builder for.
//
// Everything else a view draws comes from PluginViews, which is the SDK's own builder and
// the only path the client resolves translations through. NMGrid and NMMusicCard are app
// components on the client's allow-list - the same shelf Music draws its own artists on -
// and a plugin cannot express them any other way.
```

- [ ] **Step 4: Move every view onto `PluginViews`**

Mechanical, file by file. The mapping:

| Was | Becomes |
| --- | --- |
| `Ui.Container(id, items)` | `PluginViews.Container(id, items)` |
| `Ui.Text(id, value, variant)` | `PluginViews.Text(id, value, variant)` |
| `Ui.Row(id, items)` | `PluginViews.Row(id, items)` — the `IEnumerable` overload becomes `[.. items]` |
| `Ui.Grid(id, items)` | `PluginViews.Grid(id, items)` |
| `Ui.Card(...)` | `PluginViews.Card(id, title, subtitle, image, action)` |
| `Ui.Detail(...)` | `PluginViews.Detail(id, title, description, image, children)` |
| `Ui.Button(id, label, action, icon, variant)` | `PluginViews.Button(id, label, action, icon, variant)` |
| `Ui.Form(...)` | `PluginViews.Form(id, submitLabel, submitAction, fields)` |
| `Ui.EmptyState(id, title, message)` | `PluginViews.EmptyState(id, title, message)` |
| `Ui.Table(id, columns, rows)` | `PluginViews.Table(id, columns, rows, emptyMessage)` |
| `Ui.TableRow(id, cells)` | `PluginViews.Row(id, cells)` |
| `Ui.Badge(id, label, variant)` | `PluginViews.Badge(id, label, variant)` |

Verify each signature in `_server/src/NoMercy.PluginSdk.Abstractions/PluginViews.cs` before using it — several take their arguments in a different order or have an extra optional parameter, and `Row` has two overloads (layout row and table row) that differ only by the second argument's type.

Node ids stay exactly as they are: the tests, and the owner's muscle memory for what a payload looks like, both key on them.

- [ ] **Step 5: Update the view tests to the new shapes**

The existing tests assert on props the old components used. `PluginViews.Text` puts its value under `text` (and wraps a `title`/`subtitle` in `NMContentHeader`, a `caption` in `NMHelper` under `helperText`). Read `PluginViews.cs` and update each assertion to the prop the SDK actually emits. Do not loosen an assertion to make it pass: if a test can no longer see what it was checking, assert the same fact on the node that now carries it.

Run the view tests until green:
```bash
/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter FullyQualifiedName~Views 2>&1 | grep -E "Failed |Passed!|Failed!"
```

- [ ] **Step 6: Remove what nothing reads**

- `StationCards.TileWidth` — no reader.
- The `UserState state` parameter on `StationCards.Grid` — never used; update the call sites.
- The `description` argument passed to `Ui.MediaCard` — `NMMusicCard` builds its subtitle from its own fields and never reads it. Keep `StationCards.StationSubtitle` only if something still draws it; if nothing does, delete it and its test.

- [ ] **Step 7: Full suite, then commit**

```bash
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true 2>&1 | grep -E "Warn|Error\(s\)"
/c/Users/philp/.dotnet/dotnet.exe test -c Release --no-build 2>&1 | grep -E "Failed |Passed!|Failed!"
git add src tests
git commit -m "refactor(views): draw with PluginViews instead of our own components

Ui.cs hand-built twelve Plugin* components. The SDK ships a builder for all of
them, Automix uses nothing else, and the client keeps its older map alive for
payloads like ours. Only MediaGrid and MediaCard stay: NMGrid and NMMusicCard
are app components with no builder. This is also what lets a translation
reach the page - the design system's text nodes resolve through the plugin's
bundle, our own never did."
```

---

### Task 5: Translations that actually resolve

**Files:**
- Modify: every file under `src/NoMercy.Plugin.InternetRadio/Views/`, `src/NoMercy.Plugin.InternetRadio/lang/en.json`, `lang/nl.json`
- Test: `tests/NoMercy.Plugin.InternetRadio.Tests/TranslationTests.cs`

**Interfaces:**
- Consumes: the `PluginViews` components from Task 4 and the label keys from Tasks 1 and 3.
- Produces: `lang/en.json` and `lang/nl.json` carrying every key the plugin draws, and no key it does not.

- [ ] **Step 1: Establish which props the client translates, and write it down**

Before changing any string, read `../nomercy-app-web` at `origin/master`:
- `src/components/plugin/NMRender.vue` (which props are passed through the plugin's text resolver),
- `src/lib/plugin/pluginText.ts`,
- `src/router/middleware/getPlugins.ts` (nav labels).

Write the answer into your report as a short list: "these props are resolved, these are drawn literally". Only the resolved ones become keys in the next step; a literal prop keeps its English text, because a key drawn literally is worse than an English sentence.

- [ ] **Step 2: Write the failing test**

Rewrite `TranslationTests.cs` around these two facts:

```csharp
    // A key a view draws and the bundle does not carry renders as the key itself: the
    // viewer reads "stations.title" where a title should be.
    [Fact]
    public void EveryKeyAViewDrawsExistsInEveryLanguage()
    {
        IReadOnlySet<string> drawn = TranslationKeys.Drawn();

        foreach (string locale in new[] { "en", "nl" })
        {
            IReadOnlyDictionary<string, string> bundle = LoadBundle(locale);

            drawn.Where(key => !bundle.ContainsKey(key))
                .Should().BeEmpty($"{locale}.json should carry every key a view draws");
        }
    }

    // The other direction, which is how this plugin shipped two language files that did
    // nothing: four keys, none of them drawn anywhere.
    [Fact]
    public void EveryKeyInTheBundleIsOneAViewDraws()
    {
        IReadOnlySet<string> drawn = TranslationKeys.Drawn();

        LoadBundle("en").Keys.Where(key => !drawn.Contains(key))
            .Should().BeEmpty("en.json should carry no key nothing draws");
    }
```

`TranslationKeys.Drawn()` is a test helper that builds every view (reuse `ViewFixtures.All()` from Task 4), walks the component tree, and collects the values of exactly the props Step 1 established as resolved, plus the mount and route labels from the manifest and `InternetRadioPlugin.Table`. Keys are the values that look like keys — a dotted lowercase token with no spaces; anything else is a literal and is ignored by the collector, which is what keeps the test honest while some strings are still literal.

- [ ] **Step 3: Run to verify it fails**

Run: `/c/Users/philp/.dotnet/dotnet.exe test -c Release --filter FullyQualifiedName~TranslationTests 2>&1 | grep -E "error CS|Failed |Passed!"`
Expected: fails on the four unused keys already in `en.json`, and on the mount and route labels from Tasks 1 and 3 that no bundle carries yet.

- [ ] **Step 4: Key the strings and fill the bundles**

Replace each literal a resolved prop carries with a key, using this scheme — one namespace per page, matching the route names:

```
stations.title      Radio stations            Radiozenders
stations.all        All stations              Alle zenders
stations.empty      No stations yet           Nog geen zenders
search.title        Search                    Zoeken
search.submit       Search                    Zoeken
search.nothing      Nothing found             Niets gevonden
genre.title         Genre                     Genre
station.title       Station                   Zender
station.play        Play                      Afspelen
station.enqueue     Add to queue              Aan wachtrij toevoegen
station.favourite   Add to favourites         Aan favorieten toevoegen
station.unfavourite Remove from favourites    Uit favorieten verwijderen
station.missing     Station not found         Zender niet gevonden
station.unavailable Playing is waiting for the server owner to approve streaming for Internet Radio.   Afspelen wacht op goedkeuring van de servereigenaar om te streamen voor Internet Radio.
settings.title      Settings                  Instellingen
back                Back                      Terug
```

This is the starting set, not the whole set: add a key for every string the collector reports as drawn but unkeyed, and delete any key here that no view ends up drawing — the test enforces both directions. Dutch is the second locale the manifest declares; write it as a Dutch speaker would, not as a translation of the English word order.

- [ ] **Step 5: Run the tests, then the suite**

Run the translation filter until green, then the full suite.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(i18n): draw keys the client can resolve

The plugin shipped en.json and nl.json with four keys, none of them used, and
drew English literals that the client had no way to translate. Now that views
are built with PluginViews, the design system's text nodes resolve through the
plugin's own bundle, so the strings are keys and the bundles carry them - in
both directions, which a test now holds."
```

---

### Task 6: An install route that keeps the server's checks

**Files:**
- Modify: `.forgejo/workflows/build.yml` (the `repository.json` step), `README.md`, `src/NoMercy.Plugin.InternetRadio/README.md`, `docs/superpowers/specs/2026-09-22-abi-12-migration-design.md`
- Test: `tests/NoMercy.Plugin.InternetRadio.Tests/RepositoryManifestTests.cs`

**Interfaces:**
- Consumes: `PluginRepositoryManifest`, `PluginRepositoryEntry`, `PluginVersionEntry` from the SDK.
- Produces: a `repository.json` that deserialises into `PluginRepositoryManifest`.

- [ ] **Step 1: Read what the server accepts**

Read `_server/src/NoMercy.PluginSdk.Abstractions/PluginRepositoryManifest.cs`, `PluginRepositoryEntry.cs`, and `../nomercy-media-server/src/NoMercy.Api/Controllers/V1/Dashboard/Plugins/PluginRepositoryController.cs` (the install path, around lines 210-260). Write into your report: the exact top-level shape, whether `checksum` is expected bare or prefixed (`sha256:`), and what the controller does with `downloadUrl`. The test and the workflow both follow what you find, not what this plan assumes.

- [ ] **Step 2: Write the failing test**

Create `tests/NoMercy.Plugin.InternetRadio.Tests/RepositoryManifestTests.cs`. It builds the same JSON the workflow writes — keep the sample in the test file, built from `plugin.json` plus a fixed download URL and checksum — and asserts it round-trips:

```csharp
    // CI writes this file beside every release and an owner adds it as a repository. If
    // the server cannot read it, the supported install route silently does not exist and
    // everyone falls back to extracting a zip by hand, which skips every check.
    [Fact]
    public void TheRepositoryIndexIsOneTheServerCanRead()
    {
        PluginRepositoryManifest? index = JsonSerializer.Deserialize<PluginRepositoryManifest>(Sample);

        index.Should().NotBeNull();
        index!.Name.Should().NotBeNullOrWhiteSpace();

        PluginRepositoryEntry entry = index.Plugins.Should().ContainSingle().Subject;
        entry.Id.Should().Be(PluginIdentity.Id);
        entry.Name.Should().Be(PluginIdentity.Name);

        PluginVersionEntry version = entry.Versions.Should().ContainSingle().Subject;
        version.Version.Should().Be(PluginIdentity.Version.ToString(3));
        version.TargetAbi.Should().Be(ManifestTests.LoadManifest().TargetAbi);
        version.DownloadUrl.Should().EndWith(".zip");
        version.Checksum.Should().NotBeNullOrWhiteSpace();
    }
```

- [ ] **Step 3: Run to verify it fails, then make the sample and the workflow agree**

Run the filter; the test fails until the sample exists. Then update the workflow's `repository.json` step so its output has the same shape as the sample — including the top-level `name` and `url` the SDK type declares — and keep the two in step by construction: the test's sample is the workflow's template with the values filled in, and a comment in both says so.

- [ ] **Step 4: Rewrite the install instructions**

In `src/NoMercy.Plugin.InternetRadio/README.md` and `README.md`, replace the "extract into `<server>/plugins/`" instructions with the repository route:

> **Install.** In the dashboard, add
> `https://forgejo.phillippepelzer.me/FiLL/nomercy-radiostation-plugin/releases/download/<tag>/repository.json`
> as a plugin repository, then install Internet Radio from it. The server checks the
> download against the checksum in that file.
>
> Installing by hand — extracting the zip into `<server>/plugins/` — skips that check, and
> a plugin uploaded through the dashboard's upload button is visible to the server owner
> alone. Use the repository.

In the release-notes body in `.forgejo/workflows/build.yml`, do the same: the repository URL for this tag, one sentence on what the server verifies, and the warning about the two manual routes.

In `docs/superpowers/specs/2026-09-22-abi-12-migration-design.md`, at the decision table row for Distribution and at the "Install: the repository, not the folder" points, add a dated correction rather than editing the history away:

> **Correction (2026-09-23):** this spec's advice to extract the zip by hand was wrong.
> It works only because no `.sideloaded` marker is written, which also skips the
> developer-mode gate and the checksum the server would otherwise verify. The supported
> route is a repository index; see `2026-09-23-sdk-conformance-design.md`.

- [ ] **Step 5: Validate and commit**

```bash
python -c "import yaml; yaml.safe_load(open('.forgejo/workflows/build.yml')); print('yaml ok')"
/c/Users/philp/.dotnet/dotnet.exe test -c Release 2>&1 | grep -E "Failed |Passed!|Failed!"
git add .forgejo README.md src/NoMercy.Plugin.InternetRadio/README.md docs tests
git commit -m "fix(release): install through a repository, not by hand

Extracting the zip into the plugins folder works because no .sideloaded marker
is written - which is also how it skips the developer-mode gate and the
checksum the server would otherwise verify. CI already publishes a repository
index; the documentation now points at it, and the earlier spec carries a
dated correction rather than a quiet edit."
```

---

### Task 7: Prove it, on the owner's server

This task changes no code. It runs only when the owner asks, and it needs them: the server must be stopped and started, and the repository install is theirs to click.

- [ ] **Step 1: Local gate**

```bash
/c/Users/philp/.dotnet/dotnet.exe build -c Release -p:TreatWarningsAsErrors=true 2>&1 | grep -E "Warn|Error\(s\)"
/c/Users/philp/.dotnet/dotnet.exe test -c Release --no-build 2>&1 | grep -E "Failed |Passed!|Failed!"
grep -rn --include='*.cs' -E 'https?://' src | grep -v 'radio-browser' | grep -v 'forgejo.phillippepelzer.me' | grep -v 'github.com' || echo "no hardcoded urls"
```
All three must be clean.

- [ ] **Step 2: Deploy and check, with the owner**

Ask the owner to stop the server, run `./scripts/deploy-to-server.sh --build`, ask them to start it, and then verify:

1. The plugin loads and serves its endpoints (server log).
2. The dashboard shows **two** pending grant requests — streaming and station hosts — each with its reason, before anything has been played.
3. After approving both: browse, a genre page, all stations, search, and a station plays.
4. The pages carry their own titles, and the station grid lays out as a grid.
5. The settings page is offered to the owner and not to a member.
6. Logos, now playing and favourites still behave exactly as they did.
7. With Dutch as the display language, the pages are Dutch.

Record each with its evidence in the report. Anything that fails goes back to the task that owns it.

- [ ] **Step 3: After the release is cut, verify the install route**

The repository index can only be proven once a release exists. When the owner tags and CI publishes:

1. Add the repository URL in the dashboard.
2. Install Internet Radio from it.
3. Confirm the server verified the checksum (log), that no `.sideloaded` marker exists beside the plugin, and that a member account sees the radio.

Until that has passed, the README's install instructions are untested — say so in the report.

---

### Task 8: Say what is still someone else's

**Files:**
- Delete: `docs/upstream/2026-09-22-nmsearchinput-fires-on-click.md`, `docs/upstream/2026-09-22-tab-title-undefined-undefined.md`
- Modify: the remaining `docs/upstream/2026-09-22-*.md` and `2026-09-23-music-plugin-listed-twice-in-the-sidebar.md`
- Create: `docs/upstream/2026-09-23-manifest-schema-contradicts-the-runtime.md`, `2026-09-23-verify-rejects-the-hooks-the-runtime-requires.md`, `2026-09-23-capability-grants-do-not-glob.md`, `2026-09-23-storage-foruser-unreachable-in-process.md`

Nothing is raised anywhere. These are notes; the owner decides what becomes an issue.

- [ ] **Step 1: Delete the two that did not survive**

`nmsearchinput-fires-on-click.md` — the plugin never emits that component, and `PluginForm` renders a real `<form>` that submits on Enter. `tab-title-undefined-undefined.md` — the likeliest cause was that the plugin declared no routes, which Task 3 fixed. Both go; a note that cannot be reproduced is worse than no note.

- [ ] **Step 2: Add the status this work settles to the ones that stay**

Each remaining note gets one dated line under its heading saying where this plugin now stands, for example:

> **Status (2026-09-23):** still open. This plugin no longer meets it: it asks for the
> grant up front, so an owner is not shown the misleading remedy.

The doubled sidebar entry, the minted cover without a token, `PluginMediaCard`, the form dropping `surface`/`input`, `Caller` never being populated, `PluginUiController` hard-coding `Owned`, NMP0016, the catalogue entry and the player's silent load failure all stay — say for each whether this plugin still hits it in practice.

- [ ] **Step 3: Write the four the audits found**

In the voice of the existing notes (repository, what happens, evidence by file:line at a named commit, how to reproduce, what it costs, proposed fix marked as a proposal):

- **`PluginManifestSchema` contradicts the runtime.** The generated schema types `capabilities` as an array of `{name, scope, reason}` with `additionalProperties: false` and requires `entry`; the runtime binds `capabilities` to an object and reads `entry` nowhere. An author who follows the published schema installs a plugin whose capabilities deserialise to null, which then fails on first facade use.
- **`nomercy-plugin verify` rejects the hooks the runtime requires.** `ManifestScan` validates every hook against `PluginCapabilityVocabulary`, which carries only dotted names; the runtime requires `ui` and `scheduledTask`. Automix's own manifest is Blocked by it.
- **Capability grants do not glob.** `ConfigPluginGrantStore.Holds` compares exactly or against `*`, while `PluginGrantKind.NetworkHost` documents "a host pattern in the same glob the manifest uses". An owner granting `*.example.com` silently matches nothing.
- **`context.Storage.ForUser` is unreachable in-process.** The in-process context passes no user id, so the facade throws `UserScopeRequired` unconditionally — which is why this plugin keys its own per-user file instead.

Verify each one against the current code before writing it, and record the commit you read.

- [ ] **Step 4: Commit**

```bash
git add docs/upstream
git commit -m "docs(upstream): keep what survived the audit, drop what did not

Two notes are withdrawn: the search box ignoring Enter (this plugin never
emits that component) and the undefined tab title (the likeliest cause was
declaring no routes, which we now do). The rest gain a line saying where this
plugin stands after following the SDK, and four findings from the audits are
written up."
```

---

## Before the release

- [ ] Tasks 1-8 complete, Task 7's steps 1 and 2 evidenced.
- [ ] `git log --oneline main..feat/abi-12` reads as one story; no commit carries an attribution trailer.
- [ ] Task 8 has rewritten the upstream notes.
- [ ] Merging, pushing and tagging `v2.0.0` happen **only** on the owner's explicit instruction; after a push, watch the Forgejo run until it is green.
