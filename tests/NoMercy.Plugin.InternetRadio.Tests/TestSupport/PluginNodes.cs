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
            .Select(node => node.Props.TryGetValue("text", out object? text) ? text?.ToString() ?? "" : "")
            .Where(word => word.Length > 0);
}
