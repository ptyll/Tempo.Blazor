using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

public class TmGanttPanTests : LocalizationTestBase
{
    private static IReadOnlyList<TmWorkItem> GetSampleTasks() => new List<TmWorkItem>
    {
        new() { Id = "1", Title = "Project", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30), PercentComplete = 0 },
        new() { Id = "2", Title = "Design", ParentId = "1", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 10), PercentComplete = 100 },
        new() { Id = "3", Title = "Development", ParentId = "1", Start = new DateTime(2024, 6, 11), End = new DateTime(2024, 6, 25), PercentComplete = 50 },
        new() { Id = "4", Title = "Launch", IsMilestone = true, Start = new DateTime(2024, 6, 30), End = new DateTime(2024, 6, 30), PercentComplete = 0 },
    };

    /// <summary>
    /// Phase 7: the pan (mouse / pen) is a pointer session of the core pointer-drag.js, not Blazor mouse events (no awaited
    /// interop per move). The timeline renders NO @onmouse* handlers; gantt.js is told about it after the first render.
    /// </summary>
    [Fact]
    public void TmGantt_Timeline_Renders_No_Mouse_Handlers_And_Registers_The_Pointer_Pan()
    {
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, GetSampleTasks()));

        cut.Markup.Should().NotContain("onmousedown").And.NotContain("onmousemove").And.NotContain("onmouseup");
        JSInterop.Invocations.Select(i => i.Identifier).Should().Contain("tmGantt.attachTimeline");
    }

    /// <summary>Clicking a task bar selects the task and never enters a panning state.</summary>
    [Fact]
    public void TmGantt_Pan_ClickOnBar_DoesNotPan()
    {
        TmWorkItem? selected = null;
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, GetSampleTasks())
            .Add(c => c.OnTaskSelected, t => selected = t));

        cut.Find(".tm-gantt__bar").Click();

        selected.Should().NotBeNull("clicking a bar should select the task");
        selected!.Id.Should().Be("1");
        cut.Find(".tm-gantt__timeline").ClassList.Should().NotContain("tm-gantt__timeline--panning");
    }

    /// <summary>Bars and milestones name their task for the pointer sessions (long-press, dependency target).</summary>
    [Fact]
    public void TmGantt_Bars_And_Milestones_Carry_Data_Task_Id()
    {
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, GetSampleTasks()));

        cut.FindAll(".tm-gantt__bar").Select(b => b.GetAttribute("data-task-id")).Should().Contain(["1", "2", "3"]);
        cut.Find(".tm-gantt__milestone").GetAttribute("data-task-id").Should().Be("4");
    }
}