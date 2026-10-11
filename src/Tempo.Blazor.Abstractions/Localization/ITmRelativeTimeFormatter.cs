namespace Tempo.Blazor.Localization;

/// <summary>
/// Formats a moment relative to now in the user's language: "just now", "5 min ago",
/// "yesterday at 4:20 PM", "in 3 days", and an absolute date once it is a week away. The one shared
/// implementation replaces the private copies components used to carry.
/// </summary>
public interface ITmRelativeTimeFormatter
{
    /// <summary>Formats <paramref name="value"/> relative to <paramref name="now"/>.</summary>
    /// <param name="value">The moment to describe.</param>
    /// <param name="now">The reference moment; defaults to the formatter's clock (<see cref="TimeProvider"/>).</param>
    /// <param name="timeZone">
    /// The zone that decides what "today" and "yesterday" mean and in which the clock time is shown;
    /// defaults to the clock's local zone.
    /// </param>
    string Format(DateTimeOffset value, DateTimeOffset? now = null, TimeZoneInfo? timeZone = null);

    /// <summary>
    /// The absolute time of <paramref name="value"/> in the zone the relative stamp was judged in,
    /// for a <c>title</c> tooltip. Defaults to the same conversion <see cref="Format"/> uses.
    /// </summary>
    string Absolute(DateTimeOffset value, TimeZoneInfo? timeZone = null) =>
        TimeZoneInfo.ConvertTime(value, timeZone ?? TimeZoneInfo.Local).ToString("g");
}