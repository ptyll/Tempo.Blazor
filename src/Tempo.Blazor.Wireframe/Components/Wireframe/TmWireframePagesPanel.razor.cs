using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// Vertical numbered list of a wireframe document's pages (the editor's left-panel "Pages" tab; also usable
/// standalone). Click switches the active page; each row has a "⋯" menu (Rename, Duplicate, Move up, Move down,
/// Delete) and a "+ Add page" button sits below the list. Every operation has a non-drag alternative (WCAG 2.5.7):
/// the row menu, <c>F2</c> to rename and <c>Alt+↑</c> / <c>Alt+↓</c> to reorder; HTML5 drag-and-drop is a
/// pointer-only extra. The panel MUTATES <see cref="Document"/> (page operations are not undoable, like the page
/// tabs) and reports through <see cref="ActivePageChanged"/> / <see cref="DocumentChanged"/>. The <c>Delete</c> key
/// never deletes a page.
/// </summary>
public partial class TmWireframePagesPanel : ComponentBase
{
    // ── Parameters ────────────────────────────────────────────────────────────

    /// <summary>Document whose pages are listed. Mutated in place: pages are added, renamed, reordered, duplicated and deleted, and <see cref="WireframeDocument.ActivePageId"/> is set when the user switches page.</summary>
    [Parameter] public WireframeDocument? Document { get; set; }

    /// <summary>Raised with the (mutated) document after every change: switch, add, rename, duplicate, delete, reorder.</summary>
    [Parameter] public EventCallback<WireframeDocument> DocumentChanged { get; set; }

    /// <summary>Raised with the id of the page that became active after a switch, add, duplicate or delete (not for plain reorder or rename), before <see cref="DocumentChanged"/>.</summary>
    [Parameter] public EventCallback<string> ActivePageChanged { get; set; }

    /// <summary>
    /// When <c>true</c> the list is view-only: no add button, no row menu, no drag-and-drop and no rename or reorder
    /// shortcuts. Selecting a page still works (it is navigation, not an edit).
    /// </summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Additional CSS class on the panel root.</summary>
    [Parameter] public string? Class { get; set; }

    // ── State ─────────────────────────────────────────────────────────────────

    private string? _editingId;
    private string _editName = "";
    private ElementReference _nameInputRef;
    private bool _pendingInputFocus;
    private string? _draggedId;
    private string? _dragOverId;
    private string _dropPosition = "before";
    private string? _announcement;
    private string? _focusSelectAfterRender;      // page id whose select button gets focus after the next render
    private string? _focusRowMenuAfterRender;     // page id whose "⋯" trigger gets focus after the next render
    private readonly Dictionary<string, ElementReference> _selectRefs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RowMenuHolder> _rowMenuRefs = new(StringComparer.Ordinal);

    /// <summary>Per-row slot for the core menu reference: razor <c>@ref</c> needs a field, so each row binds into its own holder.</summary>
    private sealed class RowMenuHolder
    {
        public TmContextMenu? Menu { get; set; }
    }

    private RowMenuHolder GetRowMenuHolder(string pageId)
    {
        if (!_rowMenuRefs.TryGetValue(pageId, out var holder))
            _rowMenuRefs[pageId] = holder = new RowMenuHolder();
        return holder;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        // Drop references of pages that left the document (deleted elsewhere, a swapped document).
        var ids = Document?.Pages.Select(p => p.Id).ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in _selectRefs.Keys.Where(k => !ids.Contains(k)).ToList()) _selectRefs.Remove(key);
        foreach (var key in _rowMenuRefs.Keys.Where(k => !ids.Contains(k)).ToList()) _rowMenuRefs.Remove(key);
        if (_editingId is not null && !ids.Contains(_editingId)) _editingId = null;
        if (_draggedId is not null && !ids.Contains(_draggedId)) ClearDrag();
    }

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingInputFocus)
        {
            _pendingInputFocus = false;
            await SafeFocusAsync(_nameInputRef);
        }
        else if (_focusSelectAfterRender is { } selectId)
        {
            _focusSelectAfterRender = null;
            if (_selectRefs.TryGetValue(selectId, out var selectRef)) await SafeFocusAsync(selectRef);
        }
        else if (_focusRowMenuAfterRender is { } menuId)
        {
            _focusRowMenuAfterRender = null;
            if (_rowMenuRefs.TryGetValue(menuId, out var holder) && holder.Menu is { } menu)
                await menu.FocusTriggerAsync();
            else if (_selectRefs.TryGetValue(menuId, out var fallback))
                await SafeFocusAsync(fallback);
        }
    }

    private static async Task SafeFocusAsync(ElementReference element)
    {
        try { await element.FocusAsync(); }
        catch (InvalidOperationException) { }
        catch (JSException) { }
    }

    private void Announce(string message) => _announcement = message;

    private async Task NotifyChangedAsync()
    {
        if (Document is not null) await DocumentChanged.InvokeAsync(Document);
    }

    // ── Switch / add / duplicate / delete ─────────────────────────────────────

    private async Task SwitchPageAsync(string pageId)
    {
        if (Document is null || Document.ActivePage?.Id == pageId) return;
        if (Document.Pages.All(p => p.Id != pageId)) return;

        Document.ActivePageId = pageId;
        await ActivePageChanged.InvokeAsync(pageId);
        await NotifyChangedAsync();
    }

    private async Task AddPageAsync()
    {
        if (Document is null || ReadOnly) return;

        var active = Document.ActivePage;
        var page = WireframePageOperations.Add(
            Document,
            $"{Loc["TmWireframe_Page"]} {Document.Pages.Count + 1}",
            active?.Width ?? 1280,
            active?.Height ?? 800);

        Document.ActivePageId = page.Id;
        Announce(Loc["TmWireframePages_Announce_Added", page.Name]);
        await ActivePageChanged.InvokeAsync(page.Id);
        await NotifyChangedAsync();
    }

    private async Task DuplicateAsync(WireframePage page)
    {
        if (Document is null || ReadOnly) return;

        var copy = WireframePageOperations.Duplicate(Document, page.Id, Loc["TmWireframe_Copy"]);
        if (copy is null) return;

        Document.ActivePageId = copy.Id;
        Announce(Loc["TmWireframePages_Announce_Duplicated", copy.Name]);
        await ActivePageChanged.InvokeAsync(copy.Id);
        await NotifyChangedAsync();
    }

    private async Task DeleteAsync(WireframePage page)
    {
        if (Document is null || ReadOnly) return;

        var before = Document.ActivePage?.Id;
        var neighbourIndex = Math.Max(0, Document.Pages.FindIndex(p => p.Id == page.Id) - 1);
        var after = WireframePageOperations.Delete(Document, page.Id);
        if (after is null) return;   // refused: the last page can never be deleted

        Announce(Loc["TmWireframePages_Announce_Deleted", page.Name]);
        if (!string.Equals(before, after, StringComparison.Ordinal))
            await ActivePageChanged.InvokeAsync(after);
        await NotifyChangedAsync();

        // The row (and the control that activated it) is gone: hand focus to the neighbour's menu trigger.
        var neighbour = Document.Pages.ElementAtOrDefault(Math.Min(neighbourIndex, Document.Pages.Count - 1));
        if (neighbour is not null) _focusRowMenuAfterRender = neighbour.Id;
    }

    // ── Rename ────────────────────────────────────────────────────────────────

    private void StartRename(WireframePage page)
    {
        if (ReadOnly) return;
        _editingId = page.Id;
        _editName = page.Name;
        _pendingInputFocus = true;
    }

    private async Task OnInputKeyDown(KeyboardEventArgs e, WireframePage page)
    {
        if (e.Key == "Enter" && !e.Repeat)
        {
            await CommitRenameAsync(page);
            _focusSelectAfterRender = page.Id;
        }
        else if (e.Key == "Escape")
        {
            _editingId = null;
            _focusSelectAfterRender = page.Id;
        }
    }

    private async Task CommitRenameAsync(WireframePage page)
    {
        if (_editingId != page.Id) return;
        _editingId = null;
        if (Document is not null && WireframePageOperations.Rename(Document, page.Id, _editName))
            await NotifyChangedAsync();
    }

    // ── Keyboard shortcuts on the row ─────────────────────────────────────────

    /// <summary>
    /// Row-level shortcuts, so they work from any control of the row: <c>F2</c> renames, <c>Alt+↑</c> / <c>Alt+↓</c>
    /// reorders. Inert while the row is being renamed (arrows and F2 then belong to the text field) and while
    /// read-only. <c>Delete</c> is deliberately not handled: deleting a page is only offered through the menu.
    /// </summary>
    private async Task OnRowKeyDown(KeyboardEventArgs e, WireframePage page)
    {
        if (ReadOnly || _editingId == page.Id) return;

        if (e.Key == "F2" && !e.AltKey && !e.CtrlKey && !e.ShiftKey && !e.MetaKey)
            StartRename(page);
        else if (e.AltKey && !e.CtrlKey && !e.ShiftKey && !e.MetaKey && e.Key is "ArrowUp" or "ArrowDown")
            await MoveAsync(page, e.Key == "ArrowUp" ? -1 : +1, fromKeyboard: true);
    }

    // ── Reorder ───────────────────────────────────────────────────────────────

    /// <summary>Moves a page one position (<paramref name="direction"/> -1 = up, +1 = down); the edges are a no-op.</summary>
    private async Task MoveAsync(WireframePage page, int direction, bool fromKeyboard)
    {
        if (Document is null || ReadOnly) return;
        var newIndex = WireframePageOperations.Move(Document, page.Id, direction);
        if (newIndex < 0) return;

        Announce(Loc[direction < 0 ? "TmWireframePages_Announce_MovedUp" : "TmWireframePages_Announce_MovedDown",
            page.Name, newIndex + 1, Document.Pages.Count]);
        await NotifyChangedAsync();

        // A moved row can lose focus in the DOM: hand it back (the row survives the reorder through @key).
        if (fromKeyboard) _focusSelectAfterRender = page.Id; else _focusRowMenuAfterRender = page.Id;
    }

    // ── Drag and drop (pointer only; the menu and Alt+Arrow are the non-drag alternatives) ─

    private void OnDragStart(string pageId) => _draggedId = ReadOnly ? null : pageId;

    private void OnDragEnter(string pageId)
    {
        if (Document is null || _draggedId is null || _draggedId == pageId) return;
        var draggedIndex = Document.Pages.FindIndex(p => p.Id == _draggedId);
        var targetIndex = Document.Pages.FindIndex(p => p.Id == pageId);
        // The drop puts the dragged row at the target's index: below it when coming from above, above it otherwise.
        _dropPosition = draggedIndex >= 0 && draggedIndex < targetIndex ? "after" : "before";
        _dragOverId = pageId;
    }

    private void OnDragLeave(string pageId)
    {
        if (_dragOverId == pageId) _dragOverId = null;
    }

    private void ClearDrag()
    {
        _draggedId = null;
        _dragOverId = null;
    }

    private async Task OnDrop(string targetPageId)
    {
        var draggedId = _draggedId;
        ClearDrag();
        if (Document is null || ReadOnly || draggedId is null || draggedId == targetPageId) return;

        var draggedIndex = Document.Pages.FindIndex(p => p.Id == draggedId);
        var targetIndex = Document.Pages.FindIndex(p => p.Id == targetPageId);
        if (WireframePageOperations.MoveTo(Document, draggedId, targetIndex) < 0) return;

        var moved = Document.Pages[targetIndex];
        Announce(Loc[targetIndex < draggedIndex ? "TmWireframePages_Announce_MovedUp" : "TmWireframePages_Announce_MovedDown",
            moved.Name, targetIndex + 1, Document.Pages.Count]);
        await NotifyChangedAsync();
    }
}
