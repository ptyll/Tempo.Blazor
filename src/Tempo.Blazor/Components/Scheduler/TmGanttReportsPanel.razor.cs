using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>The Reports inspector of <see cref="TmGantt"/>: one card per report with its type icon, a Run button and an optional kebab menu.</summary>
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

    /// <summary>Fires when the user picks Edit in a card's menu. The menu offers an entry only for a callback the host handles.</summary>
    [Parameter] public EventCallback<GanttReport> OnReportEdit { get; set; }

    /// <summary>Fires when the user picks Delete in a card's menu. The menu offers an entry only for a callback the host handles.</summary>
    [Parameter] public EventCallback<GanttReport> OnReportDelete { get; set; }

    /// <summary>Fires when the user picks Duplicate in a card's menu. The menu offers an entry only for a callback the host handles.</summary>
    [Parameter] public EventCallback<GanttReport> OnReportDuplicate { get; set; }

    /// <summary>Extra attributes for the panel root (the tab strip wires <c>role</c>, <c>id</c> and <c>aria-labelledby</c> through it).</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The kebab menu exists only when the host can act on at least one of its entries.</summary>
    private bool HasMenu => OnReportEdit.HasDelegate || OnReportDelete.HasDelegate || OnReportDuplicate.HasDelegate;

    private async Task RunReportAsync(GanttReport report)
        => await OnReportRun.InvokeAsync(report);

    private static string IconFor(GanttReportType type) => type switch
    {
        GanttReportType.TimeSpent => "clock",
        GanttReportType.ResourceUtilization => "users",
        GanttReportType.BudgetOverview => "wallet",
        GanttReportType.MilestoneProgress => "flag",
        _ => "bar-chart-2",
    };

    private async Task OnMenuSelectAsync(GanttReport report, string action)
    {
        switch (action)
        {
            case "edit": await OnReportEdit.InvokeAsync(report); break;
            case "duplicate": await OnReportDuplicate.InvokeAsync(report); break;
            case "delete": await OnReportDelete.InvokeAsync(report); break;
        }
    }
}