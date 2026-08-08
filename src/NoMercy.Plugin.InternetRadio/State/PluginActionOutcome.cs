// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// What a CallPlugin action did, in the shape the controller turns into an envelope.
///
/// A message on success as well as on failure: the client refreshes the view after a
/// call, and a toggle that changed nothing looks identical to one that worked unless the
/// response says which it was.
/// </summary>
public sealed record PluginActionOutcome(bool Succeeded, string Message)
{
    public static PluginActionOutcome Ok(string message) => new(true, message);

    public static PluginActionOutcome Failed(string message) => new(false, message);
}
