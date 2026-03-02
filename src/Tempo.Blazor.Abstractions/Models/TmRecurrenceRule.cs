namespace Tempo.Blazor.Models;

/// <summary>
/// Frequency of recurrence for scheduled events.
/// </summary>
public enum TmRecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly
}

/// <summary>
/// Represents a recurrence rule for scheduled events (subset of RFC 5545 RRULE).
/// </summary>
public class TmRecurrenceRule
{
    /// <summary>Frequency of recurrence.</summary>
    public TmRecurrenceFrequency Frequency { get; set; }

    /// <summary>Interval between occurrences (e.g., every 2 weeks).</summary>
    public int Interval { get; set; } = 1;

    /// <summary>Maximum number of occurrences. Null = unlimited.</summary>
    public int? Count { get; set; }

    /// <summary>End date for recurrence. Null = unlimited (or limited by Count).</summary>
    public DateTime? Until { get; set; }

    /// <summary>Days of the week for WEEKLY frequency.</summary>
    public DayOfWeek[]? ByDay { get; set; }

    /// <summary>Days of the month for MONTHLY frequency (1–31).</summary>
    public int[]? ByMonthDay { get; set; }

    /// <summary>Months for YEARLY frequency (1–12).</summary>
    public int[]? ByMonth { get; set; }
}
