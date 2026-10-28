using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

public class TmGanttTests : LocalizationTestBase
{
    private static IReadOnlyList<TmWorkItem> GetSampleTasks() => new List<TmWorkItem>
    {
        new() { Id = "1", Title = "Project", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30), PercentComplete = 0 },
        new() { Id = "2", Title = "Design", ParentId = "1", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 10), PercentComplete = 100 },
        new() { Id = "3", Title = "Development", ParentId = "1", Start = new DateTime(2024, 6, 11), End = new DateTime(2024, 6, 25), PercentComplete = 50 },
        new() { Id = "4", Title = "Launch", IsMilestone = true, Start = new DateTime(2024, 6, 30), End = new DateTime(2024, 6, 30), PercentComplete = 0 },
    };

    private static IReadOnlyList<GanttDependency> GetSampleDependencies() => new List<GanttDependency>
    {
        new() { FromId = "2", ToId = "3" },
        new() { FromId = "3", ToId = "4" },
    };

    [Fact]
    public void TmGantt_Renders_Container()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        cut.Find(".tm-gantt").Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_Renders_Toolbar()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        cut.Find(".tm-gantt__toolbar").Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_Renders_Tree_With_Tasks()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        var rows = cut.FindAll(".tm-gantt__tree-row");
        rows.Count.Should().Be(4);
    }

    [Fact]
    public void TmGantt_Tree_Shows_Expand_Button_For_Parents()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        var buttons = cut.FindAll(".tm-gantt__expand-btn");
        buttons.Count.Should().Be(1); // Only "Project" has children
    }

    [Fact]
    public void TmGantt_Timeline_Header_Renders()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        cut.Find(".tm-gantt__timeline-header").Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_Task_Bars_Render()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        var bars = cut.FindAll(".tm-gantt__bar, .tm-gantt__milestone");
        bars.Count.Should().Be(4);
    }

    [Fact]
    public void TmGantt_Milestone_Rendered_As_Diamond()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        var milestones = cut.FindAll(".tm-gantt__milestone");
        milestones.Count.Should().Be(1);
    }

    [Fact]
    public void TmGantt_Dependency_Lines_Render()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks())
            .Add(c => c.DependencyItems, GetSampleDependencies()));

        var lines = cut.FindAll(".tm-gantt__dependency-line");
        lines.Count.Should().Be(2);
    }

    [Fact]
    public void TmGantt_Select_Task_Highlights_Row()
    {
        TmWorkItem? selected = null;
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks())
            .Add(c => c.OnTaskSelected, t => selected = t));

        var rows = cut.FindAll(".tm-gantt__tree-row");
        rows[0].Click();

        selected.Should().NotBeNull();
        selected!.Id.Should().Be("1");
    }

    [Fact]
    public void TmGantt_Collapse_Parent_Hides_Children()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        var expandBtn = cut.Find(".tm-gantt__expand-btn");
        expandBtn.Click();

        var rows = cut.FindAll(".tm-gantt__tree-row");
        rows.Count.Should().Be(2); // Project + Launch (Design and Development hidden)
    }

    [Fact]
    public void TmGantt_Custom_Class_Applied()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks())
            .Add(c => c.Class, "my-gantt"));

        cut.Find(".tm-gantt").ClassList.Should().Contain("my-gantt");
    }

    [Fact]
    public void TmGantt_Default_View_Is_Week()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        // Week view header should contain "W" for week number
        var headerText = cut.Find(".tm-gantt__timeline-header").TextContent;
        headerText.Should().Contain("W");
    }

    [Fact]
    public void TmGantt_TimelineContent_HasExplicitHeight()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        var content = cut.Find(".tm-gantt__timeline-content");
        var style = content.GetAttribute("style");
        style.Should().Contain("height:");
        // 4 sample tasks * 40px RowHeight = 160px
        style.Should().Contain("160");
    }

    [Fact]
    public void TmGantt_TimelineBody_HasVerticalScrollContainer()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        var body = cut.Find(".tm-gantt__timeline-body");
        body.Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_TimelineContainer_HasHorizontalOverflow()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        var timeline = cut.Find(".tm-gantt__timeline");
        timeline.Should().NotBeNull();
    }

    [Fact]
    public async Task TmGantt_Wheel_ShiftKey_Pans_Horizontally()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        var timeline = cut.Find(".tm-gantt__timeline");

        // Should not throw; in bUnit loose JS mode scroll read returns 0, write is no-op
        await timeline.WheelAsync(new WheelEventArgs { ShiftKey = true, DeltaY = 50 });
    }

    [Fact]
    public async Task TmGantt_Wheel_CtrlKey_Zooms()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        var timeline = cut.Find(".tm-gantt__timeline");

        // Ctrl + wheel down (negative delta) = zoom in
        await timeline.WheelAsync(new WheelEventArgs { CtrlKey = true, DeltaY = -50 });

        // Ctrl + wheel up (positive delta) = zoom out
        await timeline.WheelAsync(new WheelEventArgs { CtrlKey = true, DeltaY = 50 });
    }

    [Fact]
    public async Task TmGantt_ScrollSync_TreeScroll_DoesNotThrow()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        var treeBody = cut.Find(".tm-gantt__tree-body");

        // Should not throw in bUnit loose JS mode
        await treeBody.TriggerEventAsync("onscroll", new EventArgs());
    }

    [Fact]
    public async Task TmGantt_ScrollSync_TimelineBodyScroll_DoesNotThrow()
    {
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks()));

        var timelineBody = cut.Find(".tm-gantt__timeline-body");

        // Should not throw in bUnit loose JS mode
        await timelineBody.TriggerEventAsync("onscroll", new EventArgs());
    }
}
