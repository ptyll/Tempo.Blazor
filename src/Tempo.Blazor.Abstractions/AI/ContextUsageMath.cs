namespace Tempo.Blazor.AI.Models;

/// <summary>
/// The arithmetic of a <see cref="ContextUsageSnapshot"/>: what a section contributes, what the whole context
/// measures, how the rounded shares stay honest, whether the budget is exceeded and which sections a compaction
/// can actually drop. Nothing here touches markup, so the five decisions that are easy to get wrong are testable
/// on their own — and a host computing the same numbers elsewhere cannot drift from the component.
/// </summary>
public static class ContextUsageMath
{
    /// <summary>
    /// How many tokens one section contributes. The host's <see cref="ContextUsageSection.Tokens"/> is
    /// authoritative; only when it reports nothing (zero or less) do the section's
    /// <see cref="ContextUsageSection.Items"/> add up to the measurement. Negative numbers count as nothing — a
    /// negative token count is a data defect, not a credit — and so does a <c>null</c> item.
    /// </summary>
    /// <param name="section">The section to measure; <c>null</c> measures nothing.</param>
    /// <returns>The section's tokens, never negative.</returns>
    public static int TokensOf(ContextUsageSection? section)
    {
        if (section is null)
        {
            return 0;
        }

        if (section.Tokens > 0)
        {
            return section.Tokens;
        }

        if (section.Items is not { Count: > 0 } items)
        {
            return 0;
        }

        var sum = 0L;
        foreach (var item in items)
        {
            if (item is not null && item.Tokens > 0)
            {
                sum += item.Tokens;
            }
        }

        return (int)Math.Min(sum, int.MaxValue);
    }

    /// <summary>The whole context window in use: the sum of every section's contribution.</summary>
    /// <param name="snapshot">The snapshot; <c>null</c> measures zero.</param>
    /// <returns>The used tokens, never negative.</returns>
    public static int UsedTokens(ContextUsageSnapshot? snapshot)
    {
        if (snapshot?.Sections is not { Count: > 0 } sections)
        {
            return 0;
        }

        var sum = 0L;
        foreach (var section in sections)
        {
            sum += TokensOf(section);
        }

        return (int)Math.Min(sum, int.MaxValue);
    }

    /// <summary>
    /// The used share of the budget as a whole percentage, rounded away from zero. It is deliberately NOT
    /// clamped: an over-budget context reads as 115 %, not as a rounded 100 % that hides the overflow.
    /// </summary>
    /// <param name="snapshot">The snapshot to measure.</param>
    /// <returns>The percentage, or <c>null</c> when the budget is unknown.</returns>
    public static int? BudgetPercent(ContextUsageSnapshot? snapshot)
        => snapshot is null || snapshot.BudgetTokens <= 0
            ? null
            : (int)Math.Round(
                UsedTokens(snapshot) * 100d / snapshot.BudgetTokens,
                MidpointRounding.AwayFromZero);

    /// <summary>
    /// Whether the context is over its budget. Being exactly AT the budget is full, not over — and an unknown
    /// budget (zero or less) can never be exceeded, so no threshold claims a verdict for a 0-of-0 context.
    /// </summary>
    /// <param name="snapshot">The snapshot to judge.</param>
    /// <returns><c>true</c> when the used tokens are strictly above a known budget.</returns>
    public static bool IsOverBudget(ContextUsageSnapshot? snapshot)
        => snapshot is not null && snapshot.BudgetTokens > 0 && UsedTokens(snapshot) > snapshot.BudgetTokens;

    /// <summary>
    /// The whole percentages of <paramref name="total"/> each entry contributes, rounded with the LARGEST-REMAINDER
    /// method so the returned shares add up to exactly 100 (or to the plain floors when the entries do not account
    /// for the whole). Rounding each entry on its own leaves 33 % / 33 % / 33 % for three equal parts, which reads
    /// as a missing percent.
    /// </summary>
    /// <param name="tokens">The entries, in display order. A negative entry shares nothing.</param>
    /// <param name="total">The whole the entries are a share of.</param>
    /// <returns>One whole percentage per entry; all zero when the total is zero.</returns>
    public static int[] Shares(IReadOnlyList<int> tokens, int total)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var shares = new int[tokens.Count];
        if (tokens.Count == 0 || total <= 0)
        {
            return shares;
        }

        var fractions = new double[tokens.Count];
        var assigned = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            var amount = tokens[index] > 0 ? tokens[index] : 0;
            var exact = amount * 100d / total;
            var floor = (int)Math.Floor(exact);
            shares[index] = floor;
            fractions[index] = exact - floor;
            assigned += floor;
        }

        var remaining = 100 - assigned;

        // The largest-remainder step assumes the entries ACCOUNT for the whole: then the leftover is smaller than
        // the number of rows and every point lands on a row. A host whose entries do not add up to the total (it
        // forgot a section) must not have the leftover spread over the rows it does report, which would inflate
        // them — such a table keeps the plain floors and the caller reports the unaccounted rest itself.
        if (remaining <= 0 || remaining >= tokens.Count)
        {
            return shares;
        }

        var order = new int[tokens.Count];
        for (var index = 0; index < order.Length; index++)
        {
            order[index] = index;
        }

        Array.Sort(order, (left, right) =>
        {
            var byFraction = fractions[right].CompareTo(fractions[left]);
            return byFraction != 0 ? byFraction : left.CompareTo(right);
        });

        for (var step = 0; step < remaining && step < order.Length; step++)
        {
            shares[order[step]]++;
        }

        return shares;
    }

    /// <summary>
    /// Whether a section can be dropped by a compaction: it must carry tokens, be OPTIONAL (not
    /// <see cref="ContextUsageSection.IsRequired"/>), and be neither already summarised nor already omitted.
    /// </summary>
    /// <param name="section">The section to judge; <c>null</c> is not compactable.</param>
    /// <returns><c>true</c> when a compaction would have something to do with this section.</returns>
    public static bool IsCompactable(ContextUsageSection? section)
        => section is not null
           && !section.IsRequired
           && !section.IsSummarized
           && !section.IsOmitted
           && TokensOf(section) > 0;

    /// <summary>Whether a compaction would change anything: at least one section is compactable.</summary>
    /// <param name="snapshot">The snapshot to judge; <c>null</c> is not compactable.</param>
    /// <returns><c>true</c> when any section reports something a compaction can drop.</returns>
    public static bool IsCompactable(ContextUsageSnapshot? snapshot)
        => snapshot?.Sections is { Count: > 0 } sections && sections.Any(IsCompactable);
}
