using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

/// <summary>
/// Phase 4 review (A1): the embedded editors persisted a layout with <c>GridSize="0"</c> to stop the
/// shared canvas from re-snapping a loaded document. That hack broke every unguarded
/// <c>Math.Round(v / GridSize)</c> (a 0 grid produced <c>NaN</c> and the inserted node vanished) and
/// disagreed with the JS grid (<c>opts.gridSize || 8</c>). The contract is now:
/// <see cref="TmDiagramEditor.SnapDocumentOnLoad"/> gates ONLY the load-time snap (default on), and a
/// zero grid means "no snapping" at every division site without ever producing a non-finite value.
/// </summary>
public class TmDiagramEditorSnapDocumentOnLoadTests : DiagramTestBase
{
    private static DiagramDocument DocumentWithNode(double x, double y) => new()
    {
        Title = "Snap",
        Width = 1000,
        Height = 1000,
        Nodes = [new DiagramNode { Id = "n1", StencilId = "rect", X = x, Y = y, W = 120, H = 60 }]
    };

    [Fact]
    public void A_load_re_snaps_the_document_by_default()
    {
        var doc = DocumentWithNode(123, 47);

        Render<TmDiagramEditor>(parameters => parameters
            .Add(p => p.Document, doc));

        doc.Nodes[0].X.Should().Be(120, "the default contract normalizes a loaded document");
        doc.Nodes[0].Y.Should().Be(48);
    }

    [Fact]
    public void SnapDocumentOnLoad_false_keeps_the_authored_geometry()
    {
        var doc = DocumentWithNode(123, 47);

        Render<TmDiagramEditor>(parameters => parameters
            .Add(p => p.Document, doc)
            .Add(p => p.SnapDocumentOnLoad, false));

        doc.Nodes[0].X.Should().Be(123, "a persisted layout must not turn a load into a layout edit");
        doc.Nodes[0].Y.Should().Be(47);
    }

    [Fact]
    public void GridSize_zero_keeps_the_geometry_without_producing_a_non_finite_value()
    {
        var doc = DocumentWithNode(123, 47);

        Render<TmDiagramEditor>(parameters => parameters
            .Add(p => p.Document, doc)
            .Add(p => p.GridSize, 0));

        doc.Nodes[0].X.Should().Be(123);
        doc.Nodes[0].Y.Should().Be(47);
    }

    [Fact]
    public async Task GridSize_zero_insert_produces_finite_coordinates()
    {
        var doc = DocumentWithNode(123, 47);
        var cut = Render<TmDiagramEditor>(parameters => parameters
            .Add(p => p.Document, doc)
            .Add(p => p.GridSize, 0));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await cut.InvokeAsync(() => canvas.Instance.OnDropFromToolbox("rect", 137.5, 89.5));

        var inserted = doc.Nodes.Single(node => node.Id != "n1");
        double.IsFinite(inserted.X).Should().BeTrue($"X must stay finite with snapping off (was {inserted.X})");
        double.IsFinite(inserted.Y).Should().BeTrue($"Y must stay finite with snapping off (was {inserted.Y})");
        inserted.X.Should().Be(137.5, "a zero grid leaves the drop position untouched");
        inserted.Y.Should().Be(89.5);
    }

    [Fact]
    public async Task GridSize_eight_still_snaps_the_drop_position()
    {
        var doc = DocumentWithNode(123, 47);
        var cut = Render<TmDiagramEditor>(parameters => parameters
            .Add(p => p.Document, doc)
            .Add(p => p.GridSize, 8));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await cut.InvokeAsync(() => canvas.Instance.OnDropFromToolbox("rect", 137.5, 89.5));

        var inserted = doc.Nodes.Single(node => node.Id != "n1");
        inserted.X.Should().Be(136);
        inserted.Y.Should().Be(88);
    }
}
