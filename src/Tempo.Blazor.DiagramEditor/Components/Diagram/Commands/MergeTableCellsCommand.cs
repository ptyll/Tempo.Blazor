using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;

namespace Tempo.Blazor.Components.Diagram.Commands;

/// <summary>Merges selected cells inside a table node.</summary>
public sealed class MergeTableCellsCommand : IDiagramCommand
{
    private readonly DiagramDocument _doc;
    private readonly string _nodeId;
    private readonly List<(int Row, int Column)> _selection;
    private List<DiagramTableCellData>? _beforeCells;

    public MergeTableCellsCommand(DiagramDocument doc, string nodeId, IEnumerable<(int Row, int Column)> selection)
    {
        _doc = doc;
        _nodeId = nodeId;
        _selection = selection.ToList();
    }

    public string Name => "Merge table cells";

    public void Execute()
    {
        var node = _doc.Nodes.FirstOrDefault(n => n.Id == _nodeId);
        if (node is null || !TableLayoutService.CanMerge(node, _selection)) return;

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

        TableLayoutService.MergeCells(node, _selection);
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
