using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Final light/dark visual coverage for the four chart additions in the TmChart plan:
/// vertical and horizontal stacked bars, multi-series area, and waterfall.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmChartFinalVisualE2ETests : WasmTestBase
{
    private static readonly (string TestId, string FileName)[] Cards =
    [
        ("chart-stacked-bar", "stacked-bar"),
        ("chart-stacked-horizontal-bar", "stacked-horizontal-bar"),
        ("chart-area-balance", "area-balance"),
        ("chart-waterfall", "waterfall")
    ];

    private sealed record DemoPageHandle(IPage Page, List<string> Errors);

    [TestMethod]
    [TestCategory("Smoke")]
    public async Task NewChartShowcase_IsReadableInLightAndDarkThemes()
    {
        var handle = await OpenChartsPageAsync();
        var page = handle.Page;

        var stacked = page.Locator("[data-testid='chart-stacked-bar'] .tm-chart");
        var horizontal = page.Locator("[data-testid='chart-stacked-horizontal-bar'] .tm-chart");
        var area = page.Locator("[data-testid='chart-area-balance'] .tm-chart");
        var waterfall = page.Locator("[data-testid='chart-waterfall'] .tm-chart");

        Assert.AreEqual(12, await stacked.Locator("rect.tm-chart__bar").CountAsync());
        Assert.AreEqual(12, await horizontal.Locator("rect.tm-chart__bar").CountAsync());
        Assert.AreEqual(2, await area.Locator("path.tm-chart__area").CountAsync());
        Assert.AreEqual(6, await waterfall.Locator("rect.tm-chart__bar").CountAsync());
        Assert.AreEqual(5, await waterfall.Locator("line.tm-chart__waterfall-connector").CountAsync());

        await CaptureCardsAsync(page, "light");

        await page.Locator("button[aria-label='Switch to dark mode']:visible").ClickAsync();
        await page.Locator("[data-theme='dark']").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.WaitForTimeoutAsync(400);

        var fills = await waterfall.Locator(
                "rect.tm-chart__bar--positive, rect.tm-chart__bar--negative, rect.tm-chart__bar--total")
            .EvaluateAllAsync<string[]>("elements => elements.map(element => getComputedStyle(element).fill)");
        Assert.AreEqual(3, fills.Distinct().Count(),
            "Dark mode must retain distinct increase, decrease and total fills. Actual: "
            + string.Join(", ", fills));

        var labelColor = await area.Locator("text.tm-chart__label").First
            .EvaluateAsync<string>("element => getComputedStyle(element).fill");
        Assert.AreNotEqual("rgb(0, 0, 0)", labelColor,
            "Area labels must use the dark semantic text token rather than black.");

        await CaptureCardsAsync(page, "dark");
        Assert.AreEqual(0, handle.Errors.Count,
            "The page raised unhandled exceptions: " + string.Join(" | ", handle.Errors));
    }

    private async Task<DemoPageHandle> OpenChartsPageAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);

        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && message.Text.Contains("Unhandled exception"))
            {
                errors.Add(message.Text);
            }
        };

        await GotoAppReadyAsync(page, $"{BaseUrl}/charts");
        await page.Locator("[data-testid='chart-waterfall'] rect.tm-chart__bar").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 90_000 });
        await page.WaitForFunctionAsync(
            """
            () => {
                const hasWaterfallRule = sheet => {
                    try {
                        for (const rule of sheet.cssRules) {
                            if (rule.selectorText === '.tm-chart__bar--positive') return true;
                            if (rule.styleSheet && hasWaterfallRule(rule.styleSheet)) return true;
                        }
                    } catch {
                        return false;
                    }
                    return false;
                };
                return [...document.styleSheets].some(hasWaterfallRule);
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

        return new DemoPageHandle(page, errors);
    }

    private static async Task CaptureCardsAsync(IPage page, string theme)
    {
        var directory = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "charts-plan-final");
        Directory.CreateDirectory(directory);

        foreach (var (testId, fileName) in Cards)
        {
            var card = page.Locator($"[data-testid='{testId}']");
            await card.ScrollIntoViewIfNeededAsync();
            await page.WaitForTimeoutAsync(200);
            await card.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = Path.Combine(directory, $"{fileName}-{theme}.png")
            });
        }
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
