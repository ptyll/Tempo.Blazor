using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;
using Tempo.Blazor.Services;

namespace Tempo.Blazor.Components.Scheduler;

public partial class TmGantt : IDisposable, IAsyncDisposable
{
    internal ElementReference _timelineContainerRef;
    internal ElementReference _timelineBodyRef;
    internal ElementReference _treeBodyRef;
    private List<GanttTaskNode> _treeRoots = [];
    internal List<GanttTaskNode> _visibleNodes = [];
    internal List<TaskBarInfo> _taskBars = [];
    internal List<DependencyLineInfo> _visibleDependencies = [];
    internal List<TimelineHeader> _timelineHeaders = [];
    internal List<TimelineHeader> _upperHeaders = [];
    internal List<TimelineHeader> _lowerHeaders = [];
    internal List<NonWorkingRect> _nonWorkingRects = [];
    internal double _totalTimelineWidth;
    internal int _zoomLevel = 100;
    internal double _todayOffset;
    /// <summary>Today in the display zone, recomputed once per <see cref="RefreshVisibleData"/>.</summary>
    internal DateTime _today = default;
    internal double _currentTimeOffset;

    // Phase 3 state
    internal HashSet<string> _criticalPathIds = [];
    internal Dictionary<string, (double Left, double Width)> _baselinePositions = [];
    internal bool _filterPanelOpen;
    private IReadOnlyList<GanttFilter> _activeFilters = [];
    private IReadOnlyList<GanttFilter>? _filtersParam;

    // Phase 4 state
    private bool _exportDialogOpen;
    private bool _importDialogOpen;
    internal bool _historyDrawerOpen;
    private TmWorkItem? _pendingDelete;

    // Phase 5 state
    internal HashSet<string> _overloadedAssigneeIds = [];
    internal GanttSidebarPanel? _activeSidebarPanel;
    private IGanttRealtimeConnection? _subscribedConnection;


    // Inline edit state
    internal string? _inlineEditTaskId;
    internal GanttColumnKey? _inlineEditColumn;
    internal string? _inlineEditCustomFieldId;
    internal string _inlineEditValue = string.Empty;

    // Dependency drag state
    private string? _depDragFromId;
    private bool _depDragFromEnd;
    internal string? _selectedDependencyId;

    // Bulk select state
    internal HashSet<string> _selectedTaskIds = [];

    private static readonly IReadOnlyList<GanttColumnDefinition> DefaultColumns = new List<GanttColumnDefinition>
    {
        new() { Key = GanttColumnKey.WBS,      Visible = true,  Order = 0, Width = 50 },
        new() { Key = GanttColumnKey.Title,    Visible = true,  Order = 1 },
        new() { Key = GanttColumnKey.Status,   Visible = true,  Order = 2, Width = 90 },
        new() { Key = GanttColumnKey.Start,    Visible = true,  Order = 3, Width = 90 },
        new() { Key = GanttColumnKey.End,      Visible = true,  Order = 4, Width = 90 },
        new() { Key = GanttColumnKey.Progress, Visible = true,  Order = 5, Width = 80 },
        new() { Key = GanttColumnKey.Duration, Visible = false, Order = 6, Width = 80 },
        new() { Key = GanttColumnKey.Priority, Visible = false, Order = 7, Width = 90 },
    };

    private bool _isSyncingScroll;
    internal bool _viewSettingsOpen;

    // Context menu state
    private bool _contextMenuVisible;
    private double _contextMenuX;
    private double _contextMenuY;
    private TmWorkItem? _contextMenuTask;
    private long _touchMenuOpenedAt;
    private string? _announcement;

    // Drag & drop state
    internal string? _draggedTaskId;
    internal string? _dropTargetId;
    internal GanttDropPosition _dropPosition;


    private const int ComfortableRowHeight = 40;
    private const int CompactRowHeight = 28;
    private const int TouchRowHeight = 44;
    private const int DayWidth = 40;

    /// <summary>The density hook of the stylesheets (the tree rows read it); the default comfortable density renders no attribute.</summary>
    private string? DensityAttribute => ViewSettings.ViewDensity switch
    {
        GanttViewDensity.Compact => "compact",
        GanttViewDensity.Touch => "touch",
        _ => null,
    };

    /// <summary>The row height in pixels, one value for the tree rows and the timeline rows (the bar tops are computed from it).</summary>
    internal int RowHeight => ViewSettings.ViewDensity switch
    {
        GanttViewDensity.Compact => CompactRowHeight,
        GanttViewDensity.Touch => TouchRowHeight,
        _ => ComfortableRowHeight,
    };

    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    /// <summary>Service provider for the optional user-zone lookups (never a required dependency).</summary>
    [Inject] private IServiceProvider Services { get; set; } = default!;

    // ── Layout (container-owned, F1) ─────────────────────────────

    /// <summary>The layout the observer last resolved (the markup writes it every render, before any part reads it).</summary>
    private TmLayoutMode _layout = TmLayoutMode.Desktop;

    /// <summary>What the observer is asked for: the forced <see cref="LayoutMode"/>, else the obsolete <see cref="ContainerWidth"/> alias, else a measurement.</summary>
    private TmLayoutMode EffectiveLayoutMode
    {
        get
        {
            if (LayoutMode != TmLayoutMode.Auto) return LayoutMode;
#pragma warning disable CS0618 // the alias is this property's whole point
            var width = ContainerWidth;
#pragma warning restore CS0618
            return width is { } w && double.IsFinite(w) && w >= 0 ? TmBreakpoints.Classify(w) : TmLayoutMode.Auto;
        }
    }

    private Task OnResolvedLayoutChanged(TmLayoutMode mode)
    {
        _layout = mode;
        ReconcilePanels();
        return ResolvedLayoutChanged.InvokeAsync(mode);
    }

    /// <summary>
    /// The right-hand surfaces exclude each other on a narrow container / in a sheet presentation, and that must also hold when the
    /// LAYOUT (a resize, a rotation) or <see cref="TaskPanelMode"/> changes while several are open: the task panel keeps the place, the
    /// history drawer and the Reports inspector close. (A panel that was open in-flow stays open as the sheet; it is not re-raised later.)
    /// Runs when the resolved layout changes and when the host-driven <see cref="ShowTaskPanel"/> value changes — not on every parameter set.
    /// </summary>
    private void ReconcilePanels()
    {
        if (!(IsNarrowLayout || PanelsAsSheets) || !_taskPanelOpen) return;
        _historyDrawerOpen = false;
        if (_activeSidebarPanel == GanttSidebarPanel.Reports) _activeSidebarPanel = null;
    }

    private static string LayoutClass(TmLayoutContext layout) => layout.Resolved switch
    {
        TmLayoutMode.Tablet => "tm-gantt--tablet",
        TmLayoutMode.Mobile => "tm-gantt--mobile",
        _ => "tm-gantt--desktop",
    };

    /// <summary>True on a tablet or narrower container (not desktop): the toolbar is two rows and the panels exclude each other.</summary>
    internal bool IsNarrowLayout => _layout != TmLayoutMode.Desktop;

    /// <summary>True when the layout is tablet (the two-row toolbar).</summary>
    internal bool IsTabletLayout => _layout == TmLayoutMode.Tablet;

    /// <summary>True when the task panel and the Reports inspector render as <c>TmSidePanel</c> side sheets.</summary>
    private bool PanelsAsSheets => TaskPanelMode == GanttTaskPanelMode.Overlay
        || (TaskPanelMode == GanttTaskPanelMode.Auto && _layout != TmLayoutMode.Desktop);

    private const string SheetWidth = "clamp(22rem, 50vw, 40rem)";

    /// <summary>True while the "Discard changes?" dialog of a dirty task sheet is on screen (the sheet itself stays open).</summary>
    private bool _discardPromptOpen;

    /// <summary>The rendered task panel (sheet or in-flow): the close path reads its dirty-draft state.</summary>
    private TmGanttTaskPanel? _taskPanel;

    private void HandleTaskSheetOpenChanged(bool open)
    {
        if (open) return;
        // A dirty draft survives the close attempt (Escape / backdrop / close): the sheet stays open
        // and the user decides — Discard closes it, Keep editing returns to the fields.
        if (_taskPanel is { IsDraftDirty: true })
        {
            _discardPromptOpen = true;
            return;
        }

        CloseTaskSheet();
    }

    /// <summary>Closes the task sheet for real: the panel state goes down and focus returns to the row of the task.</summary>
    private void CloseTaskSheet()
    {
        _discardPromptOpen = false;
        SetTaskPanelOpen(false);
        // Focus back where the user was: the row of the task (a bar or a finger tap leaves no focused opener behind).
        if (SelectedTask is not null) RequestFocus(GanttFocusTarget.Row, SelectedTask.Id);
    }

    /// <summary>The "Discard changes?" answer: Discard closes the sheet (the draft is thrown away with the panel), Keep editing keeps it open.</summary>
    private Task ConfirmDiscardAsync(bool? discard)
    {
        _discardPromptOpen = false;
        if (discard == true) CloseTaskSheet();
        return Task.CompletedTask;
    }

    private void HandleReportsSheetOpenChanged(bool open)
    {
        if (!open) _activeSidebarPanel = null;
    }

    // ── Parameters ───────────────────────────────────────────────

    /// <summary>In-memory work items. Used when <see cref="WorkItemSource"/> is null.</summary>
    [Parameter] public IReadOnlyList<TmWorkItem> Items { get; set; } = [];

    /// <summary>In-memory dependencies. Used when <see cref="WorkItemSource"/> is null.</summary>
    [Parameter] public IReadOnlyList<GanttDependency> DependencyItems { get; set; } = [];

    /// <summary>
    /// Unified work-item source shared with other components. When set, the chart loads its
    /// items and dependencies from this provider instead of <see cref="Items"/>/<see cref="DependencyItems"/>,
    /// and routes task mutations back through it (when the provider declares the matching capability).
    /// </summary>
    [Parameter] public ITmWorkItemProvider? WorkItemSource { get; set; }

    private IReadOnlyList<TmWorkItem> _providerTasks = [];
    private IReadOnlyList<GanttDependency> _providerDeps = [];
    private ITmWorkItemProvider? _loadedSource;

    /// <summary>Effective task list: provider data when <see cref="WorkItemSource"/> is set, otherwise <see cref="Items"/>.</summary>
    internal IReadOnlyList<TmWorkItem> Data => WorkItemSource is null ? Items : _providerTasks;

    /// <summary>Effective dependency list: provider data when <see cref="WorkItemSource"/> is set, otherwise <see cref="DependencyItems"/>.</summary>
    internal IReadOnlyList<GanttDependency> Dependencies => WorkItemSource is null ? DependencyItems : _providerDeps;
    [Parameter] public GanttView View { get; set; } = GanttView.Week;
    [Parameter] public EventCallback<GanttView> ViewChanged { get; set; }
    [Parameter] public TmWorkItem? SelectedTask { get; set; }
    [Parameter] public EventCallback<TmWorkItem> OnTaskSelected { get; set; }

    /// <summary>
    /// Whether the task panel is shown. The Gantt NEVER assigns this parameter (assigning a parameter
    /// from inside the component is lost on the next render and fights a two-way host binding): internal
    /// changes (toolbar toggle, task selection, panel close) update <see cref="_taskPanelOpen"/> and raise
    /// <see cref="ShowTaskPanelChanged"/>, and a changed incoming value re-seeds the field. Two-way with
    /// <see cref="ShowTaskPanelChanged"/>.
    /// </summary>
    [Parameter] public bool ShowTaskPanel { get; set; }

    /// <summary>Raised when the task panel's open state changes through an internal path (open on selection, toolbar toggle, sheet close). Pair with <see cref="ShowTaskPanel"/> for two-way binding.</summary>
    [Parameter] public EventCallback<bool> ShowTaskPanelChanged { get; set; }

    /// <summary>The live task-panel open state. Seeded from <see cref="ShowTaskPanel"/> whenever the parameter value changes; every internal change goes through <see cref="SetTaskPanelOpen"/>.</summary>
    private bool _taskPanelOpen;

    /// <summary>The open state as the parts read it (the toolbar toggle's pressed state).</summary>
    internal bool TaskPanelOpen => _taskPanelOpen;

    private bool _lastShowTaskPanel;
    private bool _panelStateSeeded;

    /// <summary>Internal open-state change: never touches the <see cref="ShowTaskPanel"/> parameter; raises <see cref="ShowTaskPanelChanged"/> instead.</summary>
    private void SetTaskPanelOpen(bool open)
    {
        if (_taskPanelOpen == open) return;
        _taskPanelOpen = open;
        _ = ShowTaskPanelChanged.InvokeAsync(open);
    }

    [Parameter] public EventCallback<TmWorkItem> OnTaskUpdated { get; set; }
    [Parameter] public EventCallback<GanttDependency> OnDependencyAdded { get; set; }
    [Parameter] public EventCallback<string> OnDependencyRemoved { get; set; }
    [Parameter] public EventCallback<TmWorkItem> OnTaskAdded { get; set; }
    [Parameter] public EventCallback<GanttTaskInsertedArgs> OnTaskInserted { get; set; }
    [Parameter] public EventCallback<string> OnTaskRemoved { get; set; }
    [Parameter] public EventCallback<GanttTaskReorderedArgs> OnTaskReordered { get; set; }

    /// <summary>View display settings (today marker, days off, density, theme…).</summary>
    [Parameter] public GanttViewSettings ViewSettings { get; set; } = new();

    /// <summary>Fires when the user changes a view setting via the built-in dropdown.</summary>
    [Parameter] public EventCallback<GanttViewSettings> OnViewSettingsChanged { get; set; }

    /// <summary>Working schedule used to shade non-working days/hours in the timeline.</summary>
    [Parameter] public WorkingSchedule WorkingSchedule { get; set; } = new();

    /// <summary>Column definitions for the tree grid. Defaults to all standard columns visible.</summary>
    [Parameter] public IReadOnlyList<GanttColumnDefinition> Columns { get; set; } = DefaultColumns;

    /// <summary>Fires when column visibility/order changes via the column toggle UI.</summary>
    [Parameter] public EventCallback<IReadOnlyList<GanttColumnDefinition>> OnColumnsChanged { get; set; }

    /// <summary>When true, dep-drag handles are rendered on every task bar.</summary>
    [Parameter] public bool AllowDependencyCreation { get; set; }

    /// <summary>When true, each tree row shows a checkbox for bulk selection.</summary>
    [Parameter] public bool AllowBulkSelect { get; set; }

    /// <summary>When true, overdue tasks (End in past, not done) are highlighted.</summary>
    [Parameter] public bool ShowOverdueHighlight { get; set; }

    /// <summary>Current timeline zoom preset (Weeks by default).</summary>
    [Parameter] public GanttZoomPreset ZoomPreset { get; set; } = GanttZoomPreset.Weeks;

    /// <summary>Fires when the user triggers a bulk status/property update.</summary>
    [Parameter] public EventCallback<BulkUpdateArgs> OnBulkUpdate { get; set; }

    /// <summary>Fires when the data order changes via cascade sort.</summary>
    [Parameter] public EventCallback<IReadOnlyList<TmWorkItem>> OnDataSorted { get; set; }

    // ── Phase 3 Parameters ────────────────────────────────────────

    /// <summary>When true, auto-schedules tasks based on dependencies when parameters are set.</summary>
    [Parameter] public bool AutoSchedule { get; set; }

    /// <summary>Fires after auto-scheduling with the updated task list.</summary>
    [Parameter] public EventCallback<IReadOnlyList<TmWorkItem>> OnAutoScheduled { get; set; }

    /// <summary>When true, highlights critical-path tasks with a distinct bar color.</summary>
    [Parameter] public bool ShowCriticalPath { get; set; }

    /// <summary>Named schedule snapshots for baseline comparison.</summary>
    [Parameter] public IReadOnlyList<GanttBaseline> Baselines { get; set; } = [];

    /// <summary>ID of the currently active baseline to compare against.</summary>
    [Parameter] public string? ActiveBaselineId { get; set; }

    /// <summary>Fires after a new baseline is saved.</summary>
    [Parameter] public EventCallback<GanttBaseline> OnBaselineSaved { get; set; }

    /// <summary>User-defined custom field definitions.</summary>
    [Parameter] public IReadOnlyList<TmCustomFieldDefinition> CustomFields { get; set; } = [];

    /// <summary>Fires when a custom field value is committed for a task. Args: (taskId, fieldId, value).</summary>
    [Parameter] public EventCallback<(string TaskId, string FieldId, string? Value)> OnCustomFieldChanged { get; set; }

    /// <summary>Active filter set. Empty = show all tasks.</summary>
    [Parameter] public IReadOnlyList<GanttFilter> Filters { get; set; } = [];

    /// <summary>Fires when the user adds/removes filters via the filter panel.</summary>
    [Parameter] public EventCallback<IReadOnlyList<GanttFilter>> OnFiltersChanged { get; set; }

    // ── Phase 4 parameters ──────────────────────────────────────

    /// <summary>Fires when the user requests an export (contains selected options).</summary>
    [Parameter] public EventCallback<GanttExportOptions> OnExportRequested { get; set; }

    /// <summary>Fires when an import operation completes successfully.</summary>
    [Parameter] public EventCallback<IReadOnlyList<TmWorkItem>> OnImportCompleted { get; set; }

    /// <summary>Fires when an import operation fails (contains error message).</summary>
    [Parameter] public EventCallback<string> OnImportError { get; set; }

    /// <summary>Activity entries to display in the history drawer.</summary>
    [Parameter] public IReadOnlyList<TmActivityEntry> History { get; set; } = [];

    /// <summary>Fires when the user requests time-travel to the given timestamp.</summary>
    [Parameter] public EventCallback<DateTime> OnTimeTravelRequested { get; set; }

    /// <summary>Fires when the user requests rollback to the state before a history entry.</summary>
    [Parameter] public EventCallback<TmActivityEntry> OnRollbackRequested { get; set; }

    // ── Phase 5 Parameters ────────────────────────────────────────

    /// <summary>When true, shows the workload panel below the Gantt timeline.</summary>
    [Parameter] public bool ShowWorkloadPanel { get; set; }

    /// <summary>Whether workload is displayed in hours or as a percentage of capacity.</summary>
    [Parameter] public WorkloadDisplayMode WorkloadDisplayMode { get; set; } = WorkloadDisplayMode.Hours;

    /// <summary>Raised when the user flips the Hours | Percent segment of the docked workload panel (arg = the new mode; two-way with <see cref="WorkloadDisplayMode"/>).</summary>
    [Parameter] public EventCallback<WorkloadDisplayMode> WorkloadDisplayModeChanged { get; set; }

    /// <summary>
    /// Where the workload is shown. <see cref="GanttWorkloadPlacement.View"/> (default) keeps today's alternate
    /// <c>GanttView.Workload</c> view; <see cref="GanttWorkloadPlacement.Docked"/> adds a panel under the timeline that
    /// shares the Gantt's date range and scrolls horizontally with it (the panel renders whatever
    /// <see cref="ShowWorkloadPanel"/> says - that flag only switches on the overload computation behind the warning icons).
    /// </summary>
    [Parameter] public GanttWorkloadPlacement WorkloadPlacement { get; set; } = GanttWorkloadPlacement.View;

    /// <summary>
    /// What the history drawer is positioned against: <see cref="GanttHistoryDrawerPlacement.Component"/> (default) pins it
    /// to the right edge of this Gantt; <see cref="GanttHistoryDrawerPlacement.Viewport"/> restores the pre-redesign
    /// full-viewport-height panel.
    /// </summary>
    [Parameter] public GanttHistoryDrawerPlacement HistoryDrawerPlacement { get; set; } = GanttHistoryDrawerPlacement.Component;

    /// <summary>
    /// When false the command toolbar is not rendered (a nested Gantt of the portfolio, a host that drives the chart
    /// through its own chrome). The panel tabs / sidebar are switched off separately by <see cref="ShowSidebar"/>. Default true.
    /// </summary>
    [Parameter] public bool ShowToolbar { get; set; } = true;

    /// <summary>Optional real-time SignalR connection for live task updates.</summary>
    [Parameter] public IGanttRealtimeConnection? RealtimeConnection { get; set; }

    /// <summary>Notification preferences for the current user.</summary>
    [Parameter] public TmNotificationPreferences? NotificationSettings { get; set; }

    /// <summary>Fires when a task's status is changed from the board view.</summary>
    [Parameter] public EventCallback<(string TaskId, TmWorkItemStatus NewStatus)> OnStatusChanged { get; set; }

    /// <summary>Fires when a notification condition is met (assign, mention, deadline).</summary>
    [Parameter] public EventCallback<TmNotification> OnNotificationTriggered { get; set; }

    /// <summary>Resource calendars for vacation/day-off overlays in People view.</summary>
    [Parameter] public IReadOnlyList<GanttResourceCalendar> ResourceCalendars { get; set; } = [];

    /// <summary>Fires when a resource calendar entry changes.</summary>
    [Parameter] public EventCallback<IReadOnlyList<GanttResourceCalendar>> OnResourceCalendarChanged { get; set; }

    /// <summary>Fires when the user creates a new virtual (placeholder) resource.</summary>
    [Parameter] public EventCallback<TmWorkItemAssignee> OnVirtualResourceAdded { get; set; }

    /// <summary>Task ID of the currently running stopwatch timer. Null = no active timer.</summary>
    [Parameter] public string? ActiveTimerTaskId { get; set; }

    /// <summary>Fires when the user starts the stopwatch for a task.</summary>
    [Parameter] public EventCallback<string> OnTimerStarted { get; set; }

    /// <summary>Fires when the user stops the stopwatch for a task, providing the completed log entry.</summary>
    [Parameter] public EventCallback<(string TaskId, GanttTimeLogEntry Entry)> OnTimerStopped { get; set; }

    /// <summary>When true (default), the left sidebar navigation is visible.</summary>
    [Parameter] public bool ShowSidebar { get; set; } = true;

    /// <summary>
    /// How the toolbar buttons show their text. The default <see cref="ToolbarLabels.Icons"/> keeps today's
    /// icon-only row (the text stays the accessible name and the tooltip); <see cref="ToolbarLabels.IconsWithText"/>
    /// shows every label; <see cref="ToolbarLabels.Auto"/> shows them except on a toolbar narrower than 640px.
    /// Mapped straight onto the core <c>TmToolbar.Labels</c>.
    /// </summary>
    [Parameter] public ToolbarLabels ToolbarLabels { get; set; } = ToolbarLabels.Icons;

    /// <summary>
    /// When true, the toolbar ends with a "Search tasks…" box that writes a <c>Title</c> / <c>Contains</c> entry
    /// into <see cref="Filters"/> (raising <see cref="OnFiltersChanged"/>); emptying the box removes it. Default false.
    /// </summary>
    [Parameter] public bool ShowQuickSearch { get; set; }

    /// <summary>
    /// How the filter editor is presented: <see cref="GanttFilterPanelMode.Inline"/> (default) is a strip under the
    /// toolbar, <see cref="GanttFilterPanelMode.Dialog"/> a modal dialog (a bottom sheet on a mobile viewport) whose
    /// footer is Clear all | Cancel | Apply, and <see cref="GanttFilterPanelMode.Auto"/> picks by the Gantt's own container:
    /// a dialog on a tablet or narrower container, the inline strip on a desktop one.
    /// </summary>
    [Parameter] public GanttFilterPanelMode FilterPanelMode { get; set; } = GanttFilterPanelMode.Inline;

    /// <summary>
    /// How the Tasks / Workload / History / Notifications / Reports panels are offered: the vertical icon
    /// <see cref="GanttSidebarMode.Rail"/> (default) or a horizontal <see cref="GanttSidebarMode.Tabs"/> strip with
    /// text labels. <see cref="ShowSidebar"/> = false hides either.
    /// </summary>
    [Parameter] public GanttSidebarMode SidebarMode { get; set; } = GanttSidebarMode.Rail;

    /// <summary>Host content at the start of the header row, after the <see cref="Title"/> (a project switcher, a breadcrumb).</summary>
    [Parameter] public RenderFragment? HeaderStart { get; set; }

    /// <summary>Host actions at the end of the header row (Share, Save…). The Gantt has no built-in header actions.</summary>
    [Parameter] public RenderFragment? HeaderActions { get; set; }

    /// <summary>Header title text. The header renders only when at least one of Title, Subtitle, HeaderStart or HeaderActions is set.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Secondary header line under the <see cref="Title"/>.</summary>
    [Parameter] public string? Subtitle { get; set; }

    /// <summary>Set false to hide the header row even when its slots are filled. Default true (no header unless a slot is set).</summary>
    [Parameter] public bool ShowHeader { get; set; } = true;

    /// <summary>Report definitions shown in the Reports sidebar panel.</summary>
    [Parameter] public IReadOnlyList<GanttReport> Reports { get; set; } = [];

    /// <summary>Fires when the user clicks "Run" on a report.</summary>
    [Parameter] public EventCallback<GanttReport> OnReportRun { get; set; }

    /// <summary>Fires when the user picks Edit in a report card's menu. The menu item (and the menu) exist only when this is handled.</summary>
    [Parameter] public EventCallback<GanttReport> OnReportEdit { get; set; }

    /// <summary>Fires when the user picks Delete in a report card's menu. The menu item (and the menu) exist only when this is handled.</summary>
    [Parameter] public EventCallback<GanttReport> OnReportDelete { get; set; }

    /// <summary>Fires when the user picks Duplicate in a report card's menu. The menu item (and the menu) exist only when this is handled.</summary>
    [Parameter] public EventCallback<GanttReport> OnReportDuplicate { get; set; }

    /// <summary>
    /// Fires when the user asks for a new report from the Reports panel. The Gantt has no report editor,
    /// so the "New report" button is only rendered when the host handles this callback.
    /// </summary>
    [Parameter] public EventCallback OnNewReportRequested { get; set; }

    /// <summary>Fires when the user edits the description of the selected task in the task panel (live).</summary>
    [Parameter] public EventCallback<string?> OnDescriptionChanged { get; set; }

    /// <summary>Fires when the user picks a file to attach to the selected task in the task panel. Persisting the file is the host's job.</summary>
    [Parameter] public EventCallback<IBrowserFile> OnAttachmentUpload { get; set; }

    /// <summary>Fires when the user removes an attachment of the selected task in the task panel (arg = attachment id).</summary>
    [Parameter] public EventCallback<string> OnAttachmentRemoved { get; set; }

    /// <summary>Additional CSS classes appended to the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Width in pixels the <b>Fit to screen</b> action fits the timeline into. When <c>null</c> (default)
    /// the width of the timeline element is measured in the browser. Obsolete alias: while <see cref="LayoutMode"/> is
    /// <see cref="TmLayoutMode.Auto"/> a width set here is classified like a measurement (below 640 mobile, below 1024
    /// tablet, otherwise desktop), so a host that knew its width keeps working; new code sets <see cref="LayoutMode"/>.
    /// </summary>
    [Obsolete("Use LayoutMode: the Gantt measures its own container. ContainerWidth stays as an alias that is classified into a layout and still overrides the Fit to screen measurement.")]
    [Parameter] public double? ContainerWidth { get; set; }

    /// <summary>
    /// The layout the Gantt renders. <see cref="TmLayoutMode.Auto"/> (default) measures the Gantt's OWN container
    /// (desktop from 1024 px, tablet from 640 px, otherwise mobile); a forced value (or a forced cascading
    /// <c>TmLayoutContext</c> ancestor) pins it. Tablet renders the toolbar as two rows with a "More" overflow, a narrower
    /// task tree and finger-sized targets; the layout is also stamped as <c>data-layout</c> on the root.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>Raised when the resolved layout changes (never raised for the initial value). Do not pair it with <see cref="LayoutMode"/>.</summary>
    [Parameter] public EventCallback<TmLayoutMode> ResolvedLayoutChanged { get; set; }

    /// <summary>
    /// How the task panel (and the Reports inspector) is presented: <see cref="GanttTaskPanelMode.Inline"/> (default) is the
    /// in-flow column at every width, <see cref="GanttTaskPanelMode.Overlay"/> a <c>TmSidePanel</c> side sheet with the tabs
    /// Detail | Attachments | Comments | History, and <see cref="GanttTaskPanelMode.Auto"/> picks the in-flow panel on a
    /// desktop container and the side sheet on a tablet or narrower one.
    /// </summary>
    [Parameter] public GanttTaskPanelMode TaskPanelMode { get; set; } = GanttTaskPanelMode.Inline;
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>Returns active columns sorted by Order.</summary>
    public IReadOnlyList<GanttColumnDefinition> EffectiveColumns =>
        Columns.OrderBy(c => c.Order).ToList();

    // ── Lifecycle ────────────────────────────────────────────────

    private TmUserZoneObserver? _userZoneObserver;

    /// <summary>The circuit's user zone, resolved once in <see cref="OnInitialized"/> (null in a bare test context).</summary>
    private ITmUserTimeZone? _userZone;

    /// <summary>
    /// Today's date in the circuit's user zone — the browser's zone (UTC on Blazor Server until the
    /// probe reports after the first interactive render, the runtime's zone on WebAssembly) — and
    /// the machine-local date only when no <see cref="ITmUserTimeZone"/> is registered (a bare test
    /// context). The instant comes from the injected <see cref="TimeProvider"/>, never from
    /// <see cref="DateTime.Today"/>, so a zone that equals <see cref="TimeZoneInfo.Local"/> still
    /// follows the clock. Drives the today marker, new-task defaults, the overdue highlight and the
    /// embedded calendar.
    /// </summary>
    internal DateTime TodayInZone() => NowInZone().Date;

    /// <summary>The injected clock converted into the display zone. Shared by today and the hours marker.</summary>
    private DateTimeOffset NowInZone()
    {
        var zone = _userZone?.Zone ?? Clock.LocalTimeZone;
        return TimeZoneInfo.ConvertTime(Clock.GetUtcNow(), zone);
    }

    /// <summary>
    /// Clock behind "today" and the hours-zoom marker. Resolved through
    /// <see cref="IServiceProvider"/> so a consumer that never registers a <see cref="TimeProvider"/>
    /// still gets <see cref="TimeProvider.System"/>. Tests register a FakeTimeProvider.
    /// </summary>
    internal TimeProvider Clock
        => _clock ??= Services.GetService<TimeProvider>() ?? TimeProvider.System;
    private TimeProvider? _clock;

    protected override void OnInitialized()
    {
        // DI is available here: pin the zone service and the calendar start in one place. The zone
        // event (RefreshAfterZoneChange) re-pins both once the browser probe reports.
        _userZone = Services.GetService<ITmUserTimeZone>();
        _calendarDate = TodayInZone();

        // GanttTheme.Auto follows the global ThemeService when the host registered one (AddTempoBlazor
        // does). A bare host without it simply inherits the ambient data-theme.
        _themeService = Services.GetService<ThemeService>();
        if (_themeService is not null)
            _themeService.OnChanged += OnGlobalThemeChanged;
    }

    private ThemeService? _themeService;

    private void OnGlobalThemeChanged() => _ = InvokeAsync(StateHasChanged);

    /// <summary>Re-renders once the user zone arrives (the browser probe is Server-only and one-shot).</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _userZoneObserver ??= TmUserZoneObserver.Subscribe(Services, RefreshAfterZoneChange);
            if (_userZoneObserver is not null)
            {
                await _userZoneObserver.ProbeAsync();
            }
        }

        await AttachGesturesAsync();
    }

    // ── Pointer gestures (core pointer-drag.js, wired by gantt.js) ───────────────────────────

    private readonly string _gestureId = "tm-gantt-" + Guid.NewGuid().ToString("N");
    private DotNetObjectReference<TmGantt>? _dotNetRef;
    private string? _attachedGestureKey;
    private bool _disposed;

    /// <summary>
    /// Registers the timeline (pan with a mouse — a finger or a pen scrolls natively, dependency drag, long-press)
    /// and the tree (long-press, Alt+Up/Down) sessions with <c>gantt.js</c>. The elements exist only while the chart
    /// area is shown (a Board / Calendar view replaces them) and are replaced when the docked workload wraps them, so
    /// the registration is repeated whenever that key changes. The key is recorded only after every attach/detach
    /// call succeeded AND the JS side reported the sessions registered (<c>false</c> = the helpers were unavailable),
    /// so a failed attempt is retried on a later render instead of being remembered as done.
    /// </summary>
    private async Task AttachGesturesAsync()
    {
        if (_disposed) return;
        var hasChart = View is not (GanttView.Workload or GanttView.Calendar or GanttView.Board or GanttView.Dashboard or GanttView.People);
        var key = hasChart ? (DockedWorkload ? "docked" : "chart") + (View == GanttView.List ? "-list" : string.Empty) : null;
        if (key == _attachedGestureKey) return;

        try
        {
            if (key is null)
            {
                await JSRuntime.InvokeVoidAsync("tmGantt.detachTimeline", _gestureId);
                await JSRuntime.InvokeVoidAsync("tmGantt.detachTree", _gestureId);
                _attachedGestureKey = key;
                return;
            }

            _dotNetRef ??= DotNetObjectReference.Create(this);
            var attached = true;
            if (View != GanttView.List)
            {
                attached = await JSRuntime.InvokeAsync<bool>("tmGantt.attachTimeline", _gestureId, _timelineContainerRef, _timelineBodyRef, _treeBodyRef, _dotNetRef);
            }
            else
            {
                // The List view has no timeline: release the registration of the one that was there.
                await JSRuntime.InvokeVoidAsync("tmGantt.detachTimeline", _gestureId);
            }

            attached = await JSRuntime.InvokeAsync<bool>("tmGantt.attachTree", _gestureId, _treeBodyRef, _dotNetRef) && attached;

            // Only a fully registered set is remembered: a false (helpers unavailable) or a thrown
            // exception leaves the key unset, so the next render tries again.
            if (attached) _attachedGestureKey = key;
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException or JSException or TaskCanceledException)
        {
            // The gestures are an enhancement over the buttons and the keyboard: a missing script never breaks the chart.
            // The key stays unset, so a later render retries the registration.
        }
    }

    private Task RefreshAfterZoneChange() => InvokeAsync(() =>
    {
        _calendarDate = TodayInZone();
        RefreshVisibleData();
        StateHasChanged();
    });

    protected override void OnParametersSet()
    {
        // The open state is seeded ONLY when the host-driven parameter value actually changed: an internal
        // change (which raised ShowTaskPanelChanged instead of assigning the parameter) must survive the
        // re-render a host without the two-way binding triggers next.
        if (!_panelStateSeeded || ShowTaskPanel != _lastShowTaskPanel)
        {
            _panelStateSeeded = true;
            _lastShowTaskPanel = ShowTaskPanel;
            _taskPanelOpen = ShowTaskPanel;
            ReconcilePanels();
        }

        if (WorkItemSource is null)
            ApplyState();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (WorkItemSource is not null)
        {
            await LoadFromProviderAsync();
            ApplyState();
        }
    }

    private void ApplyState()
    {
        _treeRoots = GanttHelper.BuildTree(Data).ToList();
        if (!ReferenceEquals(Filters, _filtersParam))
        {
            // Only a new host-supplied set replaces the internal one (a re-render with the same
            // reference must not discard filters the user applied inside the component).
            _activeFilters = Filters;
            SyncQuickSearchWithFilters();
        }

        _filtersParam = Filters;

        if (AutoSchedule && Data.Count > 0 && Dependencies.Count > 0)
        {
            var mutableTasks = Data.ToList();
            GanttScheduler.Schedule(mutableTasks, Dependencies);
            _treeRoots = GanttHelper.BuildTree(mutableTasks).ToList();
            // Fire async without blocking lifecycle
            _ = OnAutoScheduled.InvokeAsync(mutableTasks);
        }

        if (ShowCriticalPath)
            _criticalPathIds = new HashSet<string>(CriticalPathCalculator.Calculate(Data, Dependencies));
        else
            _criticalPathIds.Clear();

        // Build baseline position lookup
        _baselinePositions.Clear();
        if (ActiveBaselineId is not null)
        {
            var baseline = Baselines.FirstOrDefault(b => b.Id == ActiveBaselineId);
            if (baseline is not null)
            {
                var (tsStart, tsEnd) = GanttHelper.GetTimeRange(Data);
                var ppd = GetPixelPerDay();
                foreach (var bt in baseline.Tasks)
                {
                    var (left, width) = GanttHelper.CalculateBarPosition(bt.Start, bt.End, tsStart, tsEnd,
                        Math.Max(1, (tsEnd - tsStart).TotalDays * ppd));
                    _baselinePositions[bt.TaskId] = (left, Math.Max(4, width));
                }
            }
        }

        // Subscribe to realtime connection (swap if changed)
        if (_subscribedConnection != RealtimeConnection)
        {
            if (_subscribedConnection is not null)
                _subscribedConnection.OnTaskUpdated -= HandleRealtimeTaskUpdated;
            _subscribedConnection = RealtimeConnection;
            if (_subscribedConnection is not null)
                _subscribedConnection.OnTaskUpdated += HandleRealtimeTaskUpdated;
        }

        RefreshVisibleData();
    }

    private async Task LoadFromProviderAsync()
    {
        if (WorkItemSource is null || ReferenceEquals(_loadedSource, WorkItemSource))
            return;

        _loadedSource = WorkItemSource;
        var result = await WorkItemSource.SearchAsync(new TmWorkItemQuery { IncludeCompleted = true, Take = 1000 });
        _providerTasks = result.Items;

        if (WorkItemSource.Capabilities.HasFlag(TmWorkItemCapabilities.Dependencies))
        {
            var deps = await WorkItemSource.GetDependenciesAsync(_providerTasks.Select(t => t.Id).ToArray());
            _providerDeps = deps.Select(MapDependency).ToList();
        }
    }

    private static GanttDependency MapDependency(TmWorkItemDependency d) => new()
    {
        Id = d.Id,
        FromId = d.FromId,
        ToId = d.ToId,
        LagDays = d.LagDays,
        DepType = (GanttDependencyType)(int)d.Type
    };

    // ── Mutation routing (persists through WorkItemSource when capable) ────────

    private async Task RaiseTaskUpdatedAsync(TmWorkItem task)
    {
        if (WorkItemSource is not null && WorkItemSource.Capabilities.HasFlag(TmWorkItemCapabilities.Update))
        {
            var saved = await WorkItemSource.UpdateAsync(task);
            ReplaceProviderTask(saved);
            ApplyState();
        }
        await OnTaskUpdated.InvokeAsync(task);
    }

    private async Task HandleTaskCommentAddedAsync(TmCommentEntry entry)
    {
        if (SelectedTask is null)
            return;

        if (string.IsNullOrWhiteSpace(entry.ThreadId))
            entry.ThreadId = SelectedTask.Id;

        SelectedTask.Comments.Add(entry);
        await RaiseTaskUpdatedAsync(SelectedTask);
    }

    private async Task RaiseTaskAddedAsync(TmWorkItem task)
    {
        if (WorkItemSource is not null && WorkItemSource.Capabilities.HasFlag(TmWorkItemCapabilities.Create))
        {
            var saved = await WorkItemSource.CreateAsync(task);
            _providerTasks = _providerTasks.Append(saved).ToList();
            ApplyState();
        }
        await OnTaskAdded.InvokeAsync(task);
    }

    private async Task RaiseTaskRemovedAsync(string id)
    {
        if (WorkItemSource is not null && WorkItemSource.Capabilities.HasFlag(TmWorkItemCapabilities.Delete))
        {
            await WorkItemSource.DeleteAsync(id);
            _providerTasks = _providerTasks.Where(t => t.Id != id).ToList();
            ApplyState();
        }
        await OnTaskRemoved.InvokeAsync(id);
    }

    private void ReplaceProviderTask(TmWorkItem saved)
    {
        var list = _providerTasks.ToList();
        var idx = list.FindIndex(t => t.Id == saved.Id);
        if (idx >= 0) list[idx] = saved; else list.Add(saved);
        _providerTasks = list;
    }

    private void HandleRealtimeTaskUpdated(TmWorkItem updatedTask)
    {
        var existing = Data.FirstOrDefault(t => t.Id == updatedTask.Id);
        if (existing is null) return;

        existing.Title          = updatedTask.Title;
        existing.Start          = updatedTask.Start;
        existing.End            = updatedTask.End;
        existing.PercentComplete = updatedTask.PercentComplete;
        existing.Status         = updatedTask.Status;
        existing.Priority       = updatedTask.Priority;

        _treeRoots = GanttHelper.BuildTree(Data).ToList();
        RefreshVisibleData();
        InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // The C# unsubscriptions first (a JS callback racing the detach could still reach a live object),
        // then the JS detach, then the reference.
        Dispose();

        try
        {
            if (_attachedGestureKey is not null)
            {
                await JSRuntime.InvokeVoidAsync("tmGantt.detachTimeline", _gestureId);
                await JSRuntime.InvokeVoidAsync("tmGantt.detachTree", _gestureId);
            }
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException or JSException or TaskCanceledException)
        {
        }

        _dotNetRef?.Dispose();
        _dotNetRef = null;
    }

    public void Dispose()
    {
        if (_themeService is not null)
        {
            _themeService.OnChanged -= OnGlobalThemeChanged;
            _themeService = null;
        }

        _userZoneObserver?.Dispose();
        _userZoneObserver = null;
        if (_subscribedConnection is not null)
        {
            _subscribedConnection.OnTaskUpdated -= HandleRealtimeTaskUpdated;
            _subscribedConnection = null;
        }
    }

    // ── View & Zoom ──────────────────────────────────────────────

    private GanttZoomPreset _zoomPresetOverride = GanttZoomPreset.Weeks;
    private bool _zoomPresetOverrideSet;

    internal GanttZoomPreset GetEffectiveZoomPreset() =>
        _zoomPresetOverrideSet ? _zoomPresetOverride : ZoomPreset;

    internal bool IsViewActive(GanttView view) => View == view;


    internal async Task SetViewAsync(GanttView view)
    {
        View = view;
        if (view == GanttView.Day)   { _zoomPresetOverride = GanttZoomPreset.Days;   _zoomPresetOverrideSet = true; }
        if (view == GanttView.Week)  { _zoomPresetOverride = GanttZoomPreset.Weeks;  _zoomPresetOverrideSet = true; }
        if (view == GanttView.Month) { _zoomPresetOverride = GanttZoomPreset.Months; _zoomPresetOverrideSet = true; }
        await ViewChanged.InvokeAsync(view);
        RefreshVisibleData();
    }

    internal async Task ZoomInAsync()
    {
        _zoomLevel = Math.Min(200, _zoomLevel + 25);
        RefreshVisibleData();
    }

    internal async Task ZoomOutAsync()
    {
        _zoomLevel = Math.Max(50, _zoomLevel - 25);
        RefreshVisibleData();
    }

    internal async Task FitToScreenAsync()
    {
        // ContainerWidth overrides the measurement; otherwise measure THIS Gantt's timeline through its
        // ElementReference (no eval, no global querySelector - several Gantts can share a page).
        var containerWidth = ContainerWidth
            ?? await JSRuntime.InvokeAsync<double>("tmGantt.getClientWidth", _timelineContainerRef);
        await ApplyFitToScreenAsync(containerWidth);
    }

    internal async Task ApplyFitToScreenAsync(double containerWidth)
    {
        if (containerWidth <= 0 || _totalTimelineWidth <= 0) return;
        // _totalTimelineWidth already includes the current zoom, so scale the current level by the ratio.
        var newZoom = _zoomLevel * containerWidth / _totalTimelineWidth;
        _zoomLevel = (int)Math.Clamp(Math.Floor(newZoom / 25) * 25, 50, 200);
        RefreshVisibleData();
        StateHasChanged();
    }

    // ── View Settings ────────────────────────────────────────────

    internal void ToggleViewSettings() => _viewSettingsOpen = !_viewSettingsOpen;

    internal void ToggleSidebarPanel(GanttSidebarPanel panel)
    {
        _activeSidebarPanel = _activeSidebarPanel == panel ? null : panel;
        YieldToReportsPanel();
    }

    /// <summary>Tab strip (<see cref="GanttSidebarMode.Tabs"/>): select a panel; the Tasks tab is the Gantt itself, so it clears the drawer.</summary>
    internal void SelectSidebarTab(GanttSidebarPanel panel)
    {
        _activeSidebarPanel = panel == GanttSidebarPanel.Tasks ? null : panel;
        YieldToReportsPanel();
    }

    /// <summary>On a narrow container the Reports inspector is the one open right-hand surface: the task panel and the history drawer close.</summary>
    private void YieldToReportsPanel()
    {
        if (_activeSidebarPanel != GanttSidebarPanel.Reports || !(IsNarrowLayout || PanelsAsSheets)) return;
        SetTaskPanelOpen(false);
        _historyDrawerOpen = false;
    }

    /// <summary>Stable per-instance token for element ids that must not collide between Gantts on one page.</summary>
    internal string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>DOM id of a panel tab (<see cref="GanttSidebarMode.Tabs"/>).</summary>
    internal string TabDomId(GanttSidebarPanel panel) => $"tm-gantt-{InstanceId}-tab-{panel.ToString().ToLowerInvariant()}";

    /// <summary>DOM id of the panel a tab controls; the Tasks panel is the main tree + timeline area.</summary>
    internal string PanelDomId(GanttSidebarPanel panel) => $"tm-gantt-{InstanceId}-panel-{panel.ToString().ToLowerInvariant()}";

    /// <summary>True when the header row renders: not switched off and at least one of its slots is filled.</summary>
    private bool HasHeader => ShowHeader
        && (!string.IsNullOrEmpty(Title) || !string.IsNullOrEmpty(Subtitle) || HeaderStart is not null || HeaderActions is not null);

    internal void UpdateNotification(Action<TmNotificationPreferences> apply)
    {
        if (NotificationSettings is null) return;
        apply(NotificationSettings);
        StateHasChanged();
    }

    /// <summary>Distinct assignee IDs present in all tasks — drives the People view rows.</summary>
    private IReadOnlyList<string> GetPeopleViewAssigneeIds()
        => Data.SelectMany(t => t.Assignees)
               .Select(a => a.Id)
               .Distinct()
               .OrderBy(id => id)
               .ToList();

    private async Task ChangeBoardStatusAsync(string taskId, TmWorkItemStatus newStatus)
    {
        var task = Data.FirstOrDefault(t => t.Id == taskId);
        if (task is null) return;
        task.Status = newStatus;
        RefreshVisibleData();
        await OnStatusChanged.InvokeAsync((taskId, newStatus));
    }

    private async Task OnViewSettingChangedAsync(GanttViewSettings updated)
    {
        ViewSettings = updated;
        _viewSettingsOpen = false;
        RefreshVisibleData();
        await OnViewSettingsChanged.InvokeAsync(ViewSettings);
    }

    internal async Task SetShowTodayMarker(bool value)
    {
        ViewSettings.ShowTodayMarker = value;
        RefreshVisibleData();
        await OnViewSettingsChanged.InvokeAsync(ViewSettings);
    }

    internal async Task SetShowProgressLabel(bool value)
    {
        ViewSettings.ShowProgressLabel = value;
        await OnViewSettingsChanged.InvokeAsync(ViewSettings);
    }

    internal async Task SetShowDaysOff(bool value)
    {
        ViewSettings.ShowDaysOff = value;
        RefreshVisibleData();
        await OnViewSettingsChanged.InvokeAsync(ViewSettings);
    }

    internal async Task SetShowClosedTasks(bool value)
    {
        ViewSettings.ShowClosedTasks = value;
        RefreshVisibleData();
        await OnViewSettingsChanged.InvokeAsync(ViewSettings);
    }

    internal async Task SetTaskNameLocation(GanttTaskNameLocation loc)
    {
        ViewSettings.TaskNameLocation = loc;
        RefreshVisibleData();
        await OnViewSettingsChanged.InvokeAsync(ViewSettings);
    }

    internal async Task SetViewDensity(GanttViewDensity density)
    {
        ViewSettings.ViewDensity = density;
        RefreshVisibleData();
        await OnViewSettingsChanged.InvokeAsync(ViewSettings);
    }

    internal async Task SetTheme(GanttTheme theme)
    {
        ViewSettings.Theme = theme;
        await OnViewSettingsChanged.InvokeAsync(ViewSettings);
    }

    // ── Parts (TmGanttToolbar / TmGanttTree / TmGanttTimeline / TmGanttSidebar) ──────────

    /// <summary>
    /// Moves on every data refresh and on every in-place edit of a task. The tree and the timeline compare it
    /// (among the rest of what they draw) to skip a render pass that cannot change their output.
    /// </summary>
    internal int DrawVersion { get; private set; }

    /// <summary>
    /// A hash of every task field the tree and the timeline paint. Tasks are mutated in place (the inline edit,
    /// the task panel, a host, a realtime update) and the monolith read them at render time, so a render pass
    /// always showed the current values. The parts skip a pass whose state is unchanged; this fingerprint is part
    /// of that state, which keeps the skip from ever hiding an in-place edit. It walks the list once (a few field
    /// reads per task) - far cheaper than rendering one element per row, bar and header cell.
    /// </summary>
    internal int TaskFingerprint()
    {
        var hash = new HashCode();
        foreach (var task in Data)
        {
            hash.Add(task.Id);
            hash.Add(task.Title);
            hash.Add(task.ParentId);
            hash.Add(task.Start);
            hash.Add(task.End);
            hash.Add(task.DueDate);
            hash.Add(task.PercentComplete);
            hash.Add(task.Status);
            hash.Add(task.Priority);
            hash.Add(task.Color);
            hash.Add(task.IsMilestone);
            hash.Add(task.IsExpanded);
            hash.Add(task.UseManualDates);
            hash.Add(task.Attachments.Count);
            hash.Add(task.Comments.Count);
            foreach (var assignee in task.Assignees)
            {
                hash.Add(assignee.Id);
                hash.Add(assignee.Name);
                hash.Add(assignee.AvatarUrl);
                hash.Add(assignee.IsVirtual);
            }

            foreach (var (key, value) in task.CustomFields)
            {
                hash.Add(key);
                hash.Add(value);
            }
        }

        return hash.ToHashCode();
    }
    /// <summary>Resolves an internal test id; the parts call it so a <c>TestIdPrefix</c> reaches their markup.</summary>
    internal string TestIdFor(string name) => TestId(name);

    /// <summary>The display zone: the circuit's user zone, else the clock's local zone.</summary>
    internal TimeZoneInfo DisplayZone => _userZone?.Zone ?? Clock.LocalTimeZone;

    /// <summary>
    /// Event hand-off of a part. Blazor re-renders the component that RECEIVES an event; a part's handler
    /// mutates this component's state and this component's own markup (filter panel, task panel, context
    /// menu, dialogs) follows it, so the part forwards the event here and this component renders exactly as
    /// it did when the toolbar, tree and timeline were its own markup. Mirrors ComponentBase's own
    /// <c>IHandleEvent</c>: render once now, and again when an asynchronous handler completes.
    /// </summary>
    internal Task HandlePartEventAsync(EventCallbackWorkItem callback, object? arg)
    {
        var task = callback.InvokeAsync(arg);
        var awaitTask = task.Status is not (TaskStatus.RanToCompletion or TaskStatus.Canceled);
        StateHasChanged();
        return awaitTask ? RenderAfterAsync(task) : Task.CompletedTask;
    }

    private async Task RenderAfterAsync(Task task)
    {
        try
        {
            await task;
        }
        catch when (task.IsCanceled)
        {
            return;
        }

        StateHasChanged();
    }

    /// <summary>Zoom slider (<c>change</c>): sets the level and re-lays out the timeline.</summary>
    internal void SetZoomLevel(ChangeEventArgs e)
    {
        _zoomLevel = int.Parse(e.Value!.ToString()!);
        RefreshVisibleData();
    }

    /// <summary>Toolbar: show / hide the task panel.</summary>
    internal void ToggleTaskPanel()
    {
        if (_taskPanelOpen) SetTaskPanelOpen(false);
        else OpenTaskPanelExclusive();
    }

    /// <summary>Bulk bar: clear the checkbox selection.</summary>
    internal void ClearBulkSelection() => _selectedTaskIds = [];
    // ── Interaction ──────────────────────────────────────────────

    /// <summary>
    /// Pointer type of the last <c>pointerup</c> on a row or a bar (recorded by the parts). The sheet tap gesture
    /// reads it: a finger or a pen tap opens the task sheet, a mouse click must not (the sheet would cover the
    /// chart and swallow the double-click's second click, which is the row's inline edit).
    /// </summary>
    internal string? _lastPointerType;

    /// <summary>The parts' <c>@onpointerup</c> land here before the click opens the panel.</summary>
    internal void NotePointerUp(PointerEventArgs e) => _lastPointerType = e.PointerType;

    internal async Task ToggleExpandAsync(TmWorkItem task)
    {
        task.IsExpanded = !task.IsExpanded;
        RefreshVisibleData();
    }

    internal async Task SelectTaskAsync(TmWorkItem task)
    {
        SelectedTask = task;
        _focusedRowId = task.Id;
        await OnTaskSelected.InvokeAsync(task);
    }

    /// <summary>
    /// A tap / click on a row or a bar: selects the task and, when the task panel is a side sheet AND the pointer was not
    /// a mouse (<see cref="_lastPointerType"/>), opens it — a mouse double-click must stay an inline edit of the row, not
    /// raise a modal sheet over the chart the second click lands on. Keyboard / "open" activation uses
    /// <see cref="ActivateTaskAsync"/> and always opens the panel; an in-flow panel is already there or toggled.
    /// Programmatic selections (Add, Duplicate, the Board, the Calendar) use <see cref="SelectTaskAsync"/> and never raise
    /// a modal sheet over the toolbar the user is working with.
    /// </summary>
    internal async Task TapTaskAsync(TmWorkItem task)
    {
        if (PanelsAsSheets && _lastPointerType != "mouse") OpenTaskPanelExclusive();
        await SelectTaskAsync(task);
    }

    /// <summary>Keyboard / "open" activation: selects the task and shows the task panel.</summary>
    internal async Task ActivateTaskAsync(TmWorkItem task)
    {
        OpenTaskPanelExclusive();
        await SelectTaskAsync(task);
    }

    /// <summary>
    /// Opens the task panel. On a tablet or narrower container (or with a sheet presentation) at most one right-hand
    /// surface is open: the Reports inspector and the history drawer yield to it.
    /// </summary>
    private void OpenTaskPanelExclusive()
    {
        SetTaskPanelOpen(true);
        if (IsNarrowLayout || PanelsAsSheets)
        {
            _historyDrawerOpen = false;
            if (_activeSidebarPanel == GanttSidebarPanel.Reports) _activeSidebarPanel = null;
        }
    }

    // ── Treegrid keyboard (rows) and bar focus ───────────────────

    private string? _focusedRowId;
    private int _focusSeq;

    /// <summary>The last request to move DOM focus to a row or a bar; the part that owns the element acts on it after its render.</summary>
    internal GanttFocusRequest? FocusRequest { get; private set; }

    /// <summary>A row took DOM focus (click, Tab, a programmatic move): it becomes the roving tab stop.</summary>
    internal void NoteRowFocus(string taskId) => _focusedRowId = taskId;

    internal void RequestFocus(GanttFocusTarget target, string taskId) => FocusRequest = new GanttFocusRequest(target, taskId, ++_focusSeq);

    /// <summary>
    /// The row that carries the roving tab stop: the last focused / selected row while it is still visible, else
    /// the first visible row (so the grid is always reachable with one Tab).
    /// </summary>
    internal string? EffectiveFocusRowId()
    {
        if (_visibleNodes.Count == 0) return null;
        if (_focusedRowId is not null && _visibleNodes.Any(n => n.Task.Id == _focusedRowId)) return _focusedRowId;
        if (SelectedTask is not null && _visibleNodes.Any(n => n.Task.Id == SelectedTask.Id)) return SelectedTask.Id;
        return _visibleNodes[0].Task.Id;
    }

    private void FocusRowAt(int index)
    {
        if (index < 0 || index >= _visibleNodes.Count) return;
        var id = _visibleNodes[index].Task.Id;
        _focusedRowId = id;
        RequestFocus(GanttFocusTarget.Row, id);
    }

    /// <summary>
    /// ARIA treegrid keys on a row: Up / Down / Home / End move between rows, Right expands (then steps into the
    /// first child, then onto the bar of a leaf), Left collapses (then steps to the parent), Enter opens the task
    /// panel. Space activates on key-up, like a native button (docs/keyboard-activation-convention.md).
    /// </summary>
    internal async Task HandleRowKeyDownAsync(KeyboardEventArgs e, GanttTaskNode node)
    {
        if (e.CtrlKey || e.AltKey || e.MetaKey) return;
        var index = _visibleNodes.FindIndex(n => n.Task.Id == node.Task.Id);
        if (index < 0) return;

        switch (e.Key)
        {
            case "ArrowDown": FocusRowAt(index + 1); break;
            case "ArrowUp": FocusRowAt(index - 1); break;
            case "Home": FocusRowAt(0); break;
            case "End": FocusRowAt(_visibleNodes.Count - 1); break;
            case "ArrowRight":
                if (node.Children.Count > 0)
                {
                    if (!node.Task.IsExpanded) await ToggleExpandAsync(node.Task);
                    else if (index + 1 < _visibleNodes.Count && _visibleNodes[index + 1].Task.ParentId == node.Task.Id) FocusRowAt(index + 1);
                }
                else if (View != GanttView.List)
                {
                    RequestFocus(GanttFocusTarget.Bar, node.Task.Id);
                }
                break;
            case "ArrowLeft":
                if (node.Children.Count > 0 && node.Task.IsExpanded) await ToggleExpandAsync(node.Task);
                else if (node.Task.ParentId is not null)
                {
                    var parentIndex = _visibleNodes.FindIndex(n => n.Task.Id == node.Task.ParentId);
                    if (parentIndex >= 0) FocusRowAt(parentIndex);
                }
                break;
            case "Enter":
                if (!e.Repeat) await ActivateTaskAsync(node.Task);
                break;
        }
    }

    internal async Task HandleRowKeyUpAsync(KeyboardEventArgs e, GanttTaskNode node)
    {
        if (e.Key == " " && !e.CtrlKey && !e.AltKey && !e.MetaKey) await ActivateTaskAsync(node.Task);
    }

    /// <summary>Keys on a bar or milestone (a non-native focusable: Enter on key-down, Space on key-up; Escape / Left return to the row).</summary>
    internal async Task HandleBarKeyDownAsync(KeyboardEventArgs e, TmWorkItem task)
    {
        if (e.CtrlKey || e.AltKey || e.MetaKey) return;
        switch (e.Key)
        {
            case "Enter":
                if (!e.Repeat) await ActivateTaskAsync(task);
                break;
            case "Escape":
            case "ArrowLeft":
                _focusedRowId = task.Id;
                RequestFocus(GanttFocusTarget.Row, task.Id);
                break;
        }
    }

    internal async Task HandleBarKeyUpAsync(KeyboardEventArgs e, TmWorkItem task)
    {
        if (e.Key == " " && !e.CtrlKey && !e.AltKey && !e.MetaKey) await ActivateTaskAsync(task);
    }

    internal async Task ExpandAllAsync()
    {
        GanttHelper.SetAllExpanded(_treeRoots, true);
        RefreshVisibleData();
    }

    internal async Task CollapseAllAsync()
    {
        GanttHelper.SetAllExpanded(_treeRoots, false);
        RefreshVisibleData();
    }

    internal async Task AddTaskAsync()
    {
        string? parentId = null;
        if (SelectedTask is not null)
            parentId = SelectedTask.ParentId;

        var today = TodayInZone();
        var newTask = new TmWorkItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = Loc["TmGantt_NewTaskTitle"],
            Start = today,
            End = today.AddDays(1),
            PercentComplete = 0,
            ParentId = parentId
        };

        var args = new GanttTaskInsertedArgs { Task = newTask, Position = GanttInsertPosition.End };
        await OnTaskInserted.InvokeAsync(args);
        await RaiseTaskAddedAsync(newTask);
        await SelectTaskAsync(newTask);
    }

    internal void ShowContextMenu(MouseEventArgs e, TmWorkItem task)
    {
        _contextMenuX = e.ClientX;
        _contextMenuY = e.ClientY;
        _contextMenuTask = task;
        _contextMenuVisible = true;
    }

    private void HideContextMenu()
    {
        _contextMenuVisible = false;
        _contextMenuTask = null;
    }

    /// <summary>
    /// The click-away scrim. A touch long-press opens the menu while the finger is still down, so the compatibility
    /// <c>click</c> of the lifting finger lands on the scrim: it is ignored for a moment after a touch open.
    /// </summary>
    private void HideContextMenuFromOverlay()
    {
        if (_touchMenuOpenedAt != 0 && Environment.TickCount64 - _touchMenuOpenedAt < 600) return;
        HideContextMenu();
    }

    /// <summary>
    /// Long-press (touch hold) on a bar or a tree row: opens the same context menu a right-click opens, anchored at the
    /// finger. Called by the pointer helper in <c>gantt.js</c>.
    /// </summary>
    [JSInvokable]
    public Task OnLongPressContextMenu(string taskId, double x, double y)
        => InvokeAsync(() =>
        {
            var task = Data.FirstOrDefault(t => t.Id == taskId);
            if (task is null) return;
            _contextMenuX = x;
            _contextMenuY = y;
            _contextMenuTask = task;
            _contextMenuVisible = true;
            _touchMenuOpenedAt = Environment.TickCount64;
            StateHasChanged();
        });

    // ── Reorder without dragging (WCAG 2.5.7): Alt+Up/Down on a row and "Move up / down" in the menus ─────────────

    /// <summary>Sets the polite live-region text; an identical repeat is made distinct (a trailing no-break space) so it is announced again.</summary>
    private void Announce(string message)
    {
        _announcement = message == _announcement?.TrimEnd('\u00A0') ? _announcement + "\u00A0" : message;
    }

    private bool CanMoveContextTask(int step) => _contextMenuTask is not null && CanMove(_contextMenuTask, step);

    /// <summary>The move items need a host that applies the reorder; without an <see cref="OnTaskReordered"/> handler they are disabled (a no-op must not announce success).</summary>
    internal bool CanReorder => OnTaskReordered.HasDelegate;

    /// <summary>The task panel's move flags for the currently selected task (false without a selection and when reordering is unavailable — both false hide the pair).</summary>
    private bool CanMoveSelected(int step) => SelectedTask is not null && CanMove(SelectedTask, step);

    internal bool CanMove(TmWorkItem task, int step)
    {
        if (!CanReorder) return false;
        var siblings = SiblingsOf(task);
        var index = siblings.FindIndex(t => t.Id == task.Id);
        var target = index + step;
        return index >= 0 && target >= 0 && target < siblings.Count;
    }

    /// <summary>The VISIBLE tasks that share the task's parent, in the order the tree shows them (a task hidden by a filter, the quick search or "show closed" is never the neighbour).</summary>
    private List<TmWorkItem> SiblingsOf(TmWorkItem task)
        => _visibleNodes.Select(n => n.Task).Where(t => t.ParentId == task.ParentId).ToList();

    private async Task MoveContextTaskAsync(int step)
    {
        var task = _contextMenuTask;
        HideContextMenu();
        if (task is not null) await MoveTaskAsync(task, step);
    }

    /// <summary>
    /// Moves a task one place up (<paramref name="step"/> -1) or down (+1) among its siblings through the SAME
    /// <see cref="OnTaskReordered"/> path the drag-and-drop uses, and announces it. Does nothing at the first / last place.
    /// </summary>
    internal async Task MoveTaskAsync(TmWorkItem task, int step)
    {
        if (!CanReorder) return;
        var siblings = SiblingsOf(task);
        var index = siblings.FindIndex(t => t.Id == task.Id);
        var target = index + step;
        if (index < 0 || target < 0 || target >= siblings.Count) return;

        var neighbour = siblings[target];
        await OnTaskReordered.InvokeAsync(new GanttTaskReorderedArgs
        {
            TaskId = task.Id,
            TargetTaskId = neighbour.Id,
            Position = step < 0 ? GanttDropPosition.Before : GanttDropPosition.After,
        });
        Announce(Loc[step < 0 ? "TmGantt_MovedUp" : "TmGantt_MovedDown", task.Title, neighbour.Title]);
        _focusedRowId = task.Id;
        // A modal side sheet is open over the chart: the row behind it cannot take the focus.
        if (!(_taskPanelOpen && PanelsAsSheets)) RequestFocus(GanttFocusTarget.Row, task.Id);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Alt+Up / Alt+Down on a focused tree row (called by <c>gantt.js</c>; the browser's Back / Forward chord is cancelled there).</summary>
    [JSInvokable]
    public Task OnTreeKeyboardMove(string taskId, int step)
        => InvokeAsync(async () =>
        {
            var task = Data.FirstOrDefault(t => t.Id == taskId);
            if (task is not null && step is -1 or 1) await MoveTaskAsync(task, step);
        });

    // ── Dependency drag (finger, mouse and pen: one pointer session in gantt.js) ─────────────────────────────────

    /// <summary>A dependency handle was dragged onto another bar. Commits through the same path as <see cref="OnDepDrop"/>.</summary>
    [JSInvokable]
    public Task OnDependencyDragCompleted(string fromId, bool fromEnd, string toId)
        => InvokeAsync(async () =>
        {
            if (!AllowDependencyCreation || string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId) || fromId == toId) return;
            StartDepDrag(fromId, fromEnd);
            await CommitDependencyAsync(toId);
        });

    private async Task InsertTaskAsync(GanttInsertPosition position)
    {
        if (_contextMenuTask is null) return;

        var newTask = await InsertRelativeCoreAsync(_contextMenuTask, position);
        HideContextMenu();
        await SelectTaskAsync(newTask);
    }

    /// <summary>The toolbar "Add task" menu: insert above / below / as a child of the selected task.</summary>
    internal async Task InsertAtSelectionAsync(GanttInsertPosition position)
    {
        if (SelectedTask is null) return;

        var newTask = await InsertRelativeCoreAsync(SelectedTask, position);
        await SelectTaskAsync(newTask);
    }

    private async Task<TmWorkItem> InsertRelativeCoreAsync(TmWorkItem reference, GanttInsertPosition position)
    {
        var today = TodayInZone();
        var newTask = new TmWorkItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = Loc["TmGantt_NewTaskTitle"],
            Start = today,
            End = today.AddDays(1),
            PercentComplete = 0,
            ParentId = position == GanttInsertPosition.Child ? reference.Id : reference.ParentId
        };

        var args = new GanttTaskInsertedArgs
        {
            Task = newTask,
            ReferenceTaskId = reference.Id,
            Position = position
        };

        await OnTaskInserted.InvokeAsync(args);
        await RaiseTaskAddedAsync(newTask);
        return newTask;
    }

    private async Task DeleteTaskAsync()
    {
        if (_contextMenuTask is null) return;
        var task = _contextMenuTask;
        await DeleteTaskCoreAsync(task);
        HideContextMenu();
    }

    private async Task DeleteTaskCoreAsync(TmWorkItem task)
    {
        var removedId = task.Id;
        await RaiseTaskRemovedAsync(removedId);

        if (SelectedTask?.Id == removedId)
        {
            SelectedTask = null;
            await OnTaskSelected.InvokeAsync(null!);
        }
    }

    /// <summary>The context menu's Duplicate entry.</summary>
    private async Task DuplicateContextTaskAsync()
    {
        if (_contextMenuTask is null) return;
        var task = _contextMenuTask;
        HideContextMenu();
        var copy = await DuplicateTaskCoreAsync(task);
        await SelectTaskAsync(copy);
    }

    /// <summary>
    /// A copy of <paramref name="source"/> placed below it: same plan (dates, parent, colour, priority, assignees,
    /// estimate), no progress, no logged time, comments or attachments. Raises <c>OnTaskInserted</c> and <c>OnTaskAdded</c>
    /// exactly like an inserted task.
    /// </summary>
    private async Task<TmWorkItem> DuplicateTaskCoreAsync(TmWorkItem source)
    {
        var copy = new TmWorkItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = $"{source.Title} {Loc["TmGantt_DuplicateSuffix"]}",
            Description = source.Description,
            Start = source.Start,
            End = source.End,
            DueDate = source.DueDate,
            IsMilestone = source.IsMilestone,
            Priority = source.Priority,
            ParentId = source.ParentId,
            Color = source.Color,
            EstimationHours = source.EstimationHours,
            BudgetHours = source.BudgetHours,
            UseManualDates = source.UseManualDates,
            Assignees = source.Assignees.ToList(),
            Tags = source.Tags.ToList(),
            CustomFields = new Dictionary<string, string?>(source.CustomFields),
            PercentComplete = 0,
        };

        await OnTaskInserted.InvokeAsync(new GanttTaskInsertedArgs
        {
            Task = copy,
            ReferenceTaskId = source.Id,
            Position = GanttInsertPosition.Below,
        });
        await RaiseTaskAddedAsync(copy);
        return copy;
    }

    // ── Task panel hand-offs (status, actions, overload, baseline) ──────

    /// <summary>The panel's Status field: the board's path - the task changes now, the host hears <c>OnStatusChanged</c>; Save then persists the whole task.</summary>
    private Task OnTaskPanelStatusChangedAsync((string TaskId, TmWorkItemStatus NewStatus) change)
        => ChangeBoardStatusAsync(change.TaskId, change.NewStatus);

    private async Task OnTaskPanelActionAsync(GanttTaskAction action)
    {
        var task = SelectedTask;
        if (task is null) return;

        switch (action)
        {
            case GanttTaskAction.MarkDone:
                task.PercentComplete = 100;
                await ChangeBoardStatusAsync(task.Id, TmWorkItemStatus.Done);
                await RaiseTaskUpdatedAsync(task);
                break;
            case GanttTaskAction.AddAbove:
                await InsertAtSelectionAsync(GanttInsertPosition.Above);
                break;
            case GanttTaskAction.AddBelow:
                await InsertAtSelectionAsync(GanttInsertPosition.Below);
                break;
            case GanttTaskAction.AddChild:
                await InsertAtSelectionAsync(GanttInsertPosition.Child);
                break;
            case GanttTaskAction.Duplicate:
                await SelectTaskAsync(await DuplicateTaskCoreAsync(task));
                break;
            case GanttTaskAction.MoveUp:
                await MoveTaskAsync(task, -1);
                break;
            case GanttTaskAction.MoveDown:
                await MoveTaskAsync(task, 1);
                break;
            case GanttTaskAction.Delete:
                // Destructive: confirmed first (the same pattern as the history rollback).
                _pendingDelete = task;
                break;
        }
    }

    private async Task ConfirmDeleteAsync(bool? confirmed)
    {
        var task = _pendingDelete;
        _pendingDelete = null;
        if (confirmed == true && task is not null)
        {
            await DeleteTaskCoreAsync(task);
        }
    }

    /// <summary>True when one of the selected task's assignees is over capacity on some day (read-only note of the task panel).</summary>
    private bool SelectedTaskOverloaded =>
        SelectedTask is { } task && task.Assignees.Any(a => _overloadedAssigneeIds.Contains(a.Id));

    /// <summary>The selected task's entry in the active baseline, for the panel's read-only deviation note.</summary>
    private GanttBaselineTask? SelectedTaskBaseline =>
        SelectedTask is { } task && ActiveBaselineId is not null
            ? Baselines.FirstOrDefault(b => b.Id == ActiveBaselineId)?.Tasks.FirstOrDefault(b => b.TaskId == task.Id)
            : null;

    /// <summary>A history entry's task link: select the task and open its panel.</summary>
    internal async Task OpenTaskByIdAsync(string taskId)
    {
        var task = Data.FirstOrDefault(t => t.Id == taskId);
        if (task is null) return;
        await ActivateTaskAsync(task);
    }

    private async Task OnWorkloadModeChangedAsync(WorkloadDisplayMode mode)
    {
        WorkloadDisplayMode = mode;
        await WorkloadDisplayModeChanged.InvokeAsync(mode);
    }

    /// <summary>The attributes the Reports panel carries as the tab panel of its tab (<see cref="GanttSidebarMode.Tabs"/>); none for the icon rail.</summary>
    private Dictionary<string, object> ReportsPanelAttributes => SidebarMode == GanttSidebarMode.Tabs
        ? new() { ["role"] = "tabpanel", ["id"] = PanelDomId(GanttSidebarPanel.Reports), ["aria-labelledby"] = TabDomId(GanttSidebarPanel.Reports) }
        : [];

    /// <summary>The Reports inspector is the right-hand panel while the Reports tab / rail button is active; the task panel yields to it (one right panel at a time).</summary>
    private bool ReportsPanelOpen => _activeSidebarPanel == GanttSidebarPanel.Reports;

    /// <summary>The docked workload lines up with the timeline above it, so it exists only under the views that render one (Day / Week / Month).</summary>
    private bool DockedWorkload => WorkloadPlacement == GanttWorkloadPlacement.Docked && View is GanttView.Day or GanttView.Week or GanttView.Month;

    // ── Drag & Drop ──────────────────────────────────────────────

    internal void OnDragStart(TmWorkItem task)
    {
        _draggedTaskId = task.Id;
        _dropTargetId = null;
        _dropPosition = GanttDropPosition.None;
    }

    internal void OnDragOver(TmWorkItem targetTask, DragEventArgs e)
    {
        if (_draggedTaskId == targetTask.Id) return;
        var offsetY = e.OffsetY;
        _dropTargetId = targetTask.Id;
        _dropPosition = offsetY switch
        {
            < 12 => GanttDropPosition.Before,
            > 28 => GanttDropPosition.After,
            _ => GanttDropPosition.Child
        };
    }

    internal void OnDragLeave()
    {
        _dropTargetId = null;
        _dropPosition = GanttDropPosition.None;
    }

    internal async Task OnDropAsync(TmWorkItem targetTask)
    {
        if (string.IsNullOrEmpty(_draggedTaskId) || _draggedTaskId == targetTask.Id || _dropPosition == GanttDropPosition.None)
        {
            ClearDragState();
            return;
        }

        var args = new GanttTaskReorderedArgs
        {
            TaskId = _draggedTaskId,
            TargetTaskId = targetTask.Id,
            Position = _dropPosition
        };

        await OnTaskReordered.InvokeAsync(args);
        ClearDragState();
    }

    private void ClearDragState()
    {
        _draggedTaskId = null;
        _dropTargetId = null;
        _dropPosition = GanttDropPosition.None;
    }

    // ── Board Kanban ─────────────────────────────────────────────
    // D&D moves card between columns → changes Status

    private async Task OnBoardItemMovedAsync(KanbanMoveEvent<TmWorkItem> e)
    {
        if (!Enum.TryParse<TmWorkItemStatus>(e.ToColumn, out var newStatus)) return;
        var updated = new TmWorkItem
        {
            Id = e.Item.Id, Title = e.Item.Title, Start = e.Item.Start, End = e.Item.End,
            PercentComplete = e.Item.PercentComplete, IsMilestone = e.Item.IsMilestone,
            ParentId = e.Item.ParentId, DueDate = e.Item.DueDate,
            EstimationHours = e.Item.EstimationHours, LoggedHours = e.Item.LoggedHours,
            Color = e.Item.Color, Description = e.Item.Description,
            BudgetHours = e.Item.BudgetHours, ActualCost = e.Item.ActualCost,
            Status = newStatus,
            Priority = e.Item.Priority, Assignees = e.Item.Assignees,
            Tags = e.Item.Tags.ToList(),
            CustomFields = e.Item.CustomFields, Attachments = e.Item.Attachments,
            Comments = e.Item.Comments, TimeLog = e.Item.TimeLog,
            UseManualDates = e.Item.UseManualDates,
        };
        await RaiseTaskUpdatedAsync(updated);
    }

    // ── Calendar (TmScheduler) ────────────────────────────────────
    // D&D / resize moves event → changes Start + End of the task
    // Click on event → opens task panel

    private TmScheduleViewType _calendarView = TmScheduleViewType.Month;

    // OnInitialized pins this to the user-zone today (DI is available there); the zone event
    // re-pins it when the browser probe reports.
    private DateTime _calendarDate = default;

    private async Task OnCalendarEventChangedAsync(TmScheduleEvent e)
    {
        var task = Data.FirstOrDefault(t => t.Id == e.Id);
        if (task is null) return;
        var updated = new TmWorkItem
        {
            Id = task.Id, Title = task.Title,
            Start = e.StartLocal, End = e.EndLocal,
            PercentComplete = task.PercentComplete, IsMilestone = task.IsMilestone,
            ParentId = task.ParentId, DueDate = task.DueDate,
            EstimationHours = task.EstimationHours, LoggedHours = task.LoggedHours,
            Color = task.Color, Description = task.Description,
            BudgetHours = task.BudgetHours, ActualCost = task.ActualCost,
            Status = task.Status, Priority = task.Priority, Assignees = task.Assignees,
            Tags = task.Tags.ToList(),
            CustomFields = task.CustomFields, Attachments = task.Attachments,
            Comments = task.Comments, TimeLog = task.TimeLog,
            UseManualDates = task.UseManualDates,
        };
        await RaiseTaskUpdatedAsync(updated);
    }

    private async Task OnCalendarEventClickAsync(TmScheduleEvent e)
    {
        var task = Data.FirstOrDefault(t => t.Id == e.Id);
        if (task is not null)
            await SelectTaskAsync(task);
    }

    // ── Inline Edit ──────────────────────────────────────────────

    internal void StartInlineEdit(string taskId, GanttColumnKey column, string currentValue)
    {
        _inlineEditTaskId = taskId;
        _inlineEditColumn = column;
        _inlineEditValue = currentValue;
    }

    internal async Task CommitInlineEditAsync()
    {
        if (_inlineEditTaskId is null || _inlineEditColumn is null) return;

        var task = Data.FirstOrDefault(t => t.Id == _inlineEditTaskId);
        if (task is not null && _inlineEditColumn == GanttColumnKey.Title)
        {
            task.Title = _inlineEditValue;
            DrawVersion++;
            await RaiseTaskUpdatedAsync(task);
        }

        CancelInlineEdit();
    }

    internal void StartCustomFieldInlineEdit(string taskId, string fieldId, string currentValue)
    {
        _inlineEditTaskId = taskId;
        _inlineEditColumn = null;
        _inlineEditCustomFieldId = fieldId;
        _inlineEditValue = currentValue;
    }

    private void CancelInlineEdit()
    {
        _inlineEditTaskId = null;
        _inlineEditColumn = null;
        _inlineEditCustomFieldId = null;
        _inlineEditValue = string.Empty;
    }

    internal async Task HandleInlineEditKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            if (_inlineEditCustomFieldId is not null) await CommitCustomFieldEditAsync();
            else await CommitInlineEditAsync();
        }
        else if (e.Key == "Escape") CancelInlineEdit();
    }

    // ── Dependency Drag ──────────────────────────────────────────

    public void StartDepDrag(string taskId, bool fromEnd)
    {
        _depDragFromId = taskId;
        _depDragFromEnd = fromEnd;
    }

    public async void OnDepDrop(string targetId) => await CommitDependencyAsync(targetId);

    private async Task CommitDependencyAsync(string targetId)
    {
        if (_depDragFromId is null || _depDragFromId == targetId)
        {
            _depDragFromId = null;
            return;
        }

        var dep = new GanttDependency
        {
            Id = Guid.NewGuid().ToString(),
            FromId = _depDragFromId,
            ToId = targetId,
            DepType = _depDragFromEnd ? GanttDependencyType.FinishToStart : GanttDependencyType.StartToStart
        };

        _depDragFromId = null;
        await OnDependencyAdded.InvokeAsync(dep);
    }

    public void SelectDependency(string depId)
    {
        _selectedDependencyId = _selectedDependencyId == depId ? null : depId;
    }

    // ── Bulk Select ──────────────────────────────────────────────

    internal void ToggleBulkSelect(string taskId, bool selected)
    {
        if (selected) _selectedTaskIds.Add(taskId);
        else _selectedTaskIds.Remove(taskId);
    }

    internal async Task ExecuteBulkStatusUpdateAsync(TmWorkItemStatus status)
    {
        if (_selectedTaskIds.Count == 0) return;

        var args = new BulkUpdateArgs
        {
            TaskIds = _selectedTaskIds.ToList(),
            Status = status
        };

        await OnBulkUpdate.InvokeAsync(args);
        _selectedTaskIds = [];
    }

    // ── Cascade Sort ─────────────────────────────────────────────

    internal async Task CascadeSortAsync()
    {
        _treeRoots = GanttHelper.CascadeSort(_treeRoots);
        var flatSorted = GanttHelper.FlattenVisible(_treeRoots).Select(n => n.Task).ToList();
        RefreshVisibleData();
        await OnDataSorted.InvokeAsync(flatSorted);
    }

    // ── Column Toggle ─────────────────────────────────────────────

    internal async Task ToggleColumnVisibilityAsync(GanttColumnKey key, bool visible)
    {
        var updated = EffectiveColumns.Select(c => c.Key == key
            ? new GanttColumnDefinition { Key = c.Key, Visible = visible, Width = c.Width, Order = c.Order }
            : c).ToList();

        Columns = updated;
        await OnColumnsChanged.InvokeAsync(updated);
    }

    /// <summary>The columns the grid can render (the rest of <see cref="GanttColumnKey"/> is accepted and ignored).</summary>
    internal static readonly IReadOnlySet<GanttColumnKey> RenderedColumns = new HashSet<GanttColumnKey>
    {
        GanttColumnKey.WBS, GanttColumnKey.Title, GanttColumnKey.Status, GanttColumnKey.Start, GanttColumnKey.End,
        GanttColumnKey.Progress, GanttColumnKey.Duration, GanttColumnKey.Priority,
    };

    /// <summary>The entries of the column menu: every rendered column of <see cref="Columns"/> in order; the title column is always shown.</summary>
    internal IReadOnlyList<ColumnVisibilityItem> ColumnMenuItems() => EffectiveColumns
        .Where(c => RenderedColumns.Contains(c.Key))
        .Select(c => new ColumnVisibilityItem(c.Key.ToString(), ColumnTitle(c.Key), c.Visible, c.Key != GanttColumnKey.Title))
        .ToList();

    internal string ColumnTitle(GanttColumnKey key) => key switch
    {
        GanttColumnKey.WBS => Loc["TmGantt_WBS"],
        GanttColumnKey.Title => Loc["TmGantt_TaskTitle"],
        GanttColumnKey.Status => Loc["TmGantt_Status"],
        GanttColumnKey.Start => Loc["TmGantt_Start"],
        GanttColumnKey.End => Loc["TmGantt_End"],
        GanttColumnKey.Progress => Loc["TmGantt_TooltipProgress"],
        GanttColumnKey.Duration => Loc["TmGantt_Duration"],
        GanttColumnKey.Priority => Loc["TmGantt_Priority"],
        _ => key.ToString(),
    };

    /// <summary>Column menu: flip the visibility of one column (by its <see cref="GanttColumnKey"/> name).</summary>
    internal async Task ToggleColumnAsync(string key)
    {
        if (!Enum.TryParse<GanttColumnKey>(key, out var parsed)) return;
        var current = EffectiveColumns.FirstOrDefault(c => c.Key == parsed);
        if (current is null) return;
        await ToggleColumnVisibilityAsync(parsed, !current.Visible);
    }

    /// <summary>Column menu "Reset": the default columns visible, every other rendered column hidden.</summary>
    internal async Task ResetColumnsAsync()
    {
        var defaults = DefaultColumns.Where(c => c.Visible).Select(c => c.Key).ToHashSet();
        var updated = EffectiveColumns
            .Select(c => new GanttColumnDefinition { Key = c.Key, Visible = defaults.Contains(c.Key) || !RenderedColumns.Contains(c.Key) && c.Visible, Width = c.Width, Order = c.Order })
            .ToList();
        Columns = updated;
        await OnColumnsChanged.InvokeAsync(updated);
    }

    // ── Quick search (toolbar) ───────────────────────────────────

    internal string _quickSearchText = string.Empty;
    private string? _quickSearchApplied;

    private bool IsQuickSearchFilter(GanttFilter filter) =>
        _quickSearchApplied is not null && filter.Field == "Title" && filter.Operator == GanttFilterOperator.Contains && filter.Value == _quickSearchApplied;

    /// <summary>
    /// The toolbar search box: keeps exactly one <c>Title</c> / <c>Contains</c> entry in the filter set (the other
    /// filters stay), removes it when the box is emptied, and raises <see cref="OnFiltersChanged"/>.
    /// </summary>
    internal async Task SetQuickSearchAsync(string? text)
    {
        _quickSearchText = text ?? string.Empty;
        var term = _quickSearchText.Trim();
        var filters = _activeFilters.Where(f => !IsQuickSearchFilter(f)).ToList();
        _quickSearchApplied = term.Length > 0 ? term : null;
        if (term.Length > 0) filters.Add(new GanttFilter("Title", GanttFilterOperator.Contains, term));
        await ApplyFiltersAsync(filters, fromQuickSearch: true);
    }

    /// <summary>The search box no longer describes the filter set once its Title/Contains entry is gone.</summary>
    private void SyncQuickSearchWithFilters()
    {
        if (_quickSearchApplied is not null && !_activeFilters.Any(IsQuickSearchFilter))
        {
            _quickSearchApplied = null;
            _quickSearchText = string.Empty;
        }
    }

    internal int ActiveFilterCount => _activeFilters.Count;

    /// <summary>
    /// True when the filter editor renders as a dialog. <see cref="GanttFilterPanelMode.Auto"/> is a dialog on a tablet or
    /// narrower container and an inline strip on a desktop one.
    /// </summary>
    internal bool FilterPanelAsDialog => FilterPanelMode == GanttFilterPanelMode.Dialog
        || (FilterPanelMode == GanttFilterPanelMode.Auto && _layout != TmLayoutMode.Desktop);

    /// <summary>The active filters as chips: each carries the filter it was built from, so removing a chip removes exactly that rule.</summary>
    internal IEnumerable<(GanttFilter Source, ActiveFilter Chip)> FilterChips() =>
        _activeFilters.Select(filter => (filter, new ActiveFilter(
            filter.Field,
            FilterFieldLabel(filter.Field),
            ChipOperator(filter.Operator),
            filter.Value,
            FilterChipText(filter))));

    private static FilterOperator ChipOperator(GanttFilterOperator op) => op switch
    {
        GanttFilterOperator.Equals => FilterOperator.Equals,
        GanttFilterOperator.NotEquals => FilterOperator.NotEquals,
        GanttFilterOperator.Before => FilterOperator.LessThan,
        GanttFilterOperator.After => FilterOperator.GreaterThan,
        GanttFilterOperator.IsEmpty => FilterOperator.IsEmpty,
        GanttFilterOperator.IsNotEmpty => FilterOperator.IsNotEmpty,
        _ => FilterOperator.Contains,
    };

    private string FilterFieldLabel(string field) => field switch
    {
        "Title" => Loc["TmGantt_FilterFieldTitle"],
        "Status" => Loc["TmGantt_FilterFieldStatus"],
        "Priority" => Loc["TmGantt_FilterFieldPriority"],
        "Start" => Loc["TmGantt_Start"],
        "End" => Loc["TmGantt_End"],
        "Deadline" => Loc["TmGanttTaskPanel_Deadline"],
        _ => field.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) ? field["custom:".Length..] : field,
    };

    /// <summary>"In Progress" for an equals rule, otherwise "is after 6/5/2024" / "contains ui" / "is empty".</summary>
    private string FilterChipText(GanttFilter filter)
    {
        var value = filter.Field switch
        {
            "Status" when Enum.TryParse<TmWorkItemStatus>(filter.Value, true, out var status) => Loc[$"GanttStatus_{status}"],
            "Priority" when Enum.TryParse<TmWorkItemPriority>(filter.Value, true, out var priority) => Loc[$"GanttPriority_{priority}"],
            "Start" or "End" or "Deadline" when DateTime.TryParseExact(filter.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                => date.ToString("d", Loc.Culture),
            _ => filter.Value ?? string.Empty,
        };

        return filter.Operator switch
        {
            GanttFilterOperator.Equals => value,
            GanttFilterOperator.IsEmpty or GanttFilterOperator.IsNotEmpty => GanttLabels.FilterOperator(Loc, filter.Operator),
            _ => $"{GanttLabels.FilterOperator(Loc, filter.Operator)} {value}",
        };
    }

    internal List<GanttFilter> ActiveFiltersSnapshot() => _activeFilters.ToList();

    internal Task RemoveFilterChipAsync(GanttFilter filter) =>
        ApplyFiltersAsync(_activeFilters.Where(f => !ReferenceEquals(f, filter)).ToList());

    // ── Phase 3 Methods ──────────────────────────────────────────

    public async Task SaveBaselineAsync(string name)
    {
        var baseline = new GanttBaseline
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            CreatedAt = DateTime.UtcNow,
            Tasks = Data.Select(t => new GanttBaselineTask(t.Id, t.Start, t.End)).ToList()
        };
        await OnBaselineSaved.InvokeAsync(baseline);
    }

    public Task ApplyFiltersAsync(IEnumerable<GanttFilter> filters) => ApplyFiltersAsync(filters, fromQuickSearch: false);

    private async Task ApplyFiltersAsync(IEnumerable<GanttFilter> filters, bool fromQuickSearch)
    {
        _activeFilters = filters.ToList();
        if (!fromQuickSearch) SyncQuickSearchWithFilters();
        RefreshVisibleData();
        await OnFiltersChanged.InvokeAsync(_activeFilters);
    }

    internal void ToggleFilterPanel()    => _filterPanelOpen    = !_filterPanelOpen;
    internal void OpenFilterPanel()      => _filterPanelOpen    = true;
    internal void CloseFilterPanel()     => _filterPanelOpen    = false;
    internal void ToggleExportDialog()   => _exportDialogOpen   = !_exportDialogOpen;
    internal void ToggleImportDialog()   => _importDialogOpen   = !_importDialogOpen;

    // The dialogs' OnClose is an explicit CLOSE (not a toggle): a confirming dialog raises its action callback
    // (which already closes it) and then OnClose again, so a toggle would reopen it.
    private void CloseExportDialog()     => _exportDialogOpen   = false;
    private void CloseImportDialog()     => _importDialogOpen   = false;
    internal void ToggleHistoryDrawer()
    {
        _historyDrawerOpen = !_historyDrawerOpen;
        // One right-hand surface at a time on a narrow container: the history drawer replaces the task panel / reports.
        if (_historyDrawerOpen && (IsNarrowLayout || PanelsAsSheets))
        {
            SetTaskPanelOpen(false);
            if (_activeSidebarPanel == GanttSidebarPanel.Reports) _activeSidebarPanel = null;
        }
    }

    private async Task HandleExportAsync(GanttExportOptions opts)
    {
        _exportDialogOpen = false;
        await OnExportRequested.InvokeAsync(opts);
    }

    private async Task HandleImportCompletedAsync(IReadOnlyList<TmWorkItem> tasks)
    {
        _importDialogOpen = false;
        await OnImportCompleted.InvokeAsync(tasks);
    }

    /// <summary>The host is told; the dialog stays open, because its error alert is the only place the user can read why nothing was imported.</summary>
    private async Task HandleImportErrorAsync(string error)
    {
        await OnImportError.InvokeAsync(error);
    }

    internal async Task CommitCustomFieldEditAsync()
    {
        if (_inlineEditTaskId is null || _inlineEditCustomFieldId is null) return;
        var taskId  = _inlineEditTaskId;
        var fieldId = _inlineEditCustomFieldId;
        var value   = _inlineEditValue;
        var task    = Data.FirstOrDefault(t => t.Id == taskId);
        if (task is not null)
        {
            task.CustomFields[fieldId] = value;
            DrawVersion++;
            await OnCustomFieldChanged.InvokeAsync((taskId, fieldId, value));
        }
        CancelInlineEdit();
    }

    // ── Panning ──────────────────────────────────────────────────
    // The pan (mouse only - a finger or a pen scrolls the timeline natively) and the finger gestures are pointer
    // sessions of the core pointer-drag.js, attached by gantt.js after the first render (see AttachGesturesAsync);
    // the edge auto-scroll belongs to the dependency drag, not to the pan.

    private async Task<double> GetTimelineScrollLeftAsync() =>
        await JSRuntime.InvokeAsync<double>("tmGantt.getScrollLeft", _timelineContainerRef);

    private async Task SetTimelineScrollLeftAsync(double value) =>
        await JSRuntime.InvokeVoidAsync("tmGantt.setScrollLeft", _timelineContainerRef, value);
    // ── Wheel ────────────────────────────────────────────────────

    internal async Task OnTimelineWheel(WheelEventArgs e)
    {
        if (e.ShiftKey)
        {
            var currentLeft = await GetTimelineScrollLeftAsync();
            await SetTimelineScrollLeftAsync(currentLeft + e.DeltaY);
        }
        else if (e.CtrlKey)
        {
            if (e.DeltaY < 0)
                await ZoomInAsync();
            else
                await ZoomOutAsync();
        }
    }

    // ── Scroll sync ──────────────────────────────────────────────

    internal async Task OnTreeScroll()
    {
        if (_isSyncingScroll) return;
        _isSyncingScroll = true;
        var top = await JSRuntime.InvokeAsync<double>("tmGantt.getScrollTop", _treeBodyRef);
        await JSRuntime.InvokeVoidAsync("tmGantt.setScrollTop", _timelineBodyRef, top);
        _isSyncingScroll = false;
    }

    internal async Task OnTimelineBodyScroll()
    {
        if (_isSyncingScroll) return;
        _isSyncingScroll = true;
        var top = await JSRuntime.InvokeAsync<double>("tmGantt.getScrollTop", _timelineBodyRef);
        await JSRuntime.InvokeVoidAsync("tmGantt.setScrollTop", _treeBodyRef, top);
        _isSyncingScroll = false;
    }

    // ── Rendering helpers ────────────────────────────────────────

    private void RefreshVisibleData()
    {
        DrawVersion++;
        // Apply user-defined filters to tree roots first
        var filteredRoots = _activeFilters.Count > 0
            ? GanttHelper.ApplyFilters(_treeRoots, _activeFilters)
            : _treeRoots;

        var allVisible = GanttHelper.FlattenVisible(filteredRoots).ToList();

        // Apply ShowClosedTasks filter
        _visibleNodes = ViewSettings.ShowClosedTasks
            ? allVisible
            : allVisible.Where(n => n.Task.Status != TmWorkItemStatus.Done && n.Task.Status != TmWorkItemStatus.Closed).ToList();

        var (timelineStart, timelineEnd) = GanttHelper.GetTimeRange(Data);
        var pixelPerDay = GetPixelPerDay();
        var totalDays = (timelineEnd - timelineStart).TotalDays;
        _totalTimelineWidth = Math.Max(1, totalDays * pixelPerDay);

        // One today per refresh: the marker and the overdue highlight must agree, and the zone
        // (plus the clock) is read once instead of per row.
        _today = TodayInZone();
        _todayOffset = GanttHelper.GetTodayOffset(timelineStart, pixelPerDay, _today);

        // Two-row headers via zoom preset
        var rows = GanttHelper.BuildTimelineHeaderRows(GetEffectiveZoomPreset(), timelineStart, timelineEnd, pixelPerDay);
        _upperHeaders = rows.Upper.ToList();
        _lowerHeaders = rows.Lower.ToList();
        _timelineHeaders = _lowerHeaders; // for grid lines

        // Current time offset (only meaningful in Hours view)
        if (GetEffectiveZoomPreset() == GanttZoomPreset.Hours)
        {
            var pixelPerHour = pixelPerDay / 24.0;
            var now = NowInZone();
            _currentTimeOffset = (timelineStart - timelineStart.Date).TotalHours * pixelPerHour
                + GanttHelper.GetCurrentTimeOffset(timelineStart.Date, pixelPerHour, now);
        }

        BuildTaskBars(timelineStart, timelineEnd, pixelPerDay);
        BuildDependencyLines();
        BuildNonWorkingRects(timelineStart, timelineEnd, pixelPerDay);

        // Compute overloaded assignees when workload panel is active
        if (ShowWorkloadPanel || WorkloadPlacement == GanttWorkloadPlacement.Docked)
        {
            var entries = WorkloadCalculator.Calculate(Data, WorkingSchedule);
            _overloadedAssigneeIds = new HashSet<string>(
                entries.Where(e => e.AllocatedHours > e.CapacityHours)
                       .Select(e => e.AssigneeId));
        }
        else
        {
            _overloadedAssigneeIds.Clear();
        }
    }

    internal double GetPixelPerDay()
    {
        var zoomFactor = _zoomLevel / 100.0;
        return GetEffectiveZoomPreset() switch
        {
            GanttZoomPreset.Hours    => DayWidth * zoomFactor * 3,
            GanttZoomPreset.Days     => DayWidth * zoomFactor,
            GanttZoomPreset.Weeks    => DayWidth * zoomFactor * 0.6,
            GanttZoomPreset.Months   => DayWidth * zoomFactor * 0.25,
            GanttZoomPreset.Quarters => DayWidth * zoomFactor * 0.12,
            GanttZoomPreset.Years    => DayWidth * zoomFactor * 0.04,
            _ => DayWidth * zoomFactor
        };
    }

    private void BuildTaskBars(DateTime timelineStart, DateTime timelineEnd, double pixelPerDay)
    {
        _taskBars = [];
        // Build a lookup of taskId → node for group detection
        var nodesByTaskId = _treeRoots
            .SelectMany(FlattenAll)
            .ToDictionary(n => n.Task.Id);

        for (int i = 0; i < _visibleNodes.Count; i++)
        {
            var node = _visibleNodes[i];
            var task = node.Task;

            var isGroup = node.Children.Count > 0;

            // Group task bar rollup: span children bounds unless UseManualDates=true
            var effectiveStart = task.Start;
            var effectiveEnd   = task.End;
            if (isGroup && !task.UseManualDates)
            {
                var childTasks = node.Children.Select(c => c.Task).ToList();
                var bounds = GanttHelper.CalculateGroupBounds(childTasks);
                if (bounds.HasValue)
                {
                    effectiveStart = bounds.Value.MinStart;
                    effectiveEnd   = bounds.Value.MaxEnd;
                }
            }

            var (left, width) = GanttHelper.CalculateBarPosition(
                effectiveStart, effectiveEnd, timelineStart, timelineEnd, _totalTimelineWidth);
            var isDeadlineExceeded = task.DueDate.HasValue && task.End > task.DueDate.Value;
            var barTop = i * RowHeight + (RowHeight - 20) / 2;

            double? baselineLeft  = null;
            double? baselineWidth = null;
            if (_baselinePositions.TryGetValue(task.Id, out var bp))
            {
                baselineLeft  = bp.Left;
                baselineWidth = bp.Width;
            }

            _taskBars.Add(new TaskBarInfo(
                task, left, Math.Max(4, width), barTop,
                task.Color, isGroup, isDeadlineExceeded,
                baselineLeft, baselineWidth));
        }
    }

    private static IEnumerable<GanttTaskNode> FlattenAll(GanttTaskNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in FlattenAll(child))
                yield return n;
    }

    private void BuildDependencyLines()
    {
        _visibleDependencies = [];
        var taskIndex = _visibleNodes.Select((n, i) => (n.Task.Id, Index: i)).ToDictionary(x => x.Id, x => x.Index);

        foreach (var dep in Dependencies)
        {
            if (!taskIndex.TryGetValue(dep.FromId, out var fromIdx) || !taskIndex.TryGetValue(dep.ToId, out var toIdx))
                continue;

            var fromBar = _taskBars[fromIdx];
            var toBar = _taskBars[toIdx];

            double x1, x2;
            switch (dep.DepType)
            {
                case GanttDependencyType.StartToStart:
                    x1 = fromBar.Left;
                    x2 = toBar.Left;
                    break;
                case GanttDependencyType.FinishToFinish:
                    x1 = fromBar.Left + fromBar.Width;
                    x2 = toBar.Left + toBar.Width;
                    break;
                case GanttDependencyType.StartToFinish:
                    x1 = fromBar.Left;
                    x2 = toBar.Left + toBar.Width;
                    break;
                default: // FinishToStart
                    x1 = fromBar.Left + fromBar.Width;
                    x2 = toBar.Left;
                    break;
            }

            var y1 = fromBar.Top + 10;
            var y2 = toBar.Top + 10;
            var midX = (x1 + x2) / 2;
            var midY = (y1 + y2) / 2;

            _visibleDependencies.Add(new DependencyLineInfo(
                dep.Id, x1, y1, x2, y2, midX, midY, dep.DepType, dep.LagDays));
        }
    }

    private void BuildNonWorkingRects(DateTime start, DateTime end, double pixelPerDay)
    {
        _nonWorkingRects = [];
        if (!ViewSettings.ShowDaysOff) return;

        foreach (var (offset, width) in GanttHelper.GetNonWorkingDayRects(start, end, pixelPerDay, WorkingSchedule))
            _nonWorkingRects.Add(new NonWorkingRect(offset, width));
    }

    // ── CSS helpers ──────────────────────────────────────────────

    /// <summary>
    /// The theme classes are an alias of <see cref="GetDataTheme"/>: they carry no palette of their own
    /// (the colours come from <c>tokens-dark.css</c> through <c>data-theme</c>) and stay only as a stable
    /// hook for host selectors and tests.
    /// </summary>
    private string GetThemeClass() => ViewSettings.Theme switch
    {
        GanttTheme.Dark => "tm-gantt--theme-dark",
        GanttTheme.Light => "tm-gantt--theme-light",
        _ => ""
    };

    /// <summary>
    /// The <c>data-theme</c> of the root: an explicit Light/Dark wins, Auto follows the global
    /// <see cref="ThemeService"/> (dark only - light is the default token set), and without either the
    /// attribute is omitted so the Gantt inherits whatever the host put on an ancestor.
    /// </summary>
    private string? GetDataTheme() => ViewSettings.Theme switch
    {
        GanttTheme.Dark => "dark",
        GanttTheme.Light => "light",
        _ => _themeService is { IsDark: true } ? "dark" : null
    };

    internal bool IsOverdue(TmWorkItem task) =>
        task.End < _today &&
        task.Status is not TmWorkItemStatus.Done and not TmWorkItemStatus.Closed;

    private static string F(double value) => value.ToString(CultureInfo.InvariantCulture);

    internal static string GetAvatarInitial(TmWorkItemAssignee a) =>
        string.IsNullOrEmpty(a.Name) ? "?" : a.Name[0].ToString().ToUpperInvariant();
    internal static string GetAvatarStyle(TmWorkItemAssignee a)
    {
        if (!string.IsNullOrEmpty(a.AvatarUrl))
            return $"background-image: url('{a.AvatarUrl}'); background-size: cover;";
        // Deterministic color from name hash
        var hue = Math.Abs(a.Name.GetHashCode()) % 360;
        return $"background-color: hsl({hue}, 60%, 50%);";
    }
}

// ── Enums & args ────────────────────────────────────────────────

public enum GanttView { Day, Week, Month, Workload, List, Calendar, Board, Dashboard, People }
public enum GanttInsertPosition { Above, Below, Child, End }

/// <summary>Where a requested DOM focus move lands: the task row in the grid or its bar in the timeline.</summary>
internal enum GanttFocusTarget { Row, Bar }

/// <summary>A request to move DOM focus; <c>Seq</c> makes two requests for the same element distinct.</summary>
internal readonly record struct GanttFocusRequest(GanttFocusTarget Target, string TaskId, int Seq);

public class GanttTaskInsertedArgs
{
    public TmWorkItem Task { get; set; } = new();
    public string? ReferenceTaskId { get; set; }
    public GanttInsertPosition Position { get; set; }
}

public enum GanttDropPosition { None, Before, After, Child }

public class GanttTaskReorderedArgs
{
    public string TaskId { get; set; } = string.Empty;
    public string TargetTaskId { get; set; } = string.Empty;
    public GanttDropPosition Position { get; set; }
}

// ── Internal view-model records ──────────────────────────────────

internal record TaskBarInfo(
    TmWorkItem Task,
    double Left,
    double Width,
    double Top,
    string? Color = null,
    bool IsGroup = false,
    bool IsDeadlineExceeded = false,
    double? BaselineLeft = null,
    double? BaselineWidth = null);

internal record DependencyLineInfo(
    string DependencyId,
    double X1,
    double Y1,
    double X2,
    double Y2,
    double MidX,
    double MidY,
    GanttDependencyType DepType = GanttDependencyType.FinishToStart,
    int LagDays = 0);

internal record NonWorkingRect(double Offset, double Width);
