using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Interfaces;

namespace Tempo.Blazor.Components.NotionEditor.Sidebar;

public partial class TmNotionSidebarTrash : ComponentBase
{
    // ── DI ────────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded context ──────────────────────────────────────────────────────

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ────────────────────────────────────────────────────────────

    [Parameter] public EventCallback OnClosed { get; set; }

    // ── State ─────────────────────────────────────────────────────────────────

    private IReadOnlyList<INotionPage> _allItems    = [];
    private string                     _filterQuery = string.Empty;
    private bool                       _isLoading   = true;
    private string?                    _loadError;
    private ElementReference           _searchInputRef;

    private HashSet<Guid> _pendingDelete = [];

    /// <summary>Ancestor breadcrumb per trashed page (joined " / "), resolved from the loaded tree.</summary>
    private readonly Dictionary<Guid, string> _parentPaths = [];

    // ── Computed ──────────────────────────────────────────────────────────────

    private IReadOnlyList<INotionPage> FilteredItems =>
        string.IsNullOrWhiteSpace(_filterQuery)
            ? _allItems
            : _allItems
                .Where(p => (p.Title ?? string.Empty)
                    .Contains(_filterQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();

    private string GetTitle(INotionPage page) =>
        string.IsNullOrWhiteSpace(page.Title)
            ? Loc["TmNotionSidebarTrash_Untitled"]
            : page.Title;

    private string GetDeletedAt(INotionPage page)
    {
        if (page.DeletedAt is null) return string.Empty;

        var diff = DateTime.UtcNow - page.DeletedAt.Value;

        if (diff.TotalHours < 24)
            return Loc["TmNotionSidebarTrash_DeletedToday"];

        if (diff.TotalDays < 30)
            return string.Format(Loc["TmNotionSidebarTrash_DeletedDaysAgo"], (int)diff.TotalDays);

        return string.Format(
            Loc["TmNotionSidebarTrash_DeletedOn"],
            page.DeletedAt.Value.ToLocalTime().ToString("MMM d, yyyy"));
    }

    private bool IsConfirming(Guid id) => _pendingDelete.Contains(id);

    private string GetParentPath(INotionPage page)
        => _parentPaths.TryGetValue(page.Id, out var path) ? path : string.Empty;

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveParentPathsAsync(IReadOnlyList<INotionPage> items)
    {
        var paths = new Dictionary<Guid, string>();
        foreach (var item in items)
        {
            var segments = new List<string>();
            var seen = new HashSet<Guid> { item.Id };
            var parentId = item.ParentId?.ToString("D");
            while (!string.IsNullOrEmpty(parentId) && Guid.TryParse(parentId, out var parentGuid) && seen.Add(parentGuid))
            {
                INotionPage? parent;
                try
                {
                    parent = await Context.DataProvider.GetPageAsync(parentGuid.ToString("D"));
                }
                catch
                {
                    break;
                }

                if (parent is null)
                    break;

                segments.Insert(0, string.IsNullOrWhiteSpace(parent.Title)
                    ? Loc["TmNotionSidebarTrash_Untitled"].ToString()
                    : parent.Title);
                parentId = parent.ParentId?.ToString("D");
            }

            if (segments.Count > 0)
                paths[item.Id] = string.Join(" / ", segments);
        }

        return paths;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        try
        {
            await Task.Delay(30);
            await JS.InvokeVoidAsync("tmNotionEditor.focus", _searchInputRef);
        }
        catch { }
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    internal async Task LoadAsync()
    {
        _isLoading = true;
        _loadError = null;
        StateHasChanged();

        try
        {
            var result = await Context.DataProvider.GetTrashAsync();
            _allItems  = result
                .OrderByDescending(p => p.DeletedAt ?? DateTime.MinValue)
                .ToList();
            _parentPaths.Clear();
            foreach (var (id, path) in await ResolveParentPathsAsync(_allItems))
                _parentPaths[id] = path;
        }
        catch
        {
            _loadError = Loc["TmNotionSidebarTrash_LoadError"];
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    // ── Filter ────────────────────────────────────────────────────────────────

    private void OnFilterInput(ChangeEventArgs e)
    {
        _filterQuery = e.Value?.ToString() ?? string.Empty;
    }

    private void OnFilterKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            _filterQuery = string.Empty;
    }

    // ── Restore ───────────────────────────────────────────────────────────────

    private async Task RestoreAsync(INotionPage page)
    {
        try
        {
            await Context.DataProvider.RestorePageAsync(page.Id.ToString());
            await LoadAsync();
        }
        catch { }
    }

    // ── Permanent delete ──────────────────────────────────────────────────────

    private void BeginDeleteConfirm(Guid id)   => _pendingDelete.Add(id);
    private void CancelDeleteConfirm(Guid id)  => _pendingDelete.Remove(id);

    private async Task ConfirmPermanentDeleteAsync(INotionPage page)
    {
        _pendingDelete.Remove(page.Id);

        try
        {
            await Context.DataProvider.PermanentlyDeletePageAsync(page.Id.ToString());
            await LoadAsync();
        }
        catch { }
    }

    // ── Close ─────────────────────────────────────────────────────────────────

    private async Task CloseAsync()
    {
        await OnClosed.InvokeAsync();
    }
}
