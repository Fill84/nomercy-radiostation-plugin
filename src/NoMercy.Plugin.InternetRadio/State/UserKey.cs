// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// The key a viewer's favourites and last search are stored under.
///
/// One place, because the host now names the same person two ways. The controller reads
/// the account's Guid from its claim; a view is handed a UserId, which is a Ulid over the
/// same sixteen bytes and prints as a different string. Reading each as it comes wrote a
/// favourite under one key and looked for it under the other, so a toggle appeared to
/// do nothing - and every favourite saved by 1.x, all under the Guid, vanished from view.
///
/// The Guid form wins because that is what every byte of existing state is keyed by.
/// </summary>
public static class UserKey
{
    /// <summary>From the controller's NameIdentifier claim. Null for no one.</summary>
    public static string? FromClaim(string? claim) =>
        Guid.TryParse(claim, out Guid id) ? Of(id) : null;

    /// <summary>From the caller a view is built for. Null for no one.</summary>
    public static string? FromCaller(UserId caller) =>
        caller == UserId.Empty ? null : Of(caller.Value.ToGuid());

    // Null rather than a shared bucket for the empty id: every anonymous caller writing
    // into one list would be favourites belonging to nobody, visible to everybody.
    private static string? Of(Guid id) => id == Guid.Empty ? null : id.ToString();
}
