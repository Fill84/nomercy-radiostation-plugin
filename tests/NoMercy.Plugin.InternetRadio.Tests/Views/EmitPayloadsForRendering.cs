// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests;

/// <summary>
/// Writes each screen exactly as the server would send it, for a browser to
/// draw.
///
/// A green view test says the payload has the shape the test expects. It cannot
/// say the screen draws: a card with its children in the wrong place, a form
/// whose fields are props no component reads, two text leaves sharing a line —
/// every one of those passes a structural assertion and renders as a blank box
/// or a run-on sentence. Those failures were found by looking, and this is what
/// gives something to look at while the plugin itself is waiting on consent.
///
/// Newtonsoft with the MVC settings, because that is what writes every response
/// this server sends. System.Text.Json here would emit a shape no client sees.
/// </summary>
public class EmitPayloadsForRendering
{
    private static readonly JsonSerializerSettings ApiSettings =
        new MvcNewtonsoftJsonOptions().SerializerSettings;

    private static readonly string OutputDirectory =
        Environment.GetEnvironmentVariable("NM_PAYLOAD_OUT") ?? "";

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

    [SkippableFact]
    public void WriteEveryScreen()
    {
        Skip.If(OutputDirectory.Length == 0, "Set NM_PAYLOAD_OUT to write the payloads.");

        StationCatalog catalog = Catalog();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Dictionary<string, PluginView> screens = new()
        {
            ["radio-browse"] = BrowseView.Build(catalog),
            ["radio-genre"] = GenreView.Build(catalog, "ambient"),
            ["radio-all"] = AllStationsView.Build(catalog),
            ["radio-station"] = StationView.Build(catalog, "groove"),
            ["radio-settings"] = SettingsView.Build(catalog, "/data", now, now.AddDays(1)),
            ["radio-search"] = SearchView.Build(
                "groove", [Station("groove", "Ambient"), Station("jazz", "Jazz")], queryFailed: false),
            // Emitted too, because the three empty states are the ones a structural test
            // cannot judge: they have to be told apart by looking at them.
            ["radio-search-empty"] = SearchView.Build("nothing at all", [], queryFailed: false),
            ["radio-search-failed"] = SearchView.Build("anything", [], queryFailed: true),
        };

        Directory.CreateDirectory(OutputDirectory);

        foreach ((string name, PluginView view) in screens)
        {
            string json = JsonConvert.SerializeObject(new { data = view }, ApiSettings);
            File.WriteAllText(Path.Combine(OutputDirectory, $"{name}.json"), json);
        }

        Assert.Equal(8, Directory.GetFiles(OutputDirectory, "radio-*.json").Length);
    }
}
