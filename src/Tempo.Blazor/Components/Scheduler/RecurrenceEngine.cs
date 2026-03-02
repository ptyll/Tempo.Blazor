using System.Globalization;
using System.Text;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// Engine for parsing, serializing, and expanding recurrence rules (subset of RFC 5545 RRULE).
/// </summary>
internal static class RecurrenceEngine
{
    private const int MaxExpansions = 1000;

    private static readonly Dictionary<string, DayOfWeek> DayMap = new()
    {
        ["MO"] = DayOfWeek.Monday,
        ["TU"] = DayOfWeek.Tuesday,
        ["WE"] = DayOfWeek.Wednesday,
        ["TH"] = DayOfWeek.Thursday,
        ["FR"] = DayOfWeek.Friday,
        ["SA"] = DayOfWeek.Saturday,
        ["SU"] = DayOfWeek.Sunday,
    };

    private static readonly Dictionary<DayOfWeek, string> ReverseDayMap =
        DayMap.ToDictionary(kv => kv.Value, kv => kv.Key);

    /// <summary>
    /// Parse an RRULE string into a TmRecurrenceRule object.
    /// </summary>
    public static TmRecurrenceRule? Parse(string? rrule)
    {
        if (string.IsNullOrWhiteSpace(rrule)) return null;

        var rule = new TmRecurrenceRule();
        var parts = rrule.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var eqIndex = part.IndexOf('=');
            if (eqIndex < 0) continue;

            var key = part[..eqIndex].Trim().ToUpperInvariant();
            var value = part[(eqIndex + 1)..].Trim();

            switch (key)
            {
                case "FREQ":
                    rule.Frequency = value.ToUpperInvariant() switch
                    {
                        "DAILY" => TmRecurrenceFrequency.Daily,
                        "WEEKLY" => TmRecurrenceFrequency.Weekly,
                        "MONTHLY" => TmRecurrenceFrequency.Monthly,
                        "YEARLY" => TmRecurrenceFrequency.Yearly,
                        _ => TmRecurrenceFrequency.Daily
                    };
                    break;

                case "INTERVAL":
                    if (int.TryParse(value, out var interval))
                        rule.Interval = Math.Max(1, interval);
                    break;

                case "COUNT":
                    if (int.TryParse(value, out var count))
                        rule.Count = count;
                    break;

                case "UNTIL":
                    if (DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss'Z'",
                            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var until))
                        rule.Until = until.ToUniversalTime();
                    break;

                case "BYDAY":
                    rule.ByDay = value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Where(d => DayMap.ContainsKey(d.Trim().ToUpperInvariant()))
                        .Select(d => DayMap[d.Trim().ToUpperInvariant()])
                        .ToArray();
                    break;

                case "BYMONTHDAY":
                    rule.ByMonthDay = value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => int.TryParse(d.Trim(), out var day) ? day : 0)
                        .Where(d => d >= 1 && d <= 31)
                        .ToArray();
                    break;

                case "BYMONTH":
                    rule.ByMonth = value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(m => int.TryParse(m.Trim(), out var month) ? month : 0)
                        .Where(m => m >= 1 && m <= 12)
                        .ToArray();
                    break;
            }
        }

        return rule;
    }

    /// <summary>
    /// Serialize a TmRecurrenceRule into an RRULE string.
    /// </summary>
    public static string Serialize(TmRecurrenceRule rule)
    {
        var sb = new StringBuilder();

        sb.Append("FREQ=");
        sb.Append(rule.Frequency switch
        {
            TmRecurrenceFrequency.Daily => "DAILY",
            TmRecurrenceFrequency.Weekly => "WEEKLY",
            TmRecurrenceFrequency.Monthly => "MONTHLY",
            TmRecurrenceFrequency.Yearly => "YEARLY",
            _ => "DAILY"
        });

        sb.Append(";INTERVAL=");
        sb.Append(Math.Max(1, rule.Interval));

        if (rule.Count.HasValue)
        {
            sb.Append(";COUNT=");
            sb.Append(rule.Count.Value);
        }

        if (rule.Until.HasValue)
        {
            sb.Append(";UNTIL=");
            sb.Append(rule.Until.Value.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
        }

        if (rule.ByDay is { Length: > 0 })
        {
            sb.Append(";BYDAY=");
            sb.Append(string.Join(",", rule.ByDay.Select(d => ReverseDayMap[d])));
        }

        if (rule.ByMonthDay is { Length: > 0 })
        {
            sb.Append(";BYMONTHDAY=");
            sb.Append(string.Join(",", rule.ByMonthDay));
        }

        if (rule.ByMonth is { Length: > 0 })
        {
            sb.Append(";BYMONTH=");
            sb.Append(string.Join(",", rule.ByMonth));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Expand a recurring event into individual occurrence instances within the given range.
    /// </summary>
    public static IReadOnlyList<TmScheduleEvent> ExpandRecurrence(
        TmScheduleEvent source, DateTime rangeStart, DateTime rangeEnd)
    {
        if (string.IsNullOrWhiteSpace(source.RecurrenceRule))
            return [];

        var rule = Parse(source.RecurrenceRule);
        if (rule is null) return [];

        var duration = source.End - source.Start;
        var exceptions = source.RecurrenceExceptions?
            .Select(d => d.Date)
            .ToHashSet() ?? [];

        var occurrences = new List<TmScheduleEvent>();
        var count = 0;
        var current = source.Start;

        while (current < rangeEnd && count < MaxExpansions)
        {
            if (rule.Count.HasValue && count >= rule.Count.Value)
                break;

            if (rule.Until.HasValue && current.Date > rule.Until.Value.Date)
                break;

            var candidates = GetCandidatesForDate(current, rule);

            foreach (var candidate in candidates)
            {
                if (candidate >= rangeEnd) break;
                if (rule.Count.HasValue && occurrences.Count >= rule.Count.Value) break;
                if (rule.Until.HasValue && candidate.Date > rule.Until.Value.Date) break;

                if (candidate >= rangeStart && !exceptions.Contains(candidate.Date))
                {
                    occurrences.Add(CreateOccurrence(source, candidate, duration));
                }

                count++;
            }

            current = Advance(current, rule);
        }

        return occurrences;
    }

    private static List<DateTime> GetCandidatesForDate(DateTime current, TmRecurrenceRule rule)
    {
        var candidates = new List<DateTime>();

        switch (rule.Frequency)
        {
            case TmRecurrenceFrequency.Daily:
                candidates.Add(current);
                break;

            case TmRecurrenceFrequency.Weekly:
                if (rule.ByDay is { Length: > 0 })
                {
                    // Find the week start (Monday) for the current date
                    var weekStart = current.Date.AddDays(-((int)current.DayOfWeek + 6) % 7);
                    foreach (var day in rule.ByDay.OrderBy(d => ((int)d + 6) % 7))
                    {
                        var offset = ((int)day + 6) % 7;
                        var candidate = weekStart.AddDays(offset)
                            .Add(current.TimeOfDay);
                        if (candidate >= current.Date)
                            candidates.Add(candidate);
                    }
                }
                else
                {
                    candidates.Add(current);
                }
                break;

            case TmRecurrenceFrequency.Monthly:
                if (rule.ByMonthDay is { Length: > 0 })
                {
                    foreach (var day in rule.ByMonthDay.OrderBy(d => d))
                    {
                        var daysInMonth = DateTime.DaysInMonth(current.Year, current.Month);
                        if (day <= daysInMonth)
                        {
                            candidates.Add(new DateTime(current.Year, current.Month, day,
                                current.Hour, current.Minute, current.Second));
                        }
                    }
                }
                else
                {
                    var dayInMonth = Math.Min(current.Day, DateTime.DaysInMonth(current.Year, current.Month));
                    candidates.Add(new DateTime(current.Year, current.Month, dayInMonth,
                        current.Hour, current.Minute, current.Second));
                }
                break;

            case TmRecurrenceFrequency.Yearly:
                if (rule.ByMonth is { Length: > 0 })
                {
                    foreach (var month in rule.ByMonth.OrderBy(m => m))
                    {
                        var dayInMonth = Math.Min(current.Day, DateTime.DaysInMonth(current.Year, month));
                        candidates.Add(new DateTime(current.Year, month, dayInMonth,
                            current.Hour, current.Minute, current.Second));
                    }
                }
                else
                {
                    candidates.Add(current);
                }
                break;
        }

        return candidates;
    }

    private static DateTime Advance(DateTime current, TmRecurrenceRule rule)
    {
        return rule.Frequency switch
        {
            TmRecurrenceFrequency.Daily => current.AddDays(rule.Interval),
            TmRecurrenceFrequency.Weekly => current.AddDays(7 * rule.Interval),
            TmRecurrenceFrequency.Monthly => current.AddMonths(rule.Interval),
            TmRecurrenceFrequency.Yearly => current.AddYears(rule.Interval),
            _ => current.AddDays(1)
        };
    }

    private static TmScheduleEvent CreateOccurrence(TmScheduleEvent source, DateTime start, TimeSpan duration)
    {
        return new TmScheduleEvent
        {
            Id = $"{source.Id}_{start:yyyyMMdd}",
            Title = source.Title,
            Description = source.Description,
            Start = start,
            End = start + duration,
            AllDay = source.AllDay,
            Color = source.Color,
            CssClass = source.CssClass,
            ResourceId = source.ResourceId,
            RecurrenceRule = source.RecurrenceRule,
            IsReadOnly = source.IsReadOnly,
            Metadata = source.Metadata,
        };
    }
}
