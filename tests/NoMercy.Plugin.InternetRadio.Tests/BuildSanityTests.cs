// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;
using NoMercy.Plugin.InternetRadio.Catalog;

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

    // RadioBrowserClient.cs claims "those three are already required to agree (see
    // ManifestTests)" about plugin.json, the csproj's <Version> and PluginIdentity -
    // but ManifestTests only reads plugin.json, which never touches the csproj at
    // all. This is the missing third of that claim: the built assembly's own
    // version, which the csproj's <Version> becomes at compile time. Only the first
    // three parts are compared - AssemblyVersion always carries a fourth, and
    // nothing here sets or promises one.
    [Fact]
    public void AssemblyVersion_MatchesPluginIdentity()
    {
        Version? assemblyVersion = typeof(RadioStation).Assembly.GetName().Version;

        assemblyVersion.Should().NotBeNull();
        assemblyVersion!.Major.Should().Be(PluginIdentity.Version.Major);
        assemblyVersion.Minor.Should().Be(PluginIdentity.Version.Minor);
        assemblyVersion.Build.Should().Be(PluginIdentity.Version.Build);
    }
}
