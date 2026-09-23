// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Tests.Views;
using NoMercy.PluginSdk.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests;

// The locale files arrived before anything pointed at them: they sat in the source tree
// with no manifest declaration, no copy to the output and no line in the packaging step,
// so every server would have served an empty catalogue while the files sat in git looking
// finished. Nothing failed, which is the whole problem - a missing translation degrades to
// the source language, so the symptom is "the Dutch never showed up" and not a red build.
//
// The four tests below are that chain, each asserted where it breaks. The two after them
// are a different failure in the same feature: the chain being intact says nothing about
// whether a key on it is ever drawn, or whether what is drawn is ever a key. Task 4 moved
// every view onto PluginViews, which is what finally lets a key drawn in a view resolve
// through this chain at all - and exposed that not one of the four keys this plugin shipped
// was ever drawn, while every view still spoke English literals no bundle could reach.
public class TranslationTests
{
    private static PluginTranslations Declared()
    {
        PluginTranslations? translations = ManifestTests.LoadManifest().Translations;

        translations.Should().NotBeNull(
            "PluginManager.ReadTranslationsAsync returns null the moment the manifest "
            + "declares nothing, so shipping lang/ without this block is shipping dead files");

        return translations!;
    }

    // Resolved the way the server resolves it: relative to the directory holding the
    // manifest. The test project links the files under lang/ for exactly this reason.
    private static string? ReadLocale(string locale)
    {
        string path = Path.Combine(AppContext.BaseDirectory, Declared().Path, $"{locale}.json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    // The same file, read as the flat key/value set a bundle actually is, for the two
    // tests below that compare keys rather than raw text.
    private static IReadOnlyDictionary<string, string> LoadBundle(string locale)
    {
        string text = ReadLocale(locale)
            ?? throw new InvalidOperationException($"'{locale}.json' did not ship beside the manifest");

        return JsonSerializer.Deserialize<Dictionary<string, string>>(text)
            ?? throw new InvalidOperationException($"'{locale}.json' is not a flat set of strings");
    }

    [Fact]
    public void Manifest_DeclaresTheTranslationsThePluginShips()
    {
        PluginTranslations translations = Declared();

        translations.Source.Should().Be("en");
        translations.Path.Should().Be("lang");
        translations.Locales.Should().BeEquivalentTo("en", "nl");
    }

    // Every declared locale has to be readable from where the server will look. This is
    // the link that CopyToOutputDirectory provides, and it fails here rather than as an
    // empty label on somebody's dashboard.
    [Fact]
    public void EveryDeclaredLocale_ShipsBesideTheManifest()
    {
        foreach (string locale in Declared().Locales)
        {
            ReadLocale(locale).Should().NotBeNull($"'{locale}' is declared in plugin.json");
        }
    }

    // The host's own validator rather than rules written here, for the same reason
    // ManifestTests deserialises with the host's own type: a rule this repository invents
    // is a rule that can disagree with the one that actually runs. It catches a key the
    // source has and a locale does not, a key no source has so nothing will ever read it,
    // and a value that is blank rather than untranslated.
    [Fact]
    public void EveryLocale_SatisfiesTheHostsOwnValidator()
    {
        List<PluginTranslationProblem> problems =
            PluginTranslationValidator.Validate(Declared(), ReadLocale);

        problems.Should().BeEmpty(
            "the server measures every locale against the source one: "
            + string.Join("; ", problems.Select(problem => problem.ToString())));
    }

    // The reverse direction, which the validator cannot check because it only walks what
    // the manifest declares. A locale file added to lang/ and forgotten in the manifest is
    // never read by anything, and looks translated to whoever added it.
    [Fact]
    public void NoLocaleFileIsShippedThatTheManifestNeverDeclares()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, Declared().Path);

        IEnumerable<string> onDisk = Directory
            .EnumerateFiles(directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => name!);

        onDisk.Should().BeEquivalentTo(Declared().Locales);
    }

    // A key a view draws and the bundle does not carry renders as the key itself: the
    // viewer reads "stations.title" where a title should be.
    [Fact]
    public void EveryKeyAViewDrawsExistsInEveryLanguage()
    {
        IReadOnlySet<string> drawn = TranslationKeys.Drawn();

        foreach (string locale in new[] { "en", "nl" })
        {
            IReadOnlyDictionary<string, string> bundle = LoadBundle(locale);

            drawn.Where(key => !bundle.ContainsKey(key))
                .Should().BeEmpty($"{locale}.json should carry every key a view draws");
        }
    }

    // The other direction, which is how this plugin shipped two language files that did
    // nothing: four keys, none of them drawn anywhere.
    [Fact]
    public void EveryKeyInTheBundleIsOneAViewDraws()
    {
        IReadOnlySet<string> drawn = TranslationKeys.Drawn();

        LoadBundle("en").Keys.Where(key => !drawn.Contains(key))
            .Should().BeEmpty("en.json should carry no key nothing draws");
    }
}

/// <summary>
/// Every translation key this plugin actually draws, established by reading the client
/// this plugin ships for (nomercy-app-web, <c>src/components/nm/NMRender.vue</c>) rather
/// than by assuming a prop resolves because it holds a string:
/// <para>
/// NMRender resolves exactly one prop through a plugin's own bundle - a node's own `text`,
/// and only when that node's component is <c>NMText</c> (<see cref="PluginComponentType.Text"/>).
/// That happens for <see cref="PluginViews.Text"/>'s "title"/"subtitle" variant (a text
/// leaf nested under an NMContentHeader) and for the title half of
/// <see cref="PluginViews.EmptyState"/>, <see cref="PluginViews.Card"/> and
/// <see cref="PluginViews.Detail"/> (the same leaf, via their shared Face helper).
/// </para>
/// <para>
/// Nothing else is resolved, no matter how it reads. A "caption"/"muted" Text and an
/// EmptyState's own message both land on an NMHelper's `helperText`; NMBadge's `text` and
/// NMButton's `ariaLabel` are bound to the design-system component raw; PluginForm.vue
/// binds `submitLabel` and every field's `label` straight into its template with no
/// pluginText call in it at all. Worse, <see cref="PluginViews.Button"/> (and the
/// IconButton wrapper this plugin uses for the icon slot) writes its label into `ariaLabel`
/// as well as into a resolved child leaf - so a button's visible text technically walks
/// through a resolved node, but keying it plants the same key, unresolved, in the
/// accessible name. This plugin leaves every button and icon-button label in English for
/// exactly that reason; none of their leaves carry a key, so this collector never needs to
/// special-case them.
/// </para>
/// </summary>
internal static class TranslationKeys
{
    // A key looks like "stations.title": lower-case dot-joined segments, no spaces, no
    // capitals. A string still in English fails on its first space or its first capital
    // letter, which is what lets this walk a tree where some strings are already keys and
    // others are still literals waiting for one.
    private static readonly Regex KeyShape =
        new(@"\A[a-z][a-z0-9]*(?:\.[a-z][a-z0-9]*)+\z", RegexOptions.Compiled);

    public static IReadOnlySet<string> Drawn()
    {
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (object[] row in ViewFixtures.All())
        {
            PluginView view = (PluginView)row[1];

            foreach (PluginComponent node in ViewFixtures.Flatten(view))
            {
                if (node.Component == PluginComponentType.Text
                    && node.Props.GetValueOrDefault("text") is string text
                    && KeyShape.IsMatch(text))
                {
                    keys.Add(text);
                }
            }
        }

        // The manifest's own mounts. PluginNavEntry.Label is read by getPlugins.ts through
        // the same pluginText the views resolve through, before a menu entry ever reaches
        // the sidebar - so it is required here on the same footing as a drawn view, not as
        // an afterthought.
        foreach (PluginNavEntry entry in new InternetRadioPlugin().NavEntries)
        {
            keys.Add(entry.Label);
        }

        // Every page this plugin declares. PluginRoute.Label is, in its own words, "what
        // the viewer sees in a heading or a menu, as a translation key" - required
        // regardless of whether any view happens to draw the same words itself.
        foreach (PluginRoute route in InternetRadioPlugin.Table.Routes)
        {
            if (route.Label is { } label)
            {
                keys.Add(label);
            }
        }

        return keys;
    }
}
