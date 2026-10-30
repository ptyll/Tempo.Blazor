using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TmGantt redesign, phase 11 (the summary band): the opt-in rollup strip under the tree + timeline —
/// five cards on a desktop container, two columns on a tablet, a compact one-line strip plus a "Summary"
/// entry in the More sheet on the read-mostly mobile container. Everything is OFF by default: the default
/// DOM is unchanged. Culture and clock are pinned: CI runs Linux / UTC / invariant culture plus a
/// non-English ambient lane, and every layout is FORCED with <c>LayoutMode</c> (bUnit runs no container queries).
/// </summary>
public class TmGanttSummaryBandTests : LocalizationTestBase
{
    public TmGanttSummaryBandTests()
    {
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2024, 6, 12, 10, 0, 0, TimeSpan.Zero)));
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(TimeZoneInfo.Utc));
        JSInterop.Setup<bool>("tmGantt.attachTimeline", _ => true).SetResult(true);
        JSInterop.Setup<bool>("tmGantt.attachTree", _ => true).SetResult(true);
    }

    /// <summary>Full data: every card has something to show.</summary>
    private static List<TmWorkItem> Tasks() =>
    [
        new() { Id = "1", Title = "Parent", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30), IsExpanded = true },
        new() { Id = "2", Title = "Done", ParentId = "1", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 10),
                PercentComplete = 100, Status = TmWorkItemStatus.Done, EstimationHours = 100, LoggedHours = 128, ActualCost = 120000m },
        new() { Id = "3", Title = "Running", ParentId = "1", Start = new DateTime(2024, 6, 5), End = new DateTime(2024, 6, 20),
                PercentComplete = 40, Status = TmWorkItemStatus.InProgress, EstimationHours = 220, ActualCost = 200000m },
        new() { Id = "4", Title = "Late", Start = new DateTime(2024, 5, 1), End = new DateTime(2024, 6, 1), Status = TmWorkItemStatus.Open },
        new() { Id = "5", Title = "Launch", IsMilestone = true, Start = new DateTime(2024, 6, 30), End = new DateTime(2024, 6, 30),
                Status = TmWorkItemStatus.Done },
    ];

    private IRenderedComponent<TmGantt> RenderGantt(TmLayoutMode mode, Action<ComponentParameterCollectionBuilder<TmGantt>>? configure = null) =>
        Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks());
            p.Add(c => c.LayoutMode, mode);
            p.Add(c => c.ShowSummaryBand, true);
            p.Add(c => c.BudgetTotal, 500000m);
            configure?.Invoke(p);
        });

    [Fact]
    public void TmGantt_SummaryBand_Hidden_By_Default()
    {
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Tasks()));

        cut.FindAll(".tm-gantt-summary").Should().BeEmpty("the band is opt-in (ShowSummaryBand default false)");
        cut.Instance.ShowSummaryBand.Should().BeFalse();
    }

    [Fact]
    public void TmGantt_SummaryBand_Desktop_Five_Cards_In_Order()
    {
        var cut = RenderGantt(TmLayoutMode.Desktop);

        var band = cut.Find("[data-testid='gantt-summary-band']");
        band.Should().NotBeNull();
        band.GetAttribute("aria-label").Should().NotBeNullOrEmpty("the band is a labelled section");

        var ids = cut.FindAll("[data-testid='gantt-summary-band'] .tm-gantt-summary__card")
            .Select(card => card.GetAttribute("data-testid")).ToList();
        ids.Should().Equal(
            "gantt-summary-status",
            "gantt-summary-time",
            "gantt-summary-milestones",
            "gantt-summary-budget",
            "gantt-summary-wbs");

        // The status card: "5 total" donut centre and a legend whose entries carry numbers (never colour alone).
        var status = cut.Find("[data-testid='gantt-summary-status']");
        status.QuerySelector(".tm-gantt-summary__total")!.TextContent.Should().Contain("5");
        status.QuerySelectorAll(".tm-gantt-summary__legend-item").Should().HaveCountGreaterThan(2);
        status.QuerySelector(".tm-gantt-summary__legend-count").Should().NotBeNull();

        // Time: "128 h of 320 h" (invariant digits, culture-pinned en).
        cut.Find("[data-testid='gantt-summary-time']").TextContent.Should().Contain("128").And.Contain("320");

        // Milestones: 1 of 1 done.
        cut.Find("[data-testid='gantt-summary-milestones']").TextContent.Should().Contain("1");

        // Budget: actual of the host total.
        cut.Find("[data-testid='gantt-summary-budget']").TextContent.Should().Contain("320,000").And.Contain("500,000");

        // WBS: two levels over five tasks.
        cut.Find("[data-testid='gantt-summary-wbs']").TextContent.Should().Contain("2");
    }

    [Fact]
    public void TmGantt_SummaryBand_Hides_Card_Without_Data()
    {
        var plain = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items,
                new List<TmWorkItem>
                {
                    new() { Id = "1", Title = "Only", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 5) },
                });
            p.Add(c => c.LayoutMode, TmLayoutMode.Desktop);
            p.Add(c => c.ShowSummaryBand, true);
        });

        var ids = plain.FindAll("[data-testid='gantt-summary-band'] .tm-gantt-summary__card")
            .Select(card => card.GetAttribute("data-testid")).ToList();
        ids.Should().NotContain("gantt-summary-time", "no estimations, no logged hours");
        ids.Should().NotContain("gantt-summary-milestones", "no milestones");
        ids.Should().NotContain("gantt-summary-budget", "no cost and no host budget");
        ids.Should().Contain("gantt-summary-status").And.Contain("gantt-summary-wbs");
    }

    [Fact]
    public void TmGantt_SummaryBand_Template_Override()
    {
        var cut = RenderGantt(TmLayoutMode.Desktop, p => p
            .Add(c => c.SummaryBandContent, (RenderFragment<GanttSummary>)(summary => builder =>
            {
                builder.OpenElement(0, "p");
                builder.AddAttribute(1, "class", "custom-summary");
                builder.AddContent(2, $"CUSTOM {summary.TaskCount}");
                builder.CloseElement();
            })));

        cut.Find(".tm-gantt-summary .custom-summary").TextContent.Should().Contain("CUSTOM 5");
        cut.FindAll(".tm-gantt-summary__card").Should().BeEmpty("the template replaces the built-in cards");
    }

    [Fact]
    public void TmGantt_SummaryBand_Tablet_Two_Column_Grid()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet);

        cut.Find("[data-testid='gantt-summary-band']").ClassList.Should().Contain("tm-gantt-summary--tablet");
        cut.Find(".tm-gantt-summary__cards").ClassList.Should().Contain("tm-gantt-summary__cards--two");
    }

    [Fact]
    public void TmGantt_SummaryBand_Mobile_Compact_Strip_And_More_Sheet_Entry()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile);

        // The compact one-line strip replaces the cards in the chart; the cards container is not rendered.
        var strip = cut.Find("[data-testid='gantt-summary-compact']");
        strip.TextContent.Should().Contain("5").And.Contain("%").And.Contain("1", "the overdue count rides the strip");
        cut.FindAll("[data-testid='gantt-summary-band'] .tm-gantt-summary__card").Should().BeEmpty();

        // The More sheet carries a Summary entry; opening it shows the full cards as a sibling sheet.
        cut.Find("[data-action-id='gantt-mobile-more']").Click();
        var entry = cut.Find("[data-testid='gantt-more-summary']");
        entry.Should().NotBeNull();
        entry.Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='gantt-summary-sheet'] .tm-gantt-summary__card")
            .Should().HaveCount(5, "the sheet shows the full cards"));
    }

    [Fact]
    public void TmGantt_SummaryBand_RenderGate_Skips_Unrelated_Render()
    {
        var cut = RenderGantt(TmLayoutMode.Desktop);
        var band = cut.FindComponent<TmGanttSummaryBand>();
        var renders = band.RenderCount;

        cut.Find("[data-testid='gantt-filter-btn']").Click();

        band.RenderCount.Should().Be(renders, "the filter editor is chrome: the band's render gate must skip the pass");
    }

    [Fact]
    public void TmGantt_SummaryBand_Updates_After_Inline_Status_Edit()
    {
        var cut = RenderGantt(TmLayoutMode.Desktop, p => p.Add(c => c.ShowTaskPanel, true));

        // "Late" (Open, ended 6/1) becomes Done through the task panel's draft + Save.
        cut.Find("[data-testid='tree-row-4']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='task-status']").Should().NotBeNull());
        var doneBefore = DoneCount(cut);
        doneBefore.Should().Be(2);

        cut.Find("[data-testid='task-status'] select").Change(nameof(TmWorkItemStatus.Done));
        cut.Find("[data-testid='task-save']").Click();
        cut.WaitForAssertion(() => DoneCount(cut)
            .Should().Be(3, "the summary is recomputed once per refresh — an in-place status edit moves the counts"));
    }

    [Fact]
    public void TmGantt_SummaryBand_Donut_Has_Text_Alternative()
    {
        var cut = RenderGantt(TmLayoutMode.Desktop);

        var donut = cut.Find("[data-testid='gantt-summary-status'] .tm-gantt-summary__donut");
        donut.GetAttribute("role").Should().Be("img");
        donut.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace("the donut is one image for assistive tech, named by a text summary");
    }

    [Theory]
    [InlineData(GanttView.Day)]
    [InlineData(GanttView.Week)]
    [InlineData(GanttView.Month)]
    [InlineData(GanttView.List)]
    public void TmGantt_SummaryBand_Shows_In_Chart_Views(GanttView view)
    {
        var cut = RenderGantt(TmLayoutMode.Desktop, p => p.Add(c => c.View, view));

        cut.FindAll("[data-testid='gantt-summary-band']").Should().HaveCount(1);
    }

    [Theory]
    [InlineData(GanttView.Workload)]
    [InlineData(GanttView.Calendar)]
    [InlineData(GanttView.Board)]
    [InlineData(GanttView.Dashboard)]
    [InlineData(GanttView.People)]
    public void TmGantt_SummaryBand_Hidden_In_Alternate_Views(GanttView view)
    {
        var cut = RenderGantt(TmLayoutMode.Desktop, p => p.Add(c => c.View, view));

        cut.FindAll("[data-testid='gantt-summary-band']").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_SummaryBand_Collapse_Does_Not_Change_The_Numbers()
    {
        var cut = RenderGantt(TmLayoutMode.Desktop);
        var statusBefore = cut.Find("[data-testid='gantt-summary-status']").TextContent;
        var timeBefore = cut.Find("[data-testid='gantt-summary-time']").TextContent;

        // Collapse the parent: the tree above the band hides its two children (three visible rows left).
        cut.Find("[data-testid='tree-row-1'] .tm-gantt__expand-btn").Click();

        cut.FindAll(".tm-gantt__tree-row").Should().HaveCount(3, "the collapse took effect in the tree");
        cut.Find("[data-testid='gantt-summary-status']").TextContent.Should().Be(statusBefore,
            "the summary describes the filtered data, not the collapsed view");
        cut.Find("[data-testid='gantt-summary-time']").TextContent.Should().Be(timeBefore);
        cut.Instance.Summary!.TaskCount.Should().Be(5);
        cut.FindAll("[data-testid='gantt-summary-filtered']").Should().BeEmpty("collapsing is not a filter");
    }

    [Fact]
    public void TmGantt_SummaryBand_ShowClosed_Filters_The_Scope_And_Notes_It()
    {
        // Two of the five tasks are Done; hiding closed tasks must shrink the scope AND carry the note.
        var cut = RenderGantt(TmLayoutMode.Desktop,
            p => p.Add(c => c.ViewSettings, new GanttViewSettings { ShowClosedTasks = false }));

        cut.Instance.Summary!.TaskCount.Should().Be(3, "Done and Closed tasks leave the summary scope");
        cut.Find("[data-testid='gantt-summary-filtered']").Should().NotBeNull("closed tasks are hidden, so the band notes it");
    }

    /// <summary>The number the status card's legend shows for one status (the row exists once; its count is the text).</summary>
    private static int DoneCount(IRenderedComponent<TmGantt> cut)
        => int.Parse(cut.Find("[data-testid='gantt-summary-status'] [data-status='GanttStatus_Done'] .tm-gantt-summary__legend-count")
            .TextContent.Trim(), CultureInfo.InvariantCulture);

    private static int CountLegend(IRenderedComponent<TmGantt> cut, string statusKey)
        => cut.FindAll("[data-testid='gantt-summary-status'] .tm-gantt-summary__legend-item")
            .Count(item => item.GetAttribute("data-status") == statusKey);
}
