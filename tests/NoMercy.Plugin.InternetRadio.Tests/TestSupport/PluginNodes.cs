// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio.Tests;

/// <summary>
/// Reading a built view the way the client does.
///
/// These helpers used to reconstruct a table out of nested boxes and ARIA roles, because
/// that is what this plugin was emitting: every component went out under a design-system
/// name, so there was no table, no grid and no form to read - only cards standing in for
/// them. Now that the components are named the way the client names them, a table has
/// columns and rows and this file can simply ask for them.
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

    /// <summary>A card a viewer would call one.</summary>
    public static bool IsCard(PluginComponent node) => node.Component == Ui.CardComponent;

    public static IEnumerable<PluginComponent> Cards(PluginView view) => All(view).Where(IsCard);

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

    // The prop each component says its words under. PluginText is `value`, not `text`:
    // that one rename is why half these tests went quiet.
    private static readonly string[] Speaks = ["value", "label", "title", "subtitle", "message"];

    /// <summary>The single table on a screen.</summary>
    public static PluginComponent Table(PluginView view) =>
        All(view).Single(node => node.Component == Ui.TableComponent);

    public static IEnumerable<PluginComponent> Tables(PluginView view) =>
        All(view).Where(node => node.Component == Ui.TableComponent);

    /// <summary>The column labels, in the order they are drawn.</summary>
    public static IReadOnlyList<string> Columns(PluginComponent table) =>
        [.. ColumnsOf(table).Select(column => column.Label)];

    /// <summary>The rows a viewer counts. The header is a prop, so it is never one of them.</summary>
    public static IReadOnlyList<PluginComponent> Rows(PluginComponent table) => table.Items;

    /// <summary>
    /// What one row says under one column, found by the label the viewer reads rather than
    /// by the key the plugin used.
    /// </summary>
    public static string Value(PluginComponent table, PluginComponent row, string column)
    {
        PluginTableColumn? match = ColumnsOf(table)
            .FirstOrDefault(candidate => candidate.Label == column);

        return match is null
            ? string.Empty
            : row.Props.TryGetValue(match.Key, out object? cell) ? cell?.ToString() ?? "" : "";
    }

    private static IReadOnlyList<PluginTableColumn> ColumnsOf(PluginComponent table) =>
        table.Props.TryGetValue("columns", out object? columns)
        && columns is IReadOnlyList<PluginTableColumn> typed
            ? typed
            : [];
}
