using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The summary band of <see cref="TmGantt"/> (phase 11): the rollup strip under the tree + timeline —
/// status donut, time, milestones, budget, WBS. Everything the band paints is the owner's
/// <see cref="TmGantt.Summary"/>, computed once per data refresh; this part only draws it and skips a
/// render pass that changes nothing it draws (chrome-only owner renders — a menu, a dialog — never
/// repaint the band). A host never places one: hidden from the editor like the other parts.
/// <para>
/// Layout: a desktop container renders the five cards in one row, a tablet two columns, and the
/// read-mostly mobile container a compact one-line strip — the full cards live in the mobile
/// "Summary" sheet (<c>Sheet</c>, rendered by <see cref="TmGanttMobileOverlays"/>). The
/// <see cref="TmGantt.SummaryBandContent"/> template replaces the built-in cards with host content.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class TmGanttSummaryBand : TmGanttPart
{
    /// <summary>True inside the mobile "Summary" sheet: the full cards render there too (never the compact strip).</summary>
    [Parameter, EditorBrowsable(EditorBrowsableState.Never)] public bool Sheet { get; set; }

    /// <summary>What the band paints: the owner's computed summary, the resolved layout and the culture /
    /// currency the amounts format with (the summary reference moves on every data refresh).</summary>
    private readonly record struct SummaryState(
        int DrawVersion,
        GanttSummary? Summary,
        TmLayoutMode Layout,
        GanttView View,
        string Culture,
        string? BudgetCurrency);

    /// <inheritdoc />
    protected override object? RenderState => new SummaryState(
        Owner.DrawVersion, Owner.Summary, Owner.CurrentLayout, Owner.View, Loc.Culture.Name, Owner.BudgetCurrency);

    /// <summary>The band shows in the chart views only (the tree + timeline) — an alternate view replaces the chart.</summary>
    private static bool IsChartView(GanttView view)
        => view is GanttView.Day or GanttView.Week or GanttView.Month or GanttView.List;

    /// <summary>The section's class list: the layout modifier and the sheet variant.</summary>
    private string SectionClass
    {
        get
        {
            var classes = new List<string> { "tm-gantt-summary" };
            classes.Add(Owner.CurrentLayout switch
            {
                TmLayoutMode.Tablet => "tm-gantt-summary--tablet",
                TmLayoutMode.Mobile => "tm-gantt-summary--mobile",
                _ => "tm-gantt-summary--desktop",
            });
            if (Sheet) classes.Add("tm-gantt-summary--sheet");
            return string.Join(" ", classes);
        }
    }

    private string LayoutName => Owner.CurrentLayout switch
    {
        TmLayoutMode.Tablet => "tablet",
        TmLayoutMode.Mobile => "mobile",
        _ => "desktop",
    };

    private string? TwoColumnClass => Owner.IsTabletLayout ? "tm-gantt-summary__cards--two" : null;

    /// <summary>Card visibility: a card without data would only repeat zero (the band hides it, per the mockup's five cards).</summary>
    private static bool StatusVisible(GanttSummary summary) => summary.TaskCount > 0;

    private static bool TimeVisible(GanttSummary summary) => summary.LoggedHours > 0 || summary.EstimatedHours > 0;

    private static bool MilestonesVisible(GanttSummary summary) => summary.MilestonesTotal > 0;

    private static bool BudgetVisible(GanttSummary summary) => summary.BudgetTotal is not null || summary.ActualCost > 0;

    private static bool WbsVisible(GanttSummary summary) => summary.TaskCount > 0;

    /// <summary>The share of the visible tasks that are Done, as the compact strip's whole percent.</summary>
    internal static int DonePercent(GanttSummary summary)
        => summary.TaskCount == 0 ? 0
            : (int)Math.Round(100d * summary.StatusCounts.GetValueOrDefault(TmWorkItemStatus.Done) / summary.TaskCount);

    /// <summary>The compact strip's three segments: "24 tasks • 40 % done • 1 overdue".</summary>
    internal string CompactText(GanttSummary summary)
    {
        var parts = new List<string>
        {
            Loc.Plural("TmGantt_Summary_TasksCount", summary.TaskCount),
            string.Format(Loc.Culture, Loc["TmGantt_Summary_PercentDone"], DonePercent(summary).ToString("N0", Loc.Culture)),
        };
        if (summary.OverdueCount > 0)
            parts.Add(Loc.Plural("TmGantt_Summary_OverdueCount", summary.OverdueCount));

        return string.Join(" • ", parts);
    }

    /// <summary>The donut's text alternative (one image for assistive tech, named by the summary).</summary>
    internal string DonutAria(GanttSummary summary)
        => Loc["TmGantt_Summary_AriaDonut",
            summary.TaskCount,
            summary.StatusCounts.GetValueOrDefault(TmWorkItemStatus.Done),
            summary.StatusCounts.GetValueOrDefault(TmWorkItemStatus.InProgress),
            summary.StatusCounts.GetValueOrDefault(TmWorkItemStatus.Open),
            summary.OverdueCount];

    /// <summary>The donut slice colours carry the status meaning; the legend repeats them with numbers, never colour alone.</summary>
    internal static string StatusColor(TmWorkItemStatus status) => status switch
    {
        TmWorkItemStatus.Done => "var(--tm-status-done-fg)",
        TmWorkItemStatus.InProgress => "var(--tm-status-inprogress-fg)",
        TmWorkItemStatus.Open => "var(--tm-status-open-fg)",
        TmWorkItemStatus.Closed => "var(--tm-status-closed-fg)",
        _ => "var(--tm-color-primary)",
    };

    /// <summary>Parts of an amount formatted through the active culture, currency code appended when the host set one.</summary>
    internal string Money(decimal amount) => Owner.BudgetCurrency is { } currency
        ? $"{amount.ToString("N0", Loc.Culture)} {currency}"
        : amount.ToString("N0", Loc.Culture);

    private string Hours(double amount) => amount.ToString("N0", Loc.Culture);

    /// <summary>The time card's main figure: "128 h of 320 h", or just the logged hours without an estimate.</summary>
    internal string TimeText(GanttSummary summary) => summary.EstimatedHours > 0
        ? Loc["TmGantt_Summary_TimeOf", Hours(summary.LoggedHours), Hours(summary.EstimatedHours)]
        : Loc["TmGantt_Summary_TimeLogged", Hours(summary.LoggedHours)];

    /// <summary>The time bar's percent of the estimate (100 % when only logged hours exist — nothing left to fill).</summary>
    private double TimeBarValue(GanttSummary summary)
        => summary.EstimatedHours > 0 ? Math.Min(summary.LoggedHours, summary.EstimatedHours) : summary.LoggedHours;

    private double TimeBarMax(GanttSummary summary)
        => summary.EstimatedHours > 0 ? summary.EstimatedHours : Math.Max(summary.LoggedHours, 1);

    private string TimePercent(GanttSummary summary) => TimeBarMax(summary) <= 0
        ? "0 %"
        : string.Format(Loc.Culture, "{0} %", Math.Round(100d * summary.LoggedHours / TimeBarMax(summary)).ToString("N0", Loc.Culture));
}
