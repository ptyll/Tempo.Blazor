using System.Globalization;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The single place a scheduler range is decided. Every view loads, navigates and titles the range
/// this computes, so the three cannot drift apart.
/// </summary>
internal static class SchedulerRange
{
    /// <summary>
    /// The half-open range a view renders for <paramref name="current"/>. Month covers exactly the
    /// rendered six-week grid; Week starts on the configured first day; Agenda spans its day count
    /// from the current date; Day and Timeline are that single day.
    /// </summary>
    public static (DateTime Start, DateTime End) Visible(
        TmScheduleViewType view, DateTime current, SchedulerRangeOptions options)
    {
        var day = current.Date;
        return view switch
        {
            TmScheduleViewType.Week => Week(day, options.FirstDay),
            TmScheduleViewType.Month => Month(day, options.FirstDay),
            TmScheduleViewType.Agenda => (day, day.AddDays(options.AgendaLength)),
            _ => (day, day.AddDays(1))
        };
    }

    /// <summary>The seven dates of the week containing <paramref name="current"/>, Monday-first.</summary>
    public static IReadOnlyList<DateOnly> WeekDates(DateTime current) => WeekDates(current, 1);

    /// <summary>The seven dates of the week containing <paramref name="current"/>.</summary>
    public static IReadOnlyList<DateOnly> WeekDates(DateTime current, int firstDayOfWeek)
    {
        var (start, _) = Week(current.Date, NormalizeFirstDay(firstDayOfWeek));
        return Enumerable.Range(0, 7).Select(i => DateOnly.FromDateTime(start.AddDays(i))).ToList();
    }

    /// <summary>The 42 dates of the month grid containing <paramref name="current"/>, Monday-first.</summary>
    public static IReadOnlyList<DateOnly> MonthDays(DateTime current) => MonthDays(current, 1);

    /// <summary>The 42 dates of the month grid containing <paramref name="current"/>.</summary>
    public static IReadOnlyList<DateOnly> MonthDays(DateTime current, int firstDayOfWeek)
        => DateTimeHelpers.GetCalendarDays(current.Year, current.Month, (DayOfWeek)NormalizeFirstDay(firstDayOfWeek));

    /// <summary>
    /// The date one navigation step away. <paramref name="direction"/> is negative for previous.
    /// Agenda steps by its normalized day count, Week by seven days, Month by a calendar month and
    /// Day and Timeline by one day.
    /// </summary>
    public static DateTime Step(TmScheduleViewType view, DateTime current, int direction, SchedulerRangeOptions options)
    {
        var sign = direction < 0 ? -1 : 1;
        return view switch
        {
            TmScheduleViewType.Week => current.AddDays(7 * sign),
            TmScheduleViewType.Month => current.AddMonths(sign),
            TmScheduleViewType.Agenda => current.AddDays(options.AgendaLength * sign),
            _ => current.AddDays(sign)
        };
    }

    /// <summary>Maps any integer onto a day-of-week index 0..6, negative inputs included.</summary>
    public static int NormalizeFirstDay(int firstDayOfWeek) => ((firstDayOfWeek % 7) + 7) % 7;

    /// <summary>An agenda spans at least one day.</summary>
    public static int NormalizeAgendaDays(int agendaDays) => Math.Max(1, agendaDays);

    /// <summary>
    /// Title of an inclusive range. A range inside one month compacts to a single month name (the
    /// genitive form where the culture has one); a range inside one year uses the culture's
    /// month-day pattern on both ends; a range crossing a year uses the short date on both ends.
    /// </summary>
    public static string FormatRange(DateTime start, DateTime endInclusive, CultureInfo culture)
    {
        start = start.Date;
        endInclusive = endInclusive.Date;
        if (start > endInclusive)
        {
            (start, endInclusive) = (endInclusive, start);
        }

        if (start == endInclusive)
        {
            return FormatSingleDate(start, culture);
        }

        if (start.Year != endInclusive.Year)
        {
            return $"{start.ToString("d", culture)} – {endInclusive.ToString("d", culture)}";
        }

        if (start.Month != endInclusive.Month)
        {
            var monthDayPattern = culture.DateTimeFormat.MonthDayPattern;
            if (!UsableMonthDayPattern(monthDayPattern))
            {
                return $"{start.ToString("d", culture)} – {endInclusive.ToString("d", culture)}";
            }

            var left = start.ToString(monthDayPattern, culture);
            var right = endInclusive.ToString(monthDayPattern, culture);
            // Month before day (en) takes a comma before the year: "May 15 – June 13, 2025".
            var separator = monthDayPattern.Trim().StartsWith("M", StringComparison.Ordinal) ? ", " : " ";
            return $"{left} – {right}{separator}{endInclusive.Year}";
        }

        var monthDay = culture.DateTimeFormat.MonthDayPattern.Trim();
        if (monthDay.StartsWith("M", StringComparison.Ordinal))
        {
            // Month before day (en): "June 15–21, 2026".
            return $"{culture.DateTimeFormat.GetMonthName(start.Month)} {start.Day}–{endInclusive.Day}, {start.Year}";
        }

        var month = culture.DateTimeFormat.MonthGenitiveNames[start.Month - 1];
        if (monthDay.Contains("d.", StringComparison.Ordinal))
        {
            // Day with an ordinal dot (cs, sk): "15.–21. června 2026", genitive month name.
            return $"{start.Day}.–{endInclusive.Day}. {month} {start.Year}";
        }

        // Day before month without a dot (fr): "15–21 juin 2026".
        return $"{start.Day}–{endInclusive.Day} {month} {start.Year}";
    }

    /// <summary>One day, without the doubled day a range would show: "15. června 2025".</summary>
    private static string FormatSingleDate(DateTime day, CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.MonthDayPattern.Trim();
        if (!UsableMonthDayPattern(pattern))
        {
            return day.ToString("d", culture);
        }

        var month = pattern.Contains("d.", StringComparison.Ordinal) || !pattern.StartsWith("M", StringComparison.Ordinal)
            ? culture.DateTimeFormat.MonthGenitiveNames[day.Month - 1]
            : culture.DateTimeFormat.GetMonthName(day.Month);

        if (pattern.StartsWith("M", StringComparison.Ordinal))
        {
            return $"{month} {day.Day}, {day.Year}";
        }

        return pattern.Contains("d.", StringComparison.Ordinal)
            ? $"{day.Day}. {month} {day.Year}"
            : $"{day.Day} {month} {day.Year}";
    }

    /// <summary>A month-day pattern the compact title can read; anything else falls back to the short date.</summary>
    private static bool UsableMonthDayPattern(string pattern)
        => pattern.Contains('M') && pattern.Contains('d');

    /// <summary>
    /// A column header in the culture's own order: the abbreviated weekday and the short date with
    /// its year, and the separator next to that year, removed. ("Tue", "5/12") in en-US,
    /// ("Út", "12.05") in cs-CZ. The weekday's first letter is uppercased.
    /// </summary>
    public static (string Weekday, string Date) FormatColumnHeader(DateOnly date, CultureInfo culture)
    {
        var day = date.ToDateTime(TimeOnly.MinValue);
        var weekday = day.ToString("ddd", culture).Trim();
        if (weekday.Length > 0)
            weekday = culture.TextInfo.ToUpper(weekday[0]) + weekday[1..];
        var pattern = culture.DateTimeFormat.ShortDatePattern;
        var stripped = System.Text.RegularExpressions.Regex.Replace(pattern, @"y+\.?", "").Trim(' ', '.', '/', '-');
        if (!stripped.Contains('M') || !stripped.Contains('d'))
            stripped = UsableMonthDayPattern(culture.DateTimeFormat.MonthDayPattern)
                ? culture.DateTimeFormat.MonthDayPattern : "M/d";
        return (weekday, day.ToString(stripped, culture).Trim());
    }

    /// <summary>The long date with its first letter capitalized, for the Day and Timeline titles.</summary>
    public static string FormatDayTitle(DateTime day, CultureInfo culture)
    {
        var text = day.ToString(culture.DateTimeFormat.LongDatePattern, culture);
        return text.Length == 0 ? text : culture.TextInfo.ToUpper(text[0]) + text[1..];
    }

    /// <summary>The half-open week containing <paramref name="day"/>, already normalized.</summary>
    private static (DateTime Start, DateTime End) Week(DateTime day, int firstDay)
    {
        var offset = ((int)day.DayOfWeek - firstDay + 7) % 7;
        var start = day.AddDays(-offset);
        return (start, start.AddDays(7));
    }

    /// <summary>The half-open month grid containing <paramref name="day"/>, already normalized.</summary>
    private static (DateTime Start, DateTime End) Month(DateTime day, int firstDay)
    {
        var days = MonthDays(day, firstDay);
        return (days[0].ToDateTime(TimeOnly.MinValue), days[^1].ToDateTime(TimeOnly.MinValue).AddDays(1));
    }
}

/// <summary>
/// The two knobs a range depends on, normalized once so every consumer sees the same values.
/// </summary>
internal readonly record struct SchedulerRangeOptions(int FirstDayOfWeek, int AgendaDays)
{
    /// <summary>The week start folded into 0..6.</summary>
    public int FirstDay => SchedulerRange.NormalizeFirstDay(FirstDayOfWeek);

    /// <summary>The agenda length clamped to at least one day.</summary>
    public int AgendaLength => SchedulerRange.NormalizeAgendaDays(AgendaDays);
}
