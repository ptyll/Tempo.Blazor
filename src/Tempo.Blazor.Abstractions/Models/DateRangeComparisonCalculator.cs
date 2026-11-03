namespace Tempo.Blazor.Models;

/// <summary>
/// The comparison-window arithmetic behind <see cref="DateRangeComparison.PreviousPeriod"/> and
/// <see cref="DateRangeComparison.PreviousYear"/>.
/// <para>
/// It is public so a backend can compute exactly the window the picker reports instead of
/// re-deriving it: a report header and the query it drives must agree to the day. Both members
/// accept a reversed range (a UI artefact) and normalize it, and never return an empty window.
/// </para>
/// </summary>
public static class DateRangeComparisonCalculator
{
    /// <summary>
    /// The equally long window that ends the day before <paramref name="start"/>: a 10–14 October
    /// range is compared against 5–9 October.
    /// </summary>
    /// <param name="start">One end of the reported range (inclusive).</param>
    /// <param name="end">The other end of the reported range (inclusive).</param>
    /// <returns>The previous window, ordered start to end.</returns>
    public static (DateOnly Start, DateOnly End) PreviousPeriod(DateOnly start, DateOnly end)
    {
        var (from, to) = Normalize(start, end);
        var length = to.DayNumber - from.DayNumber + 1;
        var previousEnd = from.AddDays(-1);
        return (previousEnd.AddDays(-(length - 1)), previousEnd);
    }

    /// <summary>
    /// The same window one year earlier. A leap day is clamped to the last day of the same month
    /// by the calendar, so 29 February 2024 is compared against 28 February 2023.
    /// </summary>
    /// <param name="start">One end of the reported range (inclusive).</param>
    /// <param name="end">The other end of the reported range (inclusive).</param>
    /// <returns>The previous year's window, ordered start to end.</returns>
    public static (DateOnly Start, DateOnly End) PreviousYear(DateOnly start, DateOnly end)
    {
        var (from, to) = Normalize(start, end);
        return (from.AddYears(-1), to.AddYears(-1));
    }

    private static (DateOnly Start, DateOnly End) Normalize(DateOnly start, DateOnly end)
        => start <= end ? (start, end) : (end, start);
}
