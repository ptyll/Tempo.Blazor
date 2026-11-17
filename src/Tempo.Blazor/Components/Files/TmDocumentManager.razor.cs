using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Interfaces;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;

namespace Tempo.Blazor.Components.Files;

/// <summary>
/// A DMS-ready document manager component with generic metadata support,
/// per-item permissions, custom forms, and detail panel.
/// Supports keyboard navigation, multiselect, inline rename, and context menu.
/// </summary>
/// <typeparam name="TMetadata">Custom metadata type attached to each document.</typeparam>
public partial class TmDocumentManager<TMetadata> where TMetadata : class
{
    // ── Core state ───────────────────────────────────────────────
    private string _currentPath = "/";
    private List<DocumentManagerItem<TMetadata>> _items = [];
    private List<DocumentManagerItem<TMetadata>>? _folderTree;
    private readonly List<DocumentManagerItem<TMetadata>> _selectedItems = [];
    private DocumentManagerViewMode _viewMode = DocumentManagerViewMode.List;
    private DocumentManagerItem<TMetadata>? _renamingItem;
    private string _renameValue = string.Empty;
    private ElementReference _renameInputRef;
    private bool _shouldFocusRenameInput;
    private ElementReference _contentRef;

    // ── Keyboard navigation ──────────────────────────────────────
    private int _focusedIndex = -1;
    private int _anchorIndex = -1;
    private int _gridColumnCount = 1;

    // ── Delete ───────────────────────────────────────────────────
    private bool _showDeleteDialog;
    private readonly List<DocumentManagerItem<TMetadata>> _itemsToDelete = [];

    // ── Custom forms ─────────────────────────────────────────────
    private bool _showNewFolderForm;
    // _newFolderContext.Name is the single source of truth for new folder names
    private TMetadata? _newFolderMetadata;
    private NewFolderContext<TMetadata>? _newFolderContext;

    // ── Upload ───────────────────────────────────────────────────
    private bool _showUploadForm;
    private TMetadata? _uploadMetadata;
    private UploadContext<TMetadata>? _uploadContext;

    // ── Attachments ──────────────────────────────────────────────
    private bool _showAttachmentUploadForm;
    private DocumentManagerItem<TMetadata>? _attachmentTargetItem;
    private List<FileUploadInfo> _attachmentUploadFiles = [];
    private List<TmAttachment> _editAttachments = [];
    private bool _showAttachmentDeleteDialog;
    private string? _attachmentIdToDelete;

    private bool _showCustomDeleteForm;
    private DeleteContext<TMetadata>? _deleteContext;

    private bool _showEditForm;
    private DocumentManagerItem<TMetadata>? _editingItem;
    private TMetadata? _editMetadata;
    private EditContext<TMetadata>? _editContext;

    // ── Detail panel ─────────────────────────────────────────────
    private bool _showDetailPanel;
    private DocumentManagerItem<TMetadata>? _detailItem;

    // ── Context menu ─────────────────────────────────────────────
    private bool _showContextMenu;
    private double _contextMenuX;
    private double _contextMenuY;
    private DocumentManagerItem<TMetadata>? _contextMenuItem;

    // ── Upload / versioning ──────────────────────────────────────
    private readonly List<TmUploadItem> _uploads = [];
    private bool _showVersionHistory;
    private DocumentManagerItem<TMetadata>? _versionHistoryItem;
    // Scan verdicts recorded this session, re-applied after every reload so a Blocked/Pending
    // gate survives providers that return fresh item instances. Keyed by asset id and "name:"+name.
    private readonly Dictionary<string, (FileScanStatus Status, string? Message)> _scanVerdicts = [];

    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────

    /// <summary>Data provider for document operations.</summary>
    [Parameter] public IDocumentManagerDataProvider<TMetadata>? DataProvider { get; set; }

    /// <summary>Current folder path. Two-way bindable.</summary>
    [Parameter] public string? CurrentPath { get; set; }

    /// <summary>Event fired when the current path changes.</summary>
    [Parameter] public EventCallback<string> CurrentPathChanged { get; set; }

    /// <summary>Display mode (List or Grid). Default is <see cref="DocumentManagerViewMode.List"/>.</summary>
    [Parameter] public DocumentManagerViewMode ViewMode { get; set; } = DocumentManagerViewMode.List;

    /// <summary>Event fired when the view mode changes.</summary>
    [Parameter] public EventCallback<DocumentManagerViewMode> ViewModeChanged { get; set; }

    /// <summary>When true, disables all user interactions.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Whether to show the folder tree sidebar. Default is <c>true</c>.</summary>
    [Parameter] public bool ShowFolderTree { get; set; } = true;

    /// <summary>Whether to show the New Folder button. Default is <c>true</c>.</summary>
    [Parameter] public bool ShowNewFolderButton { get; set; } = true;

    /// <summary>Whether to show the Upload button. Default is <c>true</c>.</summary>
    [Parameter] public bool ShowUploadButton { get; set; } = true;

    /// <summary>Whether to show the Delete button. Default is <c>true</c>.</summary>
    [Parameter] public bool ShowDeleteButton { get; set; } = true;

    /// <summary>Whether to show the Rename button. Default is <c>true</c>.</summary>
    [Parameter] public bool ShowRenameButton { get; set; } = true;

    /// <summary>Whether to show the Download button. Default is <c>true</c>.</summary>
    [Parameter] public bool ShowDownloadButton { get; set; } = true;

    /// <summary>Whether to show the Move button. Default is <c>false</c>.</summary>
    [Parameter] public bool ShowMoveButton { get; set; }

    /// <summary>Whether to show the Copy button. Default is <c>false</c>.</summary>
    [Parameter] public bool ShowCopyButton { get; set; }

    /// <summary>Whether to show the Edit button. Default is <c>true</c>.</summary>
    [Parameter] public bool ShowEditButton { get; set; } = true;

    /// <summary>Whether to show the Detail / Properties button. Default is <c>true</c>.</summary>
    [Parameter] public bool ShowDetailButton { get; set; } = true;

    /// <summary>When <c>true</c>, hides actions the current user is not permitted to perform.</summary>
    [Parameter] public bool RespectPermissions { get; set; } = true;

    /// <summary>How the detail panel is displayed. Default is <see cref="DocumentManagerDetailMode.SlideIn"/>.</summary>
    [Parameter] public DocumentManagerDetailMode DetailMode { get; set; } = DocumentManagerDetailMode.SlideIn;

    /// <summary>When <c>true</c>, allows one logical item to hold multiple physical file attachments.</summary>
    [Parameter] public bool AllowMultipleAttachments { get; set; }

    /// <summary>Maximum size of a single upload in bytes. Default 100 MB. Also caps the browser read stream.</summary>
    [Parameter] public long MaxUploadSize { get; set; } = 100L * 1024 * 1024;

    /// <summary>Optional scan hook run after each upload; a <see cref="FileScanStatus.Blocked"/> result marks the item unavailable.</summary>
    [Parameter] public IFileScanHook? ScanHook { get; set; }

    /// <summary>Optional versioning hook. When supplied and <see cref="ShowVersionHistory"/> is set, a version-history action is available.</summary>
    [Parameter] public IFileVersioningHook? VersioningHook { get; set; }

    /// <summary>Whether to show the version-history action (requires <see cref="VersioningHook"/>). Default <c>true</c>.</summary>
    [Parameter] public bool ShowVersionHistory { get; set; } = true;

    /// <summary>Fires for each chunk progress update during a chunked upload.</summary>
    [Parameter] public EventCallback<TmUploadProgress> OnUploadProgress { get; set; }

    /// <summary>Custom template for rendering the attachment list inside the detail panel or edit modal.</summary>
    [Parameter] public RenderFragment<AttachmentListContext<TMetadata>>? AttachmentListTemplate { get; set; }

    /// <summary>Custom form for uploading files with metadata. When null, a simple file input is used.</summary>
    [Parameter] public RenderFragment<UploadContext<TMetadata>>? UploadForm { get; set; }

    /// <summary>Custom form for creating a new folder. When null, a default inline input is used.</summary>
    [Parameter] public RenderFragment<NewFolderContext<TMetadata>>? NewFolderForm { get; set; }

    /// <summary>Custom delete confirmation form. When null, a default <see cref="TmDialog"/> is used.</summary>
    [Parameter] public RenderFragment<DeleteContext<TMetadata>>? DeleteForm { get; set; }

    /// <summary>Custom edit/metadata form. When null, the Edit action is unavailable.</summary>
    [Parameter] public RenderFragment<EditContext<TMetadata>>? EditForm { get; set; }

    /// <summary>Custom detail / properties panel content.</summary>
    [Parameter] public RenderFragment<DetailContext<TMetadata>>? DetailPanel { get; set; }

    /// <summary>Custom context menu for an item. When null, no context menu is shown.</summary>
    [Parameter] public RenderFragment<ContextMenuContext<TMetadata>>? ItemContextMenu { get; set; }

    /// <summary>Template rendered below the item name in list/grid views (e.g. tags, metadata preview).</summary>
    [Parameter] public RenderFragment<DocumentManagerItem<TMetadata>>? ItemMetaTemplate { get; set; }

    /// <summary>Event fired when an item is opened (double-clicked or Enter).</summary>
    [Parameter] public EventCallback<DocumentManagerItem<TMetadata>> OnItemOpen { get; set; }

    /// <summary>Event fired when the selection changes.</summary>
    [Parameter] public EventCallback<IReadOnlyList<DocumentManagerItem<TMetadata>>> OnSelectionChanged { get; set; }

    /// <summary>Additional CSS class for the wrapper element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional attributes spread onto the wrapper element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Lifecycle ────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!string.IsNullOrEmpty(CurrentPath) && CurrentPath != _currentPath)
        {
            _currentPath = CurrentPath;
        }
        _viewMode = ViewMode;
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    // ── Data loading ─────────────────────────────────────────────

    private async Task LoadDataAsync()
    {
        if (DataProvider is null) return;

        try
        {
            _items = (await DataProvider.GetFolderContentsAsync(_currentPath)).ToList();
            _folderTree = (await DataProvider.GetFolderTreeAsync()).ToList();
        }
        catch
        {
            _items = [];
        }

        ReapplyScanVerdicts();

        _focusedIndex = _items.Count > 0 ? 0 : -1;
        _anchorIndex = _focusedIndex;
    }

    private void ReapplyScanVerdicts()
    {
        if (_scanVerdicts.Count == 0) return;
        foreach (var item in _items)
        {
            if (item.IsDirectory) continue;
            var attachmentAsset = item.Attachments
                .FirstOrDefault(a => !string.IsNullOrEmpty(a.AssetId) && _scanVerdicts.ContainsKey(a.AssetId!))?.AssetId;
            if (_scanVerdicts.TryGetValue(item.Id, out var v)
                || (attachmentAsset is not null && _scanVerdicts.TryGetValue(attachmentAsset, out v))
                || _scanVerdicts.TryGetValue("name:" + item.Name, out v))
            {
                item.ScanStatus = v.Status;
                item.ScanMessage = v.Message;
            }
        }
    }

    // ── Permissions helpers ──────────────────────────────────────

    private bool CanRead(DocumentManagerItem<TMetadata> item)
        => !RespectPermissions || (item.Permissions?.CanRead ?? true);

    private bool CanDelete(DocumentManagerItem<TMetadata> item)
        => !RespectPermissions || (item.Permissions?.CanDelete ?? true);

    private bool CanWrite(DocumentManagerItem<TMetadata> item)
        => !RespectPermissions || (item.Permissions?.CanWrite ?? true);

    private bool CanRename(DocumentManagerItem<TMetadata> item)
        => !RespectPermissions || (item.Permissions?.CanRename ?? true);

    private bool CanDownload(DocumentManagerItem<TMetadata> item)
        => item.IsScanAvailable && (!RespectPermissions || (item.Permissions?.CanDownload ?? true));

    private bool CanDownloadAll(DocumentManagerItem<TMetadata> item)
        => !item.IsDirectory && item.Attachments.Count > 1 && CanDownload(item);

    private bool CanShare(DocumentManagerItem<TMetadata> item)
        => !RespectPermissions || (item.Permissions?.CanShare ?? true);

    private bool CanDeleteSelection()
        => _selectedItems.Count > 0 && _selectedItems.All(CanDelete);

    private bool CanDownloadAllSelection()
        => _selectedItems.Count == 1 && CanDownloadAll(_selectedItems[0]);

    private bool CanRenameSelection()
        => _selectedItems.Count == 1 && CanRename(_selectedItems[0]);

    private bool CanDownloadSelection()
        => _selectedItems.Count > 0 && _selectedItems.Any(i => !i.IsDirectory && CanDownload(i));

    private bool CanEditSelection()
        => _selectedItems.Count == 1 && CanWrite(_selectedItems[0]);

    private bool CanMoveSelection()
        => _selectedItems.Count > 0 && _selectedItems.All(CanWrite);

    private bool CanCopySelection()
        => _selectedItems.Count > 0;

    // ── Navigation ───────────────────────────────────────────────

    private async Task NavigateTo(string path)
    {
        if (Disabled) return;
        _currentPath = path;
        _selectedItems.Clear();
        await CurrentPathChanged.InvokeAsync(_currentPath);
        await OnSelectionChanged.InvokeAsync(_selectedItems);
        await LoadDataAsync();
    }

    private async Task NavigateToRoot()
    {
        await NavigateTo("/");
    }

    private async Task NavigateUpAsync()
    {
        if (_currentPath != "/")
        {
            var parent = GetParentPath(_currentPath);
            await NavigateTo(parent);
        }
    }

    private async Task OnItemDoubleClickAsync(DocumentManagerItem<TMetadata> item)
    {
        if (Disabled || !CanRead(item)) return;
        if (item.IsDirectory)
        {
            await NavigateTo(item.Path);
        }
        else if (item.IsScanAvailable)
        {
            await OnItemOpen.InvokeAsync(item);
        }
    }

    private List<BreadcrumbSegment> _breadcrumbSegments => BuildBreadcrumb(_currentPath);

    private static List<BreadcrumbSegment> BuildBreadcrumb(string path)
    {
        var segments = new List<BreadcrumbSegment>();
        if (string.IsNullOrEmpty(path) || path == "/") return segments;

        var parts = path.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        foreach (var part in parts)
        {
            current += "/" + part;
            segments.Add(new BreadcrumbSegment(part, current));
        }
        return segments;
    }

    private sealed record BreadcrumbSegment(string Name, string Path);

    // ── Selection ────────────────────────────────────────────────

    private async Task HandleItemClickAsync(DocumentManagerItem<TMetadata> item, MouseEventArgs e)
    {
        if (Disabled || !CanRead(item)) return;

        var index = _items.IndexOf(item);
        if (index < 0) return;

        if (e.CtrlKey)
        {
            if (_selectedItems.Contains(item))
                _selectedItems.Remove(item);
            else
                _selectedItems.Add(item);

            _focusedIndex = index;
            _anchorIndex = index;
        }
        else if (e.ShiftKey && _anchorIndex >= 0 && _anchorIndex < _items.Count)
        {
            var start = Math.Min(_anchorIndex, index);
            var end = Math.Max(_anchorIndex, index);
            _selectedItems.Clear();
            for (int i = start; i <= end; i++)
                _selectedItems.Add(_items[i]);

            _focusedIndex = index;
        }
        else
        {
            _selectedItems.Clear();
            _selectedItems.Add(item);
            _focusedIndex = index;
            _anchorIndex = index;
        }

        await OnSelectionChanged.InvokeAsync(_selectedItems);
    }

    // ── View mode ────────────────────────────────────────────────

    private async Task SetViewMode(DocumentManagerViewMode mode)
    {
        if (Disabled) return;
        _viewMode = mode;
        _gridColumnCount = mode == DocumentManagerViewMode.Grid ? 4 : 1;
        await ViewModeChanged.InvokeAsync(mode);
    }

    // ── Keyboard handling ────────────────────────────────────────

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (Disabled || _renamingItem is not null) return;

        if ((e.CtrlKey || e.MetaKey) && e.Key.Equals("a", StringComparison.OrdinalIgnoreCase))
        {
            SelectAll();
            return;
        }

        switch (e.Key)
        {
            case "ArrowUp":
            case "ArrowDown":
            case "ArrowLeft":
            case "ArrowRight":
                await HandleArrowKeyAsync(e);
                break;

            case "Enter":
                if (_focusedIndex >= 0 && _focusedIndex < _items.Count)
                    await OnItemDoubleClickAsync(_items[_focusedIndex]);
                break;

            case "Delete":
                if (_selectedItems.Count > 0 && CanDeleteSelection())
                    DeleteSelectedAsync();
                break;

            case "F2":
                if (_selectedItems.Count == 1 && CanRenameSelection())
                    StartRenameAsync();
                break;

            case "Backspace":
                await NavigateUpAsync();
                break;
        }
    }

    private async Task HandleArrowKeyAsync(KeyboardEventArgs e)
    {
        if (_items.Count == 0) return;

        if (_focusedIndex < 0 || _focusedIndex >= _items.Count)
            _focusedIndex = 0;

        var columns = _viewMode == DocumentManagerViewMode.Grid ? Math.Max(1, _gridColumnCount) : 1;
        var newIndex = _focusedIndex;

        switch (e.Key)
        {
            case "ArrowUp":
                newIndex = _viewMode == DocumentManagerViewMode.Grid
                    ? _focusedIndex - columns
                    : _focusedIndex - 1;
                break;

            case "ArrowDown":
                newIndex = _viewMode == DocumentManagerViewMode.Grid
                    ? _focusedIndex + columns
                    : _focusedIndex + 1;
                break;

            case "ArrowLeft":
                newIndex = _focusedIndex - 1;
                break;

            case "ArrowRight":
                newIndex = _focusedIndex + 1;
                break;
        }

        if (newIndex < 0) newIndex = 0;
        if (newIndex >= _items.Count) newIndex = _items.Count - 1;
        if (newIndex == _focusedIndex) return;

        if (e.ShiftKey && _anchorIndex >= 0 && _anchorIndex < _items.Count)
        {
            var start = Math.Min(_anchorIndex, newIndex);
            var end = Math.Max(_anchorIndex, newIndex);
            _selectedItems.Clear();
            for (int i = start; i <= end; i++)
                _selectedItems.Add(_items[i]);
        }
        else if (!e.CtrlKey)
        {
            _selectedItems.Clear();
            _selectedItems.Add(_items[newIndex]);
            _anchorIndex = newIndex;
        }

        _focusedIndex = newIndex;
        await OnSelectionChanged.InvokeAsync(_selectedItems);
    }

    private void SelectAll()
    {
        _selectedItems.Clear();
        _selectedItems.AddRange(_items.Where(CanRead));
        _focusedIndex = _items.Count > 0 ? 0 : -1;
        _anchorIndex = _focusedIndex;
        OnSelectionChanged.InvokeAsync(_selectedItems);
    }

    // ── Toolbar actions ──────────────────────────────────────────

    private void OpenNewFolderForm()
    {
        if (Disabled || DataProvider is null) return;
        // Default name is set in GetNewFolderContext()
        _newFolderMetadata = TryCreateDefaultMetadata();
        _showNewFolderForm = true;
    }

    private static TMetadata? TryCreateDefaultMetadata()
    {
        try
        {
            return Activator.CreateInstance<TMetadata>();
        }
        catch
        {
            return null;
        }
    }

    private async Task SubmitNewFolderAsync()
    {
        if (DataProvider is null || _newFolderContext is null) return;

        var name = _newFolderContext.Name.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var newItem = await DataProvider.CreateFolderAsync(_currentPath, name, _newFolderContext.Metadata);
        _showNewFolderForm = false;
        _newFolderMetadata = null;
        _newFolderContext = null;
        await LoadDataAsync();

        var createdItem = _items.FirstOrDefault(i => i.Id == newItem.Id);
        if (createdItem is not null)
        {
            _selectedItems.Clear();
            _selectedItems.Add(createdItem);
            await OnSelectionChanged.InvokeAsync(_selectedItems);
        }
    }

    private Task CancelNewFolder()
    {
        _showNewFolderForm = false;
        _newFolderMetadata = null;
        _newFolderContext = null;
        return Task.CompletedTask;
    }

    private void OpenUploadForm()
    {
        if (Disabled || DataProvider is null || UploadForm is null) return;
        _uploadMetadata = TryCreateDefaultMetadata();
        _showUploadForm = true;
    }

    private async Task SubmitUploadAsync()
    {
        if (DataProvider is null || _uploadContext is null || _uploadContext.Files.Count == 0) return;

        var name = !string.IsNullOrWhiteSpace(_uploadContext.Name) ? _uploadContext.Name : null;
        var uploaded = await DataProvider.UploadAsync(_currentPath, _uploadContext.Files, metadata: _uploadContext.Metadata, name: name);
        _showUploadForm = false;
        _uploadMetadata = null;
        _uploadContext = null;
        await LoadDataAsync();

        if (uploaded.Count > 0)
        {
            var first = _items.FirstOrDefault(i => i.Id == uploaded[0].Id);
            if (first is not null)
            {
                _selectedItems.Clear();
                _selectedItems.Add(first);
                await OnSelectionChanged.InvokeAsync(_selectedItems);
            }
        }
    }

    private Task CancelUpload()
    {
        _showUploadForm = false;
        _uploadMetadata = null;
        _uploadContext = null;
        return Task.CompletedTask;
    }

    private UploadContext<TMetadata> GetUploadContext() => new()
    {
        Name = string.Empty,
        Files = [],
        Metadata = _uploadMetadata,
        OnSubmit = SubmitUploadAsync,
        OnCancel = CancelUpload
    };

    private void OpenAttachmentUploadForm(DocumentManagerItem<TMetadata> item)
    {
        if (Disabled || DataProvider is null || !AllowMultipleAttachments) return;
        _attachmentTargetItem = item;
        _attachmentUploadFiles = [];
        _showAttachmentUploadForm = true;
    }

    private async Task SubmitAttachmentUploadAsync()
    {
        if (DataProvider is null || _attachmentTargetItem is null || _attachmentUploadFiles.Count == 0) return;

        await DataProvider.AddAttachmentsAsync(_attachmentTargetItem.Id, _attachmentUploadFiles);

        // Refresh edit attachments if we're editing the same item
        if (_editingItem is not null && _editingItem.Id == _attachmentTargetItem.Id)
        {
            var refreshed = await DataProvider.GetItemDetailAsync(_attachmentTargetItem.Id);
            _editAttachments = refreshed.Attachments.ToList();
        }

        _showAttachmentUploadForm = false;
        _attachmentTargetItem = null;
        _attachmentUploadFiles = [];
        await LoadDataAsync();
    }

    private Task CancelAttachmentUpload()
    {
        DisposeStagedAttachmentStreams();
        _showAttachmentUploadForm = false;
        _attachmentTargetItem = null;
        _attachmentUploadFiles = [];
        return Task.CompletedTask;
    }

    private void DisposeStagedAttachmentStreams()
    {
        foreach (var f in _attachmentUploadFiles)
        {
            try { f.Stream.Dispose(); } catch { /* browser stream already closed */ }
        }
    }

    private async Task RemoveAttachmentAsync(string attachmentId)
    {
        if (DataProvider is null || _selectedItems.Count != 1) return;
        await DataProvider.RemoveAttachmentAsync(_selectedItems[0].Id, attachmentId);
        await LoadDataAsync();
    }

    private async Task DownloadAttachmentAsync(string attachmentId)
    {
        if (DataProvider is null || _selectedItems.Count != 1) return;
        var stream = await DataProvider.DownloadAttachmentAsync(_selectedItems[0].Id, attachmentId);
        var attachment = _selectedItems[0].Attachments.FirstOrDefault(a => a.Id == attachmentId);
        var fileName = attachment?.FileName ?? "download";
        using var streamRef = new DotNetStreamReference(stream);
        await JSRuntime.InvokeVoidAsync("TempoFileManager.downloadFileFromStream", fileName, streamRef);
    }

    private AttachmentListContext<TMetadata> GetAttachmentListContext(DocumentManagerItem<TMetadata> item)
    {
        return new AttachmentListContext<TMetadata>
        {
            Item = item,
            Attachments = item.Attachments,
            OnAddAttachment = async files =>
            {
                if (DataProvider is null) return;
                await DataProvider.AddAttachmentsAsync(item.Id, files);
                await LoadDataAsync();
            },
            OnRemoveAttachment = async attachmentId =>
            {
                if (DataProvider is null) return;
                await DataProvider.RemoveAttachmentAsync(item.Id, attachmentId);
                await LoadDataAsync();
            },
            OnDownloadAttachment = async attachmentId =>
            {
                if (DataProvider is null) return;
                var stream = await DataProvider.DownloadAttachmentAsync(item.Id, attachmentId);
                await using (stream) { }
            }
        };
    }

    private void DeleteSelectedAsync()
    {
        if (Disabled || _selectedItems.Count == 0 || !CanDeleteSelection()) return;
        _itemsToDelete.Clear();
        _itemsToDelete.AddRange(_selectedItems);

        if (DeleteForm is not null)
            _showCustomDeleteForm = true;
        else
            _showDeleteDialog = true;
    }

    private async Task ConfirmDeleteAsync()
    {
        if (DataProvider is null || _itemsToDelete.Count == 0) return;

        var ids = _itemsToDelete.Select(i => i.Id).ToList();
        await DataProvider.DeleteAsync(ids);
        _itemsToDelete.Clear();
        _selectedItems.Clear();
        _showDeleteDialog = false;
        _showCustomDeleteForm = false;
        await OnSelectionChanged.InvokeAsync(_selectedItems);
        await LoadDataAsync();
    }

    private Task CancelDelete()
    {
        _itemsToDelete.Clear();
        _showDeleteDialog = false;
        _showCustomDeleteForm = false;
        _deleteContext = null;
        return Task.CompletedTask;
    }

    private async Task HandleDeleteDialogResult(bool? result)
    {
        if (result == true)
            await ConfirmDeleteAsync();
        else
            CancelDelete();
    }

    private void StartRenameAsync()
    {
        if (Disabled || _selectedItems.Count != 1 || !CanRenameSelection()) return;
        _renamingItem = _selectedItems[0];
        _renameValue = _renamingItem.Name;
        _shouldFocusRenameInput = true;
    }

    private async Task CommitRenameAsync()
    {
        if (_renamingItem is null || DataProvider is null) return;

        var newName = _renameValue.Trim();
        if (!string.IsNullOrEmpty(newName) && newName != _renamingItem.Name)
        {
            await DataProvider.RenameAsync(_renamingItem.Id, newName);
            await LoadDataAsync();
        }

        _renamingItem = null;
        _renameValue = string.Empty;
    }

    private void CancelRename()
    {
        _renamingItem = null;
        _renameValue = string.Empty;
    }

    private async Task HandleRenameKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            await CommitRenameAsync();
        else if (e.Key == "Escape")
            CancelRename();
    }

    private async Task HandleFileSelectedAsync(InputFileChangeEventArgs e)
    {
        if (Disabled || DataProvider is null) return;

        var browserFiles = e.GetMultipleFiles(AllowMultipleAttachments ? int.MaxValue : 1);

        // Chunked path: upload each file individually through the provider's chunk sink.
        if (DataProvider is ITmChunkedFileProvider chunked
            && (DataProvider is not ITmCapabilityProvider<TmFileProviderCapabilities> cap
                || cap.Capabilities.HasFlag(TmFileProviderCapabilities.ChunkUpload)))
        {
            foreach (var file in browserFiles)
            {
                var item = new TmUploadItem
                {
                    FileName = file.Name,
                    TotalBytes = file.Size,
                    Source = file,
                    ResumeAction = it => ChunkUploadAsync(chunked, it)
                };
                _uploads.Add(item);
                await ChunkUploadAsync(chunked, item);
            }
            // Each ChunkUploadAsync already reloads (and re-applies scan verdicts) per file.
            return;
        }

        // Fallback: whole-stream upload (cap raised to MaxUploadSize).
        var files = new List<FileUploadInfo>();
        foreach (var file in browserFiles)
        {
            files.Add(new FileUploadInfo
            {
                FileName = file.Name,
                Size = file.Size,
                ContentType = file.ContentType,
                Stream = file.OpenReadStream(maxAllowedSize: MaxUploadSize)
            });
        }

        await DataProvider.UploadAsync(_currentPath, files);
        await LoadDataAsync();
    }

    private async Task ChunkUploadAsync(ITmChunkedFileProvider chunked, TmUploadItem item)
    {
        if (item.Source is null) return;
        var file = item.Source;

        item.State = TmUploadState.Uploading;
        item.Message = null;
        item.Cts = new CancellationTokenSource();
        var token = item.Cts.Token;
        StateHasChanged();

        var progress = new Progress<TmUploadProgress>(p =>
        {
            item.Apply(p);
            OnUploadProgress.InvokeAsync(p);
            StateHasChanged();
        });

        TmFileUploadResult result;
        try
        {
            result = await TmChunkedUploader.UploadBrowserFileAsync(
                file,
                chunked.UploadChunkAsync,
                MaxUploadSize,
                new TmChunkedUploadRequest
                {
                    FileName = file.Name,
                    ContentType = file.ContentType,
                    TotalSizeBytes = file.Size,
                    Purpose = "document-manager",
                    Metadata = new Dictionary<string, object> { ["folderPath"] = _currentPath },
                    ResumeFromChunkIndex = item.NextChunkIndex,
                    UploadSessionId = item.SessionId
                },
                progress,
                token);
        }
        catch (OperationCanceledException)
        {
            item.State = TmUploadState.Cancelled;
            StateHasChanged();
            return;
        }
        catch (Exception ex)
        {
            item.State = TmUploadState.Failed;
            item.Message = ex.Message;
            StateHasChanged();
            return;
        }

        if (!result.Success)
        {
            item.State = TmUploadState.Failed;
            item.Message = result.ErrorMessage;
            StateHasChanged();
            return;
        }

        var status = await ScanUploadAsync(item, file, result);
        item.State = status is FileScanStatus.Blocked ? TmUploadState.Blocked : TmUploadState.Completed;
        // LoadDataAsync re-applies recorded verdicts, so the gate survives the reload.
        await LoadDataAsync();
        StateHasChanged();
    }

    private async Task<FileScanStatus> ScanUploadAsync(TmUploadItem item, IBrowserFile file, TmFileUploadResult result)
    {
        if (ScanHook is null) return FileScanStatus.NotScanned;

        item.State = TmUploadState.Scanning;
        StateHasChanged();

        var scan = await ScanHook.ScanAsync(new FileScanRequest
        {
            FileName = file.Name,
            ContentType = file.ContentType,
            SizeBytes = file.Size,
            AssetId = result.AssetId,
            Purpose = "document-manager"
        });

        if (scan.Status is FileScanStatus.Blocked)
        {
            item.Message = scan.Message ?? scan.ThreatName;
        }
        RecordScanVerdict(result.AssetId, file.Name, scan);
        return scan.Status;
    }

    private void RecordScanVerdict(string? assetId, string fileName, FileScanResult scan)
    {
        if (scan.Status is FileScanStatus.NotScanned or FileScanStatus.Clean) return;
        var value = (scan.Status, scan.Message ?? scan.ThreatName);
        if (!string.IsNullOrEmpty(assetId)) _scanVerdicts[assetId!] = value;
        _scanVerdicts["name:" + fileName] = value;
    }

    private Task CancelUploadAsync(TmUploadItem item) => item.CancelAsync();

    private Task ResumeUploadAsync(TmUploadItem item) => item.ResumeAsync();

    private void DismissUpload(TmUploadItem item)
    {
        // The setter disposes the old CTS and clears the reference, so the public
        // CancellationToken getter keeps returning CancellationToken.None afterwards.
        item.Cts = null;
        _uploads.Remove(item);
    }

    // ── Version history ──────────────────────────────────────────

    private bool CanShowVersionHistory()
        => _selectedItems.Count == 1 && !_selectedItems[0].IsDirectory;

    private void OpenVersionHistory(DocumentManagerItem<TMetadata> item)
    {
        if (VersioningHook is null || item.IsDirectory) return;
        _versionHistoryItem = item;
        _showVersionHistory = true;
    }

    private void CloseVersionHistory()
    {
        _showVersionHistory = false;
        _versionHistoryItem = null;
    }

    private async Task OnVersionRestoredAsync(TmFileVersion version)
    {
        await LoadDataAsync();
        StateHasChanged();
    }

    private void HandleAttachmentFilesSelected(InputFileChangeEventArgs e)
    {
        _attachmentUploadFiles = [];
        foreach (var file in e.GetMultipleFiles(int.MaxValue))
        {
            _attachmentUploadFiles.Add(new FileUploadInfo
            {
                FileName = file.Name,
                Size = file.Size,
                ContentType = file.ContentType,
                Stream = file.OpenReadStream(maxAllowedSize: MaxUploadSize)
            });
        }
    }

    private async Task DownloadSelectedAsync()
    {
        if (DataProvider is null || _selectedItems.Count == 0) return;
        var file = _selectedItems.FirstOrDefault(i => !i.IsDirectory && CanDownload(i));
        if (file is null) return;

        var stream = await DataProvider.DownloadAsync(file.Id);
        var fileName = file.Attachments.FirstOrDefault()?.FileName ?? file.Name;
        using var streamRef = new DotNetStreamReference(stream);
        await JSRuntime.InvokeVoidAsync("TempoFileManager.downloadFileFromStream", fileName, streamRef);
    }

    private async Task DownloadAllSelectedAsync()
    {
        if (DataProvider is null || _selectedItems.Count != 1) return;
        var file = _selectedItems[0];
        if (file.IsDirectory || !CanDownloadAll(file)) return;

        var stream = await DataProvider.DownloadAllAttachmentsAsync(file.Id);
        var zipName = $"{file.Name}.zip";
        using var streamRef = new DotNetStreamReference(stream);
        await JSRuntime.InvokeVoidAsync("TempoFileManager.downloadFileFromStream", zipName, streamRef);
    }

    private async Task StartEditAsync()
    {
        if (Disabled || DataProvider is null || _selectedItems.Count != 1 || !CanEditSelection()) return;
        _editingItem = _selectedItems[0];
        _editMetadata = _editingItem.Metadata;
        _editAttachments = _editingItem.Attachments.ToList();
        _showEditForm = true;

        var refreshed = await DataProvider.GetItemDetailAsync(_selectedItems[0].Id);
        _editingItem = refreshed;
        _editMetadata = refreshed.Metadata;
        _editAttachments = refreshed.Attachments.ToList();
    }

    private async Task SubmitEditAsync()
    {
        if (_editingItem is null || DataProvider is null || _editContext?.Metadata is null) return;

        // Sync attachment removals made during editing
        var originalIds = _editingItem.Attachments.Select(a => a.Id).ToHashSet();
        var currentIds = _editAttachments.Select(a => a.Id).ToHashSet();
        foreach (var id in originalIds.Except(currentIds))
        {
            await DataProvider.RemoveAttachmentAsync(_editingItem.Id, id);
        }

        await DataProvider.UpdateMetadataAsync(_editingItem.Id, _editContext.Metadata);
        _showEditForm = false;
        _editingItem = null;
        _editMetadata = null;
        _editContext = null;
        _editAttachments = [];
        await LoadDataAsync();
    }

    private Task CancelEdit()
    {
        _showEditForm = false;
        _editingItem = null;
        _editMetadata = null;
        _editContext = null;
        _editAttachments = [];
        return Task.CompletedTask;
    }

    private void PromptAttachmentRemoval(string attachmentId)
    {
        _attachmentIdToDelete = attachmentId;
        _showAttachmentDeleteDialog = true;
    }

    private void StageAttachmentRemoval(string attachmentId)
    {
        _editAttachments.RemoveAll(a => a.Id == attachmentId);
    }

    private void HandleAttachmentDeleteDialogResult(bool? result)
    {
        if (result == true && _attachmentIdToDelete is not null)
            StageAttachmentRemoval(_attachmentIdToDelete);
        _attachmentIdToDelete = null;
        _showAttachmentDeleteDialog = false;
    }

    private async Task ShowDetailAsync(DocumentManagerItem<TMetadata> item)
    {
        if (DataProvider is null || DetailPanel is null) return;
        _detailItem = await DataProvider.GetItemDetailAsync(item.Id);
        _showDetailPanel = true;
    }

    private void CloseDetailPanel()
    {
        _showDetailPanel = false;
        _detailItem = null;
    }

    private async Task MoveSelectedAsync()
    {
        if (DataProvider is null || _selectedItems.Count == 0 || !CanMoveSelection()) return;
        // Placeholder: in a real implementation a target-folder picker would be shown
        var item = _selectedItems[0];
        await DataProvider.MoveAsync(item.Id, "/");
        await LoadDataAsync();
    }

    private async Task CopySelectedAsync()
    {
        if (DataProvider is null || _selectedItems.Count == 0 || !CanCopySelection()) return;
        var item = _selectedItems[0];
        await DataProvider.CopyAsync(item.Id, "/");
        await LoadDataAsync();
    }

    // ── Context menu ─────────────────────────────────────────────

    private void HandleContextMenu(DocumentManagerItem<TMetadata> item, MouseEventArgs e)
    {
        if (Disabled || ItemContextMenu is null || !CanRead(item)) return;
        _contextMenuItem = item;
        _contextMenuX = e.ClientX;
        _contextMenuY = e.ClientY;
        _showContextMenu = true;
    }

    private void CloseContextMenu()
    {
        _showContextMenu = false;
        _contextMenuItem = null;
    }

    private IReadOnlyList<string> GetAvailableActions(DocumentManagerItem<TMetadata> item)
    {
        var actions = new List<string>();
        actions.Add("open");
        if (CanRename(item)) actions.Add("rename");
        if (CanDelete(item)) actions.Add("delete");
        if (CanWrite(item) && EditForm is not null) actions.Add("edit");
        if (DetailPanel is not null) actions.Add("detail");
        if (!item.IsDirectory && CanDownload(item)) actions.Add("download");
        if (CanDownloadAll(item)) actions.Add("downloadAll");
        return actions;
    }

    private NewFolderContext<TMetadata> GetNewFolderContext() => new()
    {
        Name = "New Folder",
        Metadata = _newFolderMetadata,
        OnSubmit = SubmitNewFolderAsync,
        OnCancel = CancelNewFolder
    };

    private DeleteContext<TMetadata> GetDeleteContext() => new()
    {
        Items = _itemsToDelete,
        OnConfirm = ConfirmDeleteAsync,
        OnCancel = CancelDelete
    };

    private EditContext<TMetadata>? GetEditContext()
    {
        if (_editingItem is null) return null;
        return new EditContext<TMetadata>
        {
            Item = _editingItem,
            Metadata = _editMetadata,
            OnSubmit = SubmitEditAsync,
            OnCancel = CancelEdit
        };
    }

    private DetailContext<TMetadata>? GetDetailContext()
    {
        if (_detailItem is null) return null;
        return new DetailContext<TMetadata> { Item = _detailItem };
    }

    private ContextMenuContext<TMetadata>? GetContextMenuContext()
    {
        if (_contextMenuItem is null) return null;
        return new ContextMenuContext<TMetadata>
        {
            Item = _contextMenuItem,
            AvailableActions = GetAvailableActions(_contextMenuItem),
            OnActionSelected = HandleContextMenuActionAsync
        };
    }

    private async Task HandleContextMenuActionAsync(string action)
    {
        if (_contextMenuItem is null) return;
        var item = _contextMenuItem;
        CloseContextMenu();

        switch (action)
        {
            case "open":
                await OnItemDoubleClickAsync(item);
                break;
            case "rename":
                if (CanRename(item))
                {
                    _selectedItems.Clear();
                    _selectedItems.Add(item);
                    await OnSelectionChanged.InvokeAsync(_selectedItems);
                    StartRenameAsync();
                }
                break;
            case "delete":
                if (CanDelete(item))
                {
                    _selectedItems.Clear();
                    _selectedItems.Add(item);
                    await OnSelectionChanged.InvokeAsync(_selectedItems);
                    DeleteSelectedAsync();
                }
                break;
            case "edit":
                if (CanWrite(item))
                {
                    _selectedItems.Clear();
                    _selectedItems.Add(item);
                    await OnSelectionChanged.InvokeAsync(_selectedItems);
                    StartEditAsync();
                }
                break;
            case "detail":
                await ShowDetailAsync(item);
                break;
            case "download":
                if (CanDownload(item) && !item.IsDirectory)
                {
                    _selectedItems.Clear();
                    _selectedItems.Add(item);
                    await OnSelectionChanged.InvokeAsync(_selectedItems);
                    await DownloadSelectedAsync();
                }
                break;
            case "downloadAll":
                if (CanDownloadAll(item))
                {
                    _selectedItems.Clear();
                    _selectedItems.Add(item);
                    await OnSelectionChanged.InvokeAsync(_selectedItems);
                    await DownloadAllSelectedAsync();
                }
                break;
        }
    }

    // ── Render helpers ───────────────────────────────────────────

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_shouldFocusRenameInput)
        {
            _shouldFocusRenameInput = false;
            await _renameInputRef.FocusAsync();
        }

        if (_viewMode == DocumentManagerViewMode.Grid && _contentRef.Id is not null)
        {
            try
            {
                var cols = await JSRuntime.InvokeAsync<int>("TempoFileManager.getGridColumnCount", _contentRef);
                if (cols > 0)
                    _gridColumnCount = cols;
            }
            catch
            {
                // JS not available (e.g. bUnit)
            }
        }
    }

    private string GetFolderClass(DocumentManagerItem<TMetadata> folder)
    {
        var cls = "tm-file-manager__folder-item";
        if (folder.Path == _currentPath) cls += " tm-file-manager__folder-item--active";
        return cls;
    }

    private string GetItemClass(DocumentManagerItem<TMetadata> item)
    {
        var cls = "";
        if (_selectedItems.Contains(item)) cls += " tm-file-manager__item--selected";
        if (item.IsDirectory) cls += " tm-file-manager__item--folder";
        return cls.Trim();
    }

    private static string GetItemIcon(DocumentManagerItem<TMetadata> item)
    {
        if (!string.IsNullOrEmpty(item.IconName)) return item.IconName;
        if (item.IsDirectory) return "folder";
        return item.Extension.ToLowerInvariant() switch
        {
            ".pdf" => "file-text",
            ".doc" or ".docx" => "file-text",
            ".xls" or ".xlsx" => "table",
            ".ppt" or ".pptx" => "presentation",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" => "image",
            ".mp3" or ".wav" or ".ogg" => "music",
            ".mp4" or ".avi" or ".mov" => "video",
            ".zip" or ".rar" or ".7z" => "archive",
            ".txt" or ".md" or ".json" or ".xml" or ".csv" => "file-text",
            _ => "file"
        };
    }

    private static string FormatSize(long? bytes)
    {
        if (!bytes.HasValue) return "—";
        var b = bytes.Value;
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        return b switch
        {
            >= GB => $"{b / (double)GB:F2} GB",
            >= MB => $"{b / (double)MB:F2} MB",
            >= KB => $"{b / (double)KB:F2} KB",
            _ => $"{b} B"
        };
    }

    private static string GetParentPath(string itemPath)
    {
        itemPath = itemPath.TrimEnd('/');
        var lastSlash = itemPath.LastIndexOf('/');
        if (lastSlash <= 0) return "/";
        return itemPath.Substring(0, lastSlash);
    }
}
