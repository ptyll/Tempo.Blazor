using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Wireframe.Commands;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>Right-side layers panel for managing wireframe layers.</summary>
public partial class TmWireframeLayersPanel : ComponentBase
{
    /// <summary>Document whose active page's layers are listed. Mutated through undoable commands.</summary>
    [Parameter, EditorRequired] public WireframeDocument? Document { get; set; }

    /// <summary>Ids of the currently selected elements; enables the "Move to layer" selector when non-empty.</summary>
    [Parameter] public string[] SelectedElementIds { get; set; } = [];

    /// <summary>Cascaded command stack of the hosting editor; when absent, layer changes are applied directly (no undo).</summary>
    [CascadingParameter] public WireframeCommandStack? CommandStack { get; set; }

    /// <summary>Disables every mutating control (add, delete, rename, visibility, lock, reorder, move to layer).</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Additional CSS class on the panel root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Id of the active layer (the one new elements are added to). When <c>null</c>/empty the panel shows the
    /// page's default layer as active and reports it once through <see cref="ActiveLayerIdChanged"/> after the first render.
    /// </summary>
    [Parameter] public string? ActiveLayerId { get; set; }

    /// <summary>Raised with the new active layer id when the user picks, adds or deletes a layer, and once for the default layer after render.</summary>
    [Parameter] public EventCallback<string?> ActiveLayerIdChanged { get; set; }

    /// <summary>Raised with the (mutated) document after every layer change.</summary>
    [Parameter] public EventCallback<WireframeDocument> DocumentChanged { get; set; }

    private bool _collapsed;
    private string? _editingLayerId;
    private string _editName = "";
    private ElementReference _nameInputRef;
    private string? _draggedLayerId;
    private string? _dragOverLayerId;
    private string? _defaultLayerId;
    private string? _moveTargetLayerId;
    private string? _reportedDefaultLayerId;

    /// <summary>The active layer shown in the list: the parent's value, or the page's default layer until the parent confirms it.</summary>
    private string? EffectiveActiveLayerId => string.IsNullOrEmpty(ActiveLayerId) ? _defaultLayerId : ActiveLayerId;

    private IEnumerable<WireframeLayer> SortedLayers =>
        Document?.Layers.OrderBy(l => l.Order).ThenBy(l => l.Name) ?? Enumerable.Empty<WireframeLayer>();

    private List<SelectOption<string>> MoveLayerOptions =>
        SortedLayers.Select(l => new SelectOption<string>(l.Id, l.Name)).ToList();

    protected override void OnParametersSet() => EnsureDefaultLayer();

    /// <summary>
    /// Announces the default active layer once the panel has rendered it. Done here (awaited) instead of
    /// fire-and-forget from <c>OnParametersSet</c>, so the parent never re-renders mid-parameter-pass and an
    /// exception in its handler surfaces on the circuit instead of being swallowed.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (string.IsNullOrEmpty(ActiveLayerId)
            && !string.IsNullOrEmpty(_defaultLayerId)
            && _reportedDefaultLayerId != _defaultLayerId)
        {
            _reportedDefaultLayerId = _defaultLayerId;
            await ActiveLayerIdChanged.InvokeAsync(_defaultLayerId);
        }
    }

    private void EnsureDefaultLayer()
    {
        if (Document is null) return;
        var page = Document.ActivePage;
        if (page is null) return;

        page.EnsureDefaultLayer();
        _defaultLayerId = page.Layers.OrderBy(l => l.Order).First().Id;

        // Ensure all elements without a layer are assigned to the default layer
        foreach (var el in page.Elements.Where(e => string.IsNullOrEmpty(e.LayerId)))
            el.LayerId = _defaultLayerId;
    }

    private void ToggleCollapse() => _collapsed = !_collapsed;

    private async Task OnAddLayer()
    {
        if (Document is null || ReadOnly) return;
        var page = Document.ActivePage;
        if (page is null) return;

        var layer = new WireframeLayer { Name = $"{Loc["TmWireframe_Layer"]} {page.Layers.Count + 1}" };
        var cmd = new AddLayerCommand(Document, layer);
        if (CommandStack is not null)
            CommandStack.Push(cmd);
        else
            cmd.Execute();

        await ActiveLayerIdChanged.InvokeAsync(layer.Id);
        await DocumentChanged.InvokeAsync(Document);
    }

    private async Task OnRemoveLayer(WireframeLayer layer)
    {
        if (Document is null || ReadOnly) return;
        var cmd = new RemoveLayerCommand(Document, layer.Id);
        if (CommandStack is not null)
            CommandStack.Push(cmd);
        else
            cmd.Execute();

        if (EffectiveActiveLayerId == layer.Id)
            await ActiveLayerIdChanged.InvokeAsync(_defaultLayerId);
        await DocumentChanged.InvokeAsync(Document);
    }

    private async Task OnSetActive(string layerId)
    {
        if (ReadOnly) return;
        await ActiveLayerIdChanged.InvokeAsync(layerId);
    }

    private async Task OnToggleVisibility(WireframeLayer layer)
    {
        if (Document is null || ReadOnly) return;
        var cmd = new ToggleLayerVisibilityCommand(Document, layer.Id);
        if (CommandStack is not null)
            CommandStack.Push(cmd);
        else
            cmd.Execute();
        await DocumentChanged.InvokeAsync(Document);
    }

    private async Task OnToggleLock(WireframeLayer layer)
    {
        if (Document is null || ReadOnly) return;
        var cmd = new ToggleLayerLockCommand(Document, layer.Id);
        if (CommandStack is not null)
            CommandStack.Push(cmd);
        else
            cmd.Execute();
        await DocumentChanged.InvokeAsync(Document);
    }

    private void OnStartEdit(WireframeLayer layer)
    {
        if (ReadOnly) return;
        _editingLayerId = layer.Id;
        _editName = layer.Name;
    }

    private async Task OnNameBlur(WireframeLayer layer)
    {
        await CommitRename(layer);
    }

    private async Task OnNameKeyDown(KeyboardEventArgs e, WireframeLayer layer)
    {
        if (e.Key == "Enter")
            await CommitRename(layer);
        else if (e.Key == "Escape")
            _editingLayerId = null;
    }

    private async Task CommitRename(WireframeLayer layer)
    {
        if (_editingLayerId != layer.Id) return;
        if (!string.IsNullOrWhiteSpace(_editName) && _editName != layer.Name)
        {
            var cmd = new RenameLayerCommand(Document!, layer.Id, layer.Name, _editName.Trim());
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

    private async Task OnMoveElementsToLayer(string? layerId)
    {
        if (Document is null || ReadOnly || SelectedElementIds.Length == 0 || string.IsNullOrEmpty(layerId)) return;
        var cmd = new MoveElementsToLayerCommand(Document, SelectedElementIds, layerId);
        if (CommandStack is not null)
            CommandStack.Push(cmd);
        else
            cmd.Execute();
        _moveTargetLayerId = null;
        await DocumentChanged.InvokeAsync(Document);
    }
}
