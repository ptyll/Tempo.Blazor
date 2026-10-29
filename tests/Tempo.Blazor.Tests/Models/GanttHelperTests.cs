using FluentAssertions;
using Tempo.Blazor.Abstractions.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Models;

public class GanttHelperTests
{
    [Fact]
    public void BuildTree_Flat_Data_With_ParentId_Creates_Hierarchy()
    {
        var tasks = new List<TmWorkItem>
        {
            new() { Id = "1", Title = "Project", Start = DateTime.Today, End = DateTime.Today.AddDays(7) },
            new() { Id = "2", Title = "Task A", ParentId = "1", Start = DateTime.Today, End = DateTime.Today.AddDays(3) },
            new() { Id = "3", Title = "Task B", ParentId = "1", Start = DateTime.Today.AddDays(3), End = DateTime.Today.AddDays(7) },
            new() { Id = "4", Title = "Subtask", ParentId = "2", Start = DateTime.Today, End = DateTime.Today.AddDays(1) },
        };

        var tree = GanttHelper.BuildTree(tasks);

        tree.Count.Should().Be(1);
        tree[0].Task.Id.Should().Be("1");
        tree[0].Children.Count.Should().Be(2);
        tree[0].Children[0].Task.Id.Should().Be("2");
        tree[0].Children[0].Children.Count.Should().Be(1);
        tree[0].Children[0].Children[0].Task.Id.Should().Be("4");
        tree[0].Children[0].Children[0].Depth.Should().Be(2);
    }

    [Fact]
    public void FlattenVisible_Respects_Expanded_State()
    {
        var root = new GanttTaskNode(new TmWorkItem { Id = "1", Title = "Root", IsExpanded = true });
        var child = new GanttTaskNode(new TmWorkItem { Id = "2", Title = "Child", IsExpanded = false });
        var grandChild = new GanttTaskNode(new TmWorkItem { Id = "3", Title = "GrandChild" });
        root.Children.Add(child);
        child.Children.Add(grandChild);

        var flat = GanttHelper.FlattenVisible(new[] { root });

        flat.Count.Should().Be(2);
        flat[0].Task.Id.Should().Be("1");
        flat[1].Task.Id.Should().Be("2");
    }

    [Fact]
    public void FlattenAll_Ignores_Expanded_State()
    {
        var root = new GanttTaskNode(new TmWorkItem { Id = "1", Title = "Root", IsExpanded = false });
        var child = new GanttTaskNode(new TmWorkItem { Id = "2", Title = "Child", IsExpanded = false });
        var grandChild = new GanttTaskNode(new TmWorkItem { Id = "3", Title = "GrandChild" });
        root.Children.Add(child);
        child.Children.Add(grandChild);

        var flat = GanttHelper.FlattenAll(new[] { root });

        // Every node in depth-first order — a collapsed parent never hides its children from a
        // computation that must not depend on the view (the summary band).
        flat.Select(n => n.Task.Id).Should().Equal("1", "2", "3");
    }

    [Fact]
    public void GetTimeRange_Computes_Min_Max()
    {
        var tasks = new List<TmWorkItem>
        {
            new() { Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 5) },
            new() { Start = new DateTime(2024, 6, 10), End = new DateTime(2024, 6, 15) },
        };

        var (start, end) = GanttHelper.GetTimeRange(tasks);
        start.Should().Be(new DateTime(2024, 6, 1));
        end.Should().Be(new DateTime(2024, 6, 15));
    }

    [Fact]
    public void CalculateBarPosition_Within_Timeline()
    {
        var timelineStart = new DateTime(2024, 6, 1);
        var timelineEnd = new DateTime(2024, 6, 11);
        var taskStart = new DateTime(2024, 6, 3);
        var taskEnd = new DateTime(2024, 6, 6);

        var (left, width) = GanttHelper.CalculateBarPosition(taskStart, taskEnd, timelineStart, timelineEnd, 1000);

        left.Should().BeApproximately(200, 1);
        width.Should().BeApproximately(300, 1);
    }

    // ── ComputeSummary (phase 11: the rollup band under the timeline) ───────────────────────────

    private static readonly DateTime SummaryToday = new(2024, 6, 15);

    /// <summary>The flat visible list the Gantt computes the summary from (one parent, two leaves and a milestone).</summary>
    private static List<GanttTaskNode> SummaryNodes() => [.. GanttHelper.FlattenVisible(GanttHelper.BuildTree(
    [
        new() { Id = "1", Title = "Parent", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30), PercentComplete = 50,
                Status = TmWorkItemStatus.InProgress, EstimationHours = 999, LoggedHours = 999, ActualCost = 9m },
        new() { Id = "2", Title = "Done", ParentId = "1", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 10),
                PercentComplete = 100, Status = TmWorkItemStatus.Done, EstimationHours = 100, LoggedHours = 128, ActualCost = 120000m },
        new() { Id = "3", Title = "Running", ParentId = "1", Start = new DateTime(2024, 6, 5), End = new DateTime(2024, 6, 20),
                PercentComplete = 40, Status = TmWorkItemStatus.InProgress, EstimationHours = 220, LoggedHours = 0, ActualCost = 200000m },
        new() { Id = "4", Title = "Milestone", IsMilestone = true, Start = new DateTime(2024, 6, 10), End = new DateTime(2024, 6, 10),
                Status = TmWorkItemStatus.Done, PercentComplete = 100 },
    ]))];

    [Fact]
    public void ComputeSummary_Counts_By_Status_And_Overdue()
    {
        // "Running" ends on 6/20 — not overdue; make a task that is.
        var nodes = SummaryNodes();
        nodes.Add(new GanttTaskNode(new TmWorkItem
        {
            Id = "5", Title = "Late", Start = new DateTime(2024, 5, 1), End = new DateTime(2024, 6, 1),
            Status = TmWorkItemStatus.Open,
        }));

        var summary = GanttHelper.ComputeSummary(nodes, SummaryToday);

        summary.StatusCounts.Should().ContainKeys(TmWorkItemStatus.Done, TmWorkItemStatus.InProgress, TmWorkItemStatus.Open);
        summary.StatusCounts[TmWorkItemStatus.Done].Should().Be(2);
        summary.StatusCounts[TmWorkItemStatus.InProgress].Should().Be(2, "the parent and the running leaf both count");
        summary.StatusCounts[TmWorkItemStatus.Open].Should().Be(1);
        summary.OverdueCount.Should().Be(1, "overdue = End before today, not Done and not Closed (TmGantt.IsOverdue)");
        summary.TaskCount.Should().Be(5);
        summary.IsFiltered.Should().BeFalse("the helper never claims a filter the caller did not apply");
    }

    [Fact]
    public void ComputeSummary_Time_Sums_Logged_And_Estimation_Leaf_Only()
    {
        var summary = GanttHelper.ComputeSummary(SummaryNodes(), SummaryToday);

        // The parent's own 999 h must not be added a second time on top of its children.
        summary.LoggedHours.Should().Be(128, "only leaves contribute (128 h from Done; Running logged nothing)");
        summary.EstimatedHours.Should().Be(320, "100 + 220 from the leaves");
    }

    [Fact]
    public void ComputeSummary_Milestones_Done_Of_Total()
    {
        var summary = GanttHelper.ComputeSummary(SummaryNodes(), SummaryToday);
        summary.MilestonesTotal.Should().Be(1);
        summary.MilestonesDone.Should().Be(1, "the milestone is Done");

        var open = GanttHelper.ComputeSummary(
        [
            new GanttTaskNode(new TmWorkItem { Id = "m1", Title = "Ship", IsMilestone = true, Status = TmWorkItemStatus.Open }),
            new GanttTaskNode(new TmWorkItem { Id = "m2", Title = "Cut", IsMilestone = true, Status = TmWorkItemStatus.Done }),
        ], SummaryToday);
        open.MilestonesTotal.Should().Be(2);
        open.MilestonesDone.Should().Be(1);
    }

    [Fact]
    public void ComputeSummary_Budget_Uses_ActualCost_And_Host_Total()
    {
        var summary = GanttHelper.ComputeSummary(SummaryNodes(), SummaryToday, budgetTotal: 500000m);

        summary.ActualCost.Should().Be(320000m, "the leaves' costs, the parent's own cost is not counted twice");
        summary.BudgetTotal.Should().Be(500000m);

        var withoutBudget = GanttHelper.ComputeSummary(SummaryNodes(), SummaryToday);
        withoutBudget.BudgetTotal.Should().BeNull("the host total is optional");
    }

    [Fact]
    public void ComputeSummary_Wbs_Levels_And_Count()
    {
        var summary = GanttHelper.ComputeSummary(SummaryNodes(), SummaryToday);

        // BuildTree assigns 1 / 1.1 / 1.2 and the milestone "2" — two WBS levels over 4 tasks.
        summary.WbsLevels.Should().Be(2);
        summary.TaskCount.Should().Be(4);
    }

    [Fact]
    public void ComputeSummary_Empty_Input_Is_A_Neutral_Summary()
    {
        var summary = GanttHelper.ComputeSummary([], SummaryToday);

        summary.TaskCount.Should().Be(0);
        summary.StatusCounts.Should().BeEmpty();
        summary.OverdueCount.Should().Be(0);
        summary.LoggedHours.Should().Be(0);
        summary.EstimatedHours.Should().Be(0);
        summary.MilestonesTotal.Should().Be(0);
        summary.MilestonesDone.Should().Be(0);
        summary.ActualCost.Should().Be(0);
        summary.WbsLevels.Should().Be(0);
        summary.BudgetTotal.Should().BeNull();
    }
}
