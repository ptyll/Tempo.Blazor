using System.Globalization;

namespace Tempo.Blazor.Components.Pickers;

/// <summary>
/// Internal helpers for calendar rendering and date arithmetic.
/// </summary>
internal static class DateTimeHelpers
{
    /// <summary>
    /// Returns exactly 42 <see cref="DateOnly"/> values that fill a 6×7 calendar grid
    /// for the given year/month, starting on Monday.
    /// Leading and trailing cells contain days from adjacent months.
    /// </summary>
    public static IReadOnlyList<DateOnly> GetCalendarDays(int year, int month)
    {
        var first   = new DateOnly(year, month, 1);
        // DayOfWeek: 0=Sun, 1=Mon, … 6=Sat
        // We want Mon=0 offset → (DayOfWeek + 6) % 7
        var offset  = ((int)first.DayOfWeek + 6) % 7;
        var start   = first.AddDays(-offset);

        var days = new DateOnly[42];
        for (var i = 0; i < 42; i++)
            days[i] = start.AddDays(i);

        return days;
    }

    /// <summary>Returns the full month name for the given month and culture.</summary>
    public static string GetMonthName(int month, CultureInfo culture)
        => culture.DateTimeFormat.GetMonthName(month);

    /// <summary>
    /// Returns 7 abbreviated day-header strings starting on Monday,
    /// honouring the culture's <see cref="DateTimeFormatInfo.FirstDayOfWeek"/>.
    /// </summary>
    public static IReadOnlyList<string> GetDayHeaders(CultureInfo culture)
    {
        var abbr = culture.DateTimeFormat.AbbreviatedDayNames; // Sun=0 … Sat=6
        // Reorder: start from Monday
        var result = new string[7];
        for (var i = 0; i < 7; i++)
        {
            // Mon=1 in DayOfWeek; map slot i → DayOfWeek (Mon=1,…,Sun=0)
            var dow = (i + 1) % 7; // 0=Mon→1, 1=Tue→2, … 6=Sun→0
            result[i] = abbr[dow];
        }
        return result;
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="date"/> is today.</summary>
    public static bool IsToday(DateOnly date)
        => date == DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Returns <see langword="true"/> if <paramref name="date"/> falls within an inclusive range.</summary>
    public static bool IsInRange(DateOnly date, DateOnly? start, DateOnly? end)
        => start.HasValue && end.HasValue && date >= start.Value && date <= end.Value;
}
