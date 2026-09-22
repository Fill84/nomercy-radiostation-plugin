// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio.Views;

/// <summary>
/// A url the player can play, or the refusal code that explains why there is none.
///
/// A view reads this as data. Minting one needs the request being served and the host,
/// and a view has neither.
/// </summary>
public sealed record StationStream(string? Url, string? RefusalCode)
{
    public bool IsPlayable => Url is not null;

    public static StationStream Playable(string url) => new(url, null);

    public static StationStream Refused(string code) => new(null, code);
}
