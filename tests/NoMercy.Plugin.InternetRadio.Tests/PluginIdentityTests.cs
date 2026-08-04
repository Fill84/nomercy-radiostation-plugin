// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests;

// The server moved plugin identity from Guid to Ulid, so this plugin's id had to be
// rewritten in a new format. A change to the value the host keys lifecycle state off
// is the one change that turns an update into "a different plugin appeared and the
// old one vanished", taking the approval an operator gave it with it - the manifest
// declares a network host, so a plugin the server has never seen starts disabled.
//
// So these do not assert that the id is *a* Ulid. They assert it is the same 128 bits
// the plugin has shipped since 1.0.0, in both directions.
public class PluginIdentityTests
{
    private const string HistoricalGuid = "b3d4f1a2-7c5e-4d8a-9f10-1c2b3a4d5e6f";
    private const string HistoricalUlid = "5KTKRT4Z2Y9P59Y40W5CX4TQKF";

    [Fact]
    public void Id_IsStillTheValueThePluginShippedWith()
    {
        PluginIdentity.Id.ToGuid().Should().Be(new Guid(HistoricalGuid));
    }

    [Fact]
    public void Id_RendersAsTheUlidTheManifestCarries()
    {
        PluginIdentity.Id.ToString().Should().Be(HistoricalUlid);
    }

    // Both constants written out rather than derived from PluginIdentity: an assertion
    // that computes its own expectation from the thing under test moves with it, and
    // would keep passing through exactly the reissue this file exists to catch.
    [Fact]
    public void TheTwoRenderingsAreTheSameIdentity()
    {
        Ulid.Parse(HistoricalUlid).ToGuid().Should().Be(new Guid(HistoricalGuid));
    }
}
