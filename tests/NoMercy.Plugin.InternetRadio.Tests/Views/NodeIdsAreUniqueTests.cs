// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.TestSupport;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

// One station legitimately appears twice on the browse page - kept in the favourites row
// and popular in the grid below it. Before the ids were scoped by section, both were the
// same node id in one payload, and a client keying on id then has two elements claiming
// to be the same thing.
//
// That was found by reading an emitted payload, not by a failing test, because every
// structural assertion passed: each section held the right cards, and nothing compares
// sections to each other. This is the assertion that would have caught it.
public class NodeIdsAreUniqueTests
{
    private static RadioStation Station(string id, string genre = "Ambient") =>
        new()
        {
            Id = id,
            Name = $"Station {id}",
            StreamUrl = $"https://example.com/{id}",
            Genre = genre,
            Popularity = 1,
        };

    private static StationCatalog Catalog() =>
        StationCatalog.Create(
            [Station("a"), Station("b"), Station("c", "Rock")],
            CatalogSource.Fetched,
            DateTimeOffset.UtcNow);

    public static TheoryData<string, PluginView> EveryScreen()
    {
        StationCatalog catalog = Catalog();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // The state that overlaps hardest: two favourites, both also in the catalogue, so
        // every station on the page has a reason to be drawn twice.
        UserState state = new()
        {
            Favourites = [Station("a"), Station("b")],
            LastSearch = "something",
        };

        return new()
        {
            { "browse", BrowseView.Build(catalog, state) },
            { "browse-empty-state", BrowseView.Build(catalog, UserState.Empty) },
            { "genre", GenreView.Build(catalog, "ambient", state) },
            { "all", AllStationsView.Build(catalog) },
            { "station", StationView.Build(catalog, "a", state) },
            { "settings", SettingsView.Build(catalog, "/data", now, now.AddDays(1), state) },
            { "search", SearchView.Build("x", [Station("a"), Station("z")], false, state) },
        };
    }

    [Theory]
    [MemberData(nameof(EveryScreen))]
    public void EveryNodeIdOnAScreenIsUnique(string screen, PluginView view)
    {
        IEnumerable<string> ids = PluginNodes.All(view).Select(node => node.Id);

        ids.Should().OnlyHaveUniqueItems($"a client keys on id, and {screen} is one payload");
    }

    [Theory]
    [MemberData(nameof(EveryScreen))]
    public void EveryNodeOnAScreenHasAnId(string screen, PluginView view)
    {
        PluginNodes.All(view)
            .Should().OnlyContain(node => !string.IsNullOrWhiteSpace(node.Id),
                $"an id-less node on {screen} cannot be keyed or targeted at all");
    }
}
