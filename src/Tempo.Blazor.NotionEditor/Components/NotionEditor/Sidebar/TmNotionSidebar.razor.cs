using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Sidebar;

public partial class TmNotionSidebar : ComponentBase, IAsyncDisposable
{
    // ── DI ────────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded context ──────────────────────────────────────────────────────

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ────────────────────────────────────────────────────────────

    /// <summary>ID of the currently active (open) page — highlights it in the tree.</summary>
    [Parameter] public string? ActivePageId { get; set; }

    /// <summary>Raised when the user requests to open the full trash view.</summary>
    [Parameter] public EventCallback OnTrashRequested { get; set; }

    // ── State — sections ──────────────────────────────────────────────────────

    private bool _pagesExpanded = true;
    private bool _showTrash     = false;

    // ── State — data ──────────────────────────────────────────────────────────

    private IReadOnlyList<INotionPage> _favorites  = [];
    private IReadOnlyList<INotionPage> _recent     = [];
    private IReadOnlyList<INotionPage> _rootPages  = [];
    private int                        _trashCount = 0;
    private string?                    _selectedSpaceId;

    private bool    _isLoading = true;
    private string? _loadError;

    // ── State — search panel ──────────────────────────────────────────────────

    private bool                       _searchOpen    = false;
    private string                     _searchQuery   = string.Empty;
    private IReadOnlyList<INotionPage> _searchResults = [];
    private bool                       _searchLoading = false;
    private string?                    _searchError;
    private ElementReference           _searchInputRef;

    // ── State — new page ──────────────────────────────────────────────────────

    private bool _isCreatingPage = false;
    private bool _localTemplateGalleryOpen = false;

    // ── Resize ───────────────────────────────────────────────────────────────

    private ElementReference _resizeHandleRef;
    private DotNetObjectReference<TmNotionSidebar>? _selfRef;

    // ── Computed ──────────────────────────────────────────────────────────────

    private bool HasSearchProvider => Context.SearchProvider is not null;
    private string? ActiveSelectedSpaceId => _selectedSpaceId ?? Context.SelectedSpaceId;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async Task OnInitializedAsync()
    {
        _selectedSpaceId = Context.SelectedSpaceId;
        await LoadAllAsync();
    }

    protected override void OnParametersSet()
    {
        if (!string.Equals(_selectedSpaceId, Context.SelectedSpaceId, StringComparison.OrdinalIgnoreCase))
            _selectedSpaceId = Context.SelectedSpaceId;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        _selfRef = DotNetObjectReference.Create(this);

        try
        {
            await JS.InvokeVoidAsync(
                "tmNotionEditor.initSidebarResize",
                _resizeHandleRef,
                _selfRef,
                180, 520);
        }
        catch { /* JS unavailable in SSR / test */ }
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    internal async Task LoadAllAsync(string? selectedSpaceIdOverride = null)
    {
        _isLoading = true;
        _loadError = null;
        StateHasChanged();

        try
        {
            var tFav    = Context.DataProvider.GetFavoritesAsync();
            var tRecent = Context.DataProvider.GetRecentPagesAsync(10);
            var tPages  = GetRootPagesAsync(selectedSpaceIdOverride);
            var tTrash  = Context.DataProvider.GetTrashAsync();

            await Task.WhenAll(tFav, tRecent, tPages, tTrash);

            _favorites  = (await tFav).ToList();
            _recent     = (await tRecent).ToList();
            _rootPages  = (await tPages).ToList();
            _trashCount = (await tTrash).Count();
        }
        catch
        {
            _loadError = Loc["TmNotionSidebar_LoadError"];
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private async Task NavigateToAsync(INotionPage page)
    {
        if (Context.NavigateTo is null) return;
        await Context.NavigateTo(page.Id.ToString());
    }

    // ── New page ──────────────────────────────────────────────────────────────

    private async Task CreateNewPageAsync()
    {
        if (_isCreatingPage) return;

        if (Context.OpenTemplateGallery is not null)
        {
            await Context.OpenTemplateGallery();
            return;
        }

        if (Context.TemplateProvider is not null)
        {
            _localTemplateGalleryOpen = true;
            StateHasChanged();
            return;
        }

        _isCreatingPage = true;
        StateHasChanged();

        try
        {
            var page = await Context.DataProvider.CreatePageAsync(null, string.Empty);
            _rootPages = _rootPages.Append(page).ToList();
            StateHasChanged();

            if (Context.NavigateTo is not null)
                await Context.NavigateTo(page.Id.ToString());
        }
        catch { }
        finally
        {
            _isCreatingPage = false;
            StateHasChanged();
        }
    }

    private async Task CreatePageFromTemplateAsync(NotionTemplateDto template)
    {
        if (_isCreatingPage) return;
        _isCreatingPage = true;
        StateHasChanged();

        try
        {
            var isBlank = string.Equals(template.Id, "blank", StringComparison.OrdinalIgnoreCase);
            var title = isBlank
                ? string.Empty
                : string.IsNullOrWhiteSpace(template.Name) ? Loc["TmNotionSidebar_Untitled"] : template.Name;

            var page = await Context.DataProvider.CreatePageAsync(null, title);

            if (!isBlank && template.Blocks.Count > 0)
            {
                var blocks = template.Blocks.Select((block, index) => new PageBlock
                {
                    Id = Guid.NewGuid(),
                    PageId = page.Id,
                    ParentBlockId = null,
                    Type = block.Type,
                    Order = index,
                    Content = block.Content,
                    CreatedAt = DateTime.UtcNow,
                    LastEditedAt = DateTime.UtcNow
                }).ToArray();

                await Context.BlockService.CreateBlocksAsync(page.Id.ToString("D"), blocks, null);
            }

            _localTemplateGalleryOpen = false;
            _rootPages = _rootPages.Append(page).ToList();
            StateHasChanged();

            if (Context.NavigateTo is not null)
                await Context.NavigateTo(page.Id.ToString("D"));
        }
        finally
        {
            _isCreatingPage = false;
            StateHasChanged();
        }
    }

    private void CloseLocalTemplateGallery()
    {
        _localTemplateGalleryOpen = false;
    }

    // ── Search panel ──────────────────────────────────────────────────────────

    private async Task OpenSearchAsync()
    {
        _searchOpen    = true;
        _searchQuery   = string.Empty;
        _searchResults = [];
        _searchError   = null;
        StateHasChanged();

        try
        {
            await Task.Delay(50); // let Blazor render the input first
            await JS.InvokeVoidAsync("tmNotionEditor.focus", _searchInputRef);
        }
        catch { }
    }

    private void CloseSearch()
    {
        _searchOpen    = false;
        _searchQuery   = string.Empty;
        _searchResults = [];
        _searchError   = null;
    }

    private async Task OnSearchInputAsync(ChangeEventArgs e)
    {
        _searchQuery = e.Value?.ToString() ?? string.Empty;
        _searchError = null;

        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            _searchResults = [];
            return;
        }

        if (Context.SearchProvider is null) return;

        _searchLoading = true;
        StateHasChanged();

        try
        {
            var results = await Context.SearchProvider.SearchPagesAsync(_searchQuery, null);
            _searchResults = results.ToList();
        }
        catch
        {
            _searchError   = Loc["TmNotionSidebar_SearchError"];
            _searchResults = [];
        }
        finally
        {
            _searchLoading = false;
            StateHasChanged();
        }
    }

    private async Task OnSearchKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape") CloseSearch();
        else if (e.Key == "Enter" && _searchResults.Count == 1)
        {
            CloseSearch();
            await NavigateToAsync(_searchResults[0]);
        }
    }

    // ── Tree / Favorites changed ──────────────────────────────────────────────

    private async Task OnTreeChangedAsync()     => await LoadAllAsync();
    private async Task OnFavoritesChangedAsync() => await LoadAllAsync();

    private async Task OnSpaceSelectedAsync(string? spaceId)
    {
        _selectedSpaceId = string.IsNullOrWhiteSpace(spaceId) ? null : spaceId;

        if (Context.SelectSpace is not null)
            await Context.SelectSpace(spaceId);

        await LoadAllAsync(_selectedSpaceId);

        if (_rootPages.FirstOrDefault() is { } firstPage && !IsActivePage(firstPage))
            await NavigateToAsync(firstPage);
    }

    private async Task OnCurrentPageMovedAsync(string spaceId)
    {
        _selectedSpaceId = string.IsNullOrWhiteSpace(spaceId) ? null : spaceId;

        if (Context.CurrentPageMovedToSpace is not null)
            await Context.CurrentPageMovedToSpace(spaceId);

        await LoadAllAsync(_selectedSpaceId);
    }

    // ── Trash ─────────────────────────────────────────────────────────────────

    private async Task OpenTrashAsync()
    {
        _showTrash = true;
        await OnTrashRequested.InvokeAsync();
    }

    private async Task CloseTrashAsync()
    {
        _showTrash = false;
        await LoadAllAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetPageIcon(INotionPage page) =>
        string.IsNullOrEmpty(page.IconEmoji) ? "📄" : page.IconEmoji;

    private string GetPageTitle(INotionPage page) =>
        string.IsNullOrWhiteSpace(page.Title)
            ? Loc["TmNotionSidebar_Untitled"]
            : page.Title;

    private bool IsActivePage(INotionPage page) =>
        ActivePageId is not null &&
        page.Id.ToString().Equals(ActivePageId, StringComparison.OrdinalIgnoreCase);

    private async Task<IEnumerable<INotionPage>> GetRootPagesAsync(string? selectedSpaceIdOverride = null)
    {
        var selectedSpaceId = selectedSpaceIdOverride ?? ActiveSelectedSpaceId;
        if (Context.SpaceProvider is not null && !string.IsNullOrWhiteSpace(selectedSpaceId))
        {
            var pages = await Context.SpaceProvider.GetPagesInSpaceAsync(selectedSpaceId);
            Context.RememberPages(pages);
            return pages.Where(page => page.ParentId is null);
        }

        var roots = await Context.DataProvider.GetChildPagesAsync(null);
        Context.RememberPages(roots);
        return roots;
    }

    // ── JS interop callback ───────────────────────────────────────────────────

    [JSInvokable]
    public void OnSidebarResized(int width) { /* width persisted by JS in localStorage */ }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.destroySidebarResize", _resizeHandleRef);
        }
        catch { }

        _selfRef?.Dispose();
    }
}
