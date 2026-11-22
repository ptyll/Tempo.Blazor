namespace Tempo.Blazor.Models;

/// <summary>Which piece of a time-of-day value a stepper or typeahead is editing.</summary>
public enum TimeSegment
{
    /// <summary>The hour (0–23, or 1–12 in a 12-hour cycle).</summary>
    Hours = 0,

    /// <summary>The minute (0–59), wrapping inside the hour without carrying into hours.</summary>
    Minutes = 1,

    /// <summary>The second (0–59), wrapping inside the minute without carrying into minutes.</summary>
    Seconds = 2,

    /// <summary>AM/PM period; toggling adds or subtracts 12 hours.</summary>
    DayPeriod = 3,
}
