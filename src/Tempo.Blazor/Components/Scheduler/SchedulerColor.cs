using System.Globalization;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// Decides whether white text stays readable on a consumer's event colour. The solid fill is kept
/// either way; only the text colour changes, through the documented <c>--event-text-color</c> hook.
/// </summary>
internal static class SchedulerColor
{
    /// <summary>WCAG AA body-text threshold.</summary>
    public const double MinimumContrast = 4.5;

    /// <summary>
    /// The inline style fragment that switches the event text to the dark-on-light token when white
    /// text would fall below <see cref="MinimumContrast"/> on <paramref name="color"/>. Empty for a
    /// colour white already clears, and for a colour that cannot be read.
    /// </summary>
    public static string ReadableText(string? color)
    {
        var rgb = Parse(color);
        if (rgb is null) return string.Empty;
        var white = Contrast(rgb.Value, (255, 255, 255));
        if (white >= MinimumContrast) return string.Empty;
        // A mid colour clears neither extreme at 4.5:1. Black still beats the gray token there
        // (#8b5cf6 is 4.23:1 on white and 4.19:1 on #111827, but 4.96:1 on black).
        return Contrast(rgb.Value, (0, 0, 0)) > white
            ? "--event-text-color: var(--tm-scheduler-event-fg-on-light);"
            : string.Empty;
    }

    /// <summary>WCAG relative-luminance contrast between two sRGB triples.</summary>
    public static double Contrast((int R, int G, int B) a, (int R, int G, int B) b)
    {
        var lighter = Math.Max(Luminance(a), Luminance(b));
        var darker = Math.Min(Luminance(a), Luminance(b));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance((int R, int G, int B) c)
    {
        double Channel(int value)
        {
            var s = value / 255d;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary><c>#rgb</c>, <c>#rrggbb</c> and <c>rgb()</c>. Anything else is left to the theme.</summary>
    private static (int R, int G, int B)? Parse(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        var text = color.Trim();

        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length == 3)
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            if (hex.Length == 6
                && int.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
                && int.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
                && int.TryParse(hex[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                return (r, g, b);
            return null;
        }

        if (text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
        {
            var parts = text[4..^1].Split(',');
            if (parts.Length == 3
                && int.TryParse(parts[0].Trim(), out var r)
                && int.TryParse(parts[1].Trim(), out var g)
                && int.TryParse(parts[2].Trim(), out var b))
                return (r, g, b);
        }

        return null;
    }
}
