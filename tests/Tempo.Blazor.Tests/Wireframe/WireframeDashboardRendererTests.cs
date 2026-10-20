using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Configuration;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// The <c>TmDashboard</c> wireframe stencil honors <c>layoutMode</c> like the diagram editor
/// stencil does (2ee91e41, same contract as U18/X11): <c>auto</c> (and any unknown value) always
/// renders the desktop variant, <c>tablet</c> reflows the widget grid to two columns and
/// <c>mobile</c> to a single stacked column with a compact header. These tests pin that contract
/// so the designer never sees a dead <c>layoutMode</c> prop.
/// </summary>
public class WireframeDashboardRendererTests
{
    private static async Task<string> RenderAsync(int w, int h, params (string Key, JsonElement Value)[] props)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTempoBlazorWireframe();
        using var provider = services.BuildServiceProvider();

        var element = new WireframeElement { Type = "TmDashboard", X = 0, Y = 0, W = w, H = h };
        foreach (var (key, value) in props)
            element.Props[key] = value;

        var page = new WireframePage { Name = "P", Width = w, Height = h };
        page.Elements.Add(element);

        return await provider.GetRequiredService<IWireframeSvgRenderer>().RenderPageAsync(page);
    }

    private static JsonElement P(string value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement P(bool value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement P(int value) => JsonSerializer.SerializeToElement(value);

    private static bool HasAttr(string svg, string name, string value)
        => Regex.IsMatch(svg, $"{name}=[\"']{Regex.Escape(value)}[\"']");

    private static readonly Regex RectPattern = new(
        "<rect x='(?<x>-?[0-9.]+)' y='(?<y>-?[0-9.]+)' width='(?<w>-?[0-9.]+)' height='(?<h>-?[0-9.]+)'",
        RegexOptions.Compiled);

    [Fact]
    public async Task Default_RendersDesktopThreeColumnGrid()
    {
        var svg = await RenderAsync(800, 500);

        HasAttr(svg, "data-layout", "desktop").Should().BeTrue();
        HasAttr(svg, "data-region", "widget-grid").Should().BeTrue();
        HasAttr(svg, "data-columns", "3").Should().BeTrue();
        svg.Should().Contain("Widget 1");
        HasAttr(svg, "data-region", "switcher").Should().BeTrue("the switcher renders by default");
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
        HasAttr(narrow, "data-columns", "3").Should().BeTrue();
    }

    [Fact]
    public async Task UnknownLayoutMode_RendersDesktopVariant()
    {
        var svg = await RenderAsync(800, 500, ("layoutMode", P("watch")));

        HasAttr(svg, "data-layout", "desktop").Should().BeTrue();
        HasAttr(svg, "data-columns", "3").Should().BeTrue();
    }

    [Fact]
    public async Task Tablet_RendersTwoColumnGrid()
    {
        var svg = await RenderAsync(1024, 720, ("layoutMode", P("tablet")));

        HasAttr(svg, "data-layout", "tablet").Should().BeTrue();
        HasAttr(svg, "data-columns", "2").Should().BeTrue();
        svg.Should().Contain("Widget 1");
        HasAttr(svg, "data-region", "switcher").Should().BeTrue("the switcher stays on tablet");
    }

    [Fact]
    public async Task Mobile_RendersSingleColumnStack()
    {
        var svg = await RenderAsync(390, 720, ("layoutMode", P("mobile")));

        HasAttr(svg, "data-layout", "mobile").Should().BeTrue();
        HasAttr(svg, "data-columns", "1").Should().BeTrue();
        svg.Should().Contain("Widget 1");
        HasAttr(svg, "data-region", "switcher").Should().BeTrue("the dashboard switcher stays reachable on mobile");
    }

    [Fact]
    public async Task ShowSwitcherFalse_HidesSwitcherPill()
    {
        var svg = await RenderAsync(800, 500, ("showSwitcher", P(false)));

        HasAttr(svg, "data-region", "switcher").Should().BeFalse();
        HasAttr(svg, "data-columns", "3").Should().BeTrue("the grid still renders without the switcher");
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

    [Fact]
    public async Task ColumnsProp_DesktopRendersRequestedColumnCount()
    {
        // f3-review: the grid column count is a prop (default 3), not a hard-coded constant.
        var svg = await RenderAsync(1200, 700, ("columns", P(4)));

        HasAttr(svg, "data-columns", "4").Should().BeTrue();
        svg.Should().Contain("Widget 8", "columns(4) × rows(2) = 8 tiles");
    }

    [Fact]
    public async Task ColumnsProp_TabletCapsAtTwoColumns()
    {
        var svg = await RenderAsync(1024, 720, ("layoutMode", P("tablet")), ("columns", P(6)));

        HasAttr(svg, "data-columns", "2").Should().BeTrue("tablet never exceeds two columns");
    }

    [Fact]
    public async Task ColumnsProp_MobileAlwaysRendersSingleColumn()
    {
        var svg = await RenderAsync(390, 720, ("layoutMode", P("mobile")), ("columns", P(4)));

        HasAttr(svg, "data-columns", "1").Should().BeTrue("mobile always stacks one column");
    }

    [Fact]
    public async Task RowsProp_ControlsTileCount()
    {
        var oneRow = await RenderAsync(1200, 400, ("columns", P(2)), ("rows", P(1)));
        oneRow.Should().Contain("Widget 2", "2 columns × 1 row");
        oneRow.Should().NotContain("Widget 3");

        var threeRows = await RenderAsync(1200, 700, ("columns", P(2)), ("rows", P(3)));
        threeRows.Should().Contain("Widget 6", "2 columns × 3 rows");
    }

    [Theory]
    [InlineData("desktop", 1600, 900, 4, 2)]
    [InlineData("desktop", 800, 500, null, null)]
    [InlineData("tablet", 1024, 720, 6, 3)]
    [InlineData("mobile", 390, 720, 4, 2)]
    [InlineData("desktop", 200, 120, null, null)]
    [InlineData("desktop", 100, 80, null, null)]
    [InlineData("desktop", 60, 40, null, null)]
    public async Task EveryRect_StaysInsideTheElementWidth(string layout, int w, int h, int? columns, int? rows)
    {
        var props = new List<(string Key, JsonElement Value)> { ("layoutMode", P(layout)) };
        if (columns.HasValue) props.Add(("columns", P(columns.Value)));
        if (rows.HasValue) props.Add(("rows", P(rows.Value)));

        var svg = await RenderAsync(w, h, props.ToArray());

        foreach (Match rect in RectPattern.Matches(svg))
        {
            var x = double.Parse(rect.Groups["x"].Value, CultureInfo.InvariantCulture);
            var width = double.Parse(rect.Groups["w"].Value, CultureInfo.InvariantCulture);
            (x + width).Should().BeLessThanOrEqualTo(w + 0.01,
                $"rect at x={x} width={width} must not paint past the element width {w}");
        }
    }

    [Fact]
    public async Task NarrowElement_SkipsSwitcherPillBelowSixtyPixels()
    {
        // f3-review: pillW = min(268, W × 0.42) in every layout; the pill gives up below 60px.
        var svg = await RenderAsync(100, 80);

        HasAttr(svg, "data-region", "switcher").Should().BeFalse("a 42px pill would crowd the title");
    }

    [Fact]
    public async Task LongTitle_EllipsizesToTheSpaceBeforeThePill()
    {
        var svg = await RenderAsync(360, 200, ("title", P("Quarterly Revenue Dashboard With A Very Long Name")));

        // 360px wide: pill = min(268, 151) = 151 → title has ~175px ≈ 22 glyphs at 14px.
        Regex.IsMatch(svg, @"<text[^>]*>Quarterly Revenue Da…</text>").Should().BeTrue(
            "the header title must ellipsize instead of painting under the switcher pill");
    }
}
