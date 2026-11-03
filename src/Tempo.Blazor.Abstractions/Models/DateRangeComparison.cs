namespace Tempo.Blazor.Models;

/// <summary>
/// How <c>TmDateRangePicker</c> derives the period its reported range is compared against.
/// </summary>
public enum DateRangeComparison
{
    /// <summary>No comparison at all — the default, and the pre-2.10 behaviour.</summary>
    None = 0,

    /// <summary>
    /// The window of the same length ending the day before the reported range starts
    /// (a 10–14 October range is compared against 5–9 October).
    /// </summary>
    PreviousPeriod = 1,

    /// <summary>
    /// The same window one year earlier. A leap day is clamped to the last day of the same month,
    /// so 29 February 2024 is compared against 28 February 2023.
    /// </summary>
    PreviousYear = 2,

    /// <summary>
    /// A second window the reader (or the host) picks explicitly, reported through
    /// <c>TmDateRangePicker.ComparisonRangeChanged</c>.
    /// </summary>
    Custom = 3,
}
