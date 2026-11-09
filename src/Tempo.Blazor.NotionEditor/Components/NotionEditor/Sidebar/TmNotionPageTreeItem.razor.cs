using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.NotionEditor.Interfaces;

namespace Tempo.Blazor.Components.NotionEditor.Sidebar;

public partial class TmNotionPageTreeItem : TmComponentBase
{
    // ── DI ────────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded tree ─────────────────────────────────────────────────────────

    [CascadingParameter] private TmNotionPageTree Tree { get; set; } = default!;

    // ── Parameters ────────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public INotionPage Page         { get; set; } = default!;
    [Parameter]                 public int         Depth        { get; set; }
    [Parameter]                 public string?     ActivePageId { get; set; }

    // ── Children ──────────────────────────────────────────────────────────────

    private IReadOnlyList<INotionPage> _children         = [];
    private bool                       _isExpanded        = false;
    private bool                       _isLoadingChildren = false;
    private bool                       _childrenLoaded    = false;

    // ── Rename ────────────────────────────────────────────────────────────────

    private bool             _isRenaming    = false;
    private string           _renameValue   = string.Empty;
    private ElementReference _renameInputRef;

    // ── Context menu ──────────────────────────────────────────────────────────

    private enum MenuPhase { None, Main, ConfirmDelete, MoveTo }
    private MenuPhase _menuPhase = MenuPhase.None;
    private double    _ctxX;
    private double    _ctxY;

    // ── Move-to search ────────────────────────────────────────────────────────

    private string                     _moveToQuery   = string.Empty;
    private IReadOnlyList<INotionPage> _moveToResults = [];
    private bool                       _moveToLoading = false;

    // ── Drag ──────────────────────────────────────────────────────────────────

    private bool _isDragOver = false;

    // ── Computed ──────────────────────────────────────────────────────────────

    private bool IsActive =>
        ActivePageId is not null &&
        Page.Id.ToString().Equals(ActivePageId, StringComparison.OrdinalIgnoreCase);

    private string PageTitle =>
        string.IsNullOrWhiteSpace(Page.Title)
            ? Loc["TmNotionPageTree_Untitled"]
            : Page.Title;

    private string PageIcon =>
        string.IsNullOrEmpty(Page.IconEmoji) ? "📄" : Page.IconEmoji;

    private bool MenuOpen => _menuPhase != MenuPhase.None;

    // ── Selection ─────────────────────────────────────────────────────────────

    private async Task OnSelectionChangedAsync(ChangeEventArgs e)
    {
        var selected = e.Value is bool value && value;
        await Tree.SetSelectedAsync(Page.Id.ToString("D"), selected);
    }

    // ── Expand / collapse ─────────────────────────────────────────────────────

    private async Task ToggleExpandAsync()
    {
        if (_isExpanded)
        {
            _isExpanded = false;
            return;
        }

        _isExpanded = true;

        if (!_childrenLoaded)
            await LoadChildrenAsync();
    }

    private async Task LoadChildrenAsync()
    {
        _isLoadingChildren = true;
        StateHasChanged();

        try
        {
            var result     = await Tree.DataProvider.GetChildPagesAsync(Page.Id.ToString());
            _children      = result.ToList();
            Tree.Context?.RememberPages(_children);
            _childrenLoaded = true;
        }
        catch { }
        finally
        {
            _isLoadingChildren = false;
            StateHasChanged();
        }
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private async Task NavigateAsync()
    {
        if (_isRenaming) return;
        await Tree.NavigateToAsync(Page.Id.ToString());
    }

    // ── Rename ────────────────────────────────────────────────────────────────

    private async Task BeginRenameAsync()
    {
        _renameValue = Page.Title;
        _isRenaming  = true;
        CloseMenu();
        StateHasChanged();

        try
        {
            await Task.Delay(30);
            await JS.InvokeVoidAsync("tmNotionEditor.focus", _renameInputRef);
        }
        catch { }
    }

    private async Task CommitRenameAsync()
    {
        if (!_isRenaming) return;
        _isRenaming = false;

        var trimmed = _renameValue.Trim();
        if (!string.IsNullOrEmpty(trimmed) && trimmed != Page.Title)
            await Tree.RenameAsync(Page, trimmed);

        StateHasChanged();
    }

    private async Task OnRenameKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            await CommitRenameAsync();
        else if (e.Key == "Escape")
        {
            _isRenaming = false;
            StateHasChanged();
        }
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private void OpenMenu(MouseEventArgs e)
    {
        _ctxX      = e.ClientX;
        _ctxY      = e.ClientY;
        _menuPhase = MenuPhase.Main;
    }

    private void CloseMenu()
    {
        _menuPhase     = MenuPhase.None;
        _moveToQuery   = string.Empty;
        _moveToResults = [];
    }

    private async Task ContextRenameAsync()
    {
        CloseMenu();
        await BeginRenameAsync();
    }

    private async Task ContextDuplicateAsync()
    {
        CloseMenu();
        await Tree.DuplicateAsync(Page.Id.ToString());
    }

    private void ContextShowDeleteConfirm() => _menuPhase = MenuPhase.ConfirmDelete;

    private async Task ContextConfirmDeleteAsync()
    {
        CloseMenu();
        await Tree.DeleteAsync(Page.Id.ToString());
    }

    private async Task ContextToggleFavoriteAsync()
    {
        CloseMenu();
        await Tree.ToggleFavoriteAsync(Page.Id.ToString(), !Page.IsFavorite);
    }

    private void ContextShowMoveTo()
    {
        _menuPhase     = MenuPhase.MoveTo;
        _moveToQuery   = string.Empty;
        _moveToResults = [];
    }

    private async Task OnMoveToInputAsync(ChangeEventArgs e)
    {
        _moveToQuery = e.Value?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_moveToQuery))
        {
            _moveToResults = [];
            return;
        }

        if (Tree.SearchProvider is null) return;

        _moveToLoading = true;
        StateHasChanged();

        try
        {
            var results    = await Tree.SearchProvider.SearchPagesAsync(_moveToQuery, null);
            _moveToResults = results.Where(p => p.Id != Page.Id).ToList();
        }
        catch { _moveToResults = []; }
        finally
        {
            _moveToLoading = false;
            StateHasChanged();
        }
    }

    private async Task MoveToPageAsync(INotionPage? targetPage)
    {
        CloseMenu();
        await Tree.MoveAsync(Page.Id.ToString(), targetPage?.Id.ToString());
    }

    // ── Add child page ────────────────────────────────────────────────────────

    private async Task AddChildPageAsync()
    {
        if (!_childrenLoaded) await LoadChildrenAsync();
        _isExpanded = true;
        await Tree.AddChildAsync(Page.Id.ToString());
    }

    // ── Drag & drop ───────────────────────────────────────────────────────────

    private void OnDragStart(DragEventArgs e)
    {
        e.DataTransfer.EffectAllowed = "move";
        Tree.BeginDrag(Page);
    }

    private void OnDragEnd(DragEventArgs e)
    {
        Tree.EndDrag();
        _isDragOver = false;
    }

    private void OnDragOver(DragEventArgs e)
    {
        if (Tree.DraggingPage?.Id == Page.Id) return;
        _isDragOver = true;
    }

    private void OnDragLeave(DragEventArgs e) => _isDragOver = false;

    private async Task OnDropAsync(DragEventArgs e)
    {
        _isDragOver = false;
        var dragging = Tree.DraggingPage;
        if (dragging is null || dragging.Id == Page.Id) return;
        Tree.EndDrag();
        await Tree.MoveAsync(dragging.Id.ToString(), Page.Id.ToString());
    }
}
