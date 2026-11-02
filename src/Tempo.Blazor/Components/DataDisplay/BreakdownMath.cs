namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// One computed share of a <see cref="TmBreakdownCard"/>: the part, its non-negative value, its
/// rounded percentage and the fallback tone index of its position.
/// </summary>
/// <param name="Part">The part this share belongs to.</param>
/// <param name="Value">The part's value clamped to zero (a negative amount is not a share of a whole).</param>
/// <param name="Percent">The rounded percentage; the shares of one card add up to the target exactly.</param>
/// <param name="ToneIndex">The 1-based fallback tone slot (<c>--tone-1</c>..<c>--tone-6</c>), rotating by position.</param>
internal sealed record BreakdownShare(BreakdownPart Part, double Value, int Percent, int ToneIndex);

/// <summary>
/// The computed breakdown of one <see cref="TmBreakdownCard"/> render: the sorted shares, the total
/// the percentages were measured against, and the remainder a <see cref="TmBreakdownCard.Total"/>
/// override leaves unaccounted for.
/// </summary>
/// <param name="Shares">The shares, biggest first (a stable sort, so equal values keep the host's order).</param>
/// <param name="Total">The total the percentages use — the host's override when it can contain the parts, otherwise the parts' sum.</param>
/// <param name="RemainderValue">The amount between the parts' sum and <paramref name="Total"/>.</param>
/// <param name="RemainderPercent">The percentage of the remainder; zero when there is none.</param>
internal sealed record BreakdownResult(
    IReadOnlyList<BreakdownShare> Shares,
    double Total,
    double RemainderValue,
    int RemainderPercent)
{
    /// <summary>Whether the card renders an explicit remainder row and segment.</summary>
    internal bool HasRemainder => RemainderPercent > 0;
}

/// <summary>
/// The percentage maths of <see cref="TmBreakdownCard"/>. Rounded shares are computed with the
/// largest-remainder method, so they add up to the target exactly: a naive per-part rounding renders
/// three equal parts as 33 % + 33 % + 33 % = 99 %, which makes a breakdown card look broken.
/// </summary>
internal static class BreakdownMath
{
    /// <summary>
    /// Computes the shares of one breakdown: sorted biggest-first, clamped at zero, rounded so they
    /// sum to the target percentage.
    /// </summary>
    /// <param name="parts">The parts, in the host's order (ties keep it).</param>
    /// <param name="totalOverride">
    /// The host's declared total. It only wins when it is positive AND can contain the parts; a total
    /// BELOW the parts' sum cannot be the whole, so the parts' sum is used instead of rendering shares
    /// above 100 %.
    /// </param>
    internal static BreakdownResult Compute(IReadOnlyList<BreakdownPart>? parts, double? totalOverride)
    {
        var ordered = (parts ?? [])
            .Where(part => part is not null)
            .Select(part => new BreakdownShare(part, Math.Max(part.Value, 0), 0, 0))
            .OrderByDescending(share => share.Value)
            .ToList();

        if (ordered.Count == 0)
        {
            return new BreakdownResult([], 0, 0, 0);
        }

        var sum = ordered.Sum(share => share.Value);
        var total = totalOverride is { } declared && declared > 0 && declared >= sum ? declared : sum;

        if (total <= 0)
        {
            return new BreakdownResult(
                [.. ordered.Select((share, index) => share with { Percent = 0, ToneIndex = Tone(index) })],
                0,
                0,
                0);
        }

        // The target is the rounded share of the WHOLE the parts account for: 100 % when the total is
        // the parts' sum (the normal case), less when a bigger declared total leaves a remainder.
        var target = (int)Math.Round(sum / total * 100, MidpointRounding.AwayFromZero);
        target = Math.Clamp(target, 0, 100);

        var percents = LargestRemainder([.. ordered.Select(share => share.Value)], target);

        var shares = ordered
            .Select((share, index) => share with { Percent = percents[index], ToneIndex = Tone(index) })
            .ToList();

        return new BreakdownResult(
            shares,
            total,
            Math.Max(total - sum, 0),
            100 - target);
    }

    /// <summary>
    /// Splits <paramref name="target"/> whole percentage points across <paramref name="weights"/> so
    /// the result sums to the target exactly: floors first, then the leftover points go to the
    /// largest fractional parts, ties broken by position (so the result is deterministic).
    /// </summary>
    /// <param name="weights">The non-negative weights.</param>
    /// <param name="target">The percentage points to distribute (0..100).</param>
    internal static int[] LargestRemainder(IReadOnlyList<double> weights, int target)
    {
        var percents = new int[weights.Count];
        if (weights.Count == 0 || target <= 0)
        {
            return percents;
        }

        var sum = weights.Sum();
        if (sum <= 0)
        {
            return percents;
        }

        var fractions = new double[weights.Count];
        var assigned = 0;
        for (var index = 0; index < weights.Count; index++)
        {
            var exact = weights[index] / sum * target;
            percents[index] = (int)Math.Floor(exact);
            fractions[index] = exact - percents[index];
            assigned += percents[index];
        }

        var leftover = target - assigned;
        var order = Enumerable.Range(0, weights.Count)
            .OrderByDescending(index => fractions[index])
            .ThenBy(index => index)
            .ToList();

        for (var step = 0; step < leftover && order.Count > 0; step++)
        {
            percents[order[step % order.Count]]++;
        }

        return percents;
    }

    /// <summary>The 1-based fallback tone slot for a row's display position (six tones, then repeat).</summary>
    /// <param name="index">The zero-based display position.</param>
    internal static int Tone(int index) => index % 6 + 1;
}

/// <summary>
/// The FLIP (First, Last, Invert, Play) shift computation of <see cref="TmBreakdownCard"/>: when the
/// sorted order changes, every row that moved gets a shift in ROW UNITS from its new position back to
/// its previous one. The component renders the shifted state first and the unshifted, transitioned
/// state right after, so the rows slide into place without measuring anything in JavaScript.
/// </summary>
internal static class BreakdownFlip
{
    /// <summary>
    /// The shift of every row that exists in BOTH orders, in row units: positive means the row moved
    /// up and therefore starts that many rows BELOW its new place.
    /// <para>
    /// The keys must be UNIQUE per row (a repeated key keeps only the last index and produces a wrong
    /// shift), which is why the caller passes its computed row keys rather than the host's
    /// <see cref="BreakdownPart.Id"/> values.
    /// </para>
    /// </summary>
    /// <param name="previousOrder">The row keys in the order rendered last time.</param>
    /// <param name="currentOrder">The row keys in the order rendered now.</param>
    internal static Dictionary<string, int> Shifts(IReadOnlyList<string> previousOrder, IReadOnlyList<string> currentOrder)
    {
        var shifts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (previousOrder.Count == 0 || currentOrder.Count == 0)
        {
            return shifts;
        }

        var previousIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < previousOrder.Count; index++)
        {
            previousIndexes[previousOrder[index]] = index;
        }

        for (var index = 0; index < currentOrder.Count; index++)
        {
            if (previousIndexes.TryGetValue(currentOrder[index], out var previous))
            {
                shifts[currentOrder[index]] = previous - index;
            }
        }

        return shifts;
    }
}
