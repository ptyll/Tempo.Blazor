using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Text.Json;
using Tempo.Blazor.Abstractions.Models;

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
public partial class TmPdfAnnotator : TmComponentBase
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>URL of the PDF document to annotate.</summary>
    [Parameter] public string? Url { get; set; }

    /// <summary>Stable document identifier used by the annotation provider. Falls back to <see cref="Url"/>.</summary>
    [Parameter] public string? DocumentId { get; set; }

    /// <summary>Height of the annotator (CSS value). Default is "600px".</summary>
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
    /// When null (default) colors resolve per author/role at display time.
    /// </summary>
    [Parameter] public string? AnnotationColor { get; set; }

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

    /// <summary>Whether the underlying viewer toolbar (pagination, zoom) is shown. Default is true.</summary>
    [Parameter] public bool ShowViewerToolbar { get; set; } = true;

    /// <summary>Whether export and print actions are offered. Default is true.</summary>
    [Parameter] public bool AllowExport { get; set; } = true;

    /// <summary>File name used for the exported PDF. Defaults to the source name with an "-annotated" suffix.</summary>
    [Parameter] public string? ExportFileName { get; set; }

    /// <summary>Callback invoked when the loaded annotation set changes.</summary>
    [Parameter] public EventCallback<IReadOnlyList<DocumentCommentThread>> AnnotationsChanged { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private ElementReference _surfaceRef;
    private List<DocumentCommentThread> _threads = [];
    private InMemoryPdfAnnotationProvider? _fallbackProvider;
    private string? _loadedDocumentId;
    private string? _selectedThreadId;
    private bool _showResolved;
    private bool _showResolvedInitialized;
    private PdfAnnotatorMode _mode;
    private PdfAnnotatorMode _lastModeParam;
    private int _currentPage = 1;
    private int _totalPages;
    private int _selectedStampIndex;
    private PdfAnnotatorDraft? _draft;
    private List<DocumentInkPoint>? _activeStroke;
    private double[]? _surfaceSize;

    private IPdfAnnotationProvider EffectiveProvider
        => AnnotationProvider ?? (_fallbackProvider ??= new InMemoryPdfAnnotationProvider());

    private string EffectiveDocumentId
        => !string.IsNullOrEmpty(DocumentId) ? DocumentId : (Url ?? string.Empty);

    private IReadOnlyList<string> EffectiveStampOptions
        => StampOptions is { Count: > 0 }
            ? StampOptions
            :
            [
                Loc["TmPdfAnnotator_StampApproved"],
                Loc["TmPdfAnnotator_StampRejected"],
                Loc["TmPdfAnnotator_StampDraft"],
                Loc["TmPdfAnnotator_StampConfidential"]
            ];

    private IReadOnlyList<DocumentCommentUser> _knownUsers = [];
    private Func<DocumentCommentThread, string>? _colorResolver;

    private IReadOnlyList<DocumentCommentUser> KnownUsers => _knownUsers;

    private bool SurfaceActive
        => _mode is PdfAnnotatorMode.Comment or PdfAnnotatorMode.Stamp or PdfAnnotatorMode.Draw;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Mode != _lastModeParam)
        {
            _lastModeParam = Mode;
            _mode = Mode;
        }

        if (!_showResolvedInitialized)
        {
            _showResolvedInitialized = true;
            _showResolved = ShowResolved;
        }

        // Recomputed once per parameter set; ResolveThreadColor runs per thread per render.
        _knownUsers = Users is null
            ? (CurrentUser is null ? [] : [CurrentUser])
            : (CurrentUser is null ? Users : [.. Users, CurrentUser]);
        _colorResolver ??= ResolveThreadColor;
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        var documentId = EffectiveDocumentId;
        if (!string.Equals(documentId, _loadedDocumentId, StringComparison.Ordinal))
        {
            _loadedDocumentId = documentId;
            _threads = [];
            _selectedThreadId = null;
            _draft = null;
            _activeStroke = null;
            _currentPage = 1;
            _totalPages = 0;
            _surfaceSize = null;
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

    // ── Surface interaction ──────────────────────────────────────────────────

    private async Task HandleSurfaceClickAsync(MouseEventArgs e)
    {
        if (_mode is not (PdfAnnotatorMode.Comment or PdfAnnotatorMode.Stamp))
        {
            return;
        }

        var point = await NormalizePointAsync(e.OffsetX, e.OffsetY, refreshSize: true);
        if (point is null)
        {
            return;
        }

        if (_mode == PdfAnnotatorMode.Comment)
        {
            _draft = new PdfAnnotatorDraft
            {
                Kind = DocumentAnnotationKind.Comment,
                Anchor = DocumentCommentAnchor.Point(_currentPage, point.Value.X, point.Value.Y)
            };
            await InvokeAsync(StateHasChanged);
        }
        else
        {
            await CreateStampAsync(point.Value.X, point.Value.Y);
        }
    }

    private async Task HandlePointerDownAsync(PointerEventArgs e)
    {
        if (_mode != PdfAnnotatorMode.Draw)
        {
            return;
        }

        var point = await NormalizePointAsync(e.OffsetX, e.OffsetY, refreshSize: true);
        if (point is null)
        {
            return;
        }

        _activeStroke = [DocumentInkPoint.Create(point.Value.X, point.Value.Y)];
        await InvokeAsync(StateHasChanged);
    }

    private async Task HandlePointerMoveAsync(PointerEventArgs e)
    {
        if (_activeStroke is null)
        {
            return;
        }

        // Reuse the size measured on pointer-down: no JS round-trip per move event.
        var point = await NormalizePointAsync(e.OffsetX, e.OffsetY, refreshSize: false);
        if (point is null)
        {
            return;
        }

        _activeStroke.Add(DocumentInkPoint.Create(point.Value.X, point.Value.Y));
        await InvokeAsync(StateHasChanged);
    }

    private Task HandlePointerUpAsync(PointerEventArgs e) => FinishStrokeAsync();

    private Task HandlePointerLeaveAsync(PointerEventArgs e) => FinishStrokeAsync();

    private async Task FinishStrokeAsync()
    {
        if (_activeStroke is null)
        {
            return;
        }

        var stroke = _activeStroke;
        _activeStroke = null;
        if (stroke.Count >= 2)
        {
            _draft ??= new PdfAnnotatorDraft
            {
                Kind = DocumentAnnotationKind.Drawing,
                Anchor = DocumentCommentAnchor.Point(_currentPage, stroke[0].X, stroke[0].Y)
            };
            _draft.Strokes.Add(new DocumentInkStroke { Points = stroke });
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task<(double X, double Y)?> NormalizePointAsync(double offsetX, double offsetY, bool refreshSize)
    {
        if (refreshSize || _surfaceSize is null)
        {
            try
            {
                _surfaceSize = await JS.InvokeAsync<double[]>("tmPdfAnnotator.measure", _surfaceRef);
            }
            catch
            {
                _surfaceSize = null;
            }
        }

        if (_surfaceSize is not { Length: >= 2 } size || size[0] <= 0 || size[1] <= 0)
        {
            return null;
        }

        return (offsetX / size[0], offsetY / size[1]);
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
            Color = AnnotationColor,
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
            Color = AnnotationColor,
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

    private string CurrentAuthorColor
        => AnnotationColor ?? PdfAnnotationColorHelper.ResolveForAuthor(
            CurrentUser?.UserId ?? "anonymous", AuthorColors, RoleColors, KnownUsers);

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
}
