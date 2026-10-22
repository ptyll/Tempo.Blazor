using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Text.Json;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Toolbar;

namespace Tempo.Blazor.Components.Files;

/// <summary>Active tool of the PDF annotator.</summary>
public enum PdfAnnotatorMode
{
    /// <summary>Select and browse existing annotations.</summary>
    Browse,

    /// <summary>Select text to create a highlight annotation.</summary>
    Highlight,

    /// <summary>Click a page location to start a comment thread.</summary>
    Comment,

    /// <summary>Click a page location to place a stamp.</summary>
    Stamp,

    /// <summary>Draw freehand ink strokes on the page.</summary>
    Draw
}

/// <summary>Pending, not-yet-saved annotation created by an annotator tool.</summary>
public sealed class PdfAnnotatorDraft
{
    /// <summary>Annotation kind of the draft.</summary>
    public DocumentAnnotationKind Kind { get; init; }

    /// <summary>Anchor of the draft, when already known.</summary>
    public DocumentCommentAnchor? Anchor { get; set; }

    /// <summary>Completed ink strokes of a drawing draft.</summary>
    public List<DocumentInkStroke> Strokes { get; } = [];
}

/// <summary>
/// Standalone PDF annotation component built on top of <see cref="TmPdfViewer"/>.
/// Provides highlight, comment, stamp, and freehand drawing tools, a comment thread
/// panel with resolve/reopen, per-author or per-role annotation colors, export of
/// annotations into the PDF (optionally flattened), and a print variant.
/// Persistence is pluggable through <see cref="IPdfAnnotationProvider"/>.
/// </summary>
public partial class TmPdfAnnotator : TmComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>URL of the PDF document to annotate.</summary>
    [Parameter] public string? Url { get; set; }

    /// <summary>Stable document identifier used by the annotation provider. Falls back to <see cref="Url"/>.</summary>
    [Parameter] public string? DocumentId { get; set; }

    /// <summary>Height of the annotator (CSS value). Default is "600px". The root owns the
    /// height contract; the embedded viewer fills it and shrinks inside it on a mobile layout.</summary>
    [Parameter] public string? Height { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Provider used to load and persist annotation threads. When omitted, an in-memory
    /// provider keeps annotations for the lifetime of the component.
    /// </summary>
    [Parameter] public IPdfAnnotationProvider? AnnotationProvider { get; set; }

    /// <summary>Author applied to annotations created in this annotator.</summary>
    [Parameter] public DocumentCommentUser? CurrentUser { get; set; }

    /// <summary>Known users, used to resolve author roles for role-based colors.</summary>
    [Parameter] public IReadOnlyList<DocumentCommentUser>? Users { get; set; }

    /// <summary>Annotation colors per author, keyed by user id.</summary>
    [Parameter] public IReadOnlyDictionary<string, string>? AuthorColors { get; set; }

    /// <summary>Annotation colors per role, keyed by role.</summary>
    [Parameter] public IReadOnlyDictionary<string, string>? RoleColors { get; set; }

    /// <summary>
    /// Explicit color (CSS value) stored with annotations created in this annotator.
    /// When null (default) colors resolve per author/role at display time. Supports two-way
    /// binding: the runtime color picker (FZE-12) raises <see cref="AnnotationColorChanged"/>,
    /// and a host-supplied value seeds the picker's selection.
    /// </summary>
    [Parameter] public string? AnnotationColor { get; set; }

    /// <summary>
    /// Callback invoked when the runtime color picker changes the color for NEW annotations
    /// (a pick raises the color, the reset raises <c>null</c> — the per-author/role fallback).
    /// </summary>
    [Parameter] public EventCallback<string?> AnnotationColorChanged { get; set; }

    /// <summary>
    /// Colors offered by the runtime color picker. Defaults to
    /// <see cref="PdfAnnotationColorHelper.DefaultPalette"/> (the helper owns the palette data).
    /// </summary>
    [Parameter] public IReadOnlyList<string>? ColorPalette { get; set; }

    /// <summary>
    /// Accessible names for the <see cref="ColorPalette"/> colors, keyed by the color string
    /// (matched case-insensitively, like the core <see cref="TmColorPalette"/>). Defaults to
    /// the localized <c>TmPdfAnnotator_ColorName_*</c> names of the default palette.
    /// </summary>
    [Parameter] public IReadOnlyDictionary<string, string>? ColorNames { get; set; }

    /// <summary>
    /// Whether the runtime color picker is offered. Default true; when false the toolbar keeps
    /// the pre-phase-12 static chip + label indicator and no palette tile renders on mobile.
    /// </summary>
    [Parameter] public bool AllowColorPicker { get; set; } = true;

    /// <summary>Active tool. Supports two-way binding. Default is <see cref="PdfAnnotatorMode.Browse"/>.</summary>
    [Parameter] public PdfAnnotatorMode Mode { get; set; } = PdfAnnotatorMode.Browse;

    /// <summary>Callback invoked when the active tool changes.</summary>
    [Parameter] public EventCallback<PdfAnnotatorMode> ModeChanged { get; set; }

    /// <summary>Stamp texts offered in stamp mode. Defaults to localized Approved/Rejected/Draft/Confidential.</summary>
    [Parameter] public IReadOnlyList<string>? StampOptions { get; set; }

    /// <summary>Whether resolved annotation threads are shown by default. Default is false.</summary>
    [Parameter] public bool ShowResolved { get; set; }

    /// <summary>Callback invoked when the resolved filter toggles.</summary>
    [Parameter] public EventCallback<bool> ShowResolvedChanged { get; set; }

    /// <summary>Whether the thread panel is rendered. Default is true.</summary>
    [Parameter] public bool ShowPanel { get; set; } = true;

    /// <summary>Callback invoked when the panel's close button requests to hide the panel
    /// (two-way <see cref="ShowPanel"/> binding).</summary>
    [Parameter] public EventCallback<bool> ShowPanelChanged { get; set; }

    /// <summary>
    /// Kept for backward compatibility. When true (default) the annotator has no toolbar of
    /// its own: its tool groups merge into the embedded viewer's single toolbar through the
    /// viewer's <c>ToolbarStart</c>/<c>ToolbarEnd</c> slots, and the export group lives in the
    /// header row when <see cref="ShowHeader"/> is true. When false the viewer's chrome
    /// (navigation, zoom, rotation) is hidden and the annotator renders a compact tools row
    /// of its own instead.
    /// </summary>
    [Parameter] public bool ShowViewerToolbar { get; set; } = true;

    /// <summary>Whether the "Open in new tab" link is rendered (in the merged toolbar, or in
    /// the header row when <see cref="ShowHeader"/> is true). Default is true.</summary>
    [Parameter] public bool ShowOpenLink { get; set; } = true;

    /// <summary>Whether the viewer's thumbnails sidebar is offered. Default is false; passed through to the embedded viewer.</summary>
    [Parameter] public bool ShowThumbnails { get; set; }

    /// <summary>Whether the viewer's search is offered. Default is false; passed through to the embedded viewer.</summary>
    [Parameter] public bool ShowSearch { get; set; }

    /// <summary>
    /// How the embedded viewer presents its search UI on desktop and tablet layouts.
    /// Default <see cref="PdfSearchPresentation.Floating"/> (a card anchored over the page
    /// body); passed through to the embedded viewer. A mobile layout always keeps the
    /// in-flow bar.
    /// </summary>
    [Parameter] public PdfSearchPresentation SearchPresentation { get; set; } = PdfSearchPresentation.Floating;

    /// <summary>Whether the viewer's rotation button is offered. Default is false; passed through to the embedded viewer.</summary>
    [Parameter] public bool AllowRotation { get; set; }

    /// <summary>Whether the thumbnails sidebar is collapsed. Supports two-way binding; the annotator tracks the state so an unbound host keeps the user's choice. Default is false.</summary>
    [Parameter] public bool ThumbnailsCollapsed { get; set; }

    /// <summary>Callback invoked when the thumbnails collapse state changes in the viewer.</summary>
    [Parameter] public EventCallback<bool> ThumbnailsCollapsedChanged { get; set; }

    /// <summary>How the viewer's zoom scale is decided. Supports two-way binding; passed through to the embedded viewer. Default is <see cref="PdfZoomMode.Auto"/>, consistent with the viewer (fit-width on a mobile layout, the bound scale everywhere else).</summary>
    [Parameter] public PdfZoomMode ZoomMode { get; set; } = PdfZoomMode.Auto;

    /// <summary>Callback when the zoom mode changes in the viewer.</summary>
    [Parameter] public EventCallback<PdfZoomMode> ZoomModeChanged { get; set; }

    /// <summary>Whether a header row (title, subtitle, export group and host actions) is rendered above the merged toolbar. Default is false.</summary>
    [Parameter] public bool ShowHeader { get; set; }

    /// <summary>Document title rendered in the header row. Used only when <see cref="ShowHeader"/> is true.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Document subtitle rendered in the header row. Used only when <see cref="ShowHeader"/> is true; when omitted the viewer shows a neutral page count.</summary>
    [Parameter] public string? Subtitle { get; set; }

    /// <summary>Content rendered at the start of the header row. Used only when <see cref="ShowHeader"/> is true.</summary>
    [Parameter] public RenderFragment? HeaderStart { get; set; }

    /// <summary>Extra host actions rendered at the end of the header row, after the export group. Used only when <see cref="ShowHeader"/> is true.</summary>
    [Parameter] public RenderFragment? HeaderActions { get; set; }

    /// <summary>Whether export and print actions are offered. Default is true.</summary>
    [Parameter] public bool AllowExport { get; set; } = true;

    /// <summary>File name used for the exported PDF. Defaults to the source name with an "-annotated" suffix.</summary>
    [Parameter] public string? ExportFileName { get; set; }

    /// <summary>Callback invoked when the loaded annotation set changes.</summary>
    [Parameter] public EventCallback<IReadOnlyList<DocumentCommentThread>> AnnotationsChanged { get; set; }

    /// <summary>
    /// How the annotator decides its responsive layout. <see cref="TmLayoutMode.Auto"/> measures
    /// the annotator's own container through the embedded <see cref="Layout.TmEditorShell"/>; a
    /// forced value renders that layout without measuring, so a test — or a host — can pin a
    /// mode. Default <see cref="TmLayoutMode.Auto"/>. The mobile layout is the G2 presentation:
    /// viewing plus light edits only.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>
    /// Whether the toolbar's icon buttons show their text labels. Default
    /// <see cref="ToolbarLabels.Icons"/> (the icon-only tools of the earlier phases);
    /// <see cref="ToolbarLabels.Auto"/> and <see cref="ToolbarLabels.IconsWithText"/> show the
    /// labels — on a tablet layout under the icons (the ribbon style of the tablet reference).
    /// The value is passed through to the embedded viewer's toolbar as well.
    /// </summary>
    [Parameter] public ToolbarLabels ToolbarLabels { get; set; } = ToolbarLabels.Icons;

    // ── State ────────────────────────────────────────────────────────────────

    private ElementReference _surfaceRef;

    /// <summary>The annotator root — the scope of the JS focus helpers (opener restore, the
    /// sheet composer focus, the selected-card scroll) for the mobile sheet.</summary>
    private ElementReference _annotatorRef;

    /// <summary>The embedded viewer instance: the annotator folds the viewer's Pages/Search
    /// entries into its own mobile action bar and drives them through the viewer's public API.</summary>
    private TmPdfViewer? _viewer;

    private List<DocumentCommentThread> _threads = [];
    private InMemoryPdfAnnotationProvider? _fallbackProvider;
    private string? _loadedDocumentId;
    private string? _selectedThreadId;
    private bool _showResolved;
    private bool _showResolvedInitialized;
    private bool _panelVisible = true;

    /// <summary>The <see cref="ShowPanel"/> value seen by the previous parameter pass: the internal
    /// panel state syncs from the parameter only when it actually changes, so a close from the
    /// panel's ✕ (or the toolbar toggle) is not undone by an unrelated re-render of an unbound host.</summary>
    private bool? _lastShowPanelParam;

    /// <summary>Whether the host explicitly supplied <see cref="ShowPanel"/> in the current
    /// parameter set (detected in <see cref="SetParametersAsync"/>). When it did, the mobile
    /// sheet default (start closed) does not apply — the host owns the state.</summary>
    private bool _showPanelSupplied;

    /// <summary>Whether the shell's right panel is collapsed to its rail. Two-way shadow of the
    /// shell's <see cref="TmEditorShell.CollapsedPanels"/> (phase 8 review 2 — the
    /// collapse chevron was dead without the binding).</summary>
    private EditorShellPanel _collapsedPanels;

    private PdfAnnotatorMode _mode;
    private PdfAnnotatorMode _lastModeParam;
    private int _currentPage = 1;
    private int _totalPages;
    private int _selectedStampIndex;
    private PdfAnnotatorDraft? _draft;

    /// <summary>The thumbnails collapse state handed to the viewer; synced from the
    /// <see cref="ThumbnailsCollapsed"/> parameter only when it actually changes, so an
    /// unbound host keeps the user's choice (same pattern as <see cref="_panelVisible"/>).</summary>
    private bool _thumbnailsCollapsed;

    /// <summary>The <see cref="ThumbnailsCollapsed"/> value seen by the previous parameter pass.</summary>
    private bool? _lastThumbnailsCollapsedParam;

    /// <summary>The zoom mode handed to the viewer; synced from <see cref="ZoomMode"/> only when
    /// it actually changes, so an unbound host keeps the mode the viewer reported back.</summary>
    private PdfZoomMode _zoomMode = PdfZoomMode.Auto;

    /// <summary>The <see cref="ZoomMode"/> value seen by the previous parameter pass.</summary>
    private PdfZoomMode _lastZoomModeParam = PdfZoomMode.Auto;

    // ── FZE-12: runtime color picker state ───────────────────────────────────

    /// <summary>The color for NEW annotations: a pick sets it, a reset (or a new document)
    /// drops it back to the <see cref="AnnotationColor"/> parameter (null = per-author/role).</summary>
    private string? _annotationColor;

    /// <summary>The <see cref="AnnotationColor"/> value seen by the previous parameter pass —
    /// a host echo of a pick must not fight the picker, an unrelated re-render must not
    /// override the host's own change either.</summary>
    private string? _lastAnnotationColorParam;

    /// <summary>Whether the host explicitly supplied <see cref="AnnotationColor"/> in the current
    /// parameter set (detected in <see cref="SetParametersAsync"/>). When it did not, the
    /// picker's runtime selection must survive the pass — an unbound host never echoes the
    /// two-way value back, so syncing from the absent parameter would wipe every pick.</summary>
    private bool _annotationColorSupplied;

    /// <summary>Whether the color palette is open.</summary>
    private bool _colorPickerOpen;

    /// <summary>The element the palette popover is anchored to — the toolbar trigger button or
    /// the mobile palette tile, whichever opened it.</summary>
    private ElementReference _colorAnchor;

    /// <summary>The desktop/tablet toolbar trigger button (the palette's anchor there).</summary>
    private ElementReference _colorTriggerRef;

    /// <summary>The mobile palette tile (the palette sheet's anchor on a phone).</summary>
    private ElementReference _colorTileRef;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public override async Task SetParametersAsync(ParameterView parameters)
    {
        // Detect an explicit host ShowPanel BEFORE the sync below: the mobile sheet default
        // (start closed) must not fight a host that binds the panel state itself (phase 8
        // review 3).
        _showPanelSupplied = parameters.TryGetValue<bool>(nameof(ShowPanel), out _);
        // FZE-12: same detection for the color — a pass without the parameter keeps the picker's
        // runtime selection (the unbound-host echo problem).
        _annotationColorSupplied = parameters.TryGetValue<string?>(nameof(AnnotationColor), out _);
        await base.SetParametersAsync(parameters);
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // The panel state syncs from the parameter first: a layout change applied below must
        // win over it (the mobile default closes the sheet), never the other way round.
        if (ShowPanel != _lastShowPanelParam)
        {
            _lastShowPanelParam = ShowPanel;
            _panelVisible = ShowPanel;
        }

        if (Mode != _lastModeParam)
        {
            _lastModeParam = Mode;
            _mode = Mode;
        }

        ApplyMobileToolFallback();

        if (ThumbnailsCollapsed != _lastThumbnailsCollapsedParam)
        {
            _lastThumbnailsCollapsedParam = ThumbnailsCollapsed;
            _thumbnailsCollapsed = ThumbnailsCollapsed;
        }

        if (ZoomMode != _lastZoomModeParam)
        {
            _lastZoomModeParam = ZoomMode;
            _zoomMode = ZoomMode;
        }

        if (!_showResolvedInitialized)
        {
            _showResolvedInitialized = true;
            _showResolved = ShowResolved;
        }

        // FZE-12: the color parameter is the baseline — a host change wins, the picker's own
        // two-way echo arrives as the same value (no fight), and the reset raises null. A pass
        // WITHOUT the parameter (an unbound host) keeps the picked value.
        if (_annotationColorSupplied
            && !EqualityComparer<string?>.Default.Equals(AnnotationColor, _lastAnnotationColorParam))
        {
            _lastAnnotationColorParam = AnnotationColor;
            _annotationColor = AnnotationColor;
        }

        SyncColorPickerData();

        // Recomputed once per parameter set; ResolveThreadColor runs per thread per render.
        _knownUsers = Users is null
            ? (CurrentUser is null ? [] : [CurrentUser])
            : (CurrentUser is null ? Users : [.. Users, CurrentUser]);
        _colorResolver ??= ResolveThreadColor;
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        // A FORCED layout is the resolution: the embedded shell reports changes only, and the
        // initial resolution is not a change — so a host (or test) that pins a mode must see
        // that presentation from the first frame. Under Auto the shell's callback owns this
        // state (it fires on the first container measurement). Awaited, so the mobile sheet
        // default lands before the first frame renders.
        if (LayoutMode != TmLayoutMode.Auto && LayoutMode != _resolved)
        {
            await ApplyResolvedLayoutAsync(LayoutMode);
        }

        var documentId = EffectiveDocumentId;
        if (!string.Equals(documentId, _loadedDocumentId, StringComparison.Ordinal))
        {
            _loadedDocumentId = documentId;
            _threads = [];
            _selectedThreadId = null;
            _draft = null;
            _activeStroke = null;
            _strokePointerId = null;
            _strokePointerType = null;
            _currentPage = 1;
            _totalPages = 0;
            _surfaceSize = null;
            // FZE-12: the picked color belongs to the document — a new Url resets the
            // selection back to the parameter baseline (the per-author/role fallback).
            _annotationColor = AnnotationColor;
            _lastAnnotationColorParam = AnnotationColor;
            await LoadThreadsAsync();
        }
    }

    private async Task LoadThreadsAsync()
    {
        try
        {
            _threads = (await EffectiveProvider.GetThreadsAsync(EffectiveDocumentId)).ToList();
        }
        catch
        {
            _threads = [];
        }

        if (AnnotationsChanged.HasDelegate)
        {
            // Hand out a snapshot: handlers may mutate their own collections while we render.
            await AnnotationsChanged.InvokeAsync(_threads.ToArray());
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Reloads annotation threads from the provider (e.g. after an external change).</summary>
    public Task RefreshAsync() => LoadThreadsAsync();


    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // FZE-9 review (M2): the embedded viewer raises PinchStarted when its two-finger
        // gesture begins — the stroke is discarded (never committed). The reference exists
        // only after a render, so the subscription rides every render pass (idempotent).
        SubscribeToViewerPinch();

        if (_focusOpenerAfter)
        {
            _focusOpenerAfter = false;
            try { await JS.InvokeAsync<bool>("tmPdfAnnotator.focusOpener", _annotatorRef); }
            catch (JSException) { }
            catch (JSDisconnectedException) { }
        }

        if (_focusComposerAfter)
        {
            _focusComposerAfter = false;
            await FocusSheetComposerAsync();
        }

        if (_scrollSelectedCardAfter)
        {
            _scrollSelectedCardAfter = false;
            try
            {
                await JS.InvokeAsync<bool>("tmPdfAnnotator.scrollSelectedCardIntoView", _annotatorRef);
            }
            catch (JSException) { }
            catch (JSDisconnectedException) { }
        }
    }


    // ── Mode & toolbar ───────────────────────────────────────────────────────

    private async Task SetModeAsync(PdfAnnotatorMode mode)
    {
        if (_mode == mode)
        {
            return;
        }

        _mode = mode;
        _draft = null;
        _activeStroke = null;
        _strokePointerId = null;
        _strokePointerType = null;
        await ModeChanged.InvokeAsync(mode);
        await InvokeAsync(StateHasChanged);
    }

    private string ModeClass(PdfAnnotatorMode mode)
        => _mode == mode
            ? "tm-pdf-annotator__mode tm-pdf-annotator__mode--active"
            : "tm-pdf-annotator__mode";

    private void HandleStampSelectionChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var index)
            && index >= 0 && index < EffectiveStampOptions.Count)
        {
            _selectedStampIndex = index;
        }
    }


    // ── Annotation creation ──────────────────────────────────────────────────

    private DocumentCommentUser ResolveAuthor()
        => CurrentUser ?? new DocumentCommentUser
        {
            UserId = "anonymous",
            DisplayName = Loc["TmPdfAnnotator_AnonymousUser"]
        };

    private async Task HandleTextSelectedAsync(PdfTextSelection selection)
    {
        if (_mode != PdfAnnotatorMode.Highlight || !selection.IsValid)
        {
            return;
        }

        _draft = new PdfAnnotatorDraft
        {
            Kind = DocumentAnnotationKind.Highlight,
            Anchor = selection.ToAnchor()
        };
        await InvokeAsync(StateHasChanged);
    }

    private async Task CreateStampAsync(double x, double y)
    {
        var stampText = EffectiveStampOptions[Math.Min(_selectedStampIndex, EffectiveStampOptions.Count - 1)];
        const double stampWidth = 0.18;
        const double stampHeight = 0.05;
        var request = new DocumentCommentThreadCreateRequest
        {
            Anchor = DocumentCommentAnchor.Area(
                _currentPage,
                Math.Clamp(x - stampWidth / 2, 0, 1 - stampWidth),
                Math.Clamp(y - stampHeight / 2, 0, 1 - stampHeight),
                stampWidth,
                stampHeight),
            Body = stampText,
            Kind = DocumentAnnotationKind.Stamp,
            Color = _annotationColor,
            StampText = stampText
        };

        await CreateThreadAsync(request);
    }

    private async Task CreateFromDraftAsync(string body)
    {
        if (_draft?.Anchor is null)
        {
            return;
        }

        var request = new DocumentCommentThreadCreateRequest
        {
            Anchor = _draft.Anchor,
            Body = body ?? string.Empty,
            Kind = _draft.Kind,
            Color = _annotationColor,
            InkStrokes = [.. _draft.Strokes]
        };

        _draft = null;
        await CreateThreadAsync(request);
    }

    private async Task CreateThreadAsync(DocumentCommentThreadCreateRequest request)
    {
        try
        {
            var created = await EffectiveProvider.CreateThreadAsync(EffectiveDocumentId, request, ResolveAuthor());
            _selectedThreadId = created.Id;
            await LoadThreadsAsync();
        }
        catch { }
    }

    private async Task DismissDraftAsync()
    {
        _draft = null;
        _activeStroke = null;
        _strokePointerId = null;
        _strokePointerType = null;
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// The tablet sticky CTA's action (FZE-9): surfaces the pending selection's draft —
    /// opens the thread panel when the host closed it and focuses the draft composer after
    /// the render. The highlight draft itself was created by the selection.
    /// </summary>
    private async Task OpenDraftComposerAsync()
    {
        if (_draft is null)
        {
            return;
        }

        if (!_panelVisible)
        {
            _panelVisible = true;
            await ShowPanelChanged.InvokeAsync(true);
        }

        _focusComposerAfter = true;
        await InvokeAsync(StateHasChanged);
    }

    // ── Panel callbacks ──────────────────────────────────────────────────────

    private async Task ReplyToThreadAsync(DocumentCommentReplyRequest request)
    {
        try
        {
            await EffectiveProvider.ReplyAsync(EffectiveDocumentId, request, ResolveAuthor());
            await LoadThreadsAsync();
        }
        catch { }
    }

    private async Task ResolveThreadAsync(string threadId)
    {
        try
        {
            await EffectiveProvider.ResolveAsync(EffectiveDocumentId, threadId, ResolveAuthor());
            await LoadThreadsAsync();
        }
        catch { }
    }

    private async Task ReopenThreadAsync(string threadId)
    {
        try
        {
            await EffectiveProvider.ReopenAsync(EffectiveDocumentId, threadId, ResolveAuthor());
            await LoadThreadsAsync();
        }
        catch { }
    }

    private async Task DeleteCommentAsync(DocumentCommentDeleteRequest request)
    {
        try
        {
            var target = _threads.FirstOrDefault(thread => string.Equals(thread.Id, request.ThreadId, StringComparison.Ordinal));
            var threadWillBeRemoved = target is null || target.Comments.Count <= 1;

            await EffectiveProvider.DeleteAsync(EffectiveDocumentId, request);

            if (threadWillBeRemoved && string.Equals(_selectedThreadId, request.ThreadId, StringComparison.Ordinal))
            {
                _selectedThreadId = null;
            }

            await LoadThreadsAsync();
        }
        catch { }
    }

    private async Task EditCommentAsync(DocumentCommentEditRequest request)
    {
        try
        {
            await EffectiveProvider.EditAsync(EffectiveDocumentId, request);
            await LoadThreadsAsync();
        }
        catch { }
    }

    private async Task ClosePanelAsync()
    {
        // Internal state first: an unbound host never echoes ShowPanel back, so the panel must
        // disappear (and be re-openable by the toolbar toggle) without the parameter changing.
        _panelVisible = false;
        if (IsMobileLayout)
        {
            // The sheet's close unmounted the opener's click target context: restore focus.
            _focusOpenerAfter = true;
        }

        await ShowPanelChanged.InvokeAsync(false);
        await InvokeAsync(StateHasChanged);
    }

    private async Task TogglePanelAsync()
    {
        _panelVisible = !_panelVisible;
        await ShowPanelChanged.InvokeAsync(_panelVisible);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Forwards the viewer's thumbnails collapse change: internal state first (an
    /// unbound host never echoes the parameter), then the two-way callback.</summary>
    private async Task HandleThumbnailsCollapsedChangedAsync(bool value)
    {
        _thumbnailsCollapsed = value;
        await ThumbnailsCollapsedChanged.InvokeAsync(value);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Forwards the viewer's zoom mode change: internal state first, then the
    /// two-way callback (a fit mode abandoned by a zoom button reports Custom).</summary>
    private async Task HandleZoomModeChangedAsync(PdfZoomMode value)
    {
        _zoomMode = value;
        await ZoomModeChanged.InvokeAsync(value);
        await InvokeAsync(StateHasChanged);
    }

    private async Task SelectThreadAsync(string? threadId)
    {
        _selectedThreadId = threadId;
        // Selecting a thread anchored on another page navigates there: the current page
        // is handed to the embedded viewer through its Page parameter.
        var page = _threads.FirstOrDefault(t => string.Equals(t.Id, threadId, StringComparison.Ordinal))?.Anchor.PageNumber ?? 0;
        if (page >= 1 && page != _currentPage)
        {
            _currentPage = page;
            ResetPageState();
        }

        // G2: on a phone the panel is the bottom sheet — a tapped thread marker opens it on
        // that thread (the HALF snap, the selected card scrolled into view and the reply
        // composer focused), like the diagram editor's selection-driven sheet.
        if (IsMobileLayout && threadId is not null)
        {
            if (!_panelVisible)
            {
                await RememberOpenerAsync();
                _panelVisible = true;
                _mobileSnapIndex = MobileOpenSnapIndex;
                await ShowPanelChanged.InvokeAsync(true);
            }

            _focusComposerAfter = true;
            _scrollSelectedCardAfter = true;
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task SetShowResolvedAsync(bool value)
    {
        _showResolved = value;
        await ShowResolvedChanged.InvokeAsync(value);
        await InvokeAsync(StateHasChanged);
    }

    // ── Colors ───────────────────────────────────────────────────────────────

    private string ResolveThreadColor(DocumentCommentThread thread)
        => PdfAnnotationColorHelper.ResolveColor(thread, AuthorColors, RoleColors, KnownUsers);

    /// <summary>The chip/draft color: the picked color, the explicit parameter, or the
    /// per-author/role resolution when neither is set.</summary>
    private string CurrentAuthorColor
        => _annotationColor ?? PdfAnnotationColorHelper.ResolveForAuthor(
            CurrentUser?.UserId ?? "anonymous", AuthorColors, RoleColors, KnownUsers);

    // ── FZE-12: runtime color picker ─────────────────────────────────────────

    /// <summary>The palette handed to the picker: the host's colors or the helper default.</summary>
    private IReadOnlyList<string> EffectiveColorPalette
        => ColorPalette is { Count: > 0 } ? ColorPalette : PdfAnnotationColorHelper.DefaultPalette;

    /// <summary>Names for the palette swatches: the host's map or the localized
    /// <c>TmPdfAnnotator_ColorName_*</c> names of the default palette.</summary>
    private IReadOnlyDictionary<string, string>? _effectiveColorNames;

    /// <summary>Rebuilds the picker's palette data once per parameter set.</summary>
    private void SyncColorPickerData()
    {
        if (ColorNames is not null)
        {
            _effectiveColorNames = ColorNames;
            return;
        }

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var color in EffectiveColorPalette)
        {
            var name = color switch
            {
                "#2563eb" => Loc["TmPdfAnnotator_ColorName_Blue"],
                "#b45309" => Loc["TmPdfAnnotator_ColorName_Amber"],
                "#0e7490" => Loc["TmPdfAnnotator_ColorName_Cyan"],
                "#be185d" => Loc["TmPdfAnnotator_ColorName_Pink"],
                "#15803d" => Loc["TmPdfAnnotator_ColorName_Green"],
                "#7c3aed" => Loc["TmPdfAnnotator_ColorName_Violet"],
                "#b91c1c" => Loc["TmPdfAnnotator_ColorName_Red"],
                "#475569" => Loc["TmPdfAnnotator_ColorName_Slate"],
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(name) && name != color)
            {
                names[color] = name;
            }
        }

        _effectiveColorNames = names.Count > 0 ? names : null;
    }

    /// <summary>The trigger's accessible name: the localized color label plus the current
    /// color's name (or its raw value when unnamed).</summary>
    private string ColorTriggerAriaLabel
    {
        get
        {
            var color = CurrentAuthorColor;
            var name = _effectiveColorNames is not null
                       && _effectiveColorNames.TryGetValue(color, out var named)
                ? named
                : color;
            return $"{Loc["TmPdfAnnotator_ColorLabel"]}: {name}";
        }
    }

    /// <summary>Opens the palette anchored to the toolbar trigger (desktop and tablet).</summary>
    private Task OpenColorPickerAsync()
    {
        _colorAnchor = _colorTriggerRef;
        _colorPickerOpen = true;
        return InvokeAsync(StateHasChanged);
    }

    /// <summary>Opens the palette anchored to the mobile tile — the F3 bottom sheet there.</summary>
    private Task OpenMobileColorPickerAsync()
    {
        _colorAnchor = _colorTileRef;
        _colorPickerOpen = true;
        return InvokeAsync(StateHasChanged);
    }

    /// <summary>Overlay two-way bridge: internal state first, no callback (the open state is
    /// the annotator's own UI state, never bound by hosts).</summary>
    private Task HandleColorPickerOpenChangedAsync(bool open)
    {
        _colorPickerOpen = open;
        return InvokeAsync(StateHasChanged);
    }

    /// <summary>Applies a picked color to NEW annotations: internal state first (an unbound
    /// host never echoes the callback), then the two-way <see cref="AnnotationColorChanged"/>.
    /// Selecting the already-checked swatch — the per-author/role fallback shown while no
    /// explicit color is picked — is a no-op: it keeps the implicit resolution and must not
    /// raise the callback.</summary>
    private async Task HandleColorPickedAsync(string color)
    {
        if (_annotationColor is null
            && string.Equals(color, CurrentAuthorColor, StringComparison.OrdinalIgnoreCase))
        {
            await CloseColorPickerAsync();
            return;
        }

        _annotationColor = color;
        _lastAnnotationColorParam = color;
        await AnnotationColorChanged.InvokeAsync(color);
        await CloseColorPickerAsync();
    }

    /// <summary>Reset: the explicit pick is dropped — new annotations fall back to the
    /// per-author/role resolution again (a bound host sees <c>null</c>), existing threads are
    /// never recolored.</summary>
    private async Task ResetColorAsync()
    {
        _annotationColor = null;
        _lastAnnotationColorParam = null;
        await AnnotationColorChanged.InvokeAsync(null);
        await CloseColorPickerAsync();
    }

    private Task CloseColorPickerAsync()
    {
        _colorPickerOpen = false;
        return InvokeAsync(StateHasChanged);
    }

    // ── Viewer callbacks ─────────────────────────────────────────────────────

    private async Task HandlePageChangedAsync(int page)
    {
        _currentPage = page;
        ResetPageState();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Drops page-scoped interaction state (an in-progress ink stroke and the cached
    /// surface size); used whenever the visible page changes, from page navigation
    /// (<see cref="HandlePageChangedAsync"/>) and thread selection
    /// (<see cref="SelectThreadAsync"/>) alike.
    /// </summary>
    private void ResetPageState()
    {
        _activeStroke = null;
        _strokePointerId = null;
        _strokePointerType = null;
        _surfaceSize = null;
    }

    private void HandleDocumentLoaded(int totalPages)
    {
        _totalPages = totalPages;
        if (totalPages > 0 && _currentPage > totalPages)
        {
            _currentPage = 1;
        }
    }

    // ── Export & print ───────────────────────────────────────────────────────

    /// <summary>Exports the document with its annotations embedded as PDF annotations,
    /// or drawn into the page content when <paramref name="flatten"/> is true.</summary>
    /// <param name="flatten">Whether annotations are flattened into the page content.</param>
    public async Task ExportAsync(bool flatten = false)
    {
        if (string.IsNullOrEmpty(Url))
        {
            return;
        }

        var payload = PdfAnnotationExportPayloadBuilder.Build(
            _threads.ToArray(), includeResolved: true, AuthorColors, RoleColors, KnownUsers);
        var options = JsonSerializer.Serialize(new { flatten, fileName = ResolveExportFileName() });

        try { await JS.InvokeVoidAsync("tmPdfAnnotator.exportPdf", Url, payload, options); }
        catch { }
    }

    /// <summary>Opens a print variant of the document with annotations rendered on the pages
    /// followed by a comment summary.</summary>
    public async Task PrintAsync()
    {
        if (string.IsNullOrEmpty(Url))
        {
            return;
        }

        var payload = PdfAnnotationExportPayloadBuilder.Build(
            _threads.ToArray(), includeResolved: _showResolved, AuthorColors, RoleColors, KnownUsers);

        try { await JS.InvokeVoidAsync("tmPdfAnnotator.printWithAnnotations", Url, payload); }
        catch { }
    }

    private string ResolveExportFileName()
    {
        if (!string.IsNullOrWhiteSpace(ExportFileName))
        {
            return ExportFileName!;
        }

        var name = Url ?? "document.pdf";
        var slash = name.LastIndexOfAny(['/', '\\']);
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        var query = name.IndexOfAny(['?', '#']);
        if (query >= 0)
        {
            name = name[..query];
        }

        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return (string.IsNullOrWhiteSpace(name) ? "document" : name) + "-annotated.pdf";
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // FZE-9 review (M2): drop the pinch subscription before the viewer instance dies.
        UnsubscribeFromViewerPinch();
        // Phase 12: a pending mobile-sheet close debounce must not fire into a dead component.
        CancelMobileSheetClose();
        await ValueTask.CompletedTask;
    }
}
