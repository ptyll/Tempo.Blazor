using System.Globalization;
using System.Text.RegularExpressions;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Pickers;

/// <summary>
/// Pure time-of-day helpers for <c>TmTimeSelector</c>: hour cycle, parse, independent segment
/// stepping (minutes never carry into hours), snap, overnight range and DST gap/ambiguity.
/// Internal; the forthcoming selector is the public surface.
/// </summary>
internal static class TimeOfDayFormat
{
    private static readonly Regex PeriodSuffix = new(
        @"\s*(?<period>a\.?m\.?|p\.?m\.?|[ap])\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Resolves <see cref="TimeHourCycle.Auto"/> from <paramref name="culture"/>'s short-time
    /// pattern: a 12-hour clock exactly when the pattern contains <c>h</c> (not <c>H</c>).
    /// A forced <see cref="TimeHourCycle.H12"/> / <see cref="TimeHourCycle.H23"/> is returned as-is.
    /// </summary>
    public static TimeHourCycle ResolveHourCycle(TimeHourCycle hourCycle, CultureInfo? culture = null)
    {
        if (hourCycle != TimeHourCycle.Auto)
        {
            return hourCycle;
        }

        culture ??= CultureInfo.CurrentCulture;
        return PatternUses12HourClock(culture.DateTimeFormat.ShortTimePattern)
            ? TimeHourCycle.H12
            : TimeHourCycle.H23;
    }

    /// <summary>
    /// Formats <paramref name="value"/> with the culture's short-time pattern, forced onto
    /// <paramref name="hourCycle"/> when it is not <see cref="TimeHourCycle.Auto"/>.
    /// </summary>
    public static string Format(TimeOnly value, TimeHourCycle hourCycle, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var cycle = ResolveHourCycle(hourCycle, culture);
        var pattern = AdjustPattern(culture.DateTimeFormat.ShortTimePattern, cycle, culture);
        return value.ToString(pattern, culture);
    }

    /// <summary>
    /// Parses a typed or pasted time. Accepts <c>1430</c>, <c>14:30</c>, <c>14.30</c>,
    /// <c>14h30</c>, <c>2:30 PM</c>, <c>2:30p</c>, <c>12:00 AM</c> → 00:00. Rejects empty input,
    /// <c>24:00</c> and seconds when <paramref name="showSeconds"/> is <see langword="false"/>.
    /// </summary>
    public static bool TryParse(string? input, CultureInfo? culture, bool showSeconds, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        culture ??= CultureInfo.CurrentCulture;
        var trimmed = input.Trim();
        var period = DetectPeriod(ref trimmed, culture);
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!TryReadClock(trimmed, showSeconds, out var hour, out var minute, out var second))
        {
            return false;
        }

        if (period is not null)
        {
            if (hour is < 1 or > 12)
            {
                return false;
            }

            hour = ApplyPeriod(hour, period.Value);
        }

        if (hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59)
        {
            return false;
        }

        time = new TimeOnly(hour, minute, second);
        return true;
    }

    /// <summary>
    /// Steps one segment. Minutes wrap inside the hour and seconds inside the minute — they never
    /// carry (ADR-0007 decision 6). Hours wrap the 24-hour day. A candidate outside
    /// <paramref name="min"/>/<paramref name="max"/> is rejected and <paramref name="value"/> is
    /// returned unchanged. An off-step minute snaps in the direction of <paramref name="delta"/>
    /// before applying a full step.
    /// </summary>
    public static TimeOnly Step(
        TimeOnly value,
        TimeSegment segment,
        int delta,
        int minuteStep,
        TimeOnly? min,
        TimeOnly? max,
        bool overnight)
    {
        if (delta == 0)
        {
            return value;
        }

        if (minuteStep < 1)
        {
            minuteStep = 1;
        }

        var candidate = segment switch
        {
            TimeSegment.Hours => value.AddHours(delta),
            TimeSegment.Minutes => StepMinutes(value, delta, minuteStep),
            TimeSegment.Seconds => StepSeconds(value, delta),
            TimeSegment.DayPeriod => value.AddHours(delta >= 0 ? 12 : -12),
            _ => value,
        };

        return IsInRange(candidate, min, max, overnight) ? candidate : value;
    }

    /// <summary>
    /// Inclusive range check. With <paramref name="overnight"/> and both bounds, a min after max
    /// is read as <c>[min, 24:00) ∪ [00:00, max]</c>. Without overnight a reversed pair matches
    /// nothing.
    /// </summary>
    public static bool IsInRange(TimeOnly value, TimeOnly? min, TimeOnly? max, bool overnight)
    {
        if (min is null && max is null)
        {
            return true;
        }

        if (min is not null && max is not null && min.Value > max.Value)
        {
            return overnight && (value >= min.Value || value <= max.Value);
        }

        if (min is not null && value < min.Value)
        {
            return false;
        }

        if (max is not null && value > max.Value)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Snaps minutes onto <paramref name="minuteStep"/> in <paramref name="direction"/> without
    /// carrying into the hour. Already-on-step values are unchanged.
    /// </summary>
    public static TimeOnly SnapToStep(TimeOnly value, int minuteStep, int direction)
    {
        if (minuteStep <= 1)
        {
            return value;
        }

        var remainder = value.Minute % minuteStep;
        if (remainder == 0)
        {
            return value;
        }

        int minutes;
        if (direction >= 0)
        {
            minutes = value.Minute - remainder + minuteStep;
            if (minutes >= 60)
            {
                minutes = 0;
            }
        }
        else
        {
            minutes = value.Minute - remainder;
        }

        return new TimeOnly(value.Hour, minutes, value.Second);
    }

    /// <summary>
    /// Rounds <paramref name="value"/> up to the next multiple of <paramref name="minuteStep"/>
    /// (the "Now" chip). Unlike segment snap this MAY change the hour and wrap past midnight.
    /// An already-on-step time with zero seconds is unchanged.
    /// </summary>
    public static TimeOnly RoundUpToStep(TimeOnly value, int minuteStep)
    {
        if (minuteStep < 1)
        {
            minuteStep = 1;
        }

        var totalMinutes = value.Hour * 60 + value.Minute;
        var remainder = totalMinutes % minuteStep;
        if (remainder == 0 && value.Second == 0 && value.Millisecond == 0)
        {
            return new TimeOnly(value.Hour, value.Minute);
        }

        var add = remainder == 0 ? minuteStep : minuteStep - remainder;
        return TimeOnly.MinValue.Add(TimeSpan.FromMinutes(totalMinutes + add));
    }

    /// <summary>
    /// Inclusive local-time gap skipped by a spring-forward DST transition on
    /// <paramref name="date"/> in <paramref name="timeZone"/>, or <see langword="null"/> when
    /// that day has no missing hour (UTC, a zone without DST, an ordinary day).
    /// </summary>
    public static (TimeOnly Start, TimeOnly End)? GetDstGap(DateOnly date, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        TimeOnly? start = null;
        TimeOnly end = default;
        for (var minute = 0; minute < 24 * 60; minute++)
        {
            var time = TimeOnly.MinValue.Add(TimeSpan.FromMinutes(minute));
            if (IsInvalid(date, time, timeZone))
            {
                start ??= time;
                end = time;
            }
            else if (start is not null)
            {
                break;
            }
        }

        return start is null ? null : (start.Value, end);
    }

    /// <summary>
    /// <see langword="true"/> when <paramref name="date"/> + <paramref name="time"/> does not
    /// exist in <paramref name="timeZone"/> (the spring-forward gap).
    /// </summary>
    public static bool IsInvalid(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return timeZone.IsInvalidTime(ToUnspecified(date, time));
    }

    /// <summary>
    /// <see langword="true"/> when <paramref name="date"/> + <paramref name="time"/> occurs twice
    /// in <paramref name="timeZone"/> (the fall-back overlap).
    /// </summary>
    public static bool IsAmbiguous(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return timeZone.IsAmbiguousTime(ToUnspecified(date, time));
    }

    /// <summary>
    /// Offsets of an ambiguous civil time, ordered earlier (DST, typically UTC+2 in Prague)
    /// then later (standard, typically UTC+1). Empty when the instant is not ambiguous.
    /// </summary>
    public static TimeSpan[] GetAmbiguousOffsets(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var unspecified = ToUnspecified(date, time);
        if (!timeZone.IsAmbiguousTime(unspecified))
        {
            return [];
        }

        var offsets = timeZone.GetAmbiguousTimeOffsets(unspecified);
        Array.Sort(offsets, (a, b) => b.CompareTo(a));
        return offsets;
    }

    private static DateTime ToUnspecified(DateOnly date, TimeOnly time)
        => DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);

    private static TimeOnly StepMinutes(TimeOnly value, int delta, int minuteStep)
    {
        var remainder = value.Minute % minuteStep;
        int minutes;
        if (remainder == 0)
        {
            minutes = value.Minute + (delta * minuteStep);
            minutes %= 60;
            if (minutes < 0)
            {
                minutes += 60;
            }
        }
        else
        {
            return SnapToStep(value, minuteStep, delta);
        }

        return new TimeOnly(value.Hour, minutes, value.Second);
    }

    private static TimeOnly StepSeconds(TimeOnly value, int delta)
    {
        var seconds = value.Second + delta;
        seconds %= 60;
        if (seconds < 0)
        {
            seconds += 60;
        }

        return new TimeOnly(value.Hour, value.Minute, seconds);
    }

    /// <summary>
    /// <see langword="true"/> when an unquoted <c>h</c> appears in the pattern. Quoted literals
    /// (fr-CA <c>HH 'h' mm</c>) are not a 12-hour clock.
    /// </summary>
    private static bool PatternUses12HourClock(string pattern)
    {
        var inQuote = false;
        foreach (var ch in pattern)
        {
            if (ch == '\'')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && ch == 'h')
            {
                return true;
            }
        }

        return false;
    }

    private static string AdjustPattern(string pattern, TimeHourCycle cycle, CultureInfo culture)
    {
        var from12 = PatternUses12HourClock(pattern);
        if (cycle == TimeHourCycle.H12)
        {
            var twelve = from12 ? pattern : MapUnquotedHourSpecifier(pattern, toTwelveHour: true);
            if (!ContainsUnquoted(twelve, 't')
                && !string.IsNullOrEmpty(culture.DateTimeFormat.AMDesignator))
            {
                twelve += " tt";
            }

            return twelve;
        }

        var twentyFour = from12 ? MapUnquotedHourSpecifier(pattern, toTwelveHour: false) : pattern;
        return StripUnquotedDayPeriod(twentyFour);
    }

    private static string MapUnquotedHourSpecifier(string pattern, bool toTwelveHour)
    {
        var builder = new System.Text.StringBuilder(pattern.Length);
        var inQuote = false;
        var i = 0;
        while (i < pattern.Length)
        {
            var ch = pattern[i];
            if (ch == '\'')
            {
                inQuote = !inQuote;
                builder.Append(ch);
                i++;
                continue;
            }

            if (!inQuote && (ch == 'H' || ch == 'h'))
            {
                var mapped = toTwelveHour ? 'h' : 'H';
                while (i < pattern.Length && (pattern[i] == 'H' || pattern[i] == 'h'))
                {
                    builder.Append(mapped);
                    i++;
                }

                continue;
            }

            builder.Append(ch);
            i++;
        }

        return builder.ToString();
    }

    private static bool ContainsUnquoted(string pattern, char letter)
    {
        var inQuote = false;
        var lower = char.ToLowerInvariant(letter);
        foreach (var ch in pattern)
        {
            if (ch == '\'')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && char.ToLowerInvariant(ch) == lower)
            {
                return true;
            }
        }

        return false;
    }

    private static string StripUnquotedDayPeriod(string pattern)
    {
        var builder = new System.Text.StringBuilder(pattern.Length);
        var inQuote = false;
        var i = 0;
        while (i < pattern.Length)
        {
            var ch = pattern[i];
            if (ch == '\'')
            {
                inQuote = !inQuote;
                builder.Append(ch);
                i++;
                continue;
            }

            if (!inQuote && (ch == 't' || ch == 'T'))
            {
                while (i < pattern.Length && (pattern[i] == 't' || pattern[i] == 'T'))
                {
                    i++;
                }

                continue;
            }

            builder.Append(ch);
            i++;
        }

        return builder.ToString().Trim();
    }

    private static bool? DetectPeriod(ref string text, CultureInfo culture)
    {
        var am = culture.DateTimeFormat.AMDesignator;
        var pm = culture.DateTimeFormat.PMDesignator;
        if (!string.IsNullOrEmpty(am) && culture.CompareInfo.IsSuffix(text, am, CompareOptions.IgnoreCase))
        {
            text = text[..^am.Length].TrimEnd();
            return false;
        }

        if (!string.IsNullOrEmpty(pm) && culture.CompareInfo.IsSuffix(text, pm, CompareOptions.IgnoreCase))
        {
            text = text[..^pm.Length].TrimEnd();
            return true;
        }

        var match = PeriodSuffix.Match(text);
        if (!match.Success || match.Index == 0)
        {
            return null;
        }

        var period = match.Groups["period"].Value;
        var pmSuffix = period.StartsWith('p') || period.StartsWith('P');
        text = text[..match.Index].TrimEnd();
        return pmSuffix;
    }

    private static bool TryReadClock(string text, bool showSeconds, out int hour, out int minute, out int second)
    {
        hour = 0;
        minute = 0;
        second = 0;

        if (text.Length > 0 && text.All(char.IsDigit))
        {
            return text.Length switch
            {
                3 => TryHms(int.Parse(text, CultureInfo.InvariantCulture) / 100,
                    int.Parse(text, CultureInfo.InvariantCulture) % 100, 0, showSeconds: false,
                    out hour, out minute, out second),
                4 => TryHms(int.Parse(text[..2], CultureInfo.InvariantCulture),
                    int.Parse(text[2..], CultureInfo.InvariantCulture), 0, showSeconds: false,
                    out hour, out minute, out second),
                6 when showSeconds => TryHms(
                    int.Parse(text[..2], CultureInfo.InvariantCulture),
                    int.Parse(text[2..4], CultureInfo.InvariantCulture),
                    int.Parse(text[4..], CultureInfo.InvariantCulture), showSeconds: true,
                    out hour, out minute, out second),
                _ => false,
            };
        }

        var normalized = text.Replace('h', ':').Replace('H', ':').Replace('.', ':');
        var parts = normalized.Split(':', StringSplitOptions.None);
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        if (parts.Length == 3 && !showSeconds)
        {
            return false;
        }

        if (parts.Any(part => part.Length == 0 || !part.All(char.IsDigit)))
        {
            return false;
        }

        var parsedHour = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var parsedMinute = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var parsedSecond = parts.Length == 3 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 0;
        return TryHms(parsedHour, parsedMinute, parsedSecond, showSeconds && parts.Length == 3,
            out hour, out minute, out second);
    }

    private static bool TryHms(
        int hour, int minute, int second, bool showSeconds,
        out int outHour, out int outMinute, out int outSecond)
    {
        outHour = hour;
        outMinute = minute;
        outSecond = showSeconds ? second : 0;
        return hour is >= 0 and <= 23
            && minute is >= 0 and <= 59
            && outSecond is >= 0 and <= 59;
    }

    private static int ApplyPeriod(int hour12, bool isPm)
    {
        if (hour12 == 12)
        {
            return isPm ? 12 : 0;
        }

        return isPm ? hour12 + 12 : hour12;
    }
}
