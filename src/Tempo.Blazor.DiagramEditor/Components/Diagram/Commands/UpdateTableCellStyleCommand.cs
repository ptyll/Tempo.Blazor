using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;

namespace Tempo.Blazor.Components.Diagram.Commands;

/// <summary>Updates the style of a specific table cell.</summary>
public sealed class UpdateTableCellStyleCommand : IDiagramCommand
{
    private readonly DiagramDocument _doc;
    private readonly string _nodeId;
    private readonly int _row;
    private readonly int _column;
    private readonly DiagramTableCellStyle? _oldStyle;
    private readonly DiagramTableCellStyle? _newStyle;

    public UpdateTableCellStyleCommand(DiagramDocument doc, string nodeId, int row, int column, DiagramTableCellStyle? newStyle)
    {
        _doc = doc;
        _nodeId = nodeId;
        _row = row;
        _column = column;
        _newStyle = newStyle?.Clone();
        var cells = TableLayoutService.GetCells(doc.Nodes.FirstOrDefault(n => n.Id == nodeId));
        var cell = cells.FirstOrDefault(c => c.Row == row && c.Column == column);
        _oldStyle = cell?.Style?.Clone();
    }

    public string Name => "Update table cell style";

    public void Execute()
    {
        var node = _doc.Nodes.FirstOrDefault(n => n.Id == _nodeId);
        if (node is null) return;
        var cells = TableLayoutService.GetCells(node);
        var cell = cells.FirstOrDefault(c => c.Row == _row && c.Column == _column);
        if (cell is null) return;
        cell.Style = _newStyle?.Clone();
        TableLayoutService.SetCells(node, cells);
    }

    public void Undo()
    {
        var node = _doc.Nodes.FirstOrDefault(n => n.Id == _nodeId);
        if (node is null) return;
        var cells = TableLayoutService.GetCells(node);
        var cell = cells.FirstOrDefault(c => c.Row == _row && c.Column == _column);
        if (cell is null) return;
        cell.Style = _oldStyle?.Clone();
        TableLayoutService.SetCells(node, cells);
    }
}
