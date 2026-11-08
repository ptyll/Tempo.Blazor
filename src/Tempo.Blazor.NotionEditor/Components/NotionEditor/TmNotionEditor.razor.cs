using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Toolbar;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.NotionEditor.Services;
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

    /// <summary>
    /// How the editor decides its responsive layout. <see cref="TmLayoutMode.Auto"/> (default)
    /// measures the editor's own container (desktop ≥1024, tablet 640–1023, mobile &lt;640; the first
    /// frame, before a measurement, is Desktop). A forced value pins the layout for a host or a test
    /// and is cascaded into the page, the menus and the database blocks.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>
    /// How much chrome the editor renders. <see cref="NotionEditorChrome.Full"/> (default) is today's
    /// editor. <see cref="NotionEditorChrome.Minimal"/> hides shortcuts, the bell, mode toggles and
    /// badges. <see cref="NotionEditorChrome.None"/> renders the page only.
    /// </summary>
    [Parameter] public NotionEditorChrome Chrome { get; set; } = NotionEditorChrome.Full;

    /// <summary>
    /// Host slot for a page inspector, rendered through <see cref="TmSidePanel"/> on the right.
    /// Null (default) renders no right region — the editor does not invent inspector content (G10).
    /// </summary>
    [Parameter] public RenderFragment? InspectorContent { get; set; }

    /// <summary>
    /// How the Tasks / Blog / Analytics / Audit shell panels present themselves.
    /// <see cref="NotionShellPanelPresentation.Overlay"/> (default) rides the core
    /// <see cref="TmSidePanel"/> — docked next to the page on a desktop layout, a side sheet on a
    /// tablet layout and a full-height sheet on a mobile layout — while the page stays mounted.
    /// <see cref="NotionShellPanelPresentation.Main"/> replaces the page in the main region
    /// (the pre-phase-3 hosting). At most one shell panel is open at a time either way.
    /// </summary>
    [Parameter] public NotionShellPanelPresentation ShellPanelPresentation { get; set; } = NotionShellPanelPresentation.Overlay;

    /// <summary>
    /// How the block / text comment panels present (NO1). <see cref="NotionCommentPresentation.Auto"/>
    /// (the default) keeps the anchored popover bubble on a fine desktop pointer and opens the core
    /// modal / sheet on a coarse pointer and on touch and narrow containers; <see cref="NotionCommentPresentation.Popover"/>
    /// pins the bubble at every layout; <see cref="NotionCommentPresentation.Dialog"/> always opens
    /// the modal.
    /// </summary>
    [Parameter] public NotionCommentPresentation CommentPresentation { get; set; } = NotionCommentPresentation.Auto;

    /// <summary>
    /// How the page-level comment panel presents (phase 5, P4). <see cref="NotionPageCommentsPresentation.Inline"/>
    /// (the default) keeps the collapsible panel at the bottom of the page;
    /// <see cref="NotionPageCommentsPresentation.Dialog"/> renders no inline panel — the header badge
    /// and the Comments page action open a core <c>TmModal</c> hosting the Embedded panel (a sheet
    /// on a mobile layout).
    /// </summary>
    [Parameter] public NotionPageCommentsPresentation PageCommentsPresentation { get; set; } = NotionPageCommentsPresentation.Inline;

    /// <summary>
    /// Opt-in page meta row under the title (last edited, last editor, comment count link).
    /// Defaults to <see langword="false"/>; the demo turns it on.
    /// </summary>
    [Parameter] public bool ShowMeta { get; set; }

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
    private TmLayoutMode               _resolvedLayout = TmLayoutMode.Desktop;

    /// <summary>The viewport scope — its pointer signal is copied into <see cref="LayoutCascade"/>.</summary>
    [CascadingParameter(Name = TmLayoutScopes.Viewport)]
    private TmLayoutContext? ViewportLayout { get; set; }
    private bool                       _sidebarTouched;

    /// <summary>Below desktop the drawer starts closed; a forced mode must not wait for the JS probe.</summary>
    private bool SidebarStartsClosed => EffectiveLayout is TmLayoutMode.Mobile or TmLayoutMode.Tablet;
    private bool                       _topbarScrolled;
    private NotionEditorContext        _context        = default!;
    private ElementReference           _rootRef;
    private ElementReference           _mainRef;
    private DotNetObjectReference<TmNotionEditor>? _selfRef;
    private IJSObjectReference?        _jsScrollListener;
    private NotionCollaborationSync?   _collabSync;
    private bool                       _notificationPanelOpen;
    private NotionShellPanel           _openShellPanel = NotionShellPanel.None;
    private bool                       _shortcutsVisible;
    private bool                       _templateGalleryOpen;
    private bool                       _shareDialogOpen;
    private bool                       _restrictionsDialogOpen;
    private Guid?                      _pageDialogPageId;
    private string?                    _selectedSpaceId;
    /// <summary>Space last taken from a loaded page. A user pick (sidebar) is not overwritten.</summary>
    private string?                    _pageSpaceId;
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

    private bool _inspectorOpen;

    /// <summary>Main presentation hides the page while a shell panel is open; Overlay keeps it mounted.</summary>
    private bool MainPanelReplacesPage => ShellPanelPresentation == NotionShellPanelPresentation.Main
        && _openShellPanel != NotionShellPanel.None;
    private ElementReference _sidebarRef;
    private ElementReference _sidebarToggleRef;

    /// <summary>The localized title of the open shell panel (names the side sheet).</summary>
    private string ShellPanelTitle => _openShellPanel switch
    {
        NotionShellPanel.Tasks    => Loc["Notion_Tasks_Title"],
        NotionShellPanel.Blog     => Loc["Notion_Blog_Title"],
        NotionShellPanel.Analytics => Loc["Notion_Analytics_Title"],
        NotionShellPanel.Audit    => Loc["Notion_Audit_Title"],
        _                         => string.Empty,
    };

    /// <summary>The side sheet width of the open shell panel.</summary>
    private string ShellPanelWidth => _openShellPanel switch
    {
        // Blog and audit need the wide table. Tasks stays wide enough that a 1440/1600 dock
        // is a ≥640px container (inline filters); a phone sheet is still <640 (Filters button).
        NotionShellPanel.Blog or NotionShellPanel.Audit => "min(52rem, 100%)",
        NotionShellPanel.Tasks => "min(42rem, 100%)",
        _ => "26rem",
    };

    /// <summary>The toolbar trigger focus returns to when Escape closes the overlay panel.</summary>
    private string? ShellPanelTriggerId => _openShellPanel switch
    {
        NotionShellPanel.Tasks     => "notion-tasks",
        NotionShellPanel.Blog      => "notion-blog-open",
        NotionShellPanel.Analytics => "notion-analytics-open",
        NotionShellPanel.Audit     => "notion-audit-open",
        _                          => null,
    };

    private string RootClasses => $"tm-notion-editor {_editorModifiers} {Class}".Trim();

    /// <summary>The layout cascaded into the page, the menus and the database blocks.</summary>
    private TmLayoutMode EffectiveLayout => LayoutMode == TmLayoutMode.Auto ? _resolvedLayout : LayoutMode;

    private TmLayoutContext LayoutCascade => new(LayoutMode, EffectiveLayout)
    {
        // The shell observer is nearer than this cascade. A forced shell does not measure, so it
        // inherits this value — without the copy a pinned desktop width would hide a coarse pointer.
        IsCoarsePointer = ViewportLayout?.IsCoarsePointer == true,
    };

    private Dictionary<string, object> ShellAttributes => new()
    {
        ["data-notion-scroll-root"] = true,
        ["data-view-mode"] = ViewModeName,
        ["data-editor-layout"] = LayoutModeName,
        ["tabindex"] = 0,
        ["onkeydown"] = EventCallback.Factory.Create<KeyboardEventArgs>(this, OnEditorKeyDown)
    };

    private string LayoutModeName => EffectiveLayout switch
    {
        TmLayoutMode.Mobile => "mobile",
        TmLayoutMode.Tablet => "tablet",
        _ => "desktop"
    };

    private string PageTitle => string.IsNullOrWhiteSpace(_currentPage?.Title)
        ? Loc["TmNotionEditor_Untitled"]
        : _currentPage!.Title;

    /// <summary>Tablet and mobile keep the low-priority tools in the toolbar "More" menu.</summary>
    private ToolbarButtonPriority ToolPriority => EffectiveLayout is TmLayoutMode.Tablet or TmLayoutMode.Mobile
        ? ToolbarButtonPriority.OverflowOnly
        : ToolbarButtonPriority.Secondary;

    /// <summary>
    /// My Tasks stays pinned on desktop and tablet. On the mobile band the pinned sidebar toggle,
    /// watch and bell (44px touch targets each) already fill a 320px bar; a pinned Tasks could
    /// not fold into "More" and the More trigger painted over it (taps opened More instead).
    /// </summary>
    private ToolbarButtonPriority TasksPriority => EffectiveLayout is TmLayoutMode.Mobile
        ? ToolbarButtonPriority.Primary
        : ToolbarButtonPriority.Pinned;

    private static Dictionary<string, object> Group(string key) => new() { ["data-tm-group"] = key };

    private Dictionary<string, object> Test(string id) => new() { ["data-testid"] = TestId(id) };

    /// <summary>
    /// The mobile action bar. Reading/presentation mode keeps only Exit; otherwise the bar pins at
    /// most three actions (Add block, Comments, My tasks when a work-item source exists) and the
    /// rest live in the More sheet with their test ids. The page actions (Add block, Comments,
    /// Page settings) are invoked through the cascaded <see cref="NotionPageActions"/> the page
    /// itself exposes — the bar never duplicates page logic.
    /// </summary>
    private IReadOnlyList<TmActionItem>? MobileBarActions
    {
        get
        {
            if (EffectiveLayout != TmLayoutMode.Mobile || Chrome != NotionEditorChrome.Full)
                return null;

            // No navigation and no inspector: the bar is editor chrome too — a host that renders
            // the page only (public share, single-page surface) gets no bar.
            if (!EffectiveShowSidebar && InspectorContent is null)
                return null;

            if (IsReadOnlyViewMode)
            {
                return
                [
                    Action("exit-reading", Loc["Notion_Reading_Exit"], "x", ExitViewModeAsync)
                ];
            }

            var items = new List<TmActionItem>();
            var pageActionsLabel = Loc["TmNotionEditor_PageActions"];
            if (_context?.PageActions is not null && !IsEffectivelyReadOnly)
            {
                items.Add(Action("add", Loc["TmNotionEditor_AddBlock"], "plus",
                    () => _context.PageActions!.AddBlock?.Invoke() ?? Task.CompletedTask, pageActionsLabel));
                if (CommentProvider is not null)
                {
                    items.Add(Action("comments", Loc["TmNotionEditor_Comments"], "message-square",
                        () => _context.PageActions!.OpenComments?.Invoke() ?? Task.CompletedTask, pageActionsLabel));
                }
            }
            if (_context?.WorkItemSource is not null && !SinglePageMode)
                items.Add(Action("tasks", Loc["Notion_Tasks_Title"], "check-square", ToggleTasksPanelAsync));

            // The More sheet holds the remaining tools; every entry keeps its test id.
            items.Add(new TmActionItem
            {
                Id = "shortcuts",
                Label = Loc["Notion_Shortcuts_Title"],
                Icon = "help-circle",
                Overflow = ActionOverflow.Always,
                TestId = TestId("notion-shortcuts-open"),
                OnClick = EventCallback.Factory.Create(this, OpenShortcutsAsync)
            });
            if (_context?.AuditProvider is not null && !SinglePageMode)
                items.Add(MoreAction("audit", Loc["Notion_Audit_Title"], "file-text", "notion-audit-open", ToggleAuditPanelAsync));
            if (_context?.BlogProvider is not null && !SinglePageMode)
                items.Add(MoreAction("blog", Loc["Notion_Blog_Title"], "book-open", "notion-blog-open", ToggleBlogPanelAsync));
            if (_context?.AnalyticsProvider is not null && !SinglePageMode)
                items.Add(MoreAction("analytics", Loc["Notion_Analytics_Title"], "bar-chart-2", "notion-analytics-open", ToggleAnalyticsPanelAsync));
            if (_context?.PublicShareProvider is not null && !SinglePageMode)
                items.Add(MoreAction("share", Loc["TmNotionEditor_Share"], "share-2", "notion-share-open", OpenShareAsync));
            items.Add(MoreAction("reading", Loc["Notion_Reading_Enter"], "book-open", "notion-reading-toggle", EnterReadingModeAsync));
            items.Add(MoreAction("presentation", Loc["Notion_Presentation_Enter"], "monitor", "notion-presentation-toggle", EnterPresentationModeAsync));
            if (_context?.WatchProvider is not null && _currentPage is not null)
                items.Add(MoreAction("watch", Loc["Notion_Watch_Start"], "eye", "notion-watch-toggle", ToggleWatchFromBarAsync));
            if (_context?.PageActions?.OpenPageSettings is not null && !IsEffectivelyReadOnly)
                items.Add(MoreAction("page-settings", Loc["TmNotionPageSettingsMenu_TriggerLabel"], "settings", "notion-page-settings", OpenPageSettingsFromBarAsync, pageActionsLabel));
            return items;
        }
    }

    private TmActionItem MoreAction(string id, string label, string icon, string testId, Func<Task> click, string? groupLabel = null) => new()
    {
        Id = id,
        Label = label,
        Icon = icon,
        Overflow = ActionOverflow.Always,
        TestId = TestId(testId),
        Group = groupLabel is null ? null : "page-actions",
        GroupLabel = groupLabel,
        OnClick = EventCallback.Factory.Create(this, click)
    };

    private Task OpenPageSettingsFromBarAsync()
        => _context.PageActions?.OpenPageSettings?.Invoke() ?? Task.CompletedTask;

    private async Task ToggleWatchFromBarAsync()
    {
        if (_context.WatchProvider is null || _currentPage is null) return;
        var pageId = _currentPage.Id.ToString("D");
        try
        {
            var watching = await _context.WatchProvider.IsWatchingAsync(pageId, EffectiveCurrentUserId);
            if (watching)
                await _context.WatchProvider.UnwatchAsync(pageId, EffectiveCurrentUserId);
            else
                await _context.WatchProvider.WatchAsync(pageId, EffectiveCurrentUserId, includeChildren: false);
        }
        catch { /* watch state is non-critical */ }
    }

    private TmActionItem Action(string id, string label, string icon, Func<Task> click, string? groupLabel = null) => new()
    {
        Id = id,
        Label = label,
        Icon = icon,
        Group = groupLabel is null ? null : "page-actions",
        GroupLabel = groupLabel,
        OnClick = EventCallback.Factory.Create(this, click)
    };

    private Task OpenShareAsync()
    {
        _shareDialogOpen = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>The page re-registered its actions — re-render the chrome that consumes them.</summary>
    private Task NotifyPageActionsChanged() => InvokeAsync(StateHasChanged);

    private Task OnShareVisibleChanged(bool visible)
    {
        _shareDialogOpen = visible;
        return Task.CompletedTask;
    }

    /// <summary>
    /// The page asks the editor to open a dialog. The dialog renders beside the shortcuts panel,
    /// outside the shell canvas, so a band flip (NOTION-EDITOR-SHELL-CANVAS-REMOUNT) cannot
    /// dispose it while it is open.
    /// </summary>
    private void OpenPageDialogFromPage(NotionPageDialog dialog, Guid pageId)
    {
        _pageDialogPageId = pageId;
        if (dialog == NotionPageDialog.Share)
            _shareDialogOpen = true;
        else if (dialog == NotionPageDialog.Restrictions)
            _restrictionsDialogOpen = true;
        _ = InvokeAsync(StateHasChanged);
    }

    private Task OnRestrictionsVisibleChanged(bool visible)
    {
        _restrictionsDialogOpen = visible;
        return Task.CompletedTask;
    }

    private Task OnRestrictionsSavedAsync() => OnPagePermissionsChangedAsync();

    private static Dictionary<string, object> Attrs(params (string Key, object Value)[] pairs)
    {
        var map = new Dictionary<string, object>();
        foreach (var (key, value) in pairs)
            map[key] = value;
        return map;
    }

    private string _editorModifiers => string.Concat(
        _currentPage?.IsFullWidth == true ? " tm-notion-editor--full-width" : string.Empty,
        IsEffectivelyReadOnly              ? " tm-notion-editor--locked"     : string.Empty,
        SinglePageMode                     ? " tm-notion-editor--single-page" : string.Empty,
        EffectiveLayout == TmLayoutMode.Mobile  ? " tm-notion-editor--mobile" : string.Empty,
        EffectiveLayout == TmLayoutMode.Tablet  ? " tm-notion-editor--tablet" : string.Empty,
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

            return string.IsNullOrWhiteSpace(CurrentUserId) ? string.Empty : CurrentUserId.Trim();
        }
    }

    private TmUserRef? EffectiveCurrentUser
    {
        get
        {
            if (_resolvedCurrentUser is not null)
                return _resolvedCurrentUser.User;

            var userId = string.IsNullOrWhiteSpace(CurrentUserId) ? string.Empty : CurrentUserId.Trim();
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
        if (SidebarStartsClosed)
            _sidebarVisible = false;
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
            if (EffectiveLayout is TmLayoutMode.Mobile or TmLayoutMode.Tablet)
                _sidebarVisible = false;
            _currentPage   = page;
            // Follow the loaded page unless the user picked another space. A one-shot ??= kept the
            // space from the first navigation, so a seed that moved the page (blog posts live in
            // "team") still queried the old space and the panel listed 0 posts.
            var loadedSpaceId = string.IsNullOrWhiteSpace(page.SpaceId) ? null : page.SpaceId;
            if (_selectedSpaceId is null
                || string.Equals(_selectedSpaceId, _pageSpaceId, StringComparison.OrdinalIgnoreCase))
                _selectedSpaceId = loadedSpaceId;
            _pageSpaceId = loadedSpaceId;

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
        _sidebarTouched = true;
        _sidebarVisible = !_sidebarVisible;
    }

    private Task OnShellLayoutChanged(TmLayoutMode mode)
    {
        _resolvedLayout = mode;
        if (!_sidebarTouched)
            _sidebarVisible = mode == TmLayoutMode.Desktop;
        return Task.CompletedTask;
    }

    private Task ToggleSidebarAsync()
    {
        ToggleSidebar();
        return Task.CompletedTask;
    }

    private Task OnSidebarOpenChanged(bool open)
    {
        if (open) _sidebarTouched = true;
        _sidebarVisible = open;
        return Task.CompletedTask;
    }

    private Task OnInspectorOpenChanged(bool open)
    {
        _inspectorOpen = open;
        return Task.CompletedTask;
    }

    private void CloseSidebar() => _sidebarVisible = false;

    private void OnNotificationDropdownOpenChanged(bool isOpen)
    {
        _notificationPanelOpen = isOpen;
        StateHasChanged();
    }

    private void ToggleTasksPanel()
    {
        _openShellPanel = _openShellPanel == NotionShellPanel.Tasks ? NotionShellPanel.None : NotionShellPanel.Tasks;
        if (_openShellPanel != NotionShellPanel.None)
            CloseTemplateGalleryOnly();
        StateHasChanged();
    }

    private Task ToggleTasksPanelAsync() { ToggleTasksPanel(); return Task.CompletedTask; }
    private Task ToggleBlogPanelAsync() { ToggleBlogPanel(); return Task.CompletedTask; }
    private Task ToggleAnalyticsPanelAsync() { ToggleAnalyticsPanel(); return Task.CompletedTask; }
    private Task ToggleAuditPanelAsync() { ToggleAuditPanel(); return Task.CompletedTask; }
    private Task OpenShortcutsAsync() { OpenShortcutsPanel(); return Task.CompletedTask; }
    private Task EnterReadingModeAsync() { EnterReadingMode(); return Task.CompletedTask; }
    private Task EnterPresentationModeAsync() { EnterPresentationMode(); return Task.CompletedTask; }
    private Task ExitViewModeAsync() { ExitViewMode(); return Task.CompletedTask; }

    /// <summary>The one close path of the open shell panel (its ×, Escape and the side sheet close).</summary>
    private void CloseShellPanel()
    {
        _openShellPanel = NotionShellPanel.None;
        StateHasChanged();
    }

    private Task OnShellPanelOverlayClosed(bool open)
    {
        if (!open)
            CloseShellPanel();
        return Task.CompletedTask;
    }

    private void ToggleBlogPanel()
    {
        _openShellPanel = _openShellPanel == NotionShellPanel.Blog ? NotionShellPanel.None : NotionShellPanel.Blog;
        if (_openShellPanel != NotionShellPanel.None)
            CloseTemplateGalleryOnly();
        StateHasChanged();
    }

    private void ToggleAnalyticsPanel()
    {
        _openShellPanel = _openShellPanel == NotionShellPanel.Analytics ? NotionShellPanel.None : NotionShellPanel.Analytics;
        if (_openShellPanel != NotionShellPanel.None)
            CloseTemplateGalleryOnly();
        StateHasChanged();
    }

    private void ToggleAuditPanel()
    {
        _openShellPanel = _openShellPanel == NotionShellPanel.Audit ? NotionShellPanel.None : NotionShellPanel.Audit;
        if (_openShellPanel != NotionShellPanel.None)
            CloseTemplateGalleryOnly();
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
        _openShellPanel = NotionShellPanel.None;
        _templateGalleryOpen = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private void CloseTemplateGallery()
    {
        _templateGalleryOpen = false;
        StateHasChanged();
    }

    /// <summary>Closes the template gallery only — the shell panels are single-state already.</summary>
    private void CloseTemplateGalleryOnly() => _templateGalleryOpen = false;

    private void EnterReadingMode()
    {
        _openShellPanel = NotionShellPanel.None;
        _viewMode = NotionEditorViewMode.Reading;
        StateHasChanged();
    }

    private void EnterPresentationMode()
    {
        _openShellPanel = NotionShellPanel.None;
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
        _openShellPanel = NotionShellPanel.None;

        if (!string.Equals(_currentPageId, target.PageId, StringComparison.OrdinalIgnoreCase))
        {
            await NavigateToPageAsync(target.PageId);
        }

        // Let the navigation render commit before scrolling: on Blazor Server the re-render is
        // queued on the renderer's work item queue and this callback may still run ahead of it;
        // the Yield hands the scroll to a later turn so the block exists in the DOM. (Ordering
        // relied upon — see the Notion plan, fáze 3 review.)
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
        CommentPresentation         = CommentPresentation,
        PageCommentsPresentation    = PageCommentsPresentation,
        PageActions                 = _context?.PageActions,
        PageActionsChanged          = NotifyPageActionsChanged,
        NavigateTo                  = pageId => NavigateToPageAsync(pageId),
        SelectedSpaceId             = _selectedSpaceId,
        SelectSpace                 = SinglePageMode ? null : HandleSpaceSelectedAsync,
        CurrentPageMovedToSpace     = SinglePageMode ? null : HandleCurrentPageMovedToSpaceAsync,
        OpenTemplateGallery         = SinglePageMode ? null : OpenTemplateGalleryAsync,
        OpenPageDialog              = OpenPageDialogFromPage
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
