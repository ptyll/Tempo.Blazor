using System.Globalization;

namespace Tempo.Blazor.AI.Formatting;

/// <summary>
/// The clock-style duration the agent surfaces print: <c>0:42</c>, <c>1:24</c>, <c>1:02:07</c>.
/// </summary>
/// <remarks>
/// Invariant digits and separator on purpose — this is a duration, not a quantity of a unit, and the four
/// shipped cultures all read it the same way while a localized digit set would make it unrecognizable next to
/// a timestamp. Shared by <c>TmAgentPlan</c>, <c>TmAgentRunTree</c> and <c>TmAgentMonitor</c>, so one run
/// never shows two duration shapes.
/// </remarks>
internal static class AiClockFormatter
{
    /// <summary>
    /// Formats a duration as <c>m:ss</c> or <c>h:mm:ss</c>.
    /// </summary>
    /// <param name="value">The duration to format; a negative value reads as zero.</param>
    /// <returns>The duration as <c>m:ss</c> or <c>h:mm:ss</c>.</returns>
    public static string FormatClock(TimeSpan value)
    {
        var span = value < TimeSpan.Zero ? TimeSpan.Zero : value;

        return span.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture,
                $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}:{span.Seconds:00}");
    }
}
