// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

// Only what can be asserted without a network. Whether each UUID still resolves and
// still passes the gates is checked by scripts/resolve-seeds.py before a release -
// a unit test that reaches radio-browser would turn their outage into our red build.
public class SeedTests
{
    [Fact]
    public void Seeds_AreTheTenCuratedStations()
    {
        SeedStations.Uuids.Should().HaveCount(10);
    }

    [Fact]
    public void Seeds_AreWellFormedGuids()
    {
        // FluentAssertions renders this predicate as an expression tree, and an
        // expression tree may not contain an out-parameter discard (CS8207), so the
        // TryParse call is wrapped in an ordinary method instead of inlined here.
        SeedStations.Uuids.Should().OnlyContain(uuid => IsWellFormedGuid(uuid));
    }

    private static bool IsWellFormedGuid(string uuid) => Guid.TryParse(uuid, out _);

    // A duplicate would ask radio-browser for the same station twice and then rely on
    // dedupe to hide it, which is a silent way to be one station short of the ten.
    [Fact]
    public void Seeds_AreUnique()
    {
        SeedStations.Uuids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void PerGenreLimit_IsPositive()
    {
        SeedStations.PerGenreLimit.Should().BeGreaterThan(0);
    }
}
