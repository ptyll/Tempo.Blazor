using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Scheduler;

public partial class TmGanttReportsPanel
{
    /// <summary>Report definitions to list.</summary>
    [Parameter] public IReadOnlyList<GanttReport> Reports { get; set; } = [];

    /// <summary>Fires when the user clicks "Run" on a report.</summary>
    [Parameter] public EventCallback<GanttReport> OnReportRun { get; set; }

    /// <summary>
    /// Fires when the user asks for a new report. The panel has no report editor of its own, so the
    /// "New report" button is only rendered when the host handles this callback.
    /// </summary>
    [Parameter] public EventCallback OnNewReportRequested { get; set; }

    /// <summary>Extra attributes for the panel root (the tab strip wires <c>role</c>, <c>id</c> and <c>aria-labelledby</c> through it).</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private async Task RunReportAsync(GanttReport report)
        => await OnReportRun.InvokeAsync(report);
}
