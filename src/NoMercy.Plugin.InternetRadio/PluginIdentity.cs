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
    public static Ulid Id { get; } = Ulid.Parse("5KTKRT4Z2Y9P59Y40W5CX4TQKF");

    public const string Name = "Internet Radio";

    public const string Description =
        "Browse and play internet radio stations in the built-in player.";

    public static Version Version { get; } = new(1, 0, 2);

    public const string AssemblyFileName = "NoMercy.Plugin.InternetRadio.dll";
}
