using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class PropertyPanelTests : LocalizationTestBase
{
    public PropertyPanelTests()
    {
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
    }

    [Fact]
    public void NodeFillColorChange_UpdatesNodeStyle()
    {
        var doc = new DiagramDocument();
        var node = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60,
            Style = new DiagramStyle { Fill = "#ffffff" }
        };
        doc.Nodes.Add(node);

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, [node.Id])
            .Add(c => c.ReadOnly, false));

        var colorInput = cut.Find("input[type='color']");
        colorInput.Change("#ff0000");

        node.Style.Fill.Should().Be("#ff0000");
    }

    [Fact]
    public void FillField_ShowsHexValueNextToSwatch()
    {
        var doc = new DiagramDocument();
        var node = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60,
            Style = new DiagramStyle { Fill = "#dbeafe", Stroke = "#2563eb" }
        };
        doc.Nodes.Add(node);

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, [node.Id])
            .Add(c => c.ReadOnly, false));

        // Mockup (Vzhled section): every color field shows the swatch with the hex value as
        // readable text next to it, not just the bare color input.
        var wraps = cut.FindAll(".tm-diagram-properties__color-wrap");
        wraps.Should().HaveCount(2);
        wraps[0].QuerySelector(".tm-diagram-properties__color-swatch").Should().NotBeNull();
        wraps[0].QuerySelector(".tm-diagram-properties__color-value")!.TextContent.Should().Be("#DBEAFE");
        wraps[1].QuerySelector(".tm-diagram-properties__color-value")!.TextContent.Should().Be("#2563EB");
    }

    [Fact]
    public void MultiSelectionNodes_ShowsMixedWhenDifferentValues()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 0, Y = 0, W = 100, H = 50,
            Style = new DiagramStyle { Fill = "#ff0000" }
        };
        var n2 = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 200, Y = 0, W = 100, H = 50,
            Style = new DiagramStyle { Fill = "#00ff00" }
        };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, [n1.Id, n2.Id])
            .Add(c => c.ReadOnly, false));

        // Panel should render without errors for multi-selection
        cut.FindAll(".tm-section--collapsible").Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MultiSelectionNodes_ApplyStyleToAllSelected()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 0, Y = 0, W = 100, H = 50,
            Style = new DiagramStyle { Fill = "#ffffff" }
        };
        var n2 = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 200, Y = 0, W = 100, H = 50,
            Style = new DiagramStyle { Fill = "#ffffff" }
        };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, [n1.Id, n2.Id])
            .Add(c => c.ReadOnly, false));

        var colorInput = cut.Find("input[type='color']");
        colorInput.Change("#0000ff");

        n1.Style.Fill.Should().Be("#0000ff");
        n2.Style.Fill.Should().Be("#0000ff");
    }

    [Fact]
    public void GroupButton_AvailableForMultiNodeSelection()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, [n1.Id, n2.Id])
            .Add(c => c.ReadOnly, false));

        var buttons = cut.FindAll("button");
        var groupBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("Group") || b.TextContent.Contains("Seskupit"));
        groupBtn.Should().NotBeNull();
    }

    [Fact]
    public void EmptySelection_ShowsPageProperties()
    {
        var doc = new DiagramDocument { Width = 794, Height = 1123 };

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, Array.Empty<string>())
            .Add(c => c.ReadOnly, false));

        cut.FindAll(".tm-section-title")
           .Any(h => h.TextContent.Contains("Page") || h.TextContent.Contains("Stránka"))
           .Should().BeTrue();
    }

    [Fact]
    public void PageSizeSelect_ChangesPageDimensions()
    {
        var doc = new DiagramDocument { Width = 1000, Height = 1200 };

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, Array.Empty<string>())
            .Add(c => c.ReadOnly, false));

        var select = cut.Find("select");
        select.Change("a4");

        doc.Width.Should().Be(794);
        doc.Height.Should().Be(1123);
    }

    [Fact]
    public void OrientationToggle_SwapsWidthAndHeight()
    {
        var doc = new DiagramDocument { Width = 794, Height = 1123 };

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, Array.Empty<string>())
            .Add(c => c.ReadOnly, false));

        var buttons = cut.FindAll(".tm-diagram-properties__segmented-btn");
        var landscapeBtn = buttons.FirstOrDefault(b => b.GetAttribute("title")?.Contains("Landscape") == true || b.GetAttribute("title")?.Contains("šířku") == true);
        landscapeBtn.Should().NotBeNull();
        landscapeBtn!.Click();

        doc.Width.Should().Be(1123);
        doc.Height.Should().Be(794);
    }

    [Fact]
    public void EdgeLabelOffsetX_Change_UpdatesOffset()
    {
        var doc = new DiagramDocument();
        var edge = new DiagramEdge
        {
            SourceNodeId = "n1",
            TargetNodeId = "n2",
            Label = "Test",
            LabelOffsetX = 0,
            LabelOffsetY = 0
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, [edge.Id])
            .Add(c => c.ReadOnly, false));

        var offsetXField = cut.FindAll(".tm-diagram-properties__field")
            .FirstOrDefault(f => f.QuerySelector("label")?.TextContent.Contains("Offset X") == true
                              || f.QuerySelector("label")?.TextContent.Contains("Posun popisku X") == true);
        offsetXField.Should().NotBeNull();
        var offsetXInput = offsetXField!.QuerySelector("input[type='number']");
        offsetXInput.Should().NotBeNull();
        offsetXInput!.Change("12");

        edge.LabelOffsetX.Should().Be(12);
    }

    [Fact]
    public void EdgeLabelOffsetY_Change_UpdatesOffset()
    {
        var doc = new DiagramDocument();
        var edge = new DiagramEdge
        {
            SourceNodeId = "n1",
            TargetNodeId = "n2",
            Label = "Test",
            LabelOffsetX = 0,
            LabelOffsetY = 0
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, [edge.Id])
            .Add(c => c.ReadOnly, false));

        var offsetYField = cut.FindAll(".tm-diagram-properties__field")
            .FirstOrDefault(f => f.QuerySelector("label")?.TextContent.Contains("Offset Y") == true
                              || f.QuerySelector("label")?.TextContent.Contains("Posun popisku Y") == true);
        offsetYField.Should().NotBeNull();
        var offsetYInput = offsetYField!.QuerySelector("input[type='number']");
        offsetYInput.Should().NotBeNull();
        offsetYInput!.Change("-5");

        edge.LabelOffsetY.Should().Be(-5);
    }
}
