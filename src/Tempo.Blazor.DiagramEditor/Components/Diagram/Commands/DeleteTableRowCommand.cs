using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;

namespace Tempo.Blazor.Components.Diagram.Commands;

/// <summary>Deletes a row from a table node.</summary>
public sealed class DeleteTableRowCommand : IDiagramCommand
{
    private readonly DiagramDocument _doc;
    private readonly string _nodeId;
    private readonly int _index;
    private readonly double _rowHeight;
    private readonly List<DiagramTableCellData> _removedCells = [];
    private readonly List<DiagramTableCellData> _shiftedCellsSnapshot = [];

    public DeleteTableRowCommand(DiagramDocument doc, string nodeId, int index, double rowHeight = 30)
    {
        _doc = doc;
        _nodeId = nodeId;
        _index = index;
        _rowHeight = rowHeight;
    }

    public string Name => "Delete table row";

    public void Execute()
    {
        var node = _doc.Nodes.FirstOrDefault(n => n.Id == _nodeId);
        if (node is null) return;

        var cells = TableLayoutService.GetCells(node);
        _removedCells.Clear();
        _removedCells.AddRange(cells.Where(c => c.Row == _index || (c.Row < _index && c.Row + c.RowSpan > _index)));
        _shiftedCellsSnapshot.Clear();
        _shiftedCellsSnapshot.AddRange(cells.Where(c => c.Row > _index).Select(c => new DiagramTableCellData
        {
            Row = c.Row,
            Column = c.Column,
            RowSpan = c.RowSpan,
            ColSpan = c.ColSpan,
            Text = c.Text,
            Style = c.Style?.Clone()
        }));

        TableLayoutService.DeleteRow(node, _index);
        node.H -= _rowHeight;
    }

    public void Undo()
    {
        var node = _doc.Nodes.FirstOrDefault(n => n.Id == _nodeId);
        if (node is null) return;

        var rowCount = TableLayoutService.GetRowCount(node);
        TableLayoutService.SetRowCount(node, rowCount + 1);

        var cells = TableLayoutService.GetCells(node);
        // Shift cells back down
        foreach (var cell in cells.Where(c => c.Row >= _index).ToList())
        {
            cell.Row++;
        }
        // Restore removed cells
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
        node.H += _rowHeight;
    }
}
