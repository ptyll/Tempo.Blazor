using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Displays a normalized signing document page and optional overlays.</summary>
public partial class TmDocumentPageViewer : ITmDraftGuard
{
    private static readonly double[] ZoomSteps = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0];
    private double _scale = 1.0;
    private DocumentPageZoomMode _zoomMode = DocumentPageZoomMode.Custom;
    private DocumentCommentAnchor? _draftAnchor;
    private IReadOnlyList<DocumentCommentMention> _draftMentions = [];
    private bool _isCommentPointerDown;
    private double _commentStartX;
    private double _commentStartY;
    private ElementReference _pageElement;

    /// <summary>Text of the new-thread draft composer (bound internally — the host cannot see it otherwise).</summary>
    private string _draftText = string.Empty;

    /// <summary>Embedded thread panel (reply/edit composers) — its drafts are discarded through this ref.</summary>
    private TmDocumentCommentThreadPanel? _threadPanel;

    /// <summary>
    /// Latest draft state the embedded thread panel reported through its OnDraftChanged callback.
    /// Stored from the callback (not read off the ref) so the aggregate never reads a stale or
    /// unmounted panel instance.
    /// </summary>
    private TmCommentDraftState _panelDraftState = new(false, null, false);

    /// <summary>Last draft state handed to the parent — used to dedup <see cref="OnDraftChanged"/> notifications.</summary>
    private TmCommentDraftState _lastDraftState = new(false, null, false);

    /// <summary>Optional id assigned to the rendered page element.</summary>
    [Parameter] public string? Id { get; set; }

    /// <summary>Document page to render. When null, an empty state is shown.</summary>
    [Parameter] public SigningDocumentPage? Page { get; set; }

    /// <summary>Whether to show the loading skeleton instead of the page.</summary>
    [Parameter] public bool IsLoading { get; set; }

    /// <summary>Error message shown instead of the page.</summary>
    [Parameter] public string? Error { get; set; }

    /// <summary>Optional alt text for the page image. Defaults to page label or localized page number.</summary>
    [Parameter] public string? Alt { get; set; }

    /// <summary>Whether overlay content should receive pointer events. Defaults to true.</summary>
    [Parameter] public bool IsOverlayInteractive { get; set; } = true;

    /// <summary>Whether to prevent the browser context menu on the page element. Defaults to true.</summary>
    [Parameter] public bool PreventDefaultContextMenu { get; set; } = true;

    /// <summary>Current visual scale. Values are clamped between <see cref="MinScale"/> and <see cref="MaxScale"/>.</summary>
    [Parameter] public double Scale { get; set; } = 1.0;

    /// <summary>Callback invoked when the visual scale changes.</summary>
    [Parameter] public EventCallback<double> ScaleChanged { get; set; }

    /// <summary>Smallest allowed visual scale. Defaults to 0.5.</summary>
    [Parameter] public double MinScale { get; set; } = 0.5;

    /// <summary>Largest allowed visual scale. Defaults to 2.0.</summary>
    [Parameter] public double MaxScale { get; set; } = 2.0;

    /// <summary>Current zoom behavior. Defaults to <see cref="DocumentPageZoomMode.Custom"/>.</summary>
    [Parameter] public DocumentPageZoomMode ZoomMode { get; set; } = DocumentPageZoomMode.Custom;

    /// <summary>Callback invoked when the zoom behavior changes.</summary>
    [Parameter] public EventCallback<DocumentPageZoomMode> ZoomModeChanged { get; set; }

    /// <summary>Current page view mode for parent tools. Defaults to <see cref="DocumentPageViewMode.SinglePage"/>.</summary>
    [Parameter] public DocumentPageViewMode ViewMode { get; set; } = DocumentPageViewMode.SinglePage;

    /// <summary>Callback invoked when the page view mode changes.</summary>
    [Parameter] public EventCallback<DocumentPageViewMode> ViewModeChanged { get; set; }

    /// <summary>Whether to render the viewer toolbar. Defaults to false for layout compatibility.</summary>
    [Parameter] public bool ShowToolbar { get; set; }

    /// <summary>Whether zoom controls are visible when the toolbar is rendered. Defaults to true.</summary>
    [Parameter] public bool ShowZoomControls { get; set; } = true;

    /// <summary>Whether page navigation controls are visible when the toolbar is rendered. Defaults to false.</summary>
    [Parameter] public bool ShowPaginationControls { get; set; }

    /// <summary>Displayed current page number for pagination controls. Defaults to the page index plus one.</summary>
    [Parameter] public int? PageNumber { get; set; }

    /// <summary>Total displayed page count for pagination controls.</summary>
    [Parameter] public int? TotalPages { get; set; }

    /// <summary>Callback invoked when the previous page button is clicked.</summary>
    [Parameter] public EventCallback PreviousPageRequested { get; set; }

    /// <summary>Callback invoked when the next page button is clicked.</summary>
    [Parameter] public EventCallback NextPageRequested { get; set; }

    /// <summary>Optional template for replacing the rendered page image.</summary>
    [Parameter] public RenderFragment<SigningDocumentPage>? PageTemplate { get; set; }

    /// <summary>Overlay content rendered over the document page.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Overlay template rendered over the document page with page context.</summary>
    [Parameter] public RenderFragment<SigningDocumentPage>? OverlayTemplate { get; set; }

    /// <summary>Callback invoked when the page is clicked.</summary>
    [Parameter] public EventCallback<TmDocumentPageViewerPointerEventArgs> OnPageClick { get; set; }

    /// <summary>Callback invoked when the page context menu is requested.</summary>
    [Parameter] public EventCallback<TmDocumentPageViewerPointerEventArgs> OnPageContextMenu { get; set; }

    /// <summary>Whether document comments are enabled for this viewer.</summary>
    [Parameter] public bool CommentsEnabled { get; set; }

    /// <summary>Current document comment mode. Defaults to browse mode.</summary>
    [Parameter] public DocumentCommentMode CommentMode { get; set; } = DocumentCommentMode.Browse;

    /// <summary>Callback invoked when the comment mode changes.</summary>
    [Parameter] public EventCallback<DocumentCommentMode> CommentModeChanged { get; set; }

    /// <summary>Comment threads rendered over the document page.</summary>
    [Parameter] public IReadOnlyList<DocumentCommentThread> CommentThreads { get; set; } = [];

    /// <summary>Selected comment thread id.</summary>
    [Parameter] public string? SelectedCommentThreadId { get; set; }

    /// <summary>Callback invoked when the selected comment thread changes.</summary>
    [Parameter] public EventCallback<string?> SelectedCommentThreadIdChanged { get; set; }

    /// <summary>Whether resolved comment threads are visible. Defaults to false.</summary>
    [Parameter] public bool ShowResolvedComments { get; set; }

    /// <summary>Whether comment interactions are disabled. Defaults to false.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Whether a plain page click clears the selected comment thread. Defaults to true.</summary>
    [Parameter] public bool ClearCommentSelectionOnPageClick { get; set; } = true;

    /// <summary>Current user id used for mention and reaction indicators.</summary>
    [Parameter] public string? CurrentUserId { get; set; }

    /// <summary>Users available for comment mentions.</summary>
    [Parameter] public IReadOnlyList<DocumentCommentUser> MentionUsers { get; set; } = [];

    /// <summary>Whether replies are allowed on resolved comment threads.</summary>
    [Parameter] public bool AllowReplyToResolved { get; set; }

    /// <summary>Optional custom marker template for comment anchors.</summary>
    [Parameter] public RenderFragment<DocumentCommentThread>? CommentMarkerTemplate { get; set; }

    /// <summary>Optional custom panel for rendering comment threads.</summary>
    [Parameter] public RenderFragment<IReadOnlyList<DocumentCommentThread>>? CommentThreadPanelTemplate { get; set; }

    /// <summary>Callback invoked when creating a new comment thread is requested.</summary>
    [Parameter] public EventCallback<DocumentCommentThreadCreateRequest> OnCommentThreadCreateRequested { get; set; }

    /// <summary>Callback invoked when replying to a comment thread is requested.</summary>
    [Parameter] public EventCallback<DocumentCommentReplyRequest> OnCommentReplyRequested { get; set; }

    /// <summary>Callback invoked when resolving a comment thread is requested.</summary>
    [Parameter] public EventCallback<DocumentCommentThreadStatusRequest> OnCommentResolveRequested { get; set; }

    /// <summary>Callback invoked when reopening a comment thread is requested.</summary>
    [Parameter] public EventCallback<DocumentCommentThreadStatusRequest> OnCommentReopenRequested { get; set; }

    /// <summary>Callback invoked when editing a document comment is requested.</summary>
    [Parameter] public EventCallback<DocumentCommentEditRequest> OnCommentEditRequested { get; set; }

    /// <summary>Callback invoked when deleting a document comment is requested.</summary>
    [Parameter] public EventCallback<DocumentCommentDeleteRequest> OnCommentDeleteRequested { get; set; }

    /// <summary>Callback invoked when toggling a document comment reaction is requested.</summary>
    [Parameter] public EventCallback<DocumentCommentReactionToggleRequest> OnCommentReactionToggled { get; set; }

    /// <summary>Callback invoked when the comment panel requests navigation to a thread page.</summary>
    [Parameter] public EventCallback<DocumentCommentThreadNavigateRequest> OnCommentThreadNavigateRequested { get; set; }

    /// <summary>Callback invoked when draft mentioned users change.</summary>
    [Parameter] public EventCallback<IReadOnlyList<DocumentCommentMention>> OnCommentMentionedUsersChanged { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>
    /// Notifies the parent that the viewer's comment draft state changed. Neither the new-thread
    /// draft composer nor the embedded thread panel's reply/edit composers expose their text
    /// through a parameter — this callback (together with <see cref="HasUnsavedDraft"/> and
    /// <see cref="DiscardDraftAsync"/>) is the aggregate draft-guard surface. It MERGES the
    /// viewer's own new-thread draft with the last state the embedded
    /// <see cref="TmDocumentCommentThreadPanel"/> reported through its own OnDraftChanged
    /// callback, and fires ONLY when the <see cref="TmCommentDraftState"/> value actually changes
    /// (value-equality dedup), never on every keystroke.
    /// <see cref="TmCommentDraftState.IsEditorOpen"/> is <see langword="true"/> while the
    /// new-thread draft panel is open or the thread panel shows a composer;
    /// <see cref="TmCommentDraftState.EditingCommentId"/> carries the id of the comment under
    /// edit in the thread panel (<see langword="null"/> for the viewer's own new-thread draft);
    /// <see cref="TmCommentDraftState.HasUnsavedContent"/> aggregates both — an open edit that
    /// still matches its original body is not an unsaved draft. Both contributions are gated by
    /// <see cref="OwnsCommentSurfaces"/> — comments enabled, no host panel template, and a
    /// rendered page (not loading, no error, page present): whenever the host owns the draft
    /// surface or the surfaces are unmounted, the viewer reports the closed, empty state.
    /// </summary>
    [Parameter] public EventCallback<TmCommentDraftState> OnDraftChanged { get; set; }

    /// <summary>
    /// Whether the viewer currently holds an unsaved comment draft anywhere — the new-thread
    /// composer text or any draft inside the embedded thread panel. The SAME value carried by
    /// <see cref="TmCommentDraftState.HasUnsavedContent"/> that <see cref="OnDraftChanged"/>
    /// reports; read live from the current state, so it can be polled at any time (for example
    /// before closing a signing session that would silently discard the draft).
    /// <para>
    /// Read it on the component's dispatcher (e.g. from an event handler or via
    /// <c>InvokeAsync</c>) when called from a background thread.
    /// </para>
    /// </summary>
    public bool HasUnsavedDraft => BuildDraftState().HasUnsavedContent;

    /// <summary>
    /// Discards every comment draft in the viewer: closes the new-thread draft panel (anchor,
    /// text and mention selection) and discards the embedded thread panel's drafts (reply text,
    /// open edit) — the panel reports its post-discard state through its own OnDraftChanged
    /// callback, which stays the single source of truth for the aggregated state (an empty
    /// discard with a still-mounted reply composer must keep reporting the open editor).
    /// Intended for the consumer AFTER the user confirmed the discard in the consumer's OWN
    /// confirmation dialog — the component never shows a confirmation itself; that is
    /// deliberately the consumer's responsibility.
    /// <para>
    /// A no-op (no notification) when no editor is open and no draft exists. Safe to call from
    /// outside the render cycle; the work is dispatched through <c>InvokeAsync</c>.
    /// </para>
    /// </summary>
    public Task DiscardDraftAsync()
        => InvokeAsync(async () =>
        {
            _draftAnchor = null;
            _draftText = string.Empty;
            _draftMentions = [];
            StateHasChanged();
            if (_threadPanel is not null)
            {
                await _threadPanel.DiscardDraftAsync();
            }

            await NotifyDraftChangedAsync();
        });

    /// <summary>
    /// Whether the viewer owns the comment draft surfaces: the embedded thread panel and the
    /// new-thread draft panel render only when comments are enabled, the host did not replace
    /// the panel with <see cref="CommentThreadPanelTemplate"/>, and an actual page is on screen
    /// (not loading, no error, page present) — in every other state the surfaces are unmounted.
    /// A template host owns its own draft surface, so neither contribution may reach the
    /// viewer's state.
    /// </summary>
    private bool OwnsCommentSurfaces => CommentsEnabled
        && CommentThreadPanelTemplate is null
        && !IsLoading
        && string.IsNullOrWhiteSpace(Error)
        && Page is not null;

    /// <summary>
    /// Builds the viewer's aggregate draft snapshot: the viewer's own new-thread draft panel
    /// (open anchor + composer text) merged with the embedded thread panel's last reported state
    /// — the panel wins for <see cref="TmCommentDraftState.EditingCommentId"/> because the
    /// viewer's own draft always creates a NEW thread. Both contributions are gated by
    /// <see cref="OwnsCommentSurfaces"/>, so an unmounted panel or a template takeover reports
    /// the post-transition state instead of a stale one.
    /// </summary>
    private TmCommentDraftState BuildDraftState()
    {
        var panelState = OwnsCommentSurfaces
            ? _panelDraftState
            : new TmCommentDraftState(false, null, false);
        var hasOwnDraft = OwnsCommentSurfaces
            && _draftAnchor is not null
            && !string.IsNullOrWhiteSpace(_draftText);

        return new TmCommentDraftState(
            OwnsCommentSurfaces && ((_draftAnchor is not null) || panelState.IsEditorOpen),
            panelState.EditingCommentId,
            hasOwnDraft || panelState.HasUnsavedContent);
    }

    /// <summary>Stores the embedded panel's reported state and re-reports the aggregate.</summary>
    private async Task HandlePanelDraftChangedAsync(TmCommentDraftState state)
    {
        _panelDraftState = state;
        await NotifyDraftChangedAsync();
    }

    /// <summary>
    /// Notifies <see cref="OnDraftChanged"/> only when the draft state actually changed — the
    /// composers report every keystroke into this path and the value-equality check keeps the
    /// parent from re-rendering without a real transition.
    /// </summary>
    private async Task NotifyDraftChangedAsync()
    {
        var state = BuildDraftState();
        if (state == _lastDraftState)
        {
            return;
        }

        _lastDraftState = state;
        await OnDraftChanged.InvokeAsync(state);
    }

    protected override async Task OnParametersSetAsync()
    {
        _scale = ClampScale(Scale);
        _zoomMode = ZoomMode;
        // Comments disabled, a host panel template takeover, a loading/error/empty page — all
        // unmount the draft surfaces. Drop the stale panel state (and the dangling ref) so it
        // cannot leak back once the surfaces remount, then report the transition; the dedup
        // keeps repeated parameter sets silent.
        if (!OwnsCommentSurfaces)
        {
            _panelDraftState = new(false, null, false);
            _threadPanel = null;
        }

        await NotifyDraftChangedAsync();
    }

    private string RootClass => string.Join(" ", new[]
    {
        "tm-document-page-viewer",
        ShowToolbar ? "tm-document-page-viewer--with-toolbar" : null,
        CommentsEnabled ? "tm-document-page-viewer--with-comments" : null,
        CommentsEnabled && CommentMode == DocumentCommentMode.Comment ? "tm-document-page-viewer--comment-mode" : null,
        $"tm-document-page-viewer--zoom-{_zoomMode.ToString().ToLowerInvariant()}",
        Class
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private string OverlayClass => IsOverlayInteractive
        ? "tm-document-page-viewer__overlay"
        : "tm-document-page-viewer__overlay tm-document-page-viewer__overlay--readonly";

    private string PageElementId => !string.IsNullOrWhiteSpace(Id)
        ? Id
        : $"tm-document-page-{Page?.PageIndex ?? 0}";

    private string PageStyle
    {
        get
        {
            if (Page is null || Page.Width <= 0 || Page.Height <= 0)
            {
                return string.Empty;
            }

            return string.Create(CultureInfo.InvariantCulture, $"aspect-ratio: {Page.Width} / {Page.Height}; --tm-document-page-scale: {_scale};");
        }
    }

    private string ZoomLabel => string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(_scale * 100)}%");

    private int CurrentPageNumber => PageNumber ?? (Page?.PageIndex ?? 0) + 1;

    private int CurrentTotalPages => Math.Max(1, TotalPages ?? CurrentPageNumber);

    private bool CanGoPrevious => CurrentPageNumber > 1 && PreviousPageRequested.HasDelegate;

    private bool CanGoNext => CurrentPageNumber < CurrentTotalPages && NextPageRequested.HasDelegate;

    private bool CanZoomOut => _scale > MinScale + 0.001;

    private bool CanZoomIn => _scale < MaxScale - 0.001;

    private int OpenCommentCount => DocumentCommentHelper.CountOpenThreads(CommentThreads);

    private int MentionCommentCount => DocumentCommentHelper.CountMentionedThreads(CommentThreads, CurrentUserId);

    private string CommentModeButtonClass => CommentMode == DocumentCommentMode.Comment
        ? "tm-document-page-viewer__toolbar-button tm-document-page-viewer__comment-toggle tm-document-page-viewer__comment-toggle--active"
        : "tm-document-page-viewer__toolbar-button tm-document-page-viewer__comment-toggle";

    private bool ShouldPreventCommentPointerDefault => CommentsEnabled && CommentMode == DocumentCommentMode.Comment;

    private string ImageAlt => !string.IsNullOrWhiteSpace(Alt)
        ? Alt
        : Page?.Label ?? Loc["TmDocumentPageViewer_PageAlt", (Page?.PageIndex ?? 0) + 1];

    private string PageAriaLabel => Page?.Label ?? Loc["TmDocumentPageViewer_PageAriaLabel", (Page?.PageIndex ?? 0) + 1];

    private async Task HandlePageClickAsync(MouseEventArgs args)
    {
        if (CommentsEnabled && CommentMode == DocumentCommentMode.Comment)
        {
            return;
        }

        if (CommentsEnabled
            && ClearCommentSelectionOnPageClick
            && !string.IsNullOrWhiteSpace(SelectedCommentThreadId))
        {
            await SelectCommentThreadAsync(null);
        }

        if (Page is not null && OnPageClick.HasDelegate)
        {
            await OnPageClick.InvokeAsync(new TmDocumentPageViewerPointerEventArgs(Page, args));
        }
    }

    private Task HandlePageContextMenuAsync(MouseEventArgs args)
    {
        return Page is null || !OnPageContextMenu.HasDelegate
            ? Task.CompletedTask
            : OnPageContextMenu.InvokeAsync(new TmDocumentPageViewerPointerEventArgs(Page, args));
    }

    private Task GoToPreviousPageAsync()
    {
        return CanGoPrevious ? PreviousPageRequested.InvokeAsync() : Task.CompletedTask;
    }

    private Task GoToNextPageAsync()
    {
        return CanGoNext ? NextPageRequested.InvokeAsync() : Task.CompletedTask;
    }

    private Task ZoomOutAsync()
    {
        var next = ZoomSteps.LastOrDefault(value => value < _scale - 0.001);
        return SetScaleAsync(next <= 0 ? MinScale : next, DocumentPageZoomMode.Custom);
    }

    private Task ZoomInAsync()
    {
        var next = ZoomSteps.FirstOrDefault(value => value > _scale + 0.001);
        return SetScaleAsync(next <= 0 ? MaxScale : next, DocumentPageZoomMode.Custom);
    }

    private Task FitWidthAsync()
    {
        return SetScaleAsync(1.0, DocumentPageZoomMode.FitWidth);
    }

    private Task FitPageAsync()
    {
        return SetScaleAsync(0.85, DocumentPageZoomMode.FitPage);
    }

    private async Task ToggleCommentModeAsync()
    {
        if (Disabled)
        {
            return;
        }

        var next = CommentMode == DocumentCommentMode.Comment
            ? DocumentCommentMode.Browse
            : DocumentCommentMode.Comment;
        CommentMode = next;
        if (next == DocumentCommentMode.Browse)
        {
            _draftAnchor = null;
            _draftText = string.Empty;
            _draftMentions = [];
            await NotifyDraftChangedAsync();
        }

        await CommentModeChanged.InvokeAsync(next);
    }

    private async Task SelectCommentThreadAsync(string? threadId)
    {
        SelectedCommentThreadId = threadId;
        if (_draftAnchor is not null)
        {
            _draftAnchor = null;
            _draftText = string.Empty;
            await NotifyDraftChangedAsync();
        }

        await SelectedCommentThreadIdChanged.InvokeAsync(threadId);
    }

    private Task HandleDraftMentionsChangedAsync(IReadOnlyList<DocumentCommentMention> mentions)
    {
        _draftMentions = mentions;
        return OnCommentMentionedUsersChanged.InvokeAsync(mentions);
    }

    private async Task CreateThreadAsync(DocumentCommentComposerSubmitEventArgs args)
    {
        if (Disabled || _draftAnchor is null || string.IsNullOrWhiteSpace(args.Body))
        {
            return;
        }

        await OnCommentThreadCreateRequested.InvokeAsync(new DocumentCommentThreadCreateRequest
        {
            Anchor = _draftAnchor,
            Body = args.Body,
            Mentions = args.Mentions.Count > 0 ? args.Mentions.ToList() : _draftMentions.ToList()
        });
        _draftAnchor = null;
        _draftText = string.Empty;
        _draftMentions = [];
        await NotifyDraftChangedAsync();
    }

    private async Task CancelDraftAsync()
    {
        _draftAnchor = null;
        _draftText = string.Empty;
        _draftMentions = [];
        await NotifyDraftChangedAsync();
        await _pageElement.FocusAsync();
    }

    private Task HandlePageKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Escape" && _draftAnchor is not null)
        {
            return CancelDraftAsync();
        }

        return Task.CompletedTask;
    }

    private async Task HandleCommentPointerDownAsync(MouseEventArgs args)
    {
        if (!CanCreateCommentDraft(args))
        {
            return;
        }

        _isCommentPointerDown = true;
        _commentStartX = NormalizeOffsetX(args);
        _commentStartY = NormalizeOffsetY(args);
        _draftAnchor = DocumentCommentAnchor.Point(CurrentPageNumber, _commentStartX, _commentStartY);
        // The anchor opens the draft panel — a transition the consumer must see even if the
        // gesture never completes with a pointer-up.
        await NotifyDraftChangedAsync();
    }

    private Task HandleCommentPointerMoveAsync(MouseEventArgs args)
    {
        if (!_isCommentPointerDown || !CanCreateCommentDraft(args))
        {
            return Task.CompletedTask;
        }

        var currentX = NormalizeOffsetX(args);
        var currentY = NormalizeOffsetY(args);
        var width = Math.Abs(currentX - _commentStartX);
        var height = Math.Abs(currentY - _commentStartY);
        if (width > 0.01 || height > 0.01)
        {
            _draftAnchor = DocumentCommentAnchor.Area(
                CurrentPageNumber,
                Math.Min(_commentStartX, currentX),
                Math.Min(_commentStartY, currentY),
                width,
                height);
        }

        return Task.CompletedTask;
    }

    private async Task HandleCommentPointerUpAsync(MouseEventArgs args)
    {
        if (!_isCommentPointerDown || !CanCreateCommentDraft(args))
        {
            _isCommentPointerDown = false;
            return;
        }

        var currentX = NormalizeOffsetX(args);
        var currentY = NormalizeOffsetY(args);
        var width = Math.Abs(currentX - _commentStartX);
        var height = Math.Abs(currentY - _commentStartY);
        var anchor = width > 0.01 || height > 0.01
            ? DocumentCommentAnchor.Area(
                CurrentPageNumber,
                Math.Min(_commentStartX, currentX),
                Math.Min(_commentStartY, currentY),
                width,
                height)
            : DocumentCommentAnchor.Point(CurrentPageNumber, currentX, currentY);

        _draftAnchor = anchor;
        _isCommentPointerDown = false;
        // An area gesture can change the anchor's shape (point → area) — report the final one.
        await NotifyDraftChangedAsync();
    }

    private bool CanCreateCommentDraft(MouseEventArgs args)
    {
        return CommentsEnabled
            && !Disabled
            && CommentMode == DocumentCommentMode.Comment
            && Page is not null
            && (args.Buttons is 0 or 1);
    }

    private double NormalizeOffsetX(MouseEventArgs args)
    {
        var width = Page?.Width > 0 ? Page.Width : 1;
        return Clamp01(args.OffsetX / width);
    }

    private double NormalizeOffsetY(MouseEventArgs args)
    {
        var height = Page?.Height > 0 ? Page.Height : 1;
        return Clamp01(args.OffsetY / height);
    }

    private static double Clamp01(double value)
    {
        return double.IsFinite(value) ? Math.Min(Math.Max(value, 0), 1) : 0;
    }

    private async Task SetScaleAsync(double scale, DocumentPageZoomMode zoomMode)
    {
        _scale = ClampScale(scale);
        _zoomMode = zoomMode;
        await ScaleChanged.InvokeAsync(_scale);
        await ZoomModeChanged.InvokeAsync(_zoomMode);
    }

    private double ClampScale(double scale)
    {
        var min = Math.Max(0.1, MinScale);
        var max = Math.Max(min, MaxScale);
        return double.IsFinite(scale)
            ? Math.Min(Math.Max(scale, min), max)
            : 1.0;
    }
}
