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
//
// The server moved plugin identity from Guid to Ulid, so the TYPE here had to change -
// IPlugin.Id and PluginManifest.Id are both Ulid now, and a manifest still carrying a
// Guid string does not even deserialise on the host. The VALUE did not change. It is
// written as the original Guid and converted, rather than pasted in as a Ulid literal,
// so this stays visibly the same identity the plugin has always had instead of looking
// like a fresh id somebody generated. Ulid's own Guid constructor defines the mapping
// and ToGuid() reverses it, so the 128 bits are the platform's conversion, not one
// invented here - see PluginIdentityTests, which pins both directions.
//
// The manifest carries the Ulid rendering of exactly this value:
// b3d4f1a2-7c5e-4d8a-9f10-1c2b3a4d5e6f is 5KTKRT4Z2Y9P59Y40W5CX4TQKF.
public static class PluginIdentity
{
    public static Ulid Id { get; } = new(new Guid("b3d4f1a2-7c5e-4d8a-9f10-1c2b3a4d5e6f"));

    public const string Name = "Internet Radio";

    public const string Description =
        "Browse and play internet radio stations in the built-in player.";

    public static Version Version { get; } = new(1, 0, 2);

    public const string AssemblyFileName = "NoMercy.Plugin.InternetRadio.dll";
}
