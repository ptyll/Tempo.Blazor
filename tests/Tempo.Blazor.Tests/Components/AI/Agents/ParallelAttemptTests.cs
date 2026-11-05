using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 4 — the pure half of <c>TmParallelAttempts</c>: one attempt, one vote and the ONE rule that
/// decides which attempt is best.
/// <para>
/// The tests pin the conservatism the surface depends on: an unset vote outcome is an abstention (never an approval), a
/// score without a verdict is legal, an unscored attempt is not "zero", a running attempt does not beat a finished one
/// on an equal score, and the host's explicit pick wins even with no numbers behind it.
/// </para>
/// </summary>
public class ParallelAttemptTests
{
    private static ParallelAttempt Attempt(
        string id,
        double? score = null,
        AgentActivityStatus status = AgentActivityStatus.Succeeded,
        params AttemptVote[] votes)
        => new(id, "Draft " + id) { Score = score, Status = status, Votes = votes };

    // ── Votes ───────────────────────────────────────────────────────────────

    [Fact]
    public void AnUnsetOutcome_IsAnAbstention_NotAnApproval()
    {
        var vote = new AttemptVote("Grader") { Score = 0.82 };

        vote.Outcome.Should().Be(AttemptVoteOutcome.Abstain);
        vote.HasScore.Should().BeTrue();
        vote.HasRationale.Should().BeFalse();
    }

    [Fact]
    public void AVotePaintsTheSharedIdentity_AndFallsBackToItsName()
    {
        var named = new AttemptVote("") { By = new AgentRef("Reviewer", "r1") { ColorIndex = 2 } };

        named.DisplayName.Should().Be("Reviewer", "the identity names the voter when the vote does not");
        named.Initials.Should().Be("R");
        named.ColorToken.Should().Be("--tm-agent-color-2");

        var plain = new AttemptVote("Dana");
        plain.DisplayName.Should().Be("Dana");
        plain.ColorToken.Should().BeEmpty();
        plain.Initials.Should().Be("D");
    }

    [Fact]
    public void ANonFiniteScore_IsNoScore()
    {
        new AttemptVote("Grader") { Score = double.NaN }.HasScore.Should().BeFalse();
        new AttemptVote("Grader") { Score = double.PositiveInfinity }.HasScore.Should().BeFalse();
        new AttemptVote("Grader") { Score = 0 }.HasScore.Should().BeTrue("zero is a real score");
    }

    [Fact]
    public void AnAttemptCountsItsVotesByOutcome()
    {
        var attempt = Attempt(
            "a",
            0.5,
            AgentActivityStatus.Succeeded,
            new AttemptVote("One") { Outcome = AttemptVoteOutcome.Approve },
            new AttemptVote("Two") { Outcome = AttemptVoteOutcome.Approve, Rationale = "Covers the edge case" },
            new AttemptVote("Three") { Outcome = AttemptVoteOutcome.Reject },
            new AttemptVote("Four") { Score = 0.4 });

        attempt.VoteCount.Should().Be(4);
        attempt.Approvals.Should().Be(2);
        attempt.Rejections.Should().Be(1);
        attempt.Abstentions.Should().Be(1);
        attempt.HasRationales.Should().BeTrue();
        attempt.HasVotes.Should().BeTrue();
    }

    [Fact]
    public void AVoteListThatWasNeverSupplied_IsEmptyAndNotANullTrap()
    {
        var attempt = new ParallelAttempt("a", "Draft") { Votes = null! };

        attempt.Votes.Should().BeEmpty();
        attempt.HasVotes.Should().BeFalse();
        attempt.HasRationales.Should().BeFalse();
        attempt.HasScore.Should().BeFalse();
        attempt.HasSummary.Should().BeFalse();
    }

    // ── Ranking ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheHighestScoreWins()
    {
        ParallelAttempt[] attempts =
        [
            Attempt("a", 0.61),
            Attempt("b", 0.93),
            Attempt("c", 0.74),
        ];

        ParallelAttemptRanking.Best(attempts)!.Id.Should().Be("b");
    }

    [Fact]
    public void AnUnscoredAttempt_NeverWinsByBeingUnscored()
    {
        ParallelAttempt[] attempts = [Attempt("a"), Attempt("b", 0.2)];

        ParallelAttemptRanking.Best(attempts)!.Id.Should().Be("b");
    }

    [Fact]
    public void NothingScoredMeansNoBest()
        => ParallelAttemptRanking.Best([Attempt("a"), Attempt("b")]).Should().BeNull();

    [Fact]
    public void TheHostsExplicitPickWins_EvenWithoutAScore()
    {
        ParallelAttempt[] attempts = [Attempt("a", 0.9), Attempt("b", 0.1)];

        ParallelAttemptRanking.Best(attempts, "b")!.Id.Should().Be("b",
            "the host may know something the numbers do not");
    }

    [Fact]
    public void AnExplicitPickThatNamesNothing_FallsBackToTheScore()
    {
        ParallelAttempt[] attempts = [Attempt("a", 0.9), Attempt("b", 0.1)];

        ParallelAttemptRanking.Best(attempts, "gone")!.Id.Should().Be("a");
        ParallelAttemptRanking.Best(attempts, "  ")!.Id.Should().Be("a");
    }

    [Fact]
    public void ATieKeepsTheEarlierAttempt()
    {
        ParallelAttempt[] attempts = [Attempt("a", 0.5), Attempt("b", 0.5)];

        ParallelAttemptRanking.Best(attempts)!.Id.Should().Be("a");
    }

    [Fact]
    public void ATieGoesToTheAttemptThatActuallyFinished()
    {
        ParallelAttempt[] attempts =
        [
            Attempt("running", 0.5, AgentActivityStatus.Running),
            Attempt("done", 0.5, AgentActivityStatus.Succeeded),
        ];

        ParallelAttemptRanking.Best(attempts)!.Id.Should().Be("done",
            "the numbers are equal and only one of them is a result a reader can use");
    }

    [Fact]
    public void AnEmptyOrNullListHasNoBest()
    {
        ParallelAttemptRanking.Best(null).Should().BeNull();
        ParallelAttemptRanking.Best([]).Should().BeNull();
        ParallelAttemptRanking.Best([null!]).Should().BeNull();
    }

    [Fact]
    public void OrderingPutsTheBestFirst_AndKeepsTheRestStable()
    {
        ParallelAttempt[] attempts = [Attempt("a", 0.2), Attempt("b", 0.9), Attempt("c", 0.5)];

        ParallelAttemptRanking.Order(attempts).Select(attempt => attempt.Id).Should().Equal("b", "a", "c");
        ParallelAttemptRanking.Order(attempts, "a").Select(attempt => attempt.Id).Should().Equal("a", "b", "c");
        ParallelAttemptRanking.Order(null).Should().BeEmpty();
    }
}
