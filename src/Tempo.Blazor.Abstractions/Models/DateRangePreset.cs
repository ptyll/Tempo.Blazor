using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Models;

/// <summary>
/// A named date range shortcut for TmDateRangePicker and TmDateTimeRangePicker.
/// </summary>
/// <param name="Label">Display label shown in the preset list.</param>
/// <param name="Start">Inclusive start date of the preset range.</param>
/// <param name="End">Inclusive end date of the preset range.</param>
public record DateRangePreset(string Label, DateOnly Start, DateOnly End);

/// <summary>
/// Built-in date range presets.
/// <para>
/// The static members below are the LEGACY 2.9.x surface: they read the machine's
/// <see cref="DateTime.Today"/> and their labels are hardcoded English. They are kept unchanged
/// (and are deliberately NOT marked <c>[Obsolete]</c>) so existing consumers keep compiling with
/// identical behaviour. New code should call <see cref="Create(TimeProvider, ITmLocalizer)"/>,
/// which takes the date from the injected <see cref="TimeProvider"/> and every label from
/// <see cref="ITmLocalizer"/> — the factory <c>TmDateRangePicker</c> itself uses.
/// </para>
/// </summary>
public static class DateRangePresets
{
    /// <summary>Today only. Legacy member: reads the machine date and a hardcoded English label.</summary>
    public static DateRangePreset Today => TodayRange(DateOnly.FromDateTime(DateTime.Today), "Today");

    /// <summary>
    /// The previous calendar week (Mon–Sun). Legacy member: reads the machine date and a hardcoded
    /// English label.
    /// </summary>
    public static DateRangePreset LastWeek => LastWeekRange(DateOnly.FromDateTime(DateTime.Today), "Last week");

    /// <summary>
    /// The previous calendar month. Legacy member: reads the machine date and a hardcoded English
    /// label.
    /// </summary>
    public static DateRangePreset LastMonth => LastMonthRange(DateOnly.FromDateTime(DateTime.Today), "Last month");

    /// <summary>
    /// The previous calendar quarter. Legacy member: reads the machine date and a hardcoded English
    /// label.
    /// </summary>
    public static DateRangePreset LastQuarter =>
        LastQuarterRange(DateOnly.FromDateTime(DateTime.Today), "Last quarter");

    /// <summary>
    /// The current calendar year from January 1 to today. Legacy member: reads the machine date and
    /// a hardcoded English label.
    /// </summary>
    public static DateRangePreset ThisYear => ThisYearRange(DateOnly.FromDateTime(DateTime.Today), "This year");

    /// <summary>
    /// The built-in preset row (<c>Today</c>, <c>Last week</c>, <c>Last month</c>,
    /// <c>Last quarter</c>, <c>This year</c>) computed from <paramref name="timeProvider"/> and
    /// labelled through <paramref name="localizer"/>.
    /// <para>
    /// The moment comes from <see cref="TimeProvider.GetLocalNow"/>, so a host that registers a
    /// <c>FakeTimeProvider</c> (or a tenant clock) gets presets pinned to ITS day, not the
    /// server's. A label the localizer does not translate falls back to the English wording, so a
    /// partial translation table can never render a resource key into the preset row.
    /// </para>
    /// </summary>
    /// <param name="timeProvider">The clock that defines "today"; must not be null.</param>
    /// <param name="localizer">The localizer the labels come from; must not be null.</param>
    /// <returns>The five presets in their historical order.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static IReadOnlyList<DateRangePreset> Create(TimeProvider timeProvider, ITmLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(localizer);

        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        return
        [
            TodayRange(today, Label(localizer, "TmDateRangePicker_Preset_Today", "Today")),
            LastWeekRange(today, Label(localizer, "TmDateRangePicker_Preset_LastWeek", "Last week")),
            LastMonthRange(today, Label(localizer, "TmDateRangePicker_Preset_LastMonth", "Last month")),
            LastQuarterRange(today, Label(localizer, "TmDateRangePicker_Preset_LastQuarter", "Last quarter")),
            ThisYearRange(today, Label(localizer, "TmDateRangePicker_Preset_ThisYear", "This year")),
        ];
    }

    private static string Label(ITmLocalizer localizer, string key, string fallback)
        => localizer.TryGetString(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static DateRangePreset TodayRange(DateOnly today, string label) => new(label, today, today);

    private static DateRangePreset LastWeekRange(DateOnly today, string label)
    {
        // Monday-based week: DayOfWeek.Sunday is 0, so shift it into a Monday-first index.
        var daysFromMonday = ((int)today.DayOfWeek + 6) % 7;
        var thisMonday = today.AddDays(-daysFromMonday);
        return new(label, thisMonday.AddDays(-7), thisMonday.AddDays(-1));
    }

    private static DateRangePreset LastMonthRange(DateOnly today, string label)
    {
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        return new(label, firstOfThisMonth.AddMonths(-1), firstOfThisMonth.AddDays(-1));
    }

    private static DateRangePreset LastQuarterRange(DateOnly today, string label)
    {
        var currentQuarter = (today.Month - 1) / 3 + 1;
        var startMonth = (currentQuarter - 2) * 3 + 1;
        if (startMonth < 1)
        {
            startMonth += 12;
        }

        var startYear = startMonth > today.Month ? today.Year - 1 : today.Year;
        var first = new DateOnly(startYear, startMonth, 1);
        return new(label, first, first.AddMonths(3).AddDays(-1));
    }

    private static DateRangePreset ThisYearRange(DateOnly today, string label) =>
        new(label, new DateOnly(today.Year, 1, 1), today);
}
