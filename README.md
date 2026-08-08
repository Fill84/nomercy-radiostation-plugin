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
| `docs/upstream/` | Findings that belong to the server or the client, not here |
| `docs/superpowers/` | The design spec and this implementation plan |

## No station data in the source tree

Not one station name, stream URL, logo, genre or country is committed here. Every
one of them is fetched from radio-browser at runtime, and the catalogue is
discovered by querying the most voted stations per genre rather than from a pinned
list of ids.

That is not tidiness. A hardcoded URL is one nobody re-checks: this repository has
already had to correct Tomorrowland URLs once, and shipped BBC streams over `http://`
that could never play in a browser. Anything wrong with a station is now fixed
upstream at radio-browser, where the fix reaches everyone rather than only whoever
updates this plugin.

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
