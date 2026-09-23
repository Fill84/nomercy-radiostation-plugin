// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using NoMercy.PluginSdk.Abstractions;

namespace NoMercy.Plugin.InternetRadio.Views;

// PluginViews.Button writes an icon under `props.icon`, which is where the legacy
// PluginButton.vue used to read it from. The design system's own NMButton has no such
// prop - it draws an icon only from its `iconLeft`/`iconRight` slots, which NMRender
// resolves from `props.slots` (a name to node-list map), and NMIcon is the component
// built to sit in one. Every icon a button on this plugin used to show - arrowLeft,
// gridMasonry, play, playlistAdd, portableRadio, home - stopped rendering the moment
// buttons started going through PluginViews. This is the one place that puts them
// back, without waiting on the client to grow an `icon` prop it has no plan to.
public static class IconButton
{
    private const string IconComponent = "NMIcon";

    /// <summary>
    /// The same button PluginViews.Button draws, with its icon in the one slot the
    /// client actually reads one from.
    /// </summary>
    public static PluginComponent Build(
        string id,
        string label,
        PluginActionIntent action,
        string icon,
        string? variant = null)
    {
        PluginComponent button = PluginViews.Button(id, label, action, icon, variant);

        // `button.Props` is read-only once built - PluginComponent's Props can only be
        // set through the object initializer that constructs it - so the icon has to
        // be added by building a new component from what PluginViews.Button already
        // decided, rather than mutating its result in place.
        Dictionary<string, object?> props = new(button.Props);

        // Dropped rather than kept: nothing on the client reads `icon` on a button,
        // and a payload still carrying it would read as though it mattered.
        props.Remove("icon");

        props["slots"] = new Dictionary<string, object?>
        {
            ["iconLeft"] = new List<PluginComponent>
            {
                new()
                {
                    // Its own id, derived from the button's: every node needs one,
                    // and the button's own id is already taken.
                    Id = $"{id}-icon",
                    Component = IconComponent,
                    Props = new() { ["icon"] = icon },
                },
            },
        };

        return new PluginComponent
        {
            Id = button.Id,
            Component = button.Component,
            Props = props,
            Items = button.Items,
            Action = button.Action,
        };
    }
}
