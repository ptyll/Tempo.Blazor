using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.Notifications;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor;

/// <summary>
/// Root shell for the Notion-style editor.
/// Provides all NotionEditor providers as a <see cref="NotionEditorContext"/> cascade,
/// manages top-level navigation, sidebar visibility, and loading state.
/// </summary>
public partial class TmNotionEditor : TmComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // ── Required parameters ──────────────────────────────────────────────────

    /// <summary>Core page data provider (required).</summary>
    [Parameter, EditorRequired]
    public INotionDataProvider DataProvider { get; set; } = default!;

    /// <summary>
    /// Canonical aggregate provider used for every editor read and validated save-once mutation.
    /// </summary>
    [Parameter, EditorRequired]
    public INotionAggregateProvider AggregateProvider { get; set; } = default!;

    // ── Optional providers ───────────────────────────────────────────────────

    [Parameter] public INotionSearchProvider?        SearchProvider        { get; set; }
    [Parameter] public INotionDatabaseProvider?      DatabaseProvider      { get; set; }
    [Parameter] public ITmCommentProvider?           CommentProvider       { get; set; }
    [Parameter] public INotionVersionProvider?       HistoryProvider       { get; set; }
    [Parameter] public INotionCollaborationProvider? CollaborationProvider { get; set; }
    [Parameter] public ITmPeopleProvider?            MentionProvider       { get; set; }
    [Parameter] public INotionAIProvider?            AIProvider            { get; set; }
    [Parameter] public ITmWorkItemProvider?          WorkItemSource        { get; set; }
    [Parameter] public TmWorkItemProviderRegistry?   WorkItemProviders     { get; set; }
    [Parameter] public INotionReactionProvider?      ReactionProvider      { get; set; }
    [Parameter] public INotionAnalyticsProvider?     AnalyticsProvider     { get; set; }
    [Parameter] public INotionBlogProvider?          BlogProvider          { get; set; }
    [Parameter] public INotionWatchProvider?         WatchProvider         { get; set; }
    [Parameter] public INotionSpaceProvider?         SpaceProvider         { get; set; }
    [Parameter] public INotionPagePropertiesProvider? PagePropertiesProvider { get; set; }
    [Parameter] public INotionTemplateProvider?      TemplateProvider       { get; set; }
    [Parameter] public ISmartLinkProvider?           SmartLinkProvider      { get; set; }
    [Parameter] public ITmAuthorizationProvider?      AuthorizationProvider  { get; set; }
    [Parameter] public INotionPermissionProvider?    PermissionProvider     { get; set; }
    [Parameter] public INotionPublicShareProvider?   PublicShareProvider    { get; set; }
    [Parameter] public ITmActivityProvider?          AuditProvider          { get; set; }
    [Parameter] public INotionBookmarkProvider?      BookmarkProvider       { get; set; }
    [Parameter] public ITmFileProvider?              FileProvider           { get; set; }
    [Parameter] public INotionImportExportProvider?  ImportExportProvider   { get; set; }
    [Parameter] public IDiagramDocumentProvider?     DiagramDocumentProvider  { get; set; }
    [Parameter] public IWireframeDocumentProvider?   WireframeDocumentProvider   { get; set; }
    [Parameter] public ISpreadsheetDocumentProvider? SpreadsheetDocumentProvider { get; set; }
    [Parameter] public Tempo.Blazor.DocumentLibrary.ITempoDocumentLibraryProvider? DocumentLibraryProvider { get; set; }
    [Parameter] public Tempo.Blazor.DocumentLibrary.ITempoDocumentChangeNotifier? DocumentChangeNotifier { get; set; }
    [Parameter] public INotionSyncedBlockProvider?   SyncedBlockProvider         { get; set; }
    [Parameter] public INotionMediaLibraryProvider?  MediaLibraryProvider        { get; set; }
    [Parameter] public ITokenDataProvider?           TokenProvider               { get; set; }

    /// <summary>
    /// When non-null, restricts the slash menu and Turn Into menu to only these block types.
    /// Existing blocks of other types are still displayed. When null, all types are available.
    /// </summary>
    [Parameter] public IReadOnlySet<BlockType>? AllowedBlockTypes { get; set; }

    // ── Behaviour parameters ─────────────────────────────────────────────────

    /// <summary>Page ID to open on first render. Supports @bind-InitialPageId.</summary>
    [Parameter] public string? InitialPageId { get; set; }

    /// <summary>Show the left navigation sidebar.</summary>
    [Parameter] public bool ShowSidebar { get; set; } = true;

    /// <summary>
    /// Renders the editor as a self-contained single-page surface (e.g. a work-item description field).
    /// When <see langword="true"/>: the sidebar is always hidden (ignoring <see cref="ShowSidebar"/>);
    /// the in-editor navigation stack, breadcrumbs/back button, space selector, template gallery, child-page
    /// creation and the tasks/blog/analytics/audit panels are unavailable; multi-page blocks
    /// (<see cref="SinglePageDeniedBlockTypes"/>) are removed from the slash and Turn-Into menus; and any
    /// request to open a <em>different</em> page raises <see cref="OnPageNavigationRequested"/> instead of navigating.
    /// Requires <see cref="InitialPageId"/> to be set.
    /// </summary>
    [Parameter] public bool SinglePageMode { get; set; }

    /// <summary>
    /// Raised in <see cref="SinglePageMode"/> when something requests navigation to a page other than
    /// <see cref="InitialPageId"/> (e.g. clicking a link/child-page). The host decides what to do
    /// (open it elsewhere, in a dialog, etc.); the editor itself stays on the initial page.
    /// </summary>
    [Parameter] public EventCallback<string> OnPageNavigationRequested { get; set; }

    /// <summary>Prevents all editing interactions.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>
    /// Explicit current user identifier for collaboration, reactions, and user-scoped actions.
    /// When blank, the editor resolves <see cref="ITmCurrentUser"/> from DI and falls back to <c>demo</c>.
    /// </summary>
    [Parameter] public string CurrentUserId { get; set; } = string.Empty;

    /// <summary>Group identifiers used for permission checks for the current user.</summary>
    [Parameter] public IReadOnlyList<string> CurrentUserGroupIds { get; set; } = [];

    /// <summary>Additional CSS class on the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Raised whenever the active page changes.</summary>
    [Parameter] public EventCallback<INotionPage> OnPageChanged { get; set; }

    /// <summary>Raised when the user requests to open the trash panel from the sidebar.</summary>
    [Parameter] public EventCallback OnTrashRequested { get; set; }

    /// <summary>
    /// Called when the user clicks "Create token" in the token dropdown.
    /// Arg = current search query (may be empty). Return the newly created
    /// token (Key, DisplayName, ColorClass) so the editor can insert it
    /// automatically, or <c>null</c> if the user cancelled.
    /// </summary>
    [Parameter] public Func<string, Task<(string Key, string DisplayName, string? ColorClass)?>?>? OnCreateTokenRequested { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private INotionPage?               _currentPage;
    private string?                    _currentPageId;
    private readonly Stack<string>     _navStack      = new();
    private bool                       _isLoading;
    private string?                    _loadError;
    private bool                       _sidebarVisible = true;
    private bool                       _sidebarOverlay;
    private bool                       _topbarScrolled;
    private NotionEditorContext        _context        = default!;
    private ElementReference           _rootRef;
    private ElementReference           _mainRef;
    private DotNetObjectReference<TmNotionEditor>? _selfRef;
    private IJSObjectReference?        _jsScrollListener;
    private NotionCollaborationSync?   _collabSync;
    private TmNotificationBell?        _notificationBell;
    private bool                       _notificationPanelOpen;
    private bool                       _tasksPanelOpen;
    private bool                       _blogPanelOpen;
    private bool                       _analyticsPanelOpen;
    private bool                       _auditPanelOpen;
    private bool                       _shortcutsVisible;
    private bool                       _templateGalleryOpen;
    private string?                    _selectedSpaceId;
    private NotionEditorViewMode       _viewMode = NotionEditorViewMode.Normal;
    private string?                    _scrollSpyBlockId;
    private PageEffectivePermissionDto? _effectivePermission;
    private TmCurrentUserState?        _resolvedCurrentUser;
    private NotionEditorAggregateSession? _aggregateSession;
    private INotionAggregateProvider? _aggregateSessionProvider;
    private NotionEditorBlockService _blockService = default!;

    /// <summary>
    /// Multi-page block types that are removed from the slash and Turn-Into menus while
    /// <see cref="SinglePageMode"/> is active. Existing blocks of these types still render.
    /// </summary>
    public static readonly IReadOnlySet<BlockType> SinglePageDeniedBlockTypes = new HashSet<BlockType>
    {
        BlockType.ChildPage,
        BlockType.LinkedPage,
        BlockType.LinkedDatabase,
        BlockType.IncludePage,
        BlockType.ChildrenDisplay,
        BlockType.ContentByLabel,
        BlockType.Breadcrumb
    };

    // ── Computed ─────────────────────────────────────────────────────────────

    private string _editorModifiers => string.Concat(
        _currentPage?.IsFullWidth == true ? " tm-notion-editor--full-width" : string.Empty,
        IsEffectivelyReadOnly              ? " tm-notion-editor--locked"     : string.Empty,
        SinglePageMode                     ? " tm-notion-editor--single-page" : string.Empty,
        _viewMode == NotionEditorViewMode.Reading ? " tm-notion-editor--reading" : string.Empty,
        _viewMode == NotionEditorViewMode.Presentation ? " tm-notion-editor--presentation" : string.Empty
    ).TrimStart();

    /// <summary>Whether the navigation sidebar and its affordances should be shown.</summary>
    private bool EffectiveShowSidebar => ShowSidebar && !SinglePageMode;

    /// <summary>True when SinglePageMode is enabled but no page was configured to display.</summary>
    private bool HasSinglePageConfigError => SinglePageMode && string.IsNullOrWhiteSpace(InitialPageId);

    private bool IsReadOnlyViewMode => _viewMode is NotionEditorViewMode.Reading or NotionEditorViewMode.Presentation;
    private bool IsPermissionRestricted => _effectivePermission is { Mode: not PageRestrictionMode.Open };
    private bool HasNoAccess => _effectivePermission?.Permission == PageRestrictionPermission.None;
    private bool IsEffectivelyReadOnly => ReadOnly || IsReadOnlyViewMode || _currentPage?.IsLocked == true || _effectivePermission?.Permission is PageRestrictionPermission.View or PageRestrictionPermission.Comment;
    private string ViewModeName => _viewMode.ToString();
    private string CurrentSpaceId => _selectedSpaceId ?? _currentPage?.SpaceId ?? "team";
    private string EffectiveCurrentUserId
    {
        get
        {
            if (_resolvedCurrentUser is not null)
                return string.IsNullOrWhiteSpace(_resolvedCurrentUser.User?.Id) ? "anonymous" : _resolvedCurrentUser.User.Id;

            return string.IsNullOrWhiteSpace(CurrentUserId) ? "demo" : CurrentUserId.Trim();
        }
    }

    private TmUserRef? EffectiveCurrentUser
    {
        get
        {
            if (_resolvedCurrentUser is not null)
                return _resolvedCurrentUser.User;

            var userId = string.IsNullOrWhiteSpace(CurrentUserId) ? "demo" : CurrentUserId.Trim();
            return new TmUserRef { Id = userId, DisplayName = userId };
        }
    }

    private IReadOnlyList<string> EffectiveCurrentUserGroupIds
        => _resolvedCurrentUser is not null ? _resolvedCurrentUser.GroupIds : CurrentUserGroupIds;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnInitialized()
    {
        EnsureAggregateServices();
        if (CollaborationProvider is not null)
            _collabSync = new NotionCollaborationSync();

        _context = BuildContext();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!ReferenceEquals(AggregateProvider, _aggregateSessionProvider))
        {
            EnsureAggregateServices();
            if (_currentPageId is not null)
            {
                if (!Guid.TryParse(_currentPageId, out var currentAggregatePageId))
                {
                    _loadError = Loc["TmNotionEditor_LoadError"];
                }
                else
                {
                    var aggregateLoad = await _aggregateSession.LoadAsync(currentAggregatePageId);
                    if (!aggregateLoad.Success)
                        _loadError = Loc["TmNotionEditor_LoadError"];
                }
            }
        }
        await ResolveCurrentUserAsync();
        _context = BuildContext();
    }

    private void EnsureAggregateServices()
    {
        if (AggregateProvider is null)
        {
            throw new InvalidOperationException(
                $"{nameof(TmNotionEditor)} requires {nameof(AggregateProvider)}.");
        }
        if (ReferenceEquals(AggregateProvider, _aggregateSessionProvider))
        {
            return;
        }

        _aggregateSessionProvider = AggregateProvider;
        _aggregateSession = new NotionEditorAggregateSession(
            AggregateProvider,
            () => EffectiveCurrentUserId);
        _blockService = new NotionEditorBlockService(
            AggregateProvider,
            _aggregateSession,
            () => EffectiveCurrentUserId);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _selfRef = DotNetObjectReference.Create(this);
            await InitEditorKeyHandlerAsync();
            await InitializeResponsiveSidebarAsync();
            await InitScrollListenerAsync();

            if (InitialPageId is not null && !HasSinglePageConfigError)
            {
                await NavigateToPageAsync(InitialPageId);
            }
        }
    }

    // ── Public navigation API ────────────────────────────────────────────────

    /// <summary>
    /// Navigates to the given page and pushes it onto the navigation stack.
    /// Calling with the current page ID is a no-op.
    /// </summary>
    public async Task NavigateToPageAsync(string pageId)
    {
        // In single-page mode the editor never leaves the initial page — hand the request to the host.
        if (SinglePageMode && !string.IsNullOrEmpty(InitialPageId) && pageId != InitialPageId)
        {
            await OnPageNavigationRequested.InvokeAsync(pageId);
            return;
        }

        if (_isLoading || pageId == _currentPageId) return;

        _isLoading  = true;
        _loadError  = null;
        StateHasChanged();

        try
        {
            var page = await DataProvider.GetPageAsync(pageId);
            _navStack.Push(pageId);
            _currentPageId = pageId;
            _currentPage   = page;
            _selectedSpaceId ??= string.IsNullOrWhiteSpace(page.SpaceId) ? null : page.SpaceId;

            if (_aggregateSession is not null)
            {
                if (!Guid.TryParse(pageId, out var aggregatePageId))
                    throw new InvalidDataException(
                        "Aggregate Notion page identifiers must be GUID values.");

                var aggregateLoad = await _aggregateSession.LoadAsync(aggregatePageId);
                if (!aggregateLoad.Success)
                {
                    throw new InvalidDataException(string.Join(
                        Environment.NewLine,
                        aggregateLoad.Issues.Select(issue => issue.Message)));
                }
            }

            if (AnalyticsProvider is not null)
            {
                try
                {
                    await AnalyticsProvider.RecordViewAsync(page.Id, EffectiveCurrentUserId);
                }
                catch
                {
                    // Analytics is non-critical; page navigation must remain available if telemetry fails.
                }
            }

            _effectivePermission = await GetEffectivePermissionAsync(page.Id);

            if (_collabSync is not null && CollaborationProvider is not null)
                await _collabSync.JoinAsync(CollaborationProvider, pageId, EffectiveCurrentUserId);

            _context = BuildContext();
            await OnPageChanged.InvokeAsync(page);
        }
        catch
        {
            _loadError = Loc["TmNotionEditor_LoadError"];
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    // ── Internal handlers ────────────────────────────────────────────────────

    private async Task NavigateBackAsync()
    {
        if (SinglePageMode || _navStack.Count <= 1) return;
        _navStack.Pop();
        var prevId = _navStack.Peek();
        _navStack.Pop();
        await NavigateToPageAsync(prevId);
    }

    private async Task RetryLoadAsync()
    {
        if (_currentPageId is null) return;
        var id = _currentPageId;
        _currentPageId = null;
        await NavigateToPageAsync(id);
    }

    private void ToggleSidebar()
    {
        _sidebarVisible = !_sidebarVisible;
        StateHasChanged();
    }

    private async Task InitializeResponsiveSidebarAsync()
    {
        if (!EffectiveShowSidebar)
            return;

        try
        {
            var isOverlayViewport = await JS.InvokeAsync<bool>("tmNotionEditor.isNarrowViewport", 1024);
            _sidebarOverlay = isOverlayViewport;
            if (isOverlayViewport)
                _sidebarVisible = false;
            StateHasChanged();
        }
        catch
        {
            _sidebarOverlay = false;
        }
    }

    private void OnNotificationDropdownOpenChanged(bool isOpen)
    {
        _notificationPanelOpen = isOpen;
        StateHasChanged();
    }

    private void ToggleTasksPanel()
    {
        _tasksPanelOpen = !_tasksPanelOpen;
        if (_tasksPanelOpen)
            CloseSecondaryPanels(exceptTasks: true);
        StateHasChanged();
    }

    private void CloseTasksPanel()
    {
        _tasksPanelOpen = false;
        StateHasChanged();
    }

    private void ToggleBlogPanel()
    {
        _blogPanelOpen = !_blogPanelOpen;
        if (_blogPanelOpen)
            CloseSecondaryPanels(exceptBlog: true);
        StateHasChanged();
    }

    private void ToggleAnalyticsPanel()
    {
        _analyticsPanelOpen = !_analyticsPanelOpen;
        if (_analyticsPanelOpen)
            CloseSecondaryPanels(exceptAnalytics: true);
        StateHasChanged();
    }

    private void ToggleAuditPanel()
    {
        _auditPanelOpen = !_auditPanelOpen;
        if (_auditPanelOpen)
            CloseSecondaryPanels(exceptAudit: true);
        StateHasChanged();
    }

    private void CloseBlogPanel()
    {
        _blogPanelOpen = false;
        StateHasChanged();
    }

    private void CloseAnalyticsPanel()
    {
        _analyticsPanelOpen = false;
        StateHasChanged();
    }

    private void CloseAuditPanel()
    {
        _auditPanelOpen = false;
        StateHasChanged();
    }

    private void OpenShortcutsPanel()
    {
        _shortcutsVisible = true;
        StateHasChanged();
    }

    private Task HandleShortcutsVisibleChangedAsync(bool visible)
    {
        _shortcutsVisible = visible;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task OpenTemplateGalleryAsync()
    {
        CloseSecondaryPanels();
        _templateGalleryOpen = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private void CloseTemplateGallery()
    {
        _templateGalleryOpen = false;
        StateHasChanged();
    }

    private void EnterReadingMode()
    {
        CloseSecondaryPanels();
        _viewMode = NotionEditorViewMode.Reading;
        StateHasChanged();
    }

    private void EnterPresentationMode()
    {
        CloseSecondaryPanels();
        _viewMode = NotionEditorViewMode.Presentation;
        StateHasChanged();
    }

    private void ExitViewMode()
    {
        _viewMode = NotionEditorViewMode.Normal;
        StateHasChanged();
    }

    private void OnEditorKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs args)
    {
        if (args.Key == "Escape" && IsReadOnlyViewMode)
            ExitViewMode();
    }

    [JSInvokable]
    public Task OnEditorEscapeAsync()
    {
        if (IsReadOnlyViewMode)
            ExitViewMode();

        return Task.CompletedTask;
    }

    private void CloseSecondaryPanels(bool exceptTasks = false, bool exceptBlog = false, bool exceptAnalytics = false, bool exceptAudit = false)
    {
        if (!exceptTasks) _tasksPanelOpen = false;
        if (!exceptBlog) _blogPanelOpen = false;
        if (!exceptAnalytics) _analyticsPanelOpen = false;
        if (!exceptAudit) _auditPanelOpen = false;
        _templateGalleryOpen = false;
    }

    private async Task HandleTemplateSelectedAsync(NotionTemplateDto template)
    {
        var isBlank = string.Equals(template.Id, "blank", StringComparison.OrdinalIgnoreCase);
        var title = isBlank
            ? string.Empty
            : string.IsNullOrWhiteSpace(template.Name) ? Loc["TmNotionEditor_Untitled"] : template.Name;

        var page = await DataProvider.CreatePageAsync(null, title);
        var pageId = page.Id.ToString("D");

        if (!isBlank && template.Blocks.Count > 0)
        {
            var blocks = template.Blocks
                .Select((block, index) => new PageBlock
                {
                    Id = Guid.NewGuid(),
                    PageId = page.Id,
                    ParentBlockId = null,
                    Type = block.Type,
                    Order = index,
                    Content = block.Content,
                    CreatedAt = DateTime.UtcNow,
                    LastEditedAt = DateTime.UtcNow
                })
                .ToArray();

            await _blockService.CreateBlocksAsync(pageId, blocks, null);
        }

        _templateGalleryOpen = false;
        await NavigateToPageAsync(pageId);
    }

    private Task HandleSpaceSelectedAsync(string? spaceId)
    {
        _selectedSpaceId = string.IsNullOrWhiteSpace(spaceId) ? null : spaceId;
        _context = BuildContext();
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task HandleCurrentPageMovedToSpaceAsync(string spaceId)
    {
        _selectedSpaceId = spaceId;
        if (_currentPage is NotionPage page)
            page.SpaceId = spaceId;
        _context = BuildContext();
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task HandleCurrentPageLabelsChangedAsync(IReadOnlyList<string> labels)
    {
        if (_currentPage is NotionPage page)
            page.Labels = labels.ToArray();
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task NavigateToTaskBlockAsync((string PageId, string BlockId) target)
    {
        _tasksPanelOpen = false;

        if (!string.Equals(_currentPageId, target.PageId, StringComparison.OrdinalIgnoreCase))
        {
            await NavigateToPageAsync(target.PageId);
        }

        await Task.Yield();
        try { await JS.InvokeVoidAsync("tmNotionEditor.scrollToBlock", target.BlockId); }
        catch { }

        StateHasChanged();
    }

    private async Task OnChildPageUpdatedAsync(INotionPage updatedPage)
    {
        _currentPage = updatedPage;
        await OnPageChanged.InvokeAsync(updatedPage);
        StateHasChanged();
    }

    private async Task OnPagePermissionsChangedAsync()
    {
        if (_currentPage is null)
            return;

        _effectivePermission = await GetEffectivePermissionAsync(_currentPage.Id);

        StateHasChanged();
    }

    private async Task ResolveCurrentUserAsync()
    {
        if (!string.IsNullOrWhiteSpace(CurrentUserId))
        {
            _resolvedCurrentUser = null;
            return;
        }

        var currentUserProvider = ServiceProvider.GetService<ITmCurrentUser>();
        if (currentUserProvider is null)
        {
            _resolvedCurrentUser = null;
            return;
        }

        try
        {
            _resolvedCurrentUser = await currentUserProvider.GetCurrentUserAsync();
        }
        catch
        {
            _resolvedCurrentUser = null;
        }
    }

    private async Task<PageEffectivePermissionDto?> GetEffectivePermissionAsync(Guid pageId)
    {
        if (PermissionProvider is not null)
        {
            return await PermissionProvider.GetEffectivePermissionAsync(
                pageId,
                EffectiveCurrentUserId,
                EffectiveCurrentUserGroupIds);
        }

        if (AuthorizationProvider is null)
            return null;

        return await GetEffectivePermissionFromAuthorizationAsync(pageId);
    }

    private async Task<PageEffectivePermissionDto> GetEffectivePermissionFromAuthorizationAsync(Guid pageId)
    {
        var permission = PageRestrictionPermission.None;
        if (await IsAuthorizedForPageAsync(pageId, TmAuthorizationActions.Edit))
            permission = PageRestrictionPermission.Edit;
        else if (await IsAuthorizedForPageAsync(pageId, TmAuthorizationActions.Comment))
            permission = PageRestrictionPermission.Comment;
        else if (await IsAuthorizedForPageAsync(pageId, TmAuthorizationActions.View))
            permission = PageRestrictionPermission.View;

        return new PageEffectivePermissionDto
        {
            PageId = pageId,
            UserId = EffectiveCurrentUserId,
            Permission = permission,
            Mode = PageRestrictionMode.Open
        };
    }

    private async Task<bool> IsAuthorizedForPageAsync(Guid pageId, string action)
    {
        if (AuthorizationProvider is null)
            return false;

        var request = TmAuthorizationRequest.Create(
            EffectiveCurrentUser,
            action,
            TmEntityRef.Create("notion-page", pageId.ToString("D")),
            EffectiveCurrentUserGroupIds,
            new Dictionary<string, object>
            {
                ["component"] = nameof(TmNotionEditor)
            });

        var result = await AuthorizationProvider.AuthorizeAsync(request);
        return result.Allowed;
    }

    private async Task OnTrashRequestedAsync()
    {
        await OnTrashRequested.InvokeAsync();
    }

    // ── JS scroll spy ────────────────────────────────────────────────────────

    private async Task InitScrollListenerAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.initSmoothScrollSpy",
                _mainRef,
                DotNetObjectReference.Create(this));
        }
        catch { /* JS not available (SSR / test) */ }
    }

    private async Task InitEditorKeyHandlerAsync()
    {
        if (_selfRef is null)
            return;

        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.initEditorKeyHandler", _rootRef, _selfRef);
        }
        catch { }
    }

    [JSInvokable]
    public async Task OnScrollSpyBlockChanged(string? blockId)
    {
        if (string.Equals(_scrollSpyBlockId, blockId, StringComparison.OrdinalIgnoreCase))
            return;

        _scrollSpyBlockId = blockId;
        await InvokeAsync(StateHasChanged);
    }

    // ── Topbar scroll detection ───────────────────────────────────────────────

    [JSInvokable]
    public void OnMainScrolled(bool scrolled)
    {
        if (_topbarScrolled == scrolled) return;
        _topbarScrolled = scrolled;
        StateHasChanged();
    }

    // ── Context builder ──────────────────────────────────────────────────────

    private NotionEditorContext BuildContext() => new()
    {
        CurrentUserId        = EffectiveCurrentUserId,
        CurrentPageId        = _currentPageId,
        DataProvider          = DataProvider,
        BlockService         = _blockService,
        AggregateSession      = _aggregateSession,
        SearchProvider        = SearchProvider,
        DatabaseProvider      = DatabaseProvider,
        CommentProvider       = CommentProvider,
        HistoryProvider       = HistoryProvider,
        CollaborationProvider = CollaborationProvider,
        CollaborationSync     = _collabSync,
        MentionProvider       = MentionProvider,
        AIProvider            = AIProvider,
        WorkItemSource        = WorkItemSource,
        WorkItemProviders     = WorkItemProviders,
        ReactionProvider      = ReactionProvider,
        AnalyticsProvider     = AnalyticsProvider,
        BlogProvider          = BlogProvider,
        WatchProvider         = WatchProvider,
        SpaceProvider         = SpaceProvider,
        PagePropertiesProvider = PagePropertiesProvider,
        TemplateProvider       = TemplateProvider,
        SmartLinkProvider      = SmartLinkProvider,
        AuthorizationProvider  = AuthorizationProvider,
        PermissionProvider     = PermissionProvider,
        PublicShareProvider    = PublicShareProvider,
        AuditProvider          = AuditProvider,
        CurrentUserGroupIds    = EffectiveCurrentUserGroupIds,
        BookmarkProvider          = BookmarkProvider,
        FileProvider              = FileProvider,
        ImportExportProvider      = ImportExportProvider,
        DiagramDocumentProvider   = DiagramDocumentProvider,
        WireframeDocumentProvider   = WireframeDocumentProvider,
        SpreadsheetDocumentProvider = SpreadsheetDocumentProvider,
        DocumentLibraryProvider     = DocumentLibraryProvider,
        DocumentChangeNotifier      = DocumentChangeNotifier,
        SyncedBlockProvider         = SyncedBlockProvider,
        MediaLibraryProvider        = MediaLibraryProvider,
        TokenProvider               = TokenProvider,
        AllowedBlockTypes           = AllowedBlockTypes,
        DeniedBlockTypes            = SinglePageMode ? SinglePageDeniedBlockTypes : null,
        NavigateTo                  = pageId => NavigateToPageAsync(pageId),
        SelectedSpaceId             = _selectedSpaceId,
        SelectSpace                 = SinglePageMode ? null : HandleSpaceSelectedAsync,
        CurrentPageMovedToSpace     = SinglePageMode ? null : HandleCurrentPageMovedToSpaceAsync,
        OpenTemplateGallery         = SinglePageMode ? null : OpenTemplateGalleryAsync
    };

    // ── Dispose ──────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_collabSync is not null)
        {
            try { await _collabSync.DisposeAsync(); } catch { }
        }
        if (_jsScrollListener is not null)
        {
            try { await _jsScrollListener.DisposeAsync(); } catch { }
        }
        try { await JS.InvokeVoidAsync("tmNotionEditor.destroyEditorKeyHandler", _rootRef); } catch { }
        _selfRef?.Dispose();
    }

    private enum NotionEditorViewMode
    {
        Normal,
        Reading,
        Presentation
    }
}
