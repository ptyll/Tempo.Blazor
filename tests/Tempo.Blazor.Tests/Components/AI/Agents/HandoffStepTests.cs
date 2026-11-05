using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 5 — the <c>HandoffStep</c> / <c>HandoffItem</c> model and its chain roll-up.
/// <para>
/// The model is what <c>TmAgentHandoff</c> renders and what a host folds its own run into, so these facts pin the
/// rules the surface is allowed to rely on: a blank id is generated, a step that has not been classified is
/// <c>Pending</c>, the item lists are never null, the counts and the loss rate are arithmetic over exactly those
/// lists, and the chain summary can never disagree with the steps it summarises.
/// </para>
/// </summary>
public class HandoffStepTests
{
    private static readonly DateTimeOffset Ten = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    private static HandoffStep Step(
        string id = "step-1",
        HandoffStatus status = HandoffStatus.Accepted,
        int passed = 2,
        int lost = 0,
        bool withLostReasons = true)
    {
        var passedItems = Enumerable.Range(0, passed)
            .Select(index => new HandoffItem($"passed-{index}", HandoffItemKind.Context))
            .ToList();
        var lostItems = Enumerable.Range(0, lost)
            .Select(index => new HandoffItem($"lost-{index}", HandoffItemKind.Tool)
            {
                Reason = withLostReasons ? "the tool was not granted" : null,
            })
            .ToList();

        return new HandoffStep(id, "Handing the invoice run over")
        {
            FromAgent = new AgentRef("Fetcher", "w-fetch") { ColorIndex = 2 },
            ToAgent = new AgentRef("Matcher", "w-match") { ColorIndex = 5 },
            At = Ten,
            Status = status,
            PassedItems = passedItems,
            LostItems = lostItems,
        };
    }

    // ── Identity and defaults ────────────────────────────────────────────────

    [Fact]
    public void ABlankId_IsGenerated_AndASuppliedOneIsKept()
    {
        new HandoffStep(" ", "note").Id.Should().NotBeNullOrWhiteSpace();
        new HandoffStep("step-a", "note").Id.Should().Be("step-a");
    }

    [Fact]
    public void AnUnclassifiedStep_IsPending_AndItsItemListsAreNeverNull()
    {
        var step = new HandoffStep("step-a", null!);

        step.Status.Should().Be(HandoffStatus.Pending,
            "an unset status must never claim the work was taken over");
        step.Status.Should().Be(default(HandoffStatus));
        step.Summary.Should().BeEmpty();
        step.PassedItems.Should().NotBeNull();
        step.LostItems.Should().NotBeNull();
        step.ItemCount.Should().Be(0);
    }

    [Fact]
    public void NullItemLists_ReadAsEmptyLists()
    {
        var step = new HandoffStep("step-a", "note") { PassedItems = null!, LostItems = null! };

        step.PassedCount.Should().Be(0);
        step.LostCount.Should().Be(0);
        step.HasLosses.Should().BeFalse();
    }

    [Fact]
    public void AnUnknownStatus_IsNotDeclared()
    {
        (new HandoffStep("s", "x") { Status = (HandoffStatus)9 }).HasKnownStatus.Should().BeFalse();
        Step().HasKnownStatus.Should().BeTrue();
    }

    // ── Identity of the agents ───────────────────────────────────────────────

    [Fact]
    public void TheAgentNamesComeFromTheSharedIdentity_AndFallBackToTheRawIds()
    {
        var step = Step();

        step.FromName.Should().Be("Fetcher");
        step.ToName.Should().Be("Matcher");
        step.HasFrom.Should().BeTrue();
        step.HasTo.Should().BeTrue();
        step.FromColorToken.Should().Be("--tm-agent-color-2");
        step.ToColorToken.Should().Be("--tm-agent-color-5");

        var byId = new HandoffStep("s", "x") { FromAgentId = "w-a", ToAgentId = "w-b" };
        byId.FromName.Should().Be("w-a");
        byId.ToName.Should().Be("w-b");
        byId.FromColorToken.Should().BeEmpty("a raw id carries no palette slot");

        var none = new HandoffStep("s", "x");
        none.HasFrom.Should().BeFalse();
        none.HasTo.Should().BeFalse();
        none.FromName.Should().BeEmpty();
        none.ToName.Should().BeEmpty();
    }

    [Fact]
    public void AnAgentRef_WinsOverARawId()
    {
        var step = Step() with { FromAgentId = "ignored", ToAgentId = "ignored" };

        step.FromName.Should().Be("Fetcher");
        step.ToName.Should().Be("Matcher");
    }

    [Fact]
    public void ADisagreeingRawId_IsIgnored_WhenTheAgentRefCarriesTheIdentity()
    {
        // The two shapes may coexist — a host mid-migration fills both. There is no merging and no
        // validation: the AgentRef is the whole identity, so a raw id that names a DIFFERENT agent is
        // simply not read (the alternative — preferring the id — would paint a name from one agent on
        // the avatar and colour of another).
        var step = Step() with { FromAgentId = "some-other-agent", ToAgentId = "some-third-agent" };

        step.FromAgent!.Id.Should().NotBe(step.FromAgentId, "the two identities deliberately disagree");
        step.FromName.Should().Be("Fetcher");
        step.ToName.Should().Be("Matcher");
        step.FromColorToken.Should().Be("--tm-agent-color-2", "the colour follows the AgentRef too");
        step.ToColorToken.Should().Be("--tm-agent-color-5");
    }

    // ── Counts, losses and the derived states ────────────────────────────────

    [Fact]
    public void TheCountsAndTheLossRate_AreArithmeticOverTheTwoLists()
    {
        var step = Step(passed: 3, lost: 1);

        step.PassedCount.Should().Be(3);
        step.LostCount.Should().Be(1);
        step.ItemCount.Should().Be(4);
        step.HasLosses.Should().BeTrue();
        step.LossRate.Should().Be(0.25);
    }

    [Fact]
    public void AStepThatCarriedNothing_HasNoLossRate()
    {
        var step = Step(passed: 0, lost: 0);

        step.LossRate.Should().Be(0d, "there is no rate without a whole");
        step.HasLosses.Should().BeFalse();
    }

    [Fact]
    public void ALossThatIsEverything_IsOne()
    {
        Step(passed: 0, lost: 2).LossRate.Should().Be(1d);
    }

    [Fact]
    public void TheSettledPendingAndRejectedFlags_FollowTheStatus()
    {
        Step(status: HandoffStatus.Accepted).IsSettled.Should().BeTrue();
        Step(status: HandoffStatus.Completed).IsSettled.Should().BeTrue();
        Step(status: HandoffStatus.Pending).IsPending.Should().BeTrue();
        Step(status: HandoffStatus.Pending).IsSettled.Should().BeFalse();
        Step(status: HandoffStatus.Rejected).IsRejected.Should().BeTrue();
        Step(status: HandoffStatus.Rejected).IsSettled.Should().BeFalse(
            "a refused handoff was never taken over");
    }

    [Fact]
    public void TheLostLabelsAreDistinct_AndKeepTheirOrder()
    {
        var step = new HandoffStep("s", "x")
        {
            LostItems =
            [
                new HandoffItem("conversation history"),
                new HandoffItem("Conversation History"),
                new HandoffItem("invoice schema"),
            ],
        };

        step.LostLabels.Should().Equal("conversation history", "invoice schema");
    }

    // ── HandoffItem ──────────────────────────────────────────────────────────

    [Fact]
    public void AnItem_DefaultsToContext_AndCarriesNoReason()
    {
        var item = new HandoffItem("the task statement");

        item.Kind.Should().Be(HandoffItemKind.Context);
        item.HasLabel.Should().BeTrue();
        item.HasReason.Should().BeFalse();
        item.HasKnownKind.Should().BeTrue();
    }

    [Fact]
    public void ALostItem_CopiesTheLabelAndKind_AndAddsTheReason()
    {
        var passed = new HandoffItem("invoice schema", HandoffItemKind.Artifact);

        var lost = passed.Lost("the file exceeded the upload limit");

        lost.Label.Should().Be(passed.Label);
        lost.Kind.Should().Be(HandoffItemKind.Artifact);
        lost.Reason.Should().Be("the file exceeded the upload limit");
        lost.HasReason.Should().BeTrue();
        passed.HasReason.Should().BeFalse("the original item is a value and is never mutated");
    }

    [Fact]
    public void AnItemWithNoLabel_AndAnUnknownKind_IsReported()
    {
        var item = new HandoffItem("  ", (HandoffItemKind)12);

        item.HasLabel.Should().BeFalse();
        item.HasKnownKind.Should().BeFalse();
    }

    // ── The chain summary ────────────────────────────────────────────────────

    [Fact]
    public void AnEmptyOrNullChain_RollsUpToTheEmptySummary()
    {
        HandoffChainSummary.Create(null).Should().Be(HandoffChainSummary.Empty);
        HandoffChainSummary.Create([]).Should().Be(HandoffChainSummary.Empty);

        var empty = HandoffChainSummary.Empty;
        empty.HasSteps.Should().BeFalse();
        empty.IsComplete.Should().BeFalse();
        empty.LossRate.Should().Be(0d);
        empty.WorstStepId.Should().BeNull();
        empty.LostLabels.Should().BeEmpty();
    }

    [Fact]
    public void TheSummary_CountsEveryStateAndSumsBothItemLists()
    {
        var summary = HandoffChainSummary.Create(
        [
            Step("a", HandoffStatus.Completed, passed: 2, lost: 0),
            Step("b", HandoffStatus.Accepted, passed: 1, lost: 2),
            Step("c", HandoffStatus.Pending, passed: 0, lost: 1),
            Step("d", HandoffStatus.Rejected, passed: 0, lost: 0),
        ]);

        summary.StepCount.Should().Be(4);
        summary.CompletedCount.Should().Be(1);
        summary.AcceptedCount.Should().Be(1);
        summary.PendingCount.Should().Be(1);
        summary.RejectedCount.Should().Be(1);
        summary.SettledCount.Should().Be(2);
        summary.PassedItems.Should().Be(3);
        summary.LostItems.Should().Be(3);
        summary.ItemCount.Should().Be(6);
        summary.LossRate.Should().Be(0.5);
        summary.HasLosses.Should().BeTrue();
        summary.HasPending.Should().BeTrue();
        summary.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void AChainIsComplete_OnlyWhenItHasStepsAndEveryOneIsCompleted()
    {
        HandoffChainSummary.Create([Step("a", HandoffStatus.Completed)]).IsComplete.Should().BeTrue();
        HandoffChainSummary.Create(
            [Step("a", HandoffStatus.Completed), Step("b", HandoffStatus.Accepted)]).IsComplete.Should().BeFalse();
    }

    [Fact]
    public void TheWorstStep_IsTheOneThatLostTheMost_AndTheFirstOneOnATie()
    {
        var summary = HandoffChainSummary.Create(
        [
            Step("a", passed: 1, lost: 1),
            Step("b", passed: 0, lost: 3),
            Step("c", passed: 0, lost: 3),
        ]);

        summary.WorstStepId.Should().Be("b", "a tie keeps the earlier step, so the pointer is deterministic");
        HandoffChainSummary.Create([Step("a", passed: 4, lost: 0)]).WorstStepId.Should().BeNull();
    }

    [Fact]
    public void TheChainLostLabels_AreDeduplicatedAcrossSteps_InChainOrder()
    {
        var summary = HandoffChainSummary.Create(
        [
            new HandoffStep("a", "x")
            {
                LostItems = [new HandoffItem("tool catalogue"), new HandoffItem("user deadline")],
            },
            new HandoffStep("b", "y")
            {
                LostItems = [new HandoffItem("Tool Catalogue"), new HandoffItem("attachment")],
            },
        ]);

        summary.LostLabels.Should().Equal("tool catalogue", "user deadline", "attachment");
        summary.LostItems.Should().Be(4, "the count is how many pieces were dropped, not how many labels there are");
    }

    [Fact]
    public void ANullStepInsideTheChain_IsSkipped()
    {
        var summary = HandoffChainSummary.Create([Step("a", passed: 1, lost: 1), null!, Step("b", passed: 2)]);

        summary.StepCount.Should().Be(2);
        summary.PassedItems.Should().Be(3);
        summary.LostItems.Should().Be(1);
    }
}
