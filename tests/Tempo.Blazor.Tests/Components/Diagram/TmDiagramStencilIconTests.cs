using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Fáze 15: the public <see cref="TmDiagramStencilIcon"/> — the small stencil preview the
/// Modeling palette and legend embed instead of duplicating the SVG pipeline. Always
/// aria-hidden (the tile/legend row owns the visible label); an unknown stencil renders the
/// generic fallback shape.
/// </summary>
public sealed class TmDiagramStencilIconTests : LocalizationTestBase
{
    public TmDiagramStencilIconTests()
    {
        var registry = new DiagramStencilRegistry();
        registry.RegisterStencil(new DiagramStencil
        {
            Id = "phase15.icon.node",
            Name = "Icon node",
            Category = "phase15",
            Kind = DiagramStencilKind.Node,
            IconSvg = "<rect x='2' y='8' width='28' height='16' rx='2' fill='#123456' />",
            Origin = DiagramStencilOrigin.TempoOriginal
        });
        registry.RegisterStencil(new DiagramStencil
        {
            Id = "phase15.icon.edge",
            Name = "Icon edge",
            Category = "phase15",
            Kind = DiagramStencilKind.Edge,
            Origin = DiagramStencilOrigin.TempoOriginal
        });
        Services.AddSingleton(registry);
    }

    [Fact]
    public void Renders_registered_stencil_icon_svg()
    {
        using var cut = Render<TmDiagramStencilIcon>(parameters => parameters
            .Add(p => p.StencilId, "phase15.icon.node"));

        var svg = cut.Find("svg.tm-diagram-stencil-icon");
        svg.GetAttribute("aria-hidden").Should().Be("true", "the label text owns the accessible name");
        svg.GetAttribute("data-stencil-id").Should().Be("phase15.icon.node");
        svg.InnerHtml.Should().Contain("#123456", "the registered stencil's preview SVG renders");
        svg.GetAttribute("style").Should().Contain("32px", "the default size matches the toolbox tile");
    }

    [Fact]
    public void Unknown_stencil_renders_fallback_icon_aria_hidden()
    {
        using var cut = Render<TmDiagramStencilIcon>(parameters => parameters
            .Add(p => p.StencilId, "missing.stencil")
            .Add(p => p.Size, "1rem"));

        var svg = cut.Find("svg.tm-diagram-stencil-icon");
        svg.GetAttribute("aria-hidden").Should().Be("true");
        svg.GetAttribute("data-fallback").Should().Be("true", "an unknown stencil falls back to the generic shape");
        svg.GetAttribute("style").Should().Contain("1rem", "the Size parameter drives the CSS box");
        svg.InnerHtml.Should().NotContain("#123456");
    }

    [Fact]
    public void Edge_stencil_fallback_renders_the_edge_shape()
    {
        using var cut = Render<TmDiagramStencilIcon>(parameters => parameters
            .Add(p => p.StencilId, "phase15.icon.edge"));

        var svg = cut.Find("svg.tm-diagram-stencil-icon[data-fallback='true']");
        svg.InnerHtml.Should().Contain("<path", "an edge without an IconSvg falls back to the edge glyph");
    }
}
