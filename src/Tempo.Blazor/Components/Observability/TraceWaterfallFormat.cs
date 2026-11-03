using System.Globalization;

namespace Tempo.Blazor.Components.Observability;

/// <summary>
/// Culture-aware number formatting of the waterfall: durations, axis ticks and metric values. The
/// unit LABELS ("ms", "s", "min") are deliberately not here — they are localized by the component,
/// because they are user-visible text; this helper returns the numeric part only.
/// </summary>
internal static class TraceWaterfallFormat
{
    /// <summary>One second in milliseconds.</summary>
    private const double MillisecondsPerSecond = 1_000;

    /// <summary>One minute in milliseconds.</summary>
    private const double MillisecondsPerMinute = 60_000;

    /// <summary>"0.##" — a grouped-free, trailing-zero-trimmed decimal that follows the culture's separators.</summary>
    private const string TrimmedFormat = "0.##";

    /// <summary>
    /// The axis unit a range reads in: milliseconds under a second, seconds under a minute, minutes
    /// above that. The boundary is inclusive of the smaller unit (exactly 1 s reads in seconds).
    /// </summary>
    /// <param name="duration">The visible axis length (or any duration to classify).</param>
    public static TraceAxisUnit AxisUnit(TimeSpan duration)
        => duration.TotalMilliseconds < MillisecondsPerSecond ? TraceAxisUnit.Ms
        : duration.TotalMilliseconds < MillisecondsPerMinute ? TraceAxisUnit.Seconds
        : TraceAxisUnit.Minutes;

    /// <summary>The numeric value of a duration expressed in the given unit.</summary>
    /// <param name="value">The duration.</param>
    /// <param name="unit">The unit to express it in.</param>
    public static double ToUnit(TimeSpan value, TraceAxisUnit unit) => unit switch
    {
        TraceAxisUnit.Ms => value.TotalMilliseconds,
        TraceAxisUnit.Seconds => value.TotalMilliseconds / MillisecondsPerSecond,
        _ => value.TotalMilliseconds / MillisecondsPerMinute
    };

    /// <summary>
    /// Formats one axis tick's offset in the axis unit. The tick label never carries a unit suffix —
    /// the component renders the localized unit once next to the axis.
    /// </summary>
    /// <param name="offset">The tick's offset from the axis start.</param>
    /// <param name="unit">The axis unit.</param>
    /// <param name="culture">The culture whose separators are used; null means the current culture.</param>
    public static string FormatAxisValue(TimeSpan offset, TraceAxisUnit unit, IFormatProvider? culture)
        => ToUnit(offset, unit).ToString(TrimmedFormat, culture ?? CultureInfo.CurrentCulture);

    /// <summary>
    /// Formats a span's duration in the unit it reads best in, together with that unit. A duration
    /// under a second is reported in whole-ish milliseconds ("820"), a duration under a minute in
    /// seconds ("1.24"), anything longer in minutes ("2.5").
    /// </summary>
    /// <param name="value">The duration to format.</param>
    /// <param name="culture">The culture whose separators are used; null means the current culture.</param>
    /// <returns>The numeric text and the unit it is expressed in.</returns>
    public static (string Text, TraceAxisUnit Unit) FormatDuration(TimeSpan value, IFormatProvider? culture)
    {
        var unit = AxisUnit(value);
        return (ToUnit(value, unit).ToString(TrimmedFormat, culture ?? CultureInfo.CurrentCulture), unit);
    }

    /// <summary>
    /// Formats one metric value with a .NET numeric format string, falling back to <c>"N0"</c> for a
    /// null/blank format. Used both for the span's own value and for the subtree sum.
    /// </summary>
    /// <param name="value">The metric value.</param>
    /// <param name="format">The .NET numeric format string, or null/blank for <c>"N0"</c>.</param>
    /// <param name="culture">The culture whose separators are used; null means the current culture.</param>
    public static string FormatMetric(double value, string? format, IFormatProvider? culture)
        => value.ToString(
            string.IsNullOrWhiteSpace(format) ? "N0" : format,
            culture ?? CultureInfo.CurrentCulture);

    /// <summary>
    /// The localized unit suffix key of a unit: the component resolves it, so no user-visible text
    /// lives in this helper. The keys exist in every resource file.
    /// </summary>
    /// <param name="unit">The unit.</param>
    public static string UnitKey(TraceAxisUnit unit) => unit switch
    {
        TraceAxisUnit.Ms => "TmTraceWaterfall_UnitMs",
        TraceAxisUnit.Seconds => "TmTraceWaterfall_UnitSeconds",
        _ => "TmTraceWaterfall_UnitMinutes"
    };
}
