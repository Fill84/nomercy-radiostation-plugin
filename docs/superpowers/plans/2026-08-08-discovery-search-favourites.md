# Discovery, Search and Favourites Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Drop the ten pinned seed stations, and give the plugin live search and per-user favourites so nothing is lost by dropping them.

**Architecture:** The catalogue becomes the per-genre discovery sweep alone. Search is a live radio-browser query on its own `/search` route, with only the term held server-side per user. Favourites are full station records in one per-user state file, written through a lock and a temp-file rename, reached by a `PluginControllerBase` controller over `CallPlugin`.

**Tech Stack:** C# / .NET 10, `NoMercy.Plugins.Abstractions`, `NoMercy.Plugins.Mvc`, xunit + FluentAssertions, Forgejo Actions.

**Spec:** `docs/superpowers/specs/2026-08-08-discovery-search-favourites-design.md`

## Global Constraints

- **Target framework** `net10.0`. SDK pinned by `global.json`.
- **On this machine, plain `dotnet` is 8.0.413 and cannot build `net10.0`.** Every command below must run as `"$USERPROFILE/.dotnet/dotnet.exe"`. CI installs 10.0 to `$HOME/.dotnet` and puts it on PATH, so the workflow's bare `dotnet` is correct there.
- **The contract comes from the shared sibling checkout** at `../nomercy-media-server`. Run `./scripts/fetch-abstractions.sh` after changing the packed project list. `SERVER_DIR` overrides the location; CI sets it to `_server`.
- **`jq` and `zip` are not installed locally.** Both are CI-only. Python 3.12 is what local tooling uses.
- **`TreatWarningsAsErrors` is true** for both projects. Build with `-p:TreatWarningsAsErrors=true`.
- **Version is 1.1.0** after Task 8, and must read identically in `src/NoMercy.Plugin.InternetRadio/plugin.json`, the csproj `<Version>`, and `PluginIdentity.Version`.
- **Plugin id is `5KTKRT4Z2Y9P59Y40W5CX4TQKF` and must never change.** The host keys lifecycle state, consent and grants off it.
- **No station name, stream URL, logo, genre or country may appear in the source tree.** After Task 1 the only permitted data is the genre tags in `GenreMap`.
- **`PluginText` variants are `title`, `subtitle`, `caption` only.** Any other string silently renders as body text.
- **Routes carry state in the path, never the query string.** The web host sends only `route`.
- **Icons must exist in the Moooom set.** Verified present and used here: `portableRadio`, `play`, `playlistAdd`, `globe`, `arrowLeft`, `gridMasonry`, `settings`, `search`, `heart`. An unknown name silently renders as `plugged`.
- **Every file gets the SPDX header:**
  ```csharp
  // SPDX-License-Identifier: MIT
  // Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84
  ```
- **Never ship `NoMercy.Plugins.Abstractions.dll`, `NoMercy.Events.dll`, `NoMercy.Design.dll`, `NoMercy.Plugins.Mvc.dll` or `Newtonsoft.Json.dll`.** All are host-owned; a second copy gives the load context two incompatible identities of the same types.
- **Commit messages:** Conventional Commits. No attribution or co-author trailers.
- **Strings stay literal English.** Translations are out of scope — see the spec. Do not introduce translation keys into views.

## File Structure

| File | Responsibility |
| --- | --- |
| `Catalog/GenreMap.cs` | Gains `PerGenreLimit`, which moves here from `SeedStations` |
| `Catalog/SeedStations.cs` | **Deleted** |
| `Catalog/RadioBrowserClient.cs` | Gains `SearchByNameAsync`; keeps `GetByUuidsAsync` for favourite resolution |
| `Catalog/CatalogProvider.cs` | Stops fetching seeds; the sweep is the whole catalogue |
| `State/UserState.cs` | One user's favourites and last search term |
| `State/UserStateStore.cs` | Read/write `user-state.json` under a lock, atomically |
| `State/FavouriteResolver.cs` | Turns a station id into a full `RadioStation`, or nothing |
| `Controllers/InternetRadioController.cs` | `PluginControllerBase`: favourite toggle and search submit |
| `Views/RadioRoutes.cs` | Gains `Search` |
| `Views/StationCards.cs` | Gains a favourite toggle beside the play card, and a cover placeholder |
| `Views/SearchView.cs` | `/search` — the field, the results, the empty and failed states |
| `Views/BrowseView.cs` | Recomposed: search field, favourites, genre chips, popular grid |
| `InternetRadioPlugin.cs` | Routes `/search`, runs the live query, owns the store |

---

### Task 1: Remove the seeds

The catalogue becomes the sweep alone. `GetByUuidsAsync` stays on the client — Task 4 needs it to resolve a favourited search result — but nothing calls it until then.

**Files:**
- Delete: `src/NoMercy.Plugin.InternetRadio/Catalog/SeedStations.cs`
- Delete: `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/SeedTests.cs`
- Delete: `scripts/resolve-seeds.py`
- Modify: `src/NoMercy.Plugin.InternetRadio/Catalog/GenreMap.cs`
- Modify: `src/NoMercy.Plugin.InternetRadio/Catalog/CatalogProvider.cs`
- Modify: `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/CatalogProviderTests.cs`
- Modify: `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/GenreMapTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `GenreMap.PerGenreLimit : int` (value `5`). `SeedStations` no longer exists.

- [ ] **Step 1: Move `PerGenreLimit` to `GenreMap`**

Add to `GenreMap`, above `Sections`:

```csharp
    /// <summary>
    /// How many stations to take per genre. Seventeen sections at five each is an
    /// upper bound of eighty-five before dedupe, which is a browse page worth
    /// scrolling rather than one worth searching.
    ///
    /// Lives here rather than in a seed list because the sweep is now the entire
    /// catalogue: this number and the section list together are its whole shape.
    /// </summary>
    public const int PerGenreLimit = 5;
```

- [ ] **Step 2: Add the failing test that the sweep is the whole catalogue**

Add to `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/GenreMapTests.cs`:

```csharp
    [Fact]
    public void PerGenreLimit_IsPositive()
    {
        GenreMap.PerGenreLimit.Should().BeGreaterThan(0);
    }
```

- [ ] **Step 3: Run it to confirm it fails**

Run: `"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --filter FullyQualifiedName~GenreMapTests`
Expected: FAIL to compile — `SeedStations.PerGenreLimit` is still referenced elsewhere, and `GenreMap.PerGenreLimit` is new.

- [ ] **Step 4: Delete the seed files**

```bash
git rm src/NoMercy.Plugin.InternetRadio/Catalog/SeedStations.cs \
       tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/SeedTests.cs \
       scripts/resolve-seeds.py
```

- [ ] **Step 5: Drop the seed fetch from `CatalogProvider`**

In `CatalogProvider`, remove the `GetByUuidsAsync(SeedStations.Uuids, …)` call and the merge of its result. The fetch becomes the genre sweep alone, still ordered by votes, still passed through `StationGates.Admits` and `StationGates.Deduplicate`, still subject to `DefaultFetchBudget`. Replace every `SeedStations.PerGenreLimit` with `GenreMap.PerGenreLimit`.

Keep the existing failure semantics exactly: a sweep where every genre query fails is a failed fetch; one where some succeed keeps what succeeded; one where all succeed but nothing is admitted is **not** a failed fetch.

- [ ] **Step 6: Update `CatalogProviderTests`**

Delete any test whose subject is seed behaviour (a seed appearing in the result, a seed surviving dedupe against a discovered copy, a seed fetch failing). Every remaining test must set up genre-sweep responses only. Do not weaken the failure-mode tests listed in Step 5 — they are the ones that matter.

- [ ] **Step 7: Build and run the full suite**

```bash
"$USERPROFILE/.dotnet/dotnet.exe" build -c Release -p:TreatWarningsAsErrors=true
"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --no-build
```

Expected: clean build, all pass.

- [ ] **Step 8: Confirm no station data remains**

```bash
grep -rn --include='*.cs' --exclude-dir=obj --exclude-dir=bin -E 'https?://' src/NoMercy.Plugin.InternetRadio \
  | grep -v 'radio-browser' | grep -v 'forgejo.phillippepelzer.me' | grep -v 'github.com'
```

Expected: no output.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(catalog): the sweep is the whole catalogue, with no pinned stations"
```

---

### Task 2: Pack `NoMercy.Plugins.Mvc` and declare `rest`

`PluginControllerBase` lives in `NoMercy.Plugins.Mvc`, so the feed needs it before any controller compiles. The sibling checkout already materialises it for the torrent plugin, and the script uses `sparse-checkout add`, so the checkout costs nothing.

**Files:**
- Modify: `scripts/fetch-abstractions.sh`
- Modify: `scripts/fetch-abstractions.ps1`
- Modify: `src/NoMercy.Plugin.InternetRadio/NoMercy.Plugin.InternetRadio.csproj`
- Modify: `src/NoMercy.Plugin.InternetRadio/plugin.json`
- Modify: `.forgejo/workflows/build.yml`
- Modify: `tests/NoMercy.Plugin.InternetRadio.Tests/ManifestTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `NoMercy.Plugins.Mvc` resolvable from `_nupkgs`; manifest declares `"rest": true`.

- [ ] **Step 1: Add `NoMercy.Plugins.Mvc` to both scripts**

In `scripts/fetch-abstractions.sh`, add it to the sparse list and to the pack loop:

```sh
git -C "$server" sparse-checkout add \
    src/NoMercy.Plugins.Abstractions src/NoMercy.Events src/NoMercy.Design src/NoMercy.Plugins.Mvc
```

```sh
for project in NoMercy.Events NoMercy.Design NoMercy.Plugins.Abstractions NoMercy.Plugins.Mvc; do
```

Replace the header comment that says Mvc is deliberately not packed with why it now is:

```sh
# NoMercy.Plugins.Mvc holds PluginControllerBase, which this plugin's controller inherits
# so a card's favourite toggle and the search field have somewhere to arrive. Its own
# assembly rather than a type in Abstractions on purpose: the base class must keep one
# identity across the load-context boundary, so it lives in the host's shared set, and
# putting it in Abstractions would force a Microsoft.AspNetCore.App FrameworkReference on
# every plugin - including the ones that never serve a request.
```

Make the same three changes in `scripts/fetch-abstractions.ps1`. The two scripts are twins and have drifted before.

- [ ] **Step 2: Pack the contract**

```bash
./scripts/fetch-abstractions.sh
```

Expected: four `.nupkg` files in `_nupkgs`, including `NoMercy.Plugins.Mvc.0.1.404.nupkg`.

- [ ] **Step 3: Reference it from the plugin csproj**

Beside the existing `NoMercy.Plugins.Abstractions` reference, with the same floating version and the same reasoning:

```xml
        <!--
            Host-owned exactly like the abstractions: PluginControllerBase must keep one
            identity across the load-context boundary. Never shipped beside the plugin.
        -->
        <PackageReference Include="NoMercy.Plugins.Mvc" Version="*" />
```

- [ ] **Step 4: Write the failing manifest test**

Replace the existing `Manifest_DeclaresNeitherRestNorWs` in `ManifestTests.cs` with:

```csharp
    // rest is what carries a favourite toggle and a search submit back to the plugin:
    // CallPlugin reaches a plugin over REST or the hub, and nothing else does. ws stays
    // false - nothing here reports progress, and declaring a transport with no handler is
    // a promise the plugin cannot keep.
    [Fact]
    public void Manifest_DeclaresRestButNotWs()
    {
        PluginCapabilities capabilities = LoadManifest().Capabilities!;

        capabilities.Rest.Should().BeTrue();
        capabilities.Ws.Should().BeFalse();
    }
```

- [ ] **Step 5: Run it to confirm it fails**

Run: `"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --filter FullyQualifiedName~ManifestTests`
Expected: FAIL — the manifest still reads `"rest": false`.

- [ ] **Step 6: Flip the manifest**

In `plugin.json`, set `"rest": true`. Leave `"ws": false`.

- [ ] **Step 7: Add `NoMercy.Plugins.Mvc.dll` to the CI host-owned assertion**

In `.forgejo/workflows/build.yml`, extend the pattern in the "Stage the plugin directory" step:

```sh
          if ls "$BIN" | grep -E '^(NoMercy\.(Plugins\.(Abstractions|Mvc)|Events|Design)|Newtonsoft\.Json)\.dll$'; then
```

- [ ] **Step 8: Build, test, commit**

```bash
"$USERPROFILE/.dotnet/dotnet.exe" build -c Release -p:TreatWarningsAsErrors=true
"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --no-build
git add -A
git commit -m "build(contract): pack NoMercy.Plugins.Mvc and declare the rest transport"
```

---

### Task 3: The per-user state store

One file, one lock, atomic writes. Favourites and the last search term share it because they need the same thing: something per-user that survives a form submit.

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/State/UserState.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/State/UserStateStore.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/State/UserStateStoreTests.cs`

**Interfaces:**
- Consumes: `RadioStation`.
- Produces:
  - `UserState` — `Favourites : IReadOnlyList<RadioStation>`, `LastSearch : string?`
  - `UserStateStore(string dataFolderPath)`
  - `Task<UserState> GetAsync(string userId, CancellationToken ct)`
  - `Task<bool> AddFavouriteAsync(string userId, RadioStation station, CancellationToken ct)` — false when already present
  - `Task<bool> RemoveFavouriteAsync(string userId, string stationId, CancellationToken ct)` — false when absent
  - `Task SetLastSearchAsync(string userId, string? term, CancellationToken ct)`

- [ ] **Step 1: Write the failing tests**

`tests/NoMercy.Plugin.InternetRadio.Tests/State/UserStateStoreTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.State;

public class UserStateStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "nm-radio-state-" + Guid.NewGuid().ToString("N"));

    private UserStateStore Store() => new(_folder);

    private static RadioStation Station(string id) =>
        new() { Id = id, Name = $"Station {id}", StreamUrl = $"https://example.com/{id}" };

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    // A cold store is empty, not an exception. The first read happens before anything
    // has ever been written, on every fresh install.
    [Fact]
    public async Task GetAsync_ReturnsEmptyStateWhenNothingWasEverWritten()
    {
        UserState state = await Store().GetAsync("user-1", default);

        state.Favourites.Should().BeEmpty();
        state.LastSearch.Should().BeNull();
    }

    [Fact]
    public async Task AddFavouriteAsync_StoresTheWholeRecord()
    {
        UserStateStore store = Store();
        await store.AddFavouriteAsync("user-1", Station("a"), default);

        RadioStation stored = (await store.GetAsync("user-1", default)).Favourites.Single();
        stored.Id.Should().Be("a");
        stored.StreamUrl.Should().Be("https://example.com/a");
    }

    // The whole point of per-user state. One user's list must not be readable or
    // damageable by another's write.
    [Fact]
    public async Task Favourites_AreSeparatePerUser()
    {
        UserStateStore store = Store();
        await store.AddFavouriteAsync("user-1", Station("a"), default);
        await store.AddFavouriteAsync("user-2", Station("b"), default);

        (await store.GetAsync("user-1", default)).Favourites.Single().Id.Should().Be("a");
        (await store.GetAsync("user-2", default)).Favourites.Single().Id.Should().Be("b");
    }

    [Fact]
    public async Task AddFavouriteAsync_IsIdempotentAndReportsIt()
    {
        UserStateStore store = Store();

        (await store.AddFavouriteAsync("user-1", Station("a"), default)).Should().BeTrue();
        (await store.AddFavouriteAsync("user-1", Station("a"), default)).Should().BeFalse();

        (await store.GetAsync("user-1", default)).Favourites.Should().HaveCount(1);
    }

    // Removal needs no resolution, so an unknown id is a no-op - but it must say so
    // rather than claim it removed something.
    [Fact]
    public async Task RemoveFavouriteAsync_ReportsWhetherAnythingWasThere()
    {
        UserStateStore store = Store();
        await store.AddFavouriteAsync("user-1", Station("a"), default);

        (await store.RemoveFavouriteAsync("user-1", "a", default)).Should().BeTrue();
        (await store.RemoveFavouriteAsync("user-1", "a", default)).Should().BeFalse();
    }

    [Fact]
    public async Task SetLastSearchAsync_RoundTrips()
    {
        UserStateStore store = Store();
        await store.SetLastSearchAsync("user-1", "groove salad", default);

        (await store.GetAsync("user-1", default)).LastSearch.Should().Be("groove salad");
    }

    // Two users clicking at the same moment is the ordinary case on a family server, and
    // a torn write loses every user's list rather than one's.
    [Fact]
    public async Task ConcurrentWritesFromDifferentUsersAllSurvive()
    {
        UserStateStore store = Store();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            store.AddFavouriteAsync($"user-{i}", Station($"s{i}"), default)));

        foreach (int i in Enumerable.Range(0, 20))
        {
            (await store.GetAsync($"user-{i}", default)).Favourites.Single().Id.Should().Be($"s{i}");
        }
    }

    // A file that is not valid JSON must not take the plugin down. Losing favourites is
    // bad; refusing to render any screen is worse.
    [Fact]
    public async Task GetAsync_TreatsAnUnreadableFileAsEmpty()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, "user-state.json"), "{ not json");

        (await Store().GetAsync("user-1", default)).Favourites.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --filter FullyQualifiedName~UserStateStoreTests`
Expected: FAIL — `UserState` and `UserStateStore` do not exist.

- [ ] **Step 3: Write `UserState.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json.Serialization;

namespace NoMercy.Plugin.InternetRadio;

// What the plugin remembers about one viewer.
//
// Favourites hold the whole station record, not an id. A station found by search is by
// definition usually not in the sweep, so an id would point at nothing the next day: no
// name, no stream, no logo. A favourite that outlives its source is the entire point.
public sealed record UserState
{
    [JsonPropertyName("favourites")]
    public IReadOnlyList<RadioStation> Favourites { get; init; } = [];

    /// <summary>
    /// The term, never the results. Re-running the query on render means what is on
    /// screen is what the database says now, and there is no second thing to invalidate.
    /// </summary>
    [JsonPropertyName("lastSearch")]
    public string? LastSearch { get; init; }
}
```

- [ ] **Step 4: Write `UserStateStore.cs`**

Implement against the interface above, with:

- One `SemaphoreSlim(1, 1)` guarding every read-modify-write.
- Writes to `user-state.json.tmp` in the same directory, then `File.Move(tmp, final, overwrite: true)`. Same directory so the move is atomic rather than a cross-volume copy.
- `Directory.CreateDirectory(dataFolderPath)` before the first write.
- A read that throws `JsonException`, `IOException` or `UnauthorizedAccessException` returns an empty map rather than propagating.
- Serialisation of the whole `Dictionary<string, UserState>` on every write. The file holds a handful of users and a few dozen stations each; incremental writing would buy nothing and cost the atomicity.

- [ ] **Step 5: Run to verify they pass**

```bash
"$USERPROFILE/.dotnet/dotnet.exe" build -c Release -p:TreatWarningsAsErrors=true
"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --no-build --filter FullyQualifiedName~UserStateStoreTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(state): per-user favourites and last search, written atomically"
```

---

### Task 4: Resolving a station id, and the controller

A button carries nothing but its path, so `POST favourites/toggle/{id}` arrives with an id alone. Resolution is what turns that into a record worth storing.

**Files:**
- Create: `src/NoMercy.Plugin.InternetRadio/State/FavouriteResolver.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Controllers/InternetRadioController.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/State/FavouriteResolverTests.cs`

**Interfaces:**
- Consumes: `StationCatalog`, `RadioBrowserClient.GetByUuidsAsync`, `UserStateStore`.
- Produces:
  - `FavouriteResolver(StationCatalog catalog, RadioBrowserClient client)`
  - `Task<RadioStation?> ResolveAsync(string stationId, CancellationToken ct)`
  - `InternetRadioController` with `[HttpPost("favourites/toggle/{stationId}")]` and `[HttpPost("search")]`

- [ ] **Step 1: Write the failing resolver tests**

Cover, each as its own `[Fact]`, using a fake `HttpMessageHandler` for the client:

1. `ResolveAsync` returns the catalogue's record when the id is in the catalogue, and makes **no** HTTP call.
2. An id absent from the catalogue but a valid UUID resolves through `GetByUuidsAsync`.
3. A resolved-by-UUID station that fails `StationGates.Admits` returns `null` — a favourite that cannot play is not worth storing.
4. An id that is neither in the catalogue nor a well-formed UUID returns `null` without any HTTP call. This is the user-override slug case, and a slug would never resolve upstream.
5. An HTTP failure during resolution returns `null` rather than throwing.

- [ ] **Step 2: Run to verify they fail**

Run: `"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --filter FullyQualifiedName~FavouriteResolverTests`
Expected: FAIL — `FavouriteResolver` does not exist.

- [ ] **Step 3: Write `FavouriteResolver.cs`**

Order is catalogue, then override-aware catalogue lookup, then `GetByUuidsAsync`. The catalogue already merges the user's `stations.json`, so a single catalogue lookup covers both of the first two cases. Guard the upstream call with `Guid.TryParse` so a slug never becomes a wasted request.

- [ ] **Step 4: Write the controller**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using Microsoft.AspNetCore.Mvc;
using NoMercy.Plugins.Mvc;

namespace NoMercy.Plugin.InternetRadio;

// The other half of an agreement with the views: every route template here is a constant
// the view reads too, so a rename cannot leave a button pointing at nothing. The client
// interpolates CallPlugin's method straight into the request path, and the convention
// prefixes the plugin's own id.
[ApiController]
public sealed class InternetRadioController(InternetRadioPlugin plugin) : PluginControllerBase
{
    public const string ToggleFavouriteRouteTemplate = "favourites/toggle/{stationId}";
    public const string SearchMethod = "search";

    public sealed record SearchRequest(string? Query);

    [HttpPost(ToggleFavouriteRouteTemplate)]
    public Task<IActionResult> ToggleFavourite(string stationId, CancellationToken ct) =>
        plugin.ToggleFavouriteAsync(User.UserId().ToString(), stationId, ct);

    [HttpPost(SearchMethod)]
    public Task<IActionResult> Search([FromBody] SearchRequest request, CancellationToken ct) =>
        plugin.StoreSearchAsync(User.UserId().ToString(), request.Query, ct);
}
```

If `User.UserId()` is not reachable from `PluginControllerBase`, read the user from `PluginViewRequest.UserId`'s equivalent on the request — check `PluginControllerBase` for the sanctioned accessor before inventing one, and do not fall back to a header.

- [ ] **Step 5: Add the plugin methods the controller calls**

On `InternetRadioPlugin`:

- `Task<IActionResult> ToggleFavouriteAsync(string userId, string stationId, CancellationToken ct)` — reads state, removes when present (always succeeds), otherwise resolves and adds. Returns the `{ status, message }` envelope with a failure status and the message `"That station could not be found."` when resolution returns null. Never writes a hollow entry.
- `Task<IActionResult> StoreSearchAsync(string userId, string? query, CancellationToken ct)` — trims, stores null for blank, returns success.

- [ ] **Step 6: Build, test, commit**

```bash
"$USERPROFILE/.dotnet/dotnet.exe" build -c Release -p:TreatWarningsAsErrors=true
"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --no-build
git add -A
git commit -m "feat(favourites): resolve a station id and toggle it for the calling user"
```

---

### Task 5: Live search

**Files:**
- Modify: `src/NoMercy.Plugin.InternetRadio/Catalog/RadioBrowserClient.cs`
- Modify: `src/NoMercy.Plugin.InternetRadio/Views/RadioRoutes.cs`
- Create: `src/NoMercy.Plugin.InternetRadio/Views/SearchView.cs`
- Modify: `src/NoMercy.Plugin.InternetRadio/InternetRadioPlugin.cs`
- Modify: `tests/NoMercy.Plugin.InternetRadio.Tests/Catalog/RadioBrowserClientTests.cs`
- Modify: `tests/NoMercy.Plugin.InternetRadio.Tests/Views/RadioRoutesTests.cs`
- Create: `tests/NoMercy.Plugin.InternetRadio.Tests/Views/SearchViewTests.cs`

**Interfaces:**
- Consumes: `StationGates`, `UserStateStore`.
- Produces:
  - `RadioBrowserClient.SearchByNameAsync(string term, int limit, CancellationToken ct) : Task<IReadOnlyList<RadioBrowserStation>>`
  - `RadioBrowserClient.SearchLimit : int` (value `50`)
  - `RadioRoutes.Search : string` (value `"/search"`), and `RadioRouteKind.Search`
  - `SearchView.Build(string? term, IReadOnlyList<RadioStation> results, bool queryFailed) : PluginView`

- [ ] **Step 1: Write the failing client test**

Assert that `SearchByNameAsync("groove salad", 50, ct)` issues a GET whose path is `/json/stations/search` and whose query contains `name=groove%20salad`, `limit=50`, `order=votes`, `reverse=true` and `hidebroken=true`; that a non-success status yields an empty list rather than throwing; and that a malformed row does not lose the whole response.

- [ ] **Step 2: Write the failing route test**

```csharp
    [Fact]
    public void Parse_RecognisesTheSearchRoute()
    {
        RadioRoutes.Parse("/search").Kind.Should().Be(RadioRouteKind.Search);
    }
```

- [ ] **Step 3: Run both to verify they fail**

Run: `"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --filter "FullyQualifiedName~RadioBrowserClientTests|FullyQualifiedName~RadioRoutesTests"`
Expected: FAIL.

- [ ] **Step 4: Implement `SearchByNameAsync` and the route**

Add `SearchLimit = 50` as a public const with the spec's reasoning in a doc comment. Add `Search` to `RadioRouteKind`, `RadioRoutes.Search = "/search"`, and the `"search"` case to the single-segment switch in `Parse`.

- [ ] **Step 5: Write `SearchView.cs`**

`Build(term, results, queryFailed)` renders, in order: the search form, then one of three states.

```csharp
    public const string FieldName = "query";

    private static PluginComponent Field(string? term) =>
        PluginViews.Form(
            "search-form",
            "Search",
            PluginActionIntent.CallPlugin(InternetRadioController.SearchMethod),
            new PluginFormField
            {
                Name = FieldName,
                Label = "Search stations",
                Type = PluginFormFieldType.Text,
                Value = term,
                Placeholder = "Station name",
            });
```

The three states, which must not look alike:

- `queryFailed` — `PluginViews.EmptyState("search-failed", "Search is unavailable", "radio-browser did not answer. Try again in a moment.")`
- no term — `PluginViews.EmptyState("search-idle", "Search for a station", "Type a name to find stations anywhere in the radio-browser database.")`
- a term with no results — `PluginViews.EmptyState("search-empty", "Nothing found", $"No playable station matches \"{term}\".")`

Results render as a grid of `StationCards.WithFavourite(...)` from Task 6.

- [ ] **Step 6: Route `/search` in the plugin**

In `GetViewAsync`, add `RadioRouteKind.Search`. The plugin reads the stored term, and when it is non-blank runs `SearchByNameAsync`, gates and maps the results to `RadioStation`, and passes them to `SearchView.Build`. A thrown or failed query sets `queryFailed: true` rather than surfacing an exception. **The network call belongs here, not in the view** — views stay pure `Build(...)` methods.

- [ ] **Step 7: Write `SearchViewTests.cs`**

One test per state above, plus: the field renders the stored term as its value so a submitted query is still visible; and a result card carries a `playMedia` action.

- [ ] **Step 8: Build, test, commit**

```bash
"$USERPROFILE/.dotnet/dotnet.exe" build -c Release -p:TreatWarningsAsErrors=true
"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --no-build
git add -A
git commit -m "feat(search): find any station in radio-browser, on its own route"
```

---

### Task 6: The favourite toggle and the cover placeholder on a card

`PluginViews.Card` takes exactly one action, and the card's action is already `playMedia`. So the toggle is a button beside the card, not inside it.

**Files:**
- Modify: `src/NoMercy.Plugin.InternetRadio/Views/StationCards.cs`
- Modify: `tests/NoMercy.Plugin.InternetRadio.Tests/Views/StationCardsTests.cs`

**Interfaces:**
- Consumes: `InternetRadioController.ToggleFavouriteRouteTemplate`.
- Produces:
  - `StationCards.WithFavourite(RadioStation station, bool isFavourite) : PluginComponent`
  - `StationCards.CoverUrl(RadioStation station) : string?`

- [ ] **Step 1: Write the failing tests**

1. `WithFavourite(station, isFavourite: false)` contains the play card and a button whose action is `CallPlugin` with method `favourites/toggle/{id}`.
2. The button's label and icon differ between favourited and not, so the state is readable without colour alone.
3. `CoverUrl` returns `station.LogoUrl` when it is a well-formed absolute https URL.
4. `CoverUrl` returns `null` for a blank, relative, or non-https logo — the placeholder is the client's job once the URL is absent, and a mixed-content image is a broken icon on every https dashboard.
5. `WithFavourite` renders the same card shape whether or not a cover exists, so a missing logo does not change the grid's geometry.

- [ ] **Step 2: Run to verify they fail**

Run: `"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --filter FullyQualifiedName~StationCardsTests`
Expected: FAIL — neither member exists.

- [ ] **Step 3: Implement both**

`CoverUrl` gates the logo the same way `StationGates` gates a stream, and for the same reason: an http image on an https dashboard is blocked as mixed content and renders as a broken icon, which looks like our bug.

`WithFavourite` wraps `Play(station)` and the toggle button in `PluginViews.Row($"station-row-{station.Id}", …)`.

- [ ] **Step 4: Thread `UserState` into the views, then use the toggle everywhere**

`WithFavourite` needs to know whether this station is already favourited, which no view can answer from the catalogue alone. So this step widens the view signatures — it is not Task 7's job, because the toggle is unusable until it happens:

- `BrowseView.Build(StationCatalog catalog, UserState state)`
- `GenreView.Build(StationCatalog catalog, string slug, UserState state)`
- `StationView.Build(StationCatalog catalog, string id, UserState state)`
- `SettingsView` gains a `UserState state` parameter alongside its existing ones

`GetViewAsync` loads the state **once per request** and passes it to whichever view needs it — one read per request, never one per card.

Then replace every direct `StationCards.Play(...)` call in `BrowseView`, `GenreView`, `StationView` and `SearchView` with `WithFavourite(station, state.Favourites.Any(f => f.Id == station.Id))`.

`AllStationsView` is a table and keeps its row shape. Add the toggle as a cell action there only if the table's cell contract supports one; if it does not, leave the table alone and say so in the commit message rather than distorting the table to fit.

- [ ] **Step 5: Add the favourites count to `/settings`**

The spec calls for it, and it is the one place an owner can see that favourites are being stored at all:

```csharp
        children.Add(PluginViews.Text(
            "settings-favourites-count",
            state.Favourites.Count == 1
                ? "1 favourite station."
                : $"{state.Favourites.Count} favourite stations.",
            "caption"));
```

Add a `SettingsViewTests` case for zero, one and several, since the singular is easy to get wrong and reads as a bug to whoever has exactly one.

- [ ] **Step 6: Build, test, commit**

```bash
"$USERPROFILE/.dotnet/dotnet.exe" build -c Release -p:TreatWarningsAsErrors=true
"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --no-build
git add -A
git commit -m "feat(ui): a favourite toggle beside every station, and a gated cover url"
```

---

### Task 7: Recompose the browse page

**Files:**
- Modify: `src/NoMercy.Plugin.InternetRadio/Views/BrowseView.cs`
- Modify: `src/NoMercy.Plugin.InternetRadio/InternetRadioPlugin.cs`
- Modify: `tests/NoMercy.Plugin.InternetRadio.Tests/Views/BrowseViewTests.cs`

**Interfaces:**
- Consumes: `SearchView.Field`, `StationCards.WithFavourite`, `UserState`.
- Produces: `BrowseView.Build(StationCatalog catalog, UserState state) : PluginView`

- [ ] **Step 1: Write the failing tests**

1. The search field is the first component after the page title.
2. A user with favourites gets a "Favourites" section before the genre chips, holding one card per favourite.
3. A user with none gets **no** favourites section at all — not an empty one.
4. The genre chips and the popular grid still render as before, and "Popular" is ordered by popularity descending.
5. A favourited station in the popular grid shows the toggle in its favourited state, so the same station cannot read two ways on one screen.

- [ ] **Step 2: Run to verify they fail**

Run: `"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --filter FullyQualifiedName~BrowseViewTests`
Expected: FAIL — `Build` still takes one argument.

- [ ] **Step 3: Implement the recomposition**

`Build` already takes `UserState` — Task 6 widened the signature so the toggle could work. This task changes only what the page is made of, in this order: title, search field, favourites section, genre chips, popular grid.

- [ ] **Step 4: Emit the payloads and look at them**

```bash
NM_PAYLOAD_OUT=./_payloads "$USERPROFILE/.dotnet/dotnet.exe" test -c Release --no-build --filter FullyQualifiedName~EmitPayloadsForRendering
```

Read `_payloads/radio-browse.json` and `_payloads/radio-search.json`. A green view test says the payload has the shape the test expects; it cannot say the screen draws. Check the favourites section sits where it should, the toggle is a sibling of the card and not a child of it, and no text leaf shares a line with another.

Add `radio-search.json` and a favourites-populated browse case to `EmitPayloadsForRendering` so both new screens are emitted.

- [ ] **Step 5: Build, test, commit**

```bash
"$USERPROFILE/.dotnet/dotnet.exe" build -c Release -p:TreatWarningsAsErrors=true
"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --no-build
git add -A
git commit -m "feat(ui): search and favourites land on the page you arrive at"
```

---

### Task 8: Answer the consent question, then bump to 1.1.0

The bump is trivial. The question in front of it is not, and it is the one that decides whether this release breaks a working install.

**Files:**
- Modify: `src/NoMercy.Plugin.InternetRadio/plugin.json`
- Modify: `src/NoMercy.Plugin.InternetRadio/NoMercy.Plugin.InternetRadio.csproj`
- Modify: `src/NoMercy.Plugin.InternetRadio/PluginIdentity.cs`
- Modify: `tests/NoMercy.Plugin.InternetRadio.Tests/ManifestTests.cs`
- Modify: `src/NoMercy.Plugin.InternetRadio/README.md`
- Create: `docs/upstream/2026-08-08-consent-widening.md`

- [ ] **Step 1: Establish what happens when a capability is added to an installed plugin**

Read, in the server at `origin/dev`: `PluginManager`'s consent and grant handling, `IPluginGrants`, `PluginGrantKind`, and the trust path added in `586be1c`. Answer in writing, in `docs/upstream/2026-08-08-consent-widening.md`:

- Does an update whose manifest declares a capability the stored grant does not cover re-prompt, inherit silently, or fail to load?
- If it fails closed, what does the owner see, and is there a migration path short of remove-and-reinstall?

**Do not proceed past this step on an assumption.** If the server's behaviour cannot be established by reading, say so in the document and stop — a release that silently disables itself on every install that already has it is exactly the failure this project exists to avoid.

- [ ] **Step 2: Write the failing version test**

In `ManifestTests.cs`, change `Manifest_VersionIsExactly_1_0_2` to:

```csharp
    [Fact]
    public void Manifest_VersionIsExactly_1_1_0()
    {
        LoadManifest().Version.Should().Be("1.1.0");
    }
```

- [ ] **Step 3: Run to verify it fails**

Run: `"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --filter FullyQualifiedName~ManifestTests`
Expected: FAIL — the manifest reads `1.0.2`.

- [ ] **Step 4: Bump all three declarations**

`plugin.json` `"version": "1.1.0"`, csproj `<Version>1.1.0</Version>`, and `PluginIdentity.Version` to `new(1, 1, 0)`. All three, or `ManifestTests` fails on the next push — which is the guard working.

- [ ] **Step 5: Update the plugin README**

Describe search and favourites, and drop any mention of curated stations. This file ships beside the DLL, so it is what an owner reads after installing.

- [ ] **Step 6: Full verification**

```bash
rm -rf _nupkgs
./scripts/fetch-abstractions.sh
"$USERPROFILE/.dotnet/dotnet.exe" restore
"$USERPROFILE/.dotnet/dotnet.exe" build -c Release -p:TreatWarningsAsErrors=true
"$USERPROFILE/.dotnet/dotnet.exe" test -c Release --no-build
```

Expected: clean build, 0 warnings, all pass.

- [ ] **Step 7: Confirm the three versions agree**

```bash
grep -o '"version": "[^"]*"' src/NoMercy.Plugin.InternetRadio/plugin.json
grep -o '<Version>[^<]*</Version>' src/NoMercy.Plugin.InternetRadio/*.csproj
grep -o 'new(1, 1, 0)' src/NoMercy.Plugin.InternetRadio/PluginIdentity.cs
```

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "release: 1.1.0 - discovery, search and favourites"
```

- [ ] **Step 9: Push and tag only on explicit ask**, then watch CI.

```bash
git push origin main
git tag v1.1.0 && git push origin v1.1.0
```

---

## Manual verification before tagging

- [ ] **The consent question is answered in writing** in `docs/upstream/2026-08-08-consent-widening.md`, and the answer does not break an existing install.
- [ ] **No station identifier remains in the source tree.**

```bash
grep -rn --include='*.cs' --exclude-dir=obj --exclude-dir=bin -E 'https?://' src/NoMercy.Plugin.InternetRadio \
  | grep -v 'radio-browser' | grep -v 'forgejo.phillippepelzer.me' | grep -v 'github.com'
```

- [ ] **Search finds a station the sweep does not return, and it plays.**
- [ ] **A favourited search result still renders after `RefreshAsync`**, with its name, stream and cover intact.
- [ ] **Two user ids have different favourites**, and neither sees the other's.
- [ ] **Every station shown carries a cover or a deliberate placeholder** — checked by reading the emitted payloads, not only by green tests.
