using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.WorkItems;

namespace Tempo.Blazor.Components.NotionEditor.UI;

/// <summary>Aggregated task panel for Notion action items, backed by the unified work-item provider.</summary>
public partial class TmNotionMyTasks : ComponentBase
{
    /// <summary>Unified work-item source used to query and update tasks.</summary>
    [Parameter, EditorRequired] public ITmWorkItemProvider WorkItemSource { get; set; } = default!;

    /// <summary>Current user id used by the "my tasks" filter.</summary>
    [Parameter] public string? CurrentUserId { get; set; }

    /// <summary>Additional CSS class.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Raised when the panel close button is clicked.</summary>
    [Parameter] public EventCallback OnClosed { get; set; }

    /// <summary>Raised when a source task is selected for navigation. Args are page id and block id.</summary>
    [Parameter] public EventCallback<(string PageId, string BlockId)> OnNavigateToBlock { get; set; }

    private readonly List<TmWorkItem> _tasks = [];
    private readonly List<TmWorkItem> _visibleTasks = [];
    private readonly List<TaskGroup> _groups = [];
    private bool _isLoading;
    private string? _loadError;
    private TaskScope _scope = TaskScope.Mine;
    private TaskStatusFilter _status = TaskStatusFilter.Open;
    private TaskDueFilter _due = TaskDueFilter.All;
    private TaskGroupBy _groupBy = TaskGroupBy.DueDate;
    private bool _filtersSheetOpen;

    private int ActiveFilterCount =>
        (_scope == TaskScope.All ? 1 : 0)
        + (_status != TaskStatusFilter.Open ? 1 : 0)
        + (_due != TaskDueFilter.All ? 1 : 0)
        + (_groupBy != TaskGroupBy.DueDate ? 1 : 0);

    private Task OpenFiltersSheetAsync()
    {
        _filtersSheetOpen = true;
        return Task.CompletedTask;
    }

    private Task CloseFiltersSheet()
    {
        _filtersSheetOpen = false;
        return Task.CompletedTask;
    }

    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentUserId))
            _scope = TaskScope.All;

        await LoadTasksAsync();
    }

    private async Task LoadTasksAsync()
    {
        _isLoading = true;
        _loadError = null;
        StateHasChanged();

        try
        {
            var query = BuildQuery();
            var result = await WorkItemSource.SearchAsync(query);
            _tasks.Clear();
            _tasks.AddRange(result.Items);
            RebuildVisibleTasks();
        }
        catch
        {
            _loadError = Loc["Notion_Tasks_LoadError"];
            _tasks.Clear();
            _visibleTasks.Clear();
            _groups.Clear();
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private TmWorkItemQuery BuildQuery()
    {
        var today = DateTime.Today;
        return new TmWorkItemQuery
        {
            AssigneeId = _scope == TaskScope.Mine ? CurrentUserId : null,
            IncludeCompleted = _status != TaskStatusFilter.Open,
            DueBefore = _due == TaskDueFilter.Overdue ? today.AddDays(-1) : null,
            DueAfter = _due == TaskDueFilter.Upcoming ? today : null,
            Skip = 0,
            Take = 200
        };
    }

    private async Task SetScopeAsync(TaskScope scope)
    {
        if (scope == TaskScope.Mine && string.IsNullOrWhiteSpace(CurrentUserId)) return;
        if (_scope == scope) return;
        _scope = scope;
        await LoadTasksAsync();
    }

    private async Task SetStatusAsync(TaskStatusFilter status)
    {
        if (_status == status) return;
        _status = status;
        await LoadTasksAsync();
    }

    private async Task SetDueAsync(TaskDueFilter due)
    {
        if (_due == due) return;
        _due = due;
        await LoadTasksAsync();
    }

    private void SetGroupBy(TaskGroupBy groupBy)
    {
        if (_groupBy == groupBy) return;
        _groupBy = groupBy;
        RebuildVisibleTasks();
    }

    private async Task ToggleTaskAsync(TmWorkItem task)
    {
        var completed = !task.IsCompleted;
        await WorkItemSource.SetCompletedAsync(task.Id, completed);
        task.IsCompleted = completed;

        if (_status == TaskStatusFilter.Open && completed)
            _tasks.Remove(task);
        else if (_status == TaskStatusFilter.Completed && !completed)
            _tasks.Remove(task);

        RebuildVisibleTasks();
    }

    private Task NavigateAsync(TmWorkItem task)
        => OnNavigateToBlock.InvokeAsync((task.OriginPageId ?? string.Empty, task.OriginBlockId ?? string.Empty));

    private void RebuildVisibleTasks()
    {
        _visibleTasks.Clear();
        _visibleTasks.AddRange(_tasks.Where(MatchesClientFilters));
        _groups.Clear();
        _groups.AddRange(BuildGroups(_visibleTasks));
    }

    private bool MatchesClientFilters(TmWorkItem task)
        => _status switch
        {
            TaskStatusFilter.Open => !task.IsCompleted,
            TaskStatusFilter.Completed => task.IsCompleted,
            _ => true
        };

    private IEnumerable<TaskGroup> BuildGroups(IReadOnlyList<TmWorkItem> tasks)
    {
        if (_groupBy == TaskGroupBy.Page)
        {
            return tasks
                .GroupBy(task => PageTitle(task))
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new TaskGroup(group.Key, group.Key, group.ToList()));
        }

        return tasks
            .GroupBy(GetDueGroupKey)
            .OrderBy(group => DueGroupOrder(group.Key))
            .Select(group => new TaskGroup(group.Key, GetDueGroupTitle(group.Key), group.ToList()));
    }

    private static string GetDueGroupKey(TmWorkItem task)
    {
        if (task.DueDate is null) return "none";
        var date = task.DueDate.Value.Date;
        var today = DateTime.Today;
        if (date < today && !task.IsCompleted) return "overdue";
        if (date == today) return "today";
        if (date == today.AddDays(1)) return "tomorrow";
        return "later";
    }

    private string GetDueGroupTitle(string key) => key switch
    {
        "overdue" => Loc["Notion_Tasks_Group_Overdue"],
        "today" => Loc["Notion_Tasks_Group_Today"],
        "tomorrow" => Loc["Notion_Tasks_Group_Tomorrow"],
        "later" => Loc["Notion_Tasks_Group_Later"],
        _ => Loc["Notion_Tasks_Group_NoDueDate"]
    };

    private static int DueGroupOrder(string key) => key switch
    {
        "overdue" => 0,
        "today" => 1,
        "tomorrow" => 2,
        "later" => 3,
        _ => 4
    };

    private string FormatDueDate(DateTime date)
    {
        var today = DateTime.Today;
        if (date.Date == today) return Loc["Notion_Tasks_Due_Today"];
        if (date.Date == today.AddDays(1)) return Loc["Notion_Tasks_Due_Tomorrow"];
        if (date.Date < today) return $"{Loc["Notion_Tasks_Due_Overdue"]}: {date:d}";
        return date.ToString("d");
    }

    private static string PageTitle(TmWorkItem task)
        => string.IsNullOrWhiteSpace(task.OriginPageTitle) ? (task.OriginPageId ?? string.Empty) : task.OriginPageTitle;

    private static string? AssigneeName(TmWorkItem task)
        => task.Assignees.FirstOrDefault()?.Name;

    private static string? AssigneeAvatar(TmWorkItem task)
        => task.Assignees.FirstOrDefault()?.AvatarUrl;

    private readonly HashSet<string> _collapsedGroups = new(StringComparer.Ordinal);

    private bool IsGroupCollapsed(string key) => _collapsedGroups.Contains(key);

    private void ToggleGroup(string key)
    {
        if (!_collapsedGroups.Remove(key))
            _collapsedGroups.Add(key);
        StateHasChanged();
    }

    private static string FilterClass(bool active)
        => active ? "tm-my-tasks__filter tm-my-tasks__filter--active" : "tm-my-tasks__filter";

    private static string TaskClass(TmWorkItem task)
        => task.IsCompleted ? "tm-my-tasks__item tm-my-tasks__item--completed" : "tm-my-tasks__item";

    private static string DueClass(TmWorkItem task)
        => task.DueDate is DateTime dueDate && dueDate.Date < DateTime.Today && !task.IsCompleted
            ? "tm-my-tasks__due tm-my-tasks__due--overdue"
            : "tm-my-tasks__due";

    private enum TaskScope { Mine, All }
    private enum TaskStatusFilter { Open, Completed, All }
    private enum TaskDueFilter { All, Overdue, Upcoming }
    private enum TaskGroupBy { DueDate, Page }

    private sealed record TaskGroup(string Key, string Title, IReadOnlyList<TmWorkItem> Tasks);
}
