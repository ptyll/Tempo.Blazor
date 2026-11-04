using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Page;

public partial class TmNotionPageSettingsMenu : TmComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded ─────────────────────────────────────────────────────────────

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    /// <summary>Page UI state: an open request from chrome (the mobile action bar) opens this menu.</summary>
    [CascadingParameter(Name = "TmNotionPageUiState")]
    private NotionPageUiState? UiState { get; set; }

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public INotionPage Page { get; set; } = default!;

    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Raised when page metadata (IsFullWidth, IsSmallText, IsLocked, IsFavorite) changes.</summary>
    [Parameter] public EventCallback<INotionPage> OnPageUpdated { get; set; }

    /// <summary>Raised after page is moved to trash — parent should navigate away.</summary>
    [Parameter] public EventCallback OnPageDeleted { get; set; }

    /// <summary>Raised when user picks "Page history".</summary>
    [Parameter] public EventCallback OnPageHistoryRequested { get; set; }

    /// <summary>Raised when user picks "Page info".</summary>
    [Parameter] public EventCallback OnPageInfoRequested { get; set; }

    /// <summary>Raised when user picks "Share".</summary>
    [Parameter] public EventCallback OnShareRequested { get; set; }

    /// <summary>Raised when user picks "Page restrictions".</summary>
    [Parameter] public EventCallback OnRestrictionsRequested { get; set; }

    /// <summary>Raised when user picks the AI page summary action.</summary>
    [Parameter] public EventCallback OnAISummarizeRequested { get; set; }

    /// <summary>Raised when user picks the AI page question action.</summary>
    [Parameter] public EventCallback OnAIAskPageRequested { get; set; }

    /// <summary>Raised after a successful import — contains new page ID.</summary>
    [Parameter] public EventCallback<string> OnNavigateToImportedPage { get; set; }

    // ── State ─────────────────────────────────────────────────────────────────

    private bool _open;
    private bool _exportOpen;
    private bool _importOpen;
    private bool _moveToOpen;
    private bool _deleteConfirm;
    private bool _exportIncludeSubpages;

    private bool _exportLoading;
    private bool _importLoading;
    private bool _deleteLoading;
    private bool _toggleLoading;
    private bool _favoriteLoading;
    private bool _moveLoading;

    private string _moveQuery         = string.Empty;
    private IReadOnlyList<INotionPage> _movePages = [];
    private bool _moveSearching;
    private CancellationTokenSource? _moveDebounceCts;

    private string _toastMessage = string.Empty;
    private bool   _toastIsError;
    private CancellationTokenSource _toastCts = new();

    private ElementReference _moveInputRef;
    private IBrowserFile?    _importFile;
    private InputFile?       _importFileRef;

    // ── Toggle / open ──────────────────────────────────────────────────────────

    protected override void OnInitialized()
    {
        if (UiState is not null)
            UiState.SettingsMenuRequested += OnSettingsMenuRequested;
    }

    private Task OnSettingsMenuRequested()
    {
        if (ReadOnly && !Page.IsLocked) return Task.CompletedTask;
        if (!_open)
            _open = true;
        _moveQuery = string.Empty;
        _movePages = [];
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task ToggleMenuAsync()
    {
        _open = !_open;
        if (!_open)
        {
            _exportOpen   = false;
            _importOpen   = false;
            _moveToOpen   = false;
            _deleteConfirm = false;
            _exportIncludeSubpages = false;
        }
        else
        {
            _moveQuery = string.Empty;
            _movePages = [];
        }
        StateHasChanged();

        if (_open && _moveToOpen)
            await FocusMoveInputAsync();
    }

    private void CloseMenu()
    {
        _open          = false;
        _exportOpen    = false;
        _importOpen    = false;
        _moveToOpen    = false;
        _deleteConfirm = false;
        _exportIncludeSubpages = false;
        StateHasChanged();
    }

    private void HandleMenuKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
            CloseMenu();
    }

    private async Task OpenPageInfoAsync()
    {
        CloseMenu();
        await OnPageInfoRequested.InvokeAsync();
    }

    private async Task OpenShareAsync()
    {
        CloseMenu();
        await OnShareRequested.InvokeAsync();
    }

    private async Task OpenRestrictionsAsync()
    {
        CloseMenu();
        await OnRestrictionsRequested.InvokeAsync();
    }

    private async Task OpenAISummarizeAsync()
    {
        CloseMenu();
        await OnAISummarizeRequested.InvokeAsync();
    }

    private async Task OpenAIAskPageAsync()
    {
        CloseMenu();
        await OnAIAskPageRequested.InvokeAsync();
    }

    // ── Toggles ───────────────────────────────────────────────────────────────

    private async Task ToggleFullWidthAsync()
    {
        if (_toggleLoading) return;
        _toggleLoading = true;
        StateHasChanged();

        var updated = MapMutable(Page);
        updated.IsFullWidth = !Page.IsFullWidth;

        try
        {
            await Context.DataProvider.UpdatePageAsync(updated);
            await OnPageUpdated.InvokeAsync(updated);
        }
        catch { await ShowToastErrorAsync(Loc["TmNotionPageSettingsMenu_ErrorUpdate"]); }
        finally { _toggleLoading = false; StateHasChanged(); }
    }

    private async Task ToggleSmallTextAsync()
    {
        if (_toggleLoading) return;
        _toggleLoading = true;
        StateHasChanged();

        var updated = MapMutable(Page);
        updated.IsSmallText = !Page.IsSmallText;

        try
        {
            await Context.DataProvider.UpdatePageAsync(updated);
            await OnPageUpdated.InvokeAsync(updated);
        }
        catch { await ShowToastErrorAsync(Loc["TmNotionPageSettingsMenu_ErrorUpdate"]); }
        finally { _toggleLoading = false; StateHasChanged(); }
    }

    private async Task ToggleLockAsync()
    {
        if (_toggleLoading) return;
        _toggleLoading = true;
        StateHasChanged();

        var updated = MapMutable(Page);
        updated.IsLocked = !Page.IsLocked;

        try
        {
            await Context.DataProvider.UpdatePageAsync(updated);
            await OnPageUpdated.InvokeAsync(updated);
        }
        catch { await ShowToastErrorAsync(Loc["TmNotionPageSettingsMenu_ErrorUpdate"]); }
        finally { _toggleLoading = false; StateHasChanged(); }
    }

    // ── Favorite ──────────────────────────────────────────────────────────────

    private async Task ToggleFavoriteAsync()
    {
        if (_favoriteLoading) return;
        _favoriteLoading = true;
        StateHasChanged();

        try
        {
            await Context.DataProvider.ToggleFavoriteAsync(Page.Id.ToString("D"), !Page.IsFavorite);
            var updated = MapMutable(Page);
            updated.IsFavorite = !Page.IsFavorite;
            await OnPageUpdated.InvokeAsync(updated);
            CloseMenu();
        }
        catch { await ShowToastErrorAsync(Loc["TmNotionPageSettingsMenu_ErrorFavorite"]); }
        finally { _favoriteLoading = false; StateHasChanged(); }
    }

    // ── Export ────────────────────────────────────────────────────────────────

    private void OpenExport()
    {
        _exportOpen = !_exportOpen;
        _importOpen = false;
        _moveToOpen = false;
        StateHasChanged();
    }

    private void HandleExportIncludeSubpagesChanged(ChangeEventArgs args)
    {
        _exportIncludeSubpages = args.Value switch
        {
            bool value => value,
            string value => bool.TryParse(value, out var parsed) && parsed,
            _ => false
        };
    }

    private async Task ExportAsync(NotionExportFormat format)
    {
        if (Context.ImportExportProvider is null || _exportLoading) return;
        _exportLoading = true;
        StateHasChanged();

        try
        {
            var pageId   = Page.Id.ToString("D");
            var stream   = _exportIncludeSubpages
                ? await Context.ImportExportProvider.ExportPageWithSubpagesAsync(pageId, format)
                : await Context.ImportExportProvider.ExportPageAsync(pageId, format);
            var ext      = GetExportExtension(format);
            var mime     = GetExportMimeType(format);
            var fileName = $"{SanitizeFileName(Page.Title)}_{DateTime.Now:yyyyMMdd}.{ext}";
            var dotNetStream = new DotNetStreamReference(stream);
            await JS.InvokeVoidAsync("tmNotionEditor.downloadFileStream", fileName, dotNetStream, mime);
            CloseMenu();
        }
        catch { await ShowToastErrorAsync(Loc["TmNotionPageSettingsMenu_ErrorExport"]); }
        finally { _exportLoading = false; StateHasChanged(); }
    }

    private static string GetExportExtension(NotionExportFormat format) => format switch
    {
        NotionExportFormat.Markdown => "md",
        NotionExportFormat.Html => "html",
        NotionExportFormat.Pdf => "pdf",
        NotionExportFormat.Docx => "docx",
        NotionExportFormat.Odt => "odt",
        _ => "bin"
    };

    private static string GetExportMimeType(NotionExportFormat format) => format switch
    {
        NotionExportFormat.Markdown => "text/markdown",
        NotionExportFormat.Html => "text/html",
        NotionExportFormat.Pdf => "application/pdf",
        NotionExportFormat.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        NotionExportFormat.Odt => "application/vnd.oasis.opendocument.text",
        _ => "application/octet-stream"
    };

    // ── Import ────────────────────────────────────────────────────────────────

    private void OpenImport()
    {
        _importOpen = !_importOpen;
        _exportOpen = false;
        _moveToOpen = false;
        StateHasChanged();
    }

    private void ImportFormatSelected(NotionImportFormat format)
    {
        _importFileFormat = format;
        StateHasChanged();
        TriggerFileInput();
    }

    private NotionImportFormat _importFileFormat = NotionImportFormat.Markdown;

    private void TriggerFileInput()
    {
        _ = JS.InvokeVoidAsync("eval",
            "document.querySelector('.tm-npsm__file-input')?.click()");
    }

    private async Task OnFileSelectedAsync(InputFileChangeEventArgs e)
    {
        if (Context.ImportExportProvider is null) return;
        var file = e.File;
        if (file is null) return;

        _importLoading = true;
        StateHasChanged();

        try
        {
            await using var stream  = file.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024);
            var newPage = await Context.ImportExportProvider.ImportAsync(
                stream,
                _importFileFormat,
                Page.ParentId?.ToString("D"));

            CloseMenu();
            await OnNavigateToImportedPage.InvokeAsync(newPage.Id.ToString("D"));
        }
        catch { await ShowToastErrorAsync(Loc["TmNotionPageSettingsMenu_ErrorImport"]); }
        finally { _importLoading = false; StateHasChanged(); }
    }

    // ── Copy link ─────────────────────────────────────────────────────────────

    private async Task CopyLinkAsync()
    {
        try
        {
            var url = await JS.InvokeAsync<string>("tmNotionEditor.getPageUrl", Page.Id.ToString("D"));
            await JS.InvokeVoidAsync("tmNotionEditor.copyToClipboard", url);
            await ShowToastSuccessAsync(Loc["TmNotionPageSettingsMenu_LinkCopied"]);
            CloseMenu();
        }
        catch { await ShowToastErrorAsync(Loc["TmNotionPageSettingsMenu_ErrorCopy"]); }
    }

    // ── Move to ───────────────────────────────────────────────────────────────

    private async Task OpenMoveToAsync()
    {
        _moveToOpen = !_moveToOpen;
        _exportOpen = false;
        _importOpen = false;
        _moveQuery  = string.Empty;
        _movePages  = [];
        StateHasChanged();

        if (_moveToOpen)
        {
            await SearchMoveTargetsAsync(string.Empty);
            await FocusMoveInputAsync();
        }
    }

    private async Task FocusMoveInputAsync()
    {
        try { await _moveInputRef.FocusAsync(); }
        catch { }
    }

    private async Task HandleMoveQueryAsync(ChangeEventArgs e)
    {
        _moveQuery = e.Value?.ToString() ?? string.Empty;
        _moveDebounceCts?.Cancel();
        _moveDebounceCts = new CancellationTokenSource();
        var token = _moveDebounceCts.Token;

        try
        {
            await Task.Delay(180, token);
            if (!token.IsCancellationRequested)
                await SearchMoveTargetsAsync(_moveQuery);
        }
        catch (TaskCanceledException) { }
    }

    private async Task SearchMoveTargetsAsync(string query)
    {
        if (Context.SearchProvider is null)
        {
            var allPages = (await Context.DataProvider.GetChildPagesAsync(null)).ToList();
            _movePages = allPages
                .Where(p => p.Id != Page.Id && (
                    string.IsNullOrEmpty(query) ||
                    p.Title.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .Take(10)
                .ToList();
            StateHasChanged();
            return;
        }

        _moveSearching = true;
        StateHasChanged();

        try
        {
            var pages = await Context.SearchProvider.SearchPagesAsync(query, null);
            _movePages = pages.Where(p => p.Id != Page.Id).Take(10).ToList();
        }
        catch { _movePages = []; }
        finally { _moveSearching = false; StateHasChanged(); }
    }

    private async Task MovePageAsync(INotionPage targetParent)
    {
        if (_moveLoading) return;
        _moveLoading = true;
        StateHasChanged();

        try
        {
            await Context.DataProvider.MovePageAsync(
                Page.Id.ToString("D"),
                targetParent.Id.ToString("D"));

            var updated = MapMutable(Page);
            updated.ParentId = targetParent.Id;
            await OnPageUpdated.InvokeAsync(updated);
            CloseMenu();
        }
        catch { await ShowToastErrorAsync(Loc["TmNotionPageSettingsMenu_ErrorMove"]); }
        finally { _moveLoading = false; StateHasChanged(); }
    }

    // ── Mark all comments as read ─────────────────────────────────────────────

    private async Task MarkAllCommentsAsReadAsync()
    {
        if (Context.CommentProvider is null) return;
        try
        {
            await Context.CommentProvider.MarkAllPageThreadsAsReadAsync(Page.Id.ToString("D"), Context.CurrentUserId);
            await ShowToastSuccessAsync(Loc["TmNotionPageSettingsMenu_MarkAllAsReadSuccess"]);
            CloseMenu();
        }
        catch { await ShowToastErrorAsync(Loc["TmNotionPageSettingsMenu_MarkAllAsReadError"]); }
    }

    // ── Page history ──────────────────────────────────────────────────────────

    private async Task OpenHistoryAsync()
    {
        CloseMenu();
        await OnPageHistoryRequested.InvokeAsync();
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    private void RequestDelete()
    {
        _deleteConfirm = true;
        _exportOpen    = false;
        _importOpen    = false;
        _moveToOpen    = false;
        StateHasChanged();
    }

    private void CancelDelete()
    {
        _deleteConfirm = false;
        StateHasChanged();
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_deleteLoading) return;
        _deleteLoading = true;
        StateHasChanged();

        try
        {
            await Context.DataProvider.DeletePageAsync(Page.Id.ToString("D"));
            CloseMenu();
            await OnPageDeleted.InvokeAsync();
        }
        catch { await ShowToastErrorAsync(Loc["TmNotionPageSettingsMenu_ErrorDelete"]); }
        finally { _deleteLoading = false; StateHasChanged(); }
    }

    // ── Toast helpers ─────────────────────────────────────────────────────────

    private async Task ShowToastSuccessAsync(string message)
    {
        _toastMessage = message;
        _toastIsError = false;
        StateHasChanged();
        await AutoDismissToastAsync();
    }

    private async Task ShowToastErrorAsync(string message)
    {
        _toastMessage = message;
        _toastIsError = true;
        StateHasChanged();
        await AutoDismissToastAsync();
    }

    private async Task AutoDismissToastAsync()
    {
        _toastCts.Cancel();
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;
        try
        {
            await Task.Delay(3000, token);
            _toastMessage = string.Empty;
            StateHasChanged();
        }
        catch (OperationCanceledException) { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetPageIcon(INotionPage p) =>
        string.IsNullOrEmpty(p.IconEmoji) ? "📄" : p.IconEmoji;

    private string GetPageTitle(INotionPage p) =>
        string.IsNullOrWhiteSpace(p.Title) ? Loc["TmNotionPageSettingsMenu_Untitled"] : p.Title;

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray())
               .Trim()
               .TrimEnd('.')
               .Replace(" ", "_");
    }

    private static NotionPage MapMutable(INotionPage src) => new()
    {
        Id                  = src.Id,
        ParentId            = src.ParentId,
        Title               = src.Title,
        Description         = src.Description,
        IconEmoji           = src.IconEmoji,
        IconImageUrl        = src.IconImageUrl,
        CoverImageUrl       = src.CoverImageUrl,
        CoverImagePositionY = src.CoverImagePositionY,
        IsFullWidth         = src.IsFullWidth,
        IsSmallText         = src.IsSmallText,
        IsLocked            = src.IsLocked,
        CreatedAt           = src.CreatedAt,
        CreatedByUserId     = src.CreatedByUserId,
        LastEditedAt        = src.LastEditedAt,
        LastEditedByUserId  = src.LastEditedByUserId,
        IsDeleted           = src.IsDeleted,
        DeletedAt           = src.DeletedAt,
        IsFavorite          = src.IsFavorite
    };

    // ── Dispose ───────────────────────────────────────────────────────────────

    public ValueTask DisposeAsync()
    {
        if (UiState is not null)
            UiState.SettingsMenuRequested -= OnSettingsMenuRequested;
        _moveDebounceCts?.Cancel();
        _moveDebounceCts?.Dispose();
        _toastCts.Cancel();
        _toastCts.Dispose();
        return ValueTask.CompletedTask;
    }
}
