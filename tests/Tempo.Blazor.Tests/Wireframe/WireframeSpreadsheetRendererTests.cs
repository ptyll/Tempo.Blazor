using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Configuration;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// The <c>TmSpreadsheet</c> wireframe stencil mirrors the container-driven spreadsheet chrome:
/// desktop keeps the tabbed ribbon with labelled groups, tablet draws two compact command rows
/// (no tab strip) and mobile a compact header, ONE command row and the core mobile action bar.
/// The responsive band is the additive <c>layoutMode</c> prop (<c>auto|desktop|tablet|mobile</c>),
/// the same X11 convention the diagram editor and dashboard stencils follow: <c>auto</c> renders
/// the desktop variant, because the stencil communicates the chosen presentation — it does not
/// re-flow the canvas.
/// </summary>
public class WireframeSpreadsheetRendererTests
{
    private static async Task<string> RenderAsync(int w, int h, params (string Key, JsonElement Value)[] props)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTempoBlazorWireframe();
        using var provider = services.BuildServiceProvider();

        var element = new WireframeElement { Type = "TmSpreadsheet", X = 0, Y = 0, W = w, H = h };
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

    private static bool HasRegion(string svg, string region) => HasAttr(svg, "data-region", region);

    [Fact]
    public async Task RenderSpreadsheet_LayoutModeAuto_IsDesktop()
    {
        // Both a wide and a narrow element render the desktop variant: "auto" is a presentation
        // choice, not a flow (X11).
        var wide = await RenderAsync(640, 400);
        var narrow = await RenderAsync(390, 720);

        HasAttr(wide, "data-layout", "desktop").Should().BeTrue();
        HasAttr(narrow, "data-layout", "desktop").Should().BeTrue();

        foreach (var region in new[] { "ribbon-tabs", "ribbon-groups", "formula-bar", "grid", "sheet-tabs", "status-bar" })
            HasRegion(wide, region).Should().BeTrue($"desktop region '{region}' renders");

        HasRegion(wide, "command-row").Should().BeFalse("the desktop chrome has the tabbed ribbon, not a compact row");
        HasRegion(wide, "action-bar").Should().BeFalse();
        HasRegion(wide, "mobile-header").Should().BeFalse();

        // The ribbon keeps its labelled groups on desktop.
        foreach (var group in new[] { "Clipboard", "Font", "Alignment", "Number", "Cells", "Insert" })
            wide.Should().Contain(group);
    }

    [Fact]
    public async Task RenderSpreadsheet_LayoutModeTablet_DrawsTwoCompactRows()
    {
        var svg = await RenderAsync(1024, 720, ("layoutMode", P("tablet")));

        HasAttr(svg, "data-layout", "tablet").Should().BeTrue();
        HasRegion(svg, "command-row").Should().BeTrue("tablet renders the compact command rows");
        HasAttr(svg, "data-row-count", "2").Should().BeTrue("tablet draws TWO compact rows");

        HasRegion(svg, "ribbon-tabs").Should().BeFalse("the compact chrome has no ribbon tab strip");
        HasRegion(svg, "ribbon-groups").Should().BeFalse("the labelled ribbon groups collapse into the compact rows");
        HasRegion(svg, "action-bar").Should().BeFalse("the action bar is mobile only");
        HasRegion(svg, "mobile-header").Should().BeFalse();

        // Tablet keeps the full chrome below the command rows.
        foreach (var region in new[] { "formula-bar", "grid", "sheet-tabs", "status-bar" })
            HasRegion(svg, region).Should().BeTrue($"tablet keeps the '{region}' region");

        svg.Should().NotContain("Clipboard", "the labelled ribbon groups are desktop only");
    }

    [Fact]
    public async Task RenderSpreadsheet_LayoutModeMobile_DrawsCompactHeaderOneRowAndActionBar()
    {
        var svg = await RenderAsync(390, 720, ("layoutMode", P("mobile")), ("title", P("Projekt Aurora")));

        HasAttr(svg, "data-layout", "mobile").Should().BeTrue();
        HasRegion(svg, "mobile-header").Should().BeTrue("mobile draws the compact header");
        svg.Should().Contain("Projekt Aurora");

        HasRegion(svg, "command-row").Should().BeTrue();
        HasAttr(svg, "data-row-count", "1").Should().BeTrue("mobile draws ONE compact row");

        HasRegion(svg, "action-bar").Should().BeTrue("mobile draws the core mobile action bar");
        foreach (var label in new[] { "Insert", "Format", "More" })
            svg.Should().Contain(label, $"the mobile action bar carries '{label}'");

        HasRegion(svg, "ribbon-tabs").Should().BeFalse();
        HasRegion(svg, "ribbon-groups").Should().BeFalse();
        HasRegion(svg, "status-bar").Should().BeFalse("a phone has no status bar; its aggregations live in More");
        svg.Should().NotContain("Clipboard");

        // The grid and the sheet tabs stay on every layout.
        foreach (var region in new[] { "formula-bar", "grid", "sheet-tabs" })
            HasRegion(svg, region).Should().BeTrue($"mobile keeps the '{region}' region");
    }

    [Fact]
    public async Task RenderSpreadsheet_MobileOverflow_StopsBeforeTheMoreChip()
    {
        // A phone is 340-390 px wide, so the compact command row cannot hold every chip. The row used to
        // paint the alignment/merge chips straight across the trailing "More" chip (el.W - 30), which read
        // as overlapping controls in the UX capture. A chip that would reach "More" is now skipped, so no
        // coordinate of the row may pass the More chip's label (el.W - 19).
        const int width = 340;
        var svg = await RenderAsync(width, 520, ("layoutMode", P("mobile")));

        var row = Regex.Match(svg, "<g data-region='command-row'[\\s\\S]*?</g>");
        row.Success.Should().BeTrue("the phone chrome draws the compact command row");

        var coordinates = Regex.Matches(row.Value, @"(?:\bx|\bcx)='([0-9.]+)'")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
        coordinates.Should().NotBeEmpty("the row paints at least the history chips and the More chip");
        coordinates.Max().Should().BeLessThanOrEqualTo(width - 18,
            "nothing in the compact row may be drawn under the trailing More chip");
    }

    [Fact]
    public async Task RenderSpreadsheet_UnknownLayoutMode_FallsBackToDesktop()
    {
        var svg = await RenderAsync(640, 400, ("layoutMode", P("giant")));

        HasAttr(svg, "data-layout", "desktop").Should().BeTrue();
        HasRegion(svg, "ribbon-tabs").Should().BeTrue();
    }

    [Fact]
    public async Task RenderSpreadsheet_CompactLayoutsKeepTheOptionalDocumentHeader()
    {
        // Tablet keeps the host's opt-in document header.
        var tablet = await RenderAsync(1024, 720, ("layoutMode", P("tablet")), ("showHeader", P(true)), ("title", P("Aurora")));

        tablet.Should().Contain("Show grid lines");
        HasRegion(tablet, "header").Should().BeTrue();

        // Mobile REPLACES the document header with the compact one (the real components are mutually
        // exclusive: a phone cannot hold the file actions), so exactly one header paints.
        var mobile = await RenderAsync(390, 720, ("layoutMode", P("mobile")), ("showHeader", P(true)), ("title", P("Aurora")));

        HasRegion(mobile, "mobile-header").Should().BeTrue();
        HasRegion(mobile, "header").Should().BeFalse("the mobile compact header replaces the document header");
        mobile.Should().NotContain("Show grid lines");
        mobile.Should().Contain("Aurora");
    }
}
