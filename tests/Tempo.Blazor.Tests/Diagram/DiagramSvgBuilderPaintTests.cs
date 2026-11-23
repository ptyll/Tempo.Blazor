using FluentAssertions;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;
using Tempo.Blazor.Components.Diagram.Stencils;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

/// <summary>
/// Phase 4 review (A7), tightened by TG-004: the server-side SVG export of a BPMN node. The shipped
/// stencils paint with the canvas tokens (<c>var(--tm-diagram-paper)</c> /
/// <c>var(--tm-diagram-node-stroke)</c>) — a standalone serialized SVG has NO stylesheet, so a
/// <c>var()</c> paint would render black. The builder therefore resolves a token paint to the
/// palette and lets the NODE's own style win over the stencil layout.
/// </summary>
public class DiagramSvgBuilderPaintTests
{
    private static DiagramSvgRenderer BuildRenderer()
    {
        var registry = new DiagramStencilRegistry();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
        registry.RegisterProvider(new Bpmn2DiagramStencilProvider());
        registry.RegisterProvider(new Archimate3DiagramStencilProvider());
        registry.RegisterProvider(new ExtendedDiagramStencilProvider());
        return new DiagramSvgRenderer(registry);
    }

    private static DiagramDocument BpmnTaskDocument(string? fill = null, string? stroke = null)
    {
        var document = new DiagramDocument();
        document.EnsurePages();
        var node = new DiagramNode
        {
            Id = "task",
            StencilId = "bpmn2.task.user",
            X = 40,
            Y = 40,
            W = 150,
            H = 92
        };
        node.Data["label"] = "Approve";
        if (fill is not null)
        {
            node.Style.Fill = fill;
        }

        if (stroke is not null)
        {
            node.Style.Stroke = stroke;
        }

        document.Pages[0].Nodes.Add(node);
        return document;
    }

    [Fact]
    public void The_server_svg_of_a_bpmn_task_never_carries_a_css_var()
    {
        var svg = BuildRenderer().RenderSvg(BpmnTaskDocument());

        svg.Should().NotContain("var(", "a var() paint cannot resolve in a standalone SVG");
        svg.Should().Contain(DiagramSvgPalette.Light.DefaultNodeFill);
        svg.Should().Contain(DiagramSvgPalette.Light.DefaultNodeStroke);
    }

    [Fact]
    public void The_nodes_own_style_wins_over_the_stencil_layout()
    {
        var svg = BuildRenderer().RenderSvg(BpmnTaskDocument(fill: "#ffcc00", stroke: "#003366"));

        svg.Should().Contain("#ffcc00", "the authored node colour must reach the export");
        svg.Should().Contain("#003366");
        svg.Should().NotContain("var(");
    }

    [Fact]
    public void A_plain_stencil_fill_still_reaches_the_export_unchanged()
    {
        // A non-token stencil fill (a host's own stencil) is emitted verbatim — the palette only
        // replaces the unresolvable var() references.
        var svg = BuildRenderer().RenderSvg(BpmnTaskDocument(fill: "rebeccapurple"));

        svg.Should().Contain("rebeccapurple");
    }

    private static DiagramDocument UmlInitialDocument()
    {
        var document = new DiagramDocument();
        document.EnsurePages();
        document.Pages[0].Nodes.Add(new DiagramNode
        {
            Id = "initial",
            StencilId = "uml.activity-start",
            X = 40,
            Y = 40,
            W = 60,
            H = 60
        });
        return document;
    }

    [Theory]
    [InlineData(DiagramSvgTheme.Light)]
    [InlineData(DiagramSvgTheme.Dark)]
    public void The_uml_initial_node_fills_with_the_stroke_token_in_every_theme(DiagramSvgTheme theme)
    {
        // TG-004 review (B3): the UML initial node is a SOLID ink disc — its layout fill is
        // var(--stencil-stroke), so the export maps the token BY NAME to the palette stroke slot
        // (mapping every var() to the fill slot would paint a white disc). The label ink is
        // var(--stencil-fill) → the palette fill, so the pair keeps its contrast in both themes.
        var palette = DiagramSvgPalette.ForTheme(theme);
        var svg = BuildRenderer().RenderSvg(UmlInitialDocument(), new DiagramSvgRenderOptions { Theme = theme });

        svg.Should().NotContain("var(", "a var() paint cannot resolve in a standalone SVG");
        svg.Should().Contain($"fill=\"{palette.DefaultNodeStroke}\"",
            "the solid disc fills with the palette STROKE colour — the token the stencil names");
        svg.Should().Contain($"fill=\"{palette.DefaultNodeFill}\"",
            "the label ink resolves var(--stencil-fill) to the palette fill (readable on the disc)");
    }

    private static DiagramDocument NodeDocument(string stencilId, string? fill = null, string? stroke = null, string? color = null)
    {
        var document = new DiagramDocument();
        document.EnsurePages();
        var node = new DiagramNode
        {
            Id = "n1",
            StencilId = stencilId,
            X = 40,
            Y = 40,
            W = 160,
            H = 90
        };
        node.Data["label"] = "Label";
        node.Data["name"] = "Label";
        if (fill is not null)
        {
            node.Style.Fill = fill;
        }

        if (stroke is not null)
        {
            node.Style.Stroke = stroke;
        }

        if (color is not null)
        {
            node.Style.Color = color;
        }

        document.Pages[0].Nodes.Add(node);
        return document;
    }

    [Fact]
    public void Light_export_of_archimate_application_keeps_the_2_12_colours()
    {
        // (d) — light canvas AND light SVG stay pixel-identical to 2.12.0. The application
        // component fill is #cfe8ff, the stencil stroke is the historical #1f2937 (NOT the
        // palette DefaultNodeStroke #111827 — R2), and the label is DefaultText #111827 (R3).
        var svg = BuildRenderer().RenderSvg(
            NodeDocument("archimate3.application.component"),
            new DiagramSvgRenderOptions { Theme = DiagramSvgTheme.Light });

        svg.Should().NotContain("var(");
        svg.Should().Contain("fill=\"#cfe8ff\"");
        svg.Should().Contain("stroke=\"#1f2937\"");
        svg.Should().Contain("fill=\"#111827\"");
    }

    [Fact]
    public void Dark_export_of_archimate_application_uses_the_dark_fill_and_on_fill_ink()
    {
        // (c) — dark export must bake the dark application fill and the on-fill ink, not the
        // light hex and not muted #a1a1aa.
        var svg = BuildRenderer().RenderSvg(
            NodeDocument("archimate3.application.component"),
            new DiagramSvgRenderOptions { Theme = DiagramSvgTheme.Dark });

        svg.Should().NotContain("var(");
        svg.Should().Contain("fill=\"#1e3a8a\"",
            "the application fill token resolves through DiagramSvgPalette.Dark.Tokens");
        svg.Should().Contain("fill=\"#e5e7eb\"",
            "on-fill ink in dark is gray-200, not the muted canvas ink");
        svg.Should().NotContain("fill=\"#cfe8ff\"",
            "the light hex must not leak into a dark export");
    }

    [Fact]
    public void Light_export_of_a_builtin_risk_and_extended_process_keeps_the_2_12_colours()
    {
        var risk = BuildRenderer().RenderSvg(
            NodeDocument("project.risk"),
            new DiagramSvgRenderOptions { Theme = DiagramSvgTheme.Light });
        risk.Should().Contain("fill=\"#fef2f2\"");
        risk.Should().Contain("stroke=\"#dc2626\"");
        risk.Should().Contain("fill=\"#991b1b\"");

        var process = BuildRenderer().RenderSvg(
            NodeDocument("tempo-flowchart.process"),
            new DiagramSvgRenderOptions { Theme = DiagramSvgTheme.Light });
        process.Should().Contain("fill=\"#f8fafc\"");
        process.Should().Contain("stroke=\"#1f2937\"");
        process.Should().Contain("fill=\"#111827\"");
    }

    [Fact]
    public void An_unknown_token_falls_back_to_the_slot_default()
    {
        var svg = BuildRenderer().RenderSvg(
            NodeDocument("archimate3.application.component", fill: "var(--tm-does-not-exist)"),
            new DiagramSvgRenderOptions { Theme = DiagramSvgTheme.Light });

        svg.Should().Contain($"fill=\"{DiagramSvgPalette.Light.DefaultNodeFill}\"",
            "an unknown var() must not render black — SlotDefault still wins");
        svg.Should().NotContain("var(--tm-does-not-exist)");
    }

    [Fact]
    public void Host_node_style_fill_wins_over_the_themed_stencil_layout()
    {
        var svg = BuildRenderer().RenderSvg(
            NodeDocument("archimate3.application.component", fill: "#ff00aa", stroke: "#00ffaa", color: "#111111"));

        svg.Should().Contain("#ff00aa");
        svg.Should().Contain("#00ffaa");
        svg.Should().Contain("#111111");
        svg.Should().NotContain("#cfe8ff");
    }

    [Fact]
    public void A_custom_palette_without_Tokens_falls_back_to_the_light_hexes()
    {
        // A host that constructs `new DiagramSvgPalette()` (empty Tokens) must still bake the
        // 2.12.0 light hexes — not DefaultNodeFill — so an ArchiMate application stays #cfe8ff.
        var registry = new DiagramStencilRegistry();
        registry.RegisterProvider(new Archimate3DiagramStencilProvider());
        var page = new DiagramPage { Width = 400, Height = 300 };
        page.Nodes.Add(new DiagramNode
        {
            Id = "app",
            StencilId = "archimate3.application.component",
            X = 10,
            Y = 10,
            W = 160,
            H = 90
        });

        var svg = DiagramSvgBuilder.Build(
            page,
            new DiagramExportOptions(),
            new DiagramSvgPalette(),
            registry.GetStencil);

        svg.Should().NotContain("var(");
        svg.Should().Contain("<rect x=\"0\" y=\"0\" width=\"160\" height=\"90\" rx=\"0\" ry=\"0\" fill=\"#cfe8ff\"",
            "a missing Tokens entry falls back to DiagramSvgPalette.Light (the 2.12.0 hex), not DefaultNodeFill");
        svg.Should().NotContain($"width=\"160\" height=\"90\" rx=\"0\" ry=\"0\" fill=\"{new DiagramSvgPalette().DefaultNodeFill}\"");
    }

    [Fact]
    public void Palette_Tokens_are_read_only_and_share_the_catalogued_names()
    {
        DiagramSvgPalette.Light.Tokens.Keys.Should().BeEquivalentTo(DiagramSvgPalette.StencilTokenNames);
        DiagramSvgPalette.Dark.Tokens.Keys.Should().BeEquivalentTo(DiagramSvgPalette.StencilTokenNames);
        DiagramSvgPalette.Light.Tokens.Keys.Should().BeEquivalentTo(DiagramSvgPalette.Dark.Tokens.Keys);

        var empty = new DiagramSvgPalette();
        empty.Tokens.Should().BeEmpty();
        ((Action)(() => ((IDictionary<string, string>)empty.Tokens)["--x"] = "#000"))
            .Should().Throw<NotSupportedException>(
                "the default Tokens dictionary must be read-only so a host cannot mutate the empty fallback");

        ((Action)(() => ((IDictionary<string, string>)DiagramSvgPalette.Light.Tokens)
            .Add("--tm-host", "#000"))).Should().Throw<NotSupportedException>(
            "Light.Tokens is a shared snapshot — mutation would leak across renders");
    }

    [Fact]
    public void Heat_overlay_token_on_the_node_style_wins_over_the_stencil_fill()
    {
        // ModelingViewerProjection writes node.Style.Fill = var(--tm-color-heat-*) — that must
        // still beat the stencil layout. The builder maps an unknown heat token through the
        // palette Tokens dictionary once ViewerTokens resolve it; a leftover var() is stripped
        // by the viewer. Here the host already wrote a concrete heat colour.
        var svg = BuildRenderer().RenderSvg(
            NodeDocument("archimate3.application.component", fill: "#dc2626", color: "#ffffff"));

        svg.Should().Contain("fill=\"#dc2626\"");
        svg.Should().Contain("fill=\"#ffffff\"");
        svg.Should().NotContain("#cfe8ff");
    }
}
