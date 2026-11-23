using FluentAssertions;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

public class TableTests : DiagramTestBase
{
    [Fact]
    public void InsertTableRowCommand_IncreasesRowCountAndShiftsCells()
    {
        var doc = new DiagramDocument { Title = "Test" };
        var node = new DiagramNode
        {
            Id = "t1",
            StencilId = "table.basic",
            W = 200,
            H = 120,
            Data = new()
            {
                ["rowCount"] = 2,
                ["columnCount"] = 2,
                ["cells"] = new List<DiagramTableCellData>
                {
                    new() { Row = 0, Column = 0, Text = "A" },
                    new() { Row = 1, Column = 1, Text = "B" }
                }
            }
        };
        doc.Nodes.Add(node);

        var cmd = new InsertTableRowCommand(doc, node.Id, 1);
        cmd.Execute();

        TableLayoutService.GetRowCount(node).Should().Be(3);
        var cells = TableLayoutService.GetCells(node);
        cells.Should().ContainSingle(c => c.Row == 0 && c.Column == 0 && c.Text == "A");
        cells.Should().ContainSingle(c => c.Row == 2 && c.Column == 1 && c.Text == "B");
        node.H.Should().Be(150);

        cmd.Undo();
        TableLayoutService.GetRowCount(node).Should().Be(2);
        cells = TableLayoutService.GetCells(node);
        cells.Should().ContainSingle(c => c.Row == 0 && c.Column == 0 && c.Text == "A");
        cells.Should().ContainSingle(c => c.Row == 1 && c.Column == 1 && c.Text == "B");
        node.H.Should().Be(120);
    }

    [Fact]
    public void MergeTableCellsCommand_SetsCorrectRowSpanAndColSpan()
    {
        var doc = new DiagramDocument { Title = "Test" };
        var node = new DiagramNode
        {
            Id = "t1",
            StencilId = "table.basic",
            W = 200,
            H = 120,
            Data = new()
            {
                ["rowCount"] = 2,
                ["columnCount"] = 2,
                ["cells"] = new List<DiagramTableCellData>
                {
                    new() { Row = 0, Column = 0, Text = "A" },
                    new() { Row = 0, Column = 1, Text = "B" },
                    new() { Row = 1, Column = 0, Text = "C" },
                    new() { Row = 1, Column = 1, Text = "D" }
                }
            }
        };
        doc.Nodes.Add(node);

        var selection = new List<(int, int)> { (0, 0), (0, 1), (1, 0), (1, 1) };
        var cmd = new MergeTableCellsCommand(doc, node.Id, selection);
        cmd.Execute();

        var cells = TableLayoutService.GetCells(node);
        cells.Should().ContainSingle();
        cells[0].RowSpan.Should().Be(2);
        cells[0].ColSpan.Should().Be(2);
        cells[0].Text.Should().Be("A B C D");

        cmd.Undo();
        cells = TableLayoutService.GetCells(node);
        cells.Count.Should().Be(4);
    }

    [Fact]
    public void TableStencil_RendersAsHtmlTable()
    {
        var doc = new DiagramDocument
        {
            Title = "Test",
            Width = 1000,
            Height = 1000,
            Nodes =
            [
                new DiagramNode
                {
                    Id = "t1",
                    StencilId = "table.basic",
                    X = 100,
                    Y = 100,
                    W = 200,
                    H = 120,
                    Data = new()
                    {
                        ["rowCount"] = 2,
                        ["columnCount"] = 2,
                        ["cells"] = new List<DiagramTableCellData>
                        {
                            new() { Row = 0, Column = 0, Text = "A" },
                            new() { Row = 0, Column = 1, Text = "B" },
                            new() { Row = 1, Column = 0, Text = "C" },
                            new() { Row = 1, Column = 1, Text = "D" }
                        }
                    }
                }
            ]
        };

        var cut = Render<TmDiagramCanvas>(parameters => parameters
            .Add(p => p.Document, doc));

        var table = cut.Find("table.tm-diagram-node__table");
        table.Should().NotBeNull();

        var cells = cut.FindAll(".tm-diagram-node__table-cell");
        cells.Count.Should().Be(4);
        cells[0].TextContent.Trim().Should().Be("A");
        cells[1].TextContent.Trim().Should().Be("B");
        cells[2].TextContent.Trim().Should().Be("C");
        cells[3].TextContent.Trim().Should().Be("D");
    }

    [Fact]
    public void TableCell_InlineEdit_UpdatesText()
    {
        var doc = new DiagramDocument
        {
            Title = "Test",
            Width = 1000,
            Height = 1000,
            Nodes =
            [
                new DiagramNode
                {
                    Id = "t1",
                    StencilId = "table.basic",
                    X = 100,
                    Y = 100,
                    W = 200,
                    H = 120,
                    Data = new()
                    {
                        ["rowCount"] = 1,
                        ["columnCount"] = 1,
                        ["cells"] = new List<DiagramTableCellData>
                        {
                            new() { Row = 0, Column = 0, Text = "Old" }
                        }
                    }
                }
            ]
        };

        var cut = Render<TmDiagramCanvas>(parameters => parameters
            .Add(p => p.Document, doc));

        var cell = cut.Find(".tm-diagram-node__table-cell");
        cell.TextContent.Trim().Should().Be("Old");

        // We can't easily simulate double-click in bUnit without JS interop,
        // but we can verify the component has the right data structure.
        var node = doc.Nodes[0];
        var cells = TableLayoutService.GetCells(node);
        cells[0].Text = "New";

        cut.Render(parameters => parameters
            .Add(p => p.Document, doc));

        cell = cut.Find(".tm-diagram-node__table-cell");
        cell.TextContent.Trim().Should().Be("New");
    }

    [Fact]
    public void DeleteTableRowCommand_RemovesRowAndShiftsCells()
    {
        var doc = new DiagramDocument { Title = "Test" };
        var node = new DiagramNode
        {
            Id = "t1",
            StencilId = "table.basic",
            W = 200,
            H = 150,
            Data = new()
            {
                ["rowCount"] = 3,
                ["columnCount"] = 2,
                ["cells"] = new List<DiagramTableCellData>
                {
                    new() { Row = 0, Column = 0, Text = "A" },
                    new() { Row = 1, Column = 1, Text = "B" },
                    new() { Row = 2, Column = 0, Text = "C" }
                }
            }
        };
        doc.Nodes.Add(node);

        var cmd = new DeleteTableRowCommand(doc, node.Id, 1);
        cmd.Execute();

        TableLayoutService.GetRowCount(node).Should().Be(2);
        var cells = TableLayoutService.GetCells(node);
        cells.Should().ContainSingle(c => c.Row == 0 && c.Column == 0 && c.Text == "A");
        cells.Should().ContainSingle(c => c.Row == 1 && c.Column == 0 && c.Text == "C");
        cells.Should().NotContain(c => c.Text == "B");

        cmd.Undo();
        TableLayoutService.GetRowCount(node).Should().Be(3);
        cells = TableLayoutService.GetCells(node);
        cells.Should().ContainSingle(c => c.Row == 1 && c.Column == 1 && c.Text == "B");
    }

    [Fact]
    public void SplitTableCellCommand_RestoresOriginalCells()
    {
        var doc = new DiagramDocument { Title = "Test" };
        var node = new DiagramNode
        {
            Id = "t1",
            StencilId = "table.basic",
            W = 200,
            H = 120,
            Data = new()
            {
                ["rowCount"] = 2,
                ["columnCount"] = 2,
                ["cells"] = new List<DiagramTableCellData>
                {
                    new() { Row = 0, Column = 0, RowSpan = 2, ColSpan = 1, Text = "Merged" }
                }
            }
        };
        doc.Nodes.Add(node);

        var cmd = new SplitTableCellCommand(doc, node.Id, 0, 0);
        cmd.Execute();

        var cells = TableLayoutService.GetCells(node);
        cells.Count.Should().Be(2);
        cells.Should().ContainSingle(c => c.Row == 0 && c.Column == 0 && c.Text == "Merged");
        cells.Should().ContainSingle(c => c.Row == 1 && c.Column == 0 && c.Text == "");

        cmd.Undo();
        cells = TableLayoutService.GetCells(node);
        cells.Should().ContainSingle(c => c.Row == 0 && c.Column == 0 && c.RowSpan == 2 && c.Text == "Merged");
    }

    [Fact]
    public void UpdateTableCellStyleCommand_UpdatesStyle()
    {
        var doc = new DiagramDocument { Title = "Test" };
        var node = new DiagramNode
        {
            Id = "t1",
            StencilId = "table.basic",
            W = 200,
            H = 120,
            Data = new()
            {
                ["rowCount"] = 1,
                ["columnCount"] = 1,
                ["cells"] = new List<DiagramTableCellData>
                {
                    new() { Row = 0, Column = 0, Text = "A", Style = new DiagramTableCellStyle { BackgroundColor = "#ffffff", Color = "var(--tm-diagram-node-ink-on-fill)" } }
                }
            }
        };
        doc.Nodes.Add(node);

        var newStyle = new DiagramTableCellStyle { BackgroundColor = "#ff0000", BorderColor = "#000000", Color = "var(--tm-diagram-node-ink-on-fill)" };
        var cmd = new UpdateTableCellStyleCommand(doc, node.Id, 0, 0, newStyle);
        cmd.Execute();

        var cells = TableLayoutService.GetCells(node);
        cells[0].Style!.BackgroundColor.Should().Be("#ff0000");
        cells[0].Style.BorderColor.Should().Be("#000000");
        cells[0].Style.Color.Should().Be("var(--tm-diagram-node-ink-on-fill)");
        cells[0].Style.Should().NotBeSameAs(newStyle);

        cmd.Undo();
        cells = TableLayoutService.GetCells(node);
        cells[0].Style!.BackgroundColor.Should().Be("#ffffff");
        cells[0].Style.BorderColor.Should().BeNull();
        cells[0].Style.Color.Should().Be("var(--tm-diagram-node-ink-on-fill)");
    }
}
