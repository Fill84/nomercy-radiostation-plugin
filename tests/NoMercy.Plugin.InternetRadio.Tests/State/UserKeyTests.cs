// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.PluginSdk.Abstractions;
using Xunit;
using NoMercy.Plugin.InternetRadio.State;

namespace NoMercy.Plugin.InternetRadio.Tests.State;

public class UserKeyTests
{
    private static readonly Guid Someone = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    // The whole point: the controller hears a Guid claim and a view hears a Ulid over the
    // same bytes. If these two ever disagree, a favourite is written where no page reads.
    [Fact]
    public void TheControllersClaimAndTheViewsCallerAreTheSameKey()
    {
        string? fromClaim = UserKey.FromClaim(Someone.ToString());
        string? fromCaller = UserKey.FromCaller(new UserId(new Ulid(Someone)));

        fromClaim.Should().NotBeNull();
        fromCaller.Should().Be(fromClaim);
    }

    // 1.x stored every user under Guid.ToString(). Anything else orphans their favourites.
    [Fact]
    public void TheKeyIsTheGuidFormOneXStoredUnder()
    {
        UserKey.FromCaller(new UserId(new Ulid(Someone))).Should().Be("0f8fad5b-d9cb-469f-a165-70867728950e");
        UserKey.FromClaim("0F8FAD5B-D9CB-469F-A165-70867728950E").Should().Be("0f8fad5b-d9cb-469f-a165-70867728950e");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void NoOneHasNoKey(string? claim)
    {
        UserKey.FromClaim(claim).Should().BeNull();
    }

    [Fact]
    public void AnEmptyCallerHasNoKey()
    {
        UserKey.FromCaller(UserId.Empty).Should().BeNull();
    }
}
