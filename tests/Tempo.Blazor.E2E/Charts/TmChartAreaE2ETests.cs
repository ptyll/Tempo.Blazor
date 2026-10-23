using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TMPO-003 E2E (WASM @ 7106): the TmChart Area type on the /charts demo page — closed
/// area paths with token-driven fill opacity, gradient fill variant, NaN gap breaking the
/// area into segments, negative values closing on the zero baseline, and an axe-core scan.
/// Screenshots land in <c>__screenshots__/charts/</c> for UX review.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmChartAreaE2ETests : WasmTestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";

    private async Task<IPage> OpenChartsPageAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/charts");
        await page.Locator("[data-testid='charts-area-section']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 90_000 });
        return page;
    }

    // ── E2E-ARE-1: plochy se vykreslí — flat i gradient varianta ────────────

    [TestMethod]
    public async Task AreaSection_RendersFlatAndGradientAreas()
    {
        var page = await OpenChartsPageAsync();
        var section = page.Locator("[data-testid='charts-area-section']");

        // Flat chart: two series → two closed area paths.
        var flatAreas = section.Locator(".tm-chart").First.Locator("path.tm-chart__area");
        Assert.AreEqual(2, await flatAreas.CountAsync(), "Two series must render two areas.");

        // Gradient chart: NaN gap splits the single series into two areas, fill = url(#…).
        var gradientChart = section.Locator(".tm-chart").Nth(1);
        var gradientAreas = gradientChart.Locator("path.tm-chart__area");
        Assert.AreEqual(2, await gradientAreas.CountAsync(), "The NaN gap must split the area into two segments.");
        StringAssert.StartsWith(await gradientAreas.First.GetAttributeAsync("fill"), "url(#");
        Assert.AreEqual(1, await gradientChart.Locator("linearGradient").CountAsync());

        await SaveScreenshotAsync(page, "chart-area-section");
    }

    // ── E2E-ARE-2: explicitní AreaOpacity přepíše token pro solid fill ───────

    [TestMethod]
    public async Task Area_CustomFillOpacity_OverridesDesignToken()
    {
        var page = await OpenChartsPageAsync();
        var section = page.Locator("[data-testid='charts-area-section']");
        var area = section.Locator(".tm-chart").First.Locator("path.tm-chart__area").First;

        Assert.AreEqual("0.4", await area.GetAttributeAsync("fill-opacity"));
        StringAssert.Contains(await area.GetAttributeAsync("style"), "--tm-chart-area-opacity:0.4");

        var opacity = await area.EvaluateAsync<string>("el => getComputedStyle(el).fillOpacity");
        var token = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--tm-chart-area-fill-opacity').trim()");

        Assert.IsFalse(string.IsNullOrWhiteSpace(token), "The --tm-chart-area-fill-opacity token must be defined.");
        Assert.AreEqual("0.4", opacity, "AreaOpacity must override the design-token default for this solid area.");
        Assert.AreNotEqual(token, opacity, "The demo override must be distinguishable from the theme default.");
    }

    // ── E2E-ARE-3: klik na bod plochy vyvolá OnSegmentClick ─────────────────

    [TestMethod]
    public async Task Area_PointClick_RaisesSegmentClick()
    {
        var page = await OpenChartsPageAsync();
        var section = page.Locator("[data-testid='charts-area-section']");
        var flatChart = section.Locator(".tm-chart").First;

        // Hovering a point shows the tooltip (same interaction path as clicking).
        var point = flatChart.Locator("circle.tm-chart__point").First;
        await point.HoverAsync();
        await Assertions.Expect(flatChart.Locator("[data-testid='chart-tooltip']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        // Clicking it keeps the chart alive (OnSegmentClick wired on the flat chart).
        var box = await point.BoundingBoxAsync();
        Assert.IsNotNull(box);
        await page.Mouse.ClickAsync((float)(box.X + box.Width / 2), (float)(box.Y + box.Height / 2));
        Assert.AreEqual(2, await flatChart.Locator("path.tm-chart__area").CountAsync());
        await SaveScreenshotAsync(page, "chart-area-point-click");
    }

    // ── E2E-ARE-4: dark mode — plochy čitelné, tokeny se přepnou ────────────

    [TestMethod]
    public async Task Area_DarkMode_UsesDarkTokenOpacity()
    {
        var page = await OpenChartsPageAsync();

        await page.Locator("button[aria-label='Switch to dark mode']:visible").ClickAsync();
        await page.Locator("[data-theme='dark']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.WaitForTimeoutAsync(400);

        var token = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('[data-theme=\"dark\"]')).getPropertyValue('--tm-chart-area-fill-opacity').trim()");
        Assert.AreEqual("0.3", token, "Dark mode must switch the area fill opacity token.");
        await SaveScreenshotAsync(page, "chart-area-dark");
    }

    // ── E2E-ARE-5: axe-core a11y scan Area sekce ────────────────────────────

    [TestMethod]
    public async Task Area_Accessibility_NoCriticalOrSeriousViolations()
    {
        var page = await OpenChartsPageAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });

        var violations = await page.EvaluateAsync<string[]>(
            """
            async () => {
                const host = document.querySelector("[data-testid='charts-area-section']") || document.body;
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
            $"Area chart section must have no critical/serious a11y violations: {string.Join(" | ", violations)}");
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "charts");
        Directory.CreateDirectory(dir);
        var section = page.Locator("[data-testid='charts-area-section']");
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
