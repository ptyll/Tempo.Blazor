using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class TmDiagramEditorInsertTableTests : LocalizationTestBase
{
    public TmDiagramEditorInsertTableTests()
    {
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task InsertTable_AddsNodeWithTableBasicStencil()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        await InvokeInsertTable(cut, 3, 4);

        doc.Nodes.Should().ContainSingle();
        doc.Nodes[0].StencilId.Should().Be("table.basic");
    }

    [Fact]
    public async Task InsertTable_SetsRowCountAndColumnCount()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        await InvokeInsertTable(cut, 3, 4);

        var node = doc.Nodes[0];
        node.Data["rowCount"].Should().Be(3);
        node.Data["columnCount"].Should().Be(4);
    }

    [Fact]
    public async Task InsertTable_GeneratesHeaderCells()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        await InvokeInsertTable(cut, 3, 4);

        var node = doc.Nodes[0];
        var cells = node.Data["cells"] as System.Collections.Generic.List<DiagramTableCellData>;
        cells.Should().NotBeNull();
        cells!.Count.Should().Be(12); // 3 × 4

        // First row cells (0..3) should have bold header style
        for (int c = 0; c < 4; c++)
        {
            var cell = cells.First(x => x.Row == 0 && x.Column == c);
            cell.Style.Should().NotBeNull();
            cell.Style!.FontWeight.Should().Be("bold");
            cell.Style.BackgroundColor.Should().Be("#f3f4f6");
            cell.Text.Should().Be($"Header {c + 1}");
        }

        // Second row cells should have no style
        for (int c = 0; c < 4; c++)
        {
            var cell = cells.First(x => x.Row == 1 && x.Column == c);
            cell.Style.Should().BeNull();
            cell.Text.Should().Be($"Cell 1,{c}");
        }
    }

    [Fact]
    public async Task InsertTable_AddsNodeAtCenterOfViewport()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        var page = doc.ActivePage;
        page.Width = 800;
        page.Height = 600;

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false)
            .Add(e => e.GridSize, 8));

        await InvokeInsertTable(cut, 3, 4);

        var node = doc.Nodes[0];
        // w = 4*80+20 = 340, h = 3*30+20 = 110
        // x = round((800/2 - 340/2) / 8) * 8 = round(230/8)*8 = 29*8 = 232
        // y = round((600/2 - 110/2) / 8) * 8 = round(245/8)*8 = 31*8 = 248
        node.X.Should().BeApproximately(232, 1);
        node.Y.Should().BeApproximately(248, 1);
        node.W.Should().Be(340);
        node.H.Should().Be(110);
    }

    [Fact]
    public async Task TableInserter_RendersAtEditorRoot_OutsideCollapsedToolbarItem()
    {
        // UX review phase 4 (high): the picker opened from the Insert dropdown (the same handler
        // the "More" entry runs) must never be a descendant of a toolbar item — a collapsed group's
        // bar copy turns inert and hidden and would take the picker with it.
        var doc = new DiagramDocument();
        doc.EnsurePages();

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        await cut.InvokeAsync(() =>
            cut.Find("[data-tm-group='insert'] .tm-dropdown-trigger").Click());
        await cut.InvokeAsync(() =>
            cut.FindAll(".tm-dropdown-item")
                .First(item => item.TextContent.Contains("Table"))
                .Click());

        // The selection closes the menu, yields and only then runs the insert handler.
        AngleSharp.Dom.IElement? popover = null;
        cut.WaitForAssertion(
            () => popover = cut.Find(".tm-diagram-table-inserter-popover"));
        popover.Should().NotBeNull("the picker opens from the bar dropdown");

        var ancestors = Ancestors(popover!).ToList();
        ancestors.Should().NotContain(
            a => a.HasAttribute("inert") || a.ClassList.Contains("tm-toolbar-item--collapsed"),
            "an inert or collapsed toolbar item must never contain the picker");
        ancestors.Should().NotContain(
            a => a.ClassList.Contains("tm-diagram-editor__toolbar"),
            "the picker renders at the editor root, outside the collapsible command row");
        popover.GetAttribute("style").Should().Contain("left:")
            .And.Contain("top:", "the popover is pinned at the measured viewport point");
    }

    private static IEnumerable<AngleSharp.Dom.IElement> Ancestors(AngleSharp.Dom.IElement element)
    {
        var current = element.ParentElement;
        while (current is not null)
        {
            yield return current;
            current = current.ParentElement;
        }
    }

    private static async Task InvokeInsertTable(IRenderedComponent<TmDiagramEditor> cut, int rows, int columns)
    {
        var method = typeof(TmDiagramEditor).GetMethod("InsertTable", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        await cut.InvokeAsync(async () =>
        {
            await (Task)method!.Invoke(cut.Instance, new object[] { rows, columns })!;
        });
    }
}
