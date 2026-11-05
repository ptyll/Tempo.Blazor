namespace Tempo.Blazor.AI.Models;

/// <summary>
/// The roll-up a fan-out surface prints above its tiles: how many units of work finished, are still running or failed,
/// what the whole fan-out has cost so far and when it is expected to be over.
/// <para>
/// A pure VALUE computed from a task list (<see cref="Create"/>), so the header, the detail surface and a test read the
/// same numbers — a component never recomputes a summary inline. It is a snapshot: it carries no clock, and the live
/// countdown is <see cref="RemainingAt"/> measured by the caller.
/// </para>
/// <para>
/// The cost is the SUM of the tasks the host priced; a task without a price contributes nothing and
/// <see cref="HasCost"/> stays <c>false</c> when NO task carries one, so a surface hides the money line instead of
/// claiming the run was free.
/// </para>
/// </summary>
public sealed record FanOutSummary
{
    /// <summary>How many tasks the summary counted. Zero is a legal, empty fan-out.</summary>
    public int Total { get; init; }

    /// <summary>How many tasks are queued and not yet picked up by a worker.</summary>
    public int Queued { get; init; }

    /// <summary>How many tasks are running right now.</summary>
    public int Running { get; init; }

    /// <summary>How many tasks are waiting for a human decision.</summary>
    public int WaitingForApproval { get; init; }

    /// <summary>How many tasks succeeded.</summary>
    public int Succeeded { get; init; }

    /// <summary>How many tasks failed (a failed task may still be retried).</summary>
    public int Failed { get; init; }

    /// <summary>How many tasks were cancelled.</summary>
    public int Cancelled { get; init; }

    /// <summary>How many tasks are neither running nor finished (an unknown state counts as waiting).</summary>
    public int Idle { get; init; }

    /// <summary>The sum of the priced tasks, in MICRO-UNITS (1,000,000 is 1.00 of the currency).</summary>
    public long TotalCostMicroUnits { get; init; }

    /// <summary>Whether at least one task carried a price — <c>false</c> means "not priced", not "free".</summary>
    public bool HasCost { get; init; }

    /// <summary>
    /// The LATEST estimate among the ACTIVE tasks, or <c>null</c> when no active task carries one. A finished task's
    /// estimate is ignored: the fan-out cannot end later because of work that is already over.
    /// </summary>
    public DateTimeOffset? EstimatedEndAt { get; init; }

    /// <summary>How many tasks reached a terminal state (succeeded, failed or cancelled).</summary>
    public int Finished => Succeeded + Failed + Cancelled;

    /// <summary>How many tasks still hold a worker (queued, running or waiting for a decision).</summary>
    public int Active => Queued + Running + WaitingForApproval;

    /// <summary>Whether there is nothing left to wait for: a non-empty fan-out with no active task.</summary>
    public bool IsComplete => Total > 0 && Active == 0;

    /// <summary>Whether the fan-out ended with at least one failed task that was never recovered.</summary>
    public bool HasFailures => Failed > 0;

    /// <summary>
    /// The share of tasks that reached a terminal state, from 0 to 1; an empty fan-out reports 0 instead of a
    /// division by zero.
    /// </summary>
    public double Progress => Total == 0 ? 0d : (double)Finished / Total;

    /// <summary>
    /// The time left until the last active task's estimate, measured on the caller's clock, or <c>null</c> when there
    /// is nothing to count (complete, or no estimate). An estimate in the past reads as zero, never as a negative
    /// duration.
    /// </summary>
    /// <param name="now">The current moment, from the caller's clock.</param>
    /// <returns>The remaining time, or <c>null</c> when there is nothing to count.</returns>
    public TimeSpan? RemainingAt(DateTimeOffset now)
    {
        if (!HasEstimate)
        {
            return null;
        }

        var remaining = EstimatedEndAt!.Value - now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    /// <summary>Whether the fan-out still has work AND at least one estimate to count down.</summary>
    public bool HasEstimate => Active > 0 && EstimatedEndAt is not null;

    /// <summary>
    /// Rolls a task list up. The order of the list is irrelevant to every number; a null list counts as empty, so a
    /// component never has to null-check the host's data.
    /// </summary>
    /// <param name="tasks">The tasks to sum. Null counts as an empty fan-out.</param>
    /// <returns>The summary of <paramref name="tasks"/>.</returns>
    public static FanOutSummary Create(IEnumerable<FanOutTask>? tasks)
    {
        var summary = new FanOutSummary();
        if (tasks is null)
        {
            return summary;
        }

        DateTimeOffset? latestEstimate = null;
        long cost = 0;
        var priced = false;
        var total = 0;

        foreach (var task in tasks)
        {
            if (task is null)
            {
                continue;
            }

            total++;

            switch (task.Status)
            {
                case AgentActivityStatus.Queued:
                    summary = summary with { Queued = summary.Queued + 1 };
                    break;

                case AgentActivityStatus.Running:
                    summary = summary with { Running = summary.Running + 1 };
                    break;

                case AgentActivityStatus.WaitingForApproval:
                    summary = summary with { WaitingForApproval = summary.WaitingForApproval + 1 };
                    break;

                case AgentActivityStatus.Succeeded:
                    summary = summary with { Succeeded = summary.Succeeded + 1 };
                    break;

                case AgentActivityStatus.Failed:
                    summary = summary with { Failed = summary.Failed + 1 };
                    break;

                case AgentActivityStatus.Cancelled:
                    summary = summary with { Cancelled = summary.Cancelled + 1 };
                    break;

                default:
                    // An idle or unknown state is still work the run has not started: counting it as "finished" would
                    // claim progress nobody made and counting it as "running" would claim a worker nobody holds.
                    summary = summary with { Idle = summary.Idle + 1 };
                    break;
            }

            if (task.CostMicroUnits is { } taskCost)
            {
                priced = true;
                cost += taskCost;
            }

            if (task.IsActive && task.EstimatedEndAt is { } estimate
                && (latestEstimate is null || estimate > latestEstimate.Value))
            {
                latestEstimate = estimate;
            }
        }

        return summary with
        {
            Total = total,
            TotalCostMicroUnits = cost,
            HasCost = priced,
            EstimatedEndAt = latestEstimate,
        };
    }
}
