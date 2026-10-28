using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TmGantt redesign, phase 4 (desktop structure): the toolbar is a grouped <c>TmToolbar</c> with the core
/// label mechanism, the "+ Add task" split button, the filter badge and the quick search; the sidebar can
/// be a tab strip; the header has host slots; the grid gained Duration / Priority columns, a column menu
/// and the treegrid keyboard contract. Written BEFORE the implementation.
/// </summary>
public class TmGanttDesktopStructureTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private static TmWorkItem Task(string id, string title, string? parentId = null, DateTime? start = null, DateTime? end = null) => new()
    {
        Id = id,
        Title = title,
        ParentId = parentId,
        Start = start ?? new DateTime(2024, 6, 3),
        End = end ?? new DateTime(2024, 6, 10),
    };

    private static List<TmWorkItem> Tasks() =>
    [
        new() { Id = "1", Title = "Project", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30), IsExpanded = true },
        new() { Id = "2", Title = "Design", ParentId = "1", Start = new DateTime(2024, 6, 3), End = new DateTime(2024, 6, 6), Priority = TmWorkItemPriority.High },
        new() { Id = "3", Title = "Development", ParentId = "1", Start = new DateTime(2024, 6, 11), End = new DateTime(2024, 6, 25), PercentComplete = 50 },
        new() { Id = "4", Title = "Launch", Start = new DateTime(2024, 6, 30), End = new DateTime(2024, 6, 30), IsMilestone = true },
    ];

    private IRenderedComponent<TmGantt> RenderGantt(Action<ComponentParameterCollectionBuilder<TmGantt>>? configure = null) =>
        Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks());
            configure?.Invoke(p);
        });

    private static List<GanttColumnDefinition> Columns(params GanttColumnKey[] keys) =>
        keys.Select((key, order) => new GanttColumnDefinition { Key = key, Visible = true, Order = order }).ToList();

    // ═══════════════════════════════════════════════════════════════
    // Toolbar: role, groups, labels
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Toolbar_Has_Role_Toolbar_And_Groups()
    {
        var cut = RenderGantt();

        var toolbar = cut.Find(".tm-gantt__toolbar [role='toolbar']");
        toolbar.GetAttribute("aria-label").Should().Be("Gantt toolbar");

        var groups = toolbar.QuerySelectorAll("[role='group'][data-tm-group]")
            .Where(g => g.ParentElement?.ClassList.Contains("tm-segment") != true && !g.ClassList.Contains("tm-gantt__segment"))
            .ToList();
        groups.Select(g => g.GetAttribute("data-tm-group")).Should().Equal("create", "structure", "time", "panels", "data", "settings");
        groups.Select(g => g.GetAttribute("aria-label")).Should().Equal("Create", "Structure", "Time", "Panels", "Data", "Settings");
    }

    [Fact]
    public void TmGantt_Toolbar_Keeps_Every_Control_Test_Id()
    {
        var cut = RenderGantt();

        foreach (var id in new[]
                 {
                     "gantt-add-task", "gantt-expand-all", "gantt-collapse-all", "gantt-view-day", "gantt-view-week", "gantt-view-month",
                     "gantt-cascade-sort", "gantt-fit-to-screen", "gantt-zoom-out", "gantt-zoom-slider", "gantt-zoom-in",
                     "gantt-toggle-task-panel", "gantt-filter-btn", "gantt-export-btn", "gantt-import-btn", "gantt-history-btn",
                     "gantt-view-settings-btn",
                 })
        {
            cut.FindAll($"[data-testid='{id}']").Should().NotBeEmpty(id);
        }
    }

    /// <summary>G14: the default stays today's icon row - no label text appears without opting in.</summary>
    [Fact]
    public void TmGantt_ToolbarLabels_Auto_Default_IconsOnly_Today()
    {
        var cut = RenderGantt();

        cut.Instance.ToolbarLabels.Should().Be(ToolbarLabels.Icons);
        var expand = cut.Find("button[data-testid='gantt-expand-all']");
        expand.QuerySelector(".tm-toolbar-btn-text").Should().BeNull("icon buttons show no text by default");
        expand.GetAttribute("aria-label").Should().Be("Expand all");
        cut.Find("button[data-testid='gantt-export-btn']").QuerySelector(".tm-toolbar-btn-text").Should().BeNull();
    }

    /// <summary>The pre-redesign TmButton always carried a title; hosts and E2E select toolbar buttons by it.</summary>
    [Theory]
    [InlineData(ToolbarLabels.Icons)]
    [InlineData(ToolbarLabels.IconsWithText)]
    public void TmGantt_Toolbar_Buttons_Keep_Their_Title_Whatever_The_Labels(ToolbarLabels labels)
    {
        var cut = RenderGantt(p => p.Add(c => c.ToolbarLabels, labels));

        foreach (var (id, title) in new[]
                 {
                     ("gantt-expand-all", "Expand all"), ("gantt-collapse-all", "Collapse all"), ("gantt-cascade-sort", "Sort by date"),
                     ("gantt-fit-to-screen", "Fit to screen"), ("gantt-zoom-out", "Zoom out"), ("gantt-zoom-in", "Zoom in"),
                     ("gantt-toggle-task-panel", "Toggle task panel"), ("gantt-filter-btn", "Filter"), ("gantt-history-btn", "History"),
                     ("gantt-export-btn", "Export"), ("gantt-import-btn", "Import"), ("gantt-view-settings-btn", "View settings"),
                 })
        {
            var button = cut.Find($"button[data-testid='{id}']");
            button.GetAttribute("title").Should().Be(title, id);
            button.GetAttribute("aria-label").Should().Be(title, id);
        }
    }

    [Fact]
    public void TmGantt_ToolbarLabels_IconsWithText_Renders_Visible_Labels()
    {
        var cut = RenderGantt(p => p.Add(c => c.ToolbarLabels, ToolbarLabels.IconsWithText));

        cut.Find("button[data-testid='gantt-expand-all'] .tm-toolbar-btn-text").TextContent.Trim().Should().Be("Expand all");
        cut.Find("button[data-testid='gantt-export-btn'] .tm-toolbar-btn-text").TextContent.Trim().Should().Be("Export");
        cut.Find("button[data-testid='gantt-view-settings-btn'] .tm-toolbar-btn-text").TextContent.Trim().Should().Be("View settings");
        cut.Find("button[data-testid='gantt-filter-btn'] .tm-toolbar-btn-text").TextContent.Trim().Should().Be("Filter");
    }

    // ═══════════════════════════════════════════════════════════════
    // + Add task split button
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_AddTask_Split_Offers_Above_Below_Child()
    {
        var inserted = new List<GanttTaskInsertedArgs>();
        var cut = RenderGantt(p => p.Add(c => c.OnTaskInserted, EventCallback.Factory.Create<GanttTaskInsertedArgs>(this, inserted.Add)));
        cut.Find("[data-testid='tree-row-2']").Click();

        cut.Find("[data-testid='gantt-add-task-split'] .tm-split-button__toggle").Click();
        var items = cut.FindAll(".tm-split-button__dropdown .tm-context-menu__item");
        items.Select(i => i.TextContent.Trim()).Should().Equal("Add task above", "Add task below", "Add child task");

        items[2].Click();
        cut.WaitForAssertion(() => inserted.Should().ContainSingle());
        inserted[0].Position.Should().Be(GanttInsertPosition.Child);
        inserted[0].ReferenceTaskId.Should().Be("2");
        inserted[0].Task.ParentId.Should().Be("2");

        cut.Find("[data-testid='tree-row-2']").Click();
        cut.Find("[data-testid='gantt-add-task-split'] .tm-split-button__toggle").Click();
        cut.FindAll(".tm-split-button__dropdown .tm-context-menu__item")[0].Click();
        cut.WaitForAssertion(() => inserted.Should().HaveCount(2));
        inserted[1].Position.Should().Be(GanttInsertPosition.Above);
        inserted[1].Task.ParentId.Should().Be("1", "an inserted-above task is a sibling of the reference (task 2 is a child of 1)");
    }

    [Fact]
    public void TmGantt_AddTask_Split_Primary_Adds_At_The_End_And_Menu_Needs_A_Selection()
    {
        var inserted = new List<GanttTaskInsertedArgs>();
        var cut = RenderGantt(p => p.Add(c => c.OnTaskInserted, EventCallback.Factory.Create<GanttTaskInsertedArgs>(this, inserted.Add)));

        cut.Find("[data-testid='gantt-add-task']").TextContent.Trim().Should().Be("Add task");
        cut.Find("[data-testid='gantt-add-task-split'] .tm-split-button__toggle").Click();
        cut.FindAll(".tm-split-button__dropdown .tm-context-menu__item")
            .Should().OnlyContain(i => i.HasAttribute("disabled"), "there is nothing to insert relative to without a selected task");
        cut.Find("[data-testid='gantt-add-task-split'] .tm-split-button__toggle").Click();

        cut.Find("button[data-testid='gantt-add-task']").Click();
        inserted.Should().ContainSingle().Which.Position.Should().Be(GanttInsertPosition.End);
    }

    // ═══════════════════════════════════════════════════════════════
    // Filter badge, quick search
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Filter_Button_Shows_Active_Count_Badge()
    {
        var none = RenderGantt();
        none.FindAll("button[data-testid='gantt-filter-btn'] .tm-toolbar-btn-badge").Should().BeEmpty("no filter, no badge");

        var cut = RenderGantt(p => p.Add(c => c.Filters,
        [
            new GanttFilter("Title", GanttFilterOperator.Contains, "s"),
            new GanttFilter("Status", GanttFilterOperator.Equals, "Open"),
        ]));

        cut.Find("button[data-testid='gantt-filter-btn'] .tm-toolbar-btn-badge").TextContent.Trim().Should().Be("2");
        cut.Find("button[data-testid='gantt-filter-btn']").GetAttribute("aria-label").Should().Be("Filter (2 active)");
    }

    [Fact]
    public void TmGantt_QuickSearch_Is_Opt_In()
    {
        RenderGantt().FindAll("input[type='search']").Should().BeEmpty();
        RenderGantt(p => p.Add(c => c.ShowQuickSearch, true)).FindAll(".tm-gantt__toolbar input[type='search']").Should().ContainSingle();
    }

    [Fact]
    public async Task TmGantt_QuickSearch_Updates_Title_Contains_Filter()
    {
        var time = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        IReadOnlyList<GanttFilter>? changed = null;
        var cut = RenderGantt(p => p
            .Add(c => c.ShowQuickSearch, true)
            .Add(c => c.OnFiltersChanged, EventCallback.Factory.Create<IReadOnlyList<GanttFilter>>(this, f => changed = f)));

        var input = cut.Find(".tm-gantt__toolbar input[type='search']");
        input.Input("Dev");
        time.Advance(TimeSpan.FromSeconds(1));
        await cut.InvokeAsync(() => { });

        cut.WaitForAssertion(() => changed.Should().NotBeNull());
        changed.Should().ContainSingle().Which.Should().Be(new GanttFilter("Title", GanttFilterOperator.Contains, "Dev"));
        cut.FindAll("[data-testid^='tree-row-']").Select(r => r.GetAttribute("data-testid"))
            .Should().Equal("tree-row-1", "tree-row-3");

        cut.Find(".tm-gantt__toolbar input[type='search']").Input("");
        time.Advance(TimeSpan.FromSeconds(1));
        await cut.InvokeAsync(() => { });
        cut.WaitForAssertion(() => changed.Should().BeEmpty("clearing the box removes the filter"));
        cut.FindAll("[data-testid^='tree-row-']").Should().HaveCount(4);
    }

    [Fact]
    public async Task TmGantt_QuickSearch_Keeps_Other_Filters()
    {
        var time = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        IReadOnlyList<GanttFilter>? changed = null;
        var cut = RenderGantt(p => p
            .Add(c => c.ShowQuickSearch, true)
            .Add(c => c.Filters, [new GanttFilter("Status", GanttFilterOperator.NotEquals, "Closed")])
            .Add(c => c.OnFiltersChanged, EventCallback.Factory.Create<IReadOnlyList<GanttFilter>>(this, f => changed = f)));

        cut.Find(".tm-gantt__toolbar input[type='search']").Input("Des");
        time.Advance(TimeSpan.FromSeconds(1));
        await cut.InvokeAsync(() => { });

        cut.WaitForAssertion(() => changed.Should().NotBeNull());
        changed.Should().BeEquivalentTo(new[]
        {
            new GanttFilter("Status", GanttFilterOperator.NotEquals, "Closed"),
            new GanttFilter("Title", GanttFilterOperator.Contains, "Des"),
        });
    }

    [Fact]
    public async Task TmGantt_QuickSearch_Keeps_Text_When_Host_Does_Not_Write_Filters_Back()
    {
        var time = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        var calls = 0;
        var cut = RenderGantt(p => p
            .Add(c => c.ShowQuickSearch, true)
            .Add(c => c.OnFiltersChanged, EventCallback.Factory.Create<IReadOnlyList<GanttFilter>>(this, _ => calls++)));

        cut.Find(".tm-gantt__toolbar input[type='search']").Input("Dev");
        time.Advance(TimeSpan.FromSeconds(1));
        await cut.InvokeAsync(() => { });
        cut.WaitForAssertion(() => calls.Should().BeGreaterThan(0));

        // The host re-renders the Gantt with the same (unchanged) Filters reference.
        cut.Render(p => p.Add(c => c.Class, "host-rerender"));

        cut.Find(".tm-gantt__toolbar input[type='search']").GetAttribute("value").Should().Be("Dev");
        cut.FindAll("[data-testid^='tree-row-']").Select(r => r.GetAttribute("data-testid"))
            .Should().Equal("tree-row-1", "tree-row-3");
    }

    [Fact]
    public async Task TmGantt_FilterPanel_Removing_Quick_Search_Filter_Clears_The_Box()
    {
        var time = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        var cut = RenderGantt(p => p.Add(c => c.ShowQuickSearch, true));

        cut.Find(".tm-gantt__toolbar input[type='search']").Input("Dev");
        time.Advance(TimeSpan.FromSeconds(1));
        await cut.InvokeAsync(() => { });
        cut.WaitForAssertion(() => cut.FindAll("[data-testid^='tree-row-']").Should().HaveCount(2));

        cut.Find("button[data-testid='gantt-filter-btn']").Click();
        cut.Find(".tm-gantt__filter-remove").Click();
        cut.Find("[data-testid='filter-apply']").Click();

        cut.Find(".tm-gantt__toolbar input[type='search']").GetAttribute("value").Should().BeNullOrEmpty();
        cut.FindAll("[data-testid^='tree-row-']").Should().HaveCount(4);
    }

    // ═══════════════════════════════════════════════════════════════
    // Sidebar mode and header
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_SidebarMode_Tabs_Renders_Tablist()
    {
        var cut = RenderGantt(p => p.Add(c => c.SidebarMode, GanttSidebarMode.Tabs));

        var tablist = cut.Find(".tm-gantt [role='tablist']");
        tablist.GetAttribute("aria-label").Should().Be("Gantt panels");
        var tabs = tablist.QuerySelectorAll("[role='tab']").ToList();
        tabs.Select(t => t.TextContent.Trim()).Should().Equal("Tasks", "Workload", "History", "Notifications", "Reports");
        tabs.Select(t => t.GetAttribute("aria-selected")).Should().Equal("true", "false", "false", "false", "false");
        cut.FindAll("nav.tm-gantt__sidebar").Should().BeEmpty("the icon rail is replaced by the tab strip");

        tablist.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        cut.FindAll("[role='tab']").Select(t => t.GetAttribute("aria-selected")).Should().Equal("false", "true", "false", "false", "false");
        cut.Find("[data-testid='sidebar-panel-workload']").Should().NotBeNull("the selected tab opens its panel");

        cut.FindAll("[role='tab']")[0].Click();
        cut.FindAll("[data-testid='sidebar-panel-workload']").Should().BeEmpty("the Tasks tab is the Gantt itself");
    }

    [Fact]
    public void TmGantt_SidebarMode_Tabs_Home_End_And_Wrap_Move_The_Selection()
    {
        var cut = RenderGantt(p => p.Add(c => c.SidebarMode, GanttSidebarMode.Tabs));
        string Selected() => cut.FindAll("[role='tab']").Single(t => t.GetAttribute("aria-selected") == "true").GetAttribute("data-testid")!;

        cut.Find("[role='tablist']").KeyDown(new KeyboardEventArgs { Key = "End" });
        Selected().Should().Be("panel-tab-reports");
        cut.Find("[role='tablist']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Selected().Should().Be("panel-tab-tasks", "ArrowRight wraps from the last tab to the first");
        cut.Find("[role='tablist']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        Selected().Should().Be("panel-tab-reports", "ArrowLeft wraps from the first tab to the last");
        cut.Find("[role='tablist']").KeyDown(new KeyboardEventArgs { Key = "Home" });
        Selected().Should().Be("panel-tab-tasks");
        cut.FindAll("[role='tab']").Select(t => t.GetAttribute("tabindex")).Should().Equal("0", "-1", "-1", "-1", "-1");
        JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().Be(4, "selection and DOM focus move together on every key");
    }

    [Fact]
    public void TmGantt_SidebarMode_Tabs_Honors_ShowSidebar_False()
    {
        var cut = RenderGantt(p => p.Add(c => c.SidebarMode, GanttSidebarMode.Tabs).Add(c => c.ShowSidebar, false));

        cut.FindAll("[role='tablist']").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_SidebarMode_Default_Rail_Unchanged()
    {
        var cut = RenderGantt();

        cut.Instance.SidebarMode.Should().Be(GanttSidebarMode.Rail);
        cut.FindAll("[role='tablist']").Should().BeEmpty();
        var rail = cut.Find("nav.tm-gantt__sidebar");
        rail.QuerySelectorAll("button").Select(b => b.GetAttribute("data-testid"))
            .Should().Equal("sidebar-btn-tasks", "sidebar-btn-workload", "sidebar-btn-history", "sidebar-btn-notifications", "sidebar-btn-reports");
        cut.FindAll(".tm-gantt__header").Should().BeEmpty("no header without opt-in");
        cut.Find(".tm-gantt").Children.Select(c => c.ClassList.First()).Should().Equal("tm-gantt__toolbar", "tm-gantt__content");
    }

    [Fact]
    public void TmGantt_HeaderStart_And_HeaderActions_Render()
    {
        var cut = RenderGantt(p => p
            .Add(c => c.Title, "Project Aurora")
            .Add(c => c.Subtitle, "Q2 plan")
            .Add(c => c.HeaderStart, b => b.AddMarkupContent(0, "<span data-testid='host-start'>start</span>"))
            .Add(c => c.HeaderActions, b => b.AddMarkupContent(0, "<button data-testid='host-action'>share</button>")));

        var header = cut.Find(".tm-gantt__header");
        header.QuerySelector(".tm-gantt__title")!.TextContent.Should().Be("Project Aurora");
        header.QuerySelector(".tm-gantt__subtitle")!.TextContent.Should().Be("Q2 plan");
        header.QuerySelector("[data-testid='host-start']").Should().NotBeNull();
        header.QuerySelector(".tm-gantt__header-actions [data-testid='host-action']").Should().NotBeNull();
        cut.Find(".tm-gantt").Children.Select(c => c.ClassList.First()).Should().Equal("tm-gantt__header", "tm-gantt__toolbar", "tm-gantt__content");
    }

    [Fact]
    public void TmGantt_ShowHeader_False_Hides_Header()
    {
        var cut = RenderGantt(p => p
            .Add(c => c.Title, "Project Aurora")
            .Add(c => c.HeaderActions, b => b.AddMarkupContent(0, "<button data-testid='host-action'>share</button>"))
            .Add(c => c.ShowHeader, false));

        cut.FindAll(".tm-gantt__header").Should().BeEmpty();
        cut.FindAll("[data-testid='host-action']").Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // Columns
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Renders_Duration_Column_When_Configured()
    {
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var tasks = new List<TmWorkItem>
        {
            Task("1", "Three", start: new DateTime(2024, 6, 3), end: new DateTime(2024, 6, 6)),
            Task("2", "One", start: new DateTime(2024, 6, 3), end: new DateTime(2024, 6, 4)),
        };
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, tasks)
            .Add(c => c.Columns, Columns(GanttColumnKey.Title, GanttColumnKey.Duration)));

        cut.Find(".tm-gantt__tree-header").TextContent.Should().Contain("Duration");
        cut.Find("[data-testid='tree-cell-duration-1']").TextContent.Trim().Should().Be("3 days");
        cut.Find("[data-testid='tree-cell-duration-2']").TextContent.Trim().Should().Be("1 day");
    }

    [Fact]
    public void TmGantt_Duration_Column_Is_Localized_With_Plural_Forms()
    {
        UseCzechLocalization();
        var tasks = new List<TmWorkItem>
        {
            Task("1", "Tři", start: new DateTime(2024, 6, 3), end: new DateTime(2024, 6, 6)),
            Task("2", "Pět", start: new DateTime(2024, 6, 3), end: new DateTime(2024, 6, 8)),
        };
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, tasks)
            .Add(c => c.Columns, Columns(GanttColumnKey.Title, GanttColumnKey.Duration)));

        cut.Find("[data-testid='tree-cell-duration-1']").TextContent.Trim().Should().Be("3 dny");
        cut.Find("[data-testid='tree-cell-duration-2']").TextContent.Trim().Should().Be("5 dnů");
    }

    [Fact]
    public void TmGantt_Renders_Priority_Column_With_Icon()
    {
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Tasks())
            .Add(c => c.Columns, Columns(GanttColumnKey.Title, GanttColumnKey.Priority)));

        var cell = cut.Find("[data-testid='tree-cell-priority-2']");
        cell.TextContent.Trim().Should().Be("High");
        var glyph = cell.QuerySelector(".tm-gantt__priority-glyph--high");
        glyph.Should().NotBeNull();
        glyph!.GetAttribute("aria-hidden").Should().Be("true");
        glyph.QuerySelector(".tm-icon").Should().NotBeNull("the glyph is an icon, not a colour dot alone");
        cut.Find(".tm-gantt__tree-header").TextContent.Should().Contain("Priority");
    }

    [Fact]
    public void TmGantt_Tree_Has_No_Inline_Width_By_Default()
    {
        RenderGantt().Find(".tm-gantt__tree").HasAttribute("style").Should().BeFalse("the default grid keeps its stylesheet width");
    }

    /// <summary>Duration and Priority do not fit the 420px default grid: the tree sizes to its columns so the header and the cells stay aligned.</summary>
    [Fact]
    public void TmGantt_Tree_Grows_To_Fit_The_Duration_And_Priority_Columns()
    {
        string Width(IReadOnlyList<GanttColumnDefinition> columns)
        {
            var cut = RenderGantt(p => p.Add(c => c.Columns, columns));
            var style = cut.Find(".tm-gantt__tree").GetAttribute("style");
            style.Should().NotBeNull();
            return style!;
        }

        var narrow = Width(Columns(GanttColumnKey.Title, GanttColumnKey.Status, GanttColumnKey.Start, GanttColumnKey.End, GanttColumnKey.Duration, GanttColumnKey.Priority));
        narrow.Should().Contain("width: 664px", "24 padding + 220 title + 90 status + 80 start + 80 end + 80 duration + 90 priority");

        var withBulk = RenderGantt(p => p.Add(c => c.AllowBulkSelect, true)
            .Add(c => c.Columns, Columns(GanttColumnKey.Title, GanttColumnKey.Priority)));
        withBulk.Find(".tm-gantt__tree").GetAttribute("style").Should().Contain("width: 366px", "24 + 32 checkbox + 220 title + 90 priority");
    }

    /// <summary>Custom fields are real cells (100px each): the grid counts them, scrolls horizontally past 70% and never clips them.</summary>
    [Fact]
    public void TmGantt_Tree_Width_Counts_Custom_Fields_And_Caps_At_70_Percent()
    {
        var fields = Enumerable.Range(1, 6).Select(i => new TmCustomFieldDefinition { Id = $"cf{i}", Name = $"Field {i}" }).ToList();
        var cut = RenderGantt(p => p
            .Add(c => c.CustomFields, fields)
            .Add(c => c.Columns, Columns(GanttColumnKey.Title, GanttColumnKey.Status, GanttColumnKey.Start, GanttColumnKey.End, GanttColumnKey.Duration, GanttColumnKey.Priority)));

        var style = cut.Find(".tm-gantt__tree").GetAttribute("style")!;
        style.Should().Contain("width: 1264px", "664 + 6 custom fields x 100");
        style.Should().Contain("max-width: 70%", "the timeline always keeps at least 30% of the main area");
        cut.Find(".tm-gantt__tree-grid").GetAttribute("style").Should().Contain("min-width: 1264px",
            "the grid keeps its full width inside the horizontally scrolling tree, so header and cells stay aligned");
    }

    [Fact]
    public void TmGantt_Tree_Css_Scrolls_Horizontally_And_Title_Cell_Keeps_A_Min_Width()
    {
        GanttCss.Property("TmGanttTree.razor.css", ".tm-gantt__tree", "overflow-x").Should().Be("auto");
        GanttCss.Property("TmGanttTree.razor.css", ".tm-gantt__tree-cell--title", "flex").Should().Be("1 0 var(--tm-gantt-title-min, 12rem)");
        GanttCss.Property("TmGanttTree.razor.css", ".tm-gantt__tree-header-cell--title", "flex").Should().Be("1 0 var(--tm-gantt-title-min, 12rem)");
    }

    [Fact]
    public void TmGantt_Default_Columns_Offer_Duration_And_Priority_Hidden()
    {
        var cut = RenderGantt();

        cut.FindAll("[data-testid='col-header-duration'], [data-testid='col-header-priority']").Should().BeEmpty("hidden by default");
        cut.Find("[data-testid='gantt-column-menu'] .tm-column-picker-toggle").Click();
        var items = cut.FindAll(".tm-column-picker-item input");
        items.Select(i => i.GetAttribute("data-key")).Should().Equal("WBS", "Status", "Start", "End", "Progress", "Duration", "Priority");
        items.Where(i => i.GetAttribute("data-key") is "Duration" or "Priority").Should().OnlyContain(i => !i.HasAttribute("checked"));

        cut.Find(".tm-column-picker-item input[data-key='Duration']").Change(true);
        cut.Find("[data-testid='col-header-duration']").Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_Fr_FilterActive_Reads_Naturally()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var fr = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor", "Resources", "TmResources.fr.json")))!;
        fr["TmGantt_FilterActive"].Should().Be("Filtre (actifs : {0})");
    }

    // ═══════════════════════════════════════════════════════════════
    // Review fixes: tab/panel ARIA wiring, column-picker toolbar hooks, keyed rows
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_SidebarMode_Tabs_Wires_Tabs_To_Panels()
    {
        var cut = RenderGantt(p => p.Add(c => c.SidebarMode, GanttSidebarMode.Tabs));
        var tabs = cut.FindAll("[role='tab']");

        // Tasks tab: the chart itself is the panel.
        var tasks = tabs[0];
        tasks.Id.Should().MatchRegex("^tm-gantt-[0-9a-f]{8}-tab-tasks$");
        var instance = tasks.Id.Split('-')[2];
        tasks.GetAttribute("aria-controls").Should().Be($"tm-gantt-{instance}-panel-tasks");
        var main = cut.Find($"[id='tm-gantt-{instance}-panel-tasks']");
        main.GetAttribute("role").Should().Be("tabpanel");
        main.GetAttribute("aria-labelledby").Should().Be(tasks.Id);
        main.ClassList.Should().Contain("tm-gantt__main");

        // Every tab id follows the pattern and the selected tab's panel is a labelled tabpanel.
        tabs.Select(t => t.Id).Should().Equal(
            $"tm-gantt-{instance}-tab-tasks", $"tm-gantt-{instance}-tab-workload", $"tm-gantt-{instance}-tab-history",
            $"tm-gantt-{instance}-tab-notifications", $"tm-gantt-{instance}-tab-reports");

        tabs[1].Click();
        var workload = cut.Find("[data-testid='sidebar-panel-workload']");
        workload.GetAttribute("role").Should().Be("tabpanel");
        workload.Id.Should().Be($"tm-gantt-{instance}-panel-workload");
        workload.GetAttribute("aria-labelledby").Should().Be($"tm-gantt-{instance}-tab-workload");
        cut.FindAll("[role='tab']")[1].GetAttribute("aria-controls").Should().Be(workload.Id);

        cut.FindAll("[role='tab']")[2].Click();
        cut.Find("[data-testid='sidebar-panel-history']").GetAttribute("role").Should().Be("tabpanel");

        cut.FindAll("[role='tab']")[4].Click();
        var reports = cut.Find("[data-testid='gantt-reports-panel']");
        reports.GetAttribute("role").Should().Be("tabpanel");
        reports.Id.Should().Be($"tm-gantt-{instance}-panel-reports");
        reports.GetAttribute("aria-labelledby").Should().Be($"tm-gantt-{instance}-tab-reports");
    }

    [Fact]
    public void TmGantt_SidebarMode_Rail_Drawers_Carry_No_Tabpanel_Semantics()
    {
        var cut = RenderGantt();
        cut.Find("[data-testid='sidebar-btn-workload']").Click();

        var drawer = cut.Find("[data-testid='sidebar-panel-workload']");
        drawer.HasAttribute("role").Should().BeFalse();
        drawer.HasAttribute("id").Should().BeFalse();
        drawer.HasAttribute("aria-labelledby").Should().BeFalse();
        cut.FindAll("[role='tabpanel']").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_Tab_Ids_Differ_Between_Instances()
    {
        var a = RenderGantt(p => p.Add(c => c.SidebarMode, GanttSidebarMode.Tabs));
        var b = RenderGantt(p => p.Add(c => c.SidebarMode, GanttSidebarMode.Tabs));

        a.Find("[role='tab']").Id.Should().NotBe(b.Find("[role='tab']").Id);
    }

    private IRenderedComponent<TmColumnPicker> RenderPickerInToolbar(bool iconOnly)
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-toolbar.js");
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        return Render<TmToolbar>(p => p
            .Add(c => c.Overflow, ToolbarOverflow.None)
            .Add(c => c.ChildContent, (RenderFragment)(b =>
            {
                b.OpenComponent<TmColumnPicker>(0);
                b.AddComponentParameter(1, nameof(TmColumnPicker.IconOnly), iconOnly);
                b.AddComponentParameter(2, nameof(TmColumnPicker.Columns), (IReadOnlyList<ColumnVisibilityItem>)[new("A", "A", true, true)]);
                b.CloseComponent();
            }))).FindComponent<TmColumnPicker>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TmColumnPicker_Inside_A_Toolbar_Joins_The_Roving_Set_With_Or_Without_Labels(bool iconOnly)
    {
        var cut = RenderPickerInToolbar(iconOnly);

        cut.Find(".tm-column-picker-toggle").HasAttribute("data-tm-toolbar-control").Should().BeTrue();
    }

    [Fact]
    public void TmColumnPicker_Standalone_Has_No_Toolbar_Control_Hook()
    {
        var cut = Render<TmColumnPicker>(p => p.Add(c => c.Columns, [new ColumnVisibilityItem("A", "A", true, true)]));

        cut.Find(".tm-column-picker-toggle").HasAttribute("data-tm-toolbar-control").Should().BeFalse();
    }

    [Fact]
    public void TmColumnPicker_Text_Is_Wrapped_In_The_Toolbar_Text_Span()
    {
        var cut = Render<TmColumnPicker>(p => p.Add(c => c.Columns, [new ColumnVisibilityItem("A", "A", true, true)]));

        cut.Find(".tm-column-picker-toggle .tm-toolbar-btn-text").TextContent.Trim().Should().Be("Choose columns");
    }

    [Fact]
    public void TmGantt_Column_Menu_Follows_ToolbarLabels()
    {
        RenderGantt().FindAll("[data-testid='gantt-column-menu'] .tm-toolbar-btn-text").Should().BeEmpty("Icons keeps the toggle icon-only");

        var withText = RenderGantt(p => p.Add(c => c.ToolbarLabels, ToolbarLabels.IconsWithText));
        withText.Find("[data-testid='gantt-column-menu'] .tm-toolbar-btn-text").TextContent.Trim().Should().Be("Choose columns");

        var auto = RenderGantt(p => p.Add(c => c.ToolbarLabels, ToolbarLabels.Auto));
        auto.Find("[data-testid='gantt-column-menu'] .tm-toolbar-btn-text").Should().NotBeNull(
            "Auto renders the label so the toolbar's container query can hide it on a narrow bar");
    }

    /// <summary>
    /// bUnit patches the DOM by diff, so element identity cannot show a missing key; the contract is asserted on the
    /// markup source instead: every repeated row, bar and milestone element is keyed by its task id.
    /// </summary>
    [Fact]
    public void TmGantt_Tree_Rows_And_Bars_Are_Keyed_By_Task_Id()
    {
        var dir = Path.Combine(ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "Components", "Scheduler");
        var tree = File.ReadAllText(Path.Combine(dir, "TmGanttTree.razor"));
        var timeline = File.ReadAllText(Path.Combine(dir, "TmGanttTimeline.razor"));

        tree.Should().Contain("class=\"tm-gantt__tree-row @GetRowClass(node) @GetDragClass(node)\"\n                 @key=\"node.Task.Id\"");
        timeline.Should().Contain("class=\"tm-gantt__task-row\"\n                     @key=\"bar.Task.Id\"");
        timeline.Should().Contain("class=\"tm-gantt__milestone\"\n                             @key=\"bar.Task.Id\"");
        timeline.Should().Contain("class=\"tm-gantt__bar @GetBarClass(bar)\"\n                             @key=\"bar.Task.Id\"");
    }
    [Fact]
    public void TmGantt_Tree_And_Timeline_Prune_Refs_Of_Hidden_Tasks()
    {
        var cut = RenderGantt();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var tree = cut.FindComponent<TmGanttTree>().Instance;
        var timeline = cut.FindComponent<TmGanttTimeline>().Instance;
        var rowRefs = (System.Collections.IDictionary)typeof(TmGanttTree).GetField("_rowRefs", flags)!.GetValue(tree)!;
        var barRefs = (System.Collections.IDictionary)typeof(TmGanttTimeline).GetField("_barRefs", flags)!.GetValue(timeline)!;
        rowRefs.Contains("2").Should().BeTrue();
        barRefs.Contains("2").Should().BeTrue();

        cut.Find("[data-testid='tree-row-1'] .tm-gantt__expand-btn").Click();

        cut.FindAll("[data-testid='tree-row-2']").Should().BeEmpty();
        rowRefs.Contains("2").Should().BeFalse("the collapsed child left the visible set");
        rowRefs.Contains("3").Should().BeFalse();
        barRefs.Contains("2").Should().BeFalse();
        barRefs.Contains("3").Should().BeFalse();
        rowRefs.Contains("1").Should().BeTrue();
    }

    /// <summary>UX review: the title header cell had no min-width, so it collapsed to nothing and every other header sat left of its cells.</summary>
    [Fact]
    public void TmGantt_Tree_Title_Header_Shares_The_Row_Title_Cell_Class()
    {
        var cut = RenderGantt();

        cut.Find(".tm-gantt__tree-header [role='columnheader']:not(.tm-gantt__tree-header-cell--status):not(.tm-gantt__tree-header-cell--narrow):not(.tm-gantt__tree-header-cell--wbs)")
            .ClassList.Should().Contain("tm-gantt__tree-header-cell--title");
        cut.Find(".tm-gantt__tree-row .tm-gantt__tree-cell--title").Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_WbsInTitle_Prefixes_The_Title_And_Defaults_Off()
    {
        var plain = RenderGantt(p => p.Add(c => c.Columns, Columns(GanttColumnKey.Title)));
        plain.FindAll(".tm-gantt__task-wbs").Should().BeEmpty();

        var cut = RenderGantt(p => p
            .Add(c => c.Columns, Columns(GanttColumnKey.Title))
            .Add(c => c.ViewSettings, new GanttViewSettings { WbsInTitle = true }));

        cut.Find("[data-testid='tree-cell-title-2']").QuerySelector(".tm-gantt__task-wbs")!.TextContent.Trim().Should().Be("1.1");
    }

    [Fact]
    public void TmGantt_Column_Menu_Toggles_Visibility()
    {
        IReadOnlyList<GanttColumnDefinition>? changed = null;
        var cut = RenderGantt(p => p
            .Add(c => c.Columns, Columns(GanttColumnKey.Title, GanttColumnKey.Status, GanttColumnKey.Progress))
            .Add(c => c.OnColumnsChanged, EventCallback.Factory.Create<IReadOnlyList<GanttColumnDefinition>>(this, c => changed = c)));

        cut.FindAll("input[data-testid='col-toggle-progress']").Should().BeEmpty("the header checkbox is gone");
        cut.Find("[data-testid='col-header-progress']").Should().NotBeNull();

        cut.Find("[data-testid='gantt-column-menu'] .tm-column-picker-toggle").Click();
        cut.FindAll(".tm-column-picker-item input").Select(i => i.GetAttribute("data-key"))
            .Should().Equal("Status", "Progress");

        cut.Find(".tm-column-picker-item input[data-key='Progress']").Change(false);

        changed!.First(c => c.Key == GanttColumnKey.Progress).Visible.Should().BeFalse();
        cut.FindAll("[data-testid='col-header-progress']").Should().BeEmpty("the hidden column leaves the grid");
        cut.FindAll(".tm-gantt__progress-mini").Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // Treegrid
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Tree_Has_Treegrid_Role_AriaLevel_AriaExpanded()
    {
        var cut = RenderGantt();

        var grid = cut.Find("[role='treegrid']");
        grid.GetAttribute("aria-label").Should().Be("Task list");
        grid.QuerySelector(".tm-gantt__tree-header")!.GetAttribute("role").Should().Be("row");
        grid.QuerySelectorAll(".tm-gantt__tree-header [role='columnheader']").Should().NotBeEmpty();

        var project = cut.Find("[data-testid='tree-row-1']");
        project.GetAttribute("role").Should().Be("row");
        project.GetAttribute("aria-level").Should().Be("1");
        project.GetAttribute("aria-expanded").Should().Be("true");
        project.QuerySelectorAll("[role='gridcell']").Should().NotBeEmpty();

        var child = cut.Find("[data-testid='tree-row-2']");
        child.GetAttribute("aria-level").Should().Be("2");
        child.HasAttribute("aria-expanded").Should().BeFalse("a leaf has no expanded state");
        child.GetAttribute("aria-selected").Should().Be("false");

        cut.Find("[data-testid='tree-row-2']").Click();
        cut.Find("[data-testid='tree-row-2']").GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void TmGantt_Tree_Rows_Use_A_Roving_Tabindex()
    {
        var cut = RenderGantt();

        cut.FindAll("[data-testid^='tree-row-']").Select(r => r.GetAttribute("tabindex")).Should().Equal("0", "-1", "-1", "-1");

        cut.Find("[data-testid='tree-row-3']").Click();
        cut.FindAll("[data-testid^='tree-row-']").Select(r => r.GetAttribute("tabindex")).Should().Equal("-1", "-1", "0", "-1");
    }

    [Fact]
    public void TmGantt_Tree_ArrowKeys_Move_Focus_And_Expand_Collapse()
    {
        var cut = RenderGantt();
        cut.Find("[data-testid='tree-row-1']").Focus();

        cut.Find("[data-testid='tree-row-1']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.FindAll("[data-testid^='tree-row-']").Select(r => r.GetAttribute("tabindex")).Should().Equal("-1", "0", "-1", "-1");
        JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().Be(1, "the next row takes the DOM focus");

        cut.Find("[data-testid='tree-row-2']").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        cut.FindAll("[data-testid^='tree-row-']").Select(r => r.GetAttribute("tabindex")).Should().Equal("0", "-1", "-1", "-1");

        cut.Find("[data-testid='tree-row-1']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        cut.Find("[data-testid='tree-row-1']").GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll("[data-testid='tree-row-2']").Should().BeEmpty("collapsing hides the children");

        cut.Find("[data-testid='tree-row-1']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        cut.Find("[data-testid='tree-row-1']").GetAttribute("aria-expanded").Should().Be("true");
        cut.FindAll("[data-testid='tree-row-2']").Should().NotBeEmpty();

        // ArrowRight on an expanded parent steps into its first child; ArrowLeft on a child steps back to the parent.
        cut.Find("[data-testid='tree-row-1']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        cut.Find("[data-testid='tree-row-2']").GetAttribute("tabindex").Should().Be("0");
        cut.Find("[data-testid='tree-row-2']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        cut.Find("[data-testid='tree-row-1']").GetAttribute("tabindex").Should().Be("0");

        cut.Find("[data-testid='tree-row-1']").KeyDown(new KeyboardEventArgs { Key = "End" });
        cut.Find("[data-testid='tree-row-4']").GetAttribute("tabindex").Should().Be("0");
        cut.Find("[data-testid='tree-row-4']").KeyDown(new KeyboardEventArgs { Key = "Home" });
        cut.Find("[data-testid='tree-row-1']").GetAttribute("tabindex").Should().Be("0");
    }

    [Fact]
    public void TmGantt_Tree_Enter_Opens_The_TaskPanel_Once_And_Space_Selects_On_KeyUp()
    {
        var selected = new List<string>();
        var cut = RenderGantt(p => p.Add(c => c.OnTaskSelected, EventCallback.Factory.Create<TmWorkItem>(this, t => selected.Add(t.Id))));

        cut.Find("[data-testid='tree-row-3']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        selected.Should().Equal("3");
        cut.Find(".tm-gantt-task-panel").TextContent.Should().Contain("Development");

        cut.Find("[data-testid='tree-row-2']").KeyDown(new KeyboardEventArgs { Key = " " });
        selected.Should().Equal("3");
        cut.Find("[data-testid='tree-row-2']").KeyUp(new KeyboardEventArgs { Key = " " });
        selected.Should().Equal("3", "2");
    }

    [Fact]
    public void TmGantt_Tree_Native_Children_Do_Not_Activate_The_Row()
    {
        var selected = new List<string>();
        var cut = RenderGantt(p => p
            .Add(c => c.AllowBulkSelect, true)
            .Add(c => c.OnTaskSelected, EventCallback.Factory.Create<TmWorkItem>(this, t => selected.Add(t.Id))));

        // The fence is a Blazor stopPropagation marker on the native control (or its cell): bUnit has no handler to
        // dispatch to there, so the contract is asserted on the markup the browser-side Blazor reads.
        var checkboxCell = cut.Find("[data-testid='bulk-select-3']").ParentElement!;
        checkboxCell.OuterHtml.Should().Contain("blazor:onkeydown:stoppropagation").And.Contain("blazor:onkeyup:stoppropagation");
        var expand = cut.Find("[data-testid='tree-row-1'] .tm-gantt__expand-btn");
        expand.OuterHtml.Should().Contain("blazor:onkeydown:stoppropagation").And.Contain("blazor:onkeyup:stoppropagation");
        expand.GetAttribute("tabindex").Should().Be("-1", "the row is the tab stop, not its inner button");
        selected.Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_Tree_Leaf_ArrowRight_Focuses_Its_Bar_And_Escape_Returns()
    {
        var cut = RenderGantt();
        var before = JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation);

        cut.Find("[data-testid='tree-row-3']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().Be(before + 1, "the bar of a leaf takes the focus");

        cut.Find("[data-testid='task-bar-3']").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().Be(before + 2, "Escape hands the focus back to the row");
        cut.Find("[data-testid='tree-row-3']").GetAttribute("tabindex").Should().Be("0");
    }

    // ═══════════════════════════════════════════════════════════════
    // Bars
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Bar_Is_Focusable_Enter_Opens_TaskPanel_Once()
    {
        var selected = new List<string>();
        var cut = RenderGantt(p => p.Add(c => c.OnTaskSelected, EventCallback.Factory.Create<TmWorkItem>(this, t => selected.Add(t.Id))));

        var bar = cut.Find("[data-testid='task-bar-3']");
        bar.GetAttribute("tabindex").Should().Be("-1", "reachable from the row, not a tab stop of its own");
        bar.GetAttribute("role").Should().Be("button");

        bar.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        selected.Should().Equal("3");
        cut.Find(".tm-gantt-task-panel").TextContent.Should().Contain("Development");

        cut.Find("[data-testid='task-bar-3']").KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });
        selected.Should().Equal("3");
    }

    [Fact]
    public void TmGantt_Bar_Space_Activates_On_KeyUp_Only()
    {
        var selected = new List<string>();
        var cut = RenderGantt(p => p.Add(c => c.OnTaskSelected, EventCallback.Factory.Create<TmWorkItem>(this, t => selected.Add(t.Id))));

        cut.Find("[data-testid='task-bar-2']").KeyDown(new KeyboardEventArgs { Key = " " });
        selected.Should().BeEmpty();
        cut.Find("[data-testid='task-bar-2']").KeyUp(new KeyboardEventArgs { Key = " " });
        selected.Should().Equal("2");
    }

    [Fact]
    public void TmGantt_Bar_And_Milestone_Have_An_AriaLabel_With_Name_And_Range()
    {
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = RenderGantt();
        var en = CultureInfo.GetCultureInfo("en");

        var bar = cut.Find("[data-testid='task-bar-3']");
        bar.GetAttribute("aria-label").Should().Be($"Development, {new DateTime(2024, 6, 11).ToString("d", en)} – {new DateTime(2024, 6, 25).ToString("d", en)}");

        var milestone = cut.Find(".tm-gantt__milestone");
        milestone.GetAttribute("aria-label").Should().StartWith("Launch, ");
        milestone.GetAttribute("tabindex").Should().Be("-1");
        milestone.GetAttribute("role").Should().Be("button");
    }

    [Fact]
    public void TmGantt_Milestone_Enter_Opens_The_TaskPanel()
    {
        var selected = new List<string>();
        var cut = RenderGantt(p => p.Add(c => c.OnTaskSelected, EventCallback.Factory.Create<TmWorkItem>(this, t => selected.Add(t.Id))));

        cut.Find(".tm-gantt__milestone").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        selected.Should().Equal("4");
    }

    // ═══════════════════════════════════════════════════════════════
    // Render gate: what the new state draws must repaint
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Toggling_WbsInTitle_In_Place_Repaints_The_Tree()
    {
        var settings = new GanttViewSettings();
        var cut = RenderGantt(p => p.Add(c => c.ViewSettings, settings).Add(c => c.Columns, Columns(GanttColumnKey.Title)));
        cut.FindAll(".tm-gantt__task-wbs").Should().BeEmpty();

        settings.WbsInTitle = true;
        cut.Find("[data-testid='gantt-filter-btn']").Click();

        cut.FindAll(".tm-gantt__task-wbs").Should().NotBeEmpty("WbsInTitle is part of what the tree draws");
    }
}
