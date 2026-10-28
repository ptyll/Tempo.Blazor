using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.Avatars;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// A task editor panel for the Gantt chart. Allows editing selected task properties and dependencies.
/// </summary>
public partial class TmGanttTaskPanel
{
    private string _editTitle = string.Empty;
    private string _editStart = string.Empty;
    private string _editEnd = string.Empty;
    private int? _editPercent;
    private bool _editIsMilestone;
    private string? _editParentId;
    private string? _editDeadline;
    private double? _editEstimation;
    private double? _editLoggedHours;
    private string? _editColor;
    private string? _editDescription;
    private double? _editBudgetHours;
    private decimal? _editActualCost;
    private string? _validationError;
    private string? _dependencyError;
    private string _newCommentText = string.Empty;
    private List<SelectOption<string?>> _parentOptions = [];
    private string? _currentTaskId;
    private TmWorkItemStatus _editStatus;
    private TmWorkItemPriority _editPriority;

    // The task values the edit fields were last synchronised with: a change made OUTSIDE the panel (the board,
    // "Mark done", a realtime update) re-syncs the field instead of leaving a stale copy behind.
    private TmWorkItemStatus _seenStatus;
    private int _seenPercent;

    // Timer state
    private DateTime? _timerStartedAt;

    private static readonly string[] ColorSwatches = new[]
    {
        "#3b82f6", "#6366f1", "#8b5cf6", "#ec4899",
        "#ef4444", "#f97316", "#f59e0b", "#eab308",
        "#22c55e", "#10b981", "#14b8a6", "#06b6d4"
    };

    private List<GanttDependency> _taskDependencies = [];
    private bool _showAddDependency;
    private string? _newDepFromId;
    private int _newDepType;
    private List<SelectOption<string?>> _dependencyFromOptions = [];
    private string? _pendingRemoveDepId;
    private static readonly List<SelectOption<int>> _dependencyTypeOptions = new()
    {
        new(0, "Finish → Start"),
        new(1, "Start → Start"),
        new(2, "Finish → Finish"),
        new(3, "Start → Finish")
    };

    /// <summary>The task being edited.</summary>
    [Parameter] public TmWorkItem? Task { get; set; }

    /// <summary>All tasks for parent selection.</summary>
    [Parameter] public IReadOnlyList<TmWorkItem> AllTasks { get; set; } = [];

    /// <summary>Existing dependencies.</summary>
    [Parameter] public IReadOnlyList<GanttDependency> Dependencies { get; set; } = [];

    /// <summary>Fires when the task is saved with valid data.</summary>
    [Parameter] public EventCallback<TmWorkItem> OnTaskUpdated { get; set; }

    /// <summary>Fires when a new dependency is added.</summary>
    [Parameter] public EventCallback<GanttDependency> OnDependencyAdded { get; set; }

    /// <summary>Fires when a dependency is removed (by its Id).</summary>
    [Parameter] public EventCallback<string> OnDependencyRemoved { get; set; }

    /// <summary>Fires when the description textarea changes (live).</summary>
    [Parameter] public EventCallback<string?> OnDescriptionChanged { get; set; }

    /// <summary>Fires when the user selects a file to attach.</summary>
    [Parameter] public EventCallback<IBrowserFile> OnAttachmentUpload { get; set; }

    /// <summary>Fires when the user removes an attachment (by attachment Id).</summary>
    [Parameter] public EventCallback<string> OnAttachmentRemoved { get; set; }

    /// <summary>Fires when the user submits a new comment.</summary>
    [Parameter] public EventCallback<TmCommentEntry> OnCommentAdded { get; set; }

    /// <summary>Fires when the user closes the panel via the close button.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>
    /// Fires on Save when the Status field was changed (arg = task id + the new status). Like the priority, the status
    /// is a draft until Save (Cancel reverts it); <see cref="TmGantt"/> applies it to the task and raises its own
    /// <c>OnStatusChanged</c>.
    /// </summary>
    [Parameter] public EventCallback<(string TaskId, TmWorkItemStatus NewStatus)> OnStatusChanged { get; set; }

    /// <summary>
    /// Fires when the user picks a command of the Actions section (mark done, add above / below / child, duplicate,
    /// delete). The section is rendered only when the host handles this callback; <see cref="TmGantt"/> wires it to the
    /// handlers of the task context menu.
    /// </summary>
    [Parameter] public EventCallback<GanttTaskAction> OnAction { get; set; }

    /// <summary>When true the Progress section states that one of the task's assignees is over capacity (read-only; <see cref="TmGantt"/> computes it).</summary>
    [Parameter] public bool IsOverloaded { get; set; }

    /// <summary>The task's entry in the active baseline. When set, the Progress section shows the start / end deviation from it (read-only).</summary>
    [Parameter] public GanttBaselineTask? BaselineTask { get; set; }

    /// <summary>Optional breadcrumb shown above the task title in the panel header (a host-drawn project / parent path).</summary>
    [Parameter] public RenderFragment? Breadcrumb { get; set; }

    /// <summary>Id of the task whose timer is currently running. Null if none.</summary>
    [Parameter] public string? ActiveTimerTaskId { get; set; }

    /// <summary>Fires when the user starts the timer for this task (arg = taskId).</summary>
    [Parameter] public EventCallback<string> OnTimerStarted { get; set; }

    /// <summary>Fires when the user stops the timer (arg = taskId + completed log entry).</summary>
    [Parameter] public EventCallback<(string TaskId, GanttTimeLogEntry Entry)> OnTimerStopped { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Task is not null && Task.Id != _currentTaskId)
        {
            _currentTaskId = Task.Id;
            _editTitle = Task.Title;
            _editStart = Task.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _editEnd = Task.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _editPercent = Task.PercentComplete;
            _editIsMilestone = Task.IsMilestone;
            _editParentId = Task.ParentId;
            _editDeadline = Task.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _editEstimation = Task.EstimationHours;
            _editLoggedHours = Task.LoggedHours;
            _editColor       = Task.Color;
            _editDescription = Task.Description;
            _editBudgetHours = Task.BudgetHours;
            _editActualCost  = Task.ActualCost;
            _editStatus      = Task.Status;
            _editPriority    = Task.Priority;
            _seenStatus      = Task.Status;
            _seenPercent     = Task.PercentComplete;
            _timerStartedAt  = null;
            _validationError = null;
            _showAddDependency = false;
            _newDepFromId = null;
            _newDepType = 0;
        }

        if (Task is not null && Task.Id == _currentTaskId)
        {
            if (Task.Status != _seenStatus)
            {
                _seenStatus = Task.Status;
                _editStatus = Task.Status;
            }

            if (Task.PercentComplete != _seenPercent)
            {
                _seenPercent = Task.PercentComplete;
                _editPercent = Task.PercentComplete;
            }
        }

        BuildStatusAndPriorityOptions();
        BuildParentOptions();
        BuildDependencyOptions();
        _taskDependencies = Dependencies.Where(d => d.ToId == Task?.Id).ToList();
    }

    private List<SelectOption<TmWorkItemStatus>> _statusOptions = [];
    private List<SelectOption<TmWorkItemPriority>> _priorityOptions = [];

    private void BuildStatusAndPriorityOptions()
    {
        _statusOptions = Enum.GetValues<TmWorkItemStatus>()
            .Select(s => new SelectOption<TmWorkItemStatus>(s, Loc[$"GanttStatus_{s}"]))
            .ToList();
        _priorityOptions = Enum.GetValues<TmWorkItemPriority>()
            .Select(p => new SelectOption<TmWorkItemPriority>(p, Loc[$"GanttPriority_{p}"]))
            .ToList();
    }

    // The status is a draft like the priority: Save raises OnStatusChanged (when it differs from the task), Cancel reverts.
    private void OnStatusSelected(TmWorkItemStatus status) => _editStatus = status;

    private Task RaiseActionAsync(GanttTaskAction action) => OnAction.InvokeAsync(action);

    // The decimal inputs bind decimal?; the model keeps hours as double? (and the cost as decimal?).
    private static decimal? Dec(double? value) => value.HasValue ? (decimal)value.Value : null;

    private static double? Dbl(decimal? value) => value.HasValue ? (double)value.Value : null;

    /// <summary>The read-only duration: whole days between the edited start and end, with the localized unit.</summary>
    private string DurationText
    {
        get
        {
            if (DateTime.TryParseExact(_editStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) &&
                DateTime.TryParseExact(_editEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                return $"{(int)(end - start).TotalDays} {Loc["TmGanttTaskPanel_Days"]}";
            }

            return string.Empty;
        }
    }

    private static string Signed(int days) => days.ToString("+0;-0;0", CultureInfo.InvariantCulture);

    private string BaselineStartText => Loc["TmGanttTaskPanel_BaselineStart", Signed((int)(Task!.Start.Date - BaselineTask!.Start.Date).TotalDays)];

    private string BaselineEndText => Loc["TmGanttTaskPanel_BaselineEnd", Signed((int)(Task!.End.Date - BaselineTask!.End.Date).TotalDays)];

    private static readonly AvatarColor[] AssigneeColors =
        [AvatarColor.Blue, AvatarColor.Purple, AvatarColor.Green, AvatarColor.Orange, AvatarColor.Pink, AvatarColor.Red];

    /// <summary>A stable colour per assignee (string.GetHashCode is randomised per process, so it is never used for a visible value).</summary>
    private AvatarColor AssigneeColor(int index)
    {
        var key = Task!.Assignees[index].Id;
        var sum = 0;
        foreach (var ch in key) sum += ch;
        return AssigneeColors[sum % AssigneeColors.Length];
    }

    /// <summary>The second line of an attachment card: size and upload date, each only when the attachment carries it.</summary>
    private string AttachmentMeta(TmAttachment attachment)
    {
        var parts = new List<string>(2);
        if (attachment.SizeBytes > 0) parts.Add(FormatSize(attachment.SizeBytes, Loc.Culture));
        if (attachment.UploadedAt != default) parts.Add(attachment.UploadedAt.ToString("d", Loc.Culture));
        return string.Join(" • ", parts);
    }

    private string FormatSize(long bytes, CultureInfo culture) => bytes switch
    {
        < 1024 => Loc["TmGanttTaskPanel_SizeBytes", bytes.ToString(culture)],
        < 1024 * 1024 => Loc["TmGanttTaskPanel_SizeKB", (bytes / 1024.0).ToString("F1", culture)],
        _ => Loc["TmGanttTaskPanel_SizeMB", (bytes / (1024.0 * 1024)).ToString("F1", culture)],
    };

    private void BuildParentOptions()
    {
        _parentOptions = AllTasks
            .Where(t => t.Id != Task?.Id)
            .Select(t => new SelectOption<string?>(t.Id, t.Title))
            .ToList();
    }

    private void BuildDependencyOptions()
    {
        _dependencyFromOptions = AllTasks
            .Where(t => t.Id != Task?.Id)
            .Select(t => new SelectOption<string?>(t.Id, t.Title))
            .ToList();
    }

    private string GetDependencyLabel(GanttDependency dep)
    {
        var fromTask = AllTasks.FirstOrDefault(t => t.Id == dep.FromId);
        var typeLabel = dep.Type switch
        {
            1 => "SS",
            2 => "FF",
            3 => "SF",
            _ => "FS"
        };
        return $"{fromTask?.Title ?? dep.FromId} ({typeLabel})";
    }

    private void PromptRemoveDependency(string id)
    {
        _pendingRemoveDepId = id;
    }

    private void CancelRemoveDependency()
    {
        _pendingRemoveDepId = null;
    }

    private async Task ConfirmRemoveDependencyAsync()
    {
        if (_pendingRemoveDepId is not null)
        {
            await OnDependencyRemoved.InvokeAsync(_pendingRemoveDepId);
            _pendingRemoveDepId = null;
        }
    }

    private async Task AddDependencyAsync()
    {
        if (Task is null || string.IsNullOrEmpty(_newDepFromId)) return;

        // Duplicate check
        if (Dependencies.Any(d => d.FromId == _newDepFromId && d.ToId == Task.Id && d.Type == _newDepType))
        {
            _dependencyError = Loc["TmGanttTaskPanel_ErrorDuplicateDependency"];
            StateHasChanged();
            return;
        }

        // Cycle detection
        if (WouldCreateCycle(_newDepFromId, Task.Id))
        {
            _dependencyError = Loc["TmGanttTaskPanel_ErrorCyclicDependency"];
            StateHasChanged();
            return;
        }

        _dependencyError = null;

        var dep = new GanttDependency
        {
            FromId = _newDepFromId,
            ToId = Task.Id,
            Type = _newDepType
        };

        await OnDependencyAdded.InvokeAsync(dep);
        _showAddDependency = false;
        _newDepFromId = null;
        _newDepType = 0;
    }

    /// <summary>
    /// Checks whether adding an edge fromId → toId would create a cycle in the dependency graph.
    /// </summary>
    private bool WouldCreateCycle(string fromId, string toId)
    {
        // A cycle would be created if there is already a path from toId back to fromId.
        var allDeps = Dependencies.ToList();
        allDeps.Add(new GanttDependency { FromId = fromId, ToId = toId });

        var visited = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(toId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == fromId)
                return true;

            if (visited.Add(current))
            {
                foreach (var dep in allDeps.Where(d => d.FromId == current))
                {
                    stack.Push(dep.ToId);
                }
            }
        }

        return false;
    }

    private void CancelAddDependency()
    {
        _showAddDependency = false;
        _newDepFromId = null;
        _newDepType = 0;
    }

    private async Task SaveAsync()
    {
        if (Task is null) return;
        if (!Validate()) return;

        DateTime? deadline = null;
        if (!string.IsNullOrEmpty(_editDeadline) &&
            DateTime.TryParseExact(_editDeadline, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dl))
            deadline = dl;

        var updated = new TmWorkItem
        {
            Id              = Task.Id,
            Title           = _editTitle,
            Start           = DateTime.ParseExact(_editStart, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            End             = DateTime.ParseExact(_editEnd,   "yyyy-MM-dd", CultureInfo.InvariantCulture),
            PercentComplete = _editPercent ?? 0,
            IsMilestone     = _editIsMilestone,
            ParentId        = _editParentId,
            DueDate         = deadline,
            EstimationHours = _editEstimation,
            LoggedHours     = _editLoggedHours,
            Color           = _editColor,
            Description     = _editDescription,
            BudgetHours     = _editBudgetHours,
            ActualCost      = _editActualCost,
            Status          = _editStatus,
            Priority        = _editPriority,
            Assignees       = Task.Assignees,
            Tags            = Task.Tags.ToList(),
            CustomFields    = Task.CustomFields,
            Attachments     = Task.Attachments,
            Comments        = Task.Comments,
            TimeLog         = Task.TimeLog,
            UseManualDates  = Task.UseManualDates,
        };

        var statusChanged = _editStatus != Task.Status;
        await OnTaskUpdated.InvokeAsync(updated);
        if (statusChanged)
        {
            await OnStatusChanged.InvokeAsync((updated.Id, updated.Status));
        }
    }

    private async Task CancelAsync()
    {
        if (Task is null) return;
        // Reset local fields back to task values
        _editTitle = Task.Title;
        _editStart = Task.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _editEnd = Task.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _editPercent = Task.PercentComplete;
        _editIsMilestone = Task.IsMilestone;
        _editParentId = Task.ParentId;
        _editDeadline = Task.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _editEstimation = Task.EstimationHours;
        _editLoggedHours = Task.LoggedHours;
        _editColor = Task.Color;
        _editDescription = Task.Description;
        _editBudgetHours = Task.BudgetHours;
        _editActualCost = Task.ActualCost;
        _editStatus = Task.Status;
        _editPriority = Task.Priority;
        _validationError = null;

        // Re-render to show original values
        StateHasChanged();
    }

    private async Task OnDescriptionInputAsync(ChangeEventArgs e)
    {
        _editDescription = e.Value?.ToString();
        await OnDescriptionChanged.InvokeAsync(_editDescription);
    }

    private async Task OnAttachmentFileChangedAsync(InputFileChangeEventArgs e)
    {
        foreach (var file in e.GetMultipleFiles())
            await OnAttachmentUpload.InvokeAsync(file);
    }

    private async Task RemoveAttachmentAsync(string attachmentId)
    {
        await OnAttachmentRemoved.InvokeAsync(attachmentId);
    }

    private async Task AddCommentAsync()
    {
        if (Task is null || string.IsNullOrWhiteSpace(_newCommentText)) return;
        var comment = CreateCommentEntry(_newCommentText.Trim());
        _newCommentText = string.Empty;
        await OnCommentAdded.InvokeAsync(comment);
    }

    private async Task StartTimerAsync()
    {
        if (Task is null) return;
        _timerStartedAt = DateTime.UtcNow;
        await OnTimerStarted.InvokeAsync(Task.Id);
    }

    private async Task StopTimerAsync()
    {
        if (Task is null) return;
        var entry = new GanttTimeLogEntry
        {
            TaskId     = Task.Id,
            AssigneeId = string.Empty,
            StartedAt  = _timerStartedAt ?? DateTime.UtcNow.AddMinutes(-1),
            StoppedAt  = DateTime.UtcNow
        };
        _timerStartedAt = null;
        await OnTimerStopped.InvokeAsync((Task.Id, entry));
    }

    internal static MarkupString ParseMentions(string text)
    {
        var escaped = System.Net.WebUtility.HtmlEncode(text);
        var html = Regex.Replace(escaped, @"@(\w+)",
            m => $"<span class=\"tm-gantt__mention\">@{m.Groups[1].Value}</span>");
        return new MarkupString(html);
    }

    internal async Task AddCommentFromHtmlAsync(string html)
    {
        if (Task is null || string.IsNullOrWhiteSpace(html)) return;
        var text = Regex.Replace(html, "<[^>]+>", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        var comment = CreateCommentEntry(text);
        await OnCommentAdded.InvokeAsync(comment);
    }

    internal IReadOnlyList<TmCommentEntry> GetCommentEntries()
        => Task?.Comments ?? [];

    private TmCommentEntry CreateCommentEntry(string text)
    {
        var entry = new TmCommentEntry
        {
            ThreadId = Task?.Id ?? string.Empty,
            Author = new TmUserRef(),
            Body = ParseMentions(text).Value.Replace("\n", "<br/>", StringComparison.Ordinal),
            BodyFormat = TmCommentBodyFormat.Html,
            CreatedAt = DateTimeOffset.UtcNow,
            CanEdit = false,
            CanDelete = false,
            Metadata = new Dictionary<string, object>()
        };

        if (Task is not null)
            entry.Metadata["TaskId"] = Task.Id;

        return entry;
    }

    private bool Validate()
    {
        if (!DateTime.TryParseExact(_editStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
        {
            _validationError = Loc["TmGanttTaskPanel_ErrorInvalidStart"];
            return false;
        }
        if (!DateTime.TryParseExact(_editEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        {
            _validationError = Loc["TmGanttTaskPanel_ErrorInvalidEnd"];
            return false;
        }
        if (start > end)
        {
            _validationError = Loc["TmGanttTaskPanel_ErrorStartAfterEnd"];
            return false;
        }
        if (_editPercent is null || _editPercent < 0 || _editPercent > 100)
        {
            _validationError = Loc["TmGanttTaskPanel_ErrorPercentRange"];
            return false;
        }
        _validationError = null;
        return true;
    }
}
