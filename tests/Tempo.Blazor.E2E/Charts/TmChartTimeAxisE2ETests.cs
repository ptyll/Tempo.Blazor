using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TMPO-004 E2E (WASM @ 7106): the TmChart DateTime axis on the /charts demo page —
/// proportional X positions for unevenly spaced (and unsorted) time series, auto month
/// labels, custom label format with an explicit axis range, full-date tooltips, and an
/// axe-core scan. Screenshots land in <c>__screenshots__/charts/</c> for UX review.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmChartTimeAxisE2ETests : WasmTestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";

    private async Task<IPage> OpenChartsPageAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}/charts", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync("[data-testid='charts-time-axis-section']", new PageWaitForSelectorOptions { Timeout = 30000 });
        return page;
    }

    // ── E2E-TIM-1: proporcionální X — nerovnoměrné rozestupy, seřazeno ──────

    [TestMethod]
    public async Task TimeAxis_UnevenSeries_RendersProportionalSortedPoints()
    {
        var page = await OpenChartsPageAsync();
        var section = page.Locator("[data-testid='charts-time-axis-section']");
        var areaChart = section.Locator(".tm-chart").First;

        var cxs = await areaChart.Locator("circle.tm-chart__point")
            .EvaluateAllAsync<double[]>("els => els.map(e => parseFloat(e.getAttribute('cx')))");

        Assert.AreEqual(9, cxs.Length);
        var sorted = cxs.OrderBy(v => v).ToArray();
        CollectionAssert.AreEqual(sorted, cxs, "points must be rendered in ascending time order");

        // Uneven spacing: the Aug 2024 → Feb 2025 gap must be wider than Feb → Apr 2024.
        var gapAutumn = sorted[4] - sorted[3];
        var gapSpring = sorted[1] - sorted[0];
        Assert.IsTrue(gapAutumn > gapSpring * 2, $"expected a visibly wider autumn gap ({gapAutumn:F0} vs {gapSpring:F0})");

        await SaveScreenshotAsync(page, "chart-time-axis-section");
    }

    // ── E2E-TIM-2: auto měsíční popisky (en) ────────────────────────────────

    [TestMethod]
    public async Task TimeAxis_AutoLabels_ShowMonths()
    {
        var page = await OpenChartsPageAsync();
        var section = page.Locator("[data-testid='charts-time-axis-section']");
        var labels = await section.Locator(".tm-chart").First.Locator("text.tm-chart__label")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent ?? '')");

        Assert.IsTrue(labels.Length is > 0 and <= 8, $"labels must be thinned to <= 8, got {labels.Length}");
        Assert.IsTrue(labels.Any(l => l.Contains("2024") || l.Contains("2025")), "month labels must include the year");
    }

    // ── E2E-TIM-3: vlastní formát + explicitní rozsah osy ───────────────────

    [TestMethod]
    public async Task TimeAxis_CustomFormatAndRange_AreApplied()
    {
        var page = await OpenChartsPageAsync();
        var section = page.Locator("[data-testid='charts-time-axis-section']");
        var lineChart = section.Locator(".tm-chart").Nth(1);

        var labels = await lineChart.Locator("text.tm-chart__label")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent ?? '')");
        Assert.IsTrue(labels.All(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d{4}-\d{2}$")),
            $"labels must use the custom yyyy-MM format: {string.Join(", ", labels)}");

        // Explicit range Jan 2024 – Dec 2025: the first data point (Feb 2024) must NOT sit at the axis start.
        var cxs = await lineChart.Locator("circle.tm-chart__point")
            .EvaluateAllAsync<double[]>("els => els.map(e => parseFloat(e.getAttribute('cx')))");
        Assert.IsTrue(cxs.Min() > 50 + 5, "with an explicit axis min, the first point must sit inside the axis");
    }

    // ── E2E-TIM-4: tooltip s plným datem ────────────────────────────────────

    [TestMethod]
    public async Task TimeAxis_Tooltip_ShowsFullDate()
    {
        var page = await OpenChartsPageAsync();
        var section = page.Locator("[data-testid='charts-time-axis-section']");
        var areaChart = section.Locator(".tm-chart").First;
        await section.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);

        await areaChart.Locator("circle.tm-chart__point").First.HoverAsync();
        var tooltip = areaChart.Locator("[data-testid='chart-tooltip']");
        await Assertions.Expect(tooltip).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        var text = await tooltip.InnerTextAsync();
        StringAssert.Matches(text, new System.Text.RegularExpressions.Regex(@"\d{1,2}/\d{1,2}/\d{4}"),
            $"tooltip must contain the full en-US date, got '{text}'");
        await SaveScreenshotAsync(page, "chart-time-axis-tooltip");
    }

    // ── E2E-TIM-5: axe-core a11y scan sekce časové osy ──────────────────────

    [TestMethod]
    public async Task TimeAxis_Accessibility_NoCriticalOrSeriousViolations()
    {
        var page = await OpenChartsPageAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });

        var violations = await page.EvaluateAsync<string[]>(
            """
            async () => {
                const host = document.querySelector("[data-testid='charts-time-axis-section']") || document.body;
                // axe samples the RESOLVED colours: a finite CSS animation/transition still running (the demo's fade-in, a colour
                // transition, the segmented indicator) paints a blended, low-contrast frame. Wait for them (looping ones,
                // like the skeleton shimmer, never finish and are not waited for).
                await Promise.all(document.getAnimations()
                    .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                    .map(a => a.finished.catch(() => { })));
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help}`);
            }
            """);

        Assert.AreEqual(0, violations.Length,
            $"Time axis section must have no critical/serious a11y violations: {string.Join(" | ", violations)}");
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "charts");
        Directory.CreateDirectory(dir);
        var section = page.Locator("[data-testid='charts-time-axis-section']");
        await section.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png") });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
