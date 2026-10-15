// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json;
using FluentAssertions;
using NoMercy.PluginSdk.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests;

public class RepositoryManifestTests
{
    // This is the "Build repository.json" step in .forgejo/workflows/build.yml -
    // its jq template with the values a real release fills in - not a shape invented
    // here. Keep the two in step: a field added, renamed, or reshaped in one belongs
    // in the other, and this test is what would notice if they drifted apart.
    //
    // The checksum is bare hex, no "sha256:" prefix. PluginPackageChecksum.Of on the
    // server hashes the downloaded .zip with SHA-256 and lowercases the hex digest,
    // then PluginPackageChecksum.Refuse compares that string directly against this
    // field - a prefixed value never matches, so the install fails verification
    // every time. The version property lives on PluginVersionEntry, not on this type.
    // `url` names the index file itself - this same repository.json, at its own
    // eventual download address - not the zip. `downloadUrl` inside the version entry
    // below is what points at the zip. The server never reads this top-level field.
    private const string Sample =
        """
        {
          "name": "NoMercy Internet Radio",
          "url": "https://forgejo.phillippepelzer.me/FiLL/nomercy-radiostation-plugin/releases/download/v2.0.0/repository.json",
          "plugins": [
            {
              "id": "5KTKRT4Z2Y9P59Y40W5CX4TQKF",
              "name": "Internet Radio",
              "description": "Browse and play internet radio stations in the built-in player.",
              "author": "NoMercy Community",
              "projectUrl": "https://forgejo.phillippepelzer.me/FiLL/nomercy-radiostation-plugin",
              "versions": [
                {
                  "version": "2.0.0",
                  "targetAbi": "12.1",
                  "downloadUrl": "https://forgejo.phillippepelzer.me/FiLL/nomercy-radiostation-plugin/releases/download/v2.0.0/NoMercy.Plugin.InternetRadio-2.0.0.zip",
                  "checksum": "df901954da3d51d6462a9b0de6c994225cd18e294260e231296b4e95df6d9ce9",
                  "timestamp": "2026-09-23T00:00:00Z"
                }
              ]
            }
          ]
        }
        """;

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

        // The shape, not the number: Sample is one past release, and the tree moves on
        // to the next patch the moment that release publishes. Pinning it to
        // PluginIdentity.Version turned main red on the bump after 2.0.0.
        PluginVersionEntry version = entry.Versions.Should().ContainSingle().Subject;
        version.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
        version.TargetAbi.Should().Be(ManifestTests.LoadManifest().TargetAbi);
        version.DownloadUrl.Should().EndWith(".zip");
        version.Checksum.Should().NotBeNullOrWhiteSpace();
    }

    // The one shape rule PluginPackageChecksum.Refuse enforces silently: it compares
    // the hex digest it computes straight against this field, so a "sha256:" prefix -
    // or any other decoration - is a checksum that can never match, on every install,
    // forever. This is the test that would have caught the workflow emitting one.
    [Fact]
    public void TheChecksumIsBareHexWithNoSchemePrefix()
    {
        PluginRepositoryManifest index = JsonSerializer.Deserialize<PluginRepositoryManifest>(Sample)!;
        string checksum = index.Plugins.Single().Versions.Single().Checksum!;

        checksum.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    private const string Releases =
        "https://forgejo.phillippepelzer.me/FiLL/nomercy-radiostation-plugin/releases/download";

    private static PluginRepositoryManifest LoadCommittedIndex()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "repository.json");
        PluginRepositoryManifest? index = JsonSerializer.Deserialize<PluginRepositoryManifest>(File.ReadAllText(path));

        index.Should().NotBeNull();
        return index!;
    }

    // The root repository.json is what the NoMercy plugin catalogue reads, at a URL
    // that does not move. If the server cannot read it, or it names another plugin,
    // the catalogue offers nothing - which is how an up-to-date server was left with
    // only 1.x, a release it refuses to load.
    [Fact]
    public void TheCommittedIndexIsOneTheServerCanRead()
    {
        PluginRepositoryManifest index = LoadCommittedIndex();

        index.Url.Should().EndWith("/raw/branch/main/repository.json");

        PluginRepositoryEntry entry = index.Plugins.Should().ContainSingle().Subject;
        entry.Id.Should().Be(PluginIdentity.Id);
        entry.Name.Should().Be(PluginIdentity.Name);
        entry.Versions.Should().NotBeEmpty();
    }

    // A server verifies the zip against this checksum and refuses on a mismatch, so a
    // "sha256:" prefix - which the 1.x release indexes carried - fails every install.
    [Fact]
    public void EveryListedVersionPointsAtItsOwnRelease()
    {
        foreach (PluginVersionEntry version in LoadCommittedIndex().Plugins.Single().Versions)
        {
            version.DownloadUrl.Should().Be(
                $"{Releases}/v{version.Version}/NoMercy.Plugin.InternetRadio-{version.Version}.zip");
            version.Checksum.Should().MatchRegex("^[0-9a-f]{64}$");
            version.TargetAbi.Should().NotBeNullOrWhiteSpace();
        }
    }

    // CI puts each release on top. Newest first, no version twice, and none ahead of
    // this tree: a listed version the tree has not reached yet was never shipped.
    [Fact]
    public void VersionsRunNewestFirstAndNoneIsAheadOfThisTree()
    {
        List<Version> versions = LoadCommittedIndex().Plugins.Single().Versions
            .Select(v => Version.Parse(v.Version))
            .ToList();

        versions.Should().OnlyHaveUniqueItems();
        versions.Should().BeInDescendingOrder();
        versions[0].Should().BeLessThanOrEqualTo(PluginIdentity.Version);
    }
}
