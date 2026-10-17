using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>Right-side layers panel for managing diagram layers.</summary>
public partial class TmDiagramLayersPanel : TmComponentBase
{
    /// <summary>Document whose layers are managed; a missing default layer is created on first render.</summary>
    [Parameter, EditorRequired] public DiagramDocument? Document { get; set; }

    /// <summary>Ids of the currently selected nodes — enables the "move nodes to layer" section.</summary>
    [Parameter] public string[] SelectedIds { get; set; } = [];

    /// <summary>Command stack used for undoable layer operations; when absent, commands execute directly.</summary>
    [CascadingParameter] public DiagramCommandStack? CommandStack { get; set; }

    /// <summary>When true, all editing paths (add, delete, rename, reorder, toggles) are blocked.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Additional CSS class applied to the panel root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Id of the active (radio-selected) layer; two-way bound.</summary>
    [Parameter] public string? ActiveLayerId { get; set; }

    /// <summary>Raises when the user activates another layer radio.</summary>
    [Parameter] public EventCallback<string?> ActiveLayerIdChanged { get; set; }

    /// <summary>Raises after a command mutated the document so hosts can persist it.</summary>
    [Parameter] public EventCallback<DiagramDocument> DocumentChanged { get; set; }

    private bool _collapsed;
    private string? _editingLayerId;
    private string _editName = "";
    private ElementReference _nameInputRef;
    private bool _pendingNameFocus;
    private string? _draggedLayerId;
    private string? _dragOverLayerId;
    private string? _defaultLayerId;
    private string? _rowMenuLayerId;
    private string? _moveTargetLayerId;
    private readonly string _moveSelectId = $"layers-move-{Guid.NewGuid().ToString("n")[..8]}";

    /// <summary>Zero-based position of the row whose "⋯" menu is open (-1 when none).</summary>
    private int _rowMenuLayerIndex
    {
        get
        {
            if (_rowMenuLayerId is null) return -1;
            var ordered = SortedLayers.ToList();
            return ordered.FindIndex(l => l.Id == _rowMenuLayerId);
        }
    }

    /// <summary>Whether the open row menu's layer is the first in the z-order (Move up is then disabled).</summary>
    private bool RowMenuIsFirst => _rowMenuLayerIndex <= 0;

    /// <summary>Whether the open row menu's layer is the last in the z-order (Move down is then disabled).</summary>
    private bool RowMenuIsLast
    {
        get
        {
            var count = Document?.Layers.Count ?? 0;
            return _rowMenuLayerIndex < 0 || _rowMenuLayerIndex >= count - 1;
        }
    }

    private void ToggleRowMenu(string layerId) => _rowMenuLayerId = _rowMenuLayerId == layerId ? null : layerId;

    private List<DiagramNode> SelectedNodes => SelectedIds
        .Select(id => Document?.Nodes.FirstOrDefault(n => n.Id == id))
        .Where(n => n is not null)
        .Select(n => n!)
        .ToList();

    private IEnumerable<DiagramLayer> SortedLayers =>
        Document?.Layers.OrderBy(l => l.Order).ThenBy(l => l.Name) ?? Enumerable.Empty<DiagramLayer>();

    private List<SelectOption<string>> MoveLayerOptions =>
        SortedLayers.Select(l => new SelectOption<string>(l.Id, l.Name)).ToList();

    protected override void OnParametersSet()
    {
        EnsureDefaultLayer();
        if (string.IsNullOrEmpty(ActiveLayerId) && !string.IsNullOrEmpty(_defaultLayerId))
        {
            ActiveLayerId = _defaultLayerId;
            ActiveLayerIdChanged.InvokeAsync(_defaultLayerId);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingNameFocus)
        {
            _pendingNameFocus = false;
            await _nameInputRef.FocusAsync();
        }
    }

    private void EnsureDefaultLayer()
    {
        if (Document is null) return;
        if (Document.Layers.Count == 0)
        {
            var defaultLayer = new DiagramLayer { Name = Loc["TmDiagramLayers_DefaultLayer"], Order = 0 };
            Document.Layers.Add(defaultLayer);
            _defaultLayerId = defaultLayer.Id;
        }
        else
        {
            _defaultLayerId = Document.Layers.OrderBy(l => l.Order).First().Id;
        }

        // Ensure all nodes/edges without a layer are assigned to the default layer
        foreach (var node in Document.Nodes.Where(n => string.IsNullOrEmpty(n.LayerId)))
            node.LayerId = _defaultLayerId;
        foreach (var edge in Document.Edges.Where(e => string.IsNullOrEmpty(e.LayerId)))
            edge.LayerId = _defaultLayerId;
    }

    private void ToggleCollapse() => _collapsed = !_collapsed;

    private async Task OnAddLayer()
    {
        if (Document is null || ReadOnly) return;
        var name = Loc["TmDiagramLayers_NewLayerName", Document.Layers.Count + 1];
        var cmd = new AddLayerCommand(Document, name);
        if (CommandStack is not null)
            CommandStack.Push(cmd);
        else
            cmd.Execute();

        await ActiveLayerIdChanged.InvokeAsync(cmd.Layer.Id);
        await DocumentChanged.InvokeAsync(Document);
    }

    private async Task OnRemoveLayer(DiagramLayer layer)
    {
        if (Document is null || ReadOnly) return;
        var cmd = new RemoveLayerCommand(Document, layer.Id);
        if (CommandStack is not null)
            CommandStack.Push(cmd);
        else
            cmd.Execute();

        if (ActiveLayerId == layer.Id)
            await ActiveLayerIdChanged.InvokeAsync(_defaultLayerId);
        _rowMenuLayerId = null;
        await DocumentChanged.InvokeAsync(Document);
    }

    /// <summary>The row menu's rename entry: closes the menu and starts the inline edit.</summary>
    private void OnRowMenuRename(DiagramLayer layer)
    {
        _rowMenuLayerId = null;
        OnStartEdit(layer);
    }

    /// <summary>The row menu's delete entry: the same undoable removal as the row's trash.</summary>
    private Task OnRowMenuDelete(DiagramLayer layer) => OnRemoveLayer(layer);

    /// <summary>
    /// Phase 6 (tablet and touch): moves a layer one position with the SAME undoable
    /// <see cref="ReorderLayersCommand"/> the drag-and-drop drop pushes — the non-drag
    /// alternative (WCAG 2.5.7). <paramref name="direction"/> is -1 for "up", +1 for "down".
    /// </summary>
    private async Task OnMoveLayer(DiagramLayer layer, int direction)
    {
        if (Document is null || ReadOnly) return;
        var ordered = SortedLayers.ToList();
        var index = ordered.FindIndex(l => l.Id == layer.Id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= ordered.Count) return;

        var reordered = ordered.ToList();
        (reordered[index], reordered[target]) = (reordered[target], reordered[index]);

        var newOrders = reordered.Select((l, i) => (l.Id, Order: i)).ToDictionary(x => x.Id, x => x.Order);
        var cmd = new ReorderLayersCommand(Document, newOrders);
        if (CommandStack is not null)
            CommandStack.Push(cmd);
        else
            cmd.Execute();

        _rowMenuLayerId = null;
        await DocumentChanged.InvokeAsync(Document);
    }

    private async Task OnSetActive(string layerId)
    {
        if (ReadOnly) return;
        await ActiveLayerIdChanged.InvokeAsync(layerId);
    }

    private async Task OnToggleVisibility(DiagramLayer layer)
    {
        if (Document is null || ReadOnly) return;
        var cmd = new ToggleLayerVisibilityCommand(layer);
        if (CommandStack is not null)
            CommandStack.Push(cmd);
        else
            cmd.Execute();
        await DocumentChanged.InvokeAsync(Document);
    }

    private async Task OnToggleLock(DiagramLayer layer)
    {
        if (Document is null || ReadOnly) return;
        var cmd = new ToggleLayerLockCommand(layer);
        if (CommandStack is not null)
            CommandStack.Push(cmd);
        else
            cmd.Execute();
        await DocumentChanged.InvokeAsync(Document);
    }

    private void OnStartEdit(DiagramLayer layer)
    {
        if (ReadOnly) return;
        _editingLayerId = layer.Id;
        _editName = layer.Name;
        _pendingNameFocus = true;
    }

    private async Task OnNameBlur(DiagramLayer layer)
    {
        await CommitRename(layer);
    }

    /// <summary>
    /// Key handling on the layer name. One handler serves two contexts — the rename
    /// <c>input</c> and the collapsed name <c>span</c> — distinguished by
    /// <c>_editingLayerId == layer.Id</c>: inside the input, Enter commits the rename
    /// and Escape cancels it (starting the edit again would leave the input open
    /// forever); outside, Enter fires on keydown (guarded against auto-repeat) and F2
    /// is a plain keydown shortcut per docs/keyboard-activation-convention.md, with
    /// Space emulated on keyup by <see cref="OnNameKeyUp"/>. Exactly one action per press.
    /// </summary>
    private async Task OnNameKeyDown(KeyboardEventArgs e, DiagramLayer layer)
    {
        if (ReadOnly) return;

        if (_editingLayerId == layer.Id)
        {
            if (e.Key == "Enter" && !e.Repeat)
                await CommitRename(layer);
            else if (e.Key == "Escape")
            {
                _editingLayerId = null;
                StateHasChanged();
            }
            return;
        }

        if (e.Key == "F2" || (e.Key == "Enter" && !e.Repeat))
            OnStartEdit(layer);
    }

    private void OnNameKeyUp(KeyboardEventArgs e, DiagramLayer layer)
    {
        if (ReadOnly || _editingLayerId == layer.Id) return;
        if (e.Key == " ")
            OnStartEdit(layer);
    }

    private async Task CommitRename(DiagramLayer layer)
    {
        if (_editingLayerId != layer.Id) return;
        if (!string.IsNullOrWhiteSpace(_editName) && _editName != layer.Name)
        {
            var cmd = new RenameLayerCommand(layer, _editName.Trim());
            if (CommandStack is not null)
                CommandStack.Push(cmd);
            else
                cmd.Execute();
            await DocumentChanged.InvokeAsync(Document);
        }
        _editingLayerId = null;
    }

    private void OnDragStart(DragEventArgs e, string layerId)
    {
        if (ReadOnly)
        {
            _draggedLayerId = null;
            return;
        }
        _draggedLayerId = layerId;
    }

    private void OnDragOver(DragEventArgs e)
    {
        // preventDefault handled in razor via @ondragover:preventDefault
    }

    private void OnDragEnter(DragEventArgs e, string layerId)
    {
        if (_draggedLayerId != null && _draggedLayerId != layerId)
            _dragOverLayerId = layerId;
    }

    private void OnDragLeave(DragEventArgs e)
    {
        _dragOverLayerId = null;
    }

    private async Task OnDrop(DragEventArgs e, string targetLayerId)
    {
        if (Document is null || ReadOnly || _draggedLayerId is null || _draggedLayerId == targetLayerId)
        {
            _draggedLayerId = null;
            _dragOverLayerId = null;
            return;
        }

        var layers = SortedLayers.ToList();
        var draggedIndex = layers.FindIndex(l => l.Id == _draggedLayerId);
        var targetIndex = layers.FindIndex(l => l.Id == targetLayerId);
        if (draggedIndex < 0 || targetIndex < 0)
        {
            _draggedLayerId = null;
            _dragOverLayerId = null;
            return;
        }

        var reordered = layers.ToList();
        var draggedLayer = reordered[draggedIndex];
        reordered.RemoveAt(draggedIndex);
        reordered.Insert(targetIndex, draggedLayer);

        var newOrders = reordered.Select((l, i) => (l.Id, Order: i)).ToDictionary(x => x.Id, x => x.Order);
        var cmd = new ReorderLayersCommand(Document, newOrders);
        if (CommandStack is not null)
            CommandStack.Push(cmd);
        else
            cmd.Execute();

        _draggedLayerId = null;
        _dragOverLayerId = null;
        await DocumentChanged.InvokeAsync(Document);
    }

    private async Task OnMoveNodesToLayer(string? layerId)
    {
        if (Document is null || ReadOnly || SelectedNodes.Count == 0) return;
        var nodeIds = SelectedNodes.Select(n => n.Id).ToList();
        var cmd = new MoveNodesToLayerCommand(Document, nodeIds, layerId);
        if (CommandStack is not null)
            CommandStack.Push(cmd);
        else
            cmd.Execute();
        _moveTargetLayerId = null;
        await DocumentChanged.InvokeAsync(Document);
    }
}
