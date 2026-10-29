using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>Loads a semantic modeling source and presents an initial modeling editor shell.</summary>
public partial class TmModelingEditor : IAsyncDisposable
{
    private const int MinimumLoadingDelayMilliseconds = 150;
    private const int LongLoadingMessageDelayMilliseconds = 5_000;

    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private int _completedLoadCount;
    private int _completedGenerationCount;
    private ModelingEditorState _state = ModelingEditorState.Empty;
    private ModelingModelDto? _model;
    private DiagramDocument? _document;
    private DiagramDocument? _openedEditorDocument;
    private IReadOnlyList<ModelingIssueDto> _issues = [];
    private ModelingElementDto? _selectedElement;
    private ModelingRelationshipDto? _selectedRelationship;
    private ModelingElementDto? _draggedElement;
    private IReadOnlyCollection<string>? _collapsedGroups;
    private string? _errorMessage;
    private string? _activeLoadKey;
    private string? _effectiveNotationKey;
    private string? _effectiveViewpointKey;
    private string? _effectiveProviderKey;
    private string? _lastNotationParameter;
    private string? _lastViewpointParameter;
    private string? _lastProviderParameter;
    private IReadOnlyList<ModelingElementDto>? _visibleTreeElementsSource;
    private IReadOnlyList<ModelingElementDto> _visibleTreeElements = [];
    private string? _visibleTreeElementsNotationKey;
    private string? _visibleTreeElementsViewpointKey;
    private bool _showLongLoadingMessage;
    private bool _isReloading;
    private ModelingEditorPanel _activePanel = ModelingEditorPanel.Preview;
    private bool _shouldFocusOpenEditorCloseButton;
    private bool _shouldRestoreOpenButtonFocus;
    private bool _focusInspectorAfterRender;

    /// <summary>Armed by a desktop issues-dock collapse/expand (final UX review M5): the next
    /// render re-fits the embedded preview through <see cref="TmModelingDiagramPreview.RefreshViewportAsync"/>.</summary>
    private bool _refitPreviewAfterRender;
    private ModelingInteropModule? _interopModule;

    /// <summary>The editor ROOT section — the focusWithin scope (final review fix): it contains
    /// the open-editor close button, the preview trigger and every mobile focus-return host in
    /// EVERY layout, so the overlay focus move and the restore work across the 768 boundary
    /// where the single dialog instance now lives outside both layout branches.</summary>
    private ElementReference _editorRoot;

    // ── Fáze 5 (MO1): docked-layout state ────────────────────────────────────
    // The shell's two-way state lives on the editor so a layout flip cannot disagree with it.
    // Fáze 6 review fix: the TABLET sheets carry their own flags (starting closed) so entering
    // the tablet band never forces the desktop dock open — see TmModelingEditor.Tablet.cs.
    private bool _shellLeftOpen = true;
    private bool _shellRightOpen = true;
    private EditorShellPanel _shellCollapsed;
    private bool _issuesCollapsed;


    /// <summary>The resolved layout of the editor's own container (Fáze 6): the compact branch
    /// keeps the panel-tab state, the docked branch uses it for the tablet sheet rules. A forced
    /// mode adopts in OnInitialized/OnParametersSet; an Auto editor follows the shell's
    /// <see cref="TmEditorShell.ResolvedLayoutChanged"/> (the shell measures the same box).</summary>
    private TmLayoutMode _resolvedLayout = TmLayoutMode.Desktop;

    /// <summary>The docked shell (panel DOM ids for the tablet toggles' aria-controls).</summary>
    private TmEditorShell? _shell;

    /// <summary>The diagram preview (the non-drag "Insert into preview" action routes through its drop path).</summary>
    private TmModelingDiagramPreview? _preview;

    /// <summary>The selected inspector tab of the docked layout: "properties" (default) or "source".</summary>
    private string _inspectorTab = "properties";

    private readonly string _openEditorTriggerId = $"tm-modeling-open-editor-trigger-{Guid.NewGuid():N}";

    /// <summary>
    /// Layout thresholds of the EMBEDDED open-editor diagram editor: 768 / 1100 (Fáze 6 review
    /// fix) so the dialog body — NOT the editor's resolved mode — decides the editor's
    /// <c>data-editor-layout</c>. The dialog is ~90vw × 90vh: a 1024 or 1180 viewport renders a
    /// 922/1062px body, which must resolve tablet, while a desktop body (≥1100px) resolves
    /// desktop — the shared 640/1024 pair would flip the embedded editor at both ends, and the
    /// old 768/1024 pair pinned a 1180 tablet dialog to desktop.
    /// </summary>
    private static readonly Tempo.Blazor.Abstractions.Layout.TmLayoutBreakpoints OpenEditorBreakpoints = new(768, 1100);

    /// <summary>
    /// Layout thresholds of the editor's own container (Fáze 5, MO1): the same pair as the
    /// <see cref="TmDiagramEditor"/> (768 / 1200) so the two editors flip layouts at the same
    /// container width — below the 768px mobile threshold the compact panel-tab workspace
    /// renders, 768–1199px is the shell's tablet dock (one expanded panel) and from 1200px the
    /// full docked desktop workspace (tree + inspector + canvas at once).
    /// </summary>
    private static readonly Tempo.Blazor.Abstractions.Layout.TmLayoutBreakpoints ModelingBreakpoints = new(768, 1200);

    /// <summary>The TestIdPrefix the cached attribute dictionaries were built with.</summary>
    private string? _cachedTestIdPrefix;

    /// <summary>Cached attributes of the open-editor TmModal overlay root (the preserved dialog test id).</summary>
    private Dictionary<string, object>? _openEditorModalAttributes;

    /// <summary>Cached attributes of the TmModal close button (the preserved test id + the focus hook).</summary>
    private Dictionary<string, object>? _openEditorCloseButtonAttributes;

    /// <summary>Cached attributes of the docked inspector's Properties tab button (the frozen tab test id).</summary>
    private Dictionary<string, object>? _propertiesTabAttributes;

    /// <summary>
    /// Overrides the localStorage key the docked shell persists user-resized panel widths under.
    /// Null (default) derives the key from <see cref="TmComponentBase.TestIdPrefix"/> —
    /// <c>{TestIdPrefix}-modeling-editor</c>, or plain <c>modeling-editor</c> without a prefix —
    /// so two editors on one page share persisted widths only when they share the prefix. Set it
    /// explicitly to namespace (or intentionally share) the saved layout per instance.
    /// </summary>
    [Parameter] public string? PersistKey { get; set; }

    /// <summary>The persistence key handed to the shell's <see cref="TmEditorShell.PersistWidthsKey"/>.</summary>
    private string EffectivePersistWidthsKey
        => string.IsNullOrWhiteSpace(PersistKey)
            ? (string.IsNullOrWhiteSpace(TestIdPrefix) ? "modeling-editor" : $"{TestIdPrefix}-modeling-editor")
            : PersistKey;

    /// <summary>
    /// Attributes of the open-editor TmModal overlay root. Cached per <see cref="TmComponentBase.TestIdPrefix"/>
    /// (the dictionary is re-created only when the prefix changes) so every render of the open dialog
    /// reuses the same instance.
    /// </summary>
    private Dictionary<string, object> OpenEditorModalAttributes
        => BuildCachedAttributes(ref _openEditorModalAttributes);

    /// <summary>
    /// Attributes of the TmModal close button. Cached alongside <see cref="OpenEditorModalAttributes"/>.
    /// </summary>
    private Dictionary<string, object> OpenEditorCloseButtonAttributes
        => BuildCachedAttributes(ref _openEditorCloseButtonAttributes);

    /// <summary>
    /// Attributes of the Properties tab button (the frozen tab test id). Cached under the same
    /// TestIdPrefix guard, so one prefix change rebuilds every cached dictionary.
    /// </summary>
    private IReadOnlyDictionary<string, object> PropertiesTabAttributes
        => BuildCachedAttributes(ref _propertiesTabAttributes);

    /// <summary>Returns the cached dictionary, rebuilding all of them when the TestIdPrefix changed.</summary>
    private Dictionary<string, object> BuildCachedAttributes(ref Dictionary<string, object>? cached)
    {
        if (cached is null || _cachedTestIdPrefix != TestIdPrefix)
        {
            _cachedTestIdPrefix = TestIdPrefix;
            _openEditorModalAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["data-testid"] = TestId("modeling-open-diagram-editor")
            };
            _openEditorCloseButtonAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["data-testid"] = TestId("modeling-open-diagram-close"),
                ["data-modeling-focus"] = "open-editor-close"
            };
            _propertiesTabAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["data-testid"] = TestId("modeling-tab-properties")
            };
        }

        return cached;
    }

    /// <summary>Scoped accessor for the shared, disposed-guarded module wrapper (the [Inject]
    /// runtime is only available after construction, so the wrapper is created lazily).</summary>
    private ModelingInteropModule Interop => _interopModule ??= new ModelingInteropModule(JSRuntime);

    /// <summary>Test seam (InternalsVisibleTo): the lazily created module wrapper, or null before first use.</summary>
    internal ModelingInteropModule? TestInteropModule => _interopModule;

    /// <summary>All registered semantic model providers available to the editor.</summary>
    [Inject] public IEnumerable<IModelingModelProvider> ModelProviders { get; set; } = [];

    /// <summary>Generator that converts semantic models into diagram documents.</summary>
    [Inject] public ModelingDiagramGenerator DiagramGenerator { get; set; } = default!;

    /// <summary>Viewpoint rules used to scope the model tree and generated diagram.</summary>
    [Inject] public IModelingViewpointRulesProvider? ViewpointRules { get; set; }

    /// <summary>JavaScript runtime used for focus restoration after editor overlay transitions.</summary>
    [Inject] public IJSRuntime JSRuntime { get; set; } = default!;

    /// <summary>Clock the loading delays run on; falls back to <see cref="TimeProvider.System"/> when
    /// the host registered none (the AddTempoBlazorModeling default registers one). Injectable so a
    /// test advances a FakeTimeProvider past the minimum-loading delay instead of sleeping.</summary>
    [Inject] public TimeProvider? Clock { get; set; }

    /// <summary>Provider key used to select the semantic model provider. Null or empty values render the empty state.</summary>
    [Parameter] public string? ProviderKey { get; set; }

    /// <summary>
    /// Renders a labeled "Provider" select in the editor header that lists every registered
    /// <see cref="IModelingModelProvider"/> (MO3). Default <c>false</c> keeps the pre-selector
    /// behavior: the provider stays a labeled value chip and remains host-owned through
    /// <see cref="ProviderKey"/>.
    /// </summary>
    [Parameter] public bool ShowProviderSelector { get; set; }

    /// <summary>Raised when the user picks a provider in the header selector; enables <c>@bind-ProviderKey</c>. The editor reloads the model with the selected provider.</summary>
    [Parameter] public EventCallback<string?> ProviderKeyChanged { get; set; }

    /// <summary>Raised when the editor provider selection changes (semantics of <see cref="OnNotationChanged"/> for the provider).</summary>
    [Parameter] public EventCallback<string?> OnProviderChanged { get; set; }

    /// <summary>Optional display-name resolver for the provider options and the provider chip
    /// (review fix, phase 3). Receives the raw provider key and returns the text to show; when
    /// the callback is null (or returns nothing) the raw key itself is shown. The demo maps the
    /// keys to localized names; a host may resolve them from its own registry.</summary>
    [Parameter] public Func<string, string>? ProviderDisplayName { get; set; }

    /// <summary>Optional notation key passed to the selected provider and generator.</summary>
    [Parameter] public string? NotationKey { get; set; }

    /// <summary>Optional viewpoint key passed to the selected provider and used to choose the initial model view.</summary>
    [Parameter] public string? ViewpointKey { get; set; }

    /// <summary>Callback raised after a model is loaded and converted into a diagram document.</summary>
    [Parameter] public EventCallback<DiagramDocument> OnDiagramGenerated { get; set; }

    /// <summary>Callback raised when the user opens the generated diagram in the full diagram editor.</summary>
    [Parameter] public EventCallback<DiagramDocument> OnOpenInEditor { get; set; }

    /// <summary>Callback raised when the editor notation selection changes.</summary>
    [Parameter] public EventCallback<string?> OnNotationChanged { get; set; }

    /// <summary>Callback raised when the editor viewpoint selection changes.</summary>
    [Parameter] public EventCallback<string?> OnViewpointChanged { get; set; }

    /// <summary>Whether model tree elements can be reused by dropping them onto the preview canvas.</summary>
    [Parameter] public bool AllowNodeDrop { get; set; } = true;

    /// <summary>Callback raised after a model tree element is reused as a new preview node.</summary>
    [Parameter] public EventCallback<ModelingNodeDroppedEventArgs> OnNodeDropped { get; set; }

    /// <summary>Additional CSS class applied to the editor root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// How the editor resolves its presentation (Fáze 5, MO1). <see cref="TmLayoutMode.Auto"/>
    /// (default) measures the editor's own container with the shared observer; a forced mode
    /// renders that layout without any measurement so tests and hosts can pin it. The docked
    /// layouts render through the core <see cref="TmEditorShell"/>; the compact layout keeps the
    /// panel-tab workspace.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The mode rendered before the first measurement of an Auto editor (SSR, prerender).</summary>
    [Parameter] public TmLayoutMode InitialMode { get; set; } = TmLayoutMode.Desktop;

    /// <summary>
    /// Per-instance thresholds for the editor's own container. Null keeps the Modeling pair
    /// (mobile below 768px of container, tablet 768–1199, desktop from 1200 — the same pair as
    /// the <see cref="TmDiagramEditor"/>, so hosts embedding both flip them together).
    /// </summary>
    [Parameter] public TmLayoutBreakpoints? LayoutBreakpoints { get; set; }

    /// <summary>
    /// The editor's block size (a CSS length only — e.g. <c>600px</c>, <c>100%</c>,
    /// <c>100dvh</c>; not a keyword or a shorthand). Default <c>null</c> keeps the stylesheet
    /// contract <c>min(768px, 100vh - 32px)</c> (the token pair on <c>.tm-modeling-editor</c>).
    /// When set, the editor renders exactly this height (the token min-height floor is dropped
    /// with an inline <c>min-height: 0</c>) — the host owns the sizing. The shell inside fills
    /// the box.
    /// </summary>
    [Parameter] public string? Height { get; set; }

    /// <summary>Optional loader used by host pages or tests to supply a model without registering a provider.</summary>
    [Parameter] public Func<ModelingModelRequest, CancellationToken, Task<ModelingModelDto>>? ModelLoaderOverride { get; set; }

    private string RootClass => string.Join(" ", new[] { "tm-modeling-editor", Class }.Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>The editor's breakpoints: the explicit pair, else the Modeling default.</summary>
    private TmLayoutBreakpoints EffectiveBreakpoints => LayoutBreakpoints ?? ModelingBreakpoints;

    /// <summary>
    /// The inline style of the height contract: default <c>null</c> (the stylesheet owns
    /// <c>min(768px, 100vh - 32px)</c>), an explicit <see cref="Height"/> becomes the block size
    /// and drops the token floor so the host value wins exactly. A value that is not a simple CSS
    /// length or <c>calc()</c> expression (final code review: anything carrying <c>;</c>, <c>{</c>,
    /// <c>}</c> or an unknown shape) is REJECTED and falls back to the stylesheet default — the
    /// inline style must never become an injection vector.
    /// </summary>
    private string? HeightStyle => IsValidCssLength(Height) ? $"height: {Height}; min-height: 0;" : null;

    /// <summary>
    /// Whether <paramref name="value"/> is a safe block-size value: a CSS length, a percentage or
    /// a single <c>calc()</c> expression — never a string containing <c>;</c>, <c>{</c> or <c>}</c>.
    /// Test seam (InternalsVisibleTo).
    /// </summary>
    internal static bool IsValidCssLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(';') || value.Contains('{') || value.Contains('}'))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.StartsWith("calc(", StringComparison.OrdinalIgnoreCase) && candidate.EndsWith(')'))
        {
            return CssCalcExpression().IsMatch(candidate);
        }

        return CssLengthExpression().IsMatch(candidate);
    }

    /// <summary>A single calc() expression: the safe charset only (numbers, units, operators,
    /// parentheses and whitespace) — no semicolons, braces, quotes or URL-ish payloads.</summary>
    [GeneratedRegex(@"^calc\([\s\-+*/0-9a-zA-Z%.()]+\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CssCalcExpression();

    /// <summary>A CSS length or percentage: an optional sign, a number (with optional decimals)
    /// and a unit — the shapes the Height documentation names.</summary>
    [GeneratedRegex(@"^-?(\d+\.?\d*|\.\d+)(px|rem|em|%|vh|vw|dvh|svh|lvh|vmin|vmax|ch|ex|pt|pc|cm|mm|in|q)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CssLengthExpression();

    /// <summary>
    /// Whether the four-region workspace is mounted (as opposed to a full-surface state screen).
    /// A force reload over a live model keeps the workspace mounted (the reload budget contract).
    /// </summary>
    private bool WorkspaceMounted => _state switch
    {
        ModelingEditorState.Empty => false,
        ModelingEditorState.Error => false,
        ModelingEditorState.Loading => _model is not null,
        _ => true
    };

    /// <summary>
    /// The canvas body class: the issues dock row is a clamped block while the panel is expanded
    /// and <c>auto</c> (header only) while collapsed. With ZERO issues the dock is a single-line
    /// empty state, so the row is <c>auto</c> as well and the preview row takes the rest (final
    /// UX review M1). The tablet band has NO dock row (the issues live in the side sheet there).
    /// The class — not an inline style — keeps the sizes in the stylesheet.
    /// </summary>
    private string CanvasBodyClass
    {
        get
        {
            const string baseClass = "tm-modeling-editor__canvas-body";
            if (IsTablet)
            {
                return baseClass;
            }

            if (IssueCount == 0)
            {
                return $"{baseClass} {baseClass}--issues-empty";
            }

            return IssuesCollapsed
                ? $"{baseClass} {baseClass}--issues-collapsed"
                : $"{baseClass} {baseClass}--issues-open";
        }
    }

    /// <summary>
    /// Whether the current configuration has a source the retry/empty screens can reload: a
    /// provider key is set or a loader override exists. The empty state offers the Reload button
    /// only then (final UX review M4) — an "empty" without a configured source has nothing to
    /// reload.
    /// </summary>
    private bool HasReloadableSource => !string.IsNullOrWhiteSpace(EffectiveProviderKey) || ModelLoaderOverride is not null;

    /// <summary>
    /// The Source tab's badge text: the localized "Stale" label while the loaded metadata is stale
    /// (MO1 — "stale source marks the Source tab"; a real word, never a bare glyph), empty (no
    /// badge) otherwise.
    /// </summary>
    private string SourceTabBadge => SourceIsStale ? Loc["TmModelingSourcePanel_Stale"] : string.Empty;

    /// <summary>Whether the loaded model's metadata exists and is marked stale.</summary>
    private bool SourceIsStale => _model?.Metadata is { } metadata && !metadata.IsFresh;

    /// <summary>Per-instance DOM id of the stale-source hint span the Source tab's
    /// <c>aria-describedby</c> points at (rendered outside the tablist).</summary>
    private readonly string _sourceStaleHintId = $"tm-modeling-source-stale-{Guid.NewGuid():N}";

    /// <summary>The TestIdPrefix and staleness the cached Source tab attributes were built with (final review: the dictionary is rebuilt only when one of them changes).</summary>
    private string? _cachedSourceTabAttributesPrefix;
    private bool _cachedSourceTabAttributesStale;
    private IReadOnlyDictionary<string, object>? _sourceTabAttributes;

    /// <summary>
    /// The Source tab button attributes: the frozen tab test id plus, while the indicator shows,
    /// the <c>data-stale</c> hook for the warning-tinted badge CSS, a <c>title</c> tooltip and an
    /// <c>aria-describedby</c> reference to the hint span rendered outside the tablist (the
    /// tab's accessible NAME stays the visible "Source" label — no aria-label override).
    /// Cached on (TestIdPrefix, SourceIsStale) — the only inputs of the dictionary.
    /// </summary>
    private IReadOnlyDictionary<string, object>? SourceTabAttributes
    {
        get
        {
            if (_sourceTabAttributes is null
                || _cachedSourceTabAttributesPrefix != TestIdPrefix
                || _cachedSourceTabAttributesStale != SourceIsStale)
            {
                _cachedSourceTabAttributesPrefix = TestIdPrefix;
                _cachedSourceTabAttributesStale = SourceIsStale;
                var attributes = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["data-testid"] = TestId("modeling-tab-source")
                };

                if (SourceIsStale)
                {
                    attributes["data-stale"] = "true";
                    attributes["title"] = Loc["TmModelingEditor_SourceStaleHint"];
                    attributes["aria-describedby"] = _sourceStaleHintId;
                }

                _sourceTabAttributes = attributes;
            }

            return _sourceTabAttributes;
        }
    }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        // The resolved layout seeds BEFORE the first render — from a forced mode, else the
        // InitialMode an Auto editor renders pre-measurement (Fáze 6 review fix: seeding through
        // <see cref="ApplyResolvedLayout"/> also arms the tablet entry defaults — sheets start
        // closed, the issues dock collapsed). The outer observer (and the shell's idempotent
        // duplicate) keep it current afterwards.
        ApplyResolvedLayout(LayoutMode != TmLayoutMode.Auto ? LayoutMode : InitialMode);
    }

    /// <inheritdoc />
    protected override Task OnParametersSetAsync()
    {
        // Forced layouts adopt their flips here; an Auto editor is driven by the shell's
        // ResolvedLayoutChanged instead (docs/responsive-conventions.md inherited-raise contract).
        if (LayoutMode != TmLayoutMode.Auto && LayoutMode != _resolvedLayout)
        {
            ApplyResolvedLayout(LayoutMode);
        }

        SyncEffectiveSelectionFromParameters();
        return LoadModelAsync(force: false);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_shouldFocusOpenEditorCloseButton)
        {
            _shouldFocusOpenEditorCloseButton = false;
            await FocusWithinAsync(OpenEditorCloseFocusSelector);
        }

        if (_focusInspectorAfterRender)
        {
            // Fáze 7 re-review: the tab switch of an issue/edge selection also MOVES FOCUS onto
            // the inspector panel — the stable data-modeling-focus hook keeps it working when a
            // host renames the test ids. Progressive enhancement: failures are swallowed.
            _focusInspectorAfterRender = false;
            await FocusWithinAsync(InspectorPanelFocusSelector);
        }

        if (_shouldRestoreOpenButtonFocus)
        {
            _shouldRestoreOpenButtonFocus = false;
            var selector = _pendingFocusReturnSelector ?? OpenEditorFocusReturnSelector;
            _pendingFocusReturnSelector = null;
            await FocusWithinAsync(selector);
        }

        // Final UX review (M5): a dock collapse/expand re-grids the canvas column — the embedded
        // editor re-measures (JS relayout) so the diagram refits the new box and no dead gap is
        // left under the chrome.
        if (_refitPreviewAfterRender)
        {
            _refitPreviewAfterRender = false;
            if (_preview is not null)
            {
                await _preview.RefreshViewportAsync();
            }
        }
    }

    private async Task LoadModelAsync(bool force)
    {
        var loadKey = BuildLoadKey();
        if (!force
            && string.Equals(_activeLoadKey, loadKey, StringComparison.Ordinal)
            && _state is ModelingEditorState.Loading or ModelingEditorState.Loaded or ModelingEditorState.Error)
        {
            return;
        }

        _activeLoadKey = loadKey;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var loadCts = _loadCts;
        var loadVersion = ++_loadVersion;

        if (string.IsNullOrWhiteSpace(EffectiveProviderKey) && ModelLoaderOverride is null)
        {
            SetEmpty();
            return;
        }

        var provider = ResolveProvider();
        if (provider is null && ModelLoaderOverride is null)
        {
            SetEmpty();
            return;
        }

        var keepLoadedSurface = force && _state == ModelingEditorState.Loaded && _model is not null;
        if (keepLoadedSurface)
        {
            _state = ModelingEditorState.Loading;
            _isReloading = true;
            _errorMessage = null;
            _showLongLoadingMessage = false;
        }
        else
        {
            _state = ModelingEditorState.Loading;
            _model = null;
            _document = null;
            _openedEditorDocument = null;
            _issues = [];
            _selectedElement = null;
            _selectedRelationship = null;
            _draggedElement = null;
            _errorMessage = null;
            _showLongLoadingMessage = false;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(MinimumLoadingDelayMilliseconds), Clock ?? TimeProvider.System, loadCts.Token);

            var request = new ModelingModelRequest
            {
                ProviderKey = EffectiveProviderKey ?? provider?.ProviderKey ?? string.Empty,
                Notation = EffectiveNotationKey,
                ViewpointKey = EffectiveViewpointKey,
                Culture = CultureInfo.CurrentUICulture.Name
            };

            var model = ModelLoaderOverride is not null
                ? ModelLoaderOverride(request, loadCts.Token)
                : provider!.GetModelAsync(request, loadCts.Token);

            var longLoadingTask = Task.Delay(TimeSpan.FromMilliseconds(LongLoadingMessageDelayMilliseconds), Clock ?? TimeProvider.System, loadCts.Token);
            var completedTask = await Task.WhenAny(model, longLoadingTask);
            if (completedTask == longLoadingTask)
            {
                _showLongLoadingMessage = true;
                await InvokeAsync(StateHasChanged);
            }

            var loadedModel = await model;

            if (loadVersion != _loadVersion)
            {
                return;
            }

            var result = GenerateDiagram(loadedModel);

            _model = loadedModel;
            _document = result.Document;
            _issues = result.Issues;
            _selectedElement = null;
            _selectedRelationship = null;
            _showLongLoadingMessage = false;
            _isReloading = false;
            _state = ModelingEditorState.Loaded;
            _completedLoadCount++;
            _completedGenerationCount++;

            if (_document is not null)
            {
                await OnDiagramGenerated.InvokeAsync(_document);
            }
        }
        catch (OperationCanceledException) when (loadCts.IsCancellationRequested)
        {
            if (loadVersion == _loadVersion)
            {
                _isReloading = false;
            }
        }
        catch (Exception ex)
        {
            if (loadVersion != _loadVersion)
            {
                return;
            }

            _state = ModelingEditorState.Error;
            _model = null;
            _document = null;
            _openedEditorDocument = null;
            _issues = [];
            _selectedElement = null;
            _selectedRelationship = null;
            _draggedElement = null;
            _errorMessage = ex.Message;
            _showLongLoadingMessage = false;
            _isReloading = false;
        }
    }

    private Task ReloadModelAsync()
    {
        if (_state == ModelingEditorState.Loading)
        {
            return Task.CompletedTask;
        }

        _activeLoadKey = null;
        return LoadModelAsync(force: true);
    }

    private IModelingModelProvider? ResolveProvider()
    {
        if (string.IsNullOrWhiteSpace(EffectiveProviderKey))
        {
            return null;
        }

        return ModelProviders.FirstOrDefault(provider => string.Equals(provider.ProviderKey, EffectiveProviderKey, StringComparison.Ordinal));
    }

    private string BuildLoadKey()
    {
        var loaderKey = ModelLoaderOverride is null
            ? string.Empty
            : $"{ModelLoaderOverride.Method.DeclaringType?.FullName}.{ModelLoaderOverride.Method.Name}";

        return string.Join(
            '\u001f',
            EffectiveProviderKey ?? string.Empty,
            EffectiveNotationKey,
            EffectiveViewpointKey,
            loaderKey);
    }

    private string? ResolveInitialViewId(ModelingModelDto model)
    {
        if (model.Views.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(EffectiveViewpointKey))
        {
            var matchingView = model.Views.FirstOrDefault(view => string.Equals(view.ViewpointKey, EffectiveViewpointKey, StringComparison.OrdinalIgnoreCase));
            if (matchingView is not null)
            {
                return matchingView.Id;
            }
        }

        return model.Views[0].Id;
    }

    private void SetEmpty()
    {
        _state = ModelingEditorState.Empty;
        _model = null;
        _document = null;
        _openedEditorDocument = null;
        _issues = [];
        _selectedElement = null;
        _selectedRelationship = null;
        _draggedElement = null;
        _errorMessage = null;
        _showLongLoadingMessage = false;
        _isReloading = false;
        _completedGenerationCount = 0;
    }

    private Task SelectElementAsync(ModelingElementDto element)
    {
        _selectedElement = element;
        _selectedRelationship = null;
        return OpenInspectorForSelectionAsync();
    }

    /// <summary>
    /// Every selection is an inspector intent — except a MOBILE tree tap, which is G2 browsing
    /// and stays on the Tree tab (Fáze 7 re-review): only an issue or an edge selection passes
    /// <paramref name="focusInspectorAfterSwitch"/>, which switches the mobile Inspector panel
    /// AND arms the post-render focus move onto it. On a TABLET the inspector side sheet opens
    /// (and the tree drawer yields, keeping the one-sheet rule); other layouts keep their docked
    /// panels.
    /// </summary>
    private Task OpenInspectorForSelectionAsync(bool focusInspectorAfterSwitch = false)
    {
        if (IsMobileLayout)
        {
            if (!focusInspectorAfterSwitch)
            {
                return Task.CompletedTask;
            }

            _activePanel = ModelingEditorPanel.Inspector;
            _focusInspectorAfterRender = true;
            return Task.CompletedTask;
        }

        if (!IsTablet || InspectorSheetOpen)
        {
            return Task.CompletedTask;
        }

        return OnShellRightOpenChanged(true);
    }

    private Task HandleElementDragStartedAsync(ModelingElementDto element)
    {
        _draggedElement = element;
        return Task.CompletedTask;
    }

    private Task HandleElementDragEndedAsync()
    {
        _draggedElement = null;
        return Task.CompletedTask;
    }

    private async Task HandleNodeDroppedAsync(ModelingNodeDroppedEventArgs args)
    {
        _selectedElement = args.Element;
        _selectedRelationship = null;
        _draggedElement = null;
        await OpenInspectorForSelectionAsync();
        await OnNodeDropped.InvokeAsync(args);
    }

    /// <summary>
    /// The model tree's non-drag alternative (Fáze 6, WCAG 2.5.7): route the "Insert into
    /// preview" request through the preview's existing drop path at the viewport center —
    /// the preview owns the node creation, the document mutation and the
    /// <see cref="OnNodeDropped"/> contract, so the alternative and the drag stay one path.
    /// </summary>
    private async Task HandleInsertIntoPreviewAsync(ModelingElementDto element)
    {
        if (_preview is null)
        {
            return;
        }

        await _preview.InsertAtViewportCenterAsync(element);
    }

    private Task SelectRelationshipAsync(string relationshipId)
    {
        if (_model is null || string.IsNullOrWhiteSpace(relationshipId))
        {
            return Task.CompletedTask;
        }

        var relationship = _model.Relationships.FirstOrDefault(item => string.Equals(item.Id, relationshipId, StringComparison.Ordinal));
        if (relationship is not null)
        {
            _selectedElement = null;
            _selectedRelationship = relationship;
            return OpenInspectorForSelectionAsync(focusInspectorAfterSwitch: true);
        }

        return Task.CompletedTask;
    }

    private Task SelectIssueAsync(ModelingIssueDto issue)
    {
        if (_model is null)
        {
            return Task.CompletedTask;
        }

        var elementId = issue.SourceElementId;
        if (string.IsNullOrWhiteSpace(elementId) && !string.IsNullOrWhiteSpace(issue.SourceRelationshipId))
        {
            var relationship = _model.Relationships.FirstOrDefault(item => string.Equals(item.Id, issue.SourceRelationshipId, StringComparison.Ordinal));
            elementId = !string.IsNullOrWhiteSpace(relationship?.SourceElementId)
                ? relationship.SourceElementId
                : relationship?.TargetElementId;
        }

        if (string.IsNullOrWhiteSpace(elementId))
        {
            return Task.CompletedTask;
        }

        var element = _model.Elements.FirstOrDefault(item => string.Equals(item.Id, elementId, StringComparison.Ordinal));
        if (element is not null)
        {
            _selectedElement = element;
            _selectedRelationship = null;
            return OpenInspectorForSelectionAsync(focusInspectorAfterSwitch: true);
        }

        return Task.CompletedTask;
    }

    private async Task ChangeNotationAsync(string? notationKey)
    {
        _effectiveNotationKey = string.IsNullOrWhiteSpace(notationKey) ? null : notationKey;
        _effectiveViewpointKey = null;
        _activePanel = ModelingEditorPanel.Preview;
        _changeViewOpen = false;
        RegenerateDiagramFromLoadedModel();
        RefreshActiveLoadKeyForLoadedModel();
        await OnNotationChanged.InvokeAsync(_effectiveNotationKey);
    }

    private async Task ChangeViewpointAsync(string? viewpointKey)
    {
        _effectiveViewpointKey = string.IsNullOrWhiteSpace(viewpointKey) ? null : viewpointKey;
        _changeViewOpen = false;
        RegenerateDiagramFromLoadedModel();
        RefreshActiveLoadKeyForLoadedModel();
        await OnViewpointChanged.InvokeAsync(_effectiveViewpointKey);
    }

    /// <summary>
    /// Handles a header provider-selection change (MO3): the in-editor override wins until the
    /// host parameter itself changes, the model is reloaded through the new provider (keeping
    /// the loaded workspace mounted, like the source-panel reload) and both callbacks are raised
    /// — <see cref="ProviderKeyChanged"/> (two-way binding) and <see cref="OnProviderChanged"/>.
    /// The in-editor notation/viewpoint overrides reset to the host parameter baseline (review
    /// fix, phase 3): a new provider may carry different notations, so a stale override must not
    /// filter the reloaded model.
    /// </summary>
    private async Task ChangeProviderAsync(string? providerKey)
    {
        var normalized = string.IsNullOrWhiteSpace(providerKey) ? null : providerKey;
        if (string.Equals(EffectiveProviderKey, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _effectiveProviderKey = normalized;
        _effectiveNotationKey = string.IsNullOrWhiteSpace(NotationKey) ? null : NotationKey;
        _effectiveViewpointKey = string.IsNullOrWhiteSpace(ViewpointKey) ? null : ViewpointKey;
        _changeViewOpen = false;
        // The opened diagram-editor overlay closes through the regular close path so the focus
        // restore onto the preview's "Open in editor" trigger still runs (review fix, phase 3).
        await CloseGeneratedDiagramAsync();
        var reload = LoadModelAsync(force: true);
        await ProviderKeyChanged.InvokeAsync(_effectiveProviderKey);
        await OnProviderChanged.InvokeAsync(_effectiveProviderKey);
        await reload;
    }

    private Task HandleProviderChangedAsync(ChangeEventArgs args)
        => ChangeProviderAsync(args.Value?.ToString());

    private void RegenerateDiagramFromLoadedModel()
    {
        if (_model is null || _state != ModelingEditorState.Loaded)
        {
            return;
        }

        var result = GenerateDiagram(_model);
        _document = result.Document;
        _openedEditorDocument = null;
        _issues = result.Issues;
        _selectedRelationship = null;
        _draggedElement = null;
        _completedGenerationCount++;
    }

    private async Task GenerateDiagramFromLoadedModelAsync()
    {
        if (_model is null || _state != ModelingEditorState.Loaded)
        {
            return;
        }

        var result = GenerateDiagram(_model);
        _document = result.Document;
        _openedEditorDocument = null;
        _issues = result.Issues;
        _selectedRelationship = null;
        _draggedElement = null;
        _completedGenerationCount++;

        if (_document is not null)
        {
            await OnDiagramGenerated.InvokeAsync(_document);
        }
    }

    private Task UpdatePreviewDocumentAsync(DiagramDocument document)
    {
        _document = document;
        return Task.CompletedTask;
    }

    private Task ActivatePanelAsync(ModelingEditorPanel panel)
    {
        _activePanel = panel;
        return Task.CompletedTask;
    }

    // ── Fáze 5 (MO1): docked-shell state handlers ────────────────────────────
    // The shell raises every state change; the editor stores it (host state is the truth).
    // The Fáze 6 tablet overlay rules (layout tracking, the one-sheet toggles, the insert
    // route) live in TmModelingEditor.Tablet.cs.

    private Task OnShellCollapsedPanelsChanged(EditorShellPanel collapsed)
    {
        _shellCollapsed = collapsed;
        return Task.CompletedTask;
    }

    /// <summary>Selects the docked inspector tab ("properties" | "source").</summary>
    private Task SelectInspectorTab(string tabId)
    {
        _inspectorTab = tabId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stores the issues dock state (the EventCallback receiver re-renders the editor, which then
    /// re-renders the panel with the new Collapsed value — the panel itself owns no state). The
    /// tablet band keeps its own flag, so a tablet toggle never writes the desktop state (Fáze 6
    /// review fix).
    /// </summary>
    private Task SetIssuesCollapsedAsync(bool collapsed)
    {
        if (IsTablet)
        {
            _tabletIssuesCollapsed = collapsed;
            return Task.CompletedTask;
        }

        if (IsMobileLayout)
        {
            // Fáze 7: the mobile Issues tab owns its flag (the tab shows the whole panel, which
            // starts expanded — unlike the tablet dock).
            _mobileIssuesCollapsed = collapsed;
            return Task.CompletedTask;
        }

        if (_issuesCollapsed == collapsed)
        {
            return Task.CompletedTask;
        }

        _issuesCollapsed = collapsed;
        // The dock row re-grids the canvas column — the embedded editor re-measures after render.
        _refitPreviewAfterRender = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// The tablet tabs' state follows the SHEET flags (the mobile `_activePanel` is a different
    /// state machine): Tree is active while the drawer is open, Inspector while the sheet is,
    /// Preview when the preview is alone — the visual state always agrees with aria-expanded.
    /// The mobile band uses the TmTabs tab strip, so it has no custom tab class.
    /// </summary>
    private string GetTabletPanelTabClass(ModelingEditorPanel panel)
    {
        var active = panel switch
        {
            ModelingEditorPanel.Tree => TreeSheetOpen,
            ModelingEditorPanel.Inspector => InspectorSheetOpen,
            ModelingEditorPanel.Issues => IssuesSheetOpen,
            _ => !TreeSheetOpen && !InspectorSheetOpen && !IssuesSheetOpen
        };
        return active
            ? "tm-modeling-editor__panel-tab tm-modeling-editor__panel-tab--active"
            : "tm-modeling-editor__panel-tab";
    }

    private ModelingDiagramGenerationResultDto GenerateDiagram(ModelingModelDto model)
        => DiagramGenerator.Generate(CreateGenerationModel(model), new ModelingDiagramGenerationOptionsDto
        {
            ViewpointKey = EffectiveViewpointKey,
            ViewId = ResolveInitialViewId(model) ?? string.Empty,
            IncludeIssues = true
        });

    private ModelingModelDto CreateGenerationModel(ModelingModelDto model)
    {
        if (string.IsNullOrWhiteSpace(EffectiveNotationKey))
        {
            return model;
        }

        var notation = EffectiveNotationKey;
        return new ModelingModelDto
        {
            Id = model.Id,
            Title = model.Title,
            Notation = notation,
            SupportedNotations = [notation],
            Metadata = model.Metadata,
            Elements = model.Elements.Select(element => new ModelingElementDto
            {
                Id = element.Id,
                SourceId = element.SourceId,
                SourceType = element.SourceType,
                SourcePath = element.SourcePath,
                Notation = notation,
                SemanticType = element.SemanticType,
                Name = element.Name,
                Description = element.Description,
                Properties = element.Properties,
                Tags = element.Tags,
                Governance = element.Governance
            }).ToList(),
            Relationships = model.Relationships,
            Views = model.Views,
            Issues = model.Issues
        };
    }

    private bool IsElementVisibleInViewpoint(ModelingElementDto element)
    {
        if (ViewpointRules is null || string.IsNullOrWhiteSpace(EffectiveViewpointKey))
            return true;

        var notationKey = !string.IsNullOrWhiteSpace(element.Notation)
            ? element.Notation
            : EffectiveNotationKey;

        if (string.IsNullOrWhiteSpace(notationKey))
            notationKey = _model?.Notation ?? string.Empty;

        return ViewpointRules.IsElementAllowedInViewpoint(notationKey, EffectiveViewpointKey, element.SemanticType);
    }

    private void SyncEffectiveSelectionFromParameters()
    {
        if (!string.Equals(_lastNotationParameter, NotationKey, StringComparison.Ordinal))
        {
            _effectiveNotationKey = string.IsNullOrWhiteSpace(NotationKey) ? null : NotationKey;
            _lastNotationParameter = NotationKey;
        }

        if (!string.Equals(_lastViewpointParameter, ViewpointKey, StringComparison.Ordinal))
        {
            _effectiveViewpointKey = string.IsNullOrWhiteSpace(ViewpointKey) ? null : ViewpointKey;
            _lastViewpointParameter = ViewpointKey;
        }

        if (!string.Equals(_lastProviderParameter, ProviderKey, StringComparison.Ordinal))
        {
            _effectiveProviderKey = string.IsNullOrWhiteSpace(ProviderKey) ? null : ProviderKey;
            _lastProviderParameter = ProviderKey;
        }
    }

    private void RefreshActiveLoadKeyForLoadedModel()
    {
        if (_model is not null && _state == ModelingEditorState.Loaded)
        {
            _activeLoadKey = BuildLoadKey();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();

        if (_interopModule is not null)
        {
            await _interopModule.DisposeAsync();
        }
    }

    private enum ModelingEditorState
    {
        Empty,
        Loading,
        Loaded,
        Error
    }

    private enum ModelingEditorPanel
    {
        Tree,
        Preview,
        Inspector,
        Issues
    }
}
