// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;
using NoMercy.Plugin.InternetRadio.Views;

namespace NoMercy.Plugin.InternetRadio.Tests;

/// <summary>
/// Reading a built view the way the client does.
///
/// A form and a button are still their own tags, but the design system has one tag -
/// "NMCard" - for a container, a card, a detail panel and a table alike, and a table's
/// own columns travel nowhere in the payload once PluginViews.Table has built it: the
/// header is just the table's first row, and a cell is just the next one along. So a
/// table here is still found the way an accessibility tree finds one - by its ARIA role
/// and by position, the same nested boxes a table always was - rather than by asking
/// PluginComponentType for a tag of its own.
/// </summary>
internal static class PluginNodes
{
    public static IEnumerable<PluginComponent> Flatten(PluginComponent node)
    {
        yield return node;

        foreach (PluginComponent child in node.Items.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    public static IEnumerable<PluginComponent> All(PluginView view) =>
        (view.Components ?? []).SelectMany(Flatten);

    /// <summary>
    /// Every word in the view, in order — from the text leaves and from the props of the
    /// components that carry their own words.
    /// </summary>
    public static IEnumerable<string> Words(PluginView view) =>
        All(view).SelectMany(Spoken).Where(word => word.Length > 0);

    private static IEnumerable<string> Spoken(PluginComponent node)
    {
        foreach (string key in Speaks)
        {
            if (node.Props.TryGetValue(key, out object? value) && value?.ToString() is { Length: > 0 } word)
            {
                yield return word;
            }
        }
    }

    // The prop each component says its words under, now that PluginViews builds every
    // node: a plain text leaf carries `text`, a caption or a card/detail/empty-state's
    // secondary line is an NMHelper carrying `helperText`, and a button's accessible
    // name is its own `ariaLabel` (its visible label is a Text child, already covered
    // by `text`). Column labels live inside PluginTableColumn objects, never as a
    // string prop directly, so a table's own labels are not read from here.
    private static readonly string[] Speaks = ["text", "helperText", "ariaLabel"];

    /// <summary>
    /// The single table on a screen, found the way an accessibility tree finds one:
    /// PluginViews.Table is the only builder that gives its own node the ARIA role
    /// "table". Container, Card, Detail and Table all draw as the same "NMCard" tag
    /// now that a payload names its components the way the client does, so a table can
    /// no longer be picked out by <see cref="PluginComponent.Component"/> alone - and
    /// it no longer carries its columns as a prop a test could read directly either,
    /// so this and the helpers below read the table the way a screen reader would.
    /// </summary>
    public static PluginComponent Table(PluginView view) => All(view).Single(IsTable);

    public static IEnumerable<PluginComponent> Tables(PluginView view) => All(view).Where(IsTable);

    private static bool IsTable(PluginComponent node) => Role(node) == "table";

    private static string? Role(PluginComponent node) =>
        node.Props.TryGetValue("accessibility", out object? accessibility)
        && accessibility is Dictionary<string, object?> typed
        && typed.TryGetValue("role", out object? role)
            ? role as string
            : null;

    // Always the table's own first item - PluginViews.Table builds it that way,
    // header before any row - so there is no need to search for it by id or role.
    private static PluginComponent HeaderRow(PluginComponent table) => table.Items[0];

    /// <summary>The column labels, in the order they are drawn.</summary>
    public static IReadOnlyList<string> Columns(PluginComponent table) =>
        [.. HeaderRow(table).Items.Select(CellText)];

    /// <summary>
    /// The rows a viewer counts: every one of the table's own items with a row's own
    /// ARIA role, other than the header - which carries that role too, so it has to be
    /// excluded by position rather than by role alone.
    /// </summary>
    public static IReadOnlyList<PluginComponent> Rows(PluginComponent table) =>
        [.. table.Items.Skip(1).Where(row => Role(row) == "row")];

    /// <summary>
    /// What one row says under one column, found by the label the viewer reads.
    ///
    /// PluginViews.Table keeps no column key anywhere in the payload once it has built
    /// a row - a cell is just the next one along - so a column is found by matching
    /// the header cell at the same position instead.
    /// </summary>
    public static string Value(PluginComponent table, PluginComponent row, string column)
    {
        List<PluginComponent> headerCells = [.. HeaderRow(table).Items];
        int index = headerCells.FindIndex(cell => CellText(cell) == column);

        return index >= 0 && index < row.Items.Count ? CellText(row.Items[index]) : string.Empty;
    }

    // A cell's own text, wherever inside it PluginViews put the leaf that carries one.
    private static string CellText(PluginComponent cell) =>
        Flatten(cell)
            .Select(node => node.Props.GetValueOrDefault("text") as string)
            .FirstOrDefault(text => text is not null) ?? string.Empty;
}
