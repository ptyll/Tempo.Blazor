using System.Globalization;
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
    private ModelingInteropModule? _interopModule;
    private ElementReference _canvasSurface;

    // ── Fáze 5 (MO1): docked-layout state ────────────────────────────────────
    // The shell's two-way state lives on the editor so a layout flip cannot disagree with it.
    private bool _shellLeftOpen = true;
    private bool _shellRightOpen = true;
    private EditorShellPanel _shellCollapsed;
    private bool _issuesCollapsed;

    /// <summary>The selected inspector tab of the docked layout: "properties" (default) or "source".</summary>
    private string _inspectorTab = "properties";

    /// <summary>Stable, test-id-independent focus hook of the open-editor overlay trigger.</summary>
    internal const string OpenInEditorFocusSelector = "[data-modeling-focus='open-in-editor']";

    /// <summary>Stable, test-id-independent focus hook of the open-editor overlay close button.</summary>
    internal const string OpenEditorCloseFocusSelector = "[data-modeling-focus='open-editor-close']";

    private readonly string _openEditorTriggerId = $"tm-modeling-open-editor-trigger-{Guid.NewGuid():N}";

    /// <summary>
    /// Layout thresholds of the EMBEDDED open-editor diagram editor: 768 / 1024 so the dialog body
    /// (not the viewport) decides the editor's <c>data-editor-layout</c>. The dialog is ~90vw ×
    /// 90vh, so a 1024–1280 px viewport renders a sub-1024px body that must resolve desktop —
    /// the shared 640/1024 pair would drop it to tablet inside a desktop dialog.
    /// </summary>
    private static readonly Tempo.Blazor.Abstractions.Layout.TmLayoutBreakpoints OpenEditorBreakpoints = new(768, 1024);

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

    /// <summary>
    /// Title of the open-editor dialog: the document's title, falling back to the localized
    /// "Open in editor" label when the document carries none (the pre-redesign default fell
    /// back to "Preview" — Fáze 4 renames the fallback to the dialog's own label).
    /// Test seam (InternalsVisibleTo): the modal header renders exactly this value.
    /// </summary>
    internal string OpenEditorDialogTitle
        => string.IsNullOrWhiteSpace(_openedEditorDocument?.Title)
            ? Loc["TmModelingDiagramPreview_OpenInEditor"]
            : _openedEditorDocument.Title!;

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
    /// and drops the token floor so the host value wins exactly.
    /// </summary>
    private string? HeightStyle => Height is null ? null : $"height: {Height}; min-height: 0;";

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
    /// and <c>auto</c> (header only) while collapsed. The class — not an inline style — keeps the
    /// sizes in the stylesheet.
    /// </summary>
    private string CanvasBodyClass => _issuesCollapsed
        ? "tm-modeling-editor__canvas-body tm-modeling-editor__canvas-body--issues-collapsed"
        : "tm-modeling-editor__canvas-body tm-modeling-editor__canvas-body--issues-open";

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

    /// <summary>
    /// The Source tab button attributes: the frozen tab test id plus, while the indicator shows,
    /// the <c>data-stale</c> hook for the warning-tinted badge CSS, a <c>title</c> tooltip and an
    /// <c>aria-describedby</c> reference to the hint span rendered outside the tablist (the
    /// tab's accessible NAME stays the visible "Source" label — no aria-label override).
    /// </summary>
    private IReadOnlyDictionary<string, object>? SourceTabAttributes
    {
        get
        {
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

            return attributes;
        }
    }

    private string StateName => _state switch
    {
        ModelingEditorState.Loading => "loading",
        ModelingEditorState.Loaded => "loaded",
        ModelingEditorState.Error => "error",
        _ => "empty"
    };

    private string StateLabelKey => _state switch
    {
        ModelingEditorState.Loading => "TmModelingEditor_StateLoading",
        ModelingEditorState.Loaded => "TmModelingEditor_StateLoaded",
        ModelingEditorState.Error => "TmModelingEditor_StateError",
        _ => "TmModelingEditor_StateEmpty"
    };

    /// <summary>Color variant of the title state badge — success when loaded, danger on error,
    /// primary while loading, neutral when no provider is selected.</summary>
    private BadgeVariant StateBadgeVariant => _state switch
    {
        ModelingEditorState.Loaded => BadgeVariant.Success,
        ModelingEditorState.Error => BadgeVariant.Danger,
        ModelingEditorState.Loading => BadgeVariant.Primary,
        _ => BadgeVariant.Default
    };

    /// <summary>Icon of the empty/error state mark — a box for "no model", an alert circle on error.</summary>
    private string StateMarkIcon => _state == ModelingEditorState.Error
        ? IconNames.AlertCircle
        : IconNames.Box;

    /// <summary>Text of the first status strip item: the last-loaded time when the loaded model
    /// carries metadata (shared "g" format with <see cref="TmModelingSourcePanel"/>), else the
    /// state label. The state TEXT itself lives only on the title badge (MO3 dedupe).</summary>
    private string StatusStripStateText
    {
        get
        {
            var loadedAt = _model?.Metadata.LoadedAt;
            return loadedAt is { } timestamp && timestamp != default
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    Loc["TmModelingEditor_LastLoaded"],
                    timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                : Loc[StateLabelKey];
        }
    }

    /// <summary>Leading status strip icon: check when loaded, alert on error, clock while loading.</summary>
    private string StatusStripIcon => _state switch
    {
        ModelingEditorState.Loaded => IconNames.CheckCircle,
        ModelingEditorState.Error => IconNames.AlertCircle,
        ModelingEditorState.Loading => IconNames.Clock,
        _ => IconNames.Box
    };

    private string DisplayProviderKey => string.IsNullOrWhiteSpace(EffectiveProviderKey) ? Loc["TmModelingEditor_NotSelected"] : ResolveProviderDisplayName(EffectiveProviderKey!);

    /// <summary>Visible text of a provider option (header select and chip): the host's
    /// <see cref="ProviderDisplayName"/> resolution when supplied, otherwise the raw key.</summary>
    private string ResolveProviderDisplayName(string providerKey)
        => ProviderDisplayName?.Invoke(providerKey) ?? providerKey;

    /// <summary>The provider the editor loads from: the host parameter until the user picks
    /// another one in the header selector (the in-editor override wins until the parameter
    /// itself changes — same pattern as the notation/viewpoint overrides).</summary>
    private string? EffectiveProviderKey => _effectiveProviderKey ?? ProviderKey;

    /// <summary>Distinct registered provider keys for the header selector, ordered; the current
    /// key is kept as an option even when no registered provider carries it (unknown-host-key
    /// edge case) so the select never silently jumps to another value.</summary>
    private IReadOnlyList<string> ProviderOptions
    {
        get
        {
            var keys = ModelProviders
                .Select(provider => provider.ProviderKey)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            var current = EffectiveProviderKey;
            if (!string.IsNullOrWhiteSpace(current) && !keys.Contains(current, StringComparer.Ordinal))
            {
                keys.Insert(0, current);
            }

            return keys;
        }
    }

    private string EffectiveNotationKey => _effectiveNotationKey ?? string.Empty;

    private string EffectiveViewpointKey => _effectiveViewpointKey ?? string.Empty;

    private string ActivePanelName => _activePanel switch
    {
        ModelingEditorPanel.Tree => "tree",
        ModelingEditorPanel.Inspector => "inspector",
        _ => "preview"
    };

    private IReadOnlyList<ModelingElementDto> LoadedElements => _model?.Elements ?? [];

    private IReadOnlyList<ModelingElementDto> VisibleTreeElements
    {
        get
        {
            var elements = LoadedElements;
            if (ReferenceEquals(elements, _visibleTreeElementsSource)
                && string.Equals(EffectiveNotationKey, _visibleTreeElementsNotationKey, StringComparison.Ordinal)
                && string.Equals(EffectiveViewpointKey, _visibleTreeElementsViewpointKey, StringComparison.Ordinal))
            {
                return _visibleTreeElements;
            }

            _visibleTreeElementsSource = elements;
            _visibleTreeElementsNotationKey = EffectiveNotationKey;
            _visibleTreeElementsViewpointKey = EffectiveViewpointKey;
            _visibleTreeElements = elements.Where(IsElementVisibleInViewpoint).ToArray();
            return _visibleTreeElements;
        }
    }

    private IReadOnlyList<ModelingIssueDto> DisplayIssues
        => _model is null
            ? _issues
            : _model.Issues.Concat(_issues).ToArray();

    private int IssueCount => DisplayIssues.Count;

    /// <inheritdoc />
    protected override Task OnParametersSetAsync()
    {
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

        if (_shouldRestoreOpenButtonFocus)
        {
            _shouldRestoreOpenButtonFocus = false;
            await FocusWithinAsync(OpenInEditorFocusSelector);
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
        return Task.CompletedTask;
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
        await OnNodeDropped.InvokeAsync(args);
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
        }

        return Task.CompletedTask;
    }

    private async Task ChangeNotationAsync(string? notationKey)
    {
        _effectiveNotationKey = string.IsNullOrWhiteSpace(notationKey) ? null : notationKey;
        _effectiveViewpointKey = null;
        _activePanel = ModelingEditorPanel.Preview;
        RegenerateDiagramFromLoadedModel();
        RefreshActiveLoadKeyForLoadedModel();
        await OnNotationChanged.InvokeAsync(_effectiveNotationKey);
    }

    private async Task ChangeViewpointAsync(string? viewpointKey)
    {
        _effectiveViewpointKey = string.IsNullOrWhiteSpace(viewpointKey) ? null : viewpointKey;
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

    private async Task OpenGeneratedDiagramAsync(DiagramDocument document)
    {
        _openedEditorDocument = document;
        _shouldFocusOpenEditorCloseButton = true;
        await OnOpenInEditor.InvokeAsync(document);
    }

    private Task UpdateOpenedEditorDocumentAsync(DiagramDocument document)
    {
        _openedEditorDocument = document;
        _document = document;
        return Task.CompletedTask;
    }

    private Task CloseGeneratedDiagramAsync()
    {
        _openedEditorDocument = null;
        _shouldRestoreOpenButtonFocus = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Moves DOM focus to the element matching <paramref name="selector"/> INSIDE the editor's
    /// canvas surface (the open-editor overlay close button, or the overlay trigger on focus
    /// return — the surface element contains both in every layout). Progressive enhancement:
    /// every failure is swallowed and the editor keeps working without it.
    /// </summary>
    private async Task FocusWithinAsync(string selector)
    {
        await Interop.InvokeVoidSafeAsync("focusWithin", _canvasSurface, selector);
    }

    private Task ActivatePanelAsync(ModelingEditorPanel panel)
    {
        _activePanel = panel;
        return Task.CompletedTask;
    }

    // ── Fáze 5 (MO1): docked-shell state handlers ────────────────────────────
    // The shell raises every state change; the editor stores it (host state is the truth). The
    // layout-flip restore policy arrives with the tablet/mobile phases — the fields just mirror
    // what the user picked so a re-render cannot undo it.

    private Task OnShellLeftOpenChanged(bool open)
    {
        _shellLeftOpen = open;
        return Task.CompletedTask;
    }

    private Task OnShellRightOpenChanged(bool open)
    {
        _shellRightOpen = open;
        return Task.CompletedTask;
    }

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
    /// re-renders the panel with the new Collapsed value — the panel itself owns no state).
    /// </summary>
    private Task SetIssuesCollapsedAsync(bool collapsed)
    {
        if (_issuesCollapsed == collapsed)
        {
            return Task.CompletedTask;
        }

        _issuesCollapsed = collapsed;
        return Task.CompletedTask;
    }

    private string GetPanelTabClass(ModelingEditorPanel panel)
        => panel == _activePanel
            ? "tm-modeling-editor__panel-tab tm-modeling-editor__panel-tab--active"
            : "tm-modeling-editor__panel-tab";

    private string GetPanelClass(ModelingEditorPanel panel, string baseClass)
        => panel == _activePanel
            ? $"{baseClass} tm-modeling-editor__panel--active"
            : baseClass;

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
        Inspector
    }
}
