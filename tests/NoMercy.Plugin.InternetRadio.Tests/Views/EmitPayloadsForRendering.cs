// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using NoMercy.PluginSdk.Abstractions;
using Xunit;
using NoMercy.Plugin.InternetRadio.Tests.Views;

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
///
/// The screens themselves are ViewFixtures.All() - the same ten fixtures
/// ComponentVocabularyTests walks - rather than a second copy built here: two
/// lists of "every screen" drift the moment one of them gains a state a station
/// or a search result changes without the other following.
/// </summary>
public class EmitPayloadsForRendering
{
    private static readonly JsonSerializerSettings ApiSettings =
        new MvcNewtonsoftJsonOptions().SerializerSettings;

    private static readonly string OutputDirectory =
        Environment.GetEnvironmentVariable("NM_PAYLOAD_OUT") ?? "";

    [SkippableFact]
    public void WriteEveryScreen()
    {
        Skip.If(OutputDirectory.Length == 0, "Set NM_PAYLOAD_OUT to write the payloads.");

        Directory.CreateDirectory(OutputDirectory);

        foreach (object[] row in ViewFixtures.All())
        {
            string name = (string)row[0];
            PluginView view = (PluginView)row[1];

            string json = JsonConvert.SerializeObject(new { data = view }, ApiSettings);
            File.WriteAllText(Path.Combine(OutputDirectory, $"{name}.json"), json);
        }

        Assert.Equal(9, Directory.GetFiles(OutputDirectory, "radio-*.json").Length);
    }
}
