// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugin.InternetRadio.Catalog;
using NoMercy.Plugin.InternetRadio.State;
using NoMercy.Plugin.InternetRadio.Views;
using NoMercy.PluginSdk.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

/// <summary>
/// One entry per screen this plugin can draw, shared by every test that needs to walk
/// every view rather than pick one. <see cref="EmitPayloadsForRendering"/> already builds
/// this same set to write them to disk for a human to look at; this is that list, reused
/// rather than kept a second time and left to drift from it.
/// </summary>
public static class ViewFixtures
{
    private static RadioStation Station(string id, string genre, string? logo = null) =>
        new()
        {
            Id = id,
            Name = $"Station {id}",
            StreamUrl = $"https://example.com/{id}",
            Genre = genre,
            Country = "NL",
            BitrateKbps = 128,
            LogoUrl = logo,
        };

    private static StationCatalog Catalog() =>
        StationCatalog.Create(
            [
                Station("groove", "Ambient", "https://somafm.com/img3/groovesalad-400.jpg"),
                Station("drone", "Ambient", "https://somafm.com/img3/dronezone-400.jpg"),
                Station("rock", "Rock"),
                Station("jazz", "Jazz"),
            ],
            CatalogSource.Fetched,
            DateTimeOffset.UtcNow
        );

    /// <summary>Every screen the plugin renders, named for what a person looking at the list would call it.</summary>
    public static TheoryData<string, PluginView> All()
    {
        StationCatalog catalog = Catalog();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return new()
        {
            { "radio-browse", BrowseView.Build(catalog, UserState.Empty) },
            // A second browse screen with favourites filled in: the section only exists
            // when there are some, so the empty one above cannot show what it looks like.
            {
                "radio-browse-favourites",
                BrowseView.Build(
                    catalog,
                    new UserState { Favourites = [Station("groove", "Ambient"), Station("jazz", "Jazz")] })
            },
            { "radio-genre", GenreView.Build(catalog, "ambient", UserState.Empty) },
            { "radio-all", AllStationsView.Build(catalog, UserState.Empty) },
            // Not a favourite: draws the "Add to favourites" side of the toggle.
            { "radio-station", StationView.Build(catalog, "groove", UserState.Empty) },
            // With a minted stream and already kept: the only state that draws Play,
            // Add to queue and "Remove from favourites" at all - Build(catalog, id,
            // state) above always leaves stream null and Favourites empty, which draws
            // the "cannot play" caption and the "Add to favourites" side instead.
            {
                "radio-station-playing",
                StationView.Build(
                    catalog.ById("groove")!,
                    new UserState { Favourites = [Station("groove", "Ambient")] },
                    stream: StationStream.Playable("https://example.com/groove"))
            },
            { "radio-settings", SettingsView.Build(catalog, "/data", now, now.AddDays(1), UserState.Empty) },
            { "radio-search-keyboard", SearchView.Build("", [], false, UserState.Empty) },
            {
                "radio-search",
                SearchView.Build(
                    "groove", [Station("groove", "Ambient"), Station("jazz", "Jazz")], false, UserState.Empty)
            },
            { "radio-search-empty", SearchView.Build("nothing at all", [], false, UserState.Empty) },
            { "radio-search-failed", SearchView.Build("anything", [], true, UserState.Empty) },
            // The three empty states no other fixture above reaches: nothing fetched
            // yet, a genre slug that does not exist, and a station id that does not
            // exist. Each draws its own resolved EmptyState title, and a key nothing
            // in ViewFixtures.All() draws is a key TranslationTests treats as unused.
            { "radio-stations-empty", BrowseView.Build(StationCatalog.Empty(), UserState.Empty) },
            { "radio-genre-empty", GenreView.Build(catalog, "no-such-genre", UserState.Empty) },
            { "radio-station-missing", StationView.Build(catalog, "no-such-station", UserState.Empty) },
        };
    }

    /// <summary>Every node in a view, depth-first, the way a client walks the tree to draw it.</summary>
    public static IEnumerable<PluginComponent> Flatten(PluginView view) => PluginNodes.All(view);
}
