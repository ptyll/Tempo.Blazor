namespace Tempo.Blazor.Models;

/// <summary>
/// A named date range shortcut for TmDateRangePicker and TmDateTimeRangePicker.
/// </summary>
/// <param name="Label">Display label shown in the preset list.</param>
/// <param name="Start">Inclusive start date of the preset range.</param>
/// <param name="End">Inclusive end date of the preset range.</param>
public record DateRangePreset(string Label, DateOnly Start, DateOnly End);

/// <summary>
/// Built-in date range presets. Labels are intentionally in English;
/// use custom presets with localized labels if needed.
/// </summary>
public static class DateRangePresets
{
    /// <summary>Today only.</summary>
    public static DateRangePreset Today =>
        new("Today",
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today));

    /// <summary>The previous calendar week (Mon–Sun).</summary>
    public static DateRangePreset LastWeek
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            // Go back to last Monday
            var daysFromMonday = ((int)today.DayOfWeek + 6) % 7;
            var thisMonday = today.AddDays(-daysFromMonday);
            return new("Last week", thisMonday.AddDays(-7), thisMonday.AddDays(-1));
        }
    }

    /// <summary>The previous calendar month.</summary>
    public static DateRangePreset LastMonth
    {
        get
        {
            var today = DateTime.Today;
            var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
            var last  = new DateOnly(today.Year, today.Month, 1).AddDays(-1);
            return new("Last month", first, last);
        }
    }

    /// <summary>The previous calendar quarter.</summary>
    public static DateRangePreset LastQuarter
    {
        get
        {
            var today = DateTime.Today;
            var currentQuarter = (today.Month - 1) / 3 + 1;
            var startMonth = (currentQuarter - 2) * 3 + 1;
            if (startMonth < 1) startMonth += 12;
            var startYear = startMonth > today.Month ? today.Year - 1 : today.Year;
            var first = new DateOnly(startYear, startMonth, 1);
            var last  = first.AddMonths(3).AddDays(-1);
            return new("Last quarter", first, last);
        }
    }

    /// <summary>The current calendar year from January 1 to today.</summary>
    public static DateRangePreset ThisYear
    {
        get
        {
            var today = DateTime.Today;
            return new("This year",
                new DateOnly(today.Year, 1, 1),
                DateOnly.FromDateTime(today));
        }
    }
}
