using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Serialization;
using Tempo.Blazor.Components.Diagram.Services;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Phase 6 (decision <c>MODELING-READONLY-VIEWER</c>): a read-only presentation surface for a
/// semantic modeling model — no model tree, no inspector, no editor chrome and no writer. It projects
/// the model with the shared <see cref="ModelingDiagramGenerator"/> and renders the presentation copy
/// through <see cref="TmDiagramEditor"/> in viewer mode; the diff and heatmap overlays restyle only
/// that copy (public <see cref="DiagramStyle"/> / <see cref="DiagramNode.Data"/>), never the caller's
/// model and never a DiagramEditor internal. The keyboard/text alternative is a real list of buttons
/// in topological order; every overlay carries a text equivalent (legend counts, scale, values).
/// </summary>
public partial class TmModelingViewer : Tempo.Blazor.Components.TmComponentBase, IAsyncDisposable
{
    private static readonly DiagramFitMode FitWidth = DiagramFitMode.Width;
    private static readonly DiagramFitMode FitPage = DiagramFitMode.Page;

    /// <summary>The padding the diagram editor's fit leaves around the content, per side, in pixels.</summary>
    private const string FittedCanvasPadding = "80px";

    /// <summary>The embedded editor's own toolbar row — the canvas box is not all SVG.</summary>
    private const string FittedCanvasChrome = "2.4rem";

    private static readonly Regex VarTokenRegex = new(
        @"var\(\s*(--[\w-]+)\s*(?:,[^)]*)?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private ElementReference _listRoot;
    private ModelingInteropModule? _interop;
    private IReadOnlyList<ModelingViewerListEntry> _entries = [];
    private IReadOnlyList<ModelingNotationCatalogEntry>? _legendEntries;
    private ModelingViewerOverlaySummary _summary = ModelingViewerOverlaySummary.Empty;
    private DiagramDocument? _document;
    private ModelingModelDto? _modelSource;
    private string? _generatedViewId;
    private string? _selectedId;
    private string? _overlaySignature;
    private string? _announcement;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private bool _elementListExpanded = true;
    private bool _legendsExpanded = true;
    private int _focusedListIndex;
    private bool _fitPending;
    private double? _appliedZoom;
    private string? _appliedFitMode;
    private TmLayoutMode _resolvedLayout = TmLayoutMode.Desktop;
    private TmDiagramEditor? _editor;
    private ElementReference _root;
    private bool _disposed;

    /// <summary>The semantic model to present. Required.</summary>
    [Parameter, EditorRequired] public ModelingModelDto Model { get; set; } = default!;

    /// <summary>Optional view id to project; null uses the default view / generated grid.</summary>
    [Parameter] public string? ViewId { get; set; }

    /// <summary>How the diagram fits into the viewer box. Default <see cref="ModelingViewerFitMode.Width"/>.</summary>
    [Parameter] public ModelingViewerFitMode FitMode { get; set; } = ModelingViewerFitMode.Width;

    /// <summary>Explicit zoom (1 = 100 %). Null fits the diagram through <see cref="FitMode"/>.</summary>
    [Parameter] public double? Zoom { get; set; }

    /// <summary>Whether the embedded viewer renders its zoom controls. Default true.</summary>
    [Parameter] public bool ShowZoomControls { get; set; } = true;

    /// <summary>Element and relationship ids to emphasize (the same styling the selection uses, without selecting).</summary>
    [Parameter] public IReadOnlyCollection<string>? HighlightElementIds { get; set; }

    /// <summary>Two-way selected element or relationship id.</summary>
    [Parameter] public string? SelectedElementId { get; set; }

    /// <summary>Raised when <see cref="SelectedElementId"/> changes.</summary>
    [Parameter] public EventCallback<string?> SelectedElementIdChanged { get; set; }

    /// <summary>Raised when the reader activates an element or relationship (canvas or element list).</summary>
    [Parameter] public EventCallback<ModelingViewerElementEventArgs> OnElementClick { get; set; }

    /// <summary>Optional diff overlay (added / removed / changed / layout-only).</summary>
    [Parameter] public ModelingDiffOverlayDto? DiffOverlay { get; set; }

    /// <summary>Optional heatmap overlay (normalized 0..1 value per element or relationship id).</summary>
    [Parameter] public ModelingHeatmapDto? Heatmap { get; set; }

    /// <summary>Whether the notation legend card renders. Default true; reuses the shared notation catalog.</summary>
    [Parameter] public bool ShowLegend { get; set; } = true;

    /// <summary>Whether the keyboard/text alternative element list renders. Default true.</summary>
    [Parameter] public bool ShowElementList { get; set; } = true;

    /// <summary>
    /// Host slot replacing the VISIBLE content of an element list row. The row stays a viewer-owned
    /// <c>&lt;button&gt;</c> (roving tabindex, <c>aria-label</c>, keyboard navigation and activation),
    /// so the fragment must be non-interactive phrasing content — a nested control would be invalid
    /// markup and would swallow the row's own activation.
    /// </summary>
    [Parameter] public RenderFragment<ModelingViewerElementContext>? ElementListContent { get; set; }

    /// <summary>CSS height of the viewer root. Default <c>480px</c>.</summary>
    [Parameter] public string Height { get; set; } = "480px";

    /// <summary>How the viewer resolves its presentation. <see cref="TmLayoutMode.Auto"/> measures its own container.</summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>Additional CSS class applied to the viewer root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Accessible name of the viewer region. Null uses the localized default.</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>
    /// Print opt-in: keep the element list in the printed output. Default false — print shows the
    /// diagram and its legends only (the list stays available on screen).
    /// </summary>
    [Parameter] public bool ShowElementListInPrint { get; set; }

    /// <summary>The shared diagram generator (registered by <c>AddTempoBlazorModeling</c>).</summary>
    [Inject] public ModelingDiagramGenerator DiagramGenerator { get; set; } = default!;

    /// <summary>The existing headless SVG renderer (registered by <c>AddTempoBlazorDiagramEditor</c>).</summary>
    [Inject] public IDiagramSvgRenderer SvgRenderer { get; set; } = default!;

    /// <summary>The optional shared notation catalog behind the legend card.</summary>
    [Inject] public ModelingNotationCatalog? NotationCatalog { get; set; }

    /// <summary>The JS runtime used for the export-only token resolution.</summary>
    [Inject] public IJSRuntime JS { get; set; } = default!;

    /// <summary>The lazy CSP-safe module handle (created from the injected runtime on first use).</summary>
    private ModelingInteropModule Interop => _interop ??= new ModelingInteropModule(JS);

    private bool IsEmpty => ModelingViewerProjection.IsEmpty(_document);

    private bool IsMobile => _resolvedLayout == TmLayoutMode.Mobile;

    private int NodeCount => _document?.Pages.Sum(page => page.Nodes.Count) ?? 0;

    private int EdgeCount => _document?.Pages.Sum(page => page.Edges.Count) ?? 0;

    private int ElementCount => _entries.Count;

    private string StateName => _document is null || IsEmpty ? "empty" : "populated";

    private string OverlayName => (DiffOverlay, Heatmap) switch
    {
        (not null, not null) => "diff-heatmap",
        (not null, null) => "diff",
        (null, not null) => "heatmap",
        _ => "none"
    };

    private string LayoutName => _resolvedLayout switch
    {
        TmLayoutMode.Mobile => "mobile",
        TmLayoutMode.Tablet => "tablet",
        _ => "desktop"
    };

    private string FitModeName => FitMode == ModelingViewerFitMode.Width ? "width" : "page";

    private DiagramFitMode DiagramFit => FitMode == ModelingViewerFitMode.Width ? FitWidth : FitPage;

    private string ZoomName => Zoom is { } zoom
        ? zoom.ToString("0.##", CultureInfo.InvariantCulture)
        : "fit";

    private string RootClass => string.Join(" ", new[]
    {
        "tm-modeling-viewer",
        $"tm-modeling-viewer--{LayoutName}",
        ShowElementListInPrint ? "tm-modeling-viewer--print-elements" : null,
        Class
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private string RootAriaLabel => string.IsNullOrWhiteSpace(AriaLabel)
        ? Loc["TmModelingViewer_AriaLabel"]
        : AriaLabel;

    /// <summary>
    /// The canvas' inline height for a WIDTH fit: the fitted content needs exactly
    /// <c>(width − 2·padding) / pageAspect</c> pixels, plus the embedded editor's own toolbar row.
    /// A width fit ignores the container height by contract, so a flexible canvas either cropped a tall
    /// diagram or left a wide one floating in empty paper (review fix, U10); sizing it from the
    /// presentation page (which hugs the content) makes the canvas follow the fitted diagram.
    /// <c>cqw</c> is the layout observer's own width (the viewer measures its container, so the
    /// component IS a query container); a browser without container queries drops the declaration and
    /// keeps the flexible canvas.
    /// </summary>
    private string? CanvasStyle
    {
        get
        {
            if (FitMode != ModelingViewerFitMode.Width || _document is null)
            {
                return null;
            }

            var page = _document.ActivePage;
            if (page.Width <= 0 || page.Height <= 0)
            {
                return null;
            }

            var aspect = (page.Width / page.Height).ToString("0.####", CultureInfo.InvariantCulture);
            return $"height:calc((100cqw - {FittedCanvasPadding}) / {aspect} + {FittedCanvasChrome})";
        }
    }

    private string HintId { get; } = $"tm-modeling-viewer-hint-{Guid.NewGuid():N}";

    private string ListTitleId { get; } = $"tm-modeling-viewer-elements-{Guid.NewGuid():N}";

    private string EmptyTitleId { get; } = $"tm-modeling-viewer-empty-title-{Guid.NewGuid():N}";

    private string LegendsTitleId { get; } = $"tm-modeling-viewer-legends-{Guid.NewGuid():N}";

    /// <summary>
    /// Per-instance id of the row description referenced by the button's <c>aria-describedby</c>.
    /// Built from an instance Guid, never <see cref="object.GetHashCode"/> (two viewers of the same
    /// model must not collide in one document).
    /// </summary>
    private string RowDescriptionId(string key)
        => $"tm-modeling-viewer-row-{key.Replace(':', '-')}-{_instanceId}";

    /// <summary>The element list collapsed/expanded state (mobile always shows the list).</summary>
    private bool IsElementListVisible => ShowElementList && (IsMobile || _elementListExpanded);

    /// <summary>Whether the diff / heatmap legend cards render at all.</summary>
    private bool HasOverlayLegends => DiffOverlay is not null || Heatmap is not null;

    /// <summary>
    /// Whether the notation legend renders. It is desktop/tablet chrome: on the mobile band the element
    /// list already names every type, and the few rows of a phone belong to the overlay cards.
    /// </summary>
    private bool ShowNotationLegend => ShowLegend && _legendEntries is not null && !IsMobile;

    /// <summary>Whether the docked legend strip renders at all.</summary>
    private bool HasLegends => HasOverlayLegends || ShowNotationLegend;

    /// <summary>The number of rows that are actually focusable — the drawn ones (removed rows are appended last).</summary>
    private int FocusableCount => _entries.Count(entry => !entry.IsRemoved);

    /// <summary>The current row contexts, in topological order (elements and relationships).</summary>
    private IReadOnlyList<ModelingViewerElementContext> RowContexts => _entries.Select(CreateContext).ToArray();

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _resolvedLayout = LayoutMode == TmLayoutMode.Auto ? TmLayoutMode.Desktop : LayoutMode;
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Model is null)
        {
            return;
        }

        if (LayoutMode != TmLayoutMode.Auto && LayoutMode != _resolvedLayout)
        {
            _resolvedLayout = LayoutMode;
        }

        SyncSelectionFromParameter();
        var regenerate = !ReferenceEquals(_modelSource, Model)
            || !string.Equals(_generatedViewId, ViewId, StringComparison.Ordinal);
        var overlaySignature = BuildOverlaySignature();
        var overlayChanged = !string.Equals(overlaySignature, _overlaySignature, StringComparison.Ordinal);
        if (regenerate)
        {
            GeneratePresentation();
        }

        ApplyFitMode();
        ApplyZoom();
        RefreshPresentation(announceOverlay: overlayChanged);
        _overlaySignature = overlaySignature;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_fitPending || _editor is null || Zoom is not null)
        {
            return;
        }

        _fitPending = false;
        try
        {
            await _editor.FitToViewAsync(DiagramFit);
        }
        catch (Exception)
        {
            // Fit is progressive enhancement: a prerender / bUnit / torn-down circuit keeps the
            // document's own scale.
        }
    }

    /// <summary>
    /// Renders the current presentation copy to a standalone SVG through the shared
    /// <see cref="IDiagramSvgRenderer"/>. Overlay paints use <c>var(--tm-…)</c> tokens in the DOM;
    /// a standalone SVG has no stylesheet, so the viewer resolves those tokens for the requested
    /// theme through the package's CSP-safe JS module and writes the concrete colours into an
    /// export-only copy. Without JS (prerender, bUnit) the overlay paint is omitted and the diagram
    /// renders with the renderer's palette.
    /// </summary>
    /// <param name="theme">Light or dark export palette.</param>
    /// <returns>The SVG markup, or an empty string when no presentation exists.</returns>
    public async Task<string> ExportSvgAsync(DiagramSvgTheme theme = DiagramSvgTheme.Light)
    {
        if (_document is null)
        {
            return string.Empty;
        }

        var document = CloneDocument(_document);
        var tokens = await ResolveExportTokensAsync(theme);
        if (tokens.Count > 0)
        {
            ReplaceTokens(document, tokens);
        }

        // A standalone SVG has no stylesheet, so a token that could not be resolved (no JS at
        // prerender, an unknown token, a torn-down circuit) must not be written into the markup: an
        // unresolvable var() would paint nothing at all. Dropping the paint falls back to the
        // renderer's own palette, which is the documented progressive enhancement.
        StripUnresolvedTokens(document);

        return SvgRenderer.RenderSvg(document, new DiagramSvgRenderOptions { Theme = theme });
    }

    /// <summary>The export-only token resolution (a JS failure yields an empty map).</summary>
    private async Task<IReadOnlyDictionary<string, string>> ResolveExportTokensAsync(DiagramSvgTheme theme)
    {
        if (_disposed)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var resolved = await Interop.InvokeSafeAsync<Dictionary<string, string>>(
            "resolveTokens",
            _root,
            ModelingViewerProjection.ViewerTokens,
            theme == DiagramSvgTheme.Dark ? "dark" : "light");
        return resolved ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static void ReplaceTokens(DiagramDocument document, IReadOnlyDictionary<string, string> tokens)
    {
        foreach (var page in document.Pages)
        {
            foreach (var node in page.Nodes)
            {
                node.Style.Fill = ReplaceTokensInPaint(node.Style.Fill, tokens);
                node.Style.Stroke = ReplaceTokensInPaint(node.Style.Stroke, tokens);
                node.Style.Color = ReplaceTokensInPaint(node.Style.Color, tokens);
            }

            foreach (var edge in page.Edges)
            {
                edge.Style.Fill = ReplaceTokensInPaint(edge.Style.Fill, tokens);
                edge.Style.Stroke = ReplaceTokensInPaint(edge.Style.Stroke, tokens);
            }
        }
    }

    private static string? ReplaceTokensInPaint(string? paint, IReadOnlyDictionary<string, string> tokens)
    {
        if (string.IsNullOrWhiteSpace(paint) || !paint.Contains("var(", StringComparison.Ordinal))
        {
            return paint;
        }

        return VarTokenRegex.Replace(paint, match =>
            tokens.TryGetValue(match.Groups[1].Value, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : match.Value);
    }

    /// <summary>The export-only pass that drops any paint still holding an unresolvable token.</summary>
    private static void StripUnresolvedTokens(DiagramDocument document)
    {
        foreach (var page in document.Pages)
        {
            foreach (var node in page.Nodes)
            {
                node.Style.Fill = StripUnresolvedPaint(node.Style.Fill);
                node.Style.Stroke = StripUnresolvedPaint(node.Style.Stroke);
                node.Style.Color = StripUnresolvedPaint(node.Style.Color);
            }

            foreach (var edge in page.Edges)
            {
                edge.Style.Fill = StripUnresolvedPaint(edge.Style.Fill);
                edge.Style.Stroke = StripUnresolvedPaint(edge.Style.Stroke);
            }
        }
    }

    private static string? StripUnresolvedPaint(string? paint)
        => paint is not null && paint.Contains("var(", StringComparison.Ordinal) ? null : paint;

    private static DiagramDocument CloneDocument(DiagramDocument source)
    {
        // A JSON round-trip is the package's own deep-clone path (the diagram document converter
        // owns the shape), so an export can never mutate the live presentation copy.
        var json = JsonSerializer.Serialize(source, DiagramJsonOptions.Default);
        return JsonSerializer.Deserialize<DiagramDocument>(json, DiagramJsonOptions.Default)
            ?? new DiagramDocument();
    }

    private void GeneratePresentation()
    {
        _modelSource = Model;
        _generatedViewId = ViewId;
        _legendEntries = null;
        var result = DiagramGenerator.Generate(
            Model,
            new ModelingDiagramGenerationOptionsDto
            {
                ViewId = string.IsNullOrWhiteSpace(ViewId) ? null : ViewId,
                IncludeIssues = true
            });
        _document = result.Document;
        // The page hugs the content: a fitted diagram belongs on a sheet its own size, not in the
        // top-left of the generator's default 3000x2000 page (review fix, U10).
        ModelingViewerProjection.FitPageToContent(_document);
        _fitPending = Zoom is null;
        _appliedZoom = null;
    }

    /// <summary>
    /// Re-arms the fit when the host flips <see cref="FitMode"/> at runtime. Without this the viewer
    /// kept the scale the first render requested: the host's "Fit width → Fit page" select changed
    /// the attribute and nothing moved.
    /// </summary>
    private void ApplyFitMode()
    {
        if (string.Equals(_appliedFitMode, FitModeName, StringComparison.Ordinal))
        {
            return;
        }

        _appliedFitMode = FitModeName;
        if (Zoom is null && _document is not null)
        {
            _fitPending = true;
        }
    }

    private void ApplyZoom()
    {
        if (Zoom == _appliedZoom)
        {
            return;
        }

        _appliedZoom = Zoom;
        if (_document is null || Zoom is not { } zoom)
        {
            _fitPending = Zoom is null;
            return;
        }

        var scale = Math.Clamp(zoom, 0.15, 5);
        foreach (var page in _document.Pages)
        {
            page.Scale = scale;
        }

        _fitPending = false;
    }

    private void RefreshPresentation(bool announceOverlay)
    {
        if (_document is null)
        {
            _entries = [];
            _summary = ModelingViewerOverlaySummary.Empty;
            return;
        }

        _summary = ModelingViewerProjection.ApplyPresentation(
            _document,
            DiffOverlay,
            Heatmap,
            HighlightElementIds,
            _selectedId);
        _entries = ModelingViewerProjection.BuildEntries(Model, _document, DiffOverlay, Heatmap, _summary);
        ApplyViewerTitles();
        _legendEntries = BuildLegendEntries();
        // The roving tab stop indexes the DRAWN rows only — a removed row (appended last) is not a
        // tab stop and must never swallow Home / End / ArrowDown.
        _focusedListIndex = Math.Clamp(_focusedListIndex, 0, Math.Max(0, FocusableCount - 1));

        if (announceOverlay)
        {
            AnnounceOverlay();
        }
    }

    /// <summary>
    /// Writes the overlay readings into the presentation copy's text hooks: <c>Data["viewerTitle"]</c>
    /// (the canvas node tooltip / accessible name) and <see cref="DiagramEdge.Title"/>. The overlay
    /// reading ("Name: value (band)") is not derivable from the picture, so it must travel in text.
    /// </summary>
    private void ApplyViewerTitles()
    {
        if (_document is null)
        {
            return;
        }

        var entriesById = new Dictionary<string, ModelingViewerListEntry>(StringComparer.Ordinal);
        foreach (var entry in _entries)
        {
            if (!entry.IsRemoved)
            {
                entriesById[entry.Id] = entry;
            }
        }

        foreach (var page in _document.Pages)
        {
            foreach (var node in page.Nodes)
            {
                node.Data.Remove(ModelingViewerProjection.ViewerTitleKey);
                var elementId = node.Data.TryGetValue("modelElementId", out var raw) ? raw?.ToString() : null;
                if (!string.IsNullOrWhiteSpace(elementId)
                    && entriesById.TryGetValue(elementId, out var elementEntry)
                    && EntryTitle(elementEntry) is { } elementTitle)
                {
                    node.Data[ModelingViewerProjection.ViewerTitleKey] = elementTitle;
                }
            }

            foreach (var edge in page.Edges)
            {
                edge.Title = entriesById.TryGetValue(edge.Id, out var edgeEntry) ? EntryTitle(edgeEntry) : null;
            }
        }
    }

    /// <summary>
    /// The text a canvas item carries: the heat reading with its band ("SLA risk: 80 % — High
    /// (75–100 %)") or the diff state word. Null when the item has no overlay reading.
    /// </summary>
    private string? EntryTitle(ModelingViewerListEntry entry)
    {
        if (entry.HeatmapValue is { } value)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Loc["TmModelingViewer_HeatmapTooltip"],
                HeatValueText(value),
                Loc[ModelingViewerProjection.HeatBandResourceKey(value)]);
        }

        return DiffStateLabel(entry.DiffState);
    }

    /// <summary>
    /// The overlay signature decides whether a pass announces its overlay. It must NOT include the
    /// "regenerate" flag: a selection-only re-render (same model, same overlay) has to keep the
    /// selection announcement instead of re-announcing the overlay over it.
    /// </summary>
    private string BuildOverlaySignature()
        => string.Join(
            "|",
            RuntimeHelpers.GetHashCode(Model),
            ViewId,
            DiffOverlay?.BaselineModel is null ? "-" : RuntimeHelpers.GetHashCode(DiffOverlay.BaselineModel),
            DiffOverlay?.AddedElementIds is null ? "-" : string.Join(",", DiffOverlay.AddedElementIds),
            DiffOverlay?.RemovedElementIds is null ? "-" : string.Join(",", DiffOverlay.RemovedElementIds),
            DiffOverlay?.ChangedElementIds is null ? "-" : string.Join(",", DiffOverlay.ChangedElementIds),
            DiffOverlay?.LayoutOnlyElementIds is null ? "-" : string.Join(",", DiffOverlay.LayoutOnlyElementIds),
            DiffOverlay?.AddedRelationshipIds is null ? "-" : string.Join(",", DiffOverlay.AddedRelationshipIds),
            DiffOverlay?.RemovedRelationshipIds is null ? "-" : string.Join(",", DiffOverlay.RemovedRelationshipIds),
            Heatmap is null ? "-" : string.Join(",", Heatmap.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value.ToString("R", CultureInfo.InvariantCulture)}")));

    private IReadOnlyList<ModelingNotationCatalogEntry>? BuildLegendEntries()
    {
        if (!ShowLegend || NotationCatalog is null || _document is null)
        {
            return null;
        }

        var notation = ResolveNotation();
        if (string.IsNullOrWhiteSpace(notation))
        {
            return null;
        }

        var edgeIds = new HashSet<string>(
            _document.Pages.SelectMany(page => page.Edges).Select(edge => edge.Id),
            StringComparer.Ordinal);
        var relationshipTypes = Model.Relationships
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Id)
                && edgeIds.Contains(relationship.Id)
                && !string.IsNullOrWhiteSpace(relationship.RelationshipType))
            .Select(relationship => relationship.RelationshipType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return NotationCatalog.GetLegend(notation, _document, relationshipTypes, Loc);
    }

    private string? ResolveNotation()
    {
        if (!string.IsNullOrWhiteSpace(Model.Notation)
            && !string.Equals(Model.Notation, "mixed", StringComparison.OrdinalIgnoreCase))
        {
            return Model.Notation;
        }

        foreach (var element in Model.Elements)
        {
            if (!string.IsNullOrWhiteSpace(element.Notation))
            {
                return element.Notation;
            }
        }

        return string.IsNullOrWhiteSpace(Model.Notation) ? null : Model.Notation;
    }

    private void SyncSelectionFromParameter()
    {
        if (string.Equals(_selectedId, SelectedElementId, StringComparison.Ordinal))
        {
            return;
        }

        _selectedId = SelectedElementId;
        var index = IndexOfEntry(_selectedId);
        if (index >= 0 && index < FocusableCount)
        {
            _focusedListIndex = index;
        }
    }

    private int IndexOfEntry(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return -1;
        }

        for (var index = 0; index < _entries.Count; index++)
        {
            if (string.Equals(_entries[index].Id, id, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private async Task TrySelectAsync(string? id, bool raiseClick)
    {
        var changed = !string.Equals(_selectedId, id, StringComparison.Ordinal);
        _selectedId = id;
        if (changed)
        {
            RefreshPresentation(announceOverlay: false);
            AnnounceSelection();
            await SelectedElementIdChanged.InvokeAsync(id);
        }

        if (raiseClick && !string.IsNullOrWhiteSpace(id))
        {
            var entry = _entries.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
            if (entry is not null)
            {
                await OnElementClick.InvokeAsync(new ModelingViewerElementEventArgs
                {
                    ElementId = entry.Id,
                    IsRelationship = entry.IsRelationship,
                    SemanticType = entry.SemanticType,
                    Name = entry.Name
                });
            }
        }

        if (changed)
        {
            StateHasChanged();
        }
    }

    private string? MapDiagramId(string diagramId)
    {
        if (_document is null)
        {
            return diagramId;
        }

        foreach (var node in _document.Pages.SelectMany(page => page.Nodes))
        {
            if (string.Equals(node.Id, diagramId, StringComparison.Ordinal))
            {
                return node.Data.TryGetValue("modelElementId", out var modelElementId)
                    ? modelElementId?.ToString()
                    : diagramId;
            }
        }

        return diagramId;
    }

    private async Task HandleSelectedIdsChangedAsync(string[] selectedIds)
    {
        if (selectedIds.Length == 0)
        {
            await TrySelectAsync(null, raiseClick: false);
            return;
        }

        if (selectedIds.Length != 1)
        {
            return;
        }

        await TrySelectAsync(MapDiagramId(selectedIds[0]), raiseClick: true);
    }

    private Task HandleEntryClickedAsync(ModelingViewerListEntry entry)
        => TrySelectAsync(entry.Id, raiseClick: true);

    private async Task HandleListKeyDownAsync(KeyboardEventArgs args, int index)
    {
        // Removed rows (appended after the drawn ones) are not tab stops and not focusable, so the
        // navigation bound is the DRAWN row count — otherwise End landed on a nonexistent button.
        var count = FocusableCount;
        if (count == 0)
        {
            return;
        }

        var next = args.Key switch
        {
            "ArrowDown" => Math.Min(index + 1, count - 1),
            "ArrowUp" => Math.Max(index - 1, 0),
            "Home" => 0,
            "End" => count - 1,
            _ => -1
        };
        if (next < 0)
        {
            return;
        }

        _focusedListIndex = next;
        StateHasChanged();
        // Focus is progressive enhancement (prerender / bUnit has no DOM); the roving tabindex
        // still moves the tab path. The list is scoped to this component's own root element.
        await Interop.InvokeVoidSafeAsync(
            "focusWithin",
            _listRoot,
            $"[data-list-index='{next}']");
    }

    private void OnResolvedLayoutChanged(TmLayoutMode mode)
        => _resolvedLayout = mode == TmLayoutMode.Auto ? TmLayoutMode.Desktop : mode;

    private void ToggleElementList() => _elementListExpanded = !_elementListExpanded;

    private void ToggleLegends() => _legendsExpanded = !_legendsExpanded;

    /// <summary>
    /// The CSS hook of one heat step. The token name has to lose its leading dashes: a class name
    /// cannot contain them, and the previous interpolation produced four dashes
    /// (<c>scale-step----tm-color-heat-3</c>), so every swatch lost its background.
    /// </summary>
    internal static string HeatStepClass(string token)
        => $"tm-modeling-viewer__scale-step tm-modeling-viewer__scale-step--{token.TrimStart('-')}";

    private string LegendsToggleLabel => _legendsExpanded
        ? Loc["TmModelingViewer_HideLegends"]
        : Loc["TmModelingViewer_ShowLegends"];

    /// <summary>
    /// The extra word a legend row needs to explain where its items actually are: a removed item is
    /// never drawn, so the "Removed" row points at the list below it.
    /// </summary>
    private string? DiffLegendHint(string state)
        => state == ModelingViewerProjection.DiffRemoved && RemovedEntries.Count > 0
            ? Loc["TmModelingViewer_DiffRemovedHint"]
            : null;

    private ModelingViewerElementContext CreateContext(ModelingViewerListEntry entry)
        => new()
        {
            ElementId = entry.Id,
            IsRelationship = entry.IsRelationship,
            SemanticType = entry.SemanticType,
            Name = entry.Name,
            IsSelected = string.Equals(_selectedId, entry.Id, StringComparison.Ordinal),
            IsRemoved = entry.IsRemoved,
            DiffState = entry.DiffState,
            HeatmapValue = entry.HeatmapValue,
            HeatmapValueText = entry.HeatmapValue is { } value
                ? ModelingViewerProjection.FormatHeatValue(Heatmap, value)
                : null
        };

    private string TypeLabel(ModelingViewerListEntry entry)
    {
        var label = _legendEntries?.FirstOrDefault(candidate =>
            string.Equals(candidate.SemanticType, entry.SemanticType, StringComparison.OrdinalIgnoreCase)
            && candidate.IsRelationship == entry.IsRelationship)?.Label;
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label!;
        }

        return string.IsNullOrWhiteSpace(entry.SemanticType)
            ? Loc["TmModelingViewer_UnknownType"]
            : ModelingNotationCatalog.Humanize(entry.SemanticType);
    }

    private string EntryAriaLabel(ModelingViewerListEntry entry)
        => string.Format(
            CultureInfo.CurrentCulture,
            Loc["TmModelingViewer_ElementAriaLabel"],
            TypeLabel(entry),
            entry.Name);

    private string? DiffStateLabel(string? state) => state switch
    {
        ModelingViewerProjection.DiffAdded => Loc["TmModelingViewer_DiffAdded"],
        ModelingViewerProjection.DiffChanged => Loc["TmModelingViewer_DiffChanged"],
        ModelingViewerProjection.DiffLayout => Loc["TmModelingViewer_DiffLayout"],
        ModelingViewerProjection.DiffRemoved => Loc["TmModelingViewer_DiffRemoved"],
        ModelingViewerProjection.HighlightState => Loc["TmModelingViewer_Highlighted"],
        _ => null
    };

    private string? EntryDescription(ModelingViewerListEntry entry)
    {
        var parts = new List<string>();
        if (DiffStateLabel(entry.DiffState) is { } state)
        {
            parts.Add(state);
        }

        if (entry.HeatmapValue is { } value)
        {
            parts.Add(HeatValueText(value));
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private string HeatValueText(double value)
        => string.Format(
            CultureInfo.CurrentCulture,
            Loc["TmModelingViewer_HeatmapValue"],
            string.IsNullOrWhiteSpace(Heatmap?.ValueLabel) ? Loc["TmModelingViewer_HeatmapTitle"] : Heatmap!.ValueLabel,
            ModelingViewerProjection.FormatHeatValue(Heatmap, value));

    private string? SelectedHeatValueText()
    {
        if (Heatmap is null || string.IsNullOrWhiteSpace(_selectedId)
            || !Heatmap.Values.TryGetValue(_selectedId, out var value))
        {
            return null;
        }

        return HeatValueText(value);
    }

    private string? DiffLegendTitle
        => string.IsNullOrWhiteSpace(DiffOverlay?.LegendTitle)
            ? Loc["TmModelingViewer_DiffLegendTitle"]
            : DiffOverlay!.LegendTitle;

    private string HeatmapTitle
        => string.IsNullOrWhiteSpace(Heatmap?.ValueLabel) ? Loc["TmModelingViewer_HeatmapTitle"] : Heatmap!.ValueLabel!;

    private string HeatmapMinLabel
        => string.IsNullOrWhiteSpace(Heatmap?.LegendMinLabel)
            ? string.Format(CultureInfo.CurrentCulture, Loc["TmModelingViewer_HeatmapMinDefault"], 0)
            : Heatmap!.LegendMinLabel!;

    private string HeatmapMaxLabel
        => string.IsNullOrWhiteSpace(Heatmap?.LegendMaxLabel)
            ? string.Format(CultureInfo.CurrentCulture, Loc["TmModelingViewer_HeatmapMaxDefault"], 100)
            : Heatmap!.LegendMaxLabel!;

    private (string State, string Label, int Count)[] DiffLegendRows =>
    [
        (ModelingViewerProjection.DiffAdded, Loc["TmModelingViewer_DiffAdded"], _summary.AddedCount),
        (ModelingViewerProjection.DiffRemoved, Loc["TmModelingViewer_DiffRemoved"], _summary.RemovedCount),
        (ModelingViewerProjection.DiffChanged, Loc["TmModelingViewer_DiffChanged"], _summary.ChangedCount),
        (ModelingViewerProjection.DiffLayout, Loc["TmModelingViewer_DiffLayout"], _summary.LayoutCount)
    ];

    private (string Label, string Token)[] HeatmapScaleRows =>
    [
        (Loc["TmModelingViewer_HeatBandLow"], ModelingViewerProjection.TokenHeat0),
        (Loc["TmModelingViewer_HeatBandModerate"], ModelingViewerProjection.TokenHeat1),
        (Loc["TmModelingViewer_HeatBandElevated"], ModelingViewerProjection.TokenHeat2),
        (Loc["TmModelingViewer_HeatBandHigh"], ModelingViewerProjection.TokenHeat3)
    ];

    private string ElementListToggleLabel => _elementListExpanded
        ? Loc["TmModelingViewer_HideElements"]
        : string.Format(CultureInfo.CurrentCulture, Loc["TmModelingViewer_ShowElements"], _entries.Count);

    private string ElementListTitle
        => string.Format(CultureInfo.CurrentCulture, Loc["TmModelingViewer_Elements"], _entries.Count);

    private IReadOnlyList<ModelingViewerListEntry> DrawnEntries
        => _entries.Where(entry => !entry.IsRemoved).ToArray();

    private IReadOnlyList<ModelingViewerListEntry> RemovedEntries
        => _entries.Where(entry => entry.IsRemoved).ToArray();

    private void AnnounceSelection()
    {
        if (string.IsNullOrWhiteSpace(_selectedId))
        {
            Announce(Loc["TmModelingViewer_SelectionCleared"]);
            return;
        }

        var entry = _entries.FirstOrDefault(candidate => string.Equals(candidate.Id, _selectedId, StringComparison.Ordinal));
        Announce(entry is null
            ? Loc["TmModelingViewer_SelectionCleared"]
            : string.Format(CultureInfo.CurrentCulture, Loc["TmModelingViewer_SelectedAnnouncement"], TypeLabel(entry), entry.Name));
    }

    private void AnnounceOverlay()
    {
        if (DiffOverlay is not null)
        {
            Announce(string.Format(
                CultureInfo.CurrentCulture,
                Loc["TmModelingViewer_DiffAnnouncement"],
                _summary.AddedCount,
                _summary.RemovedCount,
                _summary.ChangedCount,
                _summary.LayoutCount));
            return;
        }

        if (Heatmap is not null)
        {
            Announce(string.Format(
                CultureInfo.CurrentCulture,
                Loc["TmModelingViewer_HeatmapAnnouncement"],
                Heatmap.Values.Count));
            return;
        }

        Announce(string.Empty);
    }

    private void Announce(string text)
    {
        if (string.Equals(_announcement, text, StringComparison.Ordinal))
        {
            return;
        }

        _announcement = text;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        return _interop?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

}
