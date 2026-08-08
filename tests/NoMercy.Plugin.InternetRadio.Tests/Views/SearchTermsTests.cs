// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

// A term is spelled by tapping keys and then travels as a path segment, so it is both
// user input and a route. These are the assertions that keep it from being either a
// broken route or an unfiltered query.
public class SearchTermsTests
{
    [Theory]
    [InlineData("tomorrowland", "tomorrowland")]
    [InlineData("TomorrowLand", "tomorrowland")]
    [InlineData("radio 538", "radio 538")]
    public void KeepsWhatTheKeyboardCanSpell(string input, string expected)
    {
        SearchTerms.Sanitise(input).Should().Be(expected);
    }

    // Anything else in an incoming route was not built by this plugin. The safe reading of
    // that is not "escape it carefully" but "it is not part of the term" - it goes on to
    // become a query string to a third-party service.
    [Theory]
    [InlineData("tomorrow/land", "tomorrowland")]
    [InlineData("tomorrow,land", "tomorrowland")]
    [InlineData("../../etc/passwd", "etcpasswd")]
    [InlineData("<script>x</script>", "scriptxscript")]
    [InlineData("a&b=c", "abc")]
    [InlineData("%20%2F", "202f")]
    public void DropsEverythingElse(string input, string expected)
    {
        SearchTerms.Sanitise(input).Should().Be(expected);
    }

    // " somafm" and "somafm" must not be two different searches, and a doubled space makes
    // the space key a button that appears to do nothing.
    [Theory]
    [InlineData("  somafm", "somafm")]
    [InlineData("soma  fm", "soma fm")]
    [InlineData("somafm  ", "somafm")]
    public void NormalisesSpaces(string input, string expected)
    {
        SearchTerms.Sanitise(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void NothingSpellableIsAnEmptyTerm(string? input)
    {
        SearchTerms.Sanitise(input).Should().BeEmpty();
    }

    // A bound on what a hand-written route can make this plugin send upstream.
    [Fact]
    public void IsBoundedInLength()
    {
        SearchTerms.Sanitise(new string('a', SearchTerms.MaxLength * 3))
            .Should().HaveLength(SearchTerms.MaxLength);
    }

    [Fact]
    public void AppendStopsAtTheBound()
    {
        string full = new('a', SearchTerms.MaxLength);

        SearchTerms.Append(full, 'b').Should().Be(full);
    }

    [Fact]
    public void BackspaceRemovesOneCharacterAndSurvivesAnEmptyTerm()
    {
        SearchTerms.Backspace("tomo").Should().Be("tom");
        SearchTerms.Backspace(string.Empty).Should().BeEmpty();
    }

    // The round trip that makes the whole design work. A term is written into a route and
    // read back out of the one the client actually sends - which arrives comma-joined, the
    // same way every two-segment route in this plugin does.
    [Theory]
    [InlineData("tomorrowland")]
    [InlineData("radio 538")]
    [InlineData("soma fm groove")]
    public void SurvivesTheRouteItIsWrittenInto(string term)
    {
        string route = RadioRoutes.Search(term);

        RadioRoutes.Parse(route).Should().Be(new RadioRoute(RadioRouteKind.Search, term));
        RadioRoutes.Parse(route.Replace('/', ',')).Value.Should().Be(term);
    }

    [Fact]
    public void AnEmptyTermIsTheBareKeyboardRoute()
    {
        RadioRoutes.Search(string.Empty).Should().Be(RadioRoutes.SearchRoot);
        RadioRoutes.Parse(RadioRoutes.SearchRoot)
            .Should().Be(new RadioRoute(RadioRouteKind.Search, string.Empty));
    }
}
