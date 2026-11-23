using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;

namespace Tempo.Blazor.Components.Diagram.Commands;

/// <summary>Splits a merged cell inside a table node.</summary>
public sealed class SplitTableCellCommand : IDiagramCommand
{
    private readonly DiagramDocument _doc;
    private readonly string _nodeId;
    private readonly int _row;
    private readonly int _column;
    private List<DiagramTableCellData>? _beforeCells;

    public SplitTableCellCommand(DiagramDocument doc, string nodeId, int row, int column)
    {
        _doc = doc;
        _nodeId = nodeId;
        _row = row;
        _column = column;
    }

    public string Name => "Split table cell";

    public void Execute()
    {
        var node = _doc.Nodes.FirstOrDefault(n => n.Id == _nodeId);
        if (node is null || !TableLayoutService.CanSplit(node, _row, _column)) return;

        _beforeCells = TableLayoutService.GetCells(node)
            .Select(c => new DiagramTableCellData
            {
                Row = c.Row,
                Column = c.Column,
                RowSpan = c.RowSpan,
                ColSpan = c.ColSpan,
                Text = c.Text,
                Style = c.Style?.Clone()
            }).ToList();

        TableLayoutService.SplitCell(node, _row, _column);
    }

    public void Undo()
    {
        var node = _doc.Nodes.FirstOrDefault(n => n.Id == _nodeId);
        if (node is null || _beforeCells is null) return;
        TableLayoutService.SetCells(node, _beforeCells.Select(c => new DiagramTableCellData
        {
            Row = c.Row,
            Column = c.Column,
            RowSpan = c.RowSpan,
            ColSpan = c.ColSpan,
            Text = c.Text,
            Style = c.Style?.Clone()
        }).ToList());
    }
}
