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

    [Fact]
    public void OffersPlayAndEnqueueForTheStream()
    {
        PluginView view = StationView.Build(Catalog(Full), "a");

        PluginComponent play = ActionOfType(view, PluginActionType.PlayMedia)!;
        play.Action!.Payload["streamUrl"].Should().Be("https://example.com/a");
        play.Action.Payload["title"].Should().Be("Alpha FM");

        ActionOfType(view, PluginActionType.Enqueue).Should().NotBeNull();
    }

    [Fact]
    public void OffersTheHomepageWhenThereIsOne()
    {
        PluginView view = StationView.Build(Catalog(Full), "a");

        ActionOfType(view, PluginActionType.OpenWebView)!
            .Action!.Payload["entryUrl"].Should().Be("https://example.com");
    }

    // A button that opens nothing is worse than no button.
    [Fact]
    public void OmitsTheHomepageButtonWhenThereIsNoHomepage()
    {
        PluginView view = StationView.Build(Catalog(Full with { Homepage = null }), "a");

        ActionOfType(view, PluginActionType.OpenWebView).Should().BeNull();
    }

    // Homepage is untrusted from both of its sources (radio-browser.info is
    // community-editable, and StationOverrides is deliberately ungated), and a
    // button that opens a javascript: URL is worse than one that opens nothing.
    // See StationGates.IsSafeExternalUrl.
    [Fact]
    public void OmitsTheHomepageButtonWhenTheHomepageIsNotAnHttpUrl()
    {
        PluginView view = StationView.Build(Catalog(Full with { Homepage = "javascript:alert(1)" }), "a");

        ActionOfType(view, PluginActionType.OpenWebView).Should().BeNull();
    }

    // The spec calls for a row of back buttons to BOTH /all (where this page is
    // usually reached from) and / (for a bookmark or a grid card that skipped it).
    [Fact]
    public void OffersWaysBackToAllStationsAndToBrowse()
    {
        PluginView view = StationView.Build(Catalog(Full), "a");

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
        PluginView view = StationView.Build(Catalog(Full), "gone");

        AllNodes(view).Should().Contain(node =>
            node.Action != null
            && node.Action.Type == PluginActionType.Navigate
            && (string)node.Action.Payload["route"]! == RadioRoutes.Browse);
    }

    [Fact]
    public void ShowsTheFullRecordIncludingTheStreamUrl()
    {
        PluginView view = StationView.Build(Catalog(Full), "a");

        PluginComponent table = PluginNodes.Table(view);
        IEnumerable<string> values = PluginNodes.Rows(table)
            .Select(row => PluginNodes.Value(table, row, "Value"));

        values.Should().Contain("Ambient").And.Contain("NL").And.Contain("https://example.com/a");
    }

    [Fact]
    public void NamesTheProvenanceOfAFetchedStation()
    {
        PluginView view = StationView.Build(Catalog(Full), "a");

        AllNodes(view).SelectMany(node => node.Props.Values)
            .Should().Contain(value => value != null && value.ToString()!.Contains("radio-browser"));
    }

    [Fact]
    public void NamesTheProvenanceOfAUserSuppliedStation()
    {
        PluginView view = StationView.Build(Catalog(Full with { IsUserSupplied = true }), "a");

        AllNodes(view).SelectMany(node => node.Props.Values)
            .Should().Contain(value => value != null && value.ToString()!.Contains(StationOverrides.FileName));
    }

    // A station can vanish between a page being opened and a link being followed -
    // the catalogue refreshes underneath it.
    [Fact]
    public void ShowsAnEmptyStateForAStationThatIsNoLongerThere()
    {
        PluginView view = StationView.Build(Catalog(Full), "gone");

        AllNodes(view).Should().Contain(node => node.Component == PluginComponentType.EmptyState);
        ActionOfType(view, PluginActionType.PlayMedia).Should().BeNull();
    }

    [Fact]
    public void RendersAStationMissingEveryOptionalField()
    {
        RadioStation bare = new() { Id = "b", Name = "Bare FM", StreamUrl = "https://example.com/b" };

        PluginView view = StationView.Build(Catalog(bare), "b");

        ActionOfType(view, PluginActionType.PlayMedia).Should().NotBeNull();
        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();

        // The sentence-builder must not fall back to an empty or whitespace-only
        // string when nothing is known - it has to be null, or the Detail component
        // renders a stray blank line where a description would go.
        // The description is a helper line beside the heading now, not a prop, so
        // "null rather than blank" is the line being absent rather than present
        // and empty. A detail is an NMCard like a card is, so it is found by its
        // own id rather than by tag.
        AllNodes(view).Should().Contain(node => node.Id == "station-detail-b");
        AllNodes(view).Should().NotContain(node =>
            node.Id == "station-detail-b-secondary");

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
        PluginView view = StationView.Build(Catalog(Full), "a");

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }
}
