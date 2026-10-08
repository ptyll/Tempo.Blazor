using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.TempoBlocks;

public partial class TmNotionWireframeBlock : ComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded context ─────────────────────────────────────────────────────

    [CascadingParameter]
    private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public IWireframeBlockContent? Content     { get; set; }
    [Parameter] public bool                    ReadOnly    { get; set; }

    [Parameter] public EventCallback<WireframeBlockContent> OnContentSaved { get; set; }
    [Parameter] public EventCallback                        OnFocused      { get; set; }
    [Parameter] public EventCallback                        OnRemoveRequested { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private bool                                      _creating;
    private bool                                      _editorOpen;
    private bool                                      _insertDialogOpen;
    private bool                                      _notFound;
    private string?                                   _effectivePreview;
    private Guid                                      _refreshedId = Guid.Empty;
    private Guid                                      _subscribedId = Guid.Empty;
    private bool                                      _subscriptionReady;
    private bool                                      _changeHandlerRegistered;
    private ElementReference                          _previewWrapRef;
    private ElementReference                          _captionRef;
    private DotNetObjectReference<TmNotionWireframeBlock>? _dotNetRef;
    private bool                                      _resizeInitialized;
    private bool                                      _captionDirty;
    private bool                                      _captionInitialized;
    private IWireframeBlockContent?                   _lastContent;

    // ── Computed ─────────────────────────────────────────────────────────────

    private string _sizeStyle
    {
        get
        {
            var parts = new System.Text.StringBuilder();
            if (Content?.Width is int w && w > 0)
                parts.Append($"width:{w}px;max-width:100%;");
            if (Content?.Height is int h && h > 0)
                parts.Append($"height:{h}px;");
            return parts.ToString();
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(Content, _lastContent)) return;
        _lastContent        = Content;
        _resizeInitialized  = false;
        _captionInitialized = false;
        _captionDirty       = false;
        _effectivePreview   = Content?.SvgPreviewCache;
        _notFound           = false;
    }

    protected override async Task OnParametersSetAsync()
    {
        // Stale-preview refresh: when this block links to a stored document, re-fetch its
        // current preview from the library so edits made elsewhere show up (and detect deletion).
        if (Content is not null
            && Content.WireframeDocumentId != Guid.Empty
            && Context.DocumentLibraryProvider is not null
            && _refreshedId != Content.WireframeDocumentId)
        {
            _refreshedId = Content.WireframeDocumentId;
            await RefreshPreviewAsync(Content.WireframeDocumentId);
        }

        // Subscribe independently of the refresh guard so inserted blocks (which pre-seed
        // _refreshedId to skip the immediate refetch) still receive live updates.
        await EnsureSubscribedAsync();
    }

    private async Task EnsureSubscribedAsync()
    {
        var notifier = Context.DocumentChangeNotifier;
        if (notifier is null || Content is null)
        {
            return;
        }

        var id = Content.WireframeDocumentId;
        if (id == _subscribedId)
        {
            return;
        }

        if (!_changeHandlerRegistered)
        {
            notifier.Changed += OnRemoteChangedAsync;
            _changeHandlerRegistered = true;
        }

        if (_subscribedId != Guid.Empty)
        {
            await notifier.UnsubscribeAsync(DocumentLibrary.TempoDocumentKind.Wireframe, _subscribedId);
            _subscriptionReady = false;
        }

        _subscribedId = id;
        if (id != Guid.Empty)
        {
            // Awaited so the DOM barrier (data-doclib-subscribed) flips only after the hub
            // join is confirmed — remote edits broadcast before that point would be lost.
            await notifier.SubscribeAsync(DocumentLibrary.TempoDocumentKind.Wireframe, id);
            _subscriptionReady = true;
        }
        else
        {
            _subscriptionReady = false;
        }
    }

    private async Task OnRemoteChangedAsync(DocumentLibrary.TempoDocumentChange change, CancellationToken ct)
    {
        if (change.Kind != DocumentLibrary.TempoDocumentKind.Wireframe || change.DocumentId != _subscribedId)
        {
            return;
        }

        await InvokeAsync(async () =>
        {
            _refreshedId = Guid.Empty; // force re-fetch
            await RefreshPreviewAsync(change.DocumentId);
        });
    }

    private async Task RefreshPreviewAsync(Guid documentId)
    {
        DocumentLibrary.DocumentLibraryEntry? entry;
        try
        {
            entry = await Context.DocumentLibraryProvider!.GetEntryAsync(
                DocumentLibrary.TempoDocumentKind.Wireframe, documentId);
        }
        catch
        {
            return;
        }

        if (entry is null)
        {
            _notFound = true;
            StateHasChanged();
            return;
        }

        if (!string.IsNullOrEmpty(entry.PreviewSvg) && entry.PreviewSvg != _effectivePreview)
        {
            _effectivePreview = entry.PreviewSvg;
            StateHasChanged();

            // Persist the refreshed preview only on an editable page.
            if (!ReadOnly && Content is not null)
            {
                await OnContentSaved.InvokeAsync(new WireframeBlockContent
                {
                    WireframeDocumentId = Content.WireframeDocumentId,
                    SvgPreviewCache     = entry.PreviewSvg,
                    Width               = Content.Width,
                    Height              = Content.Height,
                    Caption             = Content.Caption
                });
            }
        }
    }

    private async Task HandleInsertSelectedAsync(DocumentOpenResult result)
    {
        _insertDialogOpen = false;
        if (result.Mode == DocumentOpenMode.Copy)
        {
            await InsertCopyAsync(result);
        }
        else
        {
            await InsertLinkAsync(result);
        }
    }

    private async Task InsertLinkAsync(DocumentOpenResult result)
    {
        string? preview = null;
        if (Context.DocumentLibraryProvider is not null)
        {
            var entry = await Context.DocumentLibraryProvider.GetEntryAsync(
                DocumentLibrary.TempoDocumentKind.Wireframe, result.DocumentId);
            preview = entry?.PreviewSvg;
        }

        _refreshedId = result.DocumentId; // already fresh; skip immediate re-refresh
        _effectivePreview = preview;
        await OnContentSaved.InvokeAsync(new WireframeBlockContent
        {
            WireframeDocumentId = result.DocumentId,
            SvgPreviewCache     = preview
        });
    }

    private async Task InsertCopyAsync(DocumentOpenResult result)
    {
        if (Context.WireframeDocumentProvider is null)
        {
            // No document provider to copy through — fall back to linking.
            await InsertLinkAsync(result);
            return;
        }

        var source = await Context.WireframeDocumentProvider.GetWireframeDocumentAsync(result.DocumentId);
        if (source is null)
        {
            return;
        }

        // Deep copy via serialization round-trip so the copy is fully independent.
        var copy = WireframeSerializer.Deserialize(WireframeSerializer.Serialize(source));
        var (newId, _) = await Context.WireframeDocumentProvider.CreateWireframeDocumentAsync(copy.Title);
        await Context.WireframeDocumentProvider.SaveWireframeDocumentAsync(newId, copy);

        string? preview = null;
        if (Context.DocumentLibraryProvider is not null)
        {
            var entry = await Context.DocumentLibraryProvider.GetEntryAsync(
                DocumentLibrary.TempoDocumentKind.Wireframe, result.DocumentId);
            preview = entry?.PreviewSvg;
        }

        _refreshedId = newId;
        _effectivePreview = preview;
        await OnContentSaved.InvokeAsync(new WireframeBlockContent
        {
            WireframeDocumentId = newId,
            SvgPreviewCache     = preview
        });
    }

    private async Task HandleRemoveAsync() => await OnRemoveRequested.InvokeAsync();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (ReadOnly || Content is null || Content.WireframeDocumentId == Guid.Empty) return;

        if (!_resizeInitialized)
        {
            _resizeInitialized = true;
            _dotNetRef?.Dispose();
            _dotNetRef = DotNetObjectReference.Create(this);
            try { await JS.InvokeVoidAsync("tmNotionEditor.initResizeHandle", _previewWrapRef, _dotNetRef); }
            catch { }
        }

        if (!_captionInitialized)
        {
            _captionInitialized = true;
            var caption = Content.Caption ?? string.Empty;
            if (!string.IsNullOrEmpty(caption))
            {
                try { await JS.InvokeVoidAsync("tmNotionEditor.setHtml", _captionRef, caption); }
                catch { }
            }
        }
    }

    // ── JS callback ───────────────────────────────────────────────────────────

    [JSInvokable]
    public async Task OnResize(int width, int height)
    {
        if (Content is null) return;
        var updated = new WireframeBlockContent
        {
            WireframeDocumentId = Content.WireframeDocumentId,
            SvgPreviewCache     = Content.SvgPreviewCache,
            Width               = width,
            Height              = height,
            Caption             = Content.Caption
        };
        await OnContentSaved.InvokeAsync(updated);
    }

    // ── Create / Edit ────────────────────────────────────────────────────────

    private async Task CreateWireframeAsync()
    {
        if (_creating) return;
        _creating = true;
        StateHasChanged();
        try
        {
            Guid id;
            if (Context.WireframeDocumentProvider is not null)
            {
                var (newId, _) = await Context.WireframeDocumentProvider.CreateWireframeDocumentAsync(string.Empty);
                id = newId;
            }
            else
            {
                id = Guid.NewGuid();
            }
            var created = new WireframeBlockContent { WireframeDocumentId = id };
            await OnContentSaved.InvokeAsync(created);
            _editorOpen = true;
        }
        catch { }
        finally
        {
            _creating = false;
        }
    }

    private Task OpenEditorAsync()
    {
        _editorOpen = true;
        return Task.CompletedTask;
    }

    private async Task HandleEditorSavedAsync((WireframeDocument Document, string SvgPreview) result)
    {
        _editorOpen = false;
        if (Content is null) return;
        if (Context.WireframeDocumentProvider is not null)
        {
            try
            {
                await Context.WireframeDocumentProvider.SaveWireframeDocumentAsync(
                    Content.WireframeDocumentId, result.Document);
            }
            catch { }
        }
        var updated = new WireframeBlockContent
        {
            WireframeDocumentId = Content.WireframeDocumentId,
            SvgPreviewCache     = result.SvgPreview,
            Width               = Content.Width,
            Height              = Content.Height,
            Caption             = Content.Caption
        };
        await OnContentSaved.InvokeAsync(updated);
    }

    private Task HandleEditorDiscardedAsync()
    {
        _editorOpen = false;
        return Task.CompletedTask;
    }

    // ── Caption ───────────────────────────────────────────────────────────────

    private async Task OnCaptionBlurAsync()
    {
        if (!_captionDirty || ReadOnly || Content is null) return;
        _captionDirty = false;
        try
        {
            var html    = await JS.InvokeAsync<string>("tmNotionEditor.getHtml", _captionRef);
            var updated = new WireframeBlockContent
            {
                WireframeDocumentId = Content.WireframeDocumentId,
                SvgPreviewCache     = Content.SvgPreviewCache,
                Width               = Content.Width,
                Height              = Content.Height,
                Caption             = string.IsNullOrWhiteSpace(html) ? null : html
            };
            await OnContentSaved.InvokeAsync(updated);
        }
        catch { }
    }

    // ── Focus ─────────────────────────────────────────────────────────────────

    private async Task HandleFocusAsync() => await OnFocused.InvokeAsync();

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        var notifier = Context?.DocumentChangeNotifier;
        if (notifier is not null)
        {
            if (_changeHandlerRegistered)
            {
                notifier.Changed -= OnRemoteChangedAsync;
            }
            if (_subscribedId != Guid.Empty)
            {
                try { await notifier.UnsubscribeAsync(DocumentLibrary.TempoDocumentKind.Wireframe, _subscribedId); }
                catch { }
            }
        }

        if (_resizeInitialized)
        {
            try { await JS.InvokeVoidAsync("tmNotionEditor.destroyResizeHandle", _previewWrapRef); }
            catch { }
        }
        _dotNetRef?.Dispose();
    }
}
