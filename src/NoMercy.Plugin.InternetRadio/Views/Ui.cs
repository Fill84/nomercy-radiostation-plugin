// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.Plugins.Abstractions;

namespace NoMercy.Plugin.InternetRadio;

// The components the dashboard actually renders, named the way it names them.
//
// This file exists because PluginComponentType and the deployed client disagree, and the
// disagreement is silent. The contract this plugin compiles against maps every component
// onto a design-system name:
//
//     Container = List = Row = Grid = Card = Detail = Form = Table = "NMCard"
//
// The client keys its plugin components by their own names instead - "PluginForm",
// "PluginGrid", "PluginCard" - and resolves a node in two steps: if the name is a
// design-system component it renders it as one, otherwise it looks in the plugin map. So
// every node this plugin sent as "NMCard" was drawn as a design-system card, and the
// plugin components behind those names were never reached.
//
// That one mismatch is most of what has looked broken:
//
//   - PluginForm is a real <form> that collects its fields and submits them under
//     payload.payload. Sent as "NMCard" it became a clickable box with no form in it, so
//     every submit arrived as "{}" and searching by typing could not be made to work.
//   - PluginGrid is `grid-cols-[repeat(auto-fill,minmax(10rem,1fr))]` - a real responsive
//     grid that sizes every tile alike. Sent as "NMCard" nothing laid the tiles out, which
//     is why they were all different sizes.
//   - PluginTable draws a table with columns. Sent as "NMCard" it drew a stack of boxes.
//
// The names and props below were read off the running bundle rather than guessed, so they
// are what the dashboard renders today. If the client later moves to the design-system
// names, this file is the one place that has to change.
public static class Ui
{
    public const string ContainerComponent = "PluginContainer";
    public const string TextComponent = "PluginText";
    public const string ImageComponent = "PluginImage";
    public const string RowComponent = "PluginRow";
    public const string GridComponent = "PluginGrid";
    public const string CardComponent = "PluginCard";
    public const string DetailComponent = "PluginDetail";
    public const string ButtonComponent = "PluginButton";
    public const string FormComponent = "PluginForm";
    public const string EmptyStateComponent = "PluginEmptyState";
    public const string TableComponent = "PluginTable";
    public const string BadgeComponent = "PluginBadge";

    /// <summary>A column of children.</summary>
    public static PluginComponent Container(string id, params PluginComponent[] items) =>
        new() { Id = id, Component = ContainerComponent, Items = [.. items] };

    /// <summary>
    /// A responsive grid. Every tile is the same size because the client sizes them:
    /// auto-fill over a 10rem minimum, which is what a plugin cannot express by hand.
    /// </summary>
    public static PluginComponent Grid(string id, params PluginComponent[] items) =>
        new() { Id = id, Component = GridComponent, Items = [.. items] };

    /// <inheritdoc cref="Grid(string, PluginComponent[])"/>
    public static PluginComponent Grid(string id, IEnumerable<PluginComponent> items) =>
        new() { Id = id, Component = GridComponent, Items = [.. items] };

    /// <summary>A wrapping row.</summary>
    public static PluginComponent Row(string id, params PluginComponent[] items) =>
        new() { Id = id, Component = RowComponent, Items = [.. items] };

    /// <inheritdoc cref="Row(string, PluginComponent[])"/>
    public static PluginComponent Row(string id, IEnumerable<PluginComponent> items) =>
        new() { Id = id, Component = RowComponent, Items = [.. items] };

    /// <summary>`value`, not `text`: PluginText reads its own prop name.</summary>
    public static PluginComponent Text(string id, string value, string? variant = null) =>
        new()
        {
            Id = id,
            Component = TextComponent,
            Props = new() { ["value"] = value, ["variant"] = variant },
        };

    /// <summary>`url`, not `src`.</summary>
    public static PluginComponent Image(string id, string url, string? alt = null) =>
        new()
        {
            Id = id,
            Component = ImageComponent,
            Props = new() { ["url"] = url, ["alt"] = alt },
        };

    public static PluginComponent Button(
        string id,
        string label,
        PluginActionIntent action,
        string? icon = null,
        string? variant = null) =>
        new()
        {
            Id = id,
            Component = ButtonComponent,
            Props = new() { ["label"] = label, ["icon"] = icon, ["variant"] = variant },
            Action = action,
        };

    /// <summary>
    /// A tile: image, title, subtitle. It becomes a button when it carries an action, which
    /// is how one click plays a station.
    /// </summary>
    public static PluginComponent Card(
        string id,
        string title,
        string? subtitle = null,
        string? image = null,
        PluginActionIntent? action = null) =>
        new()
        {
            Id = id,
            Component = CardComponent,
            Props = new() { ["title"] = title, ["subtitle"] = subtitle, ["image"] = image },
            Action = action,
        };

    public static PluginComponent Detail(
        string id,
        string title,
        string? description,
        string? image,
        params PluginComponent[] items) =>
        new()
        {
            Id = id,
            Component = DetailComponent,
            Props = new() { ["title"] = title, ["description"] = description, ["image"] = image },
            Items = [.. items],
        };

    /// <summary>
    /// A real form. Its fields are collected on submit and sent under `payload.payload`,
    /// which is what makes typing possible at all.
    /// </summary>
    public static PluginComponent Form(
        string id,
        string submitLabel,
        PluginActionIntent action,
        params PluginFormField[] fields) =>
        new()
        {
            Id = id,
            Component = FormComponent,
            Props = new() { ["submitLabel"] = submitLabel, ["fields"] = fields },
            Action = action,
        };

    public static PluginComponent EmptyState(string id, string title, string message) =>
        new()
        {
            Id = id,
            Component = EmptyStateComponent,
            Props = new() { ["title"] = title, ["message"] = message },
        };

    public static PluginComponent Table(
        string id,
        IReadOnlyList<PluginTableColumn> columns,
        IEnumerable<PluginComponent> rows,
        string? emptyMessage = null) =>
        new()
        {
            Id = id,
            Component = TableComponent,
            Props = new() { ["columns"] = columns, ["emptyMessage"] = emptyMessage },
            Items = [.. rows],
        };

    /// <summary>One row of a table: a cell per column key.</summary>
    public static PluginComponent TableRow(
        string id,
        IReadOnlyDictionary<string, object?> cells,
        PluginActionIntent? action = null) =>
        new()
        {
            Id = id,
            Component = RowComponent,
            Props = new(cells),
            Action = action,
        };

    public static PluginComponent Badge(string id, string label, string variant) =>
        new()
        {
            Id = id,
            Component = BadgeComponent,
            Props = new() { ["label"] = label, ["variant"] = variant },
        };
}
