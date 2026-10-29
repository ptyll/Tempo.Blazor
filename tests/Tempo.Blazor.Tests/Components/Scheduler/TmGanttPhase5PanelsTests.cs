using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TmGantt redesign, phase 5: the docked workload panel (GA2), the shared history list, the report cards
/// (GA3) and the portfolio project cards. Written BEFORE the implementation. Culture is pinned through the
/// mock localizer (<c>en</c>): CI runs Linux / UTC / invariant culture.
/// </summary>
public class TmGanttPhase5PanelsTests : LocalizationTestBase
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");

    private static TmWorkItem Task(string id, string title, DateTime start, DateTime end, int percent = 0, string? assignee = null, string? parentId = null) => new()
    {
        Id = id,
        Title = title,
        Start = start,
        End = end,
        PercentComplete = percent,
        ParentId = parentId,
        Assignees = assignee is null ? [] : [new TmWorkItemAssignee { Id = assignee, Name = "Person " + assignee }],
    };

    // ═══════════════════════════════════════════════════════════════
    // GA2 – docked workload
    // ═══════════════════════════════════════════════════════════════

    private static List<TmWorkItem> WorkloadTasks() =>
    [
        Task("1", "Design", new DateTime(2024, 6, 3), new DateTime(2024, 6, 5), assignee: "u1"),
        Task("2", "Build", new DateTime(2024, 6, 4), new DateTime(2024, 6, 14), assignee: "u2"),
    ];

    [Fact]
    public void TmGantt_WorkloadPlacement_Docked_Renders_Panel_Under_Timeline()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, WorkloadTasks())
            .Add(c => c.WorkloadPlacement, GanttWorkloadPlacement.Docked));

        var dock = cut.Find(".tm-gantt__docked-workload");
        dock.QuerySelector("[data-testid='gantt-workload-view']").Should().NotBeNull();
        dock.PreviousElementSibling!.ClassList.Should().Contain("tm-gantt__main", "the dock sits directly under the tree + timeline");
        dock.ParentElement!.ClassList.Should().Contain("tm-gantt__center");
        cut.FindAll("[data-testid='gantt-workload-view']").Count.Should().Be(1);
    }

    [Fact]
    public void TmGantt_WorkloadPlacement_Default_Is_View_And_Renders_No_Dock()
    {
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, WorkloadTasks()));

        cut.Instance.WorkloadPlacement.Should().Be(GanttWorkloadPlacement.View);
        cut.FindAll(".tm-gantt__docked-workload").Should().BeEmpty();
        cut.FindAll(".tm-gantt__center").Should().BeEmpty("today's DOM is unchanged unless the panel is docked");
    }

    [Fact]
    public void TmGantt_WorkloadPlacement_Docked_Does_Not_Duplicate_The_Workload_View()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, WorkloadTasks())
            .Add(c => c.View, GanttView.Workload)
            .Add(c => c.WorkloadPlacement, GanttWorkloadPlacement.Docked));

        cut.FindAll("[data-testid='gantt-workload-view']").Count.Should().Be(1);
        cut.FindAll(".tm-gantt__docked-workload").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_Docked_Workload_Uses_The_Gantt_Date_Range()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, WorkloadTasks())
            .Add(c => c.WorkloadPlacement, GanttWorkloadPlacement.Docked));

        // Gantt range = earliest start .. latest end (3 - 14 June); u1 has no entry on the 12th but the axis still does.
        cut.Find("[data-testid='workload-cell-u1-2024-06-12']").Should().NotBeNull();
        cut.Find("[data-testid='workload-cell-u2-2024-06-14']").Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_Docked_Workload_Mode_Toggle_Is_Mirrored_And_Raises_Changed()
    {
        WorkloadDisplayMode? changed = null;
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, WorkloadTasks())
            .Add(c => c.WorkloadPlacement, GanttWorkloadPlacement.Docked)
            .Add(c => c.WorkloadDisplayModeChanged, EventCallback.Factory.Create<WorkloadDisplayMode>(this, m => changed = m)));

        cut.Find("[data-testid='workload-mode-percent']").Click();

        changed.Should().Be(WorkloadDisplayMode.Percentage);
        cut.Find("[data-testid='workload-mode-percent']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='workload-cell-u1-2024-06-03']").TextContent.Should().Contain("%");
    }

    [Fact]
    public void TmGantt_Docked_Workload_Scroll_Is_Linked_To_The_Timeline_In_The_Browser()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, WorkloadTasks())
            .Add(c => c.WorkloadPlacement, GanttWorkloadPlacement.Docked));

        // One interop call wires both scrollers (gantt.js linkScroll): no Blazor event per scroll tick.
        cut.WaitForAssertion(() => JSInterop.Invocations.Select(i => i.Identifier).Should().Contain("tmGantt.linkScroll"));
        cut.Find(".tm-gantt__workload-scroller").HasAttribute("blazor:onscroll").Should().BeFalse();
        cut.Find("[data-testid='gantt-timeline']").HasAttribute("blazor:onscroll").Should().BeFalse("the link is made in the browser, not through a Blazor event");
    }

    [Fact]
    public void TmGantt_Default_Placement_Links_No_Scroll()
    {
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, WorkloadTasks()));

        JSInterop.Invocations.Select(i => i.Identifier).Should().NotContain("tmGantt.linkScroll");
        cut.Find("[data-testid='gantt-timeline']").HasAttribute("blazor:onscroll").Should().BeFalse("the default markup is unchanged");
    }
    [Theory]
    [InlineData(GanttView.List)]
    [InlineData(GanttView.Calendar)]
    [InlineData(GanttView.Board)]
    [InlineData(GanttView.Dashboard)]
    [InlineData(GanttView.People)]
    public void TmGantt_Docked_Workload_Is_Only_Shown_Under_A_Timeline_View(GanttView view)
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, WorkloadTasks())
            .Add(c => c.View, view)
            .Add(c => c.WorkloadPlacement, GanttWorkloadPlacement.Docked));

        cut.FindAll(".tm-gantt__docked-workload").Should().BeEmpty($"the {view} view has no timeline for the panel to line up with");
        JSInterop.Invocations.Select(i => i.Identifier).Should().NotContain("tmGantt.linkScroll");
    }

    [Theory]
    [InlineData(GanttView.Day)]
    [InlineData(GanttView.Week)]
    [InlineData(GanttView.Month)]
    public void TmGantt_Docked_Workload_Is_Shown_Under_Every_Timeline_View(GanttView view)
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, WorkloadTasks())
            .Add(c => c.View, view)
            .Add(c => c.WorkloadPlacement, GanttWorkloadPlacement.Docked));

        cut.FindAll(".tm-gantt__docked-workload").Count.Should().Be(1);
    }

    [Fact]
    public void TmGanttWorkloadView_Calls_LinkScroll_On_Every_Render_While_Docked()
    {
        var cut = RenderView(p => p
            .Add(c => c.Docked, true)
            .Add(c => c.RangeStart, new DateTime(2024, 6, 3))
            .Add(c => c.RangeEnd, new DateTime(2024, 6, 9)));
        cut.WaitForAssertion(() => JSInterop.Invocations.Count(i => i.Identifier == "tmGantt.linkScroll").Should().Be(1));

        cut.Find("[data-testid='workload-mode-percent']").Click();

        cut.WaitForAssertion(() => JSInterop.Invocations.Count(i => i.Identifier == "tmGantt.linkScroll").Should().BeGreaterThan(1,
            "the timeline element can be replaced between renders, so the (idempotent) link is re-established on every render"));
    }

    [Fact]
    public void TmGanttWorkloadView_Unlinks_On_Dispose_Only_When_The_Link_Was_Made()
    {
        JSInterop.Setup<bool>("tmGantt.linkScroll", _ => true).SetResult(true);
        var linked = RenderView(p => p
            .Add(c => c.Docked, true)
            .Add(c => c.RangeStart, new DateTime(2024, 6, 3))
            .Add(c => c.RangeEnd, new DateTime(2024, 6, 9)));
        linked.WaitForAssertion(() => JSInterop.Invocations.Select(i => i.Identifier).Should().Contain("tmGantt.linkScroll"));
        linked.Instance.DisposeAsync().AsTask().GetAwaiter().GetResult();
        JSInterop.Invocations.Select(i => i.Identifier).Should().Contain("tmGantt.unlinkScroll");
    }

    [Fact]
    public void TmGanttWorkloadView_Does_Not_Unlink_When_The_Browser_Reported_No_Link()
    {
        JSInterop.Setup<bool>("tmGantt.linkScroll", _ => true).SetResult(false);
        var cut = RenderView(p => p
            .Add(c => c.Docked, true)
            .Add(c => c.RangeStart, new DateTime(2024, 6, 3))
            .Add(c => c.RangeEnd, new DateTime(2024, 6, 9)));
        cut.WaitForAssertion(() => JSInterop.Invocations.Select(i => i.Identifier).Should().Contain("tmGantt.linkScroll"));

        cut.Instance.DisposeAsync().AsTask().GetAwaiter().GetResult();

        JSInterop.Invocations.Select(i => i.Identifier).Should().NotContain("tmGantt.unlinkScroll", "linkScroll returned false: there is nothing to release");
    }

    [Fact]
    public void TmGanttWorkloadView_Unlinks_When_The_Placement_Is_No_Longer_Docked()
    {
        JSInterop.Setup<bool>("tmGantt.linkScroll", _ => true).SetResult(true);
        var cut = RenderView(p => p
            .Add(c => c.Docked, true)
            .Add(c => c.RangeStart, new DateTime(2024, 6, 3))
            .Add(c => c.RangeEnd, new DateTime(2024, 6, 9)));
        cut.WaitForAssertion(() => JSInterop.Invocations.Select(i => i.Identifier).Should().Contain("tmGantt.linkScroll"));

        cut.Render(p => p.Add(c => c.Docked, false));

        cut.WaitForAssertion(() => JSInterop.Invocations.Select(i => i.Identifier).Should().Contain("tmGantt.unlinkScroll"));
    }

    [Fact]
    public void TmGanttWorkloadView_Truncated_Range_Shows_A_Note_And_Links_No_Scroll()
    {
        var cut = RenderView(p => p
            .Add(c => c.Docked, true)
            .Add(c => c.RangeStart, new DateTime(2024, 1, 1))
            .Add(c => c.RangeEnd, new DateTime(2025, 12, 31)));

        cut.FindAll(".tm-gantt__workload-header-cell:not(.tm-gantt__workload-assignee-header)").Count.Should().Be(366);
        cut.Find("[data-testid='workload-range-truncated']").TextContent.Should().Contain("366");
        JSInterop.Invocations.Select(i => i.Identifier).Should().NotContain("tmGantt.linkScroll",
            "a truncated axis is shorter than the timeline: the ratio mapping would put the two out of step");
    }

    [Fact]
    public void TmGanttWorkloadView_Range_Within_The_Cap_Shows_No_Truncation_Note()
    {
        var cut = RenderView(p => p
            .Add(c => c.Docked, true)
            .Add(c => c.RangeStart, new DateTime(2024, 1, 1))
            .Add(c => c.RangeEnd, new DateTime(2024, 12, 31)));

        cut.FindAll("[data-testid='workload-range-truncated']").Should().BeEmpty("366 days (a leap year) fit the cap");
    }
    private IRenderedComponent<TmGanttWorkloadView> RenderView(Action<ComponentParameterCollectionBuilder<TmGanttWorkloadView>>? configure = null) =>
        Render<TmGanttWorkloadView>(p =>
        {
            p.Add(c => c.Tasks, new List<TmWorkItem> { Task("1", "Design", new DateTime(2024, 6, 3), new DateTime(2024, 6, 3), assignee: "u1") });
            p.Add(c => c.WorkingSchedule, new WorkingSchedule { NonWorkingDaysOfWeek = [] });
            p.Add(c => c.Today, new DateTime(2024, 6, 3));
            configure?.Invoke(p);
        });

    [Fact]
    public void TmGanttWorkloadView_Continuous_Date_Axis_From_Gantt_Range()
    {
        var cut = RenderView(p => p
            .Add(c => c.Docked, true)
            .Add(c => c.RangeStart, new DateTime(2024, 6, 3))
            .Add(c => c.RangeEnd, new DateTime(2024, 6, 9)));

        for (var day = 3; day <= 9; day++)
        {
            cut.Find($"[data-testid='workload-cell-u1-2024-06-{day:00}']").Should().NotBeNull($"day {day} is on the axis although only the 3rd has an allocation");
        }

        cut.FindAll(".tm-gantt__workload-header-cell:not(.tm-gantt__workload-assignee-header)").Count.Should().Be(7);
        cut.Find("[data-testid='workload-cell-u1-2024-06-05']").ClassList.Should().Contain("tm-gantt__workload-cell--empty");
    }

    [Fact]
    public void TmGanttWorkloadView_View_Placement_Keeps_The_Sparse_Window()
    {
        var cut = RenderView();

        cut.FindAll("[data-testid^='workload-cell-u1-']").Count.Should().Be(1, "the non-docked view lists only the days with an entry, as before");
        cut.FindAll("[data-testid='workload-mode-hours']").Should().BeEmpty("the segment belongs to the docked header");
    }

    [Fact]
    public void TmGanttWorkloadView_DisplayMode_Toggle_Raises_DisplayModeChanged()
    {
        var raised = new List<WorkloadDisplayMode>();
        var cut = RenderView(p => p
            .Add(c => c.Docked, true)
            .Add(c => c.RangeStart, new DateTime(2024, 6, 3))
            .Add(c => c.RangeEnd, new DateTime(2024, 6, 9))
            .Add(c => c.DisplayModeChanged, EventCallback.Factory.Create<WorkloadDisplayMode>(this, m => raised.Add(m))));

        cut.Find("[data-testid='workload-mode-hours']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='workload-mode-percent']").Click();
        cut.Find("[data-testid='workload-mode-hours']").Click();

        raised.Should().Equal(WorkloadDisplayMode.Percentage, WorkloadDisplayMode.Hours);
        cut.Find("[data-testid='workload-mode-hours']").TextContent.Trim().Should().Be("Hours");
        cut.Find("[data-testid='workload-mode-percent']").TextContent.Trim().Should().Be("Percent");
    }

    [Fact]
    public void TmGanttWorkloadView_Legend_And_Range_Info()
    {
        var cut = RenderView(p => p
            .Add(c => c.Docked, true)
            .Add(c => c.RangeStart, new DateTime(2024, 6, 3))
            .Add(c => c.RangeEnd, new DateTime(2024, 6, 9)));

        cut.Find(".tm-gantt__workload-title").TextContent.Trim().Should().Be("Workload View");
        var legend = cut.FindAll(".tm-gantt__workload-legend-item").Select(i => i.TextContent.Trim()).ToList();
        legend.Should().Equal("Within capacity", "Overloaded", "No allocation");

        var info = cut.Find("[data-testid='workload-range-info']").TextContent;
        info.Should().Contain(new DateTime(2024, 6, 3).ToString("d", En)).And.Contain(new DateTime(2024, 6, 9).ToString("d", En));
    }

    // ═══════════════════════════════════════════════════════════════
    // Shared history list
    // ═══════════════════════════════════════════════════════════════

    private static List<TmActivityEntry> History() =>
    [
        new()
        {
            Id = "e1", Action = "StatusChanged", Timestamp = new DateTimeOffset(2024, 6, 9, 10, 0, 0, TimeSpan.Zero),
            EntityRef = new TmEntityRef { EntityType = "task", EntityId = "1" },
            Actor = new TmUserRef { Id = "a", DisplayName = "Alice" }, Before = "Open", After = "Done",
        },
        new()
        {
            Id = "e2", Action = "Created", Timestamp = new DateTimeOffset(2024, 6, 8, 10, 0, 0, TimeSpan.Zero),
            EntityRef = new TmEntityRef { EntityType = "task", EntityId = "2" },
        },
        new()
        {
            Id = "e3", Action = "DatesChanged", Timestamp = new DateTimeOffset(2024, 6, 7, 10, 0, 0, TimeSpan.Zero),
            EntityRef = new TmEntityRef { EntityType = "task", EntityId = "gone" },
        },
    ];

    private static List<TmWorkItem> HistoryTasks() =>
    [
        Task("1", "Design", new DateTime(2024, 6, 1), new DateTime(2024, 6, 10)),
        Task("2", "Build", new DateTime(2024, 6, 11), new DateTime(2024, 6, 20)),
    ];

    [Fact]
    public void TmGanttHistoryList_Shows_Task_Title_And_Type_Icon()
    {
        string? selected = null;
        var cut = Render<TmGanttHistoryList>(p => p
            .Add(c => c.History, History())
            .Add(c => c.Tasks, HistoryTasks())
            .Add(c => c.OnTaskSelected, EventCallback.Factory.Create<string>(this, id => selected = id)));

        var rows = cut.FindAll(".tm-gantt__history-entry");
        rows.Count.Should().Be(3);

        rows[0].QuerySelector(".tm-gantt__history-entry-task")!.TextContent.Trim().Should().Be("Design");
        rows[0].QuerySelector(".tm-gantt__history-entry-icon svg").Should().NotBeNull("every type has an icon in a circle");
        rows[0].QuerySelector(".tm-gantt__history-entry-icon")!.ClassList.Should().Contain("tm-gantt__history-entry-icon--status");
        rows[0].QuerySelector(".tm-gantt__history-entry-type")!.TextContent.Trim().Should().Be("Status changed");
        rows[0].QuerySelector(".tm-gantt__history-entry-author")!.TextContent.Trim().Should().Be("Alice");
        rows[0].QuerySelector(".tm-gantt__history-entry-values")!.TextContent.Should().Contain("Open").And.Contain("Done");
        rows[1].QuerySelector(".tm-gantt__history-entry-icon")!.ClassList.Should().Contain("tm-gantt__history-entry-icon--created");
        rows[1].QuerySelector(".tm-gantt__history-entry-type")!.TextContent.Trim().Should().Be("Created");
        rows[2].QuerySelector(".tm-gantt__history-entry-icon")!.ClassList.Should().Contain("tm-gantt__history-entry-icon--dates");

        rows[0].QuerySelector("button.tm-gantt__history-entry-task")!.Click();
        selected.Should().Be("1", "the task title is a link to the task");

        rows[2].QuerySelector(".tm-gantt__history-entry-task")!.TagName.Should().Be("SPAN", "a task that is no longer in the chart is not a link");
        rows[2].QuerySelector(".tm-gantt__history-entry-task")!.TextContent.Trim().Should().Be("gone");
    }

    [Fact]
    public void TmGanttHistoryList_Time_Is_Relative_With_The_Absolute_Time_As_Title()
    {
        var timestamp = new DateTimeOffset(2024, 6, 9, 10, 0, 0, TimeSpan.Zero);
        var cut = Render<TmGanttHistoryList>(p => p
            .Add(c => c.History, History())
            .Add(c => c.Tasks, HistoryTasks())
            .Add(c => c.TimeZone, TimeZoneInfo.Utc));

        var time = cut.Find(".tm-gantt__history-entry-time");
        time.TagName.Should().Be("TIME");
        time.GetAttribute("title").Should().Be(timestamp.ToString("g", En));
        time.GetAttribute("datetime").Should().Be(timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TmGanttHistoryList_Actions_Are_Opt_In_Small_Secondary_Buttons()
    {
        var plain = Render<TmGanttHistoryList>(p => p.Add(c => c.History, History()).Add(c => c.Tasks, HistoryTasks()));
        plain.FindAll(".tm-gantt__history-entry-actions").Should().BeEmpty();

        var cut = Render<TmGanttHistoryList>(p => p
            .Add(c => c.History, History())
            .Add(c => c.Tasks, HistoryTasks())
            .Add(c => c.ShowActions, true));
        foreach (var id in new[] { "history-timetravel-e1", "history-rollback-e1" })
        {
            var button = cut.Find($"[data-testid='{id}']");
            button.ClassList.Should().Contain("tm-btn-secondary").And.Contain("tm-btn-sm");
        }
    }

    [Fact]
    public void TmGanttHistoryList_Shared_By_Sidebar_And_Drawer()
    {
        var sidebar = Render<TmGantt>(p => p
            .Add(c => c.Items, HistoryTasks())
            .Add(c => c.History, History()));
        sidebar.Find("[data-testid='sidebar-btn-history']").Click();
        sidebar.FindAll("[data-testid='sidebar-panel-history'] .tm-gantt__history-list .tm-gantt__history-entry").Count.Should().Be(3);
        sidebar.FindAll("[data-testid='sidebar-panel-history'] .tm-gantt__history-entry-actions").Should().BeEmpty("the sidebar is a read-only feed");

        var drawer = Render<TmGantt>(p => p
            .Add(c => c.Items, HistoryTasks())
            .Add(c => c.History, History()));
        drawer.Find("[data-testid='gantt-history-btn']").Click();
        drawer.FindAll("[data-testid='gantt-history-drawer'] .tm-gantt__history-list .tm-gantt__history-entry").Count.Should().Be(3);
        drawer.FindAll("[data-testid='gantt-history-drawer'] .tm-gantt__history-entry-actions").Count.Should().Be(3);
    }

    [Fact]
    public void TmGantt_History_Task_Link_Selects_And_Opens_The_Task()
    {
        TmWorkItem? selected = null;
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, HistoryTasks())
            .Add(c => c.History, History())
            .Add(c => c.OnTaskSelected, EventCallback.Factory.Create<TmWorkItem>(this, t => selected = t)));
        cut.Find("[data-testid='gantt-history-btn']").Click();

        cut.Find("[data-testid='gantt-history-drawer'] button.tm-gantt__history-entry-task").Click();

        selected!.Id.Should().Be("1");
        cut.Find(".tm-gantt-task-panel").Should().NotBeNull();
    }

    [Fact]
    public void TmGanttHistoryList_Empty_Shows_Empty_Text()
    {
        var cut = Render<TmGanttHistoryList>(p => p.Add(c => c.History, []));

        cut.Find(".tm-gantt__history-empty").TextContent.Trim().Should().Be("No history yet.");
    }

    // ═══════════════════════════════════════════════════════════════
    // History drawer
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Created", "Created")]
    [InlineData("AssigneeChanged", "Assignment changed")]
    [InlineData("AssigneeAdded", "Assignment changed")]
    [InlineData("DatesChanged", "Dates changed")]
    [InlineData("DeadlineSet", "Dates changed")]
    [InlineData("CommentAdded", "Comment added")]
    [InlineData("TaskChanged", "Task changed")]
    [InlineData("SomethingUnknown", "SomethingUnknown")]
    public void GanttLabels_ChangeType_Is_Localized_For_Every_Known_Action(string action, string expected)
    {
        GanttLabels.ChangeType(BuildEnglishLocalizer(), action).Should().Be(expected);
    }

    [Fact]
    public void GanttLabels_ChangeType_Is_Czech_When_The_Localizer_Is_Czech()
    {
        GanttLabels.ChangeType(BuildCzechLocalizer(), "CommentAdded").Should().NotBe("CommentAdded").And.NotStartWith("[");
    }

    [Fact]
    public void TmGanttHistoryList_CommentAdded_Uses_The_Message_Icon_And_A_Localized_Label()
    {
        TmGanttHistoryList.Visual("CommentAdded").Icon.Should().Be("message-square");
        TmGanttHistoryList.Visual("CommentAdded").CssClass.Should().Be("tm-gantt__history-entry-icon--comment");

        var history = new List<TmActivityEntry>
        {
            new()
            {
                Id = "c1", Action = "CommentAdded", Timestamp = new DateTimeOffset(2024, 6, 9, 10, 0, 0, TimeSpan.Zero),
                EntityRef = new TmEntityRef { EntityType = "task", EntityId = "1" },
            },
        };
        var cut = Render<TmGanttHistoryList>(p => p.Add(c => c.History, history).Add(c => c.Tasks, HistoryTasks()));

        cut.Find(".tm-gantt__history-entry-type").TextContent.Trim().Should().Be("Comment added");
        cut.Find(".tm-gantt__history-entry-icon svg").Should().NotBeNull("the message-square icon exists in the icon registry");
    }

    [Fact]
    public void TmGanttHistoryDrawer_Notes_The_Opener_On_Open_And_Returns_Focus_To_It_On_Close_And_Escape()
    {
        var closed = 0;
        var cut = Render<TmGanttHistoryDrawer>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.History, History())
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed++)));

        cut.WaitForAssertion(() => JSInterop.Invocations.Count(i => i.Identifier == "tmGantt.noteOpener").Should().Be(1,
            "the element that had focus when the drawer opened is the History button that opened it"));

        cut.Find("[data-testid='gantt-history-drawer'] .tm-gantt__dialog-close").Click();
        closed.Should().Be(1);
        JSInterop.Invocations.Count(i => i.Identifier == "tmGantt.restoreOpener").Should().Be(1, "focus goes back before the drawer unmounts");

        cut.Find(".tm-gantt__history-drawer").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        closed.Should().Be(2);
        JSInterop.Invocations.Count(i => i.Identifier == "tmGantt.restoreOpener").Should().Be(2, "Escape returns focus the same way");
    }

    [Fact]
    public void TmGanttHistoryDrawer_Does_Not_Note_An_Opener_While_Closed()
    {
        Render<TmGanttHistoryDrawer>(p => p.Add(c => c.IsOpen, false).Add(c => c.History, History()));

        JSInterop.Invocations.Select(i => i.Identifier).Should().NotContain("tmGantt.noteOpener");
    }
    [Fact]
    public void TmGanttHistoryDrawer_Rollback_Requires_Confirmation()
    {
        var rolledBack = new List<TmActivityEntry>();
        var cut = Render<TmGanttHistoryDrawer>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.History, History())
            .Add(c => c.Tasks, HistoryTasks())
            .Add(c => c.OnRollbackRequested, EventCallback.Factory.Create<TmActivityEntry>(this, e => rolledBack.Add(e))));

        cut.FindAll(".tm-dialog").Should().BeEmpty("no confirmation before the click");
        cut.Find("[data-testid='history-rollback-e1']").Click();

        rolledBack.Should().BeEmpty("the rollback waits for the confirmation");
        cut.Find(".tm-dialog").Should().NotBeNull();
        cut.Find(".tm-dialog-title").TextContent.Trim().Should().Be("Roll back this change?");
        cut.Find(".tm-dialog-message").TextContent.Should().Contain("Design");

        cut.Find(".tm-dialog-btn-cancel").Click();
        rolledBack.Should().BeEmpty();
        cut.FindAll(".tm-dialog").Should().BeEmpty("cancel dismisses the dialog");

        cut.Find("[data-testid='history-rollback-e1']").Click();
        cut.Find(".tm-dialog-btn-ok").Click();

        rolledBack.Should().ContainSingle().Which.Id.Should().Be("e1");
        cut.FindAll(".tm-dialog").Should().BeEmpty();
    }

    [Fact]
    public void TmGanttHistoryDrawer_TimeTravel_Needs_No_Confirmation()
    {
        DateTime? traveled = null;
        var cut = Render<TmGanttHistoryDrawer>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.History, History())
            .Add(c => c.OnTimeTravelRequested, EventCallback.Factory.Create<DateTime>(this, t => traveled = t)));

        cut.Find("[data-testid='history-timetravel-e1']").Click();

        traveled.Should().Be(new DateTime(2024, 6, 9, 10, 0, 0));
    }

    [Fact]
    public void TmGanttHistoryDrawer_Escape_Closes()
    {
        var closed = 0;
        var cut = Render<TmGanttHistoryDrawer>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.History, History())
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed++)));

        cut.Find(".tm-gantt__history-drawer").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        closed.Should().Be(0);
        cut.Find(".tm-gantt__history-drawer").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        closed.Should().Be(1);
    }

    [Fact]
    public void TmGanttHistoryDrawer_Positioned_Relative_To_Gantt_Root()
    {
        GanttCss.Property("TmGantt.razor.css", ".tm-gantt", "position").Should().Be("relative", "the drawer is positioned against the component");
        GanttCss.Property("TmGanttHistoryDrawer.razor.css", ".tm-gantt__history-drawer", "position").Should().Be("absolute");
        GanttCss.Property("TmGanttHistoryDrawer.razor.css", ".tm-gantt__history-drawer--viewport", "position").Should().Be("fixed",
            "the viewport placement is the opt-in");
    }

    [Fact]
    public void TmGanttHistoryDrawer_Placement_Is_A_Modifier_Class()
    {
        var component = Render<TmGanttHistoryDrawer>(p => p.Add(c => c.IsOpen, true).Add(c => c.History, []));
        component.Find(".tm-gantt__history-drawer").ClassList.Should().NotContain("tm-gantt__history-drawer--viewport");

        var viewport = Render<TmGanttHistoryDrawer>(p => p
            .Add(c => c.IsOpen, true).Add(c => c.History, []).Add(c => c.Placement, GanttHistoryDrawerPlacement.Viewport));
        viewport.Find(".tm-gantt__history-drawer").ClassList.Should().Contain("tm-gantt__history-drawer--viewport");

        var gantt = Render<TmGantt>(p => p
            .Add(c => c.Items, HistoryTasks())
            .Add(c => c.HistoryDrawerPlacement, GanttHistoryDrawerPlacement.Viewport));
        gantt.Find("[data-testid='gantt-history-btn']").Click();
        gantt.Find(".tm-gantt__history-drawer").ClassList.Should().Contain("tm-gantt__history-drawer--viewport");
    }

    // ═══════════════════════════════════════════════════════════════
    // GA3 – reports
    // ═══════════════════════════════════════════════════════════════

    private static List<GanttReport> Reports() =>
    [
        new() { Id = "r1", Name = "Status", Type = GanttReportType.StatusSummary },
        new() { Id = "r2", Name = "Time", Type = GanttReportType.TimeSpent },
        new() { Id = "r3", Name = "People", Type = GanttReportType.ResourceUtilization },
        new() { Id = "r4", Name = "Money", Type = GanttReportType.BudgetOverview },
        new() { Id = "r5", Name = "Milestones", Type = GanttReportType.MilestoneProgress },
    ];

    [Fact]
    public void TmGanttReportsPanel_Card_Shows_Type_Icon()
    {
        var cut = Render<TmGanttReportsPanel>(p => p.Add(c => c.Reports, Reports()));

        var icons = cut.FindAll(".tm-gantt-reports-panel__item").Select(i => i.QuerySelector(".tm-gantt-reports-panel__item-icon svg")).ToList();
        icons.Should().OnlyContain(svg => svg != null);
        cut.FindAll(".tm-gantt-reports-panel__item").Select(i => i.QuerySelector(".tm-gantt-reports-panel__item-icon")!.GetAttribute("data-icon"))
            .Should().Equal("bar-chart-2", "clock", "users", "wallet", "flag");
        cut.Find("[data-testid='report-run-r1']").ClassList.Should().Contain("tm-btn-secondary").And.Contain("tm-btn-sm");
    }

    [Fact]
    public void TmGanttReportsPanel_Kebab_Shown_Only_With_Edit_Or_Delete_Callback()
    {
        var none = Render<TmGanttReportsPanel>(p => p.Add(c => c.Reports, Reports()));
        none.FindAll("[data-testid^='report-menu-']").Should().BeEmpty("a menu with no callback is dead UI");

        var editOnly = Render<TmGanttReportsPanel>(p => p
            .Add(c => c.Reports, Reports())
            .Add(c => c.OnReportEdit, EventCallback.Factory.Create<GanttReport>(this, _ => { })));
        editOnly.FindAll("[data-testid^='report-menu-']").Count.Should().Be(5);
        editOnly.Find("[data-testid='report-menu-r1'] .tm-dropdown-trigger").Click();
        editOnly.FindAll(".tm-dropdown-item").Select(i => i.TextContent.Trim()).Should().Equal("Edit");
    }

    [Fact]
    public void TmGanttReportsPanel_Kebab_Items_Follow_The_Supplied_Callbacks()
    {
        var calls = new List<string>();
        var cut = Render<TmGanttReportsPanel>(p => p
            .Add(c => c.Reports, Reports())
            .Add(c => c.OnReportEdit, EventCallback.Factory.Create<GanttReport>(this, r => calls.Add("edit:" + r.Id)))
            .Add(c => c.OnReportDuplicate, EventCallback.Factory.Create<GanttReport>(this, r => calls.Add("dup:" + r.Id)))
            .Add(c => c.OnReportDelete, EventCallback.Factory.Create<GanttReport>(this, r => calls.Add("del:" + r.Id))));

        cut.Find("[data-testid='report-menu-r2'] .tm-dropdown-trigger").Click();
        cut.FindAll(".tm-dropdown-item").Select(i => i.TextContent.Trim()).Should().Equal("Edit", "Duplicate", "Delete");

        cut.FindAll(".tm-dropdown-item")[0].Click();
        cut.WaitForAssertion(() => calls.Should().Equal("edit:r2"));
        cut.Find("[data-testid='report-menu-r2'] .tm-dropdown-trigger").Click();
        // Re-opening the kebab re-renders the overlay panel asynchronously on a loaded runner (CI's
        // locale lane runs as a second lane on the same machine), so the menu items are only queried
        // once the reopen render flushed - a bare FindAll raced it and indexed an empty menu.
        cut.WaitForAssertion(() => cut.FindAll(".tm-dropdown-item").Should().HaveCount(3));
        cut.FindAll(".tm-dropdown-item")[1].Click();
        cut.WaitForAssertion(() => calls.Should().Equal("edit:r2", "dup:r2"));
        cut.Find("[data-testid='report-menu-r2'] .tm-dropdown-trigger").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-dropdown-item").Should().HaveCount(3));
        cut.FindAll(".tm-dropdown-item")[2].Click();

        cut.WaitForAssertion(() => calls.Should().Equal("edit:r2", "dup:r2", "del:r2"));
    }

    [Fact]
    public void TmGantt_Forwards_Report_Callbacks_And_Shows_The_Panel_On_The_Right_Instead_Of_The_Task_Panel()
    {
        var edited = new List<string>();
        var first = Task("1", "Design", new DateTime(2024, 6, 1), new DateTime(2024, 6, 10));
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, new List<TmWorkItem> { first })
            .Add(c => c.SelectedTask, first)
            .Add(c => c.ShowTaskPanel, true)
            .Add(c => c.Reports, Reports())
            .Add(c => c.OnReportEdit, EventCallback.Factory.Create<GanttReport>(this, r => edited.Add(r.Id))));

        cut.FindAll(".tm-gantt-task-panel").Count.Should().Be(1);

        cut.Find("[data-testid='sidebar-btn-reports']").Click();

        cut.FindAll(".tm-gantt-task-panel").Should().BeEmpty("only one right-hand panel at a time");
        var panel = cut.Find("[data-testid='gantt-reports-panel']");
        panel.PreviousElementSibling!.ClassList.Should().Contain("tm-gantt__main", "the reports inspector sits to the right of the chart");

        cut.Find("[data-testid='report-menu-r1'] .tm-dropdown-trigger").Click();
        cut.Find(".tm-dropdown-item").Click();
        cut.WaitForAssertion(() => edited.Should().Equal("r1"));

        cut.Find("[data-testid='sidebar-btn-reports']").Click();
        cut.FindAll(".tm-gantt-task-panel").Count.Should().Be(1, "closing the reports brings the task panel back");
    }

    // ═══════════════════════════════════════════════════════════════
    // Portfolio
    // ═══════════════════════════════════════════════════════════════

    private static List<GanttPortfolioProject> Projects() =>
    [
        new()
        {
            Id = "p1", Name = "Alpha",
            Tasks =
            [
                Task("a", "Short", new DateTime(2024, 4, 1), new DateTime(2024, 4, 11), percent: 100),
                Task("b", "Long", new DateTime(2024, 4, 11), new DateTime(2024, 5, 31), percent: 0),
            ],
        },
        new() { Id = "p2", Name = "Beta", Tasks = [Task("c", "Only", new DateTime(2024, 6, 1), new DateTime(2024, 6, 5), percent: 40)] },
    ];

    [Fact]
    public void TmGanttPortfolio_Card_Collapsible_With_AriaExpanded()
    {
        var cut = Render<TmGanttPortfolio>(p => p.Add(c => c.Projects, Projects()));

        var toggle = cut.Find("[data-testid='portfolio-toggle-p1']");
        toggle.GetAttribute("aria-expanded").Should().Be("true");
        var controls = toggle.GetAttribute("aria-controls");
        cut.Find($"[id='{controls}']").Should().NotBeNull("aria-controls points at the card body");
        cut.FindAll("[data-testid='portfolio-project-p1'] .tm-gantt__tree-row").Should().NotBeEmpty();

        toggle.Click();

        cut.Find("[data-testid='portfolio-toggle-p1']").GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll("[data-testid='portfolio-project-p1'] .tm-gantt__tree-row").Should().BeEmpty("a collapsed card hides its Gantt");
        cut.FindAll("[data-testid='portfolio-project-p2'] .tm-gantt__tree-row").Should().NotBeEmpty("other cards are untouched");

        cut.Find("[data-testid='portfolio-toggle-p1']").Click();
        cut.Find("[data-testid='portfolio-toggle-p1']").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void TmGanttPortfolio_Card_Shows_Range_Count_And_Weighted_Progress()
    {
        var cut = Render<TmGanttPortfolio>(p => p.Add(c => c.Projects, Projects()));

        var card = cut.Find("[data-testid='portfolio-project-p1']");
        card.QuerySelector(".tm-gantt-portfolio__project-range")!.TextContent.Should()
            .Contain(new DateTime(2024, 4, 1).ToString("d", En)).And.Contain(new DateTime(2024, 5, 31).ToString("d", En));
        card.QuerySelector(".tm-gantt-portfolio__project-count")!.TextContent.Trim().Should().Be("Tasks: 2");

        // 10 days at 100 % and 50 days at 0 % -> (10 * 100 + 50 * 0) / 60 = 16.67 -> 17
        card.QuerySelector("[role='progressbar']").Should().NotBeNull();
        card.QuerySelector(".tm-progress-bar__label")!.TextContent.Trim().Should().Be("17%");
        TmGanttPortfolio.WeightedProgress(Projects()[0]).Should().BeApproximately(16.67, 0.01);
        TmGanttPortfolio.WeightedProgress(Projects()[1]).Should().BeApproximately(40, 0.01);
    }

    [Fact]
    public void TmGanttPortfolio_Nested_Gantt_Hides_Toolbar_And_Sidebar()
    {
        var cut = Render<TmGanttPortfolio>(p => p.Add(c => c.Projects, Projects()));

        cut.FindAll(".tm-gantt").Should().NotBeEmpty();
        cut.FindAll(".tm-gantt__toolbar").Should().BeEmpty();
        cut.FindAll(".tm-gantt__sidebar").Should().BeEmpty();
        cut.FindAll(".tm-gantt__panel-tabs").Should().BeEmpty();

        var standalone = Render<TmGantt>(p => p.Add(c => c.Items, new List<TmWorkItem> { Task("1", "x", DateTime.Today, DateTime.Today.AddDays(1)) }));
        standalone.FindAll(".tm-gantt__toolbar").Should().NotBeEmpty("ShowToolbar defaults to true");
        standalone.Instance.ShowToolbar.Should().BeTrue();
    }

    [Fact]
    public void TmGantt_ShowToolbar_False_Hides_The_Toolbar_Only()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, new List<TmWorkItem> { Task("1", "x", DateTime.Today, DateTime.Today.AddDays(1)) })
            .Add(c => c.ShowToolbar, false));

        cut.FindAll(".tm-gantt__toolbar").Should().BeEmpty();
        cut.FindAll("[data-testid='gantt-sidebar']").Should().NotBeEmpty("the sidebar is switched off by ShowSidebar, not by ShowToolbar");
        cut.FindAll(".tm-gantt__tree-row").Should().NotBeEmpty();
    }

    [Fact]
    public void TmGanttPortfolio_ProjectActions_Slot_And_OnProjectSelected()
    {
        GanttPortfolioProject? selected = null;
        var cut = Render<TmGanttPortfolio>(p => p
            .Add(c => c.Projects, Projects())
            .Add(c => c.OnProjectSelected, EventCallback.Factory.Create<GanttPortfolioProject>(this, project => selected = project))
            .Add(c => c.ProjectActions, project => b => b.AddMarkupContent(0, $"<button class=\"slot-btn\">⋯ {project.Name}</button>")));

        var actions = cut.FindAll(".tm-gantt-portfolio__project-actions .slot-btn");
        actions.Select(a => a.TextContent.Trim()).Should().Equal("⋯ Alpha", "⋯ Beta");

        cut.Find("[data-testid='portfolio-project-p2'] .tm-gantt-portfolio__project-name").Click();
        selected!.Id.Should().Be("p2");
    }

    [Fact]
    public void TmGanttPortfolio_Without_Select_Callback_Name_Is_Plain_Text()
    {
        var cut = Render<TmGanttPortfolio>(p => p.Add(c => c.Projects, Projects()));

        cut.Find("[data-testid='portfolio-project-p1'] .tm-gantt-portfolio__project-name").TagName.Should().Be("SPAN");
        cut.FindAll(".tm-gantt-portfolio__project-actions").Should().BeEmpty();
    }

    [Fact]
    public void TmGanttPortfolio_ProjectMaxHeight_Is_Configurable()
    {
        var cut = Render<TmGanttPortfolio>(p => p
            .Add(c => c.Projects, Projects())
            .Add(c => c.ProjectMaxHeight, "400px"));

        cut.Find("[data-testid='portfolio-project-p1'] .tm-gantt-portfolio__project-summary").GetAttribute("style").Should().Contain("max-height: 400px");

        var def = Render<TmGanttPortfolio>(p => p.Add(c => c.Projects, Projects()));
        def.Find("[data-testid='portfolio-project-p1'] .tm-gantt-portfolio__project-summary").HasAttribute("style").Should().BeFalse("the stylesheet default stands");
    }
}
