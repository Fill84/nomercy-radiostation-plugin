// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

public sealed record RadioStation
{
    public required string Name { get; init; }
    public required string StreamUrl { get; init; }
}
