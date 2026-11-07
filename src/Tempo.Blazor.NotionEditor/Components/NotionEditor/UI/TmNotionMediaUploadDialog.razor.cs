using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionMediaUploadDialog : ComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded context ─────────────────────────────────────────────────────

    [CascadingParameter]
    private NotionEditorContext? Context { get; set; }

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public bool   IsOpen    { get; set; }

    /// <summary>"image" | "video" | "audio" | "pdf" | "file"</summary>
    [Parameter] public string MediaType { get; set; } = "file";

    [Parameter] public EventCallback<(string? FileId, string? Url)> OnConfirmed { get; set; }
    [Parameter] public EventCallback                                  OnCancelled { get; set; }

    /// <summary>Forced layout. Mobile presents the dialog as the core sheet.</summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The cascaded viewport. Auto sheets when the viewport is mobile.</summary>
    [CascadingParameter(Name = TmLayoutScopes.Viewport)] public TmLayoutContext? Layout { get; set; }

    private TmLayoutMode ResolvedLayout =>
        LayoutMode != TmLayoutMode.Auto ? LayoutMode : Layout?.Resolved ?? TmLayoutMode.Auto;

    // ── State ────────────────────────────────────────────────────────────────

    private string  _activeTab   = "upload";
    private bool    _isDragging;
    private bool    _isUploading;
    private string? _uploadError;
    private string  _embedUrl    = string.Empty;
    private IBrowserFile? _selectedFile;

    private bool _focusTrapInitialized;
    private bool _wasOpen;
    private ElementReference _dialogRef;
    private ElementReference _urlInputRef;

    // ── Library state ─────────────────────────────────────────────────────────

    private List<INotionMediaLibraryItem> _libraryItems     = [];
    private string                        _libraryQuery     = string.Empty;
    private bool                          _isLoadingLibrary;
    private string?                       _libraryError;
    private int                           _librarySkip;
    private bool                          _libraryHasMore;
    private bool                          _libraryLoaded;
    private CancellationTokenSource?      _libraryCts;

    private const int LibraryPageSize = 24;

    // ── Computed ─────────────────────────────────────────────────────────────

    private bool CanUpload         => Context?.FileProvider         is not null;
    private bool CanBrowseLibrary  => Context?.MediaLibraryProvider is not null;

    private string _dialogTitle => MediaType switch
    {
        "image" => Loc["TmNotionMediaUploadDialog_TitleImage"],
        "video" => Loc["TmNotionMediaUploadDialog_TitleVideo"],
        "audio" => Loc["TmNotionMediaUploadDialog_TitleAudio"],
        "pdf"   => Loc["TmNotionMediaUploadDialog_TitlePdf"],
        _       => Loc["TmNotionMediaUploadDialog_TitleFile"]
    };

    private string SupportedFormats => MediaType switch
    {
        "image" => "PNG, JPEG, GIF, WebP, SVG",
        "video" => "MP4, WebM",
        "audio" => "MP3, WAV, OGG",
        "pdf"   => "PDF",
        _       => Loc["TmNotionMediaUploadDialog_TabUpload"],
    };

    private string _acceptTypes => MediaType switch
    {
        "image" => "image/*",
        "video" => "video/*",
        "audio" => "audio/*",
        "pdf"   => "application/pdf",
        _       => "*/*"
    };

    private string? _libraryMediaType => MediaType switch
    {
        "image" => "image",
        "pdf"   => "pdf",
        "file"  => "file",
        _       => null
    };

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        if (!IsOpen)
        {
            _wasOpen = false;
            _focusTrapInitialized = false;
            ResetLibraryState();
            return;
        }

        if (_wasOpen)
        {
            return;
        }

        _wasOpen      = true;
        _uploadError  = null;
        _embedUrl     = string.Empty;
        _isDragging   = false;
        _activeTab    = CanUpload ? "upload" : CanBrowseLibrary ? "library" : "embed";
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsOpen && !_focusTrapInitialized)
        {
            _focusTrapInitialized = true;
            try { await JS.InvokeVoidAsync("tmNotionEditor.initFocusTrap", _dialogRef); }
            catch { }
        }

        if (IsOpen && _activeTab == "embed")
        {
            try { await JS.InvokeVoidAsync("tmNotionEditor.focus", _urlInputRef); }
            catch { }
        }

        if (IsOpen && _activeTab == "library" && !_libraryLoaded && !_isLoadingLibrary)
        {
            await LoadLibraryAsync(reset: true);
        }
    }

    private void SetTab(string tab)
    {
        _activeTab = tab;
        // A click only queues the load. OnAfterRender owns the await, so a second click (or a
        // search keystroke) cannot start a second request that the first one's reset wipes.
    }

    private static readonly string[] TabOrder = ["upload", "embed", "library"];

    private Task HandleTabKeyAsync(KeyboardEventArgs e)
    {
        var tab = _activeTab;
        var delta = e.Key switch
        {
            "ArrowRight" or "ArrowDown" => 1,
            "ArrowLeft" or "ArrowUp" => -1,
            _ => 0,
        };
        if (delta == 0) return Task.CompletedTask;

        var visible = TabOrder.Where(candidate => candidate switch
        {
            "upload" => CanUpload,
            "library" => CanBrowseLibrary,
            _ => true,
        }).ToList();
        var index = visible.IndexOf(tab);
        if (index < 0) return Task.CompletedTask;

        SetTab(visible[(index + delta + visible.Count) % visible.Count]);
        return Task.CompletedTask;
    }

    private void ResetLibraryState()
    {
        _libraryItems     = [];
        _libraryQuery     = string.Empty;
        _librarySkip      = 0;
        _libraryHasMore   = false;
        _libraryLoaded    = false;
        _libraryError     = null;
        _libraryCts?.Cancel();
        _libraryCts?.Dispose();
        _libraryCts = null;
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    private async Task HandleFileSelectedAsync(InputFileChangeEventArgs e)
    {
        if (Context?.FileProvider is null) return;
        var file = e.File;
        _selectedFile = file;
        var validationKey = NotionMediaUploadValidation.Validate(MediaType, file.Name, file.ContentType, file.Size);
        if (!string.IsNullOrEmpty(validationKey))
        {
            _uploadError = Loc[validationKey, NotionMediaUploadValidation.FormatMaxFileSize()];
            _isUploading = false;
            StateHasChanged();
            return;
        }

        _isUploading = true;
        _uploadError = null;
        StateHasChanged();
        try
        {
            await using var stream = file.OpenReadStream(maxAllowedSize: NotionMediaUploadValidation.MaxFileSizeBytes);
            var media = await Context.FileProvider.UploadNotionFileAsync(stream, file.Name, file.ContentType);
            await OnConfirmed.InvokeAsync((media.AssetId, media.Url));
        }
        catch
        {
            _uploadError = Loc["TmNotionMediaUploadDialog_UploadError"];
        }
        finally
        {
            _isUploading = false;
            StateHasChanged();
        }
    }

    // ── Embed ─────────────────────────────────────────────────────────────────

    private async Task HandleEmbedConfirmAsync()
    {
        var url = _embedUrl.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;
        await OnConfirmed.InvokeAsync((null, url));
    }

    private async Task HandleUrlKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await HandleEmbedConfirmAsync();
    }

    private async Task HandleCancelAsync() => await OnCancelled.InvokeAsync();

    private void ClearSelectedFile() => _selectedFile = null;

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024):0.#} MB";
    }

    // ── Library ───────────────────────────────────────────────────────────────

    private async Task OnLibrarySearchChangedAsync(ChangeEventArgs e)
    {
        _libraryQuery = e.Value?.ToString() ?? string.Empty;
        await LoadLibraryAsync(reset: true);
    }

    private async Task LoadLibraryAsync(bool reset)
    {
        if (Context?.MediaLibraryProvider is null) return;

        _libraryCts?.Cancel();
        _libraryCts?.Dispose();
        _libraryCts = new CancellationTokenSource();
        var ct = _libraryCts.Token;

        if (reset)
        {
            _libraryItems   = [];
            _librarySkip    = 0;
            _libraryHasMore = false;
        }

        _isLoadingLibrary = true;
        _libraryError     = null;
        StateHasChanged();

        try
        {
            if (reset)
                await Task.Delay(300, ct); // debounce on search change

            var items = await Context.MediaLibraryProvider.SearchAsync(
                _libraryQuery, _libraryMediaType, _librarySkip, LibraryPageSize, ct);

            var list = items.ToList();
            _libraryItems.AddRange(list);
            _librarySkip   += list.Count;
            _libraryHasMore = list.Count == LibraryPageSize;
            _libraryLoaded  = true;
        }
        catch (OperationCanceledException) { return; }
        catch
        {
            // A failed load is still an attempt. Leaving _libraryLoaded false would make
            // OnAfterRender ask again on every render and spin the request.
            _libraryLoaded = true;
            _libraryError = Loc["TmNotionMediaUploadDialog_LibraryError"];
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                _isLoadingLibrary = false;
                StateHasChanged();
            }
        }
    }

    private INotionMediaLibraryItem? _selectedLibraryItem;

    private void SelectLibraryItem(INotionMediaLibraryItem item) => _selectedLibraryItem = item;

    private async Task InsertSelectedAsync()
    {
        if (_selectedLibraryItem is not null)
        {
            await OnConfirmed.InvokeAsync((_selectedLibraryItem.Id, _selectedLibraryItem.Url));
            return;
        }

        if (!string.IsNullOrWhiteSpace(_embedUrl))
        {
            await HandleEmbedConfirmAsync();
            return;
        }

        if (_selectedFile is not null)
            await UploadSelectedFileAsync(_selectedFile);
    }

    private async Task UploadSelectedFileAsync(IBrowserFile file)
    {
        if (Context?.FileProvider is null) return;

        _isUploading = true;
        _uploadError = null;
        try
        {
            await using var stream = file.OpenReadStream(maxAllowedSize: NotionMediaUploadValidation.MaxFileSizeBytes);
            var media = await Context.FileProvider.UploadNotionFileAsync(stream, file.Name, file.ContentType);
            await OnConfirmed.InvokeAsync((media.AssetId, media.Url));
        }
        catch
        {
            _uploadError = Loc["TmNotionMediaUploadDialog_UploadError"];
        }
        finally
        {
            _isUploading = false;
        }
    }

    private async Task HandleLibraryItemSelectedAsync(INotionMediaLibraryItem item)
    {
        SelectLibraryItem(item);
        await OnConfirmed.InvokeAsync((item.Id, item.Url));
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _libraryCts?.Cancel();
        _libraryCts?.Dispose();

        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.destroyFocusTrap", _dialogRef);
        }
        catch
        {
        }
    }
}
