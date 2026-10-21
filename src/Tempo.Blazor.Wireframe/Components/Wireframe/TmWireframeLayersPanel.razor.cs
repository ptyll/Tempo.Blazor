using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Components.Wireframe.Commands;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// Layers panel of the wireframe editor: the active layer (a <c>radiogroup</c>), visibility and lock toggles
/// (<c>aria-pressed</c>), inline rename, add / delete, a per-row "⋯" menu and a "Move to layer" selector.
/// Every operation has a non-drag alternative (WCAG 2.5.7): the row menu (Rename, Move up, Move down, Delete),
/// <c>F2</c> to rename and <c>Alt+↑</c> / <c>Alt+↓</c> to reorder — all through the same undoable commands the
/// drag-and-drop drop uses.
/// </summary>
public partial class TmWireframeLayersPanel : ComponentBase
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

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
    /// Whether the panel renders its own collapse tab. Defaults to <c>true</c> (standalone use). Hosts that own the
    /// collapse or the surrounding panel (an inspector tab, a sheet) pass <c>false</c>: the panel then renders without
    /// the tab, is never collapsed and fills the width of its host.
    /// </summary>
    [Parameter] public bool ShowCollapseToggle { get; set; } = true;

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
    private ElementReference _addButtonRef;
    private ElementReference _listRef;
    private bool _listJsInit;
    private bool _pendingNameInputFocus;
    private string? _draggedLayerId;
    private string? _dragOverLayerId;
    private string _dropPosition = "before";
    private string? _moveTargetLayerId;
    private string? _reportedDefaultLayerId;
    private string? _announcement;
    private string? _focusNameAfterRender;        // layer id whose name gets focus after the next render
    private string? _focusRowMenuAfterRender;     // layer id whose "⋯" trigger gets focus after the next render
    private string? _focusRadioAfterRender;       // layer id whose radio gets focus after the next render
    private bool _focusAddButtonAfterRender;
    private readonly string _moveSelectId = $"wd-layers-move-{Guid.NewGuid().ToString("n")[..8]}";
    private readonly Dictionary<string, ElementReference> _nameRefs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ElementReference> _radioRefs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RowMenuHolder> _rowMenuRefs = new(StringComparer.Ordinal);

    /// <summary>
    /// The page's default layer: the lowest-ordered one. It cannot be deleted (a deleted layer's elements fall back
    /// to it); reordering can hand the role to another layer, which is why it is read on every render.
    /// </summary>
    private string? DefaultLayerId =>
        Document?.ActivePage?.Layers.OrderBy(l => l.Order).FirstOrDefault()?.Id;

    /// <summary>The active layer shown in the list: the parent's value, or the page's default layer until the parent confirms it.</summary>
    private string? EffectiveActiveLayerId => string.IsNullOrEmpty(ActiveLayerId) ? DefaultLayerId : ActiveLayerId;

    private IEnumerable<WireframeLayer> SortedLayers =>
        Document?.Layers.OrderBy(l => l.Order).ThenBy(l => l.Name) ?? Enumerable.Empty<WireframeLayer>();

    private List<SelectOption<string>> MoveLayerOptions =>
        SortedLayers.Select(l => new SelectOption<string>(l.Id, l.Name)).ToList();

    private bool CanDelete(WireframeLayer layer) =>
        Document is { Layers.Count: > 1 } && layer.Id != DefaultLayerId;

    /// <summary>The roving tab stop of the radiogroup: the active layer's radio, else the first row's.</summary>
    private string? RadioTabStopId(List<WireframeLayer> ordered)
    {
        var active = EffectiveActiveLayerId;
        return ordered.Any(l => l.Id == active) ? active : ordered.FirstOrDefault()?.Id;
    }

    /// <summary>Per-row slot for the core menu reference: razor <c>@ref</c> needs a field, so each row binds into its own holder.</summary>
    private sealed class RowMenuHolder
    {
        public TmContextMenu? Menu { get; set; }
    }

    /// <summary>Returns the row's existing menu holder or creates and remembers one.</summary>
    private RowMenuHolder GetRowMenuHolder(string layerId)
    {
        if (!_rowMenuRefs.TryGetValue(layerId, out var holder))
            _rowMenuRefs[layerId] = holder = new RowMenuHolder();
        return holder;
    }

    protected override void OnParametersSet()
    {
        EnsureDefaultLayer();
        PruneStaleRefs();
    }

    /// <summary>
    /// Announces the default active layer once the panel has rendered it, then hands focus back to a control a
    /// command replaced or moved. Done here (awaited) instead of fire-and-forget from <c>OnParametersSet</c>, so the
    /// parent never re-renders mid-parameter-pass and an exception in its handler surfaces on the circuit.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_listJsInit)
        {
            _listJsInit = true;
            // Scoped scroll guard for plain arrows on the radios and Space on the name (wireframe-designer.js).
            try { await JS.InvokeVoidAsync("tmWireframeDesigner.initLayersList", _listRef); }
            catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException) { }
        }

        var defaultLayerId = DefaultLayerId;
        if (string.IsNullOrEmpty(ActiveLayerId)
            && !string.IsNullOrEmpty(defaultLayerId)
            && _reportedDefaultLayerId != defaultLayerId)
        {
            _reportedDefaultLayerId = defaultLayerId;
            await ActiveLayerIdChanged.InvokeAsync(defaultLayerId);
        }

        if (_pendingNameInputFocus)
        {
            _pendingNameInputFocus = false;
            await SafeFocusAsync(_nameInputRef);
        }
        else if (_focusNameAfterRender is { } nameId)
        {
            _focusNameAfterRender = null;
            if (_nameRefs.TryGetValue(nameId, out var nameRef)) await SafeFocusAsync(nameRef);
        }
        else if (_focusRadioAfterRender is { } radioId)
        {
            _focusRadioAfterRender = null;
            if (_radioRefs.TryGetValue(radioId, out var radioRef)) await SafeFocusAsync(radioRef);
        }
        else if (_focusRowMenuAfterRender is { } menuId)
        {
            _focusRowMenuAfterRender = null;
            await FocusRowMenuTriggerAsync(menuId);
        }
        else if (_focusAddButtonAfterRender)
        {
            _focusAddButtonAfterRender = false;
            await SafeFocusAsync(_addButtonRef);
        }
    }

    private void EnsureDefaultLayer()
    {
        if (Document is null) return;
        var page = Document.ActivePage;
        if (page is null) return;

        page.EnsureDefaultLayer();
        var defaultLayerId = DefaultLayerId;

        // Ensure all elements without a layer are assigned to the default layer
        foreach (var el in page.Elements.Where(e => string.IsNullOrEmpty(e.LayerId)))
            el.LayerId = defaultLayerId;
    }

    /// <summary>Drops references of layers that left the page (deleted elsewhere, undo, a swapped document).</summary>
    private void PruneStaleRefs()
    {
        var ids = Document?.ActivePage?.Layers.Select(l => l.Id).ToHashSet(StringComparer.Ordinal)
                  ?? new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in _nameRefs.Keys.Where(k => !ids.Contains(k)).ToList()) _nameRefs.Remove(key);
        foreach (var key in _radioRefs.Keys.Where(k => !ids.Contains(k)).ToList()) _radioRefs.Remove(key);
        foreach (var key in _rowMenuRefs.Keys.Where(k => !ids.Contains(k)).ToList()) _rowMenuRefs.Remove(key);
        if (_editingLayerId is not null && !ids.Contains(_editingLayerId)) _editingLayerId = null;
        if (_focusNameAfterRender is not null && !ids.Contains(_focusNameAfterRender)) _focusNameAfterRender = null;
        if (_focusRadioAfterRender is not null && !ids.Contains(_focusRadioAfterRender)) _focusRadioAfterRender = null;
        if (_focusRowMenuAfterRender is not null && !ids.Contains(_focusRowMenuAfterRender)) _focusRowMenuAfterRender = null;
    }

    private static async Task SafeFocusAsync(ElementReference element)
    {
        try { await element.FocusAsync(); }
        catch (InvalidOperationException) { }
        catch (JSException) { }
    }

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

    private void Run(IWireframeCommand command)
    {
        if (CommandStack is not null)
            CommandStack.Push(command);
        else
            command.Execute();
    }

    private void Announce(string message) => _announcement = message;

    private void ToggleCollapse() => _collapsed = !_collapsed;

    // ── Add / delete ─────────────────────────────────────────────────────────

    private async Task OnAddLayer()
    {
        if (Document is null || ReadOnly) return;
        var page = Document.ActivePage;
        if (page is null) return;

        var layer = new WireframeLayer { Name = $"{Loc["TmWireframe_Layer"]} {page.Layers.Count + 1}" };
        Run(new AddLayerCommand(Document, layer));

        await ActiveLayerIdChanged.InvokeAsync(layer.Id);
        await DocumentChanged.InvokeAsync(Document);
    }

    private async Task OnRemoveLayer(WireframeLayer layer)
    {
        if (Document is null || ReadOnly || !CanDelete(layer)) return;

        var ordered = SortedLayers.ToList();
        var idx = ordered.FindIndex(l => l.Id == layer.Id);
        var neighbour = idx > 0 ? ordered[idx - 1] : ordered.ElementAtOrDefault(idx + 1);
        var wasActive = EffectiveActiveLayerId == layer.Id;

        Run(new RemoveLayerCommand(Document, layer.Id));

        if (wasActive)
            await ActiveLayerIdChanged.InvokeAsync(DefaultLayerId);
        await DocumentChanged.InvokeAsync(Document);
        Announce(Loc["TmWireframeLayers_Announce_Deleted", layer.Name]);

        // The row (and the control that activated it) is gone: focus the neighbour's menu, or the add button.
        if (neighbour is not null) _focusRowMenuAfterRender = neighbour.Id; else _focusAddButtonAfterRender = true;
    }

    // ── Active layer, visibility, lock ───────────────────────────────────────

    private async Task OnSetActive(string layerId)
    {
        if (ReadOnly) return;
        await ActiveLayerIdChanged.InvokeAsync(layerId);
    }

    /// <summary>Radio keys: ↑/← and ↓/→ move to the previous / next layer and activate it (wrapping), like a native radio group.</summary>
    private async Task OnRadioKeyDown(KeyboardEventArgs e, int index)
    {
        if (ReadOnly || e.AltKey || e.CtrlKey || e.MetaKey || e.ShiftKey) return;
        var step = e.Key switch
        {
            "ArrowDown" or "ArrowRight" => 1,
            "ArrowUp" or "ArrowLeft" => -1,
            _ => 0,
        };
        if (step == 0) return;

        var ordered = SortedLayers.ToList();
        if (ordered.Count < 2) return;
        var target = ordered[(index + step + ordered.Count) % ordered.Count];
        _focusRadioAfterRender = target.Id;
        await OnSetActive(target.Id);
    }

    private async Task OnToggleVisibility(WireframeLayer layer)
    {
        if (Document is null || ReadOnly) return;
        Run(new ToggleLayerVisibilityCommand(Document, layer.Id));
        await DocumentChanged.InvokeAsync(Document);
    }

    private async Task OnToggleLock(WireframeLayer layer)
    {
        if (Document is null || ReadOnly) return;
        Run(new ToggleLayerLockCommand(Document, layer.Id));
        await DocumentChanged.InvokeAsync(Document);
    }

    // ── Rename ───────────────────────────────────────────────────────────────

    private void OnStartEdit(WireframeLayer layer)
    {
        if (ReadOnly) return;
        _editingLayerId = layer.Id;
        _editName = layer.Name;
        _pendingNameInputFocus = true;
    }

    private async Task OnNameBlur(WireframeLayer layer) => await CommitRename(layer);

    /// <summary>
    /// Key handling on the (collapsed) name, a non-native focusable: Enter activates on keydown (guarded against
    /// auto-repeat), Space on keyup (<see cref="OnNameKeyUp"/>) — exactly one activation per press
    /// (docs/keyboard-activation-convention.md).
    /// </summary>
    private void OnNameKeyDown(KeyboardEventArgs e, WireframeLayer layer)
    {
        if (e.Key == "Enter" && !e.Repeat) OnStartEdit(layer);
    }

    private void OnNameKeyUp(KeyboardEventArgs e, WireframeLayer layer)
    {
        if (e.Key == " ") OnStartEdit(layer);
    }

    /// <summary>Keys inside the rename input: Enter commits, Escape discards; focus returns to the name.</summary>
    private async Task OnInputKeyDown(KeyboardEventArgs e, WireframeLayer layer)
    {
        if (e.Key == "Enter" && !e.Repeat)
        {
            await CommitRename(layer);
            _focusNameAfterRender = layer.Id;
        }
        else if (e.Key == "Escape")
        {
            _editingLayerId = null;
            _focusNameAfterRender = layer.Id;
        }
    }

    private async Task CommitRename(WireframeLayer layer)
    {
        if (_editingLayerId != layer.Id) return;
        var newName = _editName.Trim();
        if (newName.Length > 0 && !string.Equals(newName, layer.Name, StringComparison.Ordinal))
        {
            Run(new RenameLayerCommand(Document!, layer.Id, layer.Name, newName));
            await DocumentChanged.InvokeAsync(Document);
        }
        _editingLayerId = null;
    }

    // ── Keyboard shortcuts on the row ────────────────────────────────────────

    /// <summary>
    /// Row-level shortcuts, so they work from any control of the row: <c>F2</c> renames, <c>Alt+↑</c> / <c>Alt+↓</c>
    /// reorders through <see cref="ReorderLayersCommand"/> (the non-drag alternative to drag-and-drop). Inert while
    /// the row is being renamed (arrows and F2 then belong to the text field) and while read-only.
    /// </summary>
    private async Task OnRowKeyDown(KeyboardEventArgs e, WireframeLayer layer)
    {
        if (ReadOnly || _editingLayerId == layer.Id) return;

        if (e.Key == "F2" && !e.AltKey && !e.CtrlKey && !e.ShiftKey && !e.MetaKey)
        {
            OnStartEdit(layer);
        }
        else if (e.AltKey && !e.CtrlKey && !e.ShiftKey && !e.MetaKey && e.Key is "ArrowUp" or "ArrowDown")
        {
            await OnMoveLayer(layer, e.Key == "ArrowUp" ? -1 : +1, fromKeyboard: true);
        }
    }

    // ── Reorder ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves a layer one position through the SAME undoable <see cref="ReorderLayersCommand"/> the drag-and-drop drop
    /// pushes. <paramref name="direction"/> is -1 for "up", +1 for "down". The edges are a no-op (no empty undo step).
    /// </summary>
    private async Task OnMoveLayer(WireframeLayer layer, int direction, bool fromKeyboard)
    {
        if (Document is null || ReadOnly) return;
        var ordered = SortedLayers.ToList();
        var index = ordered.FindIndex(l => l.Id == layer.Id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= ordered.Count) return;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        Run(new ReorderLayersCommand(Document, ToOrders(ordered)));

        await DocumentChanged.InvokeAsync(Document);
        Announce(Loc[direction < 0 ? "TmWireframeLayers_Announce_MovedUp" : "TmWireframeLayers_Announce_MovedDown", layer.Name, target + 1, ordered.Count]);

        // A moved row can lose focus in the DOM: hand it back (the row survives the reorder through @key).
        if (fromKeyboard) _focusNameAfterRender = layer.Id; else _focusRowMenuAfterRender = layer.Id;
    }

    private static Dictionary<string, int> ToOrders(List<WireframeLayer> reordered) =>
        reordered.Select((l, i) => (l.Id, Order: i)).ToDictionary(x => x.Id, x => x.Order);

    // ── Drag and drop (mouse; every drag has the non-drag alternatives above) ─

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
        if (_draggedLayerId is null || _draggedLayerId == layerId) return;

        var ordered = SortedLayers.ToList();
        var draggedIndex = ordered.FindIndex(l => l.Id == _draggedLayerId);
        var targetIndex = ordered.FindIndex(l => l.Id == layerId);
        // The drop puts the dragged row at the target's index: below it when coming from above, above it otherwise.
        _dropPosition = draggedIndex >= 0 && draggedIndex < targetIndex ? "after" : "before";
        _dragOverLayerId = layerId;
    }

    private void OnDragLeave(DragEventArgs e, string layerId)
    {
        if (_dragOverLayerId == layerId) _dragOverLayerId = null;
    }

    private void OnDragEnd(DragEventArgs e) => ClearDrag();

    private void ClearDrag()
    {
        _draggedLayerId = null;
        _dragOverLayerId = null;
    }

    private async Task OnDrop(DragEventArgs e, string targetLayerId)
    {
        var draggedId = _draggedLayerId;
        ClearDrag();
        if (Document is null || ReadOnly || draggedId is null || draggedId == targetLayerId) return;

        var layers = SortedLayers.ToList();
        var draggedIndex = layers.FindIndex(l => l.Id == draggedId);
        var targetIndex = layers.FindIndex(l => l.Id == targetLayerId);
        if (draggedIndex < 0 || targetIndex < 0) return;

        var draggedLayer = layers[draggedIndex];
        layers.RemoveAt(draggedIndex);
        layers.Insert(targetIndex, draggedLayer);

        Run(new ReorderLayersCommand(Document, ToOrders(layers)));
        await DocumentChanged.InvokeAsync(Document);
        Announce(Loc[targetIndex < draggedIndex ? "TmWireframeLayers_Announce_MovedUp" : "TmWireframeLayers_Announce_MovedDown", draggedLayer.Name, targetIndex + 1, layers.Count]);
    }

    // ── Move selection to a layer ────────────────────────────────────────────

    private async Task OnMoveElementsToLayer(string? layerId)
    {
        if (Document is null || ReadOnly || SelectedElementIds.Length == 0 || string.IsNullOrEmpty(layerId)) return;
        Run(new MoveElementsToLayerCommand(Document, SelectedElementIds, layerId));
        _moveTargetLayerId = null;
        await DocumentChanged.InvokeAsync(Document);
    }
}
