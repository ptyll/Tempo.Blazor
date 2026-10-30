using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The audit-log feed shared by the sidebar History panel and the history drawer: one row per entry with a typed icon
/// in a circle, the change type, the task (a link when the task is still in the chart), the author, a relative time
/// (the absolute time is its tooltip) and, in the drawer, the Time travel / Rollback actions.
/// </summary>
public partial class TmGanttHistoryList
{
    private ITmRelativeTimeFormatter? _relative;

    /// <summary>Activity entries, newest first when rendered.</summary>
    [Parameter] public IReadOnlyList<TmActivityEntry>? History { get; set; }

    /// <summary>Tasks the entries refer to (resolves an entry's task title and makes it a link). Default none: the task id is shown.</summary>
    [Parameter] public IReadOnlyList<TmWorkItem> Tasks { get; set; } = [];

    /// <summary>Zone the times are judged and shown in. Defaults to the machine's local zone; <see cref="TmGantt"/> passes the user's display zone.</summary>
    [Parameter] public TimeZoneInfo? TimeZone { get; set; }

    /// <summary>Maximum number of entries rendered (newest first). Default all.</summary>
    [Parameter] public int? Take { get; set; }

    /// <summary>When true each entry carries the Time travel and Rollback buttons (the drawer). Default false: a read-only feed.</summary>
    [Parameter] public bool ShowActions { get; set; }

    /// <summary>Fires when the user clicks a task title (arg = task id).</summary>
    [Parameter] public EventCallback<string> OnTaskSelected { get; set; }

    /// <summary>Fires when the user asks to travel to an entry's time.</summary>
    [Parameter] public EventCallback<DateTime> OnTimeTravelRequested { get; set; }

    /// <summary>Fires when the user asks to roll back an entry. The drawer confirms first.</summary>
    [Parameter] public EventCallback<TmActivityEntry> OnRollbackRequested { get; set; }

    [Inject] private IServiceProvider Services { get; set; } = default!;

    private ITmRelativeTimeFormatter Relative => _relative ??= TmRelativeTime.Resolve(Services, Loc);

    private TimeZoneInfo Zone => TimeZone ?? TimeZoneInfo.Local;

    private IEnumerable<TmActivityEntry> Entries()
    {
        var ordered = (History ?? []).OrderByDescending(e => e.Timestamp);
        return Take is { } take ? ordered.Take(take) : ordered;
    }

    private TmWorkItem? TaskOf(TmActivityEntry entry)
        => Tasks.FirstOrDefault(t => t.Id == entry.EntityRef.EntityId);

    private string TaskTitle(TmActivityEntry entry)
        => TaskOf(entry)?.Title ?? entry.EntityRef.EntityId;

    private string TimeText(TmActivityEntry entry)
    {
        var text = Relative.Format(entry.Timestamp, timeZone: Zone);
        return text;
    }

    private string TimeTitle(TmActivityEntry entry) => GanttLabels.Timestamp(entry.Timestamp, Loc.Culture, Zone);

    private Task TimeTravelAsync(DateTimeOffset timestamp) => OnTimeTravelRequested.InvokeAsync(timestamp.UtcDateTime);

    private Task RollbackAsync(TmActivityEntry entry) => OnRollbackRequested.InvokeAsync(entry);

    /// <summary>Icon + colour family per audit action; an unknown action gets the neutral edit icon.</summary>
    internal static (string Icon, string CssClass) Visual(string action) => action switch
    {
        "Created" => ("plus", "tm-gantt__history-entry-icon--created"),
        "StatusChanged" => ("check", "tm-gantt__history-entry-icon--status"),
        "AssigneeChanged" or "AssigneeAdded" => ("users", "tm-gantt__history-entry-icon--assignee"),
        "DatesChanged" or "DeadlineSet" => ("calendar", "tm-gantt__history-entry-icon--dates"),
        "DependencyChanged" => ("link", "tm-gantt__history-entry-icon--dependency"),
        "CommentAdded" => ("message-square", "tm-gantt__history-entry-icon--comment"),
        _ => ("edit", "tm-gantt__history-entry-icon--edit"),
    };
}