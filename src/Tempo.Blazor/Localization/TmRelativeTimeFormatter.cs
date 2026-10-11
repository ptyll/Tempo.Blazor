using System.Globalization;

namespace Tempo.Blazor.Localization;

/// <summary>
/// The default <see cref="ITmRelativeTimeFormatter"/>: strings from <see cref="ITmLocalizer"/>
/// (<c>Tm_Time_*</c>), plural forms from <see cref="TmLocalizerPluralExtensions.Plural"/>, the current
/// moment from a <see cref="TimeProvider"/>.
/// <list type="bullet">
///   <item><description>under a minute either way → "just now" (so clock skew never says "in 0 min");</description></item>
///   <item><description>under an hour → minutes; the same calendar day → hours;</description></item>
///   <item><description>the neighbouring calendar day → "yesterday at 16:20" / "tomorrow at 9:00";</description></item>
///   <item><description>2–6 calendar days → days in the right grammatical form;</description></item>
///   <item><description>a week or more → the absolute date in the localizer's culture.</description></item>
/// </list>
/// "Calendar day" is judged in the time zone the caller passes (or the clock's local zone), never in
/// the offset the value happens to carry, so the same instant reads the same for everyone in a zone.
/// </summary>
public sealed class TmRelativeTimeFormatter : ITmRelativeTimeFormatter
{
    private const int AbsoluteFromDays = 7;

    private readonly ITmLocalizer _localizer;
    private readonly TimeProvider _clock;

    /// <summary>Creates the formatter.</summary>
    /// <param name="localizer">Source of the <c>Tm_Time_*</c> strings and of the culture used for dates.</param>
    /// <param name="clock">The clock; <see cref="TimeProvider.System"/> when null.</param>
    public TmRelativeTimeFormatter(ITmLocalizer localizer, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Format(DateTimeOffset value, DateTimeOffset? now = null, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? _clock.LocalTimeZone;
        var reference = now ?? _clock.GetUtcNow();
        var difference = reference - value;
        var isPast = difference >= TimeSpan.Zero;
        var distance = isPast ? difference : -difference;

        if (distance < TimeSpan.FromMinutes(1))
        {
            return _localizer["Tm_Time_JustNow"];
        }

        if (distance < TimeSpan.FromHours(1))
        {
            var minutes = (int)distance.TotalMinutes;
            return _localizer[isPast ? "Tm_Time_MinutesAgo" : "Tm_Time_InMinutes", minutes];
        }

        var localValue = TimeZoneInfo.ConvertTime(value, zone);
        var localNow = TimeZoneInfo.ConvertTime(reference, zone);
        var days = Math.Abs(localNow.Date.Subtract(localValue.Date).Days);

        if (days == 0)
        {
            var hours = (int)distance.TotalHours;
            return _localizer[isPast ? "Tm_Time_HoursAgo" : "Tm_Time_InHours", hours];
        }

        if (days == 1)
        {
            return _localizer[isPast ? "Tm_Time_YesterdayAt" : "Tm_Time_TomorrowAt", FormatClock(localValue)];
        }

        if (days < AbsoluteFromDays)
        {
            return _localizer.Plural(isPast ? "Tm_Time_DaysAgo" : "Tm_Time_InDays", days);
        }

        return FormatDate(localValue);
    }

    private string FormatClock(DateTimeOffset value)
        => value.ToString(_localizer.TryGetString("Tm_Time_TimeFormat", out var pattern) ? pattern : "t",
            _localizer.Culture ?? CultureInfo.CurrentUICulture);

    private string FormatDate(DateTimeOffset value)
        => value.ToString(_localizer.TryGetString("Tm_Time_DateFormat", out var pattern) ? pattern : "d",
            _localizer.Culture ?? CultureInfo.CurrentUICulture);
}