using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 7 — the <c>DelegateTask</c> / <c>DelegateAgent</c> / <c>DelegateMoveRequest</c> models behind
/// <c>TmDelegateBoard</c>.
/// <para>
/// The facts pin what the board is allowed to claim: a task without an estimate prints no duration and no projected
/// end time, a task without a priority is not silently graded <c>Normal</c>, a blank assignee is the same as no
/// assignee, capacity is decided by the HOST's <c>ActiveCount</c> against <c>MaxConcurrency</c> (never by how many
/// cards the board happens to show), a stale <c>BusyUntil</c> in the past means "free now", and a blank identity is
/// rejected so an assignment overlay can never silently lose its key.
/// </para>
/// </summary>
public class DelegateTaskTests
{
    private static readonly DateTimeOffset Ten = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private static AgentRef Agent(string id, string name, int color = 0) => new(name, id) { ColorIndex = color };

    // ── the task ─────────────────────────────────────────────────────────────

    [Fact]
    public void ABlankIdIsRejected_SoTheAssignmentOverlayKeepsItsKey()
    {
        var blank = () => new DelegateTask("  ", "Draft the summary");
        var missing = () => new DelegateTask(null!, "Draft the summary");

        blank.Should().Throw<ArgumentException>();
        missing.Should().Throw<ArgumentException>();
        new DelegateTask("t-1", "Draft the summary").Id.Should().Be("t-1");
    }

    [Fact]
    public void ABlankTitleReadsAsEmpty_AndEveryTaskHasAStableIdentity()
    {
        var task = new DelegateTask("t-1", "   ");

        task.Title.Should().BeEmpty();
        task.Id.Should().Be("t-1");
    }

    [Fact]
    public void AMissingOrZeroEstimateIsNotAnEstimate()
    {
        new DelegateTask("t-1", "Draft").HasEstimate.Should().BeFalse("nothing was measured");
        new DelegateTask("t-1", "Draft") { EstimatedDuration = TimeSpan.Zero }.HasEstimate.Should().BeFalse();
        new DelegateTask("t-1", "Draft") { EstimatedDuration = TimeSpan.FromMinutes(-5) }.HasEstimate.Should().BeFalse();
        new DelegateTask("t-1", "Draft") { EstimatedDuration = TimeSpan.FromMinutes(45) }.HasEstimate.Should().BeTrue();
    }

    [Fact]
    public void AMissingPriorityIsNotNormal_ItIsNoGradeAtAll()
    {
        var ungraded = new DelegateTask("t-1", "Draft");

        ungraded.Priority.Should().BeNull("an invented Normal would be a judgement nobody made");
        ungraded.HasPriority.Should().BeFalse();
        ungraded.HasKnownPriority.Should().BeFalse();

        var graded = ungraded with { Priority = DelegateTaskPriority.High };
        graded.HasPriority.Should().BeTrue();
        graded.HasKnownPriority.Should().BeTrue();
        ((DelegateTaskPriority)42).Should().NotBe(graded.Priority!.Value, "the enum is a closed set");
        (ungraded with { Priority = (DelegateTaskPriority)42 }).HasKnownPriority.Should().BeFalse();
    }

    [Fact]
    public void ABlankAssigneeCountsAsTheBacklog()
    {
        new DelegateTask("t-1", "Draft").IsAssigned.Should().BeFalse();
        new DelegateTask("t-1", "Draft") { AssignedAgentId = " " }.IsAssigned.Should().BeFalse();
        new DelegateTask("t-1", "Draft") { AssignedAgentId = " " }.AssignedAgentId.Should().BeNull();
        new DelegateTask("t-1", "Draft") { AssignedAgentId = "planner" }.IsAssigned.Should().BeTrue();
    }

    [Fact]
    public void TheRecordIsAValue_TheHostRecomposesItAndNothingMutatesIt()
    {
        var original = new DelegateTask("t-1", "Draft") { EstimatedDuration = TimeSpan.FromMinutes(30) };
        var assigned = original with { AssignedAgentId = "planner" };

        original.AssignedAgentId.Should().BeNull("with copies, it never edits");
        assigned.AssignedAgentId.Should().Be("planner");
        assigned.EstimatedDuration.Should().Be(TimeSpan.FromMinutes(30));
        original.IsAssignable.Should().BeTrue();
        assigned.IsAssignable.Should().BeFalse();
    }

    // ── the agent column ─────────────────────────────────────────────────────

    [Fact]
    public void AnAgentNeedsAReference()
    {
        var missing = () => new DelegateAgent(null!);

        missing.Should().Throw<ArgumentNullException>();
        new DelegateAgent(Agent("planner", "Planner")).Id.Should().Be("planner");
    }

    [Fact]
    public void CapacityIsTheHostsNumbers_NeverAiInventedOne()
    {
        var agent = new DelegateAgent(Agent("planner", "Planner")) { MaxConcurrency = 3, ActiveCount = 2 };

        agent.EffectiveMaxConcurrency.Should().Be(3);
        agent.Load.Should().Be(2);
        agent.RemainingSlots.Should().Be(1);
        agent.HasCapacity.Should().BeTrue();
        agent.IsFull.Should().BeFalse();

        (agent with { ActiveCount = 3 }).IsFull.Should().BeTrue();
        (agent with { ActiveCount = 9 }).RemainingSlots.Should().Be(0, "a full agent never reports a negative slot");
        (agent with { ActiveCount = 9 }).IsFull.Should().BeTrue();
    }

    [Fact]
    public void AConcurrencyBelowOneIsClamped_SoAnAgentCanAlwaysTakeOneTask()
    {
        var agent = new DelegateAgent(Agent("planner", "Planner")) { MaxConcurrency = 0 };

        agent.EffectiveMaxConcurrency.Should().Be(1);
        agent.RemainingSlots.Should().Be(1);
        agent.HasCapacity.Should().BeTrue("a column that refuses everything would be an unexplained refusal");

        (agent with { ActiveCount = -4 }).Load.Should().Be(0);
        (agent with { ActiveCount = -4 }).RemainingSlots.Should().Be(1);
    }

    [Fact]
    public void AStaleBusyUntilMeansFreeNow()
    {
        var agent = new DelegateAgent(Agent("planner", "Planner")) { BusyUntil = Ten.AddMinutes(-1) };

        agent.HasBusyUntil.Should().BeTrue("the host did report a time");
        agent.IsBusyAt(Ten).Should().BeFalse("a timestamp in the past must not paint a free agent as occupied");
        agent.FreeAt(Ten).Should().Be(Ten);

        var busy = agent with { BusyUntil = Ten.AddHours(2) };
        busy.IsBusyAt(Ten).Should().BeTrue();
        busy.FreeAt(Ten).Should().Be(Ten.AddHours(2));

        (agent with { BusyUntil = null }).IsBusyAt(Ten).Should().BeFalse();
        (agent with { BusyUntil = null }).FreeAt(Ten).Should().Be(Ten);
    }

    // ── the move request ─────────────────────────────────────────────────────

    [Fact]
    public void TheMoveRequestDescribesAssignmentReturnAndHandover()
    {
        var task = new DelegateTask("t-1", "Draft");
        var planner = new DelegateAgent(Agent("planner", "Planner"));
        var writer = new DelegateAgent(Agent("writer", "Writer"));

        var assignment = new DelegateMoveRequest(task, null, planner);
        assignment.IsAssignment.Should().BeTrue();
        assignment.IsUnassignment.Should().BeFalse();
        assignment.IsHandover.Should().BeFalse();
        assignment.AssigneeId.Should().Be("planner");

        var returned = new DelegateMoveRequest(task, planner, null);
        returned.IsAssignment.Should().BeFalse();
        returned.IsUnassignment.Should().BeTrue();
        returned.IsHandover.Should().BeFalse();
        returned.AssigneeId.Should().BeNull();

        var handover = new DelegateMoveRequest(task, planner, writer);
        handover.IsHandover.Should().BeTrue();
        handover.AssigneeId.Should().Be("writer");

        var same = new DelegateMoveRequest(task, planner, new DelegateAgent(Agent("planner", "Planner")));
        same.IsHandover.Should().BeFalse("the agent did not change, so nothing was handed over");
    }

    [Fact]
    public void TheMoveRequestNeedsATask()
    {
        var missing = () => new DelegateMoveRequest(null!, null, null);

        missing.Should().Throw<ArgumentNullException>();
    }
}
