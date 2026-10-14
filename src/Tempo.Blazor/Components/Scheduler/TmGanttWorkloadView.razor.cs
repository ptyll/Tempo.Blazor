using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Scheduler;

public partial class TmGanttWorkloadView
{
    [Parameter] public IReadOnlyList<TmWorkItem> Tasks { get; set; } = [];
    [Parameter] public WorkingSchedule WorkingSchedule { get; set; } = new();
    [Parameter] public WorkloadDisplayMode DisplayMode { get; set; } = WorkloadDisplayMode.Hours;

    /// <summary>
    /// Today's date in the display zone — caps the rolling window at (today − 7 days) and seeds it
    /// when no data exists. <see cref="TmGantt"/> passes the user-zone today; defaults to
    /// <see cref="DateTime.Today"/>.
    /// </summary>
    [Parameter] public DateTime Today { get; set; } = DateTime.Today;
}
