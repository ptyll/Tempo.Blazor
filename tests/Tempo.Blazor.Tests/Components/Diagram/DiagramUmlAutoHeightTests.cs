using FluentAssertions;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Guards the content-driven UML auto-height (spec-D U7 + decision X9): the shared
/// <see cref="DiagramUmlAutoHeight"/> helper computes the height a UML stencil's content needs,
/// grows (never shrinks) nodes to it on the 10-unit grid, and is applied on insertion, connect
/// and template creation — not only during inline editing. Review fix: <c>Fit</c> is content-driven
/// only — it returns early unless the node data carries a list section (attributes/methods/values),
/// grows to <c>ceil(content/10)*10</c> with no minimum-height floor, and never shrinks; stencils
/// without list sections (activity start/end, note, actor, …) keep their stencil default height.
/// <see cref="DiagramUmlAutoHeight.Compute"/> (the property-edit resize path) shares the same
/// content measurement: grid-rounded, never below the node's current height, no 140/120/100 floor.
/// </summary>
public class DiagramUmlAutoHeightTests : LocalizationTestBase
{
    private static readonly string[] ListSectionKeys = ["attributes", "methods", "values"];

    private static IEnumerable<DiagramStencil> BuiltInUmlStencils() =>
        new BuiltInDiagramStencilProvider()
            .GetStencilSets()
            .SelectMany(set => set.Stencils)
            .Where(stencil => stencil.Id.StartsWith("uml.", StringComparison.Ordinal))
            .OrderBy(stencil => stencil.Id, StringComparer.Ordinal);

    public static TheoryData<string> UmlStencilIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var stencil in BuiltInUmlStencils())
                data.Add(stencil.Id);
            return data;
        }
    }

    private static DiagramNode CreateClassNode(double h, int attributes = 3, int methods = 2)
    {
        var node = new DiagramNode
        {
            StencilId = "uml.class",
            X = 0,
            Y = 0,
            W = 180,
            H = h
        };
        node.Data["name"] = "Customer";
        node.Data["attributes"] = Enumerable.Range(1, attributes).Select(i => $"- attr{i}: string").ToArray();
        node.Data["methods"] = Enumerable.Range(1, methods).Select(i => $"+ method{i}(): void").ToArray();
        return node;
    }

    [Fact]
    public void Compute_Customer3Attributes2Methods_GrowsToGrid_NeverBelowCurrentHeight()
    {
        var node = CreateClassNode(h: 140);
        // 30 (name header) + 1+16+3*18 (attributes) + 1+16+2*18 (methods) = 154 → 160 on the grid.
        // Review fix: no 140/120/100 stencil floor — the node's own height is the floor.
        DiagramUmlAutoHeight.Compute(node, node.Data).Should().Be(160);
    }

    [Fact]
    public void Compute_EditedAttributeOnFittedAbstractClass_LandsOnTheGrid()
    {
        // The property-edit resize path (TmDiagramCanvas.HandleSectionEdit → Compute) must grow the
        // node exactly like Fit: content-driven, rounded up to the 10-unit grid, never below the
        // fitted height. 1 attribute = 38 + 1 + 16 + 18 = 73 (fits the 80 default); editing in a
        // second attribute needs 38 + 1 + 16 + 2*18 = 91 → 100 on the grid.
        var node = new DiagramNode { StencilId = "uml.abstract-class", X = 0, Y = 0, W = 180, H = 80 };
        node.Data["name"] = "Shape";
        node.Data["attributes"] = new[] { "- x: int" };

        DiagramUmlAutoHeight.Fit(node);
        node.H.Should().Be(80, "one attribute fits the fitted default");

        var edited = new Dictionary<string, object>(node.Data) { ["attributes"] = new[] { "- x: int", "- y: int" } };
        var newHeight = DiagramUmlAutoHeight.Compute(node, edited);

        newHeight.Should().Be(100);
        newHeight.Should().Be(Math.Ceiling(newHeight / 10) * 10, "the resize lands on the 10-unit grid");
    }

    [Fact]
    public void Fit_GrowsToGrid_NeverShrinks()
    {
        var small = CreateClassNode(h: 140);
        DiagramUmlAutoHeight.Fit(small);
        small.H.Should().Be(160, "154 rounds up to the 10-unit grid");

        var large = CreateClassNode(h: 300);
        DiagramUmlAutoHeight.Fit(large);
        large.H.Should().Be(300, "Fit never shrinks a node");
    }

    [Fact]
    public void Fit_IgnoresNonUmlStencils()
    {
        var node = new DiagramNode { StencilId = "general.rectangle", H = 50 };
        node.Data["attributes"] = new[] { "a", "b", "c" };
        DiagramUmlAutoHeight.Fit(node);
        node.H.Should().Be(50);
    }

    [Fact]
    public void Fit_WithoutListSections_KeepsStencilDefaultHeight()
    {
        // Activity start/end are circles: no list section exists, so Fit must not touch them.
        foreach (var (stencilId, defaultHeight) in new[] { ("uml.activity-start", 60), ("uml.activity-end", 60), ("uml.note", 80) })
        {
            var node = new DiagramNode { StencilId = stencilId, H = defaultHeight };
            node.Data["label"] = "";
            DiagramUmlAutoHeight.Fit(node);
            node.H.Should().Be(defaultHeight, $"{stencilId} has no list content to fit");
        }
    }

    [Fact]
    public void Fit_ListContentBelowOldFloor_KeepsNodeHeight_NoMinHeightFloor()
    {
        // 1 attribute = 30 + 1 + 16 + 18 = 65 → 70 on the grid. The old implementation applied a
        // 140 class floor via Compute and grew this node to 140; the review fix removed the floor,
        // so a node already taller than its content keeps its height (Fit never shrinks either).
        var node = CreateClassNode(h: 100, attributes: 1, methods: 0);
        node.Data.Remove("methods");
        DiagramUmlAutoHeight.Fit(node);
        node.H.Should().Be(100, "content below the node height never grows it — no min-height floor");
    }

    [Theory]
    [MemberData(nameof(UmlStencilIds))]
    public void Fit_OnInsertWithDefaultData_KeepsDefaultHeight_OrGrowsForClassLike(string stencilId)
    {
        var stencil = BuiltInUmlStencils().Single(s => s.Id == stencilId);
        var node = new DiagramNode
        {
            StencilId = stencil.Id,
            W = stencil.DefaultWidth,
            H = stencil.DefaultHeight
        };
        foreach (var kvp in stencil.DefaultData)
            node.Data[kvp.Key] = kvp.Value;

        DiagramUmlAutoHeight.Fit(node);

        var classLike = stencil.DefaultData.Keys.Any(key => ListSectionKeys.Contains(key));
        if (classLike)
        {
            node.H.Should().BeGreaterThanOrEqualTo(stencil.DefaultHeight,
                $"{stencilId} carries list sections and may only grow (never shrink)");
            if (node.H > stencil.DefaultHeight)
                node.H.Should().Be(Math.Ceiling(node.H / 10) * 10,
                    $"{stencilId} growth lands on the 10-unit grid");
        }
        else
        {
            node.H.Should().Be(stencil.DefaultHeight,
                $"{stencilId} has no list sections in its default data — Fit is content-driven only");
        }
    }

    [Fact]
    public void FitAll_WalksEveryPage()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        doc.Pages[0].Nodes.Add(CreateClassNode(h: 100));
        doc.Pages.Add(new DiagramPage { Name = "Page 2" });
        doc.Pages[1].Nodes.Add(CreateClassNode(h: 100));

        DiagramUmlAutoHeight.FitAll(doc);

        doc.Pages[0].Nodes[0].H.Should().Be(160);
        doc.Pages[1].Nodes[0].H.Should().Be(160);
    }
}
