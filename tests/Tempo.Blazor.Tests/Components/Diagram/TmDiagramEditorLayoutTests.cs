using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class TmDiagramEditorLayoutTests : LocalizationTestBase
{
    public TmDiagramEditorLayoutTests()
    {
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void LayoutDropdown_ContainsAllLayoutOptions()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 300, Y = 100, W = 120, H = 60 });
        doc.Edges.Add(new DiagramEdge { SourceNodeId = doc.Nodes[0].Id, TargetNodeId = doc.Nodes[1].Id });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        // Open layout menu
        var layoutBtn = cut.FindAll("button").FirstOrDefault(b =>
            b.TextContent.Contains("Layout", StringComparison.OrdinalIgnoreCase) ||
            b.TextContent.Contains("Rozložit", StringComparison.OrdinalIgnoreCase));
        layoutBtn.Should().NotBeNull();
        layoutBtn!.Click();
        cut.Render();

        var menuItems = cut.FindAll(".tm-dropdown-item");
        var texts = menuItems.Select(m => m.TextContent).ToList();

        // Localized through MockTmLocalizer — a raw resource key must never reach the UI
        texts.Should().ContainEquivalentOf("Hierarchical Top-Down");
        texts.Should().ContainEquivalentOf("Hierarchical Left-Right");
        texts.Should().ContainEquivalentOf("[TmDiagram_LayoutTree]");
        texts.Should().ContainEquivalentOf("[TmDiagram_LayoutTreeLR]");
        texts.Should().ContainEquivalentOf("[TmDiagram_LayoutForce]");
        texts.Should().ContainEquivalentOf("[TmDiagram_LayoutCircle]");
        texts.Should().ContainEquivalentOf("[TmDiagram_LayoutGrid]");
        texts.Should().NotContain(t => t.StartsWith('[') && t.Contains("Hierarchical"));
    }

    [Fact]
    public async Task RunLayoutAsync_Dagre_InvokesRunDagreLayout()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 300, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.SetSelection(doc.Nodes[0].Id, doc.Nodes[1].Id);
        await cut.Instance.RunLayoutAsync("dagre", "TB");

        var invocations = JSInterop.Invocations.Where(i => i.Identifier == "tmDiagramEditor.runDagreLayout");
        invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task RunLayoutAsync_Tree_InvokesRunTreeLayout()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 300, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.SetSelection(doc.Nodes[0].Id, doc.Nodes[1].Id);
        await cut.Instance.RunLayoutAsync("tree", "TB");

        var invocations = JSInterop.Invocations.Where(i => i.Identifier == "tmDiagramEditor.runTreeLayout");
        invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task RunLayoutAsync_Force_InvokesRunForceLayout()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 300, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.SetSelection(doc.Nodes[0].Id, doc.Nodes[1].Id);
        await cut.Instance.RunLayoutAsync("force");

        var invocations = JSInterop.Invocations.Where(i => i.Identifier == "tmDiagramEditor.runForceLayout");
        invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task RunLayoutAsync_Circle_InvokesRunCircleLayout()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 300, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.SetSelection(doc.Nodes[0].Id, doc.Nodes[1].Id);
        await cut.Instance.RunLayoutAsync("circle");

        var invocations = JSInterop.Invocations.Where(i => i.Identifier == "tmDiagramEditor.runCircleLayout");
        invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task RunLayoutAsync_Grid_InvokesRunGridLayout()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 300, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.SetSelection(doc.Nodes[0].Id, doc.Nodes[1].Id);
        await cut.Instance.RunLayoutAsync("grid");

        var invocations = JSInterop.Invocations.Where(i => i.Identifier == "tmDiagramEditor.runGridLayout");
        invocations.Should().ContainSingle();
    }
}
