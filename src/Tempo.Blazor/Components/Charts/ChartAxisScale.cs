namespace Tempo.Blazor.Components.Charts;

/// <summary>
/// The arithmetic behind a <see cref="TmChart"/> value axis: a domain whose ends sit on a "nice" tick (a step
/// of 1, 2 or 5 × 10<sup>n</sup>), the tick list of that domain and the width-derived spacing of category
/// labels. Pure and culture-free — the chart formats the numbers, this type only chooses them — so every
/// rule is unit-testable without a DOM.
/// </summary>
/// <param name="Min">The first tick: a multiple of <paramref name="Step"/> at or below the data minimum.</param>
/// <param name="Max">The last tick: a multiple of <paramref name="Step"/> at or above the data maximum.</param>
/// <param name="Step">The distance between two neighbouring ticks (1, 2 or 5 × 10<sup>n</sup>).</param>
internal readonly record struct ChartAxisScale(double Min, double Max, double Step)
{
    /// <summary>The number of grid intervals a value axis aims for (the pre-2.12.0 axis always drew exactly five).</summary>
    public const int TargetIntervals = 5;

    /// <summary>
    /// The advance width of one axis-label character in viewBox units: the label type is
    /// <c>--tm-font-size-xs</c> (12 units) and a letter, digit or separator averages ≈ 0.5 em
    /// so the estimate never promises room the glyphs then take. SVG text is sized in USER units, so the
    /// estimate holds at every rendered scale.
    /// </summary>
    public const double LabelCharWidth = 6.5;

    /// <summary>The air kept between two neighbouring category labels, in viewBox units.</summary>
    public const double LabelGap = 4;

    /// <summary>Deconstructs the domain only, so <c>var (min, max) = scale</c> keeps reading like the old tuple.</summary>
    public void Deconstruct(out double min, out double max)
    {
        min = Min;
        max = Max;
    }

    /// <summary>
    /// A domain that covers <c>[<paramref name="min"/>, <paramref name="max"/>]</c> with both ends on a tick.
    /// A flat or inverted range is widened to one unit so the axis never has zero height.
    /// </summary>
    public static ChartAxisScale Nice(double min, double max)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            min = 0;
            max = 1;
        }

        if (max <= min)
        {
            max = min + 1;
        }

        var step = NiceStep((max - min) / TargetIntervals);
        var decimals = DecimalsFor(step);
        // 1e-9 swallows the float noise of x / step (0.6 / 0.2 = 2.9999999999999996) so an exact multiple
        // never grows a whole extra interval.
        var niceMin = Math.Floor(min / step + 1e-9) * step;
        var niceMax = Math.Ceiling(max / step - 1e-9) * step;
        return new ChartAxisScale(Round(niceMin, decimals), Round(niceMax, decimals), step);
    }

    /// <summary>
    /// The nice step closest to <paramref name="rawStep"/>: its mantissa snaps to 1, 2, 5 or 10
    /// (thresholds 1.5, 3, 7), so a raw 9.0 becomes 10 and a raw 26 becomes 20.
    /// </summary>
    public static double NiceStep(double rawStep)
    {
        if (!double.IsFinite(rawStep) || rawStep <= 0)
        {
            return 1;
        }

        var exponent = Math.Floor(Math.Log10(rawStep));
        var mantissa = rawStep / Math.Pow(10, exponent);
        var nice = mantissa < 1.5 - 1e-9 ? 1 : mantissa < 3 - 1e-9 ? 2 : mantissa < 7 - 1e-9 ? 5 : 10;
        return Round(nice * Math.Pow(10, exponent), (int)Math.Clamp(-exponent, 0, 15));
    }

    /// <summary>The fraction digits a <paramref name="step"/> needs to print every tick exactly (0.2 → 1, 5 → 0, 0.001 → 3).</summary>
    public static int DecimalsFor(double step)
    {
        if (!double.IsFinite(step) || step <= 0 || step >= 1)
        {
            return 0;
        }

        return (int)Math.Clamp(Math.Ceiling(-Math.Log10(step) - 1e-9), 0, 15);
    }

    /// <summary>Every tick from <see cref="Min"/> to <see cref="Max"/>, free of accumulated floating-point noise.</summary>
    public double[] Ticks()
    {
        var count = (int)Math.Round((Max - Min) / Step);
        var decimals = DecimalsFor(Step);
        var ticks = new double[count + 1];
        for (var k = 0; k <= count; k++)
        {
            ticks[k] = Round(Min + k * Step, decimals);
        }

        return ticks;
    }

    /// <summary>
    /// How many categories to skip so neighbouring labels never overlap: every n-th label renders, where n is
    /// the smallest step that fits the widest label (<paramref name="longestLabelChars"/> characters) into
    /// <paramref name="slotWidth"/> viewBox units per category. 1 means "keep every label".
    /// </summary>
    public static int LabelStep(int count, double slotWidth, int longestLabelChars)
    {
        if (count <= 1 || !double.IsFinite(slotWidth) || slotWidth <= 0)
        {
            return 1;
        }

        var required = Math.Max(longestLabelChars, 1) * LabelCharWidth + LabelGap;
        return Math.Max(1, (int)Math.Ceiling(required / slotWidth - 1e-9));
    }

    /// <summary>The height of one value-axis row label (the label type is 12 units) plus its gap, in viewBox units.</summary>
    public const double RowLabelHeight = 16;

    /// <summary>
    /// How many rows to skip so neighbouring row labels (a horizontal bar's categories) never overlap: every
    /// n-th label renders, where n is the smallest step that fits one label line into <paramref name="slotHeight"/>
    /// viewBox units per row. 1 means "keep every label".
    /// </summary>
    public static int RowStep(int count, double slotHeight)
    {
        if (count <= 1 || !double.IsFinite(slotHeight) || slotHeight <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Ceiling(RowLabelHeight / slotHeight - 1e-9));
    }

    /// <summary>
    /// The most labels of <paramref name="longestLabelChars"/> characters that fit across
    /// <paramref name="width"/> viewBox units without touching (at least 2): the width-derived replacement of
    /// the fixed "8 labels" cap of the time axis.
    /// </summary>
    public static int LabelBudget(double width, int longestLabelChars)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            return 2;
        }

        var required = Math.Max(longestLabelChars, 1) * LabelCharWidth + LabelGap;
        return Math.Max(2, (int)Math.Floor(width / required));
    }
    private static double Round(double value, int decimals) =>
        Math.Round(value, Math.Clamp(decimals, 0, 15), MidpointRounding.AwayFromZero) + 0.0; // + 0.0 turns -0 into 0
}
