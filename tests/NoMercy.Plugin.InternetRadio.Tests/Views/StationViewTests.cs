// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

public class StationViewTests
{
    private static RadioStation Full =>
        new()
        {
            Id = "a",
            Name = "Alpha FM",
            StreamUrl = "https://example.com/a",
            LogoUrl = "https://example.com/logo.png",
            Homepage = "https://example.com",
            Genre = "Ambient",
            Country = "NL",
            Language = "english",
            BitrateKbps = 128,
            Codec = "MP3",
        };

    private static StationCatalog Catalog(params RadioStation[] stations) =>
        StationCatalog.Create(stations, CatalogSource.Fetched, DateTimeOffset.UtcNow);

    private static IEnumerable<PluginComponent> Flatten(PluginComponent node)
    {
        yield return node;
        foreach (PluginComponent child in node.Items.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static IEnumerable<PluginComponent> AllNodes(PluginView view) =>
        (view.Components ?? []).SelectMany(Flatten);

    private static PluginComponent? ActionOfType(PluginView view, string type) =>
        AllNodes(view).FirstOrDefault(node => node.Action?.Type == type);

    // By id, not by component type. The design system collapsed Container, List, Row,
    // Grid, Card, Detail, Form and Table onto the single NMCard component, so
    // PluginComponentType.Table, .Detail and .Container are now the same string and a
    // search by type matches every container on the page.
    private static PluginComponent Node(PluginView view, string id) =>
        AllNodes(view).Single(node => node.Id == id);

    // A row carries its cells as props, keyed by column. The header is a prop on the
    // table rather than a row, so every item here is a row a viewer would count.
    private static List<PluginComponent> Rows(PluginComponent table) => [.. table.Items];

    private static string CellText(PluginComponent row, string columnKey) =>
        row.Props.GetValueOrDefault(columnKey)?.ToString() ?? string.Empty;

    [Fact]
    // Sound comes from a page this plugin serves, embedded here - not from the dashboard's
    // player, which cannot play plugin media at all: it derives a track id from the stream
    // url and then uses it as a CSS selector, so it throws before requesting a byte. See
    // PlayerPage and app-web issue #15.
    public void EmbedsAPlayerThatCanActuallyPlayTheStream()
    {
        // The relay only learns this server's address from a request, so the test supplies
        // one rather than depending on whichever test ran first.
        MediaProxy.Remember("https://server.example", null);

        PluginView view = StationView.Build(Catalog(Full), "a", UserState.Empty);

        AllNodes(view).Single(node => node.Id == "station-player-a")
            .Props["entryUrl"]!.ToString()
            .Should().StartWith($"https://server.example/api/v1/plugins/{PluginIdentity.Id}/player/a");
    }

    // Nothing that only ever raises an error toast. A queue belongs to the player that has
    // one, and this page is not it.
    [Fact]
    public void DoesNotOfferTheDashboardPlayerItCannotUse()
    {
        PluginView view = StationView.Build(Catalog(Full), "a", UserState.Empty);

        ActionOfType(view, PluginActionType.PlayMedia).Should().BeNull();
        ActionOfType(view, PluginActionType.Enqueue).Should().BeNull();
    }

    [Fact]
    public void OffersTheHomepageWhenThereIsOne()
    {
        PluginView view = StationView.Build(Catalog(Full), "a", UserState.Empty);

        ActionOfType(view, PluginActionType.OpenWebView)!
            .Action!.Payload["entryUrl"].Should().Be("https://example.com");
    }

    // A button that opens nothing is worse than no button.
    [Fact]
    public void OmitsTheHomepageButtonWhenThereIsNoHomepage()
    {
        PluginView view = StationView.Build(Catalog(Full with { Homepage = null }), "a", UserState.Empty);

        ActionOfType(view, PluginActionType.OpenWebView).Should().BeNull();
    }

    // Homepage is untrusted from both of its sources (radio-browser.info is
    // community-editable, and StationOverrides is deliberately ungated), and a
    // button that opens a javascript: URL is worse than one that opens nothing.
    // See StationGates.IsSafeExternalUrl.
    [Fact]
    public void OmitsTheHomepageButtonWhenTheHomepageIsNotAnHttpUrl()
    {
        PluginView view = StationView.Build(Catalog(Full with { Homepage = "javascript:alert(1)" }), "a", UserState.Empty);

        ActionOfType(view, PluginActionType.OpenWebView).Should().BeNull();
    }

    // The spec calls for a row of back buttons to BOTH /all (where this page is
    // usually reached from) and / (for a bookmark or a grid card that skipped it).
    [Fact]
    public void OffersWaysBackToAllStationsAndToBrowse()
    {
        PluginView view = StationView.Build(Catalog(Full), "a", UserState.Empty);

        AllNodes(view).Should().Contain(node =>
            node.Action != null
            && node.Action.Type == PluginActionType.Navigate
            && (string)node.Action.Payload["route"]! == RadioRoutes.AllStations);

        AllNodes(view).Should().Contain(node =>
            node.Action != null
            && node.Action.Type == PluginActionType.Navigate
            && (string)node.Action.Payload["route"]! == RadioRoutes.Browse);
    }

    // The missing-station empty state is reached from the same routes as a found
    // one - a stale link deserves the same way back, not just to /all.
    [Fact]
    public void OffersWaysBackEvenWhenTheStationIsMissing()
    {
        PluginView view = StationView.Build(Catalog(Full), "gone", UserState.Empty);

        AllNodes(view).Should().Contain(node =>
            node.Action != null
            && node.Action.Type == PluginActionType.Navigate
            && (string)node.Action.Payload["route"]! == RadioRoutes.Browse);
    }

    [Fact]
    public void ShowsTheFullRecordIncludingTheStreamUrl()
    {
        PluginView view = StationView.Build(Catalog(Full), "a", UserState.Empty);

        PluginComponent table = PluginNodes.Table(view);
        IEnumerable<string> values = PluginNodes.Rows(table)
            .Select(row => PluginNodes.Value(table, row, "Value"));

        values.Should().Contain("Ambient").And.Contain("NL").And.Contain("https://example.com/a");
    }

    [Fact]
    public void NamesTheProvenanceOfAFetchedStation()
    {
        PluginView view = StationView.Build(Catalog(Full), "a", UserState.Empty);

        AllNodes(view).SelectMany(node => node.Props.Values)
            .Should().Contain(value => value != null && value.ToString()!.Contains("radio-browser"));
    }

    [Fact]
    public void NamesTheProvenanceOfAUserSuppliedStation()
    {
        PluginView view = StationView.Build(Catalog(Full with { IsUserSupplied = true }), "a", UserState.Empty);

        AllNodes(view).SelectMany(node => node.Props.Values)
            .Should().Contain(value => value != null && value.ToString()!.Contains(StationOverrides.FileName));
    }

    // A station can vanish between a page being opened and a link being followed -
    // the catalogue refreshes underneath it.
    [Fact]
    public void ShowsAnEmptyStateForAStationThatIsNoLongerThere()
    {
        PluginView view = StationView.Build(Catalog(Full), "gone", UserState.Empty);

        AllNodes(view).Should().Contain(node => node.Component == Ui.EmptyStateComponent);
        ActionOfType(view, PluginActionType.PlayMedia).Should().BeNull();
    }

    [Fact]
    public void RendersAStationMissingEveryOptionalField()
    {
        RadioStation bare = new() { Id = "b", Name = "Bare FM", StreamUrl = "https://example.com/b" };

        PluginView view = StationView.Build(Catalog(bare), "b", UserState.Empty);

        AllNodes(view).Select(node => node.Id).Should().Contain("station-player-b");
        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();

        // The sentence-builder must not fall back to an empty or whitespace-only
        // string when nothing is known - it has to be null, or the Detail component
        // renders a stray blank line where a description would go.
        //
        // Detail no longer carries the description as a prop to read back: it renders
        // the line as its own Text node, and only when there is something to say. So
        // the absence is asserted the way it now shows up - no blank text anywhere
        // under the detail - which is the rendered outcome the prop only stood in for.
        PluginComponent detail = Node(view, "station-detail-b");
        Flatten(detail).Should().NotContain(node =>
            node.Props.ContainsKey("text")
            && string.IsNullOrWhiteSpace(node.Props["value"] as string));
        Flatten(detail).Should().NotContain(node => node.Id == "station-detail-b-secondary");

        // Stream and Source are the only two facts that always survive - Stream
        // because it is required on RadioStation, Source because Provenance never
        // returns null. Every other fact is optional and absent here, and a filtered
        // fact must never leave a blank or null cell behind.
        PluginComponent facts = PluginNodes.Table(view);
        IReadOnlyList<PluginComponent> rows = PluginNodes.Rows(facts);

        rows.Should().HaveCount(2);
        rows.Select(row => PluginNodes.Value(facts, row, "Field")).Should().Equal("Stream", "Source");
        rows.Select(row => PluginNodes.Value(facts, row, "Value")).Should()
            .OnlyContain(value => !string.IsNullOrWhiteSpace(value));
    }

    [Fact]
    public void EveryNodeHasAUniqueId()
    {
        PluginView view = StationView.Build(Catalog(Full), "a", UserState.Empty);

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }
}
