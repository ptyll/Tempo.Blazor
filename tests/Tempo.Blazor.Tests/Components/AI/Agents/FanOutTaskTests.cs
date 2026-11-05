using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 4 — the pure half of <c>TmFanOutGrid</c>: one unit of work (<see cref="FanOutTask"/>) and the
/// roll-up the orchestrator's header prints (<see cref="FanOutSummary"/>).
/// <para>
/// The tests pin the rules a surface and a test must agree on: an attempt below 1 reads as 1, "no attempt limit" is not
/// a limit, a retry is offered only for a FAILED task whose retries are not used up, a cancel only for work that still
/// holds a worker, an estimate is a countdown and never a fact, an unpriced task is not free, and the summary's
/// estimate is the latest ACTIVE one (a finished task cannot postpone the run).
/// </para>
/// </summary>
public class FanOutTaskTests
{
    private static readonly DateTimeOffset Ten = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private static FanOutTask Task(
        string id = "t1",
        AgentActivityStatus status = AgentActivityStatus.Running,
        int attempt = 1,
        int? maxAttempts = null,
        long? cost = null,
        DateTimeOffset? started = null,
        DateTimeOffset? estimate = null,
        string? error = null)
        => new(id, "Region " + id)
        {
            Status = status,
            Attempt = attempt,
            MaxAttempts = maxAttempts,
            CostMicroUnits = cost,
            StartedAt = started,
            EstimatedEndAt = estimate,
            Error = error,
        };

    // ── Identity and fallbacks ───────────────────────────────────────────────

    [Fact]
    public void AMissingId_IsGenerated_SoAListBuiltInALoopStillRenders()
    {
        new FanOutTask(" ", "Region").Id.Should().NotBeNullOrWhiteSpace();
        new FanOutTask("a", "Region").Id.Should().Be("a");
    }

    [Fact]
    public void AMissingLabel_IsNotALabel_AndTheFallbackIsTheSurfacesOwn()
    {
        var task = new FanOutTask("t", "   ");

        task.HasLabel.Should().BeFalse("a whitespace label is no label");
        task.Initials.Should().Be("?", "an avatar must never be an empty circle");
    }

    [Fact]
    public void TheWorkerPaintsTheSharedIdentity_AndThePlainIdIsTheFallback()
    {
        var anonymous = Task() with { WorkerId = "w-7" };
        anonymous.WorkerName.Should().Be("w-7");
        anonymous.HasWorker.Should().BeTrue();
        anonymous.ColorToken.Should().BeEmpty("a worker tracked by id has no palette identity");

        var named = Task() with
        {
            WorkerId = "w-7",
            Worker = new AgentRef("Fetcher", "w-7") { ColorIndex = 3 },
        };

        named.WorkerName.Should().Be("Fetcher", "the identity carries the whole truth and wins over the bare id");
        named.ColorToken.Should().Be("--tm-agent-color-3");
        named.Initials.Should().Be("F");
    }

    [Fact]
    public void ATaskWithoutAWorker_HasNoWorker()
    {
        Task().HasWorker.Should().BeFalse();
        Task().WorkerName.Should().BeEmpty();
    }

    // ── Attempts and retry ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    public void TheAttemptIsNeverBelowOne(int supplied, int read)
        => Task(attempt: supplied).EffectiveAttempt.Should().Be(read);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-2)]
    public void AnAbsentOrNonPositiveLimit_IsNoLimit(int? limit)
    {
        var task = Task(status: AgentActivityStatus.Failed, attempt: 9, maxAttempts: limit);

        task.HasAttemptLimit.Should().BeFalse();
        task.RetryExhausted.Should().BeFalse("a host that never set a limit must not block a retry");
        task.CanRetry.Should().BeTrue();
    }

    [Fact]
    public void RetriesAreExhaustedOnlyWhenTheLimitIsReached()
    {
        Task(status: AgentActivityStatus.Failed, attempt: 1, maxAttempts: 3).CanRetry.Should().BeTrue();
        Task(status: AgentActivityStatus.Failed, attempt: 2, maxAttempts: 3).CanRetry.Should().BeTrue();
        Task(status: AgentActivityStatus.Failed, attempt: 3, maxAttempts: 3).CanRetry.Should().BeFalse();
        Task(status: AgentActivityStatus.Failed, attempt: 4, maxAttempts: 3).RetryExhausted.Should().BeTrue();
    }

    [Theory]
    [InlineData(AgentActivityStatus.Running)]
    [InlineData(AgentActivityStatus.Queued)]
    [InlineData(AgentActivityStatus.Succeeded)]
    [InlineData(AgentActivityStatus.Cancelled)]
    public void OnlyAFailedTask_OffersARetry(AgentActivityStatus status)
        => Task(status: status, attempt: 1, maxAttempts: 3).CanRetry.Should().BeFalse();

    [Theory]
    [InlineData(AgentActivityStatus.Queued, true)]
    [InlineData(AgentActivityStatus.Running, true)]
    [InlineData(AgentActivityStatus.WaitingForApproval, true)]
    [InlineData(AgentActivityStatus.Succeeded, false)]
    [InlineData(AgentActivityStatus.Failed, false)]
    [InlineData(AgentActivityStatus.Cancelled, false)]
    public void OnlyWorkThatStillHoldsAWorker_OffersACancel(AgentActivityStatus status, bool canCancel)
    {
        var task = Task(status: status);

        task.CanCancel.Should().Be(canCancel);
        task.IsActive.Should().Be(canCancel);
        task.IsFinished.Should().Be(!canCancel && status != AgentActivityStatus.Idle);
    }

    // ── Estimate and money ──────────────────────────────────────────────────

    [Fact]
    public void TheEstimateIsALiveCountdown_NotAFact()
    {
        var task = Task(estimate: Ten.AddSeconds(90));

        task.RemainingAt(Ten).Should().Be(TimeSpan.FromSeconds(90));
        task.IsOverdueAt(Ten).Should().BeFalse();
    }

    [Fact]
    public void AnOverdueEstimateReadsAsZero_AndNeverAsANegativeDuration()
    {
        var task = Task(estimate: Ten.AddSeconds(-30));

        task.RemainingAt(Ten).Should().Be(TimeSpan.Zero);
        task.IsOverdueAt(Ten).Should().BeTrue();
    }

    [Theory]
    [InlineData(AgentActivityStatus.Succeeded)]
    [InlineData(AgentActivityStatus.Failed)]
    [InlineData(AgentActivityStatus.Cancelled)]
    public void AFinishedTaskHasNothingLeftToCount(AgentActivityStatus status)
    {
        var task = Task(status: status, estimate: Ten.AddMinutes(5));

        task.RemainingAt(Ten).Should().BeNull();
        task.IsOverdueAt(Ten).Should().BeFalse();
    }

    [Fact]
    public void NoEstimateMeansNothingToCount()
    {
        Task().RemainingAt(Ten).Should().BeNull();
        Task().IsOverdueAt(Ten).Should().BeFalse();
    }

    [Fact]
    public void AnUnpricedTask_IsNotFree()
    {
        Task().HasCost.Should().BeFalse();
        Task(cost: 0).HasCost.Should().BeTrue("a priced zero is a real price");
        Task(error: "  ").HasError.Should().BeFalse();
    }

    // ── The summary ─────────────────────────────────────────────────────────

    private static FanOutSummary Summary() => FanOutSummary.Create(
    [
        Task("q", AgentActivityStatus.Queued, cost: 100_000, estimate: Ten.AddMinutes(2)),
        Task("r1", AgentActivityStatus.Running, cost: 250_000, estimate: Ten.AddMinutes(4)),
        Task("r2", AgentActivityStatus.Running, estimate: Ten.AddMinutes(3)),
        Task("w", AgentActivityStatus.WaitingForApproval),
        Task("s1", AgentActivityStatus.Succeeded, cost: 500_000, estimate: Ten.AddMinutes(30)),
        Task("s2", AgentActivityStatus.Succeeded),
        Task("f", AgentActivityStatus.Failed, attempt: 3, maxAttempts: 3, error: "429"),
        Task("c", AgentActivityStatus.Cancelled),
        Task("i", AgentActivityStatus.Idle),
    ]);

    [Fact]
    public void TheSummaryCountsEveryState()
    {
        var summary = Summary();

        summary.Total.Should().Be(9);
        summary.Queued.Should().Be(1);
        summary.Running.Should().Be(2);
        summary.WaitingForApproval.Should().Be(1);
        summary.Succeeded.Should().Be(2);
        summary.Failed.Should().Be(1);
        summary.Cancelled.Should().Be(1);
        summary.Idle.Should().Be(1, "an idle task is neither finished nor running");
    }

    [Fact]
    public void TheSummarySeparatesFinishedFromActive()
    {
        var summary = Summary();

        summary.Finished.Should().Be(4);
        summary.Active.Should().Be(4);
        summary.IsComplete.Should().BeFalse();
        summary.HasFailures.Should().BeTrue();
        summary.Progress.Should().BeApproximately(4d / 9d, 0.0001);
    }

    [Fact]
    public void TheCostIsTheSumOfThePricedTasks_AndUnpricedIsNotZero()
    {
        Summary().TotalCostMicroUnits.Should().Be(850_000);
        Summary().HasCost.Should().BeTrue();

        var unpriced = FanOutSummary.Create([Task("a"), Task("b")]);
        unpriced.HasCost.Should().BeFalse("a surface hides the money line instead of claiming the run was free");
        unpriced.TotalCostMicroUnits.Should().Be(0);
    }

    [Fact]
    public void TheEstimateIsTheLatestActiveOne_SoFinishedWorkCannotPostponeTheRun()
    {
        var summary = Summary();

        summary.EstimatedEndAt.Should().Be(Ten.AddMinutes(4),
            "the succeeded task's +30 minutes estimate belongs to work that is already over");
        summary.RemainingAt(Ten).Should().Be(TimeSpan.FromMinutes(4));
        summary.HasEstimate.Should().BeTrue();
    }

    [Fact]
    public void ACompleteFanOut_StopsCounting()
    {
        var done = FanOutSummary.Create(
        [
            Task("a", AgentActivityStatus.Succeeded, estimate: Ten.AddMinutes(10)),
            Task("b", AgentActivityStatus.Failed, error: "boom"),
        ]);

        done.IsComplete.Should().BeTrue();
        done.Active.Should().Be(0);
        done.Progress.Should().Be(1d);
        done.EstimatedEndAt.Should().BeNull();
        done.RemainingAt(Ten).Should().BeNull();
    }

    [Fact]
    public void AnEmptyOrNullList_RollsUpToAnEmptySummary()
    {
        foreach (var summary in new[] { FanOutSummary.Create(null), FanOutSummary.Create([]) })
        {
            summary.Total.Should().Be(0);
            summary.IsComplete.Should().BeFalse("an empty fan-out has not completed, it has not started");
            summary.Progress.Should().Be(0);
            summary.HasCost.Should().BeFalse();
            summary.HasEstimate.Should().BeFalse();
            summary.RemainingAt(Ten).Should().BeNull();
        }
    }

    [Fact]
    public void ANullEntryInTheList_IsIgnored()
    {
        var summary = FanOutSummary.Create([Task("a", AgentActivityStatus.Succeeded), null!]);

        summary.Total.Should().Be(1);
        summary.Succeeded.Should().Be(1);
    }
}
