using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Shared;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>Side drawer showing the Gantt audit log with time-travel and rollback.</summary>
public partial class TmGanttHistoryDrawer
{
    /// <summary>Activity history entries to display.</summary>
    [Parameter] public IReadOnlyList<TmActivityEntry>? History { get; set; }

    /// <summary>Whether the drawer is visible.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Fires when the user requests time-travel to a specific timestamp.</summary>
    [Parameter] public EventCallback<DateTime> OnTimeTravelRequested { get; set; }

    /// <summary>Fires when the user requests rollback to a specific history entry.</summary>
    [Parameter] public EventCallback<TmActivityEntry> OnRollbackRequested { get; set; }

    /// <summary>Fires when the drawer should close.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>Zone the entry timestamps are shown in. Defaults to the machine's local zone (<see cref="TimeZoneInfo.Local"/>); <see cref="TmGantt"/> passes the user's display zone.</summary>
    [Parameter] public TimeZoneInfo? TimeZone { get; set; }

    private string GetChangeTypeLabel(string changeType) => GanttLabels.ChangeType(Loc, changeType);

    private async Task TimeTravelAsync(DateTimeOffset timestamp) =>
        await OnTimeTravelRequested.InvokeAsync(timestamp.UtcDateTime);

    private async Task RollbackAsync(TmActivityEntry entry) =>
        await OnRollbackRequested.InvokeAsync(entry);
}
