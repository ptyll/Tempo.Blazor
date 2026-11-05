using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Interfaces;

namespace Tempo.Blazor.Components.NotionEditor.Sidebar;

public partial class TmNotionPageTree : TmComponentBase
{
    internal enum BulkAction
    {
        Move,
        Copy
    }

    // ── Cascaded context ──────────────────────────────────────────────────────

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ────────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public IReadOnlyList<INotionPage> RootPages    { get; set; } = [];
    [Parameter]                 public string?                    ActivePageId { get; set; }
    [Parameter]                 public EventCallback              OnTreeChanged { get; set; }

    // ── Bulk state ────────────────────────────────────────────────────────────

    private readonly HashSet<string> _selectedPageIds = new(StringComparer.OrdinalIgnoreCase);
    private BulkAction               _bulkAction       = BulkAction.Move;
    private bool                     _bulkPickerOpen;
    private bool                     _bulkDeleteConfirmOpen;
    private string                   _bulkTargetQuery = string.Empty;
    private List<TargetNode>         _targetRoots     = [];
    private HashSet<string>          _excludedTargetIds = new(StringComparer.OrdinalIgnoreCase);
    private bool                     _bulkTargetLoading;
    private string                   _bulkError = string.Empty;

    /// <summary>Pending two-step target: the root option (flag) or a concrete page.</summary>
    private bool          _pendingTargetIsRoot;
    private INotionPage?  _pendingTargetPage;

    internal int SelectedCount => _selectedPageIds.Count;

    internal bool IsSelected(string pageId) => _selectedPageIds.Contains(pageId);

    internal Task SetSelectedAsync(string pageId, bool selected)
    {
        if (selected)
            _selectedPageIds.Add(pageId);
        else
            _selectedPageIds.Remove(pageId);

        if (_selectedPageIds.Count == 0)
            CloseBulkPanels();

        return InvokeAsync(StateHasChanged);
    }

    // ── Drag state (accessed by TmNotionPageTreeItem) ─────────────────────────

    internal INotionPage? DraggingPage { get; private set; }

    internal void BeginDrag(INotionPage page) => DraggingPage = page;
    internal void EndDrag()                   => DraggingPage = null;

    // ── Provider access (accessed by TmNotionPageTreeItem) ────────────────────

    internal INotionDataProvider    DataProvider   => Context.DataProvider;
    internal INotionSearchProvider? SearchProvider => Context.SearchProvider;

    // ── Mutations ─────────────────────────────────────────────────────────────

    internal async Task NavigateToAsync(string pageId)
    {
        if (Context.NavigateTo is not null)
            await Context.NavigateTo(pageId);
    }

    internal async Task RenameAsync(INotionPage page, string newTitle)
    {
        var updated = new NotionPage
        {
            Id                  = page.Id,
            ParentId            = page.ParentId,
            Title               = newTitle,
            Description         = page.Description,
            IconEmoji           = page.IconEmoji,
            IconImageUrl        = page.IconImageUrl,
            CoverImageUrl       = page.CoverImageUrl,
            CoverImagePositionY = page.CoverImagePositionY,
            IsFullWidth         = page.IsFullWidth,
            IsSmallText         = page.IsSmallText,
            IsLocked            = page.IsLocked,
            CreatedAt           = page.CreatedAt,
            CreatedByUserId     = page.CreatedByUserId,
            LastEditedAt        = page.LastEditedAt,
            LastEditedByUserId  = page.LastEditedByUserId,
            IsDeleted           = page.IsDeleted,
            DeletedAt           = page.DeletedAt,
            IsFavorite          = page.IsFavorite,
        };

        await Context.DataProvider.UpdatePageAsync(updated);
        await OnTreeChanged.InvokeAsync();
    }

    internal async Task DuplicateAsync(string pageId)
    {
        await Context.DataProvider.DuplicatePageAsync(pageId);
        await OnTreeChanged.InvokeAsync();
    }

    internal async Task DeleteAsync(string pageId)
    {
        await Context.DataProvider.DeletePageAsync(pageId);
        await OnTreeChanged.InvokeAsync();
    }

    internal async Task ToggleFavoriteAsync(string pageId, bool makeFavorite)
    {
        await Context.DataProvider.ToggleFavoriteAsync(pageId, makeFavorite);
        await OnTreeChanged.InvokeAsync();
    }

    internal async Task MoveAsync(string pageId, string? newParentId)
    {
        await Context.DataProvider.MovePageAsync(pageId, newParentId);
        await OnTreeChanged.InvokeAsync();
    }

    internal async Task AddChildAsync(string parentId)
    {
        var page = await Context.DataProvider.CreatePageAsync(parentId, string.Empty);
        await OnTreeChanged.InvokeAsync();

        if (Context.NavigateTo is not null)
            await Context.NavigateTo(page.Id.ToString());
    }

    private string BulkPickerTitle => _bulkAction == BulkAction.Move
        ? string.Format(Loc["TmNotionPageTree_BulkMoveTitle"], SelectedCount)
        : string.Format(Loc["TmNotionPageTree_BulkCopyTitle"], SelectedCount);

    private async Task OpenBulkTargetPicker(BulkAction action)
    {
        _bulkAction            = action;
        _bulkPickerOpen        = true;
        _bulkDeleteConfirmOpen = false;
        _bulkTargetQuery       = string.Empty;
        _pendingTargetIsRoot   = false;
        _pendingTargetPage     = null;
        _bulkError             = string.Empty;

        // ComputeExcludedTargetsAsync walks the provider — the spinner covers that latency.
        _bulkTargetLoading = true;
        await InvokeAsync(StateHasChanged);
        _excludedTargetIds = await ComputeExcludedTargetsAsync();
        _targetRoots = BuildRootTargets();
        RebuildVisibleRows();
        _bulkTargetLoading = false;
        await InvokeAsync(StateHasChanged);
    }

    private void OpenBulkDeleteConfirm()
    {
        _bulkDeleteConfirmOpen = true;
        _bulkPickerOpen        = false;
        _bulkError             = string.Empty;
    }

    private async Task HandleBulkDeleteResult(bool? result)
    {
        if (result == true)
            await ExecuteBulkDeleteAsync();
        else
            CloseBulkPanels();
    }

    /// <summary>Selected pages plus their whole descendant subtrees — never offered as a move/copy
    /// target (a cycle).</summary>
    private async Task<HashSet<string>> ComputeExcludedTargetsAsync()
    {
        var excluded = new HashSet<string>(_selectedPageIds, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(_selectedPageIds);
        while (queue.Count > 0)
        {
            var parentId = queue.Dequeue();
            try
            {
                var children = await DataProvider.GetChildPagesAsync(parentId);
                foreach (var child in children)
                {
                    var id = child.Id.ToString("D");
                    if (excluded.Add(id))
                        queue.Enqueue(id);
                }
            }
            catch
            {
                // A provider that cannot list children still excludes the selection itself.
            }
        }

        return excluded;
    }

    private bool IsExcluded(INotionPage page) => _excludedTargetIds.Contains(page.Id.ToString("D"));

    private List<TargetNode> BuildRootTargets()
        => RootPages
            .Where(page => !IsExcluded(page))
            .Select(page => new TargetNode { Page = page, Depth = 0 })
            .ToList();

    private async Task ToggleNodeAsync(TargetNode node)
    {
        if (node.Expanded)
        {
            node.Expanded = false;
            RebuildVisibleRows();
            await InvokeAsync(StateHasChanged);
            return;
        }

        if (!node.ChildrenLoaded)
        {
            node.ChildrenLoaded = true;
            try
            {
                var children = await DataProvider.GetChildPagesAsync(node.Page.Id.ToString("D"));
                node.Children = children
                    .Where(child => !IsExcluded(child))
                    .Select(child => new TargetNode { Page = child, Depth = node.Depth + 1 })
                    .ToList();
            }
            catch
            {
                node.Children = [];
            }
        }

        node.Expanded = node.Children.Count > 0;
        RebuildVisibleRows();
        await InvokeAsync(StateHasChanged);
    }

    private void SelectBulkTarget(INotionPage? page)
    {
        _pendingTargetPage   = page;
        _pendingTargetIsRoot = page is null;
    }

    private bool IsPending(INotionPage page)
        => !_pendingTargetIsRoot
        && string.Equals(_pendingTargetPage?.Id.ToString("D"), page.Id.ToString("D"), StringComparison.OrdinalIgnoreCase);

    private async Task ConfirmBulkTargetAsync()
    {
        if (!_pendingTargetIsRoot && _pendingTargetPage is null)
            return;
        await ExecuteBulkTargetAsync(_pendingTargetPage);
    }

    // ── Bulk target picker keyboard (APG flat listbox with aria-level) ────────

    /// <summary>A visible picker row: the page plus its (possibly expanded) node. The list renders
    /// FLAT from this list so the roving tabindex can index the option refs positionally.</summary>
    private sealed record TargetRow(INotionPage Page, TargetNode Node)
    {
        public int Depth => Node.Depth;
    }

    /// <summary>The visible rows in order — root-level pages, with expanded children inline.</summary>
    private List<TargetRow> _visibleRows = [];

    private ElementReference[] _rowRefs = [];

    private ElementReference _rootTargetRef;

    /// <summary>Row index the next render must focus (-1 = the root option; null = no move pending).</summary>
    private int? _pendingFocusRow;

    /// <summary> preventDefault mirror for the listbox keydown (repo roving pattern, see
    /// TmSchedulerDateStrip): set for every handled nav key, cleared for anything else.</summary>
    private bool _targetNavHandled;

    private void RebuildVisibleRows()
    {
        var rows = new List<TargetRow>();
        AddNodes(_targetRoots);
        _visibleRows = rows;
        _rowRefs = new ElementReference[rows.Count];
        return;

        void AddNodes(IEnumerable<TargetNode> nodes)
        {
            foreach (var node in nodes)
            {
                rows.Add(new TargetRow(node.Page, node));
                if (node.Expanded)
                    AddNodes(node.Children);
            }
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingFocusRow is null)
            return;

        var index = _pendingFocusRow.Value;
        _pendingFocusRow = null;
        var reference = index < 0 ? _rootTargetRef
            : index < _rowRefs.Length ? _rowRefs[index]
            : default;
        if (reference.Id is null)
            return;
        try { await reference.FocusAsync(); }
        catch (InvalidOperationException) { /* element unmounted mid-move */ }
    }

    /// <summary>Moves the active option (selection follows focus — the roving tabindex target) and
    /// queues the DOM focus move for after the render.</summary>
    private async Task ActivateTargetAsync(INotionPage? page, int rowIndex)
    {
        SelectBulkTarget(page);
        _pendingFocusRow = rowIndex;
        await InvokeAsync(StateHasChanged);
    }

    private TargetNode? FindTargetNode(INotionPage page)
    {
        var id = page.Id.ToString("D");
        return Find(_targetRoots);

        TargetNode? Find(IEnumerable<TargetNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (string.Equals(node.Page.Id.ToString("D"), id, StringComparison.OrdinalIgnoreCase))
                    return node;
                var inChild = Find(node.Children);
                if (inChild is not null)
                    return inChild;
            }

            return null;
        }
    }

    private async Task ExpandActiveTargetAsync()
    {
        if (_pendingTargetPage is null)
            return;
        var node = FindTargetNode(_pendingTargetPage);
        if (node is not null && !node.Expanded && (!node.ChildrenLoaded || node.Children.Count > 0))
            await ToggleNodeAsync(node);
    }

    private async Task CollapseActiveTargetAsync()
    {
        if (_pendingTargetPage is null)
            return;
        var node = FindTargetNode(_pendingTargetPage);
        if (node is { Expanded: true })
            await ToggleNodeAsync(node);
    }

    private async Task OnTargetListKeyDown(KeyboardEventArgs e)
    {
        // Visible options: the root option at index 0, the visible rows after it.
        var count = _visibleRows.Count + 1;
        var current = _pendingTargetIsRoot ? 0
            : 1 + _visibleRows.FindIndex(r => string.Equals(
                r.Page.Id.ToString("D"), _pendingTargetPage?.Id.ToString("D"), StringComparison.OrdinalIgnoreCase));
        if (current < 1)
            current = 0;

        switch (e.Key)
        {
            case "ArrowDown":
                _targetNavHandled = true;
                await MoveAsync(Math.Min(current + 1, count - 1));
                break;
            case "ArrowUp":
                _targetNavHandled = true;
                await MoveAsync(Math.Max(current - 1, 0));
                break;
            case "Home":
                _targetNavHandled = true;
                await MoveAsync(0);
                break;
            case "End":
                _targetNavHandled = true;
                await MoveAsync(count - 1);
                break;
            case "ArrowRight":
                _targetNavHandled = true;
                await ExpandActiveTargetAsync();
                break;
            case "ArrowLeft":
                _targetNavHandled = true;
                await CollapseActiveTargetAsync();
                break;
            default:
                // Enter/Space stay with the native option button (one activation per press, no
                // emulated keydown — see docs/keyboard-activation-convention.md) and only SELECT;
                // the sticky "Select" footer button is the only confirmation path.
                _targetNavHandled = false;
                break;
        }

        async Task MoveAsync(int index)
            => await ActivateTargetAsync(index == 0 ? null : _visibleRows[index - 1].Page, index - 1);
    }


    private void CloseBulkPanels()
    {
        _bulkPickerOpen        = false;
        _bulkDeleteConfirmOpen = false;
        _bulkTargetQuery       = string.Empty;
        _targetRoots           = [];
        _visibleRows           = [];
        _rowRefs               = [];
        _pendingFocusRow       = null;
        _bulkTargetLoading     = false;
        _pendingTargetPage     = null;
        _pendingTargetIsRoot   = false;
    }

    private async Task ClearSelectionAsync()
    {
        _selectedPageIds.Clear();
        CloseBulkPanels();
        _bulkError = string.Empty;
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnBulkTargetSearchAsync(ChangeEventArgs e)
    {
        _bulkTargetQuery = e.Value?.ToString() ?? string.Empty;

        if (SearchProvider is null || string.IsNullOrWhiteSpace(_bulkTargetQuery))
        {
            // No query: the loaded tree roots (selected pages and their descendants excluded).
            _targetRoots = BuildRootTargets();
            _pendingTargetPage = null;
            _pendingTargetIsRoot = false;
            RebuildVisibleRows();
            return;
        }

        _bulkTargetLoading = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            var results = await SearchProvider.SearchPagesAsync(_bulkTargetQuery, null);
            _targetRoots = results
                .Where(page => !IsExcluded(page))
                .Select(page => new TargetNode { Page = page, Depth = 0, ChildrenLoaded = true })
                .ToList();
            _pendingTargetPage = null;
            _pendingTargetIsRoot = false;
            RebuildVisibleRows();
        }
        catch
        {
            _targetRoots = [];
            RebuildVisibleRows();
        }
        finally
        {
            _bulkTargetLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ExecuteBulkTargetAsync(INotionPage? targetPage)
    {
        var selected = _selectedPageIds.ToArray();
        if (selected.Length == 0)
            return;

        var targetPageId = targetPage?.Id.ToString("D");

        try
        {
            if (_bulkAction == BulkAction.Move)
                await DataProvider.MovePagesAsync(selected, targetPageId);
            else
            {
                foreach (var pageId in selected)
                    await DataProvider.CopyPageTreeAsync(pageId, targetPageId);
            }

            _selectedPageIds.Clear();
            _bulkError = string.Empty;
            CloseBulkPanels();
            await OnTreeChanged.InvokeAsync();
        }
        catch (Exception) when (_bulkAction == BulkAction.Move)
        {
            _bulkError = Loc["TmNotionPageTree_BulkMoveDescendantError"];
            CloseBulkPanels();
            await InvokeAsync(StateHasChanged);
        }
        catch
        {
            _bulkError = Loc["TmNotionPageTree_BulkOperationError"];
            CloseBulkPanels();
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ExecuteBulkDeleteAsync()
    {
        var selected = _selectedPageIds.ToArray();
        if (selected.Length == 0)
            return;

        try
        {
            await DataProvider.DeletePagesAsync(selected);
            _selectedPageIds.Clear();
            _bulkError = string.Empty;
            CloseBulkPanels();
            await OnTreeChanged.InvokeAsync();
        }
        catch
        {
            _bulkError = Loc["TmNotionPageTree_BulkOperationError"];
            CloseBulkPanels();
            await InvokeAsync(StateHasChanged);
        }
    }

    private sealed class TargetNode
    {
        public required INotionPage Page { get; init; }
        public int Depth { get; init; }
        public bool Expanded { get; set; }
        public bool ChildrenLoaded { get; set; }
        public List<TargetNode> Children { get; set; } = [];
    }
}
