using System.Globalization;
using Tempo.Blazor.Abstractions.WorkItems;

namespace Tempo.Blazor.Abstractions.Models;

/// <summary>
/// The one place a <see cref="TmWorkItem"/> becomes a flat cell value for an Excel export (or a
/// preview of one). Both <c>GanttXlsxExporter</c> and the export dialog's built-in preview share it,
/// so a cell and its preview never drift — and the format is invariant, so a CSV/locale lane can
/// never turn 12.5 into 12,5 inside a workbook.
/// </summary>
public static class GanttColumnValues
{
    /// <summary>Formats the task's value for <paramref name="column"/>; an empty string when the column carries nothing.</summary>
    public static string Format(TmWorkItem task, GanttColumnKey column) => column switch
    {
        GanttColumnKey.Title => task.Title,
        GanttColumnKey.Start => FormatDate(task.Start),
        GanttColumnKey.End => FormatDate(task.End),
        GanttColumnKey.Progress => task.PercentComplete.ToString(CultureInfo.InvariantCulture),
        GanttColumnKey.Status => task.Status.ToString(),
        GanttColumnKey.Priority => task.Priority.ToString(),
        GanttColumnKey.Duration => ((int)task.Duration.TotalDays).ToString(CultureInfo.InvariantCulture),
        GanttColumnKey.Deadline => task.DueDate is { } due ? FormatDate(due) : string.Empty,
        GanttColumnKey.Estimation => FormatHours(task.EstimationHours),
        GanttColumnKey.TimeLog => FormatHours(task.LoggedHours),
        GanttColumnKey.Assignees => string.Join(", ", task.Assignees.Select(a => a.Name)),
        GanttColumnKey.Comments => task.Comments.Count.ToString(CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    private static string FormatDate(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatHours(double? hours)
        => hours?.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty;
}
