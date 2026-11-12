namespace Tempo.Blazor.Components.Charts;

/// <summary>
/// The token-driven chart series palette shared by every chart component (<see cref="TmChart"/>,
/// <see cref="TmSparkline"/>, <see cref="TmSankeyChart"/>, <see cref="TmGauge"/>,
/// <see cref="TmStockChart"/>). Since TG-002 the palette lives in the <c>--tm-chart-series-*</c>
/// design tokens (light, dark and the indigo theme) instead of hardcoded hexes, so a theme retints
/// the series and dark mode lifts them to tones that hold contrast on the dark surface.
/// </summary>
/// <remarks>
/// Each series has a PAIRED ink token (<c>--tm-chart-series-n-ink</c>) — the ink a label drawn ON
/// that fill takes in the current theme. <see cref="IndexOf"/> maps a resolved fill back to its
/// series so a fill-level component (the funnel, the treemap) can pick the paired ink; a fill the
/// palette did not produce (a host's own colour) returns <see langword="null"/> and the caller keeps
/// its own contrast logic.
/// </remarks>
internal static class ChartSeriesTokens
{
    /// <summary>The number of series slots the token palette declares (<c>--tm-chart-series-1..8</c>).</summary>
    internal const int Count = 8;

    /// <summary>The token references of the whole palette, index 0 = series 1.</summary>
    internal static readonly string[] Palette =
    [
        "var(--tm-chart-series-1)",
        "var(--tm-chart-series-2)",
        "var(--tm-chart-series-3)",
        "var(--tm-chart-series-4)",
        "var(--tm-chart-series-5)",
        "var(--tm-chart-series-6)",
        "var(--tm-chart-series-7)",
        "var(--tm-chart-series-8)",
    ];

    /// <summary>The paired ink token references of the whole palette, index 0 = series 1.</summary>
    internal static readonly string[] PaletteInk =
    [
        "var(--tm-chart-series-1-ink)",
        "var(--tm-chart-series-2-ink)",
        "var(--tm-chart-series-3-ink)",
        "var(--tm-chart-series-4-ink)",
        "var(--tm-chart-series-5-ink)",
        "var(--tm-chart-series-6-ink)",
        "var(--tm-chart-series-7-ink)",
        "var(--tm-chart-series-8-ink)",
    ];

    /// <summary>The token reference of one series (1-based, wrapped into the declared range).</summary>
    internal static string Series(int index) => Palette[NormalizedIndex(index)];

    /// <summary>The paired ink token reference of one series (1-based, wrapped).</summary>
    internal static string SeriesInk(int index) => PaletteInk[NormalizedIndex(index)];

    /// <summary>
    /// The palette index (0-based) whose token reference <paramref name="fill"/> IS, or
    /// <see langword="null"/> when the fill is not one of the built-in series tokens (a host colour,
    /// another token, …). The comparison is ordinal — a host passing the literal string
    /// <c>"var(--tm-chart-series-3)"</c> in its own colours still gets the paired ink, which is the
    /// correct reading of the contract.
    /// </summary>
    internal static int? IndexOf(string? fill)
    {
        if (string.IsNullOrWhiteSpace(fill))
        {
            return null;
        }

        var index = Array.IndexOf(Palette, fill);
        return index >= 0 ? index : null;
    }

    private static int NormalizedIndex(int index) =>
        index >= 0 ? index % Count : ((index % Count) + Count) % Count;
}
