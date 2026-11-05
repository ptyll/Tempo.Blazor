namespace Tempo.Blazor.AI.Models;

/// <summary>
/// Which of several parallel attempts is the best one: the host's explicit pick when it names one, otherwise the
/// highest score that is actually a score.
/// <para>
/// The rule is one pure function so the column surface, its comparison table and a test can never disagree about who
/// won. It is deliberately conservative: an unscored attempt never wins on a comparison scale nobody set (its
/// <c>null</c> is "not scored", not "zero"), an attempt still running never wins over a finished one with the same
/// score, and a named attempt wins even when it has no score — the host may know something the numbers do not.
/// </para>
/// </summary>
public static class ParallelAttemptRanking
{
    /// <summary>
    /// Resolves the best attempt out of <paramref name="attempts"/>.
    /// </summary>
    /// <param name="attempts">The attempts to rank. Null or empty resolves to <c>null</c>.</param>
    /// <param name="bestAttemptId">
    /// The host's explicit pick. When it names one of <paramref name="attempts"/> that attempt wins outright, scored or
    /// not; when it names nothing, the highest score decides.
    /// </param>
    /// <returns>The best attempt, or <c>null</c> when nothing can be ranked.</returns>
    public static ParallelAttempt? Best(IReadOnlyList<ParallelAttempt>? attempts, string? bestAttemptId = null)
    {
        if (attempts is null || attempts.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(bestAttemptId))
        {
            foreach (var attempt in attempts)
            {
                if (attempt is not null && string.Equals(attempt.Id, bestAttemptId, StringComparison.Ordinal))
                {
                    return attempt;
                }
            }
        }

        ParallelAttempt? best = null;
        foreach (var attempt in attempts)
        {
            if (attempt is null || !attempt.HasScore)
            {
                continue;
            }

            if (best is null)
            {
                best = attempt;
                continue;
            }

            var score = attempt.Score!.Value;
            var bestScore = best.Score!.Value;

            // A tie keeps the earlier attempt, except that a SUCCEEDED attempt beats one that is not: the numbers are
            // equal, and the attempt with a result is the one a reader can actually use.
            if (score > bestScore
                || (score == bestScore
                    && attempt.Status == AgentActivityStatus.Succeeded
                    && best.Status != AgentActivityStatus.Succeeded))
            {
                best = attempt;
            }
        }

        return best;
    }

    /// <summary>
    /// Orders the attempts best first: the best attempt (see <see cref="Best"/>), then the rest in declaration order.
    /// The order is stable, so a surface that sorts by score never reshuffles equal attempts between renders.
    /// </summary>
    /// <param name="attempts">The attempts to order. Null or empty returns an empty list.</param>
    /// <param name="bestAttemptId">The host's explicit pick, as for <see cref="Best"/>.</param>
    /// <returns>The attempts with the best one first.</returns>
    public static IReadOnlyList<ParallelAttempt> Order(
        IReadOnlyList<ParallelAttempt>? attempts, string? bestAttemptId = null)
    {
        if (attempts is null || attempts.Count == 0)
        {
            return [];
        }

        var best = Best(attempts, bestAttemptId);
        if (best is null)
        {
            return [.. attempts];
        }

        var ordered = new List<ParallelAttempt>(attempts.Count) { best };
        foreach (var attempt in attempts)
        {
            if (attempt is not null && !ReferenceEquals(attempt, best))
            {
                ordered.Add(attempt);
            }
        }

        return ordered;
    }
}
