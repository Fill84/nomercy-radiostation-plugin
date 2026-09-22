// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;

namespace NoMercy.Plugin.InternetRadio.Tests.TestSupport;

/// <summary>
/// A caller as the host builds one: the account's Guid, carried as a Ulid over the same
/// sixteen bytes (PluginUiController does exactly `new UserId(new Ulid(guid))`).
/// </summary>
public static class TestCaller
{
    public static PluginCaller For(Guid user) =>
        new(new UserId(new Ulid(user)), "Test", PluginRole.Member, PluginAccess.Shared, "en", PluginSurface.Web);

    public static PluginCaller Anyone { get; } = For(Guid.Parse("11111111-2222-3333-4444-555555555555"));
}
