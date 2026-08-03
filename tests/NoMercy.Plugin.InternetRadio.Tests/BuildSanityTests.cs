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
