using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 98c80bc3, phase 10b — the /charts "Responsive width, nice axis and culture" section on the WASM demo
/// (@ 7106): the opt-in <c>Responsive</c> chart fills a wide card where the default one keeps its 600 x 400
/// viewBox, the value axis steps on 1/2/5 ticks, the numbers follow the page culture (cs → a decimal comma) and
/// 24 hourly labels never touch. Screenshots (light + dark, wide + phone) land in
/// <c>__screenshots__/charts-f10b/</c> for the UX review.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmChartResponsiveE2ETests : WasmTestBase
{
    private const string Section = "[data-testid='charts-demo-responsive']";
    private const string Off = "[data-testid='charts-demo-responsive-off']";
    private const string On = "[data-testid='charts-demo-responsive-on']";

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1000, string culture = "en")
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync($"localStorage.setItem('tm-demo-culture', '{culture}');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await GotoAppReadyAsync(page, $"{BaseUrl}/charts");
        await page.WaitForSelectorAsync(Section, new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.Locator(Section).ScrollIntoViewIfNeededAsync();
        // The measuring module reports the plot box a frame after the first render.
        await page.WaitForFunctionAsync(
            "sel => { const s = document.querySelector(sel + ' svg'); return s && s.getAttribute('viewBox') !== '0 0 600 400'; }",
            On, new PageWaitForFunctionOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    private static async Task<double[]> ViewBoxAsync(IPage page, string card)
        => await page.Locator($"{card} svg").First.EvaluateAsync<double[]>(
            "s => s.getAttribute('viewBox').split(' ').map(Number)");

    private static async Task<string[]> TextsAsync(IPage page, string card, string cls)
        => await page.Locator($"{card} text.{cls}")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent ?? '')");

    private static string ShotDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx"))) dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "charts-f10b");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task ShotAsync(IPage page, string name)
    {
        await page.Locator(Section).ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);
        await page.Locator(Section).ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(ShotDir(), $"{name}.png") });
    }

    private static async Task ToggleDarkAsync(IPage page)
    {
        await page.Locator("button[aria-label='Switch to dark mode']:visible").ClickAsync();
        await page.Locator("[data-theme='dark']").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.WaitForTimeoutAsync(400);
    }

    [TestMethod]
    [TestCategory("Smoke")]
    public async Task Responsive_FillsTheWideCard_WhereTheDefaultKeepsItsFixedViewBox()
    {
        var page = await OpenAsync();

        var off = await ViewBoxAsync(page, Off);
        var on = await ViewBoxAsync(page, On);
        CollectionAssert.AreEqual(new[] { 0d, 0d, 600d, 400d }, off, "the default chart keeps the pre-2.12.0 viewBox");

        var card = await page.Locator($"{On} .tm-chart__plot").BoundingBoxAsync();
        Assert.IsNotNull(card);
        Assert.IsTrue(Math.Abs(on[2] - card!.Width) <= 1.5, $"the viewBox width ({on[2]}) equals the plot width ({card.Width})");
        Assert.IsTrue(on[2] > 900, $"a 1440 px page gives the card a wide plot, got {on[2]}");

        // The drawn polyline reaches the right edge of the plot instead of stopping at x = 580.
        var maxX = await page.Locator($"{On} circle.tm-chart__point")
            .EvaluateAllAsync<double>("els => Math.max(...els.map(e => parseFloat(e.getAttribute('cx'))))");
        Assert.IsTrue(maxX > on[2] - 60, $"the last point ({maxX}) sits near the right edge of a {on[2]} unit plot");

        await ShotAsync(page, "responsive-light-1440");
        await ToggleDarkAsync(page);
        await ShotAsync(page, "responsive-dark-1440");
    }

    [TestMethod]
    public async Task Responsive_FollowsAViewportResize()
    {
        var page = await OpenAsync();
        var wide = (await ViewBoxAsync(page, On))[2];

        await page.SetViewportSizeAsync(700, 1000);
        await page.WaitForFunctionAsync(
            "([sel, w]) => Number(document.querySelector(sel + ' svg').getAttribute('viewBox').split(' ')[2]) < w",
            new object[] { On, wide - 100 }, new PageWaitForFunctionOptions { Timeout = 15000 });

        var narrow = (await ViewBoxAsync(page, On))[2];
        Assert.IsTrue(narrow < wide - 100, $"the viewBox shrinks with the card ({wide} -> {narrow})");
        var labels = (await TextsAsync(page, On, "tm-chart__label")).Length;
        Assert.IsTrue(labels is > 2 and <= 24, $"labels stay thinned to the narrower plot, got {labels}");
    }

    [TestMethod]
    public async Task NiceAxis_PrintsOneTwoFiveTicks_WithCultureSeparator()
    {
        var page = await OpenAsync(culture: "en");
        var ticks = await TextsAsync(page, Off, "tm-chart__axis-label");
        CollectionAssert.AreEqual(new[] { "0", "5", "10", "15", "20" }, ticks, "the default chart: 6.1..16.1 -> 0..20 in steps of 5");

        // The responsive card sets ValueFormat="0.0".
        var formatted = await TextsAsync(page, On, "tm-chart__axis-label");
        CollectionAssert.AreEqual(new[] { "0.0", "5.0", "10.0", "15.0", "20.0" }, formatted);
    }

    [TestMethod]
    public async Task Czech_UsesADecimalComma_InAxisAndTooltip_ButInvariantCoordinates()
    {
        var page = await OpenAsync(culture: "cs");

        var ticks = await TextsAsync(page, On, "tm-chart__axis-label");
        CollectionAssert.AreEqual(new[] { "0,0", "5,0", "10,0", "15,0", "20,0" }, ticks);

        var coordinates = await page.Locator($"{On} circle.tm-chart__point")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('cx') + ',' + e.getAttribute('cy'))");
        Assert.IsTrue(coordinates.All(c => c.Split(',').Length == 2), "SVG coordinates carry no decimal comma");

        await page.Locator($"{On} circle.tm-chart__point").Nth(11).HoverAsync();
        var tooltip = page.Locator($"{On} [data-testid='chart-tooltip']");
        await Assertions.Expect(tooltip).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        StringAssert.Contains(await tooltip.InnerTextAsync(), "16,1");

        await ShotAsync(page, "responsive-czech-light-1440");
    }

    [TestMethod]
    public async Task HourlyLabels_NeverTouch_OnTheDefaultChart_AndAllShowOnTheWideOne()
    {
        var page = await OpenAsync();

        async Task<double[]> LabelXs(string card) => await page.Locator($"{card} text.tm-chart__label")
            .EvaluateAllAsync<double[]>("els => els.map(e => parseFloat(e.getAttribute('x')))");

        var fixedXs = await LabelXs(Off);
        Assert.IsTrue(fixedXs.Length < 24, $"24 hourly labels are thinned in the 600-unit plot, got {fixedXs.Length}");
        var gaps = fixedXs.Zip(fixedXs.Skip(1), (a, b) => b - a).ToArray();
        Assert.IsTrue(gaps.All(g => g >= 5 * 6.5), $"neighbouring labels keep their own width: {string.Join(", ", gaps.Select(g => g.ToString("F0", CultureInfo.InvariantCulture)))}");

        var wideXs = await LabelXs(On);
        Assert.AreEqual(24, wideXs.Length, "the wide responsive plot has room for every hourly label");

        // Rendered boxes: no two label text boxes overlap on screen.
        var boxes = await page.Locator($"{Off} text.tm-chart__label")
            .EvaluateAllAsync<double[][]>("els => els.map(e => { const r = e.getBoundingClientRect(); return [r.left, r.right]; })");
        for (var i = 1; i < boxes.Length; i++)
        {
            Assert.IsTrue(boxes[i][0] >= boxes[i - 1][1] - 0.5, $"labels {i - 1} and {i} overlap on screen");
        }
    }

    [TestMethod]
    public async Task Phone_390_KeepsTheChartInsideItsCard_LightAndDark()
    {
        var page = await OpenAsync(390, 900);

        var plot = await page.Locator($"{On} .tm-chart__plot").BoundingBoxAsync();
        var card = await page.Locator(On).BoundingBoxAsync();
        Assert.IsTrue(plot!.Width <= card!.Width + 1, "the plot never overflows its card");
        var overflow = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > window.innerWidth + 1");
        Assert.IsFalse(overflow, "no horizontal page scroll at 390 px");

        await ShotAsync(page, "responsive-light-390");
        await ToggleDarkAsync(page);
        await ShotAsync(page, "responsive-dark-390");
    }

    [TestMethod]
    public async Task Section_Accessibility_NoCriticalOrSeriousViolations()
    {
        var page = await OpenAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js" });

        var violations = await page.EvaluateAsync<string[]>(
            """
            async () => {
                const host = document.querySelector("[data-testid='charts-demo-responsive']");
                await Promise.all(document.getAnimations()
                    .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                    .map(a => a.finished.catch(() => { })));
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const result = await axe.run(host, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] }, resultTypes: ['violations'] });
                return result.violations.filter(v => v.impact === 'critical' || v.impact === 'serious').map(v => `${v.impact}: ${v.id} - ${v.help}`);
            }
            """);
        Assert.AreEqual(0, violations.Length, string.Join(" | ", violations));
    }
}
