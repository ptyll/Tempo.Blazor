namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// The arithmetic of <see cref="TmUsageMeter"/>: nothing here touches markup, so the two decisions
/// that are easy to get wrong — how a use counts and how a share rounds — are testable on their own.
/// </summary>
internal static class UsageMeterMath
{
    /// <summary>
    /// The amount a meter measures: a negative consumption is a data defect, so it counts as zero. The
    /// measurement is only defined against a POSITIVE limit — with an unknown limit (<c>0</c> or less)
    /// nothing is measurable and the result is zero rather than a division by zero.
    /// </summary>
    /// <param name="used">The host's used amount.</param>
    /// <param name="limit">The host's limit.</param>
    /// <returns>The used amount as the meter measures it.</returns>
    internal static double EffectiveUsed(double used, double limit)
        => limit > 0 && used > 0 ? used : 0;

    /// <summary>The ratio of the measured amount to the limit, clamped to the <c>0..1</c> band.</summary>
    /// <param name="used">The measured used amount (see <see cref="EffectiveUsed"/>).</param>
    /// <param name="limit">The host's limit.</param>
    /// <returns>The used share of the limit, between 0 and 1.</returns>
    internal static double Ratio(double used, double limit)
        => limit > 0 ? Math.Clamp(used / limit, 0, 1) : 0;

    /// <summary>
    /// The whole percentage every amount contributes to <paramref name="total"/>, rounded with the
    /// LARGEST-REMAINDER method so the returned shares always add up to exactly 100 (or to 0 when the
    /// total is zero). Rounding each row on its own leaves a table of 33 %, 33 %, 33 % for three equal
    /// parts, which reads as a missing percent.
    /// </summary>
    /// <param name="amounts">The amounts, in display order; a negative amount counts as zero.</param>
    /// <param name="total">The whole they are a share of.</param>
    /// <returns>One whole percentage per amount, summing to 100 when the total is positive.</returns>
    internal static int[] Shares(IReadOnlyList<double> amounts, double total)
    {
        var shares = new int[amounts.Count];
        if (amounts.Count == 0)
        {
            return shares;
        }

        if (!(total > 0) || double.IsNaN(total) || double.IsInfinity(total))
        {
            return shares;
        }

        var fractions = new double[amounts.Count];
        var assigned = 0;
        for (var index = 0; index < amounts.Count; index++)
        {
            var amount = amounts[index] > 0 ? amounts[index] : 0;
            var exact = amount / total * 100;
            var floor = (int)Math.Floor(exact);
            shares[index] = floor;
            fractions[index] = exact - floor;
            assigned += floor;
        }

        var remaining = 100 - assigned;

        // The largest-remainder step assumes the amounts ACCOUNT for the whole: then the leftover is
        // smaller than the number of rows and every point falls on a row. A host whose amounts do not
        // add up to the total (it forgot a category) would otherwise have the leftover spread over the
        // rows it does supply, inflating each of them; such a table keeps the plain floors and the
        // caller reports the unaccounted rest itself.
        if (remaining <= 0 || remaining >= amounts.Count)
        {
            return shares;
        }

        // Largest remainder first; the index breaks a tie, so the result is deterministic.
        var order = Enumerable.Range(0, amounts.Count)
            .OrderByDescending(index => fractions[index])
            .ThenBy(index => index)
            .ToArray();

        for (var step = 0; step < remaining && step < order.Length; step++)
        {
            shares[order[step]]++;
        }

        return shares;
    }
}
