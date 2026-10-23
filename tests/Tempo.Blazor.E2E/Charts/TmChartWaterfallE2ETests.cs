using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Playwright coverage for TmChart Waterfall rendering on the HTTPS WASM demo, including
/// cumulative geometry, interaction, dark theme tokens and below-zero edge-state screenshots.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmChartWaterfallE2ETests : WasmTestBase
{
    private const string SectionSelector = "[data-testid='charts-waterfall-section']";

    private sealed record DemoPageHandle(IPage Page, List<string> Errors);

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

        await page.Locator($"{SectionSelector} rect.tm-chart__bar").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 90_000 });
        await page.WaitForFunctionAsync(
            """
            () => {
                const containsWaterfallRule = sheet => {
                    try {
                        for (const rule of sheet.cssRules) {
                            if (rule.selectorText === '.tm-chart__bar--positive') return true;
                            if (rule.styleSheet && containsWaterfallRule(rule.styleSheet)) return true;
                        }
                    } catch {
                        return false;
                    }
                    return false;
                };
                return [...document.styleSheets].some(containsWaterfallRule);
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        return new DemoPageHandle(page, errors);
    }

    [TestMethod]
    [TestCategory("Smoke")]
    public async Task Waterfall_RendersCumulativeBars_Total_Connectors_AndClick()
    {
        var handle = await OpenChartsPageAsync();
        var page = handle.Page;
        var chart = page.Locator($"{SectionSelector} .tm-chart").Nth(0);
        var bars = chart.Locator("rect.tm-chart__bar");

        Assert.AreEqual(6, await bars.CountAsync());
        Assert.AreEqual(5, await chart.Locator("line.tm-chart__waterfall-connector").CountAsync());
        Assert.AreEqual(6, await chart.Locator("text.tm-chart__value").CountAsync());
        Assert.AreEqual("+90", await chart.Locator("text.tm-chart__value").Nth(1).TextContentAsync());
        Assert.AreEqual("-55", await chart.Locator("text.tm-chart__value").Nth(2).TextContentAsync());

        var opening = await GeometryAsync(bars.Nth(0));
        var sales = await GeometryAsync(bars.Nth(1));
        Assert.AreEqual(opening.Y, sales.Y + sales.Height, 0.02,
            "The +50 delta must begin at the opening bar's cumulative end.");

        await bars.Nth(2).ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='charts-waterfall-clicked']"))
            .ToContainTextAsync("Payroll = -55", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        await SaveSectionScreenshotAsync(page, "waterfall-normal");
        AssertNoBrowserErrors(handle);
    }

    [TestMethod]
    public async Task Waterfall_NegativeDomain_AndDarkTokens_RenderEdgeState()
    {
        var handle = await OpenChartsPageAsync();
        var page = handle.Page;
        var chart = page.Locator($"{SectionSelector} .tm-chart").Nth(1);

        var zeroAxisY = Parse(await chart.Locator("line.tm-chart__axis-zero").GetAttributeAsync("y1"));
        Assert.IsTrue(zeroAxisY > 20 && zeroAxisY < 360,
            $"The zero axis must be inside the plot for a below-zero cumulative path, actual Y={zeroAxisY}.");
        var axisLabels = await chart.Locator("text.tm-chart__axis-label").AllTextContentsAsync();
        Assert.IsTrue(axisLabels.Any(text =>
            double.Parse(text, CultureInfo.InvariantCulture) < 0));

        await page.Locator("button[aria-label='Switch to dark mode']:visible").ClickAsync();
        var darkRoot = page.Locator("[data-theme='dark']").First;
        await darkRoot.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var tokens = await darkRoot.EvaluateAsync<string[]>(
            """
            el => {
                const probe = document.createElement('span');
                el.appendChild(probe);
                const values = ['success', 'danger', 'primary'].map(name => {
                    probe.style.color = `var(--tm-color-${name})`;
                    return getComputedStyle(probe).color;
                });
                probe.remove();
                return values;
            }
            """);
        CollectionAssert.AreEqual(
            new[] { "rgb(74, 222, 128)", "rgb(248, 113, 113)", "rgb(96, 165, 250)" },
            tokens);

        var semanticFills = await chart.Locator(
                "rect.tm-chart__bar--positive, rect.tm-chart__bar--negative, rect.tm-chart__bar--total")
            .EvaluateAllAsync<string[]>("elements => elements.map(el => getComputedStyle(el).fill)");
        Assert.AreEqual(3, semanticFills.Distinct().Count(),
            "Increase, decrease and total bars must resolve to distinct semantic fills. Actual: "
            + string.Join(", ", semanticFills));

        await SaveSectionScreenshotAsync(page, "waterfall-edge-dark");
        AssertNoBrowserErrors(handle);
    }

    private static async Task<(double Y, double Height)> GeometryAsync(ILocator locator)
        => (
            Parse(await locator.GetAttributeAsync("y")),
            Parse(await locator.GetAttributeAsync("height")));

    private static double Parse(string? value)
        => double.Parse(value ?? throw new InvalidOperationException("SVG geometry attribute is missing."),
            CultureInfo.InvariantCulture);

    private static void AssertNoBrowserErrors(DemoPageHandle handle)
        => Assert.AreEqual(0, handle.Errors.Count,
            "The page raised unhandled exceptions: " + string.Join(" | ", handle.Errors));

    private static async Task SaveSectionScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "charts-waterfall");
        Directory.CreateDirectory(directory);
        var section = page.Locator(SectionSelector);
        await section.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);
        await section.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, $"{fileName}.png")
        });
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
