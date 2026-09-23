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

    /// <summary>
    /// The rows a viewer counts: every one of the table's own items with a row's own
    /// ARIA role, other than the header - which carries that role too, so it has to be
    /// excluded by position rather than by role alone.
    /// </summary>
    public static IReadOnlyList<PluginComponent> Rows(PluginComponent table) =>
        [.. table.Items.Skip(1).Where(row => Role(row) == "row")];

    /// <summary>
    /// What one row says under one column, found by <see cref="PluginTableColumn.Key"/>
    /// rather than by the header's rendered label - the label is a translation key now,
    /// not stable text a test can compare against. PluginViews.Table writes that key into
    /// every cell's own id (a header cell is "{tableId}-head-{column.Key}", a body cell
    /// "{row.Id}-{column.Key}"), which is what a column is matched on instead.
    /// </summary>
    public static string Value(PluginComponent table, PluginComponent row, string column)
    {
        List<PluginComponent> headerCells = [.. HeaderRow(table).Items];
        int index = headerCells.FindIndex(cell => HeaderKey(cell) == column);

        return index >= 0 && index < row.Items.Count ? CellText(row.Items[index]) : string.Empty;
    }

    // The column key a header cell was built for - the part of its own id after
    // PluginViews.Table's "-head-" marker.
    private static string? HeaderKey(PluginComponent headerCell)
    {
        const string marker = "-head-";
        int at = headerCell.Id.IndexOf(marker, StringComparison.Ordinal);

        return at < 0 ? null : headerCell.Id[(at + marker.Length)..];
    }

    // A cell's own text, wherever inside it PluginViews put the leaf that carries one.
    private static string CellText(PluginComponent cell) =>
        Flatten(cell)
            .Select(node => node.Props.GetValueOrDefault("text") as string)
            .FirstOrDefault(text => text is not null) ?? string.Empty;

    /// <summary>
    /// The single icon a button draws before its label, or null when it has none.
    /// NMButton has no `icon` prop of its own - it draws one only from its `iconLeft`
    /// slot, which is what IconButton fills and PluginViews.Button never does.
    /// </summary>
    public static PluginComponent? IconLeft(PluginComponent button) =>
        button.Props.GetValueOrDefault("slots") is Dictionary<string, object?> slots
        && slots.GetValueOrDefault("iconLeft") is IEnumerable<PluginComponent> icons
            ? icons.SingleOrDefault()
            : null;
}
