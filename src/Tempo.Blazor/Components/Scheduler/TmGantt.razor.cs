using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Scheduler;

public partial class TmGantt : IDisposable
{
    private ElementReference _timelineContainerRef;
    private ElementReference _timelineBodyRef;
    private ElementReference _treeBodyRef;
    private List<GanttTaskNode> _treeRoots = [];
    private List<GanttTaskNode> _visibleNodes = [];
    private List<TaskBarInfo> _taskBars = [];
    private List<DependencyLineInfo> _visibleDependencies = [];
    private List<TimelineHeader> _timelineHeaders = [];
    private List<TimelineHeader> _upperHeaders = [];
    private List<TimelineHeader> _lowerHeaders = [];
    private List<NonWorkingRect> _nonWorkingRects = [];
    private double _totalTimelineWidth;
    private int _zoomLevel = 100;
    private double _todayOffset;
    /// <summary>Today in the display zone, recomputed once per <see cref="RefreshVisibleData"/>.</summary>
    private DateTime _today = DateTime.Today;
    private double _currentTimeOffset;

    // Phase 3 state
    private HashSet<string> _criticalPathIds = [];
    private Dictionary<string, (double Left, double Width)> _baselinePositions = [];
    private bool _filterPanelOpen;
    private IReadOnlyList<GanttFilter> _activeFilters = [];

    // Phase 4 state
    private bool _exportDialogOpen;
    private bool _importDialogOpen;
    private bool _historyDrawerOpen;

    // Phase 5 state
    private HashSet<string> _overloadedAssigneeIds = [];
    private GanttSidebarPanel? _activeSidebarPanel;
    private IGanttRealtimeConnection? _subscribedConnection;


    // Inline edit state
    private string? _inlineEditTaskId;
    private GanttColumnKey? _inlineEditColumn;
    private string? _inlineEditCustomFieldId;
    private string _inlineEditValue = string.Empty;

    // Dependency drag state
    private string? _depDragFromId;
    private bool _depDragFromEnd;
    private string? _selectedDependencyId;

    // Bulk select state
    private HashSet<string> _selectedTaskIds = [];

    private static readonly IReadOnlyList<GanttColumnDefinition> DefaultColumns = new List<GanttColumnDefinition>
    {
        new() { Key = GanttColumnKey.WBS,      Visible = true,  Order = 0, Width = 50 },
        new() { Key = GanttColumnKey.Title,    Visible = true,  Order = 1 },
        new() { Key = GanttColumnKey.Status,   Visible = true,  Order = 2, Width = 90 },
        new() { Key = GanttColumnKey.Start,    Visible = true,  Order = 3, Width = 90 },
        new() { Key = GanttColumnKey.End,      Visible = true,  Order = 4, Width = 90 },
        new() { Key = GanttColumnKey.Progress, Visible = true,  Order = 5, Width = 80 },
    };

    private bool _isPanning;
    private double _panStartX;
    private double _panStartY;
    private double _panStartScrollLeft;
    private double _panStartScrollTop;
    private bool _isSyncingScroll;
    private bool _viewSettingsOpen;

    // Context menu state
    private bool _contextMenuVisible;
    private double _contextMenuX;
    private double _contextMenuY;
    private TmWorkItem? _contextMenuTask;

    // Drag & drop state
    private string? _draggedTaskId;
    private string? _dropTargetId;
    private GanttDropPosition _dropPosition;


    private const int ComfortableRowHeight = 40;
    private const int CompactRowHeight = 28;
    private const int DayWidth = 40;

    private int RowHeight => ViewSettings.ViewDensity == GanttViewDensity.Compact ? CompactRowHeight : ComfortableRowHeight;

    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    /// <summary>Service provider for the optional user-zone lookups (never a required dependency).</summary>
    [Inject] private IServiceProvider Services { get; set; } = default!;

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
    private IReadOnlyList<TmWorkItem> Data => WorkItemSource is null ? Items : _providerTasks;

    /// <summary>Effective dependency list: provider data when <see cref="WorkItemSource"/> is set, otherwise <see cref="DependencyItems"/>.</summary>
    private IReadOnlyList<GanttDependency> Dependencies => WorkItemSource is null ? DependencyItems : _providerDeps;
    [Parameter] public GanttView View { get; set; } = GanttView.Week;
    [Parameter] public EventCallback<GanttView> ViewChanged { get; set; }
    [Parameter] public TmWorkItem? SelectedTask { get; set; }
    [Parameter] public EventCallback<TmWorkItem> OnTaskSelected { get; set; }
    [Parameter] public bool ShowTaskPanel { get; set; }
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

    /// <summary>Report definitions shown in the Reports sidebar panel.</summary>
    [Parameter] public IReadOnlyList<GanttReport> Reports { get; set; } = [];

    /// <summary>Fires when the user clicks "Run" on a report.</summary>
    [Parameter] public EventCallback<GanttReport> OnReportRun { get; set; }

    [Parameter] public string? Class { get; set; }
    [Parameter] public double? ContainerWidth { get; set; }
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
    /// context). Drives the today marker, new-task defaults, the overdue highlight and the
    /// embedded calendar.
    /// </summary>
    internal DateTime TodayInZone()
    {
        var zone = _userZone?.Zone;
        return zone is null || zone.Equals(TimeZoneInfo.Local)
            ? DateTime.Today
            : TimeZoneInfo.ConvertTime(DateTimeOffset.Now, zone).Date;
    }

    protected override void OnInitialized()
    {
        // DI is available here: pin the zone service and the calendar start in one place. The zone
        // event (RefreshAfterZoneChange) re-pins both once the browser probe reports.
        _userZone = Services.GetService<ITmUserTimeZone>();
        _calendarDate = TodayInZone();
    }

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
    }

    private Task RefreshAfterZoneChange() => InvokeAsync(() =>
    {
        _calendarDate = TodayInZone();
        RefreshVisibleData();
        StateHasChanged();
    });

    protected override void OnParametersSet()
    {
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
        _activeFilters = Filters;

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

    public void Dispose()
    {
        _userZoneObserver?.Dispose();
        if (_subscribedConnection is not null)
        {
            _subscribedConnection.OnTaskUpdated -= HandleRealtimeTaskUpdated;
            _subscribedConnection = null;
        }
    }

    // ── View & Zoom ──────────────────────────────────────────────

    private GanttZoomPreset _zoomPresetOverride = GanttZoomPreset.Weeks;
    private bool _zoomPresetOverrideSet;

    private GanttZoomPreset GetEffectiveZoomPreset() =>
        _zoomPresetOverrideSet ? _zoomPresetOverride : ZoomPreset;

    private async Task SetViewAsync(GanttView view)
    {
        View = view;
        if (view == GanttView.Day)   { _zoomPresetOverride = GanttZoomPreset.Days;   _zoomPresetOverrideSet = true; }
        if (view == GanttView.Week)  { _zoomPresetOverride = GanttZoomPreset.Weeks;  _zoomPresetOverrideSet = true; }
        if (view == GanttView.Month) { _zoomPresetOverride = GanttZoomPreset.Months; _zoomPresetOverrideSet = true; }
        await ViewChanged.InvokeAsync(view);
        RefreshVisibleData();
    }

    private async Task ZoomInAsync()
    {
        _zoomLevel = Math.Min(200, _zoomLevel + 25);
        RefreshVisibleData();
    }

    private async Task ZoomOutAsync()
    {
        _zoomLevel = Math.Max(50, _zoomLevel - 25);
        RefreshVisibleData();
    }

    private async Task FitToScreenAsync()
    {
        var containerWidth = await JSRuntime.InvokeAsync<double>("eval", "document.querySelector('.tm-gantt__timeline')?.clientWidth || 0");
        await ApplyFitToScreenAsync(containerWidth);
    }

    internal async Task ApplyFitToScreenAsync(double containerWidth)
    {
        if (containerWidth <= 0 || _totalTimelineWidth <= 0) return;
        var newZoom = (containerWidth / _totalTimelineWidth) * 100.0;
        _zoomLevel = (int)Math.Max(50, Math.Min(200, newZoom));
        RefreshVisibleData();
        StateHasChanged();
    }

    // ── View Settings ────────────────────────────────────────────

    private void ToggleViewSettings() => _viewSettingsOpen = !_viewSettingsOpen;

    private void ToggleSidebarPanel(GanttSidebarPanel panel)
        => _activeSidebarPanel = _activeSidebarPanel == panel ? null : panel;

    private void UpdateNotification(Action<TmNotificationPreferences> apply)
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

    private async Task SetShowTodayMarker(bool value)
    {
        ViewSettings.ShowTodayMarker = value;
        RefreshVisibleData();
        await OnViewSettingsChanged.InvokeAsync(ViewSettings);
    }

    private async Task SetShowDaysOff(bool value)
    {
        ViewSettings.ShowDaysOff = value;
        RefreshVisibleData();
        await OnViewSettingsChanged.InvokeAsync(ViewSettings);
    }

    private async Task SetShowClosedTasks(bool value)
    {
        ViewSettings.ShowClosedTasks = value;
        RefreshVisibleData();
        await OnViewSettingsChanged.InvokeAsync(ViewSettings);
    }

    private async Task SetTaskNameLocation(GanttTaskNameLocation loc)
    {
        ViewSettings.TaskNameLocation = loc;
        RefreshVisibleData();
        await OnViewSettingsChanged.InvokeAsync(ViewSettings);
    }

    private async Task SetViewDensity(GanttViewDensity density)
    {
        ViewSettings.ViewDensity = density;
        RefreshVisibleData();
        await OnViewSettingsChanged.InvokeAsync(ViewSettings);
    }

    private async Task SetTheme(GanttTheme theme)
    {
        ViewSettings.Theme = theme;
        await OnViewSettingsChanged.InvokeAsync(ViewSettings);
    }

    // ── Interaction ──────────────────────────────────────────────

    private async Task ToggleExpandAsync(TmWorkItem task)
    {
        task.IsExpanded = !task.IsExpanded;
        RefreshVisibleData();
    }

    private async Task SelectTaskAsync(TmWorkItem task)
    {
        SelectedTask = task;
        await OnTaskSelected.InvokeAsync(task);
    }

    private async Task ExpandAllAsync()
    {
        GanttHelper.SetAllExpanded(_treeRoots, true);
        RefreshVisibleData();
    }

    private async Task CollapseAllAsync()
    {
        GanttHelper.SetAllExpanded(_treeRoots, false);
        RefreshVisibleData();
    }

    private async Task AddTaskAsync()
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

    private void ShowContextMenu(MouseEventArgs e, TmWorkItem task)
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

    private async Task InsertTaskAsync(GanttInsertPosition position)
    {
        if (_contextMenuTask is null) return;

        var today = TodayInZone();
        var newTask = new TmWorkItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = Loc["TmGantt_NewTaskTitle"],
            Start = today,
            End = today.AddDays(1),
            PercentComplete = 0,
            ParentId = position == GanttInsertPosition.Child ? _contextMenuTask.Id : _contextMenuTask.ParentId
        };

        var args = new GanttTaskInsertedArgs
        {
            Task = newTask,
            ReferenceTaskId = _contextMenuTask.Id,
            Position = position
        };

        await OnTaskInserted.InvokeAsync(args);
        await RaiseTaskAddedAsync(newTask);
        HideContextMenu();
        await SelectTaskAsync(newTask);
    }

    private async Task DeleteTaskAsync()
    {
        if (_contextMenuTask is null) return;
        var removedId = _contextMenuTask.Id;
        await RaiseTaskRemovedAsync(removedId);
        HideContextMenu();

        if (SelectedTask?.Id == removedId)
        {
            SelectedTask = null;
            await OnTaskSelected.InvokeAsync(null!);
        }
    }

    // ── Drag & Drop ──────────────────────────────────────────────

    private void OnDragStart(TmWorkItem task)
    {
        _draggedTaskId = task.Id;
        _dropTargetId = null;
        _dropPosition = GanttDropPosition.None;
    }

    private void OnDragOver(TmWorkItem targetTask, DragEventArgs e)
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

    private void OnDragLeave()
    {
        _dropTargetId = null;
        _dropPosition = GanttDropPosition.None;
    }

    private async Task OnDropAsync(TmWorkItem targetTask)
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
    private DateTime _calendarDate = DateTime.Today;

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

    private void StartInlineEdit(string taskId, GanttColumnKey column, string currentValue)
    {
        _inlineEditTaskId = taskId;
        _inlineEditColumn = column;
        _inlineEditValue = currentValue;
    }

    private async Task CommitInlineEditAsync()
    {
        if (_inlineEditTaskId is null || _inlineEditColumn is null) return;

        var task = Data.FirstOrDefault(t => t.Id == _inlineEditTaskId);
        if (task is not null && _inlineEditColumn == GanttColumnKey.Title)
        {
            task.Title = _inlineEditValue;
            await RaiseTaskUpdatedAsync(task);
        }

        CancelInlineEdit();
    }

    private void StartCustomFieldInlineEdit(string taskId, string fieldId, string currentValue)
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

    private async Task HandleInlineEditKeyDownAsync(KeyboardEventArgs e)
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

    public async void OnDepDrop(string targetId)
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

    private void ToggleBulkSelect(string taskId, bool selected)
    {
        if (selected) _selectedTaskIds.Add(taskId);
        else _selectedTaskIds.Remove(taskId);
    }

    private async Task ExecuteBulkStatusUpdateAsync(TmWorkItemStatus status)
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

    private async Task CascadeSortAsync()
    {
        _treeRoots = GanttHelper.CascadeSort(_treeRoots);
        var flatSorted = GanttHelper.FlattenVisible(_treeRoots).Select(n => n.Task).ToList();
        RefreshVisibleData();
        await OnDataSorted.InvokeAsync(flatSorted);
    }

    // ── Column Toggle ─────────────────────────────────────────────

    private async Task ToggleColumnVisibilityAsync(GanttColumnKey key, bool visible)
    {
        var updated = EffectiveColumns.Select(c => c.Key == key
            ? new GanttColumnDefinition { Key = c.Key, Visible = visible, Width = c.Width, Order = c.Order }
            : c).ToList();

        Columns = updated;
        await OnColumnsChanged.InvokeAsync(updated);
    }

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

    public async Task ApplyFiltersAsync(IEnumerable<GanttFilter> filters)
    {
        _activeFilters = filters.ToList();
        RefreshVisibleData();
        await OnFiltersChanged.InvokeAsync(_activeFilters);
    }

    private void ToggleFilterPanel()    => _filterPanelOpen    = !_filterPanelOpen;
    private void ToggleExportDialog()   => _exportDialogOpen   = !_exportDialogOpen;
    private void ToggleImportDialog()   => _importDialogOpen   = !_importDialogOpen;
    private void ToggleHistoryDrawer()  => _historyDrawerOpen  = !_historyDrawerOpen;

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

    private async Task HandleImportErrorAsync(string error)
    {
        _importDialogOpen = false;
        await OnImportError.InvokeAsync(error);
    }

    private async Task CommitCustomFieldEditAsync()
    {
        if (_inlineEditTaskId is null || _inlineEditCustomFieldId is null) return;
        var taskId  = _inlineEditTaskId;
        var fieldId = _inlineEditCustomFieldId;
        var value   = _inlineEditValue;
        var task    = Data.FirstOrDefault(t => t.Id == taskId);
        if (task is not null)
        {
            task.CustomFields[fieldId] = value;
            await OnCustomFieldChanged.InvokeAsync((taskId, fieldId, value));
        }
        CancelInlineEdit();
    }

    // ── Panning ──────────────────────────────────────────────────

    private async Task OnTimelineMouseDown(MouseEventArgs e)
    {
        if (e.Button != 0) return;
        _isPanning = true;
        _panStartX = e.ClientX;
        _panStartY = e.ClientY;
        _panStartScrollLeft = await GetTimelineScrollLeftAsync();
        _panStartScrollTop = await GetTimelineBodyScrollTopAsync();
    }

    private async Task OnTimelineMouseMove(MouseEventArgs e)
    {
        if (!_isPanning) return;
        var deltaX = _panStartX - e.ClientX;
        await SetTimelineScrollLeftAsync(_panStartScrollLeft + deltaX);
        var deltaY = _panStartY - e.ClientY;
        var newTop = _panStartScrollTop + deltaY;
        await SetTimelineBodyScrollTopAsync(newTop);
        await SetTreeBodyScrollTopAsync(newTop);
    }

    private void OnTimelineMouseUp(MouseEventArgs e) => _isPanning = false;

    private async Task<double> GetTimelineScrollLeftAsync() =>
        await JSRuntime.InvokeAsync<double>("tmGantt.getScrollLeft", _timelineContainerRef);

    private async Task SetTimelineScrollLeftAsync(double value) =>
        await JSRuntime.InvokeVoidAsync("tmGantt.setScrollLeft", _timelineContainerRef, value);

    private async Task<double> GetTimelineBodyScrollTopAsync() =>
        await JSRuntime.InvokeAsync<double>("tmGantt.getScrollTop", _timelineBodyRef);

    private async Task SetTimelineBodyScrollTopAsync(double value) =>
        await JSRuntime.InvokeVoidAsync("tmGantt.setScrollTop", _timelineBodyRef, value);

    private async Task SetTreeBodyScrollTopAsync(double value) =>
        await JSRuntime.InvokeVoidAsync("tmGantt.setScrollTop", _treeBodyRef, value);

    // ── Wheel ────────────────────────────────────────────────────

    private async Task OnTimelineWheel(WheelEventArgs e)
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

    private async Task OnTreeScroll()
    {
        if (_isSyncingScroll) return;
        _isSyncingScroll = true;
        var top = await JSRuntime.InvokeAsync<double>("tmGantt.getScrollTop", _treeBodyRef);
        await JSRuntime.InvokeVoidAsync("tmGantt.setScrollTop", _timelineBodyRef, top);
        _isSyncingScroll = false;
    }

    private async Task OnTimelineBodyScroll()
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
            _currentTimeOffset = (timelineStart - timelineStart.Date).TotalHours * pixelPerHour
                + GanttHelper.GetCurrentTimeOffset(timelineStart.Date, pixelPerHour);
        }

        BuildTaskBars(timelineStart, timelineEnd, pixelPerDay);
        BuildDependencyLines();
        BuildNonWorkingRects(timelineStart, timelineEnd, pixelPerDay);

        // Compute overloaded assignees when workload panel is active
        if (ShowWorkloadPanel)
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

    private double GetPixelPerDay()
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

    private string GetThemeClass() => ViewSettings.Theme switch
    {
        GanttTheme.Dark => "tm-gantt--theme-dark",
        GanttTheme.Light => "tm-gantt--theme-light",
        _ => ""
    };

    private string GetRowClass(GanttTaskNode node)
    {
        var classes = new List<string>();
        if (SelectedTask?.Id == node.Task.Id)
            classes.Add("tm-gantt__tree-row--selected");
        if (_draggedTaskId == node.Task.Id)
            classes.Add("tm-gantt__tree-row--dragging");
        if (ShowOverdueHighlight && IsOverdue(node.Task))
            classes.Add("tm-gantt__tree-row--overdue");
        return string.Join(" ", classes);
    }

    private bool IsOverdue(TmWorkItem task) =>
        task.End < _today &&
        task.Status is not TmWorkItemStatus.Done and not TmWorkItemStatus.Closed;

    private string GetDragClass(GanttTaskNode node)
    {
        if (_dropTargetId != node.Task.Id || _dropPosition == GanttDropPosition.None)
            return "";
        return _dropPosition switch
        {
            GanttDropPosition.Before => "tm-gantt__tree-row--drop-before",
            GanttDropPosition.After => "tm-gantt__tree-row--drop-after",
            GanttDropPosition.Child => "tm-gantt__tree-row--drop-child",
            _ => ""
        };
    }

    private string GetBarClass(TaskBarInfo bar)
    {
        var classes = new List<string>();

        if (SelectedTask?.Id == bar.Task.Id)
            classes.Add("tm-gantt__bar--selected");

        if (bar.IsGroup)
            classes.Add("tm-gantt__bar--group");

        if (bar.Task.Status is TmWorkItemStatus.Done or TmWorkItemStatus.Closed)
            classes.Add("tm-gantt__bar--completed");

        if (ShowOverdueHighlight && IsOverdue(bar.Task))
            classes.Add("tm-gantt__bar--overdue");

        if (ShowCriticalPath && _criticalPathIds.Contains(bar.Task.Id))
            classes.Add("tm-gantt__bar--critical-path");

        var priorityClass = bar.Task.Priority switch
        {
            TmWorkItemPriority.Highest => "tm-gantt__bar--priority-highest",
            TmWorkItemPriority.High => "tm-gantt__bar--priority-high",
            TmWorkItemPriority.Medium => "tm-gantt__bar--priority-medium",
            TmWorkItemPriority.Low => "tm-gantt__bar--priority-low",
            TmWorkItemPriority.Lowest => "tm-gantt__bar--priority-lowest",
            _ => ""
        };
        if (!string.IsNullOrEmpty(priorityClass))
            classes.Add(priorityClass);

        if (ActiveTimerTaskId == bar.Task.Id)
            classes.Add("tm-gantt__bar--timer-active");

        return string.Join(" ", classes);
    }

    private string GetArrowPoints(double x, double y) =>
        $"{F(x)},{F(y - 4)} {F(x + 6)},{F(y)} {F(x)},{F(y + 4)}";

    private static string F(double value) => value.ToString(CultureInfo.InvariantCulture);

    private bool ShouldRenderBarLabel(TaskBarInfo bar) =>
        ViewSettings.TaskNameLocation != GanttTaskNameLocation.Hidden && bar.Width > 60;

    private string GetBarColorStyle(TaskBarInfo bar)
    {
        var color = string.IsNullOrEmpty(bar.Color) ? "var(--tm-color-primary, #3b82f6)" : bar.Color;
        return $"--tm-gantt-task-color: {color};";
    }

    private string GetStatusBadgeClass(TmWorkItemStatus status) => status switch
    {
        TmWorkItemStatus.Open => "tm-gantt__status-badge--open",
        TmWorkItemStatus.InProgress => "tm-gantt__status-badge--inprogress",
        TmWorkItemStatus.Done => "tm-gantt__status-badge--done",
        TmWorkItemStatus.Closed => "tm-gantt__status-badge--closed",
        _ => ""
    };

    private string GetPriorityIconClass(TmWorkItemPriority priority) => priority switch
    {
        TmWorkItemPriority.Highest => "tm-gantt__priority-icon--highest",
        TmWorkItemPriority.High => "tm-gantt__priority-icon--high",
        TmWorkItemPriority.Medium => "tm-gantt__priority-icon--medium",
        TmWorkItemPriority.Low => "tm-gantt__priority-icon--low",
        TmWorkItemPriority.Lowest => "tm-gantt__priority-icon--lowest",
        _ => ""
    };

    private string GetDepLineClass(DependencyLineInfo dep)
    {
        var classes = new List<string> { "tm-gantt__dependency-line", "tm-gantt__dep-line" };
        if (_selectedDependencyId == dep.DependencyId)
            classes.Add("tm-gantt__dep-line--selected");
        classes.Add(dep.DepType switch
        {
            GanttDependencyType.StartToStart    => "tm-gantt__dep-line--ss",
            GanttDependencyType.FinishToFinish  => "tm-gantt__dep-line--ff",
            GanttDependencyType.StartToFinish   => "tm-gantt__dep-line--sf",
            _                                   => "tm-gantt__dep-line--fs"
        });
        return string.Join(" ", classes);
    }

    private string GetAvatarInitial(TmWorkItemAssignee a) =>
        string.IsNullOrEmpty(a.Name) ? "?" : a.Name[0].ToString().ToUpperInvariant();

    private string GetAvatarStyle(TmWorkItemAssignee a)
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
