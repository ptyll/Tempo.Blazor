using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.DocumentLibrary;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Components.Files;

/// <summary>
/// A Word-like "Open document" dialog over an <see cref="ITempoDocumentLibraryProvider"/>:
/// a folder tree, breadcrumb, searchable list/grid of documents, optional folder/document
/// management, and a link-or-copy choice. Emits a <see cref="DocumentOpenResult"/> on confirm.
/// </summary>
public partial class TmDocumentOpenDialog : ComponentBase, IAsyncDisposable
{
    private const string GuardModulePath = "./_content/Tempo.Blazor/Components/Files/TmDocumentOpenDialog.razor.js";

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ─────────────────────────────────────────────────────────────

    /// <summary>The library to browse. Required.</summary>
    [Parameter, EditorRequired] public ITempoDocumentLibraryProvider Provider { get; set; } = default!;

    /// <summary>Which kind of documents to list.</summary>
    [Parameter] public TempoDocumentKind Kind { get; set; }

    /// <summary>Whether the dialog is shown.</summary>
    [Parameter] public bool Open { get; set; }

    /// <summary>Raised when the dialog opens or closes.</summary>
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }

    /// <summary>Whether to offer the link-or-copy choice. Defaults to <c>true</c>.</summary>
    [Parameter] public bool ShowModeToggle { get; set; } = true;

    /// <summary>Initial insert mode. Defaults to <see cref="DocumentOpenMode.Link"/>.</summary>
    [Parameter] public DocumentOpenMode DefaultMode { get; set; } = DocumentOpenMode.Link;

    /// <summary>Raised with the chosen document when the user confirms.</summary>
    [Parameter] public EventCallback<DocumentOpenResult> OnSelected { get; set; }

    /// <summary>Raised when the user cancels.</summary>
    [Parameter] public EventCallback OnCancelled { get; set; }

    /// <summary>Page size for browsing. Defaults to 50.</summary>
    [Parameter] public int PageSize { get; set; } = 50;

    /// <summary>Search debounce in milliseconds. Defaults to 300; set 0 in tests.</summary>
    [Parameter] public int SearchDebounceMs { get; set; } = 300;

    // ── State ──────────────────────────────────────────────────────────────────

    private bool _wasOpen;
    private bool _loading;
    private bool _error;

    private DocumentLibraryFolder? _tree;
    private string _currentFolder = "/";
    private readonly List<DocumentLibraryEntry> _entries = [];
    private int _totalCount;

    private Guid? _selectedId;
    private DocumentLibraryEntry? _selected;

    private DialogView _view = DialogView.List;
    private DocumentLibrarySortField _sortField = DocumentLibrarySortField.Name;
    private bool _descending;

    private string _search = string.Empty;
    private DocumentOpenMode _mode = DocumentOpenMode.Link;

    private bool _creatingFolder;
    private string _newFolderName = string.Empty;

    private bool _renaming;
    private string _renameName = string.Empty;
    private bool _renameError;

    private bool _confirmingDelete;

    private CancellationTokenSource? _searchCts;

    // Space scroll guard (TmDocumentOpenDialog.razor.js): attached to the dialog root while it is open.
    private ElementReference _root;
    private LazyJsModule? _guardModule;
    private bool _guardAttached;

    private enum DialogView { List, Grid }

    private bool CanSearch => Provider.Capabilities.HasFlag(DocumentLibraryCapabilities.Search);
    private bool CanCreateFolder => Provider.Capabilities.HasFlag(DocumentLibraryCapabilities.CreateFolder);
    private bool CanRename => Provider.Capabilities.HasFlag(DocumentLibraryCapabilities.Rename);
    private bool CanDelete => Provider.Capabilities.HasFlag(DocumentLibraryCapabilities.Delete);

    // ── Lifecycle ────────────────────────────────────────────────────────────────

    protected override async Task OnParametersSetAsync()
    {
        if (Open && !_wasOpen)
        {
            _wasOpen = true;
            ResetState();
            await LoadAsync();
        }
        else if (!Open)
        {
            _wasOpen = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Open && !_guardAttached && _root.Id is not null)
        {
            _guardAttached = true;
            _guardModule ??= new LazyJsModule(JS, GuardModulePath);
            await _guardModule.TryInvokeVoidAsync("attachSpaceGuard", _root);
        }
        else if (!Open && _guardAttached)
        {
            _guardAttached = false;
            if (_guardModule is { IsImported: true })
            {
                await _guardModule.TryInvokeVoidAsync("detachSpaceGuard", _root);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        if (_guardModule is not null)
        {
            await _guardModule.DisposeAsync();
        }
    }

    private void ResetState()
    {
        _loading = false;
        _error = false;
        _tree = null;
        _currentFolder = "/";
        _entries.Clear();
        _totalCount = 0;
        _selectedId = null;
        _selected = null;
        _view = DialogView.List;
        _sortField = DocumentLibrarySortField.Name;
        _descending = false;
        _search = string.Empty;
        _mode = DefaultMode;
        _creatingFolder = false;
        _renaming = false;
        _renameError = false;
        _confirmingDelete = false;
    }

    // ── Loading ──────────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        _loading = true;
        _error = false;
        StateHasChanged();
        try
        {
            _tree = await Provider.GetFolderTreeAsync(Kind);
            await ReloadEntriesAsync(resetPaging: true);
        }
        catch
        {
            _error = true;
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private async Task ReloadEntriesAsync(bool resetPaging = true)
    {
        if (resetPaging)
        {
            _entries.Clear();
        }

        var page = await Provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = Kind,
            FolderPath = string.IsNullOrEmpty(_search) ? _currentFolder : null,
            Search = string.IsNullOrEmpty(_search) ? null : _search,
            SortField = _sortField,
            Descending = _descending,
            Skip = _entries.Count,
            Take = PageSize
        });

        _entries.AddRange(page.Items);
        _totalCount = page.TotalCount;

        // Keep selection only if still present.
        if (_selectedId is { } id && _entries.All(e => e.Id != id))
        {
            _selectedId = null;
            _selected = null;
        }
    }

    private bool HasMore => _entries.Count < _totalCount;

    private async Task LoadMoreAsync() => await SafeReloadAsync(resetPaging: false);

    private async Task SafeReloadAsync(bool resetPaging)
    {
        try
        {
            await ReloadEntriesAsync(resetPaging);
        }
        catch
        {
            _error = true;
        }
    }

    // ── Navigation ─────────────────────────────────────────────────────────────

    private async Task NavigateToAsync(string folderPath)
    {
        _currentFolder = folderPath;
        _search = string.Empty;
        _creatingFolder = false;
        _renaming = false;
        _confirmingDelete = false;
        await SafeReloadAsync(resetPaging: true);
    }

    private IReadOnlyList<(string Path, string Name)> Breadcrumb()
    {
        var crumbs = new List<(string, string)> { ("/", Loc["TmDocumentOpenDialog_RootFolder"]) };
        if (_currentFolder == "/")
        {
            return crumbs;
        }

        var segments = _currentFolder.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var acc = string.Empty;
        foreach (var seg in segments)
        {
            acc += "/" + seg;
            crumbs.Add((acc, seg));
        }

        return crumbs;
    }

    private static IEnumerable<(DocumentLibraryFolder Folder, int Depth)> Flatten(
        DocumentLibraryFolder folder, int depth = 0)
    {
        yield return (folder, depth);
        foreach (var child in folder.Children)
        {
            foreach (var d in Flatten(child, depth + 1))
            {
                yield return d;
            }
        }
    }

    // ── Search ─────────────────────────────────────────────────────────────────

    private async Task OnSearchInputAsync(ChangeEventArgs e)
    {
        _search = e.Value?.ToString() ?? string.Empty;
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        try
        {
            if (SearchDebounceMs > 0)
            {
                await Task.Delay(SearchDebounceMs, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await SafeReloadAsync(resetPaging: true);
        StateHasChanged();
    }

    // ── Sorting ──────────────────────────────────────────────────────────────────

    private async Task SortByAsync(DocumentLibrarySortField field)
    {
        if (_sortField == field)
        {
            _descending = !_descending;
        }
        else
        {
            _sortField = field;
            _descending = false;
        }

        await SafeReloadAsync(resetPaging: true);
    }

    // ── Selection ──────────────────────────────────────────────────────────────

    // Rows (div role=row) and grid cards (div role=option) are non-native focusables, so the library
    // emulates activation (docs/keyboard-activation-convention.md): Enter opens on keydown (auto-repeat
    // ignored), Space selects on keyup — where a native button would click.
    private async Task OnItemKeyDownAsync(KeyboardEventArgs e, DocumentLibraryEntry entry)
    {
        if (e.Key == "Enter" && !e.Repeat)
        {
            await ConfirmAsync(entry);
        }
    }

    private void OnItemKeyUp(KeyboardEventArgs e, DocumentLibraryEntry entry)
    {
        if (e.Key == " ")
        {
            Select(entry);
        }
    }

    /// <summary>The <c>aria-sort</c> value of a sortable column header.</summary>
    private string AriaSort(DocumentLibrarySortField field)
        => _sortField != field ? "none" : _descending ? "descending" : "ascending";

    private void Select(DocumentLibraryEntry entry)
    {
        _selectedId = entry.Id;
        _selected = entry;
        _renaming = false;
        _confirmingDelete = false;
        _renameError = false;
        _creatingFolder = false;
    }

    private async Task ConfirmAsync(DocumentLibraryEntry entry)
    {
        Select(entry);
        await ConfirmSelectionAsync();
    }

    private async Task ConfirmSelectionAsync()
    {
        if (_selected is null)
        {
            return;
        }

        await OnSelected.InvokeAsync(new DocumentOpenResult
        {
            DocumentId = _selected.Id,
            Kind = _selected.Kind,
            Mode = _mode,
            Name = _selected.Name
        });
        await CloseAsync();
    }

    private async Task CancelAsync()
    {
        await OnCancelled.InvokeAsync();
        await CloseAsync();
    }

    private async Task CloseAsync()
    {
        if (OpenChanged.HasDelegate)
        {
            await OpenChanged.InvokeAsync(false);
        }
    }

    private void SetView(DialogView view) => _view = view;

    private void SetMode(DocumentOpenMode mode) => _mode = mode;

    // ── New folder ───────────────────────────────────────────────────────────────

    private void BeginNewFolder()
    {
        _creatingFolder = true;
        _newFolderName = string.Empty;
    }

    private async Task ConfirmNewFolderAsync()
    {
        var name = _newFolderName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return;
        }

        try
        {
            await Provider.CreateFolderAsync(Kind, _currentFolder, name);
            _tree = await Provider.GetFolderTreeAsync(Kind);
            _creatingFolder = false;
        }
        catch
        {
            _error = true;
        }
    }

    // ── Rename ───────────────────────────────────────────────────────────────────

    private void BeginRename()
    {
        if (_selected is null)
        {
            return;
        }

        _creatingFolder = false;
        _confirmingDelete = false;
        _renaming = true;
        _renameError = false;
        _renameName = _selected.Name;
    }

    private async Task ConfirmRenameAsync()
    {
        if (_selected is null)
        {
            return;
        }

        var name = _renameName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            _renameError = true;
            return;
        }

        try
        {
            await Provider.RenameDocumentAsync(Kind, _selected.Id, name);
            _renaming = false;
            await SafeReloadAsync(resetPaging: true);
        }
        catch
        {
            _error = true;
        }
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    private void BeginDelete()
    {
        if (_selected is not null)
        {
            _creatingFolder = false;
            _renaming = false;
            _confirmingDelete = true;
        }
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_selected is null)
        {
            return;
        }

        try
        {
            await Provider.DeleteDocumentsAsync(Kind, [_selected.Id]);
            _confirmingDelete = false;
            _selectedId = null;
            _selected = null;
            await SafeReloadAsync(resetPaging: true);
        }
        catch
        {
            _error = true;
        }
    }

    private async Task RetryAsync() => await LoadAsync();
}
