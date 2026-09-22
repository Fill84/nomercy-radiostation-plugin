// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// A station's logo, as a view may draw it: a data: URI, or null for the placeholder.
///
/// An interface so views stay pure functions. The plugin hands its cache in; a test hands
/// in whatever it needs; nothing under Views/ ever reaches the network for one.
/// </summary>
public interface IStationLogos
{
    string? DataUriFor(RadioStation station);
}

/// <summary>No logos: every card draws its own placeholder.</summary>
public sealed class NoStationLogos : IStationLogos
{
    public static NoStationLogos Instance { get; } = new();

    public string? DataUriFor(RadioStation station) => null;
}
