using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;

namespace Tempo.Blazor.Components.Diagram.Commands;

/// <summary>Deletes a column from a table node.</summary>
public sealed class DeleteTableColumnCommand : IDiagramCommand
{
    private readonly DiagramDocument _doc;
    private readonly string _nodeId;
    private readonly int _index;
    private readonly double _columnWidth;
    private readonly List<DiagramTableCellData> _removedCells = [];

    public DeleteTableColumnCommand(DiagramDocument doc, string nodeId, int index, double columnWidth = 60)
    {
        _doc = doc;
        _nodeId = nodeId;
        _index = index;
        _columnWidth = columnWidth;
    }

    public string Name => "Delete table column";

    public void Execute()
    {
        var node = _doc.Nodes.FirstOrDefault(n => n.Id == _nodeId);
        if (node is null) return;

        var cells = TableLayoutService.GetCells(node);
        _removedCells.Clear();
        _removedCells.AddRange(cells.Where(c => c.Column == _index || (c.Column < _index && c.Column + c.ColSpan > _index)));

        TableLayoutService.DeleteColumn(node, _index);
        node.W -= _columnWidth;
    }

    public void Undo()
    {
        var node = _doc.Nodes.FirstOrDefault(n => n.Id == _nodeId);
        if (node is null) return;

        var colCount = TableLayoutService.GetColumnCount(node);
        TableLayoutService.SetColumnCount(node, colCount + 1);

        var cells = TableLayoutService.GetCells(node);
        foreach (var cell in cells.Where(c => c.Column >= _index).ToList())
        {
            cell.Column++;
        }
        foreach (var removed in _removedCells)
        {
            cells.Add(new DiagramTableCellData
            {
                Row = removed.Row,
                Column = removed.Column,
                RowSpan = removed.RowSpan,
                ColSpan = removed.ColSpan,
                Text = removed.Text,
                Style = removed.Style?.Clone()
            });
        }
        TableLayoutService.SetCells(node, cells);
        node.W += _columnWidth;
    }
}
