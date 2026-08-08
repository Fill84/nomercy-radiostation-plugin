// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio.Tests;

/// <summary>
/// Reading a built view the way a client does.
///
/// A card and a layout container are the same design-system component wearing
/// different clothes: both are <c>NMCard</c>, and a container says so with
/// <c>variant: ghost</c>. Filtering on the tag alone therefore counts every
/// wrapper as a card — which is what these tests did, back when a container had
/// a tag of its own and the design system had never seen the payload.
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

    /// <summary>A card a viewer would call one: not one of the ghost boxes holding the layout.</summary>
    public static bool IsCard(PluginComponent node) =>
        node.Component == PluginComponentType.Card && !IsLayout(node);

    /// <summary>A box that exists to place things, drawn as nothing at all.</summary>
    public static bool IsLayout(PluginComponent node) =>
        node.Component == PluginComponentType.Container
        && node.Props.TryGetValue("variant", out object? variant)
        && "ghost".Equals(variant);

    public static IEnumerable<PluginComponent> Cards(PluginView view) => All(view).Where(IsCard);

    /// <summary>
    /// Every word in the view, in order. Text is a leaf now rather than a prop
    /// on whatever was going to draw it, so a title lives one or two levels
    /// under the thing it titles.
    /// </summary>
    public static IEnumerable<string> Words(PluginView view) =>
        All(view)
            .Where(node => node.Component == PluginComponentType.Text)
            .Select(Word)
            .Where(word => word.Length > 0);

    private static string Word(PluginComponent node) =>
        node.Props.TryGetValue("text", out object? text) ? text?.ToString() ?? "" : "";

    /// <summary>What a screen reader is told this node is.</summary>
    public static bool HasRole(PluginComponent node, string role) =>
        node.Props.TryGetValue("accessibility", out object? accessibility)
        && accessibility is IDictionary<string, object?> map
        && map.TryGetValue("role", out object? value)
        && role.Equals(value);

    /// <summary>
    /// The table on a screen. There is no table tag to look for: a table is boxes
    /// carrying the ARIA roles, because the design system's own table element has
    /// a single slot and generates neither a row nor a cell to put in it. The role
    /// is what a client reads and what a reader announces, so it is what a test
    /// asks about too.
    /// </summary>
    public static PluginComponent Table(PluginView view) =>
        All(view).Single(node => HasRole(node, "table"));

    public static IEnumerable<PluginComponent> Tables(PluginView view) =>
        All(view).Where(node => HasRole(node, "table"));

    /// <summary>The column labels, in the order they are drawn.</summary>
    public static IReadOnlyList<string> Columns(PluginComponent table) =>
        [.. HeaderRow(table).Items.Select(CellText)];

    public static PluginComponent HeaderRow(PluginComponent table) =>
        table.Items.Single(row => row.Items.Any(cell => HasRole(cell, "columnheader")));

    /// <summary>The rows a viewer counts: the header is not one of them.</summary>
    public static IReadOnlyList<PluginComponent> Rows(PluginComponent table) =>
        [.. table.Items.Where(row => row.Items.Any(cell => HasRole(cell, "cell")))];

    /// <summary>
    /// What one row says under one column, found by the label the viewer reads
    /// rather than by the key the plugin used: the key never reaches the client.
    /// </summary>
    public static string Value(PluginComponent table, PluginComponent row, string column)
    {
        int index = Columns(table).ToList().IndexOf(column);

        return index < 0 || index >= row.Items.Count ? "" : CellText(row.Items[index]);
    }

    /// <summary>Whatever a cell says, whether it says it as text or as a badge.</summary>
    public static string CellText(PluginComponent cell) =>
        Flatten(cell).Select(Word).FirstOrDefault(word => word.Length > 0) ?? "";
}
