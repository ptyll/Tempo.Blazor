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
    public void PageOrientation_IsLabelledSegmentedControl()
    {
        // U9: orientation is a DESCRIBED segmented control — visible labels, role=group bound to
        // the label via aria-labelledby, and exactly one aria-pressed button. Default document
        // is 3000×2000, i.e. landscape.
        var doc = new DiagramDocument();
        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, [])
            .Add(c => c.ReadOnly, false));

        var group = cut.Find(".tm-diagram-properties__segmented--labelled[role='group']");
        var labelledBy = group.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        cut.Find($"#{labelledBy}").TextContent.Trim()
            .Should().Be("Orientation", "the group's accessible name comes from the visible label");

        var buttons = group.QuerySelectorAll("button");
        buttons.Length.Should().Be(2);
        buttons[0].TextContent.Trim().Should().Be("Portrait");
        buttons[1].TextContent.Trim().Should().Be("Landscape");
        buttons.Count(b => b.GetAttribute("aria-pressed") == "true").Should().Be(1);
        buttons[1].GetAttribute("aria-pressed").Should().Be("true", "a 3000×2000 document is landscape");
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
        // readable text next to it, not just the bare color input — fill, stroke AND text colour
        // share the one color-field fragment.
        var wraps = cut.FindAll(".tm-diagram-properties__color-wrap");
        wraps.Should().HaveCount(3);
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

        // D13: a mixed fill must not fall back to a fake "#FFFFFF" — the swatch shows a neutral
        // hatch, the value reads "Mixed" and the input names the mixed state.
        var fillWrap = cut.FindAll(".tm-diagram-properties__color-wrap")[0];
        fillWrap.QuerySelector(".tm-diagram-properties__color-value")!.TextContent.Should().Be("Mixed");
        fillWrap.QuerySelector(".tm-diagram-properties__color-swatch")!.ClassList
            .Should().Contain("tm-diagram-properties__color-swatch--mixed");
        fillWrap.QuerySelector("input[type='color']")!.GetAttribute("aria-label").Should().Be("Fill: Mixed");
    }

    [Fact]
    public void MultiSelectionNodes_SameFill_ShowsTheHex()
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
            Style = new DiagramStyle { Fill = "#ff0000" }
        };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, [n1.Id, n2.Id])
            .Add(c => c.ReadOnly, false));

        // Equal values are not "mixed" — the shared hex shows next to the swatch.
        var fillWrap = cut.FindAll(".tm-diagram-properties__color-wrap")[0];
        fillWrap.QuerySelector(".tm-diagram-properties__color-value")!.TextContent.Should().Be("#FF0000");
        fillWrap.QuerySelector(".tm-diagram-properties__color-swatch")!.ClassList
            .Should().NotContain("tm-diagram-properties__color-swatch--mixed");
    }

    [Fact]
    public void MultiSelectionNodes_SameFillDifferentCase_ShowsTheHex()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 0, Y = 0, W = 100, H = 50,
            Style = new DiagramStyle { Fill = "#FF0000" }
        };
        var n2 = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 200, Y = 0, W = 100, H = 50,
            Style = new DiagramStyle { Fill = "#ff0000" }
        };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, [n1.Id, n2.Id])
            .Add(c => c.ReadOnly, false));

        // Colours compare case-insensitively (OrdinalIgnoreCase): "#FF0000" + "#ff0000" is ONE
        // fill, not a mixed selection — value and mixed flag derive from the same helper, so
        // they can never disagree.
        var fillWrap = cut.FindAll(".tm-diagram-properties__color-wrap")[0];
        fillWrap.QuerySelector(".tm-diagram-properties__color-value")!.TextContent.Should().Be("#FF0000");
        fillWrap.QuerySelector(".tm-diagram-properties__color-swatch")!.ClassList
            .Should().NotContain("tm-diagram-properties__color-swatch--mixed");
        fillWrap.QuerySelector("input[type='color']")!.GetAttribute("aria-label").Should().Be("Fill");
    }

    [Fact]
    public void MultiSelectionNodes_MixedStroke_ShowsMixed()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 0, Y = 0, W = 100, H = 50,
            Style = new DiagramStyle { Fill = "#ffffff", Stroke = "#ff0000" }
        };
        var n2 = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 200, Y = 0, W = 100, H = 50,
            Style = new DiagramStyle { Fill = "#ffffff", Stroke = "#00ff00" }
        };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, [n1.Id, n2.Id])
            .Add(c => c.ReadOnly, false));

        // wraps[0] is the fill (same → hex), wraps[1] is the stroke (different → Mixed).
        var strokeWrap = cut.FindAll(".tm-diagram-properties__color-wrap")[1];
        strokeWrap.QuerySelector(".tm-diagram-properties__color-value")!.TextContent.Should().Be("Mixed");
        strokeWrap.QuerySelector(".tm-diagram-properties__color-swatch")!.ClassList
            .Should().Contain("tm-diagram-properties__color-swatch--mixed");
        strokeWrap.QuerySelector("input[type='color']")!.GetAttribute("aria-label").Should().Be("Stroke: Mixed");
    }

    [Fact]
    public void MultiSelectionEdges_MixedStroke_ShowsMixed()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);
        var e1 = new DiagramEdge { SourceNodeId = n1.Id, TargetNodeId = n2.Id, Style = new DiagramStyle { Stroke = "#ff0000" } };
        var e2 = new DiagramEdge { SourceNodeId = n1.Id, TargetNodeId = n2.Id, Style = new DiagramStyle { Stroke = "#00ff00" } };
        doc.Edges.Add(e1);
        doc.Edges.Add(e2);

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, [e1.Id, e2.Id])
            .Add(c => c.ReadOnly, false));

        var edgeStrokeInput = cut.Find("input[aria-label='Line Color: Mixed']");
        edgeStrokeInput.Should().NotBeNull();
        edgeStrokeInput.ParentElement!.ClassList
            .Should().Contain("tm-diagram-properties__color-swatch--mixed");
        var mixedValue = cut.FindAll(".tm-diagram-properties__color-value")
            .FirstOrDefault(v => v.TextContent == "Mixed");
        mixedValue.Should().NotBeNull("the edge stroke colour names its mixed state in text too");
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

        var buttons = cut.FindAll(".tm-diagram-properties__segmented--labelled .tm-diagram-properties__segmented-btn");
        var landscapeBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("Landscape"));
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

    [Fact]
    public void ShowTitle_DefaultsToTrue()
    {
        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, new DiagramDocument()));

        cut.Instance.ShowTitle.Should().BeTrue();
        cut.Find(".tm-diagram-properties__title").Should().NotBeNull();
    }

    [Fact]
    public void ShowTitle_False_OmitsTheHeading()
    {
        // U4: a host that already labels the panel (a tab, a sheet tab) passes false —
        // no duplicate heading.
        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, new DiagramDocument())
            .Add(c => c.ShowTitle, false));

        cut.FindAll(".tm-diagram-properties__title").Should().BeEmpty();
    }
}
