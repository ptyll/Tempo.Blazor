using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class TmDiagramEditorConnectTests : LocalizationTestBase
{
    /// <summary>
    /// Registers the built-in stencil set on first use. Deliberately NOT in the constructor:
    /// resolving a service there locks the BunitContext provider, and the localized-name test
    /// must swap the ITmLocalizer before any resolution happens.
    /// </summary>
    private void EnsureBuiltInStencils()
    {
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        if (registry.Count == 0)
            registry.RegisterProvider(new BuiltInDiagramStencilProvider());
    }

    [Fact]
    public async Task ConnectArrowClick_OpensStencilModal()
    {
        EnsureBuiltInStencils();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60
        });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        // Select node so arrows appear
        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();

        // Click east arrow
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        // Modal should be visible
        cut.Find(".tm-modal").Should().NotBeNull();
        cut.Find(".tm-modal-title").TextContent.Should().Contain("Select shape to connect");
    }

    [Fact]
    public async Task ModalStencilSelection_AddsNodeAndEdge()
    {
        EnsureBuiltInStencils();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60
        });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();

        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        // Click first stencil in the modal grid (general.rectangle)
        var firstStencil = cut.Find(".tm-diagram-connect-modal__item");
        firstStencil.Click();
        cut.Render();

        // Document should now have 2 nodes and 1 edge
        doc.Nodes.Count.Should().Be(2);
        doc.Edges.Count.Should().Be(1);

        var edge = doc.Edges[0];
        edge.SourceNodeId.Should().Be(doc.Nodes[0].Id);
        edge.TargetNodeId.Should().Be(doc.Nodes[1].Id);
    }

    [Fact]
    public async Task EdgeToEdgeCreation_SetsTargetEdgeIdAndT()
    {
        EnsureBuiltInStencils();
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 40, H = 40 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 100, W = 40, H = 40 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var targetEdge = new DiagramEdge { SourceNodeId = n1.Id, TargetNodeId = n2.Id };
        doc.Edges.Add(targetEdge);

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await cut.InvokeAsync(async () => await canvas.Instance.JsOnEdgeCreated(n1.Id, null, null, null, "right", 0.5, null, 0.5, targetEdge.Id, 0.75));

        doc.Edges.Count.Should().Be(2);
        var newEdge = doc.Edges[1];
        newEdge.SourceNodeId.Should().Be(n1.Id);
        newEdge.TargetNodeId.Should().BeNull();
        newEdge.TargetEdgeId.Should().Be(targetEdge.Id);
        newEdge.TargetEdgeT.Should().Be(0.75);
    }

    [Fact]
    public async Task ConnectModal_ExcludesEdgeStencils()
    {
        EnsureBuiltInStencils();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        var nodeStencilCount = registry.GetAll().Count(s => s.Kind == DiagramStencilKind.Node);
        nodeStencilCount.Should().BeGreaterThan(0);

        var items = cut.FindAll(".tm-diagram-connect-modal__item").ToList();
        items.Count.Should().Be(nodeStencilCount,
            "the connect modal must offer exactly the node stencils — edge stencils are not connectable shapes");

        var edgeStencilNames = registry.GetAll()
            .Where(s => s.Kind == DiagramStencilKind.Edge)
            .Select(s => s.Name)
            .ToList();
        foreach (var edgeName in edgeStencilNames)
        {
            items.Should().NotContain(i => i.TextContent.Contains(edgeName),
                $"edge stencil '{edgeName}' must not appear in the connect modal");
        }
    }

    [Fact]
    public void StencilDisplayName_UsesResourceKeyWithFallback()
    {
        var localizer = new MockTmLocalizer(new Dictionary<string, string>
        {
            ["TestStencil_Localized"] = "Lokalizovaný tvar"
        });
        var keyed = new DiagramStencil { Id = "test.keyed", Name = "Fallback Name", NameResourceKey = "TestStencil_Localized" };
        var fallback = new DiagramStencil { Id = "test.plain", Name = "Fallback Name" };

        DiagramStencilDisplayNames.GetStencilDisplayName(keyed, localizer).Should().Be("Lokalizovaný tvar");
        DiagramStencilDisplayNames.GetStencilDisplayName(fallback, localizer).Should().Be("Fallback Name");
    }

    [Fact]
    public async Task ConnectModal_UsesLocalizedStencilNames()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TestStencil_Localized"] = "Lokalizovaný tvar"
        });
        EnsureBuiltInStencils();
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        registry.RegisterStencil(new DiagramStencil
        {
            Id = "test.localized",
            Name = "Fallback Name",
            NameResourceKey = "TestStencil_Localized",
            Category = "General",
            Kind = DiagramStencilKind.Node,
            Origin = DiagramStencilOrigin.TempoOriginal
        });

        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        var labels = cut.FindAll(".tm-diagram-connect-modal__label").Select(l => l.TextContent.Trim()).ToList();
        labels.Should().Contain("Lokalizovaný tvar",
            "the connect modal must show localized stencil names through the shared helper");
        labels.Should().NotContain(l => l.StartsWith('['),
            "no raw resource key may leak into the connect modal");
    }
}
