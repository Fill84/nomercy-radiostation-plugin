// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text;

namespace NoMercy.Plugin.InternetRadio;

// What a search term is allowed to be.
//
// Narrow on purpose. A term is spelled by tapping keys and then travels as a path
// segment, so the only characters that can ever legitimately appear are the ones the
// keyboard offers. Anything else in an incoming route was not built by this plugin, and
// the safe reading of that is not "escape it carefully" but "it is not a term".
public static class SearchTerms
{
    /// <summary>
    /// The longest term the keyboard will build.
    ///
    /// Not a limit anyone reaches by spelling a station name; it is a limit on what a
    /// hand-written route can make this plugin send to radio-browser.
    /// </summary>
    public const int MaxLength = 32;

    /// <summary>
    /// How many characters before a search is worth running.
    ///
    /// One letter matches thousands of stations and answers nothing, so the first tap
    /// shows the keyboard rather than a wall of results.
    /// </summary>
    public const int MinLength = 2;

    /// <summary>The keys, in the order they are drawn.</summary>
    public const string Letters = "abcdefghijklmnopqrstuvwxyz";

    /// <summary>Digits, because a great many stations are named after one.</summary>
    public const string Digits = "0123456789";

    /// <summary>
    /// <paramref name="term"/> reduced to what a term may contain: lower-case letters,
    /// digits and single interior spaces, bounded by <see cref="MaxLength"/>.
    /// </summary>
    public static string Sanitise(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return string.Empty;
        }

        StringBuilder clean = new(Math.Min(term.Length, MaxLength));

        foreach (char character in term.ToLowerInvariant())
        {
            if (clean.Length == MaxLength)
            {
                break;
            }

            if (Letters.Contains(character) || Digits.Contains(character))
            {
                clean.Append(character);
            }
            // Never leading, never doubled: a term is what gets compared to a station
            // name, and " somafm" and "somafm" must not be two different searches.
            else if (character == ' ' && clean.Length > 0 && clean[^1] != ' ')
            {
                clean.Append(' ');
            }
        }

        return clean.ToString().TrimEnd();
    }

    /// <summary>
    /// <paramref name="term"/> with one character appended, or unchanged when it is full.
    /// </summary>
    public static string Append(string term, char character) =>
        Sanitise(term + character);

    /// <summary>
    /// <paramref name="term"/> with its last character removed.
    /// </summary>
    public static string Backspace(string term) =>
        term.Length == 0 ? string.Empty : Sanitise(term[..^1]);
}
