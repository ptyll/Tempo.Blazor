using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TDD tests for Phase 5: Enterprise features.
/// Written BEFORE implementation (Red → Green → Refactor).
/// </summary>
public class TmGanttPhase5Tests : LocalizationTestBase
{
    private static TmWorkItem MakeTask(string id, string? assigneeId = null,
        DateTime? start = null, DateTime? end = null)
    {
        var t = new TmWorkItem
        {
            Id = id,
            Title = $"Task {id}",
            Start = start ?? new DateTime(2024, 1, 1),
            End   = end   ?? new DateTime(2024, 1, 5)
        };
        if (assigneeId is not null)
            t.Assignees.Add(new TmWorkItemAssignee { Id = assigneeId, Name = $"User {assigneeId}" });
        return t;
    }

    // ═══════════════════════════════════════════════════════════════
    // F5.1 – Workload Panel
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void WorkloadEntry_Record_Has_Required_Properties()
    {
        var entry = new WorkloadEntry(
            AssigneeId: "u1",
            Date: new DateTime(2024, 1, 15),
            AllocatedHours: 8.0,
            CapacityHours: 8.0);

        entry.AssigneeId.Should().Be("u1");
        entry.Date.Should().Be(new DateTime(2024, 1, 15));
        entry.AllocatedHours.Should().Be(8.0);
        entry.CapacityHours.Should().Be(8.0);
    }

    [Fact]
    public void WorkloadCalculator_Calculate_Returns_Entries_For_Each_AssigneeDay()
    {
        var schedule = new WorkingSchedule
        {
            WorkDayStartHour = 8,
            WorkDayEndHour = 16,
            NonWorkingDaysOfWeek = [DayOfWeek.Saturday, DayOfWeek.Sunday]
        };
        var tasks = new List<TmWorkItem>
        {
            MakeTask("t1", "u1", new DateTime(2024, 1, 1), new DateTime(2024, 1, 3)),
            MakeTask("t2", "u1", new DateTime(2024, 1, 1), new DateTime(2024, 1, 3))
        };

        var entries = WorkloadCalculator.Calculate(tasks, schedule);

        entries.Should().NotBeEmpty();
        entries.Should().AllSatisfy(e => e.AssigneeId.Should().Be("u1"));
    }

    [Fact]
    public void WorkloadEntry_IsOverloaded_When_AllocatedHours_Exceeds_CapacityHours()
    {
        var overloaded = new WorkloadEntry("u1", new DateTime(2024, 1, 1), 10.0, 8.0);
        var normal     = new WorkloadEntry("u1", new DateTime(2024, 1, 1), 6.0,  8.0);

        (overloaded.AllocatedHours > overloaded.CapacityHours).Should().BeTrue();
        (normal.AllocatedHours > normal.CapacityHours).Should().BeFalse();
    }

    [Fact]
    public void TmGantt_Has_ShowWorkloadPanel_Parameter()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.ShowWorkloadPanel, true));

        cut.Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_Has_WorkloadDisplayMode_Parameter()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.WorkloadDisplayMode, WorkloadDisplayMode.Percentage));

        cut.Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_In_Workload_View_Renders_WorkloadView_Component()
    {
        var tasks = new List<TmWorkItem>
        {
            MakeTask("1", "u1", new DateTime(2024, 1, 1), new DateTime(2024, 1, 5))
        };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.View, GanttView.Workload));

        cut.Find("[data-testid='gantt-workload-view']").Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_In_Workload_View_Hides_Timeline()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1", "u1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.View, GanttView.Workload));

        cut.FindAll("[data-testid='gantt-timeline']").Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // F5.2 – Real-time Sync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void IGanttRealtimeConnection_Interface_Exists()
    {
        var type = typeof(IGanttRealtimeConnection);
        type.IsInterface.Should().BeTrue();
    }

    [Fact]
    public void IGanttRealtimeConnection_Has_OnTaskUpdated_Event_And_SendTaskUpdate()
    {
        var type = typeof(IGanttRealtimeConnection);

        type.GetEvent("OnTaskUpdated").Should().NotBeNull();
        type.GetMethod("SendTaskUpdate").Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_Has_RealtimeConnection_Parameter()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.RealtimeConnection, (IGanttRealtimeConnection?)null));

        cut.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // F5.3 – Notifications
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmNotificationPreferences_Has_Required_Properties()
    {
        var settings = new TmNotificationPreferences
        {
            EmailOnAssign  = true,
            EmailOnMention = false,
            PushOnAssign   = true,
            PushOnMention  = true,
            PushOnDeadline = false
        };

        settings.EmailOnAssign.Should().BeTrue();
        settings.EmailOnMention.Should().BeFalse();
        settings.PushOnAssign.Should().BeTrue();
        settings.PushOnMention.Should().BeTrue();
        settings.PushOnDeadline.Should().BeFalse();
    }

    [Fact]
    public void TmGantt_Has_NotificationSettings_Parameter()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.NotificationSettings, new TmNotificationPreferences()));

        cut.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // F5.4 – List View
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttView_Has_List_Value()
    {
        var values = Enum.GetValues<GanttView>();
        values.Should().Contain(GanttView.List);
    }

    [Fact]
    public void TmGantt_In_List_View_Hides_Timeline()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.View, GanttView.List));

        cut.FindAll("[data-testid='gantt-timeline']").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_In_List_View_Renders_Tree_Grid()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.View, GanttView.List));

        cut.Find("[data-testid='gantt-tree']").Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // F5.5 – Calendar View
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttView_Has_Calendar_Value()
    {
        var values = Enum.GetValues<GanttView>();
        values.Should().Contain(GanttView.Calendar);
    }

    [Fact]
    public void TmGantt_In_Calendar_View_Renders_Calendar_Grid()
    {
        var tasks = new List<TmWorkItem>
        {
            MakeTask("1", null, new DateTime(2024, 1, 10), new DateTime(2024, 1, 15))
        };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.View, GanttView.Calendar));

        cut.Find("[data-testid='gantt-calendar-view']").Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // F5.6 – Board View (Kanban)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttView_Has_Board_Value()
    {
        var values = Enum.GetValues<GanttView>();
        values.Should().Contain(GanttView.Board);
    }

    [Fact]
    public void TmGantt_In_Board_View_Renders_Four_Status_Columns()
    {
        var tasks = new List<TmWorkItem>
        {
            MakeTask("1"), MakeTask("2"), MakeTask("3"), MakeTask("4")
        };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.View, GanttView.Board));

        cut.FindAll("[data-testid^='board-column-']").Should().HaveCount(4);
    }

    [Fact]
    public void TmGantt_Board_View_Has_OnStatusChanged_Callback()
    {
        (string TaskId, TmWorkItemStatus NewStatus)? received = null;
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.View, GanttView.Board)
            .Add(x => x.OnStatusChanged, EventCallback.Factory.Create<(string, TmWorkItemStatus)>(
                this, args => received = args)));

        cut.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // F5.7 – Portfolio View
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttPortfolioProject_Class_Has_Required_Properties()
    {
        var proj = new GanttPortfolioProject
        {
            Id           = "p1",
            Name         = "Project Alpha",
            Tasks        = [],
            Dependencies = []
        };

        proj.Id.Should().Be("p1");
        proj.Name.Should().Be("Project Alpha");
        proj.Tasks.Should().BeEmpty();
        proj.Dependencies.Should().BeEmpty();
    }

    [Fact]
    public void TmGanttPortfolio_Component_Exists_And_Has_Projects_Parameter()
    {
        var projects = new List<GanttPortfolioProject>
        {
            new() { Id = "p1", Name = "Alpha", Tasks = [], Dependencies = [] },
            new() { Id = "p2", Name = "Beta",  Tasks = [], Dependencies = [] }
        };

        var cut = Render<TmGanttPortfolio>(p =>
            p.Add(x => x.Projects, projects));

        cut.Should().NotBeNull();
    }

    [Fact]
    public void TmGanttPortfolio_Renders_One_Row_Per_Project()
    {
        var projects = new List<GanttPortfolioProject>
        {
            new() { Id = "p1", Name = "Alpha", Tasks = [], Dependencies = [] },
            new() { Id = "p2", Name = "Beta",  Tasks = [], Dependencies = [] }
        };

        var cut = Render<TmGanttPortfolio>(p =>
            p.Add(x => x.Projects, projects));

        cut.FindAll("[data-testid^='portfolio-project-']").Should().HaveCount(2);
        // Rows from the project tasks are asserted in TmGanttPhase1FixesTests
        // (TmGanttPortfolio_Renders_Task_Rows_From_Project_Tasks) - this test used empty task lists,
        // which is how the always-empty portfolio (B22) went unnoticed.
    }

    // ═══════════════════════════════════════════════════════════════
    // F5.8 – Dashboard View
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttView_Has_Dashboard_Value()
    {
        var values = Enum.GetValues<GanttView>();
        values.Should().Contain(GanttView.Dashboard);
    }

    [Fact]
    public void TmGantt_In_Dashboard_View_Renders_Four_Widget_Containers()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1"), MakeTask("2") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.View, GanttView.Dashboard));

        cut.FindAll("[data-testid^='dashboard-widget-']").Should().HaveCount(4);
    }

    // ═══════════════════════════════════════════════════════════════
    // UT-5.1.5 – Overloaded assignee red icon in tree row (F5.1 + F2.7)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Tree_Row_Shows_Overload_Icon_When_Assignee_Is_Overloaded()
    {
        var schedule = new WorkingSchedule
        {
            WorkDayStartHour = 8, WorkDayEndHour = 16,
            NonWorkingDaysOfWeek = [DayOfWeek.Saturday, DayOfWeek.Sunday]
        };
        // Two 1-day tasks for u1 on the same Monday → each takes 8h/day, total 16h > 8h capacity
        var tasks = new List<TmWorkItem>
        {
            MakeTask("t1", "u1", new DateTime(2024, 1, 1), new DateTime(2024, 1, 1)),
            MakeTask("t2", "u1", new DateTime(2024, 1, 1), new DateTime(2024, 1, 1))
        };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.ShowWorkloadPanel, true)
            .Add(x => x.WorkingSchedule, schedule));

        cut.FindAll("[data-testid^='overload-icon-']").Should().NotBeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // UT-5.2.2 – Realtime OnTaskUpdated triggers refresh
    // ═══════════════════════════════════════════════════════════════

    private sealed class FakeRealtimeConnection : IGanttRealtimeConnection
    {
        public event Action<TmWorkItem>? OnTaskUpdated;
        public Task SendTaskUpdate(TmWorkItem task) => Task.CompletedTask;
        public void FireTaskUpdated(TmWorkItem task) => OnTaskUpdated?.Invoke(task);
    }

    [Fact]
    public void TmGantt_RealtimeConnection_OnTaskUpdated_Updates_Rendered_Title()
    {
        var task = MakeTask("rt1");
        var tasks = new List<TmWorkItem> { task };
        var conn  = new FakeRealtimeConnection();

        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.RealtimeConnection, conn));

        task.Title = "Realtime Updated Title";
        conn.FireTaskUpdated(task);

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='tree-cell-title-rt1']").TextContent
               .Should().Contain("Realtime Updated Title"));
    }

    // ═══════════════════════════════════════════════════════════════
    // F5.9 – Cost / Budget Tracking
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttTask_Has_BudgetHours_Property()
    {
        var task = new TmWorkItem();
        task.BudgetHours = 40.0;
        task.BudgetHours.Should().Be(40.0);
    }

    [Fact]
    public void GanttTask_Has_ActualCost_Property()
    {
        var task = new TmWorkItem();
        task.ActualCost = 1200.50m;
        task.ActualCost.Should().Be(1200.50m);
    }

    [Fact]
    public void TmWorkItemAssignee_Has_HourlyRate_Property()
    {
        var assignee = new TmWorkItemAssignee();
        assignee.HourlyRate = 75.0m;
        assignee.HourlyRate.Should().Be(75.0m);
    }

    // ═══════════════════════════════════════════════════════════════
    // F5.10 – Resource Calendar / People View
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttView_Has_People_Value()
    {
        var values = Enum.GetValues<GanttView>();
        values.Should().Contain(GanttView.People);
    }

    [Fact]
    public void DateRange_Record_Has_Start_And_End()
    {
        var range = new DateRange(new DateTime(2024, 1, 10), new DateTime(2024, 1, 20));
        range.Start.Should().Be(new DateTime(2024, 1, 10));
        range.End.Should().Be(new DateTime(2024, 1, 20));
    }

    [Fact]
    public void GanttResourceCalendar_Has_Required_Properties()
    {
        var cal = new GanttResourceCalendar
        {
            AssigneeId   = "u1",
            VacationDays = [new DateRange(new DateTime(2024, 1, 10), new DateTime(2024, 1, 15))],
            DaysOff      = [new DateTime(2024, 1, 20)]
        };
        cal.AssigneeId.Should().Be("u1");
        cal.VacationDays.Should().HaveCount(1);
        cal.DaysOff.Should().HaveCount(1);
    }

    [Fact]
    public void TmGantt_Has_ResourceCalendars_Parameter()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.ResourceCalendars, new List<GanttResourceCalendar>()));

        cut.Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_In_People_View_Renders_One_Row_Per_Assignee()
    {
        var tasks = new List<TmWorkItem>
        {
            MakeTask("t1", "u1", new DateTime(2024, 1, 1), new DateTime(2024, 1, 5)),
            MakeTask("t2", "u2", new DateTime(2024, 1, 1), new DateTime(2024, 1, 5)),
            MakeTask("t3", "u1", new DateTime(2024, 1, 3), new DateTime(2024, 1, 8))
        };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.View, GanttView.People));

        // u1 and u2 → 2 rows
        cut.FindAll("[data-testid^='people-row-']").Should().HaveCount(2);
    }

    // ═══════════════════════════════════════════════════════════════
    // F5.11 – Virtual Resources
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmWorkItemAssignee_Has_IsVirtual_Property_Defaulting_To_False()
    {
        var assignee = new TmWorkItemAssignee();
        assignee.IsVirtual.Should().BeFalse();
    }

    [Fact]
    public void TmGantt_Has_OnVirtualResourceAdded_Parameter()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.OnVirtualResourceAdded, EventCallback.Factory.Create<TmWorkItemAssignee>(this, _ => { })));

        cut.Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_Virtual_Assignee_Without_Avatar_Shows_Virtual_Icon_On_Bar()
    {
        var task = MakeTask("t1", null, new DateTime(2024, 1, 1), new DateTime(2024, 1, 5));
        task.Assignees.Add(new TmWorkItemAssignee { Id = "v1", Name = "Virtual Team", IsVirtual = true });
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, new List<TmWorkItem> { task }));

        cut.FindAll("[data-testid^='bar-avatar-virtual-']").Should().NotBeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // F5.12 – Time Tracker (Stopwatch)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttTimeLogEntry_Class_Has_Required_Properties()
    {
        var entry = new GanttTimeLogEntry
        {
            Id         = "e1",
            TaskId     = "t1",
            AssigneeId = "u1",
            StartedAt  = new DateTime(2024, 1, 1, 9, 0, 0),
            StoppedAt  = new DateTime(2024, 1, 1, 10, 0, 0),
            Notes      = "Morning session"
        };
        entry.Id.Should().Be("e1");
        entry.TaskId.Should().Be("t1");
        entry.AssigneeId.Should().Be("u1");
        entry.StoppedAt.Should().NotBeNull();
        entry.Notes.Should().Be("Morning session");
    }

    [Fact]
    public void GanttTask_Has_TimeLog_Property_Defaulting_Empty()
    {
        var task = new TmWorkItem();
        task.TimeLog.Should().NotBeNull();
        task.TimeLog.Should().BeEmpty();
    }

    [Fact]
    public void GanttHelper_CalculateTotalLoggedHours_Returns_Sum_Of_Completed_Entries()
    {
        var log = new List<GanttTimeLogEntry>
        {
            new() { StartedAt = new DateTime(2024,1,1,9,0,0), StoppedAt = new DateTime(2024,1,1,11,0,0) },
            new() { StartedAt = new DateTime(2024,1,2,9,0,0), StoppedAt = new DateTime(2024,1,2,10,30,0) }
        };
        var total = GanttHelper.CalculateTotalLoggedHours(log);
        total.Should().BeApproximately(3.5, 0.001);
    }

    [Fact]
    public void TmGantt_Has_ActiveTimerTaskId_And_Timer_Callbacks()
    {
        var tasks = new List<TmWorkItem> { MakeTask("t1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.ActiveTimerTaskId, "t1")
            .Add(x => x.OnTimerStarted, EventCallback.Factory.Create<string>(this, _ => { }))
            .Add(x => x.OnTimerStopped, EventCallback.Factory.Create<(string, GanttTimeLogEntry)>(this, _ => { })));

        cut.Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_Bar_With_Active_Timer_Has_Timer_Active_Class()
    {
        var tasks = new List<TmWorkItem>
        {
            MakeTask("t1", null, new DateTime(2024, 1, 1), new DateTime(2024, 1, 5))
        };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.ActiveTimerTaskId, "t1"));

        cut.Find("[data-testid='task-bar-t1']").ClassList.Should().Contain("tm-gantt__bar--timer-active");
    }

    // ═══════════════════════════════════════════════════════════════
    // F5.13 – Left Sidebar Navigation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Has_ShowSidebar_Parameter_Defaulting_True()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, tasks));
        // Default ShowSidebar=true → sidebar visible
        cut.Find("[data-testid='gantt-sidebar']").Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_ShowSidebar_False_Hides_Sidebar()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.ShowSidebar, false));

        cut.FindAll("[data-testid='gantt-sidebar']").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_Sidebar_Renders_Five_Icon_Buttons()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.ShowSidebar, true));

        cut.FindAll("[data-testid^='sidebar-btn-']").Should().HaveCount(5);
    }

    [Fact]
    public void GanttSidebarPanel_Enum_Has_Five_Values()
    {
        var values = Enum.GetValues<GanttSidebarPanel>();
        values.Should().HaveCount(5);
    }

    [Fact]
    public void GanttReport_Has_Required_Properties()
    {
        var report = new GanttReport
        {
            Id     = "r1",
            Name   = "Status Report",
            Type   = GanttReportType.StatusSummary,
            Config = new Dictionary<string, string> { ["period"] = "month" }
        };
        report.Id.Should().Be("r1");
        report.Type.Should().Be(GanttReportType.StatusSummary);
    }

    [Fact]
    public void GanttReportType_Enum_Has_Five_Values()
    {
        var values = Enum.GetValues<GanttReportType>();
        values.Should().HaveCount(5);
    }

    [Fact]
    public void TmGantt_Has_Reports_And_OnReportRun_Parameter()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.Reports, new List<GanttReport>())
            .Add(x => x.OnReportRun, EventCallback.Factory.Create<GanttReport>(this, _ => { })));

        cut.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // F5.14 – Group Task Bar Rollup
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttHelper_CalculateGroupBounds_Returns_Min_Start_And_Max_End()
    {
        var children = new List<TmWorkItem>
        {
            new() { Start = new DateTime(2024, 1, 5), End = new DateTime(2024, 1, 10) },
            new() { Start = new DateTime(2024, 1, 1), End = new DateTime(2024, 1, 8) },
            new() { Start = new DateTime(2024, 1, 3), End = new DateTime(2024, 1, 15) }
        };

        var result = GanttHelper.CalculateGroupBounds(children);

        result.Should().NotBeNull();
        result!.Value.MinStart.Should().Be(new DateTime(2024, 1, 1));
        result!.Value.MaxEnd.Should().Be(new DateTime(2024, 1, 15));
    }

    [Fact]
    public void GanttHelper_CalculateGroupBounds_With_Empty_List_Returns_Null()
    {
        var result = GanttHelper.CalculateGroupBounds([]);
        result.Should().BeNull();
    }

    [Fact]
    public void GanttTask_Has_UseManualDates_Property_Defaulting_To_False()
    {
        var task = new TmWorkItem();
        task.UseManualDates.Should().BeFalse();
    }

    [Fact]
    public void TmGantt_Group_Task_With_UseManualDates_False_Bar_Spans_Children()
    {
        var parent = new TmWorkItem
        {
            Id            = "p1", Title = "Parent",
            Start         = new DateTime(2024, 1, 20), End = new DateTime(2024, 1, 25),
            UseManualDates = false
        };
        var child1 = new TmWorkItem
        {
            Id = "c1", Title = "Child 1", ParentId = "p1",
            Start = new DateTime(2024, 1, 1), End = new DateTime(2024, 1, 5)
        };
        var child2 = new TmWorkItem
        {
            Id = "c2", Title = "Child 2", ParentId = "p1",
            Start = new DateTime(2024, 1, 8), End = new DateTime(2024, 1, 12)
        };
        var tasks = new List<TmWorkItem> { parent, child1, child2 };

        var cut = Render<TmGantt>(p => p.Add(x => x.Items, tasks));

        // Parent bar must be rendered (child bounds are within Jan 1-12, not Jan 20-25)
        cut.Find("[data-testid='task-bar-p1']").Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_Group_Task_With_UseManualDates_True_Uses_Own_Dates()
    {
        var parent = new TmWorkItem
        {
            Id            = "p1", Title = "Parent",
            Start         = new DateTime(2024, 1, 1), End = new DateTime(2024, 1, 31),
            UseManualDates = true
        };
        var child = new TmWorkItem
        {
            Id = "c1", Title = "Child", ParentId = "p1",
            Start = new DateTime(2024, 1, 5), End = new DateTime(2024, 1, 10)
        };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, new List<TmWorkItem> { parent, child }));

        cut.Find("[data-testid='task-bar-p1']").Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // UT-5.13.3 – Sidebar click activates / deactivates panel
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Sidebar_Click_Activates_Panel()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.ShowSidebar, true));

        cut.Find("[data-testid='sidebar-btn-tasks']").Click();

        cut.Find("[data-testid='sidebar-btn-tasks']")
           .ClassList.Should().Contain("tm-gantt__sidebar-btn--active");
    }

    [Fact]
    public void TmGantt_Sidebar_Click_Same_Button_Twice_Deactivates_Panel()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.ShowSidebar, true));

        cut.Find("[data-testid='sidebar-btn-tasks']").Click();
        cut.Find("[data-testid='sidebar-btn-tasks']").Click();

        cut.Find("[data-testid='sidebar-btn-tasks']")
           .ClassList.Should().NotContain("tm-gantt__sidebar-btn--active");
    }

    // ═══════════════════════════════════════════════════════════════
    // UT-5.13.6 – Reports panel (RAZOR-5.13.2)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGanttReportsPanel_Renders_Reports_List_And_NewReport_Button()
    {
        var reports = new List<GanttReport>
        {
            new() { Id = "r1", Name = "Monthly Status", Type = GanttReportType.StatusSummary,  Config = [] },
            new() { Id = "r2", Name = "Time Summary",   Type = GanttReportType.TimeSpent,      Config = [] }
        };

        var cut = Render<TmGanttReportsPanel>(p => p
            .Add(x => x.Reports, reports)
            .Add(x => x.OnReportRun, EventCallback.Factory.Create<GanttReport>(this, _ => { }))
            .Add(x => x.OnNewReportRequested, EventCallback.Factory.Create(this, () => { })));

        cut.FindAll("[data-testid^='report-item-']").Should().HaveCount(2);
        cut.Find("[data-testid='reports-new-btn']").Should().NotBeNull();
    }

    [Fact]
    public void TmGanttReportsPanel_RunReport_Button_Fires_OnReportRun()
    {
        GanttReport? fired = null;
        var reports = new List<GanttReport>
        {
            new() { Id = "r1", Name = "Monthly Status", Type = GanttReportType.StatusSummary, Config = [] }
        };

        var cut = Render<TmGanttReportsPanel>(p => p
            .Add(x => x.Reports, reports)
            .Add(x => x.OnReportRun, EventCallback.Factory.Create<GanttReport>(this, r => fired = r)));

        cut.Find("[data-testid='report-run-r1']").Click();

        fired.Should().NotBeNull();
        fired!.Id.Should().Be("r1");
    }

    [Fact]
    public void TmGantt_Shows_ReportsPanel_When_Reports_Sidebar_Activated()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var reports = new List<GanttReport>
        {
            new() { Id = "r1", Name = "Monthly Status", Type = GanttReportType.StatusSummary, Config = [] }
        };

        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.Reports, reports)
            .Add(x => x.ShowSidebar, true));

        cut.Find("[data-testid='sidebar-btn-reports']").Click();

        cut.Find("[data-testid='gantt-reports-panel']").Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // RAZOR-5.3.1 – Notification settings in View Settings
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_ViewSettings_Shows_Notification_Section_When_Settings_Provided()
    {
        var tasks = new List<TmWorkItem> { MakeTask("1") };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.NotificationSettings, new TmNotificationPreferences
            {
                EmailOnAssign  = true,
                PushOnDeadline = true
            }));

        cut.Find("[data-testid='gantt-view-settings-btn']").Click();

        cut.Find("[data-testid='notification-settings-section']").Should().NotBeNull();
    }
}
