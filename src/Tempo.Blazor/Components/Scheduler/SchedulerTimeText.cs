using System.Globalization;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The time range an event card prints. A narrow column (a week with resources, a phone, a day cell)
/// cannot fit <c>9:00 AM – 10:00 AM</c> on one line, so the card renders the full text and a compact
/// variant and the stylesheet picks by the card's own width. Both readings come from the culture's
/// own patterns — the compact one simply prints the meridiem once, the way a person writes it.
/// </summary>
internal static class SchedulerTimeText
{
    /// <summary>The two readings of a range in the current UI culture.</summary>
    public static (string Full, string Compact) Range(DateTime start, DateTime end)
        => Range(start, end, CultureInfo.CurrentUICulture);

    /// <summary>
    /// The two readings of a range. <c>Full</c> is the culture's short time on both ends. <c>Compact</c>
    /// drops the start's meridiem when both ends share it (<c>9:00 – 10:00 AM</c>); a culture without
    /// a meridiem, or a range crossing noon, keeps the full text on both.
    /// </summary>
    public static (string Full, string Compact) Range(DateTime start, DateTime end, CultureInfo culture)
    {
        // ICU separates the time from its meridiem with a narrow no-break space. Keep the pair
        // together — a card may wrap AROUND the en dash, never inside "10:00 AM" — so every
        // in-time space becomes the plain non-breaking one and only the range separator stays a
        // plain space. Normalised here rather than in every caller.
        var startText = InTime(start.ToString("t", culture));
        var endText = InTime(end.ToString("t", culture));
        var full = $"{startText} – {endText}";
        return (full, Compact(start, end, startText, endText, culture) ?? full);
    }

    /// <summary>The space that joins a time to its meridiem; it must not be a break opportunity.</summary>
    private const char NoBreakSpace = '\u00A0';

    /// <summary>
    /// Every space inside one time reading becomes non-breaking (ICU's narrow and thin spaces
    /// included). The reading still joins its neighbours with a plain space at the caller.
    /// </summary>
    private static string InTime(string text)
        => text.Replace('\u202F', NoBreakSpace).Replace('\u2009', NoBreakSpace).Replace(' ', NoBreakSpace);

    private static string? Compact(DateTime start, DateTime end, string startText, string endText, CultureInfo culture)
    {
        // A 24-hour culture already prints the shortest legal form.
        if (!culture.DateTimeFormat.ShortTimePattern.Contains('t')) return null;

        var startDesignator = DesignatorAt(start, culture);
        if (startDesignator.Length == 0) return null;

        // The meridiem has to be the START text's suffix; a culture that prints it first keeps the
        // full reading, because the compact form would have to move it in front of the end time.
        if (!startText.EndsWith(startDesignator, StringComparison.CurrentCulture)) return null;
        var trimmed = startText[..^startDesignator.Length].TrimEnd();
        if (trimmed.Length == 0) return null;
        // Both ends must share the meridiem, or one designator would be lost.
        var endDesignator = DesignatorAt(end, culture);
        if (!string.Equals(startDesignator, endDesignator, StringComparison.CurrentCulture)) return null;
        return $"{trimmed} – {endText}";
    }

    private static string DesignatorAt(DateTime moment, CultureInfo culture)
        => moment.Hour < 12 ? culture.DateTimeFormat.AMDesignator : culture.DateTimeFormat.PMDesignator;
}
