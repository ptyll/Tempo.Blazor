using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Plan 3e96f6d8, phase 2 — the two contract types <c>TmAgentPlan</c> hands to its host:
/// <see cref="AgentPlanStep"/> (one step of the plan and the durations it exposes) and
/// <see cref="AgentPlanEdit"/> (WHICH edit the new list represents, so a host can log, persist or undo it
/// without diffing two lists).
/// </summary>
public class AgentPlanModelTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AStepWithoutAnId_GetsOne_SoTheListAlwaysHasAStableKey()
    {
        var first = new AgentPlanStep("Draft the outline");
        var second = new AgentPlanStep("Draft the outline");

        first.Id.Should().NotBeNullOrWhiteSpace();
        first.Id.Should().NotBe(second.Id, "two identical steps are still two rows");
        first.Title.Should().Be("Draft the outline");
        first.Status.Should().Be(AgentStepStatus.Pending);
        first.IsEditable.Should().BeTrue("a plan step is editable unless the host says otherwise");
        first.StartedAt.Should().BeNull();
        first.CompletedAt.Should().BeNull();
        first.Error.Should().BeNull();
    }

    [Fact]
    public void ANullTitle_IsStoredAsAnEmptyString_SoNothingRendersTheWordNull()
    {
        var step = new AgentPlanStep(null!);

        step.Title.Should().BeEmpty();
    }

    [Fact]
    public void AHostSuppliedId_IsKeptAndWhitespaceFallsBackToAGeneratedOne()
    {
        new AgentPlanStep("One", "s-1").Id.Should().Be("s-1");
        new AgentPlanStep("One", "   ").Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TheDuration_NeedsBothEnds_AndARunningStepElapsesToNow()
    {
        var running = new AgentPlanStep("One")
        {
            Status = AgentStepStatus.Running,
            StartedAt = Start,
        };

        running.Duration.Should().BeNull("a running step has no finished duration");
        running.Elapsed(Start.AddSeconds(42)).Should().Be(TimeSpan.FromSeconds(42));

        var done = running with { Status = AgentStepStatus.Done, CompletedAt = Start.AddSeconds(30) };
        done.Duration.Should().Be(TimeSpan.FromSeconds(30));
        done.Elapsed(Start.AddSeconds(600)).Should().Be(
            TimeSpan.FromSeconds(30), "a finished step keeps its recorded duration");
        done.IsFinished.Should().BeTrue();
    }

    [Fact]
    public void Elapsed_NeverGoesNegative_AndNeedsAStart()
    {
        var noStart = new AgentPlanStep("One") { Status = AgentStepStatus.Running };
        noStart.Elapsed(Start).Should().BeNull();

        var future = new AgentPlanStep("One") { Status = AgentStepStatus.Running, StartedAt = Start.AddSeconds(5) };
        future.Elapsed(Start).Should().Be(TimeSpan.Zero, "a clock skew must not paint a negative duration");
    }

    [Theory]
    [InlineData(AgentStepStatus.Done, true)]
    [InlineData(AgentStepStatus.Failed, true)]
    [InlineData(AgentStepStatus.Skipped, true)]
    [InlineData(AgentStepStatus.Running, false)]
    [InlineData(AgentStepStatus.Pending, false)]
    public void IsFinished_CoversEveryTerminalState(AgentStepStatus status, bool expected)
    {
        new AgentPlanStep("One") { Status = status }.IsFinished.Should().Be(expected);
    }

    [Fact]
    public void With_CopiesTheStep_SoARenameCannotMutateTheHostsSnapshot()
    {
        var original = new AgentPlanStep("Draft the outline", "s1");

        var renamed = original with { Title = "Draft the summary" };

        renamed.Title.Should().Be("Draft the summary");
        renamed.Id.Should().Be("s1");
        original.Title.Should().Be("Draft the outline");
    }

    [Fact]
    public void RenameEdit_NamesTheRowAndTheNewTitle()
    {
        var edit = AgentPlanEdit.Rename(2, "s3", "Review with the client");

        edit.Kind.Should().Be(AgentPlanEditKind.Rename);
        edit.Index.Should().Be(2);
        edit.StepId.Should().Be("s3");
        edit.Title.Should().Be("Review with the client");
        edit.Step.Should().BeNull();
        edit.FromIndex.Should().BeNull();
        edit.ToIndex.Should().BeNull();
    }

    [Fact]
    public void AddEdit_CarriesTheInsertedStep_AndItsPosition()
    {
        var step = new AgentPlanStep("New step");

        var edit = AgentPlanEdit.Add(1, step);

        edit.Kind.Should().Be(AgentPlanEditKind.Add);
        edit.Index.Should().Be(1);
        edit.Step.Should().BeSameAs(step);
        edit.StepId.Should().Be(step.Id);
    }

    [Fact]
    public void RemoveEdit_CarriesTheRemovedStep_SoTheHostCanUndoIt()
    {
        var step = new AgentPlanStep("Review with the team", "s3");

        var edit = AgentPlanEdit.Remove(2, step);

        edit.Kind.Should().Be(AgentPlanEditKind.Remove);
        edit.Index.Should().Be(2);
        edit.Step.Should().BeSameAs(step);
    }

    [Fact]
    public void ReorderEdit_NamesBothIndexes_AndTheTargetIndexIsTheNewPosition()
    {
        var edit = AgentPlanEdit.Reorder(0, 2, "s1");

        edit.Kind.Should().Be(AgentPlanEditKind.Reorder);
        edit.FromIndex.Should().Be(0);
        edit.ToIndex.Should().Be(2);
        edit.Index.Should().Be(2, "Index is always the row's position in the NEW list");
        edit.StepId.Should().Be("s1");
    }

    [Fact]
    public void TheFactories_RejectNonsense()
    {
        var step = new AgentPlanStep("One");

        var negative = () => AgentPlanEdit.Rename(-1, "s1", "Title");
        var blankTitle = () => AgentPlanEdit.Rename(0, "s1", "   ");
        var missingStep = () => AgentPlanEdit.Add(0, null!);
        var negativeAdd = () => AgentPlanEdit.Add(-1, step);
        var negativeReorder = () => AgentPlanEdit.Reorder(-1, 0, "s1");

        negative.Should().Throw<ArgumentOutOfRangeException>();
        blankTitle.Should().Throw<ArgumentException>();
        missingStep.Should().Throw<ArgumentNullException>();
        negativeAdd.Should().Throw<ArgumentOutOfRangeException>();
        negativeReorder.Should().Throw<ArgumentOutOfRangeException>();
    }
}
