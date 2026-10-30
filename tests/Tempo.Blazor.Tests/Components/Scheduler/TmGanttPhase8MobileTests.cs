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
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TmGantt redesign, phase 8 (mobile read-mostly, decision G2): on a container below 768 px the chart is a
/// reading surface — the title column with a two-line cell, the full-width view segment and chips, the
/// <see cref="TmMobileActionBar"/> (Add task / Expand all / Collapse all / More), the "More" bottom sheet
/// (zoom, task panel, history, reports, view settings — never import/export), the task sheet as a
/// read-only detail with editable Status and Percent, the minimal add-task sheet, the filter dialog, the
/// read-only history sheet, the run-only reports sheet, the portfolio cards with a mini-gantt and the
/// workload person list with a utilization bar. Reorder and dependency drag are not offered on mobile.
/// A bUnit render cannot run container queries, so every layout is FORCED with <c>LayoutMode</c>.
/// Culture and clock are pinned: CI runs Linux / UTC / invariant culture plus a non-English ambient lane.
/// </summary>
public class TmGanttPhase8MobileTests : LocalizationTestBase
{
    public TmGanttPhase8MobileTests()
    {
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2024, 6, 12, 10, 0, 0, TimeSpan.Zero)));
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(TimeZoneInfo.Utc));
        JSInterop.Setup<bool>("tmGantt.attachTimeline", _ => true).SetResult(true);
        JSInterop.Setup<bool>("tmGantt.attachTree", _ => true).SetResult(true);
    }

    private static List<TmWorkItem> Tasks() =>
    [
        new() { Id = "1", Title = "Project", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30), IsExpanded = true, PercentComplete = 40 },
        new() { Id = "2", Title = "Design", ParentId = "1", Start = new DateTime(2024, 6, 3), End = new DateTime(2024, 6, 6), PercentComplete = 100 },
        new() { Id = "3", Title = "Development", ParentId = "1", Start = new DateTime(2024, 6, 11), End = new DateTime(2024, 6, 25), PercentComplete = 30 },
    ];

    private IRenderedComponent<TmGantt> RenderGantt(TmLayoutMode mode, Action<ComponentParameterCollectionBuilder<TmGantt>>? configure = null, IReadOnlyList<TmWorkItem>? items = null) =>
        Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, items ?? Tasks());
            p.Add(c => c.LayoutMode, mode);
            configure?.Invoke(p);
        });

    /// <summary>A touch tap in bUnit: the row's pointerup (names the pointer type) followed by its click.</summary>
    private static void TouchTap(AngleSharp.Dom.IElement row)
    {
        row.TriggerEvent("onpointerup", new PointerEventArgs { PointerType = "touch" });
        row.TriggerEvent("onclick", new MouseEventArgs());
    }

    // ── 1. Title column with the two-line cell ─────────────────────────────────────────────────

    [Fact]
    public void TmGantt_Mobile_Shows_Only_Title_Column_With_TwoLine_Cell()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile);

        cut.FindAll(".tm-gantt__tree-header [role='columnheader']").Select(h => h.TextContent.Trim()).Where(t => t.Length > 0)
            .Should().Equal(["Task"], "the mobile tree keeps only the identifying column");

        var cells = cut.FindAll(".tm-gantt__mobile-cell");
        cells.Should().HaveCount(3, "one two-line cell per visible row");
        cells.Select(c => c.QuerySelector(".tm-gantt__mobile-cell-title")!.TextContent.Trim())
            .Should().Equal(["Project", "Design", "Development"]);
        // The second line is the date range of the task ("1.6. – 30.6." in the pinned culture).
        cells.Select(c => c.QuerySelector(".tm-gantt__mobile-cell-range")!.TextContent.Trim())
            .Should().OnlyContain(r => r.Contains("–"), "the range line reads 'start – end'");

        // The desktop tree keeps every visible column and no two-line cell.
        var desktop = RenderGantt(TmLayoutMode.Desktop);
        desktop.FindAll(".tm-gantt__mobile-cell").Should().BeEmpty();
        desktop.FindAll(".tm-gantt__tree-header [role='columnheader']").Should().HaveCountGreaterThan(3);
    }

    [Fact]
    public void TmGantt_Mobile_Row_Height_Is_56_In_Tree_And_Timeline()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile);

        cut.Find(".tm-gantt").GetAttribute("data-density").Should().Be("mobile");
        // 3 rows x 56 px, one consistent height for the tree rows and the timeline rows.
        cut.Find(".tm-gantt__timeline-content").GetAttribute("style").Should().Contain("height: 168px");
        cut.FindAll(".tm-gantt__task-row").Should().OnlyContain(r => r.GetAttribute("style")!.Contains("height: 56px"));
    }

    // ── 2. Full-width segment + chips ───────────────────────────────────────────────────────────

    [Fact]
    public void TmGantt_Mobile_Segment_FullWidth_And_Chips()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile, p => p.Add(c => c.Filters, [new GanttFilter("Title", GanttFilterOperator.Contains, "dev")]));

        // The full-width Day | Week | Month segment replaces the desktop command row.
        var segment = cut.Find(".tm-gantt__segment--full");
        segment.QuerySelectorAll("[data-testid='gantt-view-day'], [data-testid='gantt-view-week'], [data-testid='gantt-view-month']")
            .Should().HaveCount(3);

        // Chips: sort by date | fit to screen | filter (with the active-filter dot).
        cut.Find("[data-testid='gantt-cascade-sort']").Should().NotBeNull();
        cut.Find("[data-testid='gantt-fit-to-screen']").Should().NotBeNull();
        var filterChip = cut.Find("[data-testid='gantt-filter-btn']");
        filterChip.QuerySelector(".tm-gantt__mobile-chip-dot").Should().NotBeNull("an active filter is announced by the dot");

        var noFilter = RenderGantt(TmLayoutMode.Mobile);
        noFilter.Find("[data-testid='gantt-filter-btn']").QuerySelector(".tm-gantt__mobile-chip-dot").Should().BeNull();

        // The legacy command toolbar (groups, export/import, column menu, search) is not part of the mobile chrome.
        cut.FindAll(".tm-gantt__toolbar [role='toolbar']").Should().BeEmpty();
        cut.FindAll("[data-testid='gantt-export-btn'], [data-testid='gantt-import-btn']").Should().BeEmpty();
    }

    // ── 3. The mobile action bar (F5) ───────────────────────────────────────────────────────────

    [Fact]
    public void TmGantt_Mobile_ActionBar_AddTask_ExpandAll_CollapseAll_More()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile);

        var bar = cut.FindComponent<TmMobileActionBar>();
        bar.Instance.Items!.Select(i => i.Id).Should().Equal(["gantt-mobile-add", "gantt-mobile-expand", "gantt-mobile-collapse", "gantt-mobile-more"]);

        // The tablet and the desktop render no action bar.
        RenderGantt(TmLayoutMode.Tablet).FindComponents<TmMobileActionBar>().Should().BeEmpty();
        RenderGantt(TmLayoutMode.Desktop).FindComponents<TmMobileActionBar>().Should().BeEmpty();
    }

    // ── 4. The "More" sheet ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void TmGantt_Mobile_More_Contains_Zoom_Panels_Settings_History_But_Not_Import_Export()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile);

        cut.Find("[data-action-id='gantt-mobile-more']").Click();
        var sheet = cut.Find("[data-testid='gantt-more-sheet']");

        sheet.QuerySelector("[data-testid='gantt-more-zoom-in']").Should().NotBeNull();
        sheet.QuerySelector("[data-testid='gantt-more-zoom-out']").Should().NotBeNull();
        sheet.QuerySelector("[data-testid='gantt-more-task-panel']").Should().NotBeNull();
        sheet.QuerySelector("[data-testid='gantt-more-settings']").Should().NotBeNull();
        sheet.QuerySelector("[data-testid='gantt-more-history']").Should().NotBeNull();
        sheet.QuerySelector("[data-testid='gantt-more-reports']").Should().NotBeNull();

        // Read-mostly: the data commands are not offered anywhere on the mobile chrome.
        sheet.QuerySelector("[data-testid='gantt-export-btn']").Should().BeNull();
        sheet.QuerySelector("[data-testid='gantt-import-btn']").Should().BeNull();

        // "View settings" swaps the sheet body to the settings list.
        cut.Find("[data-testid='gantt-more-settings']").Click();
        cut.Find("[data-testid='vset-show-today']").Should().NotBeNull();
    }

    // ── 5. The read-only task sheet ─────────────────────────────────────────────────────────────

    [Fact]
    public void TmGantt_Mobile_TaskSheet_ReadOnly_Except_Status_And_Percent()
    {
        var updated = new List<TmWorkItem>();
        var statusChanges = new List<(string TaskId, TmWorkItemStatus NewStatus)>();
        var tasks = Tasks();
        var cut = RenderGantt(TmLayoutMode.Mobile, items: tasks, configure: p => p
            .Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto)
            .Add(c => c.OnTaskUpdated, EventCallback.Factory.Create<TmWorkItem>(this, t => updated.Add(t)))
            .Add(c => c.OnStatusChanged, EventCallback.Factory.Create<(string, TmWorkItemStatus)>(this, c => statusChanges.Add(c))));

        TouchTap(cut.FindAll(".tm-gantt__tree-row")[1]);

        // The mobile detail is a FULL-HEIGHT BOTTOM sheet (F2), not the tablet side sheet.
        var sheet = cut.Find(".tm-drawer.tm-gantt__sheet");
        sheet.ClassList.Should().Contain("tm-drawer--bottom");
        cut.Find(".tm-drawer__title").TextContent.Should().Contain("Design");

        // Read-only fields render as text, never as the desktop editor inputs.
        cut.Find("[data-testid='task-mobile-name']").TextContent.Should().Contain("Design");
        cut.Find("[data-testid='task-mobile-start']").Should().NotBeNull();
        cut.Find("[data-testid='task-mobile-end']").Should().NotBeNull();
        cut.FindAll("[data-testid='task-title'] input").Should().BeEmpty("the title, the dates and the rest are read-only on mobile");

        // Status and Percent stay editable.
        cut.FindAll("select[data-testid='task-mobile-status']").Should().ContainSingle();
        cut.FindAll("input[data-testid='task-mobile-percent']").Should().ContainSingle();

        // The localized hint points to a larger screen for full editing.
        cut.Find("[data-testid='task-mobile-hint']").TextContent.Should().Contain("larger screen");

        // The sticky footer: Cancel | Save.
        cut.Find("[data-testid='task-mobile-cancel']").Should().NotBeNull();
        cut.Find("[data-testid='task-mobile-save']").Should().NotBeNull();

        // Editing only the two editable fields and saving raises exactly those changes.
        cut.Find("input[data-testid='task-mobile-percent']").Change("80");
        cut.Find("select[data-testid='task-mobile-status']").Change(TmWorkItemStatus.InProgress.ToString());
        cut.Find("[data-testid='task-mobile-save']").Click();

        updated.Should().ContainSingle(t => t.Id == "2" && t.PercentComplete == 80);
        statusChanges.Should().ContainSingle(c => c.TaskId == "2" && c.NewStatus == TmWorkItemStatus.InProgress);
        updated[0].Title.Should().Be("Design", "nothing else was editable, so nothing else changed");
    }

    [Fact]
    public void TmGantt_Mobile_TaskSheet_Hides_The_Desktop_Editor_Sections()
    {
        var tasks = Tasks();
        tasks[1].Attachments = [new TmAttachment { Id = "a1", FileName = "spec.pdf" }];
        var cut = RenderGantt(TmLayoutMode.Mobile, items: tasks, configure: p => p
            .Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto)
            .Add(c => c.ShowTaskPanel, true)
            .Add(c => c.SelectedTask, tasks[1]));

        // Read-mostly: attachments, comments, dependencies, timer and the action list are not offered.
        cut.FindAll("[data-testid='task-section-attachments']").Should().BeEmpty();
        cut.FindAll("[data-testid='task-section-comments']").Should().BeEmpty();
        cut.FindAll("[data-testid='task-section-dependencies']").Should().BeEmpty();
        cut.FindAll("[data-testid='task-action-move-up']").Should().BeEmpty();
        cut.FindAll(".tm-gantt-task-panel__tabs").Should().BeEmpty("the mobile sheet has no tabs");
    }

    // ── 6. The minimal add-task sheet ───────────────────────────────────────────────────────────

    [Fact]
    public void TmGantt_Mobile_AddTask_Opens_Minimal_Sheet()
    {
        var added = new List<TmWorkItem>();
        var cut = RenderGantt(TmLayoutMode.Mobile, p =>
            p.Add(c => c.OnTaskAdded, EventCallback.Factory.Create<TmWorkItem>(this, t => added.Add(t))));

        cut.FindAll("[data-testid='gantt-add-sheet']").Should().BeEmpty();
        cut.Find("[data-action-id='gantt-mobile-add']").Click();

        var sheet = cut.Find("[data-testid='gantt-add-sheet']");
        sheet.QuerySelector("[data-testid='gantt-add-name']").Should().NotBeNull();
        sheet.QuerySelector("[data-testid='gantt-add-start']").Should().NotBeNull();
        sheet.QuerySelector("[data-testid='gantt-add-end']").Should().NotBeNull();

        // An empty name is refused.
        cut.Find("[data-testid='gantt-add-submit']").Click();
        added.Should().BeEmpty();
        cut.Find("[data-testid='gantt-add-error']").TextContent.Should().Contain("task name");

        cut.Find("[data-testid='gantt-add-name']").Input("Wireframe review");
        cut.Find("[data-testid='gantt-add-submit']").Click();

        added.Should().ContainSingle(t => t.Title == "Wireframe review");
        added[0].Start.Should().Be(new DateTime(2024, 6, 12), "start defaults to today in the display zone");
        added[0].End.Should().Be(new DateTime(2024, 6, 13));
        cut.FindAll("[data-testid='gantt-add-sheet']").Should().BeEmpty("the sheet closes after the task is added");
    }

    // ── 7. The read-only history sheet ──────────────────────────────────────────────────────────

    [Fact]
    public void TmGantt_Mobile_History_Hides_Rollback_And_TimeTravel()
    {
        var timeTravel = new List<DateTime>();
        var rollback = new List<TmActivityEntry>();
        var history = new List<TmActivityEntry>
        {
            new() { Id = "h1", EntityRef = TmEntityRef.Create("task", "2"), Action = "edit", Timestamp = new DateTimeOffset(2024, 6, 10, 9, 0, 0, TimeSpan.Zero) },
        };
        var cut = RenderGantt(TmLayoutMode.Mobile, p => p
            .Add(c => c.History, history)
            .Add(c => c.OnTimeTravelRequested, EventCallback.Factory.Create<DateTime>(this, t => timeTravel.Add(t)))
            .Add(c => c.OnRollbackRequested, EventCallback.Factory.Create<TmActivityEntry>(this, e => rollback.Add(e))));

        cut.Find("[data-action-id='gantt-mobile-more']").Click();
        cut.Find("[data-testid='gantt-more-history']").Click();

        var sheet = cut.Find("[data-testid='gantt-history-sheet']");
        cut.FindAll("[data-testid='history-entry-h1']").Should().ContainSingle("the entries are readable");
        cut.FindAll("[data-testid^='history-timetravel-']").Should().BeEmpty("time travel is tablet+");
        cut.FindAll("[data-testid^='history-rollback-']").Should().BeEmpty("rollback is tablet+");

        // The sheet closes and returns to the chart.
        cut.Find("[data-testid='gantt-history-sheet']").QuerySelector(".tm-drawer__close")!.Click();
        cut.FindAll("[data-testid='gantt-history-sheet']").Should().BeEmpty();
        timeTravel.Should().BeEmpty();
        rollback.Should().BeEmpty();
    }

    // ── 8. The filter bottom sheet ──────────────────────────────────────────────────────────────

    [Fact]
    public void TmGantt_Mobile_Filter_In_Bottom_Sheet_With_Sticky_Apply()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile, p => p.Add(c => c.ShowQuickSearch, false));

        cut.Find("[data-testid='gantt-filter-btn']").Click();

        // The dialog presentation (TmModal) — a fullscreen bottom sheet on the mobile container.
        var dialog = cut.Find(".tm-modal-overlay.tm-gantt__filter-dialog");
        dialog.Should().NotBeNull();
        cut.Find("[data-testid='filter-apply']").Should().NotBeNull();
        cut.Find("[data-testid='filter-cancel']").Should().NotBeNull();

        // The dialog overlay is its own size container, so the two-line rows resolve there
        // (the overlay root is rendered by TmModal: the rule reaches it through ::deep).
        GanttCss.Property("TmGanttFilterPanel.razor.css", "::deep .tm-gantt__filter-dialog", "container-type").Should().Be("inline-size");
        var css = GanttCss.Strip(File.ReadAllText(Path.Combine(GanttCss.SchedulerDir, "TmGanttFilterPanel.razor.css")));
        css.Should().Contain("max-width: 767.99px", "the two-line condition row is a mobile-container rule");
        css.Should().Contain("flex: 1 1 100%", "the field select takes its own line, operator and value share the second");
    }

    // ── 9. Run-only reports ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void TmGantt_Mobile_Reports_Run_Only()
    {
        var ran = new List<GanttReport>();
        var edited = new List<GanttReport>();
        var created = 0;
        var reports = new List<GanttReport> { new() { Id = "r1", Name = "Weekly", Type = GanttReportType.StatusSummary } };
        var cut = RenderGantt(TmLayoutMode.Mobile, p => p
            .Add(c => c.Reports, reports)
            .Add(c => c.OnReportRun, EventCallback.Factory.Create<GanttReport>(this, r => ran.Add(r)))
            .Add(c => c.OnReportEdit, EventCallback.Factory.Create<GanttReport>(this, r => edited.Add(r)))
            .Add(c => c.OnNewReportRequested, EventCallback.Factory.Create(this, () => created++)));

        cut.Find("[data-action-id='gantt-mobile-more']").Click();
        cut.Find("[data-testid='gantt-more-reports']").Click();

        cut.FindAll("[data-testid='gantt-reports-sheet']").Should().ContainSingle();
        cut.Find("[data-testid='report-run-r1']").Should().NotBeNull();
        cut.FindAll("[data-testid='report-menu-r1']").Should().BeEmpty("edit / duplicate / delete are tablet+");
        cut.FindAll("[data-testid='reports-new-btn']").Should().BeEmpty("creating a report is tablet+");

        cut.Find("[data-testid='report-run-r1']").Click();
        ran.Should().ContainSingle(r => r.Id == "r1");
        edited.Should().BeEmpty();
        created.Should().Be(0);
    }

    // ── 10. Portfolio mobile ────────────────────────────────────────────────────────────────────

    [Fact]
    public void TmGanttPortfolio_Mobile_Card_Summary_And_MiniGantt_ReadOnly()
    {
        var selected = new List<GanttPortfolioProject>();
        var project = new GanttPortfolioProject
        {
            Id = "p1",
            Name = "Aurora",
            Tasks =
            [
                new TmWorkItem { Id = "t1", Title = "Analysis", Start = new DateTime(2024, 4, 15), End = new DateTime(2024, 4, 30), PercentComplete = 50 },
                new TmWorkItem { Id = "t2", Title = "Build", Start = new DateTime(2024, 5, 1), End = new DateTime(2024, 6, 30), PercentComplete = 20 },
            ],
        };
        var cut = Render<TmGanttPortfolio>(p =>
        {
            p.Add(c => c.Projects, [project]);
            p.Add(c => c.LayoutMode, TmLayoutMode.Mobile);
            p.Add(c => c.OnProjectSelected, EventCallback.Factory.Create<GanttPortfolioProject>(this, p => selected.Add(p)));
        });

        // The summary line: neutral "Tasks: N • start – end".
        var summary = cut.Find("[data-testid='portfolio-summary-p1']");
        summary.TextContent.Should().Contain("Tasks: 2").And.Contain("–");

        // The mini-gantt draws bars only — no nested chart, nothing to edit.
        cut.FindAll(".tm-gantt-portfolio__mini").Should().ContainSingle();
        cut.FindAll(".tm-gantt-portfolio__mini-bar").Should().HaveCount(2);
        cut.FindComponents<TmGantt>().Should().BeEmpty("the nested chart is a desktop affordance");

        // A tap selects the project.
        cut.Find("[data-testid='portfolio-name-p1']").Click();
        selected.Should().ContainSingle(p => p.Id == "p1");

        // The desktop card keeps the embedded chart and has no summary line.
        var desktop = Render<TmGanttPortfolio>(p =>
        {
            p.Add(c => c.Projects, [project]);
            p.Add(c => c.LayoutMode, TmLayoutMode.Desktop);
        });
        desktop.FindComponents<TmGantt>().Should().NotBeEmpty();
        desktop.FindAll("[data-testid='portfolio-summary-p1']").Should().BeEmpty();
    }

    // ── 11. Workload mobile ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void TmGanttWorkloadView_Mobile_Person_List_With_Utilization_Bar()
    {
        var tasks = new List<TmWorkItem>
        {
            new()
            {
                Id = "t1", Title = "Task", Start = new DateTime(2024, 6, 10), End = new DateTime(2024, 6, 14),
                Assignees = [new TmWorkItemAssignee { Id = "u1", Name = "Alice" }],
                TimeLog = [new GanttTimeLogEntry { TaskId = "t1", AssigneeId = "u1", StartedAt = new DateTime(2024, 6, 10, 8, 0, 0, DateTimeKind.Utc), StoppedAt = new DateTime(2024, 6, 10, 12, 0, 0, DateTimeKind.Utc) }],
            },
        };
        var cut = Render<TmGanttWorkloadView>(p =>
        {
            p.Add(c => c.Tasks, tasks);
            p.Add(c => c.LayoutMode, TmLayoutMode.Mobile);
            p.Add(c => c.Today, new DateTime(2024, 6, 12));
        });

        // One row per person: the name (the pinned column), the utilization bar and the hours of the week.
        var row = cut.Find("[data-testid='workload-mobile-row-u1']");
        row.QuerySelector(".tm-gantt__workload-util-bar").Should().NotBeNull();
        row.QuerySelector(".tm-gantt__workload-util-fill").Should().NotBeNull();
        row.TextContent.Should().Contain("Alice").And.Contain("h");

        // The mobile view shows the week (7 day columns), not the 4-week window.
        cut.FindAll(".tm-gantt__workload-mobile-header-cell").Should().HaveCount(7);

        // The desktop grid is unchanged (no mobile list, no utilization bar).
        var desktop = Render<TmGanttWorkloadView>(p =>
        {
            p.Add(c => c.Tasks, tasks);
            p.Add(c => c.Today, new DateTime(2024, 6, 12));
        });
        desktop.FindAll(".tm-gantt__workload-util-bar").Should().BeEmpty();
        desktop.FindAll("[data-testid='workload-mobile-row-u1']").Should().BeEmpty();
    }

    // ── 12. Reorder and dependency drag are read-mostly ─────────────────────────────────────────

    [Fact]
    public async Task TmGantt_Mobile_Tree_Reorder_And_DependencyDrag_Disabled()
    {
        var reordered = new List<GanttTaskReorderedArgs>();
        var added = new List<GanttDependency>();
        var cut = RenderGantt(TmLayoutMode.Mobile, p => p
            .Add(c => c.AllowDependencyCreation, true)
            .Add(c => c.OnTaskReordered, EventCallback.Factory.Create<GanttTaskReorderedArgs>(this, a => reordered.Add(a)))
            .Add(c => c.OnDependencyAdded, EventCallback.Factory.Create<GanttDependency>(this, d => added.Add(d))));

        // No drag affordances on the mobile chart.
        cut.FindAll(".tm-gantt__tree-row").Should().OnlyContain(r => r.GetAttribute("draggable") == "false");
        cut.FindAll(".tm-gantt__dep-handle").Should().BeEmpty();

        // The keyboard alternative of the (absent) drag is a no-op too.
        await cut.InvokeAsync(() => cut.Instance.OnTreeKeyboardMove("3", -1));
        reordered.Should().BeEmpty();

        // And the commit path of the pointer session is refused (defence in depth).
        await cut.InvokeAsync(() => cut.Instance.OnDependencyDragCompleted("2", true, "3"));
        added.Should().BeEmpty();
    }

    [Fact]
    public async Task TmGantt_Mobile_LongPress_Menu_Offers_Status_Percent_And_Add_Task_Only()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile);
        await cut.InvokeAsync(() => cut.Instance.OnLongPressContextMenu("3", 100, 100));

        var menu = cut.Find(".tm-gantt__mobile-menu");
        menu.QuerySelector("[data-testid='gantt-context-status']").Should().NotBeNull();
        menu.QuerySelector("[data-testid='gantt-context-percent']").Should().NotBeNull();
        menu.QuerySelector("[data-testid='gantt-context-add']").Should().NotBeNull();
        menu.QuerySelector("[data-testid='gantt-context-move-up']").Should().BeNull("reorder is tablet+");
        menu.QuerySelector("[data-testid='gantt-context-move-down']").Should().BeNull();
        menu.TextContent.Should().NotContain("Delete task", "deleting is tablet+");

        // "Change status" opens the read-only task sheet of that task.
        cut.Find("[data-testid='gantt-context-status']").Click();
        cut.FindAll(".tm-drawer.tm-gantt__sheet").Should().ContainSingle();
        cut.Find(".tm-drawer__title").TextContent.Should().Contain("Development");
    }

    [Fact]
    public void TmGantt_Mobile_Sidebar_Rail_Is_Replaced_By_The_More_Sheet()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile);

        cut.FindAll("[data-testid='gantt-sidebar']").Should().BeEmpty("the panels live in the More sheet on mobile");
        cut.FindAll("[data-testid='sidebar-btn-reports']").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_Mobile_Culture_Pinned_Markup_Is_Culture_Independent()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
            var cut = RenderGantt(TmLayoutMode.Mobile);
            cut.Find(".tm-gantt__timeline-content").GetAttribute("style").Should().Contain("168px", "3 rows x 56 px");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void TmGantt_Layout_Flip_To_Mobile_Rebuilds_The_Timeline_Headers_As_Days()
    {
        // The real browser flips AFTER the first render (the observer measures once mounted): the flip
        // must rebuild the header data, not only the markup — the read-mostly zoom is Days. The layout
        // is NOT forced here: a forced mode would suppress the measured flip, which is the case tested.
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, new List<TmWorkItem>
        {
            new() { Id = "1", Title = "A", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30), IsExpanded = true },
        }));

        cut.FindAll(".tm-gantt__timeline-header--lower .tm-gantt__timeline-header-cell")
            .First().TextContent.Trim().Should().StartWith("W", "the unmeasured first frame is a Desktop: the Weeks zoom");

        var observer = cut.FindComponent<TmLayoutObserver>();
        cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("mobile"));

        cut.FindAll(".tm-gantt__timeline-header--lower .tm-gantt__timeline-header-cell")
            .Select(c => c.TextContent.Trim()).Should().NotContain(l => l.StartsWith("W"), "the mobile zoom is Days");
        cut.Find(".tm-gantt").GetAttribute("data-density").Should().Be("mobile");
    }
}
