using System.Globalization;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// Localized labels and culture-aware date formats shared by the TmGantt sub-components
/// (filter panel, import mapping dialog, history drawer, sidebar history, workload grid), so an enum
/// name or a hard-coded <c>dd.MM</c> pattern never reaches the user.
/// </summary>
internal static class GanttLabels
{
    /// <summary>Localized text of a filter operator ("contains", "is before", ...).</summary>
    public static string FilterOperator(ITmLocalizer loc, GanttFilterOperator op) => op switch
    {
        GanttFilterOperator.Equals => loc["TmGantt_FilterOp_Equals"],
        GanttFilterOperator.NotEquals => loc["TmGantt_FilterOp_NotEquals"],
        GanttFilterOperator.Contains => loc["TmGantt_FilterOp_Contains"],
        GanttFilterOperator.Before => loc["TmGantt_FilterOp_Before"],
        GanttFilterOperator.After => loc["TmGantt_FilterOp_After"],
        GanttFilterOperator.IsEmpty => loc["TmGantt_FilterOp_IsEmpty"],
        GanttFilterOperator.IsNotEmpty => loc["TmGantt_FilterOp_IsNotEmpty"],
        _ => op.ToString(),
    };

    /// <summary>Localized column / mapping-target name for a <see cref="GanttColumnKey"/>.</summary>
    public static string Column(ITmLocalizer loc, GanttColumnKey key) => key switch
    {
        GanttColumnKey.Title => loc["TmGantt_TaskTitle"],
        GanttColumnKey.Start => loc["TmGantt_Start"],
        GanttColumnKey.End => loc["TmGantt_End"],
        GanttColumnKey.Progress => loc["TmGantt_TooltipProgress"],
        GanttColumnKey.Status => loc["TmGantt_Status"],
        GanttColumnKey.Priority => loc["TmGantt_FilterFieldPriority"],
        GanttColumnKey.WBS => loc["TmGantt_WBS"],
        GanttColumnKey.Duration => loc["TmGanttTaskPanel_Duration"],
        GanttColumnKey.Deadline => loc["TmGanttTaskPanel_Deadline"],
        GanttColumnKey.Assignees => loc["TmGantt_ColumnAssignees"],
        GanttColumnKey.Estimation => loc["TmGanttTaskPanel_Estimation"],
        GanttColumnKey.TimeLog => loc["TmGanttTaskPanel_TimeLog"],
        GanttColumnKey.Budget => loc["GanttTask_Budget"],
        GanttColumnKey.ActualCost => loc["GanttTask_ActualCost"],
        GanttColumnKey.Comments => loc["GanttTask_Comments"],
        GanttColumnKey.Predecessor => loc["TmGantt_ColumnPredecessor"],
        GanttColumnKey.ResourceOverload => loc["TmGantt_Overloaded"],
        _ => key.ToString(),
    };

    /// <summary>Localized text of an audit-log action key; an unknown key is shown as-is.</summary>
    public static string ChangeType(ITmLocalizer loc, string changeType) => changeType switch
    {
        "TaskChanged" => loc["GanttHistory_TaskChanged"],
        "StatusChanged" => loc["GanttHistory_StatusChanged"],
        "PriorityChanged" => loc["GanttHistory_PriorityChanged"],
        "DependencyChanged" => loc["GanttHistory_DependencyChanged"],
        _ => changeType,
    };

    /// <summary>Audit-log timestamp converted into <paramref name="zone"/>, in the localizer's culture (general short pattern).</summary>
    public static string Timestamp(DateTimeOffset timestamp, CultureInfo culture, TimeZoneInfo zone)
        => TimeZoneInfo.ConvertTime(timestamp, zone).ToString("g", culture);

    /// <summary>
    /// Day and month in the culture's own order and separator, without the year
    /// (<c>3/9</c> in en-US, <c>09.03.</c> in cs-CZ, <c>9. 3.</c> in sk-SK, <c>03-09</c> in sv-SE).
    /// </summary>
    public static string ShortDayMonth(DateTime date, CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.ShortDatePattern;
        var year = System.Text.RegularExpressions.Regex.Match(
            pattern, "y+", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!year.Success)
        {
            return date.ToString(pattern, culture);
        }

        var start = year.Index;
        var end = year.Index + year.Length;
        if (end >= pattern.Length)
        {
            // Year last ("M/d/yyyy"): drop the separator in front of it. A dot is part of the date
            // ("9. 3." in sk-SK, "09.03." in cs-CZ), so the first dot of that separator stays.
            while (start > 0 && !char.IsLetter(pattern[start - 1]))
            {
                start--;
            }

            var dot = pattern.IndexOf('.', start, year.Index - start);
            if (dot >= 0)
            {
                start = dot + 1;
            }
        }
        else
        {
            // Year first or in the middle ("yyyy-MM-dd"): drop the separator behind it.
            while (end < pattern.Length && !char.IsLetter(pattern[end]))
            {
                end++;
            }
        }

        var trimmed = pattern.Remove(start, end - start);
        return date.ToString(string.IsNullOrWhiteSpace(trimmed) ? "d" : trimmed, culture);
    }
}
