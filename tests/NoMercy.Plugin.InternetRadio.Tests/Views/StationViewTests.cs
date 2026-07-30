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

    [Fact]
    public void ShowsTheFullRecordIncludingTheStreamUrl()
    {
        PluginView view = StationView.Build(Catalog(Full), "a");

        PluginComponent table = AllNodes(view).Single(node => node.Component == PluginComponentType.Table);
        IEnumerable<object?> values = table.Items.Select(row => row.Props["value"]);

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
    }

    [Fact]
    public void EveryNodeHasAUniqueId()
    {
        PluginView view = StationView.Build(Catalog(Full), "a");

        AllNodes(view).Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }
}
