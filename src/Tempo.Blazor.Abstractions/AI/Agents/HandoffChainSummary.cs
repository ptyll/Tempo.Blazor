namespace Tempo.Blazor.AI.Models;

/// <summary>
/// The roll-up of a whole handoff chain: how many steps there were and how many were settled, how much travelled, how
/// much was lost, which step lost the most and which pieces of information were dropped anywhere in the chain.
/// <para>
/// The record is derived, never stored by a host: <see cref="Create"/> folds the same <see cref="HandoffStep"/> list the
/// surface renders, so the summary line and the per-step lists can never disagree. Every member is arithmetic over
/// that list — no wording, no colour, no culture — because the words belong to the localizer and the tones to the
/// stylesheet.
/// </para>
/// </summary>
public sealed record HandoffChainSummary
{
    /// <summary>The roll-up of an empty chain: no steps, nothing passed and nothing lost.</summary>
    public static HandoffChainSummary Empty { get; } = new();

    /// <summary>How many steps the chain has.</summary>
    public int StepCount { get; init; }

    /// <summary>How many steps are still waiting for the receiving agent.</summary>
    public int PendingCount { get; init; }

    /// <summary>How many steps the receiving agent accepted.</summary>
    public int AcceptedCount { get; init; }

    /// <summary>How many steps the receiving agent refused.</summary>
    public int RejectedCount { get; init; }

    /// <summary>How many steps the receiving agent completed.</summary>
    public int CompletedCount { get; init; }

    /// <summary>How many items travelled across the whole chain (the sum over every step).</summary>
    public int PassedItems { get; init; }

    /// <summary>How many items were lost across the whole chain (the sum over every step).</summary>
    public int LostItems { get; init; }

    /// <summary>
    /// The identity of the step that lost the most items — the FIRST such step when several tie — or <c>null</c> when
    /// nothing was lost at all. A surface uses it to point at the damage; the step's own list carries the detail.
    /// </summary>
    public string? WorstStepId { get; init; }

    /// <summary>
    /// The labels of everything lost anywhere in the chain, de-duplicated case-insensitively and kept in the order the
    /// steps declared them — "what the run actually dropped", as opposed to how many pieces.
    /// </summary>
    public IReadOnlyList<string> LostLabels { get; init; } = [];

    /// <summary>Whether any step lost anything.</summary>
    public bool HasLosses => LostItems > 0;

    /// <summary>Whether the chain has steps at all.</summary>
    public bool HasSteps => StepCount > 0;

    /// <summary>How many steps were answered, whether accepted or completed.</summary>
    public int SettledCount => AcceptedCount + CompletedCount;

    /// <summary>Whether at least one step still waits for the receiving agent.</summary>
    public bool HasPending => PendingCount > 0;

    /// <summary>Whether the chain is finished: it has steps and every one of them is completed.</summary>
    public bool IsComplete => StepCount > 0 && CompletedCount == StepCount;

    /// <summary>How many items the chain tried to hand over (passed plus lost).</summary>
    public int ItemCount => PassedItems + LostItems;

    /// <summary>
    /// The share of the carried items that was lost, from 0 to 1, or 0 when the chain carried nothing (a loss rate
    /// needs a whole to be a rate of).
    /// </summary>
    public double LossRate => ItemCount == 0 ? 0d : LostItems / (double)ItemCount;

    /// <summary>
    /// Folds a chain into its roll-up. Nulls inside the list are skipped, so a host that built the list from an
    /// optional query still gets a summary instead of a crash.
    /// </summary>
    /// <param name="steps">The steps, in chain order. Null or empty returns <see cref="Empty"/>.</param>
    /// <returns>The summary of <paramref name="steps"/>.</returns>
    public static HandoffChainSummary Create(IReadOnlyList<HandoffStep>? steps)
    {
        if (steps is null || steps.Count == 0)
        {
            return Empty;
        }

        var stepCount = 0;
        var pending = 0;
        var accepted = 0;
        var rejected = 0;
        var completed = 0;
        var passed = 0;
        var lost = 0;
        string? worstStepId = null;
        var worstLost = 0;
        var labels = new List<string>();
        var seenLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps)
        {
            if (step is null)
            {
                continue;
            }

            stepCount++;
            switch (step.Status)
            {
                case HandoffStatus.Accepted:
                    accepted++;
                    break;
                case HandoffStatus.Rejected:
                    rejected++;
                    break;
                case HandoffStatus.Completed:
                    completed++;
                    break;
                default:
                    pending++;
                    break;
            }

            passed += step.PassedCount;
            lost += step.LostCount;

            if (step.LostCount > worstLost)
            {
                worstLost = step.LostCount;
                worstStepId = step.Id;
            }

            foreach (var label in step.LostLabels)
            {
                if (seenLabels.Add(label))
                {
                    labels.Add(label);
                }
            }
        }

        return new HandoffChainSummary
        {
            StepCount = stepCount,
            PendingCount = pending,
            AcceptedCount = accepted,
            RejectedCount = rejected,
            CompletedCount = completed,
            PassedItems = passed,
            LostItems = lost,
            WorstStepId = worstStepId,
            LostLabels = labels,
        };
    }
}
