// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.Views;

namespace NoMercy.Plugin.InternetRadio.Playback;

/// <summary>A playable url for a station, minted for whoever is asking right now.</summary>
public interface IStationStreams
{
    Task<StationStream> ForAsync(RadioStation station, CancellationToken ct);
}
