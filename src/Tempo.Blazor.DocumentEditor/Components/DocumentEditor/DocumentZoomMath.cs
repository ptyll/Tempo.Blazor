using System.Globalization;

namespace Tempo.Blazor.Components.DocumentEditor;

// Phase 11 (fix round): the arithmetic and the text of the zoom controls, in one place so the status bar, the ribbon and the print preview agree.
internal static class DocumentZoomMath
{
    /// <summary>
    /// The percent as the culture writes it: "62%" in English, "62 %" with a NON-BREAKING space in Czech, Slovak and French (the number and its sign
    /// never wrap apart). The pattern comes from the culture's own number format, the space is always U+00A0.
    /// </summary>
    internal static string FormatPercent(int percent, CultureInfo culture)
    {
        var format = culture.NumberFormat;
        var number = percent.ToString("N0", culture);
        var symbol = format.PercentSymbol;
        return format.PercentPositivePattern switch
        {
            0 => $"{number} {symbol}",
            2 => $"{symbol}{number}",
            3 => $"{symbol} {number}",
            _ => $"{number}{symbol}",
        };
    }

    /// <summary>
    /// One step of the zoom buttons, snapped to the grid of the step: from a fitted 62 percent the next step up is 70 (not 72) and the next step down
    /// 60, so the reader lands on the round values of the zoom menu. The result is kept inside [<paramref name="min"/>, <paramref name="max"/>].
    /// </summary>
    internal static int Step(int percent, int step, int min, int max, bool up)
    {
        step = Math.Max(1, step);
        var snapped = up
            ? ((percent / step) + 1) * step
            : (int)Math.Ceiling((percent / (double)step) - 1d) * step;
        return Math.Clamp(snapped, min, max);
    }
}
