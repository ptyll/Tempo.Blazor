using System.Globalization;

namespace Tempo.Blazor.Components.Pickers;

/// <summary>
/// Internal helpers for calendar rendering and date arithmetic.
/// </summary>
internal static class DateTimeHelpers
{
    /// <summary>
    /// Returns <see cref="DateOnly"/> values that fill the calendar grid for the given year/month,
    /// starting on Monday (the historical contract; the culture's own first day is not consulted).
    /// The grid always contains six weeks so the calendar keeps a stable shape while navigating
    /// between months. Leading and trailing cells contain days from adjacent months.
    /// </summary>
    public static IReadOnlyList<DateOnly> GetCalendarDays(int year, int month)
        => GetCalendarDays(year, month, DayOfWeek.Monday);

    /// <summary>
    /// Returns <see cref="DateOnly"/> values that fill the calendar grid for the given year/month,
    /// starting on <paramref name="firstDayOfWeek"/>. The grid always contains six weeks so the
    /// calendar keeps a stable shape while navigating between months.
    /// Leading and trailing cells contain days from adjacent months.
    /// </summary>
    public static IReadOnlyList<DateOnly> GetCalendarDays(int year, int month, DayOfWeek firstDayOfWeek)
    {
        var first = new DateOnly(year, month, 1);
        var startOffset = ((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        var start = first.AddDays(-startOffset);
        const int count = 42;
        var days = new DateOnly[count];
        for (var i = 0; i < count; i++)
            days[i] = start.AddDays(i);

        return days;
    }

    /// <summary>Returns the full month name for the given month and culture.</summary>
    public static string GetMonthName(int month, CultureInfo culture)
        => culture.DateTimeFormat.GetMonthName(month);

    /// <summary>Returns the abbreviated month name for the given month and culture.</summary>
    public static string GetAbbreviatedMonthName(int month, CultureInfo culture)
        => culture.DateTimeFormat.GetAbbreviatedMonthName(month);

    /// <summary>
    /// Returns 7 abbreviated day-header strings starting on Monday. The culture supplies the
    /// names only; its <see cref="DateTimeFormatInfo.FirstDayOfWeek"/> is not consulted.
    /// </summary>
    public static IReadOnlyList<string> GetDayHeaders(CultureInfo culture)
        => GetDayHeaders(culture, DayOfWeek.Monday);

    /// <summary>
    /// Returns 7 abbreviated day-header strings starting on <paramref name="firstDayOfWeek"/>.
    /// </summary>
    public static IReadOnlyList<string> GetDayHeaders(CultureInfo culture, DayOfWeek firstDayOfWeek)
    {
        var abbr = culture.DateTimeFormat.AbbreviatedDayNames; // Sun=0 … Sat=6
        var result = new string[7];
        for (var i = 0; i < 7; i++)
            result[i] = abbr[((int)firstDayOfWeek + i) % 7];
        return result;
    }

    /// <summary>
    /// Full weekday names in the same order as <see cref="GetDayHeaders"/>. Header cells are
    /// generic, so the abbreviated text stays visible and the full name is the accessible name
    /// (an <c>abbr</c> title, or a screen-reader-only span).
    /// </summary>
    public static IReadOnlyList<string> GetDayHeaderNames(CultureInfo culture, DayOfWeek firstDayOfWeek)
    {
        var names = culture.DateTimeFormat.DayNames; // Sun=0 … Sat=6
        var result = new string[7];
        for (var i = 0; i < 7; i++)
            result[i] = names[((int)firstDayOfWeek + i) % 7];
        return result;
    }

    /// <summary>Returns the start of the week containing <paramref name="date"/> for the supplied culture.</summary>
    public static DateOnly GetStartOfWeek(DateOnly date, CultureInfo culture)
    {
        var firstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek;
        var offset = ((int)date.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        return date.AddDays(-offset);
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="date"/> is today.</summary>
    public static bool IsToday(DateOnly date)
        => date == DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Returns <see langword="true"/> if <paramref name="date"/> falls within an inclusive range.</summary>
    public static bool IsInRange(DateOnly date, DateOnly? start, DateOnly? end)
        => start.HasValue && end.HasValue && date >= start.Value && date <= end.Value;
}
