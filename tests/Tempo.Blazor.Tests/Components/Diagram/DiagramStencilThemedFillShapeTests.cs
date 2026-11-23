using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Fáze 8c — <see cref="TmDiagramStencilShape"/> emits the catalogued stencil fill as
/// <c>--stencil-fill</c> (so the canvas CSS var() resolves it) and the on-fill ink as a
/// section colour. The node's own style and a heat overlay still win (R5).
/// </summary>
public sealed class DiagramStencilThemedFillShapeTests : LocalizationTestBase
{
    public DiagramStencilThemedFillShapeTests()
    {
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        registry.RegisterProvider(new Archimate3DiagramStencilProvider());
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
        registry.RegisterProvider(new ExtendedDiagramStencilProvider());
    }

    [Fact]
    public void Archimate_application_shape_emits_the_family_fill_and_on_fill_ink()
    {
        var cut = RenderShape("archimate3.application.component");

        var style = cut.Find(".tm-diagram-node__shape").GetAttribute("style");
        style.Should().Contain("--stencil-fill:var(--tm-archimate-application-fill)",
            "TmDiagramStencilShape copies Layout.Fill onto --stencil-fill so the ShapeSvg var() resolves it");
        style.Should().Contain("--stencil-stroke:var(--tm-diagram-stencil-stroke-on-fill)");

        var section = cut.Find(".tm-diagram-node__section").GetAttribute("style");
        section.Should().Contain("color: var(--tm-diagram-node-ink-on-fill)",
            "the ArchiMate TextSection colour is the on-fill ink, not the muted canvas ink");
    }

    [Fact]
    public void Host_Fill_wins_over_the_stencil_layout()
    {
        var node = Node("archimate3.application.component");
        node.Style.Fill = "#ff00aa";
        node.Style.Stroke = "#00ffaa";
        node.Style.Color = "#111111";

        var cut = Render<TmDiagramStencilShape>(p => p.Add(c => c.Node, node));

        var style = cut.Find(".tm-diagram-node__shape").GetAttribute("style");
        style.Should().Contain("--stencil-fill:#ff00aa");
        style.Should().Contain("--stencil-stroke:#00ffaa");
        style.Should().NotContain("--tm-archimate-application-fill");

        cut.Find(".tm-diagram-node__section").GetAttribute("style")
            .Should().Contain("color: #111111");
    }

    [Fact]
    public void Heat_overlay_fill_wins_over_the_stencil_layout()
    {
        var node = Node("archimate3.application.component");
        node.Style.Fill = "var(--tm-color-heat-3)";
        node.Style.Color = "var(--tm-color-heat-3-text)";

        var cut = Render<TmDiagramStencilShape>(p => p.Add(c => c.Node, node));

        var style = cut.Find(".tm-diagram-node__shape").GetAttribute("style");
        style.Should().Contain("--stencil-fill:var(--tm-color-heat-3)",
            "ModelingViewerProjection writes node.Style.Fill — that still beats the stencil layout");
        style.Should().NotContain("--tm-archimate-application-fill");
        cut.Find(".tm-diagram-node__section").GetAttribute("style")
            .Should().Contain("color: var(--tm-color-heat-3-text)");
    }

    private static DiagramNode Node(string stencilId)
        => new()
        {
            Id = "n1",
            StencilId = stencilId,
            X = 10,
            Y = 10,
            W = 160,
            H = 80
        };

    private IRenderedComponent<TmDiagramStencilShape> RenderShape(string stencilId)
        => Render<TmDiagramStencilShape>(p => p.Add(c => c.Node, Node(stencilId)));
}
