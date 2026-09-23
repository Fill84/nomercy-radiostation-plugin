// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;

namespace NoMercy.Plugin.InternetRadio.Tests.TestSupport;

/// <summary>
/// A grants facade that remembers what it was asked, for tests that need to see
/// whether a call site asked at all. FakePluginContext's own NoGrants answers "held
/// nothing, asked nothing back" and is enough for tests that only need a page to
/// draw when nothing is granted; this one is for the tests that need to see the ask
/// itself, such as a lifecycle test that a refresh tick or a served view actually
/// requests both grants.
/// </summary>
public sealed class RecordingGrants(params string[] held) : IPluginGrants
{
    public List<(string Kind, string Value, string Reason)> Requested { get; } = [];

    public Task<bool> HasAsync(string kind, string value, CancellationToken ct = default) =>
        Task.FromResult(held.Contains($"{kind}={value}"));

    public Task<IReadOnlyList<string>> GetAsync(string kind, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task RequestAsync(string kind, string value, string reason, CancellationToken ct = default)
    {
        Requested.Add((kind, value, reason));
        return Task.CompletedTask;
    }
}
