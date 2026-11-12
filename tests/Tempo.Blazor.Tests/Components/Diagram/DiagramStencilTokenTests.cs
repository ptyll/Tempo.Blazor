using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Plan 92e96b25, phase 4 (TG-004) — the diagram canvas and stencil paints are token-driven and the
/// <c>--stencil-*</c> tokens are catalogued for light AND dark mode. Before this phase the BPMN
/// stencil templates and the canvas chrome carried hardcoded <c>#111827</c> / <c>#e5e7eb</c> /
/// <c>#d1d5db</c> / <c>white</c>, the <c>--stencil-*</c> tokens the stencil templates reference were
/// not in the token catalog, and the page stayed white in dark mode. Now: the rendered BPMN stencil
/// SVG carries <c>var(--stencil-*)</c> / <c>var(--tm-*)</c> / <c>currentColor</c> only (never a hex,
/// never <c>white</c>), the catalog ships the five <c>--stencil-*</c> tokens plus the
/// <c>--tm-canvas-*</c> paints for both themes (the dark values mirror the headless
/// <see cref="DiagramSvgPalette.Dark"/> export), and the canvas markup resolves them through
/// <c>var()</c> attributes the SVG export bakes into concrete colours.
/// </summary>
public class DiagramStencilTokenTests : LocalizationTestBase
{
    private const string DiagramEditorCssPath =
        "src/Tempo.Blazor.DiagramEditor/wwwroot/css/components/_diagram-editor.css";

    private const string TokensCssPath = "src/Tempo.Blazor/wwwroot/css/tokens.css";

    private const string TokensDarkCssPath = "src/Tempo.Blazor/wwwroot/css/tokens-dark.css";

    private static readonly Regex HexPaint = new(@"#[0-9a-fA-F]{3,8}\b", RegexOptions.Compiled);

    public DiagramStencilTokenTests()
    {
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
        registry.RegisterProvider(new Bpmn2DiagramStencilProvider());
    }

    private static string DiagramCss() => ThemeCss.StripComments(File.ReadAllText(
        Path.Combine(ThemeCss.RepositoryRoot().FullName, DiagramEditorCssPath)));

    private static string TokensCss() => ThemeCss.StripComments(File.ReadAllText(
        Path.Combine(ThemeCss.RepositoryRoot().FullName, TokensCssPath)));

    private static string TokensDarkCss() => ThemeCss.StripComments(File.ReadAllText(
        Path.Combine(ThemeCss.RepositoryRoot().FullName, TokensDarkCssPath)));

    private static string NormalisedTokens() => ThemeCss.Normalise(TokensCss());

    private static string NormalisedDark() => ThemeCss.Normalise(TokensDarkCss());

    // ── Task 3: the rendered BPMN stencil SVG carries tokens or currentColor, never hex/white ──

    [Fact]
    public void Bpmn2StencilSvgs_Contain_NoHex_And_NoWhite()
    {
        var provider = new Bpmn2DiagramStencilProvider();
        var stencils = provider.GetStencilSets().SelectMany(set => set.Stencils).ToList();
        stencils.Should().NotBeEmpty();

        foreach (var stencil in stencils)
        {
            foreach (var (kind, svg) in new[]
            {
                ("IconSvg", stencil.IconSvg),
                ("ShapeSvg", stencil.Layout.ShapeSvg),
                ("Fill", stencil.Layout.Fill),
                ("Stroke", stencil.Layout.Stroke)
            })
            {
                if (svg is null)
                {
                    continue;
                }

                HexPaint.IsMatch(svg).Should().BeFalse(
                    $"{stencil.Id} {kind} must not carry a hardcoded hex colour: {svg}");
                svg.Should().NotContain("white",
                    $"{stencil.Id} {kind} must not carry the literal 'white' paint — use a token or currentColor");
            }
        }
    }

    [Fact]
    public void Bpmn2StencilLayout_Paints_ThroughCanvasTokens_WithoutHexFallbacks()
    {
        var provider = new Bpmn2DiagramStencilProvider();
        var node = provider.GetStencilSets().SelectMany(set => set.Stencils)
            .First(s => s.Id == "bpmn2.task");

        node.Layout.Fill.Should().Be("var(--tm-diagram-paper)",
            "the BPMN fill rides the canvas paper token (which flips dark in dark mode)");
        node.Layout.Stroke.Should().Be("var(--tm-diagram-node-stroke)",
            "the BPMN stroke rides the canvas ink token");
    }

    // ── Task 1: the --stencil-* tokens are catalogued for light and dark mode ──

    [Fact]
    public void StencilTokens_Are_Catalogued_In_Light()
    {
        var tokens = NormalisedTokens();

        tokens.Should().Contain("--stencil-fill: var(--tm-canvas-paper)",
            "the default stencil fill rides the canvas paper token (white in light mode)");
        tokens.Should().Contain("--stencil-stroke: var(--tm-canvas-paper-ink)");
        tokens.Should().Contain("--stencil-text: var(--tm-canvas-paper-ink)");
        tokens.Should().Contain("--stencil-stroke-width: 1.5px");
        tokens.Should().Contain("--stencil-accent: var(--tm-color-primary)");
    }

    [Fact]
    public void StencilTokens_Are_ReDeclared_On_TheDarkRoots()
    {
        // The dark re-declaration is load-bearing: a token declared only on :root substitutes its
        // var() where it is DECLARED, so a dark theme switched on a parent element would keep the
        // light paints (the convention documented in tokens-dark.css). The stencil tokens repeat
        // the light expression and recompute against the flipped --tm-canvas-* tokens; the
        // concrete dark values are pinned by CanvasPaintTokens_Are_Catalogued_For_BothThemes.
        var dark = NormalisedDark();

        dark.Should().Contain("--stencil-fill: var(--tm-canvas-paper)");
        dark.Should().Contain("--stencil-stroke: var(--tm-canvas-paper-ink)");
        dark.Should().Contain("--stencil-text: var(--tm-canvas-paper-ink)");
        dark.Should().Contain("--stencil-stroke-width: 1.5px");
        dark.Should().Contain("--stencil-accent: var(--tm-color-primary)");
    }

    [Fact]
    public void CanvasPaintTokens_Are_Catalogued_For_BothThemes()
    {
        var light = NormalisedTokens();
        light.Should().Contain("--tm-canvas-paper: var(--tm-paper)");
        light.Should().Contain("--tm-canvas-paper-ink: var(--tm-paper-ink)");
        light.Should().Contain("--tm-canvas-paper-ink-muted: var(--tm-paper-ink-muted)");
        light.Should().Contain("--tm-canvas-paper-border: var(--tm-paper-border)");
        light.Should().Contain("--tm-canvas-grid: #e5e7eb");
        light.Should().Contain("--tm-canvas-grid-strong: #d1d5db");

        var dark = NormalisedDark();
        dark.Should().Contain("--tm-canvas-paper: #27272a");
        dark.Should().Contain("--tm-canvas-paper-ink: #e5e7eb");
        dark.Should().Contain("--tm-canvas-paper-ink-muted: #a1a1aa");
        dark.Should().Contain("--tm-canvas-paper-border: #3f3f46");
        dark.Should().Contain("--tm-canvas-grid: #3f3f46");
        dark.Should().Contain("--tm-canvas-grid-strong: #52525b");
    }

    [Fact]
    public void StencilAndCanvasTokens_Are_RePinned_In_TheLightIsland()
    {
        // ThemeLightIslandTests requires every dark-root re-declaration to be mirrored in the
        // [data-theme="light"] island block.
        var island = Regex.Match(TokensDarkCss(), @"\[data-theme=""light""\]\s*\{(?<body>[^}]*)\}");
        island.Success.Should().BeTrue("the light island block must exist");
        var body = ThemeCss.Normalise(island.Groups["body"].Value);

        body.Should().Contain("--stencil-fill: var(--tm-canvas-paper)");
        body.Should().Contain("--stencil-stroke: var(--tm-canvas-paper-ink)");
        body.Should().Contain("--stencil-text: var(--tm-canvas-paper-ink)");
        body.Should().Contain("--tm-canvas-paper: var(--tm-paper)");
        body.Should().Contain("--tm-canvas-grid: #e5e7eb");
    }

    [Fact]
    public void DarkTheme_Never_ReDeclares_ThePrintedPaperTokens()
    {
        // The TG-004 dark flip is scoped to the DIAGRAM canvas (--tm-canvas-*): the printed/exported
        // paper surfaces (report designer, document pages) keep the theme-invariant --tm-paper*.
        var dark = TokensDarkCss();
        Regex.Matches(dark, @"--tm-paper[a-z-]*\s*:")
            .Should().BeEmpty("the printed-paper guarantee is untouched — only --tm-canvas-* flips");
    }

    // ── The canvas stylesheet aliases the catalog tokens (and carries no dark block) ──

    [Fact]
    public void CanvasCss_Aliases_TheCatalogTokens()
    {
        var canvas = Regex.Match(DiagramCss(), @"\.tm-diagram-canvas\s*\{(?<body>[^}]*)\}");
        canvas.Success.Should().BeTrue("the canvas rule must exist");
        var body = ThemeCss.Normalise(canvas.Groups["body"].Value);

        body.Should().Contain("--tm-diagram-paper: var(--tm-canvas-paper)",
            "the canvas paper rides the catalog token that flips in dark mode");
        body.Should().Contain("--tm-diagram-node-stroke: var(--tm-canvas-paper-ink)");
        body.Should().Contain("--tm-diagram-node-ink: var(--tm-canvas-paper-ink-muted)");

        var page = Regex.Match(DiagramCss(), @"\.tm-diagram-page\s*\{(?<body>[^}]*)\}");
        page.Success.Should().BeTrue();
        ThemeCss.Normalise(page.Groups["body"].Value).Should()
            .Contain("stroke: var(--tm-canvas-paper-border)",
                "the page edge follows the canvas border token (dark in dark mode)");
    }

    [Fact]
    public void GridLines_Are_TokenDriven_InCss()
    {
        var css = DiagramCss();

        var small = Regex.Match(css, @"\.tm-diagram-grid-line\s*\{(?<body>[^}]*)\}");
        small.Success.Should().BeTrue("the minor grid line class must exist");
        ThemeCss.Normalise(small.Groups["body"].Value).Should()
            .Contain("stroke: var(--tm-canvas-grid)");

        var strong = Regex.Match(css, @"\.tm-diagram-grid-line--strong\s*\{(?<body>[^}]*)\}");
        strong.Success.Should().BeTrue("the major grid line class must exist");
        ThemeCss.Normalise(strong.Groups["body"].Value).Should()
            .Contain("stroke: var(--tm-canvas-grid-strong)");
    }

    [Fact]
    public void Print_Repoints_TheCanvasAndStencilTokens_ToTheThemeInvariantPaper()
    {
        // TG-004 review (B1): a custom property substitutes its var() where it is DECLARED, so
        // the --tm-canvas-* / --stencil-* aliases keep the dark values they computed to on :root.
        // The print block must re-declare them on .tm-diagram-editor or a diagram printed from
        // a dark-themed host comes out as a dark sheet with light ink on white paper.
        var block = Regex.Match(DiagramCss(), @"@media print\s*\{\s*\.tm-diagram-editor\s*\{(?<body>[^}]*)\}");
        block.Success.Should().BeTrue("the @media print .tm-diagram-editor block must exist");
        var body = ThemeCss.Normalise(block.Groups["body"].Value);

        body.Should().Contain("--tm-canvas-paper: var(--tm-paper)");
        body.Should().Contain("--tm-canvas-paper-ink: var(--tm-paper-ink)");
        body.Should().Contain("--tm-canvas-paper-ink-muted: var(--tm-paper-ink-muted)");
        body.Should().Contain("--tm-canvas-paper-border: var(--tm-paper-border)");
        body.Should().Contain("--tm-canvas-grid: var(--tm-paper-border)");
        body.Should().Contain("--tm-canvas-grid-strong: var(--tm-paper-border)");
        body.Should().Contain("--tm-canvas-backdrop: var(--tm-paper)");
        body.Should().Contain("--stencil-fill: var(--tm-paper)");
        body.Should().Contain("--stencil-stroke: var(--tm-paper-ink)");
        body.Should().Contain("--stencil-text: var(--tm-paper-ink)");
    }

    // ── Task 2: the rendered canvas markup carries var() attributes, never hex literals ──

    [Fact]
    public void CanvasMarkup_Page_Backdrop_AndGrid_ResolveTokens_NotHex()
    {
        var doc = new DiagramDocument { Width = 800, Height = 600 };
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 10, Y = 10, W = 120, H = 60 });
        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ShowGrid, true));

        var page = cut.Find("rect.tm-diagram-page");
        page.GetAttribute("fill").Should().Be("var(--tm-diagram-paper)",
            "the page fill is the token reference — never the 'white' literal");
        page.GetAttribute("stroke").Should().Be("var(--tm-canvas-paper-border)");

        var backdrop = cut.Find("rect.tm-diagram-page-backdrop");
        backdrop.GetAttribute("fill").Should().Be("var(--tm-diagram-backdrop)");

        var gridSmall = cut.Find("path.tm-diagram-grid-line");
        gridSmall.GetAttribute("stroke").Should().Be("var(--tm-canvas-grid)");
        var gridStrong = cut.Find("path.tm-diagram-grid-line--strong");
        gridStrong.GetAttribute("stroke").Should().Be("var(--tm-canvas-grid-strong)");

        cut.Find(".tm-diagram-canvas__svg").InnerHtml.Should().NotContain("white");
        HexPaint.IsMatch(cut.Find(".tm-diagram-bg-pane").InnerHtml).Should().BeFalse(
            "the background pane (page, backdrop, grid) carries no hardcoded hex: " +
            cut.Find(".tm-diagram-bg-pane").InnerHtml);
    }

    [Fact]
    public void NativeShape_WithoutExplicitPaint_FallsBackToStencilTokens_NotHex()
    {
        var doc = new DiagramDocument { Width = 800, Height = 600 };
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 10, Y = 10, W = 120, H = 60 });
        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ShowPageView, false));

        var shape = cut.Find("rect.tm-diagram-node__shape-bg");
        shape.GetAttribute("fill").Should().Be("var(--stencil-fill)",
            "a node without an explicit fill resolves the catalog token, not #ffffff");
        shape.GetAttribute("stroke").Should().Be("var(--stencil-stroke)",
            "a node without an explicit stroke resolves the catalog token, not #111827");
    }

    [Fact]
    public void Bpmn2Node_ShapeStyle_CarriesCanvasTokens_WithoutHexFallbacks()
    {
        var doc = new DiagramDocument { Width = 800, Height = 600 };
        doc.Nodes.Add(new DiagramNode { StencilId = "bpmn2.task", X = 10, Y = 10, W = 150, H = 92 });
        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ShowPageView, false));

        var shapeStyle = cut.Find(".tm-diagram-node__shape").GetAttribute("style");
        shapeStyle.Should().Contain("--stencil-fill:var(--tm-diagram-paper)",
            "the BPMN layout fill is emitted as the canvas token reference");
        shapeStyle.Should().Contain("--stencil-stroke:var(--tm-diagram-node-stroke)");
        HexPaint.IsMatch(shapeStyle).Should().BeFalse(
            "the emitted shape style carries no hex fallback: " + shapeStyle);
    }

    [Fact]
    public void Edge_WithoutExplicitStroke_FallsBackToStencilToken_NotHex()
    {
        var doc = new DiagramDocument { Width = 800, Height = 600 };
        var a = new DiagramNode { StencilId = "general.rectangle", X = 10, Y = 10, W = 120, H = 60 };
        var b = new DiagramNode { StencilId = "general.rectangle", X = 300, Y = 10, W = 120, H = 60 };
        doc.Nodes.Add(a);
        doc.Nodes.Add(b);
        doc.Edges.Add(new DiagramEdge { SourceNodeId = a.Id, TargetNodeId = b.Id });
        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ShowPageView, false));

        var edgePath = cut.Find("path.tm-diagram-edge-path");
        var stroke = edgePath.GetAttribute("stroke");
        stroke.Should().NotBeNullOrWhiteSpace();
        HexPaint.IsMatch(stroke!).Should().BeFalse(
            "the edge stroke falls back to the stencil token, not #111827: " + stroke);
    }
}
