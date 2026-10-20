using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Configuration;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// The <c>TmDiagramEditor</c> wireframe stencil mirrors the redesigned editor shell — desktop
/// chrome (docked palette, inspector tabs, minimap card, status bar), a tablet rail + drawer
/// variant and a mobile action-bar variant. These tests pin the stencil to that contract so the
/// designer never shows a pre-redesign layout or a dead <c>layoutMode</c> prop.
/// </summary>
public class WireframeDiagramEditorRendererTests
{
    private static async Task<string> RenderAsync(int w, int h, params (string Key, JsonElement Value)[] props)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTempoBlazorWireframe();
        using var provider = services.BuildServiceProvider();

        var element = new WireframeElement { Type = "TmDiagramEditor", X = 0, Y = 0, W = w, H = h };
        foreach (var (key, value) in props)
            element.Props[key] = value;

        var page = new WireframePage { Name = "P", Width = w, Height = h };
        page.Elements.Add(element);

        return await provider.GetRequiredService<IWireframeSvgRenderer>().RenderPageAsync(page);
    }

    private static JsonElement P(string value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement P(bool value) => JsonSerializer.SerializeToElement(value);

    private static bool HasAttr(string svg, string name, string value)
        => Regex.IsMatch(svg, $"{name}=[\"']{Regex.Escape(value)}[\"']");

    [Fact]
    public async Task Default_RendersDesktopShell()
    {
        var svg = await RenderAsync(520, 340);

        HasAttr(svg, "data-layout", "desktop").Should().BeTrue();
        foreach (var region in new[] { "header", "command-row", "palette", "canvas", "inspector", "minimap", "status-bar" })
            HasAttr(svg, "data-region", region).Should().BeTrue($"region {region} renders");
        svg.Should().Contain("Layers");
        svg.Should().Contain("Editing");
        svg.Should().NotContain("action-bar");
    }

    [Fact]
    public async Task AutoLayout_AlwaysRendersDesktop_WhateverTheElementWidth()
    {
        // X11: "auto" (the default) is a presentation choice, not a flow — both a wide and a
        // narrow element render the desktop variant.
        var wide = await RenderAsync(1600, 900);
        var narrow = await RenderAsync(390, 720);

        HasAttr(wide, "data-layout", "desktop").Should().BeTrue();
        HasAttr(narrow, "data-layout", "desktop").Should().BeTrue();
    }

    [Fact]
    public async Task Tablet_HasRailAndDrawer_NoDockedInspector()
    {
        var svg = await RenderAsync(1024, 720, ("layoutMode", P("tablet")));

        HasAttr(svg, "data-layout", "tablet").Should().BeTrue();
        HasAttr(svg, "data-region", "inspector-drawer").Should().BeTrue();
        HasAttr(svg, "data-region", "status-bar").Should().BeTrue();
        HasAttr(svg, "data-region", "command-row").Should().BeTrue();
        HasAttr(svg, "data-region", "palette").Should().BeTrue();
        HasAttr(svg, "data-region", "inspector").Should().BeFalse("the tablet inspector is a drawer, not a docked region");
    }

    [Fact]
    public async Task Mobile_HasActionBar_NoCommandRowNoStatusBar()
    {
        // The mobile action bar carries Select | Shapes | Properties | More (Pan lives in More).
        var svg = await RenderAsync(390, 720, ("layoutMode", P("mobile")));

        HasAttr(svg, "data-layout", "mobile").Should().BeTrue();
        HasAttr(svg, "data-region", "action-bar").Should().BeTrue();
        svg.Should().Contain("Select");
        svg.Should().Contain("Shapes");
        svg.Should().Contain("More");
        foreach (var region in new[] { "command-row", "status-bar", "inspector", "palette" })
            HasAttr(svg, "data-region", region).Should().BeFalse($"region {region} stays tablet+ on mobile");
    }

    [Fact]
    public async Task ViewerPreset_DropsPalette_ShowsReadOnlyState()
    {
        var svg = await RenderAsync(520, 340, ("toolbarPreset", P("viewer")));

        HasAttr(svg, "data-preset", "viewer").Should().BeTrue();
        svg.Should().Contain("Read-only");
        HasAttr(svg, "data-region", "palette").Should().BeFalse("the viewer preset has no shapes palette");
        svg.Should().NotContain("▦", "the palette header toggle is hidden for the viewer preset");
        svg.Should().NotContain("▢", "the minimap header toggle is hidden for the viewer preset");
        svg.Should().Contain("☰", "the inspector header toggle stays");
    }

    [Fact]
    public async Task ReadOnlyMobile_HasNoActionBar()
    {
        var svg = await RenderAsync(390, 720,
            ("layoutMode", P("mobile")),
            ("readOnly", P(true)));

        svg.Should().Contain("Read-only");
        HasAttr(svg, "data-region", "action-bar").Should().BeFalse("a host-pinned read-only viewer hides the action bar");
    }

    [Fact]
    public async Task ViewerPresetMobile_HasNoActionBar()
    {
        // The viewer preset is a host-level state (toolbarPreset=viewer), not only a readOnly
        // flag — on mobile it must hide the action bar and panels sheet just like readOnly does.
        var svg = await RenderAsync(390, 720,
            ("layoutMode", P("mobile")),
            ("toolbarPreset", P("viewer")));

        svg.Should().Contain("Read-only");
        HasAttr(svg, "data-region", "action-bar").Should().BeFalse("the viewer preset hides the action bar on mobile");
        HasAttr(svg, "data-region", "command-row").Should().BeFalse();
    }

    [Fact]
    public async Task ShowMinimapFalse_RemovesMinimap()
    {
        var svg = await RenderAsync(520, 340, ("showMinimap", P(false)));

        HasAttr(svg, "data-region", "minimap").Should().BeFalse();
    }

    [Theory]
    [InlineData("desktop", 320, 200)]
    [InlineData("tablet", 320, 200)]
    [InlineData("mobile", 160, 200)]
    [InlineData("desktop", 60, 40)]
    public async Task SmallSizes_DoNotThrow_AndProduceNoNegativeSizes(string layout, int w, int h)
    {
        var svg = await RenderAsync(w, h, ("layoutMode", P(layout)));

        svg.Should().NotContain("width='-");
        svg.Should().NotContain("width=\"-");
        svg.Should().NotContain("height='-");
        svg.Should().NotContain("height=\"-");
    }
}
