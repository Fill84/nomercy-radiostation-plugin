// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugins.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests;

// The locale files arrived before anything pointed at them: they sat in the source tree
// with no manifest declaration, no copy to the output and no line in the packaging step,
// so every server would have served an empty catalogue while the files sat in git looking
// finished. Nothing failed, which is the whole problem - a missing translation degrades to
// the source language, so the symptom is "the Dutch never showed up" and not a red build.
//
// These tests are the four links in that chain, each asserted where it breaks.
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
}
