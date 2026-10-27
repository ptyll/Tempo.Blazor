using System.ComponentModel;
using System.Reflection;
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
/// TmGantt redesign, phase 3: the monolith is split into <see cref="TmGanttToolbar"/>,
/// <see cref="TmGanttTree"/>, <see cref="TmGanttTimeline"/> and <see cref="TmGanttSidebar"/>. State stays in
/// <see cref="TmGantt"/>; the tree and the timeline memoize their render against the state they draw, so a
/// toolbar action no longer repaints every bar. Written BEFORE the split: it did not compile against the
/// monolith (the sub-components did not exist).
/// </summary>
public class TmGanttInternalSplitTests : LocalizationTestBase
{
    private static List<TmWorkItem> Tasks() =>
    [
        new() { Id = "1", Title = "Project", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30) },
        new() { Id = "2", Title = "Design", ParentId = "1", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 10), PercentComplete = 100 },
        new() { Id = "3", Title = "Development", ParentId = "1", Start = new DateTime(2024, 6, 11), End = new DateTime(2024, 6, 25), PercentComplete = 50 },
    ];

    private IRenderedComponent<TmGantt> RenderGantt(Action<ComponentParameterCollectionBuilder<TmGantt>>? configure = null) =>
        Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks());
            configure?.Invoke(p);
        });

    // ═══════════════════════════════════════════════════════════════
    // Render isolation
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// A toolbar action that only opens a chrome surface (filter panel, view-settings menu, history drawer,
    /// export / import dialog, task panel) must not repaint the tree or the timeline: the timeline holds one
    /// element per bar, dependency and header cell.
    /// </summary>
    [Theory]
    [InlineData("gantt-filter-btn")]
    [InlineData("gantt-view-settings-btn")]
    [InlineData("gantt-history-btn")]
    [InlineData("gantt-export-btn")]
    [InlineData("gantt-import-btn")]
    [InlineData("gantt-toggle-task-panel")]
    public void Toolbar_Click_Does_Not_Rerender_Timeline(string testId)
    {
        var cut = RenderGantt();
        var timeline = cut.FindComponent<TmGanttTimeline>();
        var tree = cut.FindComponent<TmGanttTree>();
        var timelineRenders = timeline.RenderCount;
        var treeRenders = tree.RenderCount;

        cut.Find($"[data-testid='{testId}']").Click();

        timeline.RenderCount.Should().Be(timelineRenders, "'{0}' is chrome-only: the timeline must not repaint", testId);
        tree.RenderCount.Should().Be(treeRenders, "'{0}' is chrome-only: the tree must not repaint", testId);
    }

    [Fact]
    public void Opening_The_ContextMenu_Does_Not_Rerender_Timeline()
    {
        var cut = RenderGantt();
        var timeline = cut.FindComponent<TmGanttTimeline>();
        var timelineRenders = timeline.RenderCount;

        cut.Find("[data-testid='tree-row-2']").TriggerEvent("oncontextmenu", new MouseEventArgs { ClientX = 10, ClientY = 10 });

        cut.Find(".tm-gantt__context-menu").Should().NotBeNull("the menu is TmGantt's own markup");
        timeline.RenderCount.Should().Be(timelineRenders);
    }

    [Fact]
    public void A_Parent_Parameter_Pass_Still_Repaints_Tree_And_Timeline()
    {
        var cut = RenderGantt();
        var timeline = cut.FindComponent<TmGanttTimeline>();
        var tree = cut.FindComponent<TmGanttTree>();
        var timelineRenders = timeline.RenderCount;
        var treeRenders = tree.RenderCount;

        cut.Render(p => p.Add(c => c.Items, Tasks().Append(new TmWorkItem
        {
            Id = "4", Title = "Added", Start = new DateTime(2024, 6, 12), End = new DateTime(2024, 6, 14),
        }).ToList()));

        timeline.RenderCount.Should().BeGreaterThan(timelineRenders);
        tree.RenderCount.Should().BeGreaterThan(treeRenders);
        cut.FindAll(".tm-gantt__tree-row").Should().HaveCount(4);
        cut.FindAll("[data-testid^='task-bar-']").Should().HaveCount(4);
    }

    // ═══════════════════════════════════════════════════════════════
    // The memoization does not hide state the sub-components draw
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Selecting_A_Task_Repaints_Tree_Timeline_And_Panel()
    {
        var cut = RenderGantt(p => p.Add(c => c.ShowTaskPanel, true));

        cut.Find("[data-testid='tree-row-2']").Click();

        cut.Find("[data-testid='tree-row-2']").ClassList.Should().Contain("tm-gantt__tree-row--selected");
        cut.Find("[data-testid='task-bar-2']").ClassList.Should().Contain("tm-gantt__bar--selected");
        cut.Find(".tm-gantt-task-panel").TextContent.Should().Contain("Design", "the click's receiver is TmGantt, so its panel follows the selection");
    }

    [Fact]
    public void Clicking_A_Bar_Selects_The_Task_Through_The_Owner()
    {
        TmWorkItem? selected = null;
        var cut = RenderGantt(p => p.Add(c => c.OnTaskSelected, EventCallback.Factory.Create<TmWorkItem>(this, t => selected = t)));

        cut.Find("[data-testid='task-bar-3']").Click();

        selected!.Id.Should().Be("3");
        cut.Find("[data-testid='task-bar-3']").ClassList.Should().Contain("tm-gantt__bar--selected");
    }

    [Fact]
    public void ZoomIn_Repaints_The_Timeline()
    {
        var cut = RenderGantt();
        string Width() => cut.Find(".tm-gantt__timeline-content").GetAttribute("style")!;
        var before = Width();

        cut.Find("[data-testid='gantt-zoom-in']").Click();

        Width().Should().NotBe(before, "the zoom changes the pixel width of the whole timeline");
        cut.Find(".tm-gantt__zoom-level").TextContent.Should().Be("125%");
    }

    [Fact]
    public void Toggling_ProgressLabel_In_The_Menu_Repaints_The_Timeline()
    {
        // ShowProgressLabel is mutated in place on ViewSettings and does not refresh the data: only the
        // snapshot of the drawn state can see it.
        var cut = RenderGantt();
        cut.FindAll("[data-testid='bar-progress-label']").Should().BeEmpty();

        cut.Find("[data-testid='gantt-view-settings-btn']").Click();
        cut.Find("[data-testid='vset-show-progress']").Change(true);

        cut.FindAll("[data-testid='bar-progress-label']").Should().NotBeEmpty();
    }

    [Fact]
    public void Toggling_TodayMarker_In_The_Menu_Repaints_The_Timeline()
    {
        var cut = RenderGantt();
        cut.FindAll("[data-testid='today-marker']").Should().NotBeEmpty();

        cut.Find("[data-testid='gantt-view-settings-btn']").Click();
        cut.Find("[data-testid='vset-show-today']").Change(false);

        cut.FindAll("[data-testid='today-marker']").Should().BeEmpty();
    }

    [Fact]
    public void Committing_An_Inline_Title_Edit_Repaints_The_Bar_Title()
    {
        // The edit mutates the task in place; the timeline shows the title on the bar.
        var cut = RenderGantt();
        cut.Find("[data-testid='tree-cell-title-3']").DoubleClick();
        var input = cut.Find("input[data-testid='inline-edit-input']");
        input.Change("Renamed");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.Find("[data-testid='tree-cell-title-3']").TextContent.Trim().Should().Be("Renamed");
        cut.Find("[data-testid='task-bar-3']").GetAttribute("title").Should().StartWith("Renamed");
    }

    [Fact]
    public async Task Inline_Edit_Closed_After_A_Delayed_OnTaskUpdated_Closes_The_Editor_In_The_Tree()
    {
        // The commit awaits the host callback and only then clears the inline-edit state. The tree renders
        // while the edit is still open (before the await completes); the state that closes the editor changes
        // later, so the tree's render gate must compare against what it painted at that first render.
        var gate = new TaskCompletionSource();
        var cut = RenderGantt(p => p.Add(c => c.OnTaskUpdated,
            EventCallback.Factory.Create<TmWorkItem>(this, async _ => await gate.Task)));

        cut.Find("[data-testid='tree-cell-title-3']").DoubleClick();
        var input = cut.Find("input[data-testid='inline-edit-input']");
        input.Change("Renamed");
        var keyDown = input.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        cut.FindAll("input[data-testid='inline-edit-input']").Should().NotBeEmpty("the host callback has not returned yet");

        gate.SetResult();
        await keyDown;

        cut.WaitForAssertion(() => cut.FindAll("input[data-testid='inline-edit-input']").Should().BeEmpty());
        cut.Find("[data-testid='tree-cell-title-3']").TextContent.Trim().Should().Be("Renamed");
    }

    [Fact]
    public void Bulk_Selection_Repaints_The_Tree_Rows_And_The_Bulk_Toolbar()
    {
        var cut = RenderGantt(p => p.Add(c => c.AllowBulkSelect, true));

        cut.Find("[data-testid='bulk-select-2']").Change(true);
        cut.Find(".tm-gantt__bulk-toolbar-count").TextContent.Should().StartWith("1");
        cut.Find("[data-testid='bulk-select-2']").HasAttribute("checked").Should().BeTrue();

        cut.Find("[data-testid='bulk-clear']").Click();

        cut.FindAll(".tm-gantt__bulk-toolbar").Should().BeEmpty();
        cut.Find("[data-testid='bulk-select-2']").HasAttribute("checked").Should().BeFalse();
    }

    [Fact]
    public void Dependency_Selection_Repaints_Only_The_Selected_Line()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, Tasks())
            .Add(c => c.DependencyItems, [new GanttDependency { Id = "d1", FromId = "2", ToId = "3" }]));

        cut.Find("[data-testid='dep-line-d1']").Click();
        cut.Find("[data-testid='dep-line-d1']").ClassList.Should().Contain("tm-gantt__dep-line--selected");

        cut.Find("[data-testid='dep-line-d1']").Click();
        cut.Find("[data-testid='dep-line-d1']").ClassList.Should().NotContain("tm-gantt__dep-line--selected");
    }

    [Fact]
    public void Panning_State_Reaches_The_Timeline_Root()
    {
        var cut = RenderGantt();

        cut.Find(".tm-gantt__timeline").MouseDown(new MouseEventArgs { ClientX = 10, Button = 0 });
        cut.Find(".tm-gantt__timeline").ClassList.Should().Contain("tm-gantt__timeline--panning");

        cut.Find(".tm-gantt__timeline").MouseUp(new MouseEventArgs { Button = 0 });
        cut.Find(".tm-gantt__timeline").ClassList.Should().NotContain("tm-gantt__timeline--panning");
    }

    [Fact]
    public void Sidebar_Panel_Toggle_Goes_Through_The_Owner()
    {
        var cut = RenderGantt();

        cut.Find("[data-testid='sidebar-btn-tasks']").Click();
        cut.Find("[data-testid='sidebar-panel-tasks']").Should().NotBeNull();
        cut.Find("[data-testid='sidebar-btn-tasks']").GetAttribute("aria-pressed").Should().Be("true");

        cut.Find("[data-testid='sidebar-btn-tasks']").Click();
        cut.FindAll("[data-testid='sidebar-panel-tasks']").Should().BeEmpty();
    }

    [Fact]
    public void TestIdPrefix_Of_The_Gantt_Reaches_Every_Sub_Component()
    {
        var cut = RenderGantt(p => p.Add(c => c.TestIdPrefix, "g1").Add(c => c.AllowBulkSelect, true));

        foreach (var id in new[] { "g1-gantt-add-task", "g1-gantt-view-week", "g1-gantt-tree", "g1-gantt-timeline",
                     "g1-gantt-sidebar", "g1-tree-row-2", "g1-task-bar-2", "g1-today-marker", "g1-bulk-select-2" })
        {
            cut.FindAll($"[data-testid='{id}']").Should().NotBeEmpty($"'{id}' must be prefixed by the owning Gantt");
        }
    }

    /// <summary>
    /// The gate must not hide an in-place edit of a task: a host (or the task panel) that mutates a task and then
    /// re-renders the Gantt for an unrelated reason - here the filter button - still sees the new values in the
    /// tree AND on the bars, exactly as the monolith repainted them.
    /// </summary>
    [Fact]
    public void An_InPlace_Task_Edit_Is_Painted_On_The_Next_Gantt_Render()
    {
        var tasks = Tasks();
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, tasks));

        tasks[2].Title = "Renamed in place";
        tasks[2].PercentComplete = 80;
        tasks[2].Status = TmWorkItemStatus.Done;
        cut.Find("[data-testid='gantt-filter-btn']").Click();

        cut.Find("[data-testid='tree-cell-title-3']").TextContent.Trim().Should().Be("Renamed in place");
        cut.Find("[data-testid='task-bar-3']").GetAttribute("title").Should().Be("Renamed in place (80%)");
        cut.Find("[data-testid='task-bar-3']").ClassList.Should().Contain("tm-gantt__bar--completed");
    }
    // ═══════════════════════════════════════════════════════════════
    // Structure
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Dom_Order_Is_Toolbar_Content_With_Sidebar_Tree_Timeline()
    {
        var cut = RenderGantt();

        var root = cut.Find(".tm-gantt");
        root.Children.Select(c => c.ClassList.FirstOrDefault()).Should().Equal("tm-gantt__toolbar", "tm-gantt__content");

        var content = cut.Find(".tm-gantt__content");
        content.Children.Select(c => c.ClassList.First()).Should().Equal("tm-gantt__sidebar", "tm-gantt__main");

        cut.Find(".tm-gantt__main").Children.Select(c => c.ClassList.First()).Should().Equal("tm-gantt__tree", "tm-gantt__timeline");
    }

    [Theory]
    [InlineData(typeof(TmGanttToolbar))]
    [InlineData(typeof(TmGanttTree))]
    [InlineData(typeof(TmGanttTimeline))]
    [InlineData(typeof(TmGanttSidebar))]
    public void SubComponents_Are_Hidden_From_The_Editor_And_Take_Only_The_Owner(Type component)
    {
        // Razor cannot emit an internal component (it generates `public partial class`), so the contract is
        // "public but never a host's to place": EditorBrowsable(Never) on the type and on the one parameter.
        component.GetCustomAttribute<EditorBrowsableAttribute>()!.State.Should().Be(EditorBrowsableState.Never);

        var parameters = component.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
            .ToList();
        parameters.Should().ContainSingle(p => p.Name == "Owner" && p.PropertyType == typeof(TmGantt));
        parameters.Should().HaveCount(1, "state stays in TmGantt; the sub-component reads it through the owner (no second source of truth)");
        parameters[0].GetCustomAttribute<EditorBrowsableAttribute>()!.State.Should().Be(EditorBrowsableState.Never);
    }

    [Fact]
    public void TmGantt_Razor_Stays_Under_400_Lines()
    {
        var path = Path.Combine(ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "Components", "Scheduler", "TmGantt.razor");

        File.ReadLines(path).Count().Should().BeLessThan(400,
            "the toolbar, tree, timeline and sidebar markup live in their own components now");
    }

    [Theory]
    [InlineData("TmGanttToolbar")]
    [InlineData("TmGanttTree")]
    [InlineData("TmGanttTimeline")]
    [InlineData("TmGanttSidebar")]
    public void Each_SubComponent_Has_Its_Own_Markup_CodeBehind_And_Stylesheet(string name)
    {
        var dir = Path.Combine(ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "Components", "Scheduler");

        foreach (var extension in new[] { ".razor", ".razor.cs", ".razor.css" })
        {
            File.Exists(Path.Combine(dir, name + extension)).Should().BeTrue($"{name}{extension}");
        }
    }

    [Fact]
    public void Public_Surface_Of_TmGantt_Is_Unchanged()
    {
        // The split is a refactor: not one [Parameter] of TmGantt may appear, move or disappear.
        var parameters = typeof(TmGantt).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null || p.GetCustomAttribute<CascadingParameterAttribute>() is not null)
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        parameters.Should().Equal(ExpectedParameters);
    }

    private static readonly string[] ExpectedParameters =
    [
        "ActiveBaselineId", "ActiveTimerTaskId", "AdditionalAttributes", "AllowBulkSelect", "AllowDependencyCreation", "AutoSchedule",
        "Baselines", "Class", "Columns", "ContainerWidth", "CustomFields", "DataTestId", "DependencyItems", "Filters", "History",
        "Items", "NotificationSettings", "OnAttachmentRemoved", "OnAttachmentUpload", "OnAutoScheduled", "OnBaselineSaved",
        "OnBulkUpdate", "OnColumnsChanged", "OnCustomFieldChanged", "OnDataSorted", "OnDependencyAdded", "OnDependencyRemoved",
        "OnDescriptionChanged", "OnExportRequested", "OnFiltersChanged", "OnImportCompleted", "OnImportError", "OnNewReportRequested",
        "OnNotificationTriggered", "OnReportRun", "OnResourceCalendarChanged", "OnRollbackRequested", "OnStatusChanged",
        "OnTaskAdded", "OnTaskInserted", "OnTaskRemoved", "OnTaskReordered", "OnTaskSelected", "OnTaskUpdated", "OnTimeTravelRequested",
        "OnTimerStarted", "OnTimerStopped", "OnViewSettingsChanged", "OnVirtualResourceAdded", "RealtimeConnection", "Reports",
        "ResourceCalendars", "SelectedTask", "ShowCriticalPath", "ShowOverdueHighlight", "ShowSidebar", "ShowTaskPanel",
        "ShowWorkloadPanel", "TestIdPrefix", "View", "ViewChanged", "ViewSettings", "WorkItemSource", "WorkingSchedule",
        "WorkloadDisplayMode", "ZoomPreset",
    ];
}
