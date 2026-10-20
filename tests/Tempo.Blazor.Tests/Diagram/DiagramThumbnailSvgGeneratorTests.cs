using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;

namespace Tempo.Blazor.Tests.Diagram;

public class DiagramThumbnailSvgGeneratorTests
{
    [Fact]
    public void Generate_Returns_Svg_Containing_Rect()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        doc.Pages[0].Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60
        });

        var svg = DiagramThumbnailSvgGenerator.Generate(doc);

        svg.Should().Contain("<svg");
        svg.Should().Contain("<rect");
        svg.Should().Contain("x=\"100\"");
        svg.Should().Contain("width=\"120\"");
    }

    [Fact]
    public void Generate_Returns_Svg_Containing_Line_For_Edge()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 40, H = 40 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 100, W = 40, H = 40 };
        doc.Pages[0].Nodes.Add(n1);
        doc.Pages[0].Nodes.Add(n2);
        doc.Pages[0].Edges.Add(new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id
        });

        var svg = DiagramThumbnailSvgGenerator.Generate(doc);

        svg.Should().Contain("<line");
    }

    [Fact]
    public void Generate_Returns_Svg_For_Ellipse()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        doc.Pages[0].Nodes.Add(new DiagramNode
        {
            StencilId = "general.ellipse",
            X = 100,
            Y = 100,
            W = 80,
            H = 50
        });

        var svg = DiagramThumbnailSvgGenerator.Generate(doc);

        svg.Should().Contain("<ellipse");
    }

    [Fact]
    public void Generate_Returns_Svg_For_Rhombus()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        doc.Pages[0].Nodes.Add(new DiagramNode
        {
            StencilId = "general.rhombus",
            X = 100,
            Y = 100,
            W = 80,
            H = 80
        });

        var svg = DiagramThumbnailSvgGenerator.Generate(doc);

        svg.Should().Contain("<polygon");
    }

    [Fact]
    public void Generate_Includes_Node_Label()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        doc.Pages[0].Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60,
            Data = new Dictionary<string, object> { ["label"] = "Hello SVG" }
        });

        var svg = DiagramThumbnailSvgGenerator.Generate(doc);

        svg.Should().Contain("Hello SVG");
    }

    [Fact]
    public void Generate_Escapes_Special_Characters_In_Label()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        doc.Pages[0].Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60,
            Data = new Dictionary<string, object> { ["label"] = "A & B <C>" }
        });

        var svg = DiagramThumbnailSvgGenerator.Generate(doc);

        svg.Should().NotContain("A & B <C>");
        svg.Should().Contain("A &amp; B &lt;C&gt;");
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(-10, 200)]
    [InlineData(320, 0)]
    [InlineData(320, -1)]
    public void Generate_Rejects_NonPositive_Viewport(int width, int height)
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();

        var options = new DiagramThumbnailOptions { Width = width, Height = height };
        var act = () => DiagramThumbnailSvgGenerator.Generate(doc, options);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Generate_WithOptions_Uses_The_Requested_Viewport()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        doc.Pages[0].Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60
        });

        var svg = DiagramThumbnailSvgGenerator.Generate(doc,
            new DiagramThumbnailOptions { Width = 320, Height = 200, IncludeLabels = false, OpaqueBackground = false });

        svg.Should().Contain("width=\"320\"");
        svg.Should().Contain("height=\"200\"");
        svg.Should().NotContain("fill=\"#ffffff\"",
            "template card previews pass OpaqueBackground=false — the card provides the surface");
    }
}
