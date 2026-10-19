using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Components.Overlay;
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

    /// <summary>When false, the panel renders without its own collapse tab and fills its host's width —
    /// for hosts (TmDiagramEditor's inspector) that own the collapse. Default true (standalone use).</summary>
    [Parameter] public bool ShowCollapseToggle { get; set; } = true;

    /// <summary>Whether the panel renders its own heading. Hosts that already label the panel (a tab, a sheet tab) pass false. Defaults to true.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>Id of the active (radio-selected) layer; two-way bound.</summary>
    [Parameter] public string? ActiveLayerId { get; set; }

    /// <summary>Raises when the user activates another layer radio.</summary>
    [Parameter] public EventCallback<string?> ActiveLayerIdChanged { get; set; }

    /// <summary>Raises after a command mutated the document so hosts can persist it.</summary>
    [Parameter] public EventCallback<DiagramDocument> DocumentChanged { get; set; }

    /// <summary>
    /// How each row's "⋯" menu presents on a narrow viewport — handed to the core
    /// <see cref="TmContextMenu.MobilePresentation"/>. Default <see cref="PanelPresentation.Auto"/>
    /// resolves from the viewport scope (content-height bottom sheet on a phone, anchored popover
    /// otherwise). <c>TmDiagramEditor</c> passes <see cref="PanelPresentation.Sheet"/> on its
    /// mobile layout and <see cref="PanelPresentation.Popover"/> elsewhere.
    /// </summary>
    [Parameter] public PanelPresentation RowMenuPresentation { get; set; } = PanelPresentation.Auto;

    private bool _collapsed;
    private string? _editingLayerId;
    private string _editName = "";
    private ElementReference _nameInputRef;
    private bool _pendingNameFocus;
    private string? _draggedLayerId;
    private string? _dragOverLayerId;
    private string? _defaultLayerId;
    private string? _moveTargetLayerId;
    private readonly string _moveSelectId = $"layers-move-{Guid.NewGuid().ToString("n")[..8]}";
    private readonly string _titleId = $"tm-diagram-layers-title-{Guid.NewGuid().ToString("n")[..8]}";
    private readonly Dictionary<string, ElementReference> _nameRefs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RowMenuHolder> _rowMenuRefs = new(StringComparer.Ordinal);
    private string? _announcement;
    private string? _focusNameAfterRender;   // layer id whose name span gets focus after the next render
    private string? _focusRowMenuTriggerAfterRender; // layer id whose "⋯" trigger gets focus after the next render
    private bool _focusAddButtonAfterRender;
    private ElementReference _addButtonRef;

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
        PruneStaleRefs();
        if (string.IsNullOrEmpty(ActiveLayerId) && !string.IsNullOrEmpty(_defaultLayerId))
        {
            ActiveLayerId = _defaultLayerId;
            ActiveLayerIdChanged.InvokeAsync(_defaultLayerId);
        }
    }

    /// <summary>
    /// Drops name/menu references of layers that left <see cref="Document"/> (deleted elsewhere,
    /// undo, a swapped document) so a later focus hand-back can never resolve a dead element and
    /// the dictionaries do not grow unboundedly.
    /// </summary>
    private void PruneStaleRefs()
    {
        if (Document is null) return;
        var ids = Document.Layers.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _nameRefs.Keys.Where(k => !ids.Contains(k)).ToList()) _nameRefs.Remove(key);
        foreach (var key in _rowMenuRefs.Keys.Where(k => !ids.Contains(k)).ToList()) _rowMenuRefs.Remove(key);
        if (_editingLayerId is not null && !ids.Contains(_editingLayerId)) _editingLayerId = null;
        if (_focusNameAfterRender is not null && !ids.Contains(_focusNameAfterRender)) _focusNameAfterRender = null;
        if (_focusRowMenuTriggerAfterRender is not null && !ids.Contains(_focusRowMenuTriggerAfterRender)) _focusRowMenuTriggerAfterRender = null;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingNameFocus)
        {
            _pendingNameFocus = false;
            await SafeFocusAsync(_nameInputRef);
            return;
        }
        if (_focusNameAfterRender is { } id)
        {
            _focusNameAfterRender = null;
            if (_nameRefs.TryGetValue(id, out var nameRef)) await SafeFocusAsync(nameRef);
        }
        else if (_focusRowMenuTriggerAfterRender is { } menuLayerId)
        {
            _focusRowMenuTriggerAfterRender = null;
            await FocusRowMenuTriggerAsync(menuLayerId);
        }
        else if (_focusAddButtonAfterRender)
        {
            _focusAddButtonAfterRender = false;
            await SafeFocusAsync(_addButtonRef);
        }
    }

    /// <summary>
    /// Per-row slot for the core menu component reference: razor <c>@ref</c> needs a field/property,
    /// so each row binds into its own holder (keyed by layer id).
    /// </summary>
    private sealed class RowMenuHolder
    {
        public TmContextMenu? Menu { get; set; }
    }

    /// <summary>Returns the row's existing holder or creates and remembers one.</summary>
    private RowMenuHolder GetRowMenuHolder(string layerId)
    {
        if (!_rowMenuRefs.TryGetValue(layerId, out var holder))
            _rowMenuRefs[layerId] = holder = new RowMenuHolder();
        return holder;
    }

    /// <summary>Focuses the row's "⋯" trigger through the core menu's safe focus API, with the
    /// name span as fallback (read-only renders no menu).</summary>
    private async Task FocusRowMenuTriggerAsync(string layerId)
    {
        if (_rowMenuRefs.TryGetValue(layerId, out var holder) && holder.Menu is { } menu)
        {
            await menu.FocusTriggerAsync();
        }
        else if (_nameRefs.TryGetValue(layerId, out var nameRef))
        {
            await SafeFocusAsync(nameRef);
        }
    }

    private static async Task SafeFocusAsync(ElementReference element)
    {
        try { await element.FocusAsync(); }
        catch (InvalidOperationException) { }
        catch (Microsoft.JSInterop.JSException) { }
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
        var name = NextNewLayerName();
        var cmd = new AddLayerCommand(Document, name);
        if (CommandStack is not null)
            CommandStack.Push(cmd);
        else
            cmd.Execute();

        await ActiveLayerIdChanged.InvokeAsync(cmd.Layer.Id);
        await DocumentChanged.InvokeAsync(Document);
        _focusNameAfterRender = cmd.Layer.Id;
    }

    /// <summary>
    /// "New layer {n}" with the smallest free ordinal ≥ 1 (case-insensitive). The probe is
    /// BOUNDED — at most existing.Count + 1 ordinals, so a host localizer that ignores the
    /// argument cannot loop forever — and then falls back to the base name plus a unique short
    /// suffix, so two consecutive adds always finish and stay distinct.
    /// </summary>
    private string NextNewLayerName()
    {
        var existing = new HashSet<string>(Document!.Layers.Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
        var baseName = Loc["TmDiagramLayers_NewLayerName", Document.Layers.Count + 1];
        if (!existing.Contains(baseName)) return baseName;
        for (var n = 1; n <= existing.Count; n++)
        {
            var candidate = Loc["TmDiagramLayers_NewLayerName", n];
            if (!existing.Contains(candidate)) return candidate;
        }
        string fallback;
        do { fallback = $"{baseName} ({Guid.NewGuid().ToString("N")[..4]})"; }
        while (existing.Contains(fallback));
        return fallback;
    }

    private async Task OnRemoveLayer(DiagramLayer layer)
    {
        if (Document is null || ReadOnly) return;
        var ordered = SortedLayers.ToList();
        var idx = ordered.FindIndex(l => l.Id == layer.Id);
        var neighbour = idx > 0 ? ordered[idx - 1] : ordered.ElementAtOrDefault(idx + 1);

        var cmd = new RemoveLayerCommand(Document, layer.Id);
        if (CommandStack is not null)
            CommandStack.Push(cmd);
        else
            cmd.Execute();

        if (ActiveLayerId == layer.Id)
            await ActiveLayerIdChanged.InvokeAsync(_defaultLayerId);
        await DocumentChanged.InvokeAsync(Document);
        Announce(Loc["TmDiagramLayers_Announce_Deleted", layer.Name]);
        if (neighbour is not null) _focusRowMenuTriggerAfterRender = neighbour.Id; else _focusAddButtonAfterRender = true;
    }

    /// <summary>The row menu's rename entry: starts the inline edit.</summary>
    private void OnRowMenuRename(DiagramLayer layer) => OnStartEdit(layer);

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

        await DocumentChanged.InvokeAsync(Document);
        Announce(Loc[direction < 0 ? "TmDiagramLayers_Announce_MovedUp" : "TmDiagramLayers_Announce_MovedDown", layer.Name]);
        // Keyboard/touch alternative to DnD: the menu just closed, so hand focus back to the
        // moved row's "⋯" trigger (the row element survives the reorder through @key).
        _focusRowMenuTriggerAfterRender = layer.Id;
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
        Announce(Loc[layer.IsVisible ? "TmDiagramLayers_Announce_Shown" : "TmDiagramLayers_Announce_Hidden", layer.Name]);
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
        Announce(Loc[layer.IsLocked ? "TmDiagramLayers_Announce_Locked" : "TmDiagramLayers_Announce_Unlocked", layer.Name]);
    }

    /// <summary>Sets the polite live-region text; the next render exposes it to screen readers.</summary>
    private void Announce(string message) => _announcement = message;

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
            {
                await CommitRename(layer);
                _focusNameAfterRender = layer.Id;      // keyboard commit → focus back on the name
            }
            else if (e.Key == "Escape")
            {
                _editingLayerId = null;
                _focusNameAfterRender = layer.Id;      // keyboard cancel → focus back on the name
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
        var newName = _editName.Trim();
        if (newName.Length > 0 && !string.Equals(newName, layer.Name, StringComparison.Ordinal))
        {
            var cmd = new RenameLayerCommand(layer, newName);
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
        var targetName = Document.Layers.FirstOrDefault(l => l.Id == layerId)?.Name;
        if (targetName is not null)
            Announce(Loc["TmDiagramLayers_Announce_NodesMoved", targetName]);
    }
}
