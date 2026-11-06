using FluentAssertions;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 7 — <c>DelegateAutoAssigner</c>, the pure C# half of <c>TmDelegateBoard</c>'s automatic mode.
/// <para>
/// The rule pins what "the first free agent" means: a FULL agent is never a candidate, the candidate that becomes free
/// FIRST wins, a tie keeps the host's declaration order, one pass never hands two tasks to an agent with one slot, and a
/// task nobody can take is absent from the plan instead of being forced onto a full column. The backlog order is the
/// second half of the rule: the most urgent task first, and an ungraded task keeps the host's order after the graded ones.
/// </para>
/// </summary>
public class DelegateAutoAssignerTests
{
    private static readonly DateTimeOffset Ten = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private static AgentRef Agent(string id, string name, int color = 0) => new(name, id) { ColorIndex = color };

    private static DelegateAgent Free(string id, int slots = 1) =>
        new(Agent(id, id)) { MaxConcurrency = slots, ActiveCount = 0 };

    private static DelegateAgent Busy(string id, DateTimeOffset until, int slots = 1) =>
        new(Agent(id, id)) { MaxConcurrency = slots, ActiveCount = 0, BusyUntil = until };

    private static DelegateAgent Full(string id) =>
        new(Agent(id, id)) { MaxConcurrency = 2, ActiveCount = 2 };

    private static DelegateTask Task(string id, DelegateTaskPriority? priority = null, int minutes = 30) =>
        new(id, id) { Priority = priority, EstimatedDuration = TimeSpan.FromMinutes(minutes) };

    // ── who is a candidate ───────────────────────────────────────────────────

    [Fact]
    public void CandidatesSkipAFullAgent_AndOrderTheRestByFreeTime()
    {
        var free = Free("free");
        var soon = Busy("soon", Ten.AddMinutes(15));
        var later = Busy("later", Ten.AddHours(1));
        var full = Full("full");

        var candidates = DelegateAutoAssigner.Candidates([later, full, free, soon], Ten);

        // A free agent first, then the earliest free time, and never a full one.
        candidates.Select(agent => agent.Id).Should().Equal("free", "soon", "later");
    }

    [Fact]
    public void AStaleBusyUntilCountsAsFree_SoTheOrderDoesNotDependOnAnOldTimestamp()
    {
        var stale = Busy("stale", Ten.AddMinutes(-30));
        var real = Busy("real", Ten.AddMinutes(5));

        DelegateAutoAssigner.Candidates([real, stale], Ten).Select(agent => agent.Id).Should().Equal("stale", "real");
        DelegateAutoAssigner.FirstFree([real, stale], Ten)!.Id.Should().Be("stale");
    }

    [Fact]
    public void ATieKeepsTheHostsOrder()
    {
        var first = Free("first");
        var second = Free("second");

        // An unstable sort would make the choice depend on a hash.
        DelegateAutoAssigner.Candidates([first, second], Ten).Select(agent => agent.Id)
            .Should().Equal("first", "second");
    }

    [Fact]
    public void NoCandidates_WhenEveryAgentIsFull()
    {
        DelegateAutoAssigner.Candidates([Full("a"), Full("b")], Ten).Should().BeEmpty();
        DelegateAutoAssigner.FirstFree([Full("a")], Ten).Should().BeNull();
        DelegateAutoAssigner.Candidates(null, Ten).Should().BeEmpty();
        DelegateAutoAssigner.Candidates([], Ten).Should().BeEmpty();
    }

    // ── the pass ─────────────────────────────────────────────────────────────

    [Fact]
    public void OnePassNeverHandsTwoTasksToAnAgentWithOneSlot()
    {
        var only = Free("only");
        var plan = DelegateAutoAssigner.Plan([Task("t1"), Task("t2")], [only], Ten);

        plan.Should().HaveCount(1, "the second task has nowhere to go");
        plan[0].Agent.Id.Should().Be("only");
        plan[0].Task.Id.Should().Be("t1");
    }

    [Fact]
    public void ThePassFillsTheAgentThatBecomesFreeFirst()
    {
        var soon = Busy("soon", Ten.AddMinutes(5));
        var later = Busy("later", Ten.AddHours(1));
        var plan = DelegateAutoAssigner.Plan([Task("t1")], [later, soon], Ten);

        plan.Should().HaveCount(1);
        plan[0].Agent.Id.Should().Be("soon");
    }

    [Fact]
    public void ThePassUsesEveryFreeSlot_ThenStops()
    {
        var wide = Free("wide", slots: 2);
        var plan = DelegateAutoAssigner.Plan([Task("t1"), Task("t2"), Task("t3")], [wide], Ten);

        plan.Select(assignment => assignment.Task.Id).Should().Equal("t1", "t2");
        plan.Should().OnlyContain(assignment => assignment.Agent.Id == "wide");
    }

    [Fact]
    public void ThePassSkipsNulls_AndAnEmptyBacklogPlansNothing()
    {
        DelegateAutoAssigner.Plan([], [Free("a")], Ten).Should().BeEmpty();
        DelegateAutoAssigner.Plan(null, [Free("a")], Ten).Should().BeEmpty();
        DelegateAutoAssigner.Plan([Task("t1")], null, Ten).Should().BeEmpty();
        DelegateAutoAssigner.Plan([null!, Task("t1")], [Free("a")], Ten).Should().HaveCount(1);
    }

    // ── the backlog order ────────────────────────────────────────────────────

    [Fact]
    public void TheAssignmentOrderIsTheMostUrgentFirst()
    {
        var ordered = DelegateAutoAssigner.InAssignmentOrder(
        [
            Task("low", DelegateTaskPriority.Low),
            Task("urgent", DelegateTaskPriority.Urgent),
            Task("normal", DelegateTaskPriority.Normal),
            Task("high", DelegateTaskPriority.High),
        ]);

        ordered.Select(task => task.Id).Should().Equal("urgent", "high", "normal", "low");
    }

    [Fact]
    public void AnUngradedTaskKeepsItsOrderAfterTheGradedOnes()
    {
        var ordered = DelegateAutoAssigner.InAssignmentOrder(
        [
            Task("ungraded-1"),
            Task("low", DelegateTaskPriority.Low),
            Task("ungraded-2"),
            Task("high", DelegateTaskPriority.High),
        ]);

        // A task with no grade is neither promoted to Urgent nor demoted below Low.
        ordered.Select(task => task.Id).Should().Equal("high", "low", "ungraded-1", "ungraded-2");
    }

    [Fact]
    public void ATieInsideOneGradeKeepsTheHostsOrder()
    {
        var ordered = DelegateAutoAssigner.InAssignmentOrder(
        [
            Task("a", DelegateTaskPriority.High),
            Task("b", DelegateTaskPriority.High),
            Task("c", DelegateTaskPriority.High),
        ]);

        ordered.Select(task => task.Id).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void TheOrderNeverTouchesTheInputList()
    {
        var input = new List<DelegateTask> { Task("b", DelegateTaskPriority.Low), Task("a", DelegateTaskPriority.High) };

        DelegateAutoAssigner.InAssignmentOrder(input);

        // The host's list is not reordered behind its back.
        input.Select(task => task.Id).Should().Equal("b", "a");
    }

    // ── the occupied-until projection ────────────────────────────────────────

    [Fact]
    public void TheProjectionAddsTheTasksEstimateToTheAgentsFreeTime()
    {
        var busy = Busy("busy", Ten.AddHours(1));

        var projection = DelegatePresentation.Project(busy, Task("t1", minutes: 30), Ten);

        projection.IsKnown.Should().BeTrue();
        projection.UsesEstimate.Should().BeTrue();
        projection.Until.Should().Be(Ten.AddHours(1).AddMinutes(30));

        DelegatePresentation.Project(Free("free"), Task("t1", minutes: 45), Ten).Until
            .Should().Be(Ten.AddMinutes(45), "a free agent starts now");
    }

    [Fact]
    public void ATaskWithoutAnEstimateProjectsOnlyTheAgentsOwnLoad()
    {
        var busy = DelegatePresentation.Project(Busy("busy", Ten.AddHours(1)), Task("t1", minutes: 0), Ten);
        busy.IsKnown.Should().BeTrue("the agent is busy, so there is a time to state");
        busy.UsesEstimate.Should().BeFalse();
        busy.Until.Should().Be(Ten.AddHours(1));

        var free = DelegatePresentation.Project(Free("free"), Task("t1", minutes: 0), Ten);
        free.IsKnown.Should().BeFalse("nothing is known about a free agent and an unestimated task");
    }
}
