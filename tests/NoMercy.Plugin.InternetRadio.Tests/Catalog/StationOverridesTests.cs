// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Reflection;
using System.Text.Json.Serialization;
using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Catalog;

public class StationOverridesTests
{
    private static IEnumerable<string> JsonPropertyNames(Type type) =>
        type.GetProperties()
            .Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Name);

    // The highest-risk failure mode this design introduces: OverrideEntry exists
    // only so a hand-written stations.json can omit fields RadioStation requires,
    // and it is hand-maintained rather than generated. If a later task adds a
    // property to RadioStation without a matching one here, every user's file
    // silently loses that field with no error at all - this makes that drift a
    // build failure instead.
    [Fact]
    public void MirrorsEveryRadioStationPropertyExceptIsUserSupplied()
    {
        IEnumerable<string> stationNames =
            JsonPropertyNames(typeof(RadioStation)).Where(name => name != "isUserSupplied");
        IEnumerable<string> entryNames = JsonPropertyNames(typeof(StationOverrides.OverrideEntry));

        entryNames.Should().BeEquivalentTo(stationNames);
    }
}
