// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using FluentAssertions;
using NoMercy.Plugin.InternetRadio.Views;
using NoMercy.PluginSdk.Abstractions;
using Xunit;

namespace NoMercy.Plugin.InternetRadio.Tests.Views;

/// <summary>
/// Every component a view emits has to be one the client draws. The plugin used to build
/// its own `Plugin*` tags, which the client kept a second, older map alive for - and which
/// no translation ever reached, because only the design system's own text nodes are
/// resolved through the plugin's language bundle.
/// </summary>
public class ComponentVocabularyTests
{
    // PluginForm and PluginWebView are the SDK's own exceptions: the client keys its form
    // on exactly that name. NMGrid and NMMusicCard are app components on the client's
    // allow-list, which PluginViews has no equivalent for.
    private static readonly string[] Allowed =
    [
        PluginComponentType.Form,
        Ui.MediaGridComponent,
        Ui.MediaCardComponent,
    ];

    public static TheoryData<string, PluginView> EveryView() => ViewFixtures.All();

    [Theory]
    [MemberData(nameof(EveryView))]
    public void NoViewEmitsAHandRolledComponent(string name, PluginView view)
    {
        IEnumerable<string> offenders = ViewFixtures.Flatten(view)
            .Select(node => node.Component)
            .Where(component => component.StartsWith("Plugin", StringComparison.Ordinal))
            .Where(component => !Allowed.Contains(component))
            .Distinct();

        offenders.Should().BeEmpty($"{name} should draw with PluginViews");
    }
}
