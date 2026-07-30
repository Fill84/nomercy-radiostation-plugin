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
