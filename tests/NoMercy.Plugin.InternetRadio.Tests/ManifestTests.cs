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
    //
    // No test here pins the concrete version string: the build's "Open the next
    // patch version" step bumps it on every release, and a hardcoded literal would
    // go stale the moment it did, failing every push until a human hand-edited it.
    // The tag/manifest agreement this exists to protect is asserted at tag time by
    // the CI gate instead, where the correct value - the tag just pushed - is known.
    [Fact]
    public void Manifest_VersionMatchesPluginIdentity()
    {
        Version.Parse(LoadManifest().Version).Should().Be(PluginIdentity.Version);
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
