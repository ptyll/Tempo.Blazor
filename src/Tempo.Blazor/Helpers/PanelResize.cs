using System.Globalization;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// The pure resize rules shared by every component that resizes a docked panel with a separator —
/// the keyboard path (C#) and the pointer path (<c>clampWidth</c> in the shared
/// <c>tm-panel-resize.js</c>): clamp to the min/max width while keeping the content region at least
/// its minimum, and parse a plain pixel length without ever trusting a raw string.
/// <para>
/// Used by <c>TmEditorShell</c> (its two panels), <c>TmAppShell</c> (its work area) and
/// <c>TmSplitterPane</c> (its sizes). The C# vectors and the JS vectors are asserted as the same
/// contract on both sides, so a change here needs the matching change in
/// <c>tm-panel-resize.js</c>.
/// </para>
/// </summary>
internal static partial class PanelResize
{
    /// <summary>The clamped panel width in whole pixels (identical to <c>clampWidth</c> in JS).</summary>
    /// <param name="requested">The width the pointer or key asked for.</param>
    /// <param name="current">The width the gesture started from.</param>
    /// <param name="min">Smallest allowed panel width.</param>
    /// <param name="max">Largest allowed panel width.</param>
    /// <param name="canvas">The content region width at the start of the gesture; NaN when unknown.</param>
    /// <param name="minCanvas">The content width a resize never takes away.</param>
    public static double Clamp(double requested, double current, int min, int max, double canvas, int minCanvas)
    {
        // Math.Floor(x + 0.5) is JS Math.round: half rounds up, so both sides agree on .5.
        var width = Math.Min(max, Math.Max(min, Math.Floor(requested + 0.5)));
        if (double.IsFinite(canvas) && width > current)
        {
            width = Math.Min(width, Math.Max(current, current + (canvas - minCanvas)));
        }
        return width;
    }

    /// <summary>Parses a plain "NNNpx" length; anything else (rem, %, calc, trailing junk, negatives) is rejected.</summary>
    public static bool TryParsePx(string? value, out int px)
    {
        px = 0;
        if (value is null) return false;
        var match = PixelLength().Match(value);
        if (!match.Success) return false;
        var number = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        if (number > int.MaxValue) return false;
        px = (int)Math.Floor(number + 0.5);
        return true;
    }

    [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?)px\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex PixelLength();
}
