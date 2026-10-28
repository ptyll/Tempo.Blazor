using System.Globalization;
using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TmGantt redesign, phase 7 (tablet and touch): the container layout (<c>LayoutMode</c>), the two-row toolbar, the task panel as
/// a <c>TmSidePanel</c> side sheet with tabs (<c>TaskPanelMode</c>), at most one right-hand surface, 44 px touch rows
/// (<c>GanttViewDensity.Touch</c>), the pointer sessions of the core <c>pointer-drag.js</c> (registered through gantt.js) and the non-drag
/// alternative of the tree reorder. A bUnit render cannot run container queries, so every layout is FORCED with <c>LayoutMode</c>.
/// Culture and clock are pinned: CI runs Linux / UTC / invariant culture plus a non-English ambient lane.
/// </summary>
public class TmGanttPhase7TabletTests : LocalizationTestBase
{
    public TmGanttPhase7TabletTests()
    {
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2024, 6, 12, 10, 0, 0, TimeSpan.Zero)));
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(TimeZoneInfo.Utc));
        // gantt.js reports whether the pointer sessions registered (false = helpers unavailable);
        // the component records its gesture key only on true.
        JSInterop.Setup<bool>("tmGantt.attachTimeline", _ => true).SetResult(true);
        JSInterop.Setup<bool>("tmGantt.attachTree", _ => true).SetResult(true);
    }

    private static List<TmWorkItem> Tasks() =>
    [
        new() { Id = "1", Title = "Project", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30), IsExpanded = true },
        new() { Id = "2", Title = "Design", ParentId = "1", Start = new DateTime(2024, 6, 3), End = new DateTime(2024, 6, 6) },
        new() { Id = "3", Title = "Development", ParentId = "1", Start = new DateTime(2024, 6, 11), End = new DateTime(2024, 6, 25) },
        new() { Id = "4", Title = "Launch", Start = new DateTime(2024, 6, 30), End = new DateTime(2024, 6, 30), IsMilestone = true },
    ];

    /// <summary>Renders with the sample tasks unless <paramref name="items"/> brings the test's own (the very objects it later selects).</summary>
    private IRenderedComponent<TmGantt> RenderGantt(TmLayoutMode mode, Action<ComponentParameterCollectionBuilder<TmGantt>>? configure = null, IReadOnlyList<TmWorkItem>? items = null) =>
        Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, items ?? Tasks());
            p.Add(c => c.LayoutMode, mode);
            configure?.Invoke(p);
        });

    // ── Layout ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TmLayoutMode.Tablet, "tablet", "tm-gantt--tablet")]
    [InlineData(TmLayoutMode.Desktop, "desktop", "tm-gantt--desktop")]
    [InlineData(TmLayoutMode.Mobile, "mobile", "tm-gantt--mobile")]
    public void TmGantt_LayoutMode_Applies_Layout_Attribute_And_Class(TmLayoutMode mode, string modifier, string cssClass)
    {
        var cut = RenderGantt(mode);

        var root = cut.Find(".tm-gantt");
        root.GetAttribute("data-layout").Should().Be(modifier);
        root.ClassList.Should().Contain(cssClass);
    }

    [Fact]
    public void TmGantt_LayoutMode_Tablet_Applies_Tablet_Class_And_TwoRow_Toolbar()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet);

        cut.Find(".tm-gantt").ClassList.Should().Contain("tm-gantt--tablet");
        cut.Find(".tm-gantt__toolbar").ClassList.Should().Contain("tm-gantt__toolbar--two-rows");
        var rows = cut.FindAll(".tm-gantt__toolbar [role='toolbar']");
        rows.Should().HaveCount(2, "row 1 is create / structure / time, row 2 panels / data / settings");

        string[] first = ["create", "structure", "time"];
        string[] second = ["panels", "data", "settings"];
        rows[0].QuerySelectorAll("[data-tm-group]").Select(g => g.GetAttribute("data-tm-group")).Should().Equal(first);
        rows[1].QuerySelectorAll("[data-tm-group]").Select(g => g.GetAttribute("data-tm-group")).Should().Equal(second);
    }

    [Fact]
    public void TmGantt_Desktop_Toolbar_Stays_One_Row()
    {
        var cut = RenderGantt(TmLayoutMode.Desktop);

        cut.FindAll(".tm-gantt__toolbar [role='toolbar']").Should().ContainSingle();
        cut.Find(".tm-gantt__toolbar").ClassList.Should().NotContain("tm-gantt__toolbar--two-rows");
    }

    [Fact]
    public void TmGantt_Tablet_Toolbar_Rows_Overflow_Into_More()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet);

        // Overflow=Menu rows register their buttons with the toolbar: collapsed-capable items carry the measured-item marker.
        cut.FindAll(".tm-gantt__toolbar [data-tm-toolbar-item]").Should().NotBeEmpty();
        cut.FindAll(".tm-gantt__toolbar [data-testid='gantt-history-btn']").Should().NotBeEmpty("every command stays reachable");
    }

    [Fact]
    public void TmGantt_ContainerWidth_Is_Obsolete_Alias_For_LayoutMode()
    {
        typeof(TmGantt).GetProperty(nameof(TmGantt.ContainerWidth))!.GetCustomAttribute<ObsoleteAttribute>()
            .Should().NotBeNull("ContainerWidth is superseded by LayoutMode (G15 / R9)");

        // The alias is classified like a measurement while LayoutMode is Auto: 900 px is a tablet.
        var tablet = Render<TmGantt>(p => p.Add(c => c.Items, Tasks()).Add(c => c.ContainerWidth, 900d));
        tablet.Find(".tm-gantt").GetAttribute("data-layout").Should().Be("tablet");

        var desktop = Render<TmGantt>(p => p.Add(c => c.Items, Tasks()).Add(c => c.ContainerWidth, 1280d));
        desktop.Find(".tm-gantt").GetAttribute("data-layout").Should().Be("desktop");

        // An explicit LayoutMode wins over the alias.
        var forced = Render<TmGantt>(p => p.Add(c => c.Items, Tasks()).Add(c => c.ContainerWidth, 900d).Add(c => c.LayoutMode, TmLayoutMode.Desktop));
        forced.Find(".tm-gantt").GetAttribute("data-layout").Should().Be("desktop");
    }

    [Fact]
    public void TmGantt_Root_Stays_The_Css_Container_And_The_Observer_Is_Not()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet);

        cut.Find(".tm-layout").ClassList.Should().Contain("tm-layout--no-container", "the chart root inside it is the one size container");
        cut.Find(".tm-layout > .tm-gantt").Should().NotBeNull();
    }

    // ── Task panel ─────────────────────────────────────────────────────────────

    [Fact]
    public void TmGantt_TaskPanelMode_Default_Is_Inline_And_Unchanged()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet, p => p.Add(c => c.ShowTaskPanel, true).Add(c => c.SelectedTask, Tasks()[1]));

        cut.FindAll(".tm-gantt__content > .tm-gantt-task-panel").Should().ContainSingle("Inline is today's in-flow panel at every width");
        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_TaskPanelMode_Auto_Uses_TmSidePanel_Sheet_On_Tablet()
    {
        var tasks = Tasks();
        var cut = RenderGantt(TmLayoutMode.Tablet, items: tasks, configure: p => p
            .Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto)
            .Add(c => c.ShowTaskPanel, true)
            .Add(c => c.SelectedTask, tasks[1]));

        var sheet = cut.Find(".tm-drawer.tm-side-panel-sheet");
        sheet.ClassList.Should().Contain("tm-drawer--right");
        cut.FindAll(".tm-gantt__content > .tm-gantt-task-panel").Should().BeEmpty();
        cut.Find(".tm-drawer__title").TextContent.Should().Contain("Design");
    }

    [Fact]
    public void TmGantt_TaskPanelMode_Auto_Stays_Inline_On_Desktop()
    {
        var tasks = Tasks();
        var cut = RenderGantt(TmLayoutMode.Desktop, items: tasks, configure: p => p
            .Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto)
            .Add(c => c.ShowTaskPanel, true)
            .Add(c => c.SelectedTask, tasks[1]));

        cut.FindAll(".tm-gantt__content > .tm-gantt-task-panel").Should().ContainSingle();
        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_TaskPanelMode_Overlay_Is_A_Sheet_At_Every_Width()
    {
        var tasks = Tasks();
        var cut = RenderGantt(TmLayoutMode.Desktop, items: tasks, configure: p => p
            .Add(c => c.TaskPanelMode, GanttTaskPanelMode.Overlay)
            .Add(c => c.ShowTaskPanel, true)
            .Add(c => c.SelectedTask, tasks[1]));

        cut.FindAll(".tm-drawer.tm-side-panel-sheet").Should().ContainSingle();
    }

    [Fact]
    public void TmGantt_Tablet_Tap_On_A_Row_Opens_The_Task_Sheet_And_Close_Dismisses_It()
    {
        var changes = new List<bool>();
        var cut = RenderGantt(TmLayoutMode.Tablet, p =>
        {
            p.Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto);
            p.Add(c => c.ShowTaskPanelChanged, EventCallback.Factory.Create<bool>(this, changes.Add));
        });
        cut.FindAll(".tm-drawer").Should().BeEmpty();

        // A finger tap: the pointerup names the pointer type before the click opens the sheet.
        TouchTap(cut.FindAll(".tm-gantt__tree-row")[1]);

        cut.FindAll(".tm-drawer.tm-side-panel-sheet").Should().ContainSingle();
        changes.Should().ContainSingle(v => v, "the internal open raised ShowTaskPanelChanged");
        cut.Instance.ShowTaskPanel.Should().BeFalse("the parameter is never assigned by the component");

        cut.Find(".tm-drawer__close").Click();
        cut.FindAll(".tm-drawer").Should().BeEmpty();
        changes.Should().Equal([true, false]);
        cut.Instance.ShowTaskPanel.Should().BeFalse();
    }

    /// <summary>A touch tap in bUnit: the row's pointerup (names the pointer type) followed by its click.</summary>
    private static void TouchTap(AngleSharp.Dom.IElement row)
    {
        row.TriggerEvent("onpointerup", new Microsoft.AspNetCore.Components.Web.PointerEventArgs { PointerType = "touch" });
        row.TriggerEvent("onclick", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
    }

    [Fact]
    public void TmGantt_TaskPanel_Tablet_Tabs_Detail_Attachments_Comments_History()
    {
        var tasks = Tasks();
        tasks[1].Attachments = [new TmAttachment { Id = "a1", FileName = "spec.pdf" }];
        var history = new List<TmActivityEntry>
        {
            new() { Id = "h1", EntityRef = TmEntityRef.Create("task", "2"), Action = "edit", Timestamp = new DateTimeOffset(2024, 6, 10, 9, 0, 0, TimeSpan.Zero) },
            new() { Id = "h2", EntityRef = TmEntityRef.Create("task", "3"), Action = "edit", Timestamp = new DateTimeOffset(2024, 6, 10, 9, 5, 0, TimeSpan.Zero) },
        };
        var cut = RenderGantt(TmLayoutMode.Tablet, items: tasks, configure: p => p
            .Add(c => c.History, history)
            .Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto)
            .Add(c => c.ShowTaskPanel, true)
            .Add(c => c.SelectedTask, tasks[1]));

        cut.FindAll(".tm-gantt-task-panel__tabs .tm-tab__label").Select(t => t.TextContent.Trim())
            .Should().Equal("Detail", "Attachments", "Comments", "History");

        // Detail: the form and the transactional footer, no attachments or history section.
        cut.FindAll("[data-testid='task-section-plan']").Should().ContainSingle();
        cut.FindAll("[data-testid='task-section-attachments']").Should().BeEmpty();
        cut.FindAll("[data-testid='task-save']").Should().ContainSingle();

        Tab(cut, "attachments").Click();
        cut.FindAll("[data-testid='task-section-attachments']").Should().ContainSingle();
        cut.FindAll("[data-testid='task-section-plan']").Should().BeEmpty();

        Tab(cut, "comments").Click();
        cut.FindAll("[data-testid='task-section-comments']").Should().ContainSingle();

        Tab(cut, "history").Click();
        cut.FindAll("[data-testid='task-section-history']").Should().ContainSingle();
        cut.FindAll("[data-testid='history-entry-h1']").Should().ContainSingle("the history of THIS task");
        cut.FindAll("[data-testid='history-entry-h2']").Should().BeEmpty("an entry of another task is filtered out by EntityId");
    }

    [Fact]
    public void TmGantt_TaskPanel_Tablet_Keeps_The_Dirty_Draft_When_The_Tab_Changes()
    {
        var tasks = Tasks();
        var cut = RenderGantt(TmLayoutMode.Tablet, items: tasks, configure: p => p
            .Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto)
            .Add(c => c.ShowTaskPanel, true)
            .Add(c => c.SelectedTask, tasks[1]));

        cut.FindAll("[data-testid='task-dirty']").Should().BeEmpty();
        cut.Find("[data-testid='task-title'] input").Change("Renamed draft");

        Tab(cut, "comments").Click();

        cut.Find("[data-testid='task-dirty']").TextContent.Should().Contain("Unsaved changes");
        cut.Find(".tm-gantt-task-panel").GetAttribute("data-dirty").Should().Be("true");
        cut.FindAll("[data-testid='task-save']").Should().ContainSingle("the footer stays reachable while a draft waits");

        Tab(cut, "detail").Click();
        cut.Find("[data-testid='task-title'] input").GetAttribute("value").Should().Be("Renamed draft", "the draft survives a tab switch");
    }

    private static AngleSharp.Dom.IElement Tab(IRenderedComponent<TmGantt> cut, string id) => cut.Find($"#tab-{id}");

    // ── One right-hand surface ─────────────────────────────────────────────────

    [Fact]
    public void TmGantt_Tablet_At_Most_One_Side_Sheet()
    {
        var tasks = Tasks();
        var reports = new List<GanttReport> { new() { Id = "r1", Name = "Weekly", Type = GanttReportType.StatusSummary } };
        var cut = RenderGantt(TmLayoutMode.Tablet, items: tasks, configure: p => p
            .Add(c => c.Reports, reports)
            .Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto)
            .Add(c => c.ShowTaskPanel, true)
            .Add(c => c.SelectedTask, tasks[1]));
        cut.FindAll(".tm-drawer").Should().ContainSingle();

        // Reports replace the task sheet.
        cut.Find("[data-testid='sidebar-btn-reports']").Click();
        cut.FindAll(".tm-drawer").Should().ContainSingle();
        cut.FindAll("[data-testid='gantt-reports-sheet']").Should().ContainSingle();
        cut.FindAll("[data-testid='gantt-task-sheet']").Should().BeEmpty();

        // History replaces reports.
        cut.Find("[data-testid='gantt-history-btn']").Click();
        cut.FindAll("[data-testid='gantt-history-drawer']").Should().ContainSingle();
        cut.FindAll("[data-testid='gantt-reports-sheet']").Should().BeEmpty();
        cut.FindAll("[data-testid='gantt-task-sheet']").Should().BeEmpty();

        // Selecting a task closes the history again and shows the task sheet.
        TouchTap(cut.FindAll(".tm-gantt__tree-row")[1]);
        cut.FindAll("[data-testid='gantt-history-drawer']").Should().BeEmpty();
        cut.FindAll("[data-testid='gantt-task-sheet']").Should().ContainSingle();
    }

    [Fact]
    public void TmGantt_Desktop_Keeps_Panels_Independent()
    {
        var tasks = Tasks();
        var cut = RenderGantt(TmLayoutMode.Desktop, items: tasks, configure: p => p
            .Add(c => c.ShowTaskPanel, true)
            .Add(c => c.SelectedTask, tasks[1]));

        cut.Find("[data-testid='gantt-history-btn']").Click();

        cut.FindAll("[data-testid='gantt-history-drawer']").Should().ContainSingle();
        cut.FindAll(".tm-gantt-task-panel").Should().ContainSingle("the desktop keeps today's independent surfaces");
    }

    // ── Density ────────────────────────────────────────────────────────────────

    [Fact]
    public void TmGantt_Density_Touch_RowHeight_44_In_Tree_And_Timeline()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet, p => p.Add(c => c.ViewSettings, new GanttViewSettings { ViewDensity = GanttViewDensity.Touch }));

        cut.Find(".tm-gantt__timeline-content").GetAttribute("style").Should().Contain("height: 176px", "4 rows x 44");
        var tops = cut.FindAll(".tm-gantt__task-row").Select(r => r.GetAttribute("style")).ToList();
        tops.Should().HaveCount(4);
        tops.Should().OnlyContain(s => s!.Contains("height: 44px"));
        // The bar top is (row - bar height) / 2 inside each row: 12px, 56, 100, 144.
        tops.Select(s => System.Text.RegularExpressions.Regex.Match(s!, @"top: (\d+)px").Groups[1].Value)
            .Should().Equal("12", "56", "100", "144");

        GanttCss.Property("TmGanttTree.razor.css", "[data-density=\"touch\"] .tm-gantt__tree-row", "height").Should().Be("44px");
    }

    [Fact]
    public void TmGantt_Density_Touch_Is_An_Additive_Enum_Value()
    {
        Enum.GetValues<GanttViewDensity>().Select(v => (int)v).Should().Equal([0, 1, 2], "Comfortable and Compact keep their values");
        Enum.GetName(typeof(GanttViewDensity), 2).Should().Be("Touch");
    }

    [Fact]
    public void TmGantt_View_Settings_Offers_The_Touch_Density()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet);
        cut.Find("[data-testid='gantt-view-settings-btn']").Click();

        cut.FindAll("input[type=radio][value='Touch']").Should().ContainSingle();
    }

    // ── Pointer sessions ───────────────────────────────────────────────────────

    [Fact]
    public void TmGantt_Timeline_Registers_PointerDrag_And_Disposes()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet);

        var attach = JSInterop.Invocations.Single(i => i.Identifier == "tmGantt.attachTimeline");
        var id = (string)attach.Arguments[0]!;
        id.Should().StartWith("tm-gantt-");
        JSInterop.Invocations.Should().Contain(i => i.Identifier == "tmGantt.attachTree" && (string)i.Arguments[0]! == id);
        JSInterop.Invocations.Select(i => i.Identifier).Should().NotContain("tmGantt.detachTimeline");

        cut.Instance.DisposeAsync().AsTask().GetAwaiter().GetResult();

        JSInterop.Invocations.Should().Contain(i => i.Identifier == "tmGantt.detachTimeline" && (string)i.Arguments[0]! == id);
        JSInterop.Invocations.Should().Contain(i => i.Identifier == "tmGantt.detachTree" && (string)i.Arguments[0]! == id);
    }

    [Fact]
    public void TmGantt_Timeline_Does_Not_Reattach_On_Every_Render()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet);

        cut.Find("[data-testid='gantt-zoom-in']").Click();
        cut.Find("[data-testid='gantt-zoom-in']").Click();

        JSInterop.Invocations.Count(i => i.Identifier == "tmGantt.attachTimeline").Should().Be(1);
    }

    [Fact]
    public void TmGantt_Timeline_Detaches_When_The_Chart_Is_Replaced_By_Another_View()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet);

        cut.Render(p => p.Add(c => c.View, GanttView.Board));

        JSInterop.Invocations.Select(i => i.Identifier).Should().Contain("tmGantt.detachTimeline");
    }

    [Fact]
    public void TmGantt_Timeline_Has_No_Mouse_Handlers_Touch_Action_Is_Css_Owned()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet);

        cut.Markup.Should().NotContain("onmousedown");
        GanttCss.Property("TmGanttTimeline.razor.css", ".tm-gantt__timeline", "touch-action").Should().Be("pan-x pan-y");
    }

    [Fact]
    public void TmGantt_DepHandles_Name_Their_Task_And_End_For_The_Pointer_Session()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet, p => p.Add(c => c.AllowDependencyCreation, true));

        var handles = cut.FindAll(".tm-gantt__bar[data-task-id='2'] .tm-gantt__dep-handle");
        handles.Select(h => h.GetAttribute("data-dep-end")).Should().Equal("false", "true");
        handles.Should().OnlyContain(h => h.GetAttribute("data-task-id") == "2");
        cut.Markup.Should().NotContain("onmouseup");
    }

    [Fact]
    public async Task TmGantt_DependencyDrag_Completion_Raises_OnDependencyAdded_Once()
    {
        GanttDependency? added = null;
        var cut = RenderGantt(TmLayoutMode.Tablet, p => p
            .Add(c => c.AllowDependencyCreation, true)
            .Add(c => c.OnDependencyAdded, EventCallback.Factory.Create<GanttDependency>(this, d => added = d)));

        await cut.InvokeAsync(() => cut.Instance.OnDependencyDragCompleted("2", true, "3"));

        added.Should().NotBeNull();
        added!.FromId.Should().Be("2");
        added.ToId.Should().Be("3");
        added.DepType.Should().Be(GanttDependencyType.FinishToStart);

        added = null;
        await cut.InvokeAsync(() => cut.Instance.OnDependencyDragCompleted("2", false, "2"));
        added.Should().BeNull("a bar is never its own predecessor");
    }

    [Fact]
    public async Task TmGantt_DependencyDrag_Completion_Is_Ignored_Without_AllowDependencyCreation()
    {
        GanttDependency? added = null;
        var cut = RenderGantt(TmLayoutMode.Tablet, p => p
            .Add(c => c.OnDependencyAdded, EventCallback.Factory.Create<GanttDependency>(this, d => added = d)));

        await cut.InvokeAsync(() => cut.Instance.OnDependencyDragCompleted("2", true, "3"));

        added.Should().BeNull();
    }

    [Fact]
    public async Task TmGantt_LongPress_Opens_ContextMenu()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet);
        cut.FindAll(".tm-gantt__context-menu").Should().BeEmpty();

        await cut.InvokeAsync(() => cut.Instance.OnLongPressContextMenu("3", 240.5, 180));

        var menu = cut.Find(".tm-gantt__context-menu");
        menu.GetAttribute("style").Should().Contain("240.5px").And.Contain("180px");
        cut.FindAll(".tm-gantt__context-menu-item").Select(b => b.TextContent.Trim())
            .Should().Contain(["Add task above", "Add task below", "Add child task", "Move up", "Move down", "Delete task"]);

        // A long-press on a vanished task is ignored.
        var other = RenderGantt(TmLayoutMode.Tablet);
        await other.InvokeAsync(() => other.Instance.OnLongPressContextMenu("nope", 1, 1));
        other.FindAll(".tm-gantt__context-menu").Should().BeEmpty();
    }

    [Fact]
    public async Task TmGantt_LongPress_Menu_Ignores_The_Release_Click_Of_The_Lifting_Finger()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet);
        await cut.InvokeAsync(() => cut.Instance.OnLongPressContextMenu("3", 100, 100));

        cut.Find(".tm-gantt__context-menu-overlay").Click();

        cut.FindAll(".tm-gantt__context-menu").Should().ContainSingle("the compatibility click of the finger that opened it must not dismiss it");
    }

    // ── Reorder without dragging (WCAG 2.5.7) ──────────────────────────────────

    [Fact]
    public async Task TmGantt_Tree_Reorder_Has_NonDrag_Alternative_Alt_Arrow()
    {
        var reordered = new List<GanttTaskReorderedArgs>();
        var cut = RenderGantt(TmLayoutMode.Tablet, p => p
            .Add(c => c.OnTaskReordered, EventCallback.Factory.Create<GanttTaskReorderedArgs>(this, a => reordered.Add(a))));

        await cut.InvokeAsync(() => cut.Instance.OnTreeKeyboardMove("3", -1));

        reordered.Should().ContainSingle();
        reordered[0].TaskId.Should().Be("3");
        reordered[0].TargetTaskId.Should().Be("2");
        reordered[0].Position.Should().Be(GanttDropPosition.Before);

        await cut.InvokeAsync(() => cut.Instance.OnTreeKeyboardMove("2", 1));
        reordered[1].Position.Should().Be(GanttDropPosition.After);
        reordered[1].TargetTaskId.Should().Be("3");

        cut.Find("[data-testid='gantt-announcer']").TextContent.Should().Contain("Design").And.Contain("Development");
    }

    [Fact]
    public async Task TmGantt_Tree_Reorder_At_The_Edge_Is_A_NoOp()
    {
        var reordered = new List<GanttTaskReorderedArgs>();
        var cut = RenderGantt(TmLayoutMode.Tablet, p => p
            .Add(c => c.OnTaskReordered, EventCallback.Factory.Create<GanttTaskReorderedArgs>(this, a => reordered.Add(a))));

        await cut.InvokeAsync(() => cut.Instance.OnTreeKeyboardMove("2", -1));
        await cut.InvokeAsync(() => cut.Instance.OnTreeKeyboardMove("3", 1));
        await cut.InvokeAsync(() => cut.Instance.OnTreeKeyboardMove("3", 5));

        reordered.Should().BeEmpty();
    }

    [Fact]
    public async Task TmGantt_Tree_Reorder_Has_NonDrag_Alternative_Menu_Action()
    {
        var reordered = new List<GanttTaskReorderedArgs>();
        var cut = RenderGantt(TmLayoutMode.Tablet, p => p
            .Add(c => c.OnTaskReordered, EventCallback.Factory.Create<GanttTaskReorderedArgs>(this, a => reordered.Add(a))));
        await cut.InvokeAsync(() => cut.Instance.OnLongPressContextMenu("3", 10, 10));

        cut.Find("[data-testid='gantt-context-move-up']").Click();

        reordered.Should().ContainSingle(a => a.TaskId == "3" && a.TargetTaskId == "2" && a.Position == GanttDropPosition.Before);
        cut.FindAll(".tm-gantt__context-menu").Should().BeEmpty();
    }

    [Fact]
    public async Task TmGantt_Context_Menu_Disables_Move_At_The_Edge()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet, p => p.Add(c => c.OnTaskReordered, EventCallback.Factory.Create<GanttTaskReorderedArgs>(this, _ => { })));
        await cut.InvokeAsync(() => cut.Instance.OnLongPressContextMenu("2", 10, 10));

        cut.Find("[data-testid='gantt-context-move-up']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='gantt-context-move-down']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void TmGantt_TaskPanel_Offers_Move_Up_And_Down_In_The_Tabbed_Presentation()
    {
        var tasks = Tasks();
        var reordered = new List<GanttTaskReorderedArgs>();
        var cut = RenderGantt(TmLayoutMode.Tablet, items: tasks, configure: p => p
            .Add(c => c.OnTaskReordered, EventCallback.Factory.Create<GanttTaskReorderedArgs>(this, a => reordered.Add(a)))
            .Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto)
            .Add(c => c.ShowTaskPanel, true)
            .Add(c => c.SelectedTask, tasks[2]));

        cut.Find("[data-testid='task-action-move-up']").Click();

        reordered.Should().ContainSingle(a => a.TaskId == "3" && a.TargetTaskId == "2");
        cut.Find(".tm-gantt-task-panel__action-list").PreviousElementSibling!.TextContent.Should().Be("More actions");
    }

    [Fact]
    public void TmGantt_Tree_Rows_Name_Their_Task_For_The_Pointer_Sessions()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet);

        cut.FindAll(".tm-gantt__tree-row").Select(r => r.GetAttribute("data-task-id")).Should().Equal("1", "2", "3", "4");
        cut.FindAll(".tm-gantt__tree-row").Should().OnlyContain(r => r.GetAttribute("draggable") == "true", "the mouse keeps HTML5 drag-and-drop; gantt.js turns it off while a finger is down");
    }

    // ── Dialogs, hint, theme-independent CSS contract ──────────────────────────

    [Fact]
    public void TmGantt_Tablet_Shows_The_Touch_Hint_Desktop_Does_Not()
    {
        RenderGantt(TmLayoutMode.Tablet).FindAll("[data-testid='gantt-touch-hint']").Should().ContainSingle();
        RenderGantt(TmLayoutMode.Desktop).FindAll("[data-testid='gantt-touch-hint']").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_Tablet_Css_Has_Coarse_Pointer_44px_Targets_And_Container_Query()
    {
        foreach (var file in new[] { "TmGantt.razor.css", "TmGanttToolbar.razor.css", "TmGanttTree.razor.css", "TmGanttTimeline.razor.css" })
        {
            var css = GanttCss.Strip(File.ReadAllText(Path.Combine(GanttCss.SchedulerDir, file)));
            css.Should().Contain("pointer: coarse", $"{file} sizes its targets for a finger");
        }

        GanttCss.Property("TmGantt.razor.css", ".tm-gantt", "container-type").Should().Be("inline-size");
        GanttCss.Property("TmGantt.razor.css", ".tm-gantt", "container-name").Should().Be("tm-gantt");
    }

    [Fact]
    public void TmGantt_Tree_Bulk_Checkbox_Column_Is_Fixed_Width()
    {
        // Found in the browser at 1024: the checkbox column was flex 1 and swallowed the narrowed grid (the title was pushed out of view).
        GanttCss.Property("TmGanttTree.razor.css", ".tm-gantt__tree-cell--checkbox", "flex").Should().Be("0 0 32px");
    }

    [Fact]
    public void TmGantt_Tablet_Tree_Shows_Task_Status_Start_End_Only()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet, p => p.Add(c => c.AllowBulkSelect, true).Add(c => c.Columns, [
            new GanttColumnDefinition { Key = GanttColumnKey.WBS, Visible = true, Order = 0 },
            new GanttColumnDefinition { Key = GanttColumnKey.Title, Visible = true, Order = 1 },
            new GanttColumnDefinition { Key = GanttColumnKey.Status, Visible = true, Order = 2 },
            new GanttColumnDefinition { Key = GanttColumnKey.Start, Visible = true, Order = 3 },
            new GanttColumnDefinition { Key = GanttColumnKey.End, Visible = true, Order = 4 },
            new GanttColumnDefinition { Key = GanttColumnKey.Progress, Visible = true, Order = 5 },
            new GanttColumnDefinition { Key = GanttColumnKey.Duration, Visible = true, Order = 6 },
        ]));

        cut.FindAll(".tm-gantt__tree-header [role='columnheader']").Select(h => h.TextContent.Trim()).Where(t => t.Length > 0)
            .Should().Equal("Task", "Status", "Start", "End");

        // The width a wider column set would force is not applied on a narrow container (found in the browser: the grid stayed 990px in a 330px tree).
        cut.Find(".tm-gantt__tree-grid").GetAttribute("style").Should().BeNullOrEmpty();
        cut.Find(".tm-gantt__tree").GetAttribute("style").Should().BeNullOrEmpty();

        var desktop = RenderGantt(TmLayoutMode.Desktop, p => p.Add(c => c.Columns, [new GanttColumnDefinition { Key = GanttColumnKey.Progress, Visible = true, Order = 0 }]));
        desktop.FindAll(".tm-gantt__tree-header-cell--narrow").Should().NotBeEmpty("the desktop keeps every visible column");
    }

    [Fact]
    public void TmGantt_Culture_Pinned_Layout_Is_Culture_Independent()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
            var cut = RenderGantt(TmLayoutMode.Tablet, p => p.Add(c => c.ViewSettings, new GanttViewSettings { ViewDensity = GanttViewDensity.Touch }));
            cut.Find(".tm-gantt__timeline-content").GetAttribute("style").Should().Contain("176px");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // ── Review fixes ───────────────────────────────────────────────────────────

    [Fact]
    public void TmGantt_Programmatic_Selection_Does_Not_Raise_The_Modal_Sheet()
    {
        var tasks = Tasks();
        var cut = RenderGantt(TmLayoutMode.Tablet, items: tasks, configure: p => p.Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto));

        // Add task selects the new task programmatically: the sheet must not cover the toolbar the user is working in.
        cut.Find("[data-testid='gantt-add-task']").Click();

        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_Layout_Flip_Reconciles_Open_Panels_To_One_Surface()
    {
        var tasks = Tasks();
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, tasks)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto)
            .Add(c => c.ShowTaskPanel, true)
            .Add(c => c.SelectedTask, tasks[1]));
        cut.Find("[data-testid='gantt-history-btn']").Click();
        cut.FindAll("[data-testid='gantt-history-drawer']").Should().ContainSingle();

        cut.Render(p => p.Add(c => c.LayoutMode, TmLayoutMode.Tablet));

        cut.FindAll("[data-testid='gantt-history-drawer']").Should().BeEmpty("the sheet and the history drawer never coexist after a resize");
        cut.FindAll(".tm-drawer.tm-side-panel-sheet").Should().ContainSingle();
    }

    [Fact]
    public async Task TmGantt_Reorder_Neighbour_Is_A_Visible_Sibling_And_Needs_A_Handler()
    {
        var tasks = Tasks();
        tasks.Insert(2, new TmWorkItem { Id = "9", Title = "Hidden by filter", ParentId = "1", Start = new DateTime(2024, 6, 7), End = new DateTime(2024, 6, 8) });
        var reordered = new List<GanttTaskReorderedArgs>();
        var cut = RenderGantt(TmLayoutMode.Tablet, items: tasks, configure: p => p
            .Add(c => c.Filters, [new GanttFilter("Title", GanttFilterOperator.NotEquals, "Hidden by filter")])
            .Add(c => c.OnTaskReordered, EventCallback.Factory.Create<GanttTaskReorderedArgs>(this, a => reordered.Add(a))));

        await cut.InvokeAsync(() => cut.Instance.OnTreeKeyboardMove("3", -1));

        reordered.Should().ContainSingle(a => a.TargetTaskId == "2", "the hidden task is never the neighbour");

        // Without a handler the move does nothing, announces nothing, and the menu entries are disabled.
        var inert = RenderGantt(TmLayoutMode.Tablet);
        await inert.InvokeAsync(() => inert.Instance.OnTreeKeyboardMove("3", -1));
        inert.Find("[data-testid='gantt-announcer']").TextContent.Trim().Should().BeEmpty();
        await inert.InvokeAsync(() => inert.Instance.OnLongPressContextMenu("3", 10, 10));
        inert.Find("[data-testid='gantt-context-move-up']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task TmGantt_Announcement_Region_Always_Renders_And_Repeats_Are_Distinct()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet, p => p.Add(c => c.OnTaskReordered, EventCallback.Factory.Create<GanttTaskReorderedArgs>(this, _ => { })));
        cut.Find("[data-testid='gantt-announcer']").GetAttribute("role").Should().Be("status");

        await cut.InvokeAsync(() => cut.Instance.OnTreeKeyboardMove("3", -1));
        var first = cut.Find("[data-testid='gantt-announcer']").TextContent;
        await cut.InvokeAsync(() => cut.Instance.OnTreeKeyboardMove("3", -1));

        cut.Find("[data-testid='gantt-announcer']").TextContent.Should().NotBe(first, "an identical repeat must still change the text so it is announced again");
    }

    [Fact]
    public void TmGantt_Switching_To_List_Detaches_The_Timeline_Session()
    {
        var cut = RenderGantt(TmLayoutMode.Tablet);

        cut.Render(p => p.Add(c => c.View, GanttView.List));

        JSInterop.Invocations.Should().Contain(i => i.Identifier == "tmGantt.detachTimeline");
        JSInterop.Invocations.Count(i => i.Identifier == "tmGantt.attachTree").Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void TmGantt_TaskPanel_Tolerates_Null_History()
    {
        var tasks = Tasks();
        var cut = RenderGantt(TmLayoutMode.Tablet, items: tasks, configure: p => p
            .Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto)
            .Add(c => c.ShowTaskPanel, true)
            .Add(c => c.SelectedTask, tasks[1]));

        Tab(cut, "history").Click();

        cut.FindAll("[data-testid='task-section-history']").Should().ContainSingle();
    }

    [Fact]
    public void TmGantt_Dependency_Handle_Owns_Its_Touch_On_Every_Touch_Device()
    {
        var css = GanttCss.Strip(File.ReadAllText(Path.Combine(GanttCss.SchedulerDir, "TmGanttTimeline.razor.css")));
        css.Should().Contain("any-pointer: coarse", "a touch laptop's primary pointer is fine, but a finger on the handle must still link");
    }

    // ── Review fixes (phase 7 follow-up) ───────────────────────────────────────

    /// <summary>Opens the tabbed task sheet on a tablet container and marks the draft dirty through the title field.</summary>
    private (IRenderedComponent<TmGantt> Cut, List<bool> Changes) OpenDirtySheet()
    {
        var changes = new List<bool>();
        var tasks = Tasks();
        var cut = RenderGantt(TmLayoutMode.Tablet, items: tasks, configure: p =>
        {
            p.Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto);
            p.Add(c => c.ShowTaskPanelChanged, EventCallback.Factory.Create<bool>(this, changes.Add));
        });
        TouchTap(cut.FindAll(".tm-gantt__tree-row")[1]);
        cut.FindAll(".tm-drawer.tm-side-panel-sheet").Should().ContainSingle();
        cut.Find("[data-testid='task-title'] input").Change("Renamed draft");
        return (cut, changes);
    }

    [Fact]
    public void TmGantt_Sheet_Close_Over_A_Dirty_Draft_Asks_And_Discard_Closes_It()
    {
        var (cut, changes) = OpenDirtySheet();

        cut.Find(".tm-drawer__close").Click();

        cut.FindAll(".tm-drawer.tm-side-panel-sheet").Should().ContainSingle("the dirty draft keeps the sheet open");
        var dialog = cut.Find(".tm-modal-overlay");
        dialog.TextContent.Should().Contain("Discard changes?").And.Contain("Keep editing");
        changes.Should().ContainSingle(v => v, "no close was raised while the question is open");

        dialog.QuerySelector(".tm-dialog-btn-ok")!.Click();

        cut.FindAll(".tm-drawer").Should().BeEmpty("Discard closes the sheet");
        cut.FindAll(".tm-modal-overlay").Should().BeEmpty();
        changes.Should().Equal([true, false]);
    }

    [Fact]
    public void TmGantt_Sheet_Close_Over_A_Dirty_Draft_Asks_And_KeepEditing_Stays()
    {
        var (cut, changes) = OpenDirtySheet();

        cut.Find(".tm-drawer__close").Click();
        cut.Find(".tm-modal-overlay").QuerySelector(".tm-dialog-btn-cancel")!.Click();

        cut.FindAll(".tm-drawer.tm-side-panel-sheet").Should().ContainSingle("Keep editing returns to the fields");
        cut.FindAll(".tm-modal-overlay").Should().BeEmpty("the question is answered and gone");
        changes.Should().ContainSingle(v => v, "the sheet never closed");
        cut.Find("[data-testid='task-title'] input").GetAttribute("value").Should().Be("Renamed draft", "the draft is untouched");
    }

    [Fact]
    public void TmGantt_Sheet_Close_Without_A_Draft_Does_Not_Ask()
    {
        var changes = new List<bool>();
        var cut = RenderGantt(TmLayoutMode.Tablet, p =>
        {
            p.Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto);
            p.Add(c => c.ShowTaskPanelChanged, EventCallback.Factory.Create<bool>(this, changes.Add));
        });
        TouchTap(cut.FindAll(".tm-gantt__tree-row")[1]);
        cut.FindAll(".tm-drawer.tm-side-panel-sheet").Should().ContainSingle();

        cut.Find(".tm-drawer__close").Click();

        cut.FindAll(".tm-drawer").Should().BeEmpty();
        cut.FindAll(".tm-modal-overlay").Should().BeEmpty("a clean sheet closes straight away");
        changes.Should().Equal([true, false]);
    }

    [Fact]
    public void TmGantt_Mouse_Click_Does_Not_Open_The_Sheet_A_Touch_Tap_Does()
    {
        // A mouse double-click is the row's inline edit: the sheet must not rise over the chart and
        // swallow the second click. The row's pointerup names the pointer the tap came from.
        var cut = RenderGantt(TmLayoutMode.Tablet, p => p.Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto));

        var row = cut.FindAll(".tm-gantt__tree-row")[1];
        row.TriggerEvent("onpointerup", new Microsoft.AspNetCore.Components.Web.PointerEventArgs { PointerType = "mouse" });
        row.TriggerEvent("onclick", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.FindAll(".tm-drawer.tm-side-panel-sheet").Should().BeEmpty("a mouse click selects, it does not raise the sheet");

        TouchTap(cut.FindAll(".tm-gantt__tree-row")[2]);
        cut.FindAll(".tm-drawer.tm-side-panel-sheet").Should().ContainSingle("a finger or a pen tap does");
    }

    [Fact]
    public void TmGantt_TaskPanel_Move_Pair_Disables_At_The_Edge_And_Hides_Without_A_Reorder_Handler()
    {
        var tasks = Tasks();
        var moved = new List<GanttTaskReorderedArgs>();
        var cut = RenderGantt(TmLayoutMode.Tablet, items: tasks, configure: p =>
        {
            p.Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto);
            p.Add(c => c.OnTaskReordered, EventCallback.Factory.Create<GanttTaskReorderedArgs>(this, a => moved.Add(a)));
            p.Add(c => c.ShowTaskPanel, true);
            p.Add(c => c.SelectedTask, tasks[1]); // "Design": first visible child of "Project" — no way up.
        });

        cut.Find("[data-testid='task-action-move-up']").HasAttribute("disabled").Should().BeTrue("the task cannot move up");
        cut.Find("[data-testid='task-action-move-down']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='task-action-move-down']").Click();
        moved.Should().ContainSingle(a => a.TaskId == "2" && a.TargetTaskId == "3");

        // Without a host handler a move is a no-op: the pair hides instead of lying.
        var inert = RenderGantt(TmLayoutMode.Tablet, items: Tasks(), configure: p => p
            .Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto)
            .Add(c => c.ShowTaskPanel, true)
            .Add(c => c.SelectedTask, Tasks()[1]));
        inert.FindAll("[data-testid='task-action-move-up']").Should().BeEmpty();
        inert.FindAll("[data-testid='task-action-move-down']").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_Overlays_Render_Outside_The_Chart_Container()
    {
        // .tm-gantt is a CSS size container (inline-size), so it is the containing block of every
        // fixed-position descendant: the context menu, the sheets and the dialogs live OUTSIDE it.
        var tasks = Tasks();
        var cut = RenderGantt(TmLayoutMode.Tablet, items: tasks, configure: p =>
        {
            p.Add(c => c.TaskPanelMode, GanttTaskPanelMode.Auto);
            p.Add(c => c.FilterPanelMode, GanttFilterPanelMode.Auto);
            p.Add(c => c.AllowDependencyCreation, true);
            p.Add(c => c.ShowTaskPanel, true);
            p.Add(c => c.SelectedTask, tasks[1]);
        });

        var chart = cut.Find(".tm-gantt");
        cut.FindAll(".tm-drawer.tm-side-panel-sheet").Should().ContainSingle();
        cut.Find(".tm-drawer.tm-side-panel-sheet").Closest(".tm-gantt").Should().BeNull("the task sheet is a sibling of the chart root");
        cut.Find(".tm-drawer.tm-side-panel-sheet").ParentElement!.ClassList.Should().Contain("tm-layout", "the overlay is a child of the observer root, next to the chart");

        // Context menu (anchored at clientX/Y).
        cut.InvokeAsync(() => cut.Instance.OnLongPressContextMenu("2", 240, 180));
        cut.Find(".tm-gantt__context-menu-overlay").Closest(".tm-gantt").Should().BeNull();

        // Export dialog.
        cut.Find("[data-testid='gantt-export-btn']").Click();
        cut.Find(".tm-modal-overlay.tm-gantt__export-dialog").Closest(".tm-gantt").Should().BeNull();

        // Filter dialog (Auto on a tablet container).
        cut.Find("[data-testid='gantt-filter-btn']").Click();
        cut.Find(".tm-modal-overlay.tm-gantt__filter-dialog").Closest(".tm-gantt").Should().BeNull();
        chart.QuerySelector(".tm-gantt__filter-dialog").Should().BeNull();
    }
}
