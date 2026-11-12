using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
[TestCategory("WASM")]
public sealed class TmChartColorsE2ETests : WasmTestBase
{
    private static string ScreenshotDir
    {
        get
        {
            var dir = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "screenshots",
                "charts"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    [TestMethod]
    public async Task TmChart_Donut_CustomColors_AreApplied()
    {
        var page = await OpenChartsPageAsync();
        var chart = page.Locator("[data-testid='chart-donut-custom']");

        await AssertFillAttributesAsync(
            chart.Locator("path.tm-chart__slice"),
            ["#dc2626", "#ea580c", "#2563eb", "#16a34a", "#64748b"]);
        await CaptureChartAsync(chart, "tm-chart-colors-donut-custom");
    }

    [TestMethod]
    public async Task TmChart_HorizontalBar_CustomColors_AreApplied()
    {
        var page = await OpenChartsPageAsync();
        var chart = page.Locator("[data-testid='chart-horizontal-bar-custom']");

        await AssertFillAttributesAsync(
            chart.Locator("rect.tm-chart__bar"),
            ["#2563eb", "#0d9488", "#7c3aed", "#ea580c", "#be123c"]);
        await CaptureChartAsync(chart, "tm-chart-colors-horizontal-bar-custom");
    }

    [TestMethod]
    public async Task TmChart_Bar_CustomColors_AreApplied()
    {
        var page = await OpenChartsPageAsync();
        var chart = page.Locator("[data-testid='chart-bar-custom']");

        await AssertFillAttributesAsync(
            chart.Locator("rect.tm-chart__bar"),
            ["#2563eb", "#0d9488", "#7c3aed", "#ea580c", "#65a30d"]);
        await CaptureChartAsync(chart, "tm-chart-colors-bar-custom");
    }

    [TestMethod]
    public async Task TmChart_Donut_DefaultPalette_UsesSeriesTokens_AndComputesTheLegacyColours()
    {
        var page = await OpenChartsPageAsync();
        var chart = page.Locator("[data-testid='chart-donut-default']");

        // The fill ATTRIBUTE carries the design token since TG-002 (no hardcoded hex in the library
        // markup)…
        await AssertFillAttributesAsync(
            chart.Locator("path.tm-chart__slice"),
            ["var(--tm-chart-series-1)", "var(--tm-chart-series-2)", "var(--tm-chart-series-3)",
             "var(--tm-chart-series-4)", "var(--tm-chart-series-5)"]);

        // …while the COMPUTED fills stay byte-identical to the pre-token palette in the light theme,
        // so an existing host's charts look exactly the same (getComputedStyle returns rgb(), so the
        // comparison normalises to #rrggbb).
        var computedFills = await chart.Locator("path.tm-chart__slice").EvaluateAllAsync<string[]>(
            "elements => elements.map(e => getComputedStyle(e).fill)");
        CollectionAssert.AreEqual(
            new[] { "#3b82f6", "#ef4444", "#10b981", "#f59e0b", "#8b5cf6" },
            computedFills.Select(ParseHex).ToArray(),
            $"Expected computed fills: #3b82f6, #ef4444, #10b981, #f59e0b, #8b5cf6; actual: {string.Join(", ", computedFills)}");

        await CaptureChartAsync(chart, "tm-chart-colors-donut-default");
    }

    private async Task<IPage> OpenChartsPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1366, 900);
        await page.GotoAsync($"{BaseUrl}/charts", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync("[data-testid='chart-donut-custom'] .tm-chart svg", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        return page;
    }

    private static async Task AssertFillAttributesAsync(ILocator locator, string[] expectedFills)
    {
        await Assertions.Expect(locator).ToHaveCountAsync(expectedFills.Length);
        var actualFills = await locator.EvaluateAllAsync<string[]>(
            "elements => elements.map(element => element.getAttribute('fill'))");

        CollectionAssert.AreEqual(
            expectedFills,
            actualFills,
            $"Expected fills: {string.Join(", ", expectedFills)}; actual fills: {string.Join(", ", actualFills)}");
    }

    private static string ParseHex(string rgb)
    {
        var match = System.Text.RegularExpressions.Regex.Match(rgb, @"rgba?\((\d+),\s*(\d+),\s*(\d+)");
        Assert.IsTrue(match.Success, $"cannot parse '{rgb}'");
        return "#" + string.Concat(match.Groups.Cast<System.Text.RegularExpressions.Group>().Skip(1)
            .Select(g => int.Parse(g.Value, System.Globalization.CultureInfo.InvariantCulture)
                .ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static async Task CaptureChartAsync(ILocator chart, string screenshotName)
    {
        await chart.ScrollIntoViewIfNeededAsync();
        await chart.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(ScreenshotDir, $"{screenshotName}.png"),
            Type = ScreenshotType.Png,
            OmitBackground = false
        });
    }
}
