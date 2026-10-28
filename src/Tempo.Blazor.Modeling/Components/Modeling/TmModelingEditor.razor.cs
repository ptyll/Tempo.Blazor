using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Icons;
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
    private string _providerSelectId = $"tm-modeling-editor-{Guid.NewGuid():N}-provider";
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
    private ElementReference _editorRoot;

    /// <summary>Stable, test-id-independent focus hook of the open-editor overlay trigger.</summary>
    internal const string OpenInEditorFocusSelector = "[data-modeling-focus='open-in-editor']";

    /// <summary>Stable, test-id-independent focus hook of the open-editor overlay close button.</summary>
    internal const string OpenEditorCloseFocusSelector = "[data-modeling-focus='open-editor-close']";

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

    /// <summary>Optional loader used by host pages or tests to supply a model without registering a provider.</summary>
    [Parameter] public Func<ModelingModelRequest, CancellationToken, Task<ModelingModelDto>>? ModelLoaderOverride { get; set; }

    private string RootClass => string.Join(" ", new[] { "tm-modeling-editor", Class }.Where(value => !string.IsNullOrWhiteSpace(value)));

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

    private string DisplayProviderKey => string.IsNullOrWhiteSpace(EffectiveProviderKey) ? Loc["TmModelingEditor_NotSelected"] : EffectiveProviderKey!;

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
    /// </summary>
    private async Task ChangeProviderAsync(string? providerKey)
    {
        var normalized = string.IsNullOrWhiteSpace(providerKey) ? null : providerKey;
        if (string.Equals(EffectiveProviderKey, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _effectiveProviderKey = normalized;
        _openedEditorDocument = null;
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
    /// Moves DOM focus to the element matching <paramref name="selector"/> INSIDE this editor's
    /// root (the overlay close button, or the overlay trigger on focus return). Progressive
    /// enhancement: every failure is swallowed and the editor keeps working without it.
    /// </summary>
    private async Task FocusWithinAsync(string selector)
    {
        await Interop.InvokeVoidSafeAsync("focusWithin", _editorRoot, selector);
    }

    private Task ActivatePanelAsync(ModelingEditorPanel panel)
    {
        _activePanel = panel;
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
