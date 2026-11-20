using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Playwright coverage for vertical and horizontal TmChart stacked bars on the WASM demo,
/// including screenshots of the normal state and the interactive-legend edge case.
/// </summary>
[TestClass]
public sealed class TmChartStackedBarE2ETests : WasmTestBase
{
    private const string StackedSection = "[data-testid='charts-demo-stacked']";

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
        await page.Locator($"{StackedSection} rect.tm-chart__bar").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 90_000 });
        await page.Locator(StackedSection).ScrollIntoViewIfNeededAsync();
        return new DemoPageHandle(page, errors);
    }

    [TestMethod]
    [TestCategory("WASM")]
    [TestCategory("Smoke")]
    public async Task StackedBars_RenderAccumulatedGeometry_ValuesAndClick()
    {
        var handle = await OpenChartsPageAsync();
        var page = handle.Page;
        var charts = page.Locator($"{StackedSection} .tm-chart");
        var verticalBars = charts.Nth(0).Locator("rect.tm-chart__bar");
        var horizontalBars = charts.Nth(1).Locator("rect.tm-chart__bar");

        Assert.AreEqual(12, await verticalBars.CountAsync());
        Assert.AreEqual(12, await horizontalBars.CountAsync());
        Assert.AreEqual(12, await charts.Nth(0).Locator("text.tm-chart__value").CountAsync());
        Assert.AreEqual(12, await charts.Nth(1).Locator("text.tm-chart__value").CountAsync());

        var verticalBottom = await BoxAsync(verticalBars.Nth(0));
        var verticalTop = await BoxAsync(verticalBars.Nth(1));
        Assert.AreEqual(verticalBottom.X, verticalTop.X, 0.02);
        Assert.AreEqual(verticalBottom.Y, verticalTop.Y + verticalTop.Height, 0.02);

        var horizontalLeft = await BoxAsync(horizontalBars.Nth(0));
        var horizontalRight = await BoxAsync(horizontalBars.Nth(1));
        Assert.AreEqual(horizontalLeft.Y, horizontalRight.Y, 0.02);
        Assert.AreEqual(horizontalLeft.X + horizontalLeft.Width, horizontalRight.X, 0.02);

        await verticalBars.Nth(5).ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='charts-demo-clicked']"))
            .ToContainTextAsync("Feb", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        await SaveSectionScreenshotAsync(page, "stacked-bars");
        AssertNoBlazorErrors(handle);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task StackedBars_LegendToggle_RemovesOneDatasetFromEveryStack()
    {
        var handle = await OpenChartsPageAsync();
        var page = handle.Page;
        var charts = page.Locator($"{StackedSection} .tm-chart");
        var vertical = charts.Nth(0);

        await vertical.Locator("[data-testid='chart-legend-0']").ClickAsync();

        await Assertions.Expect(vertical.Locator("rect.tm-chart__bar"))
            .ToHaveCountAsync(8, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Assertions.Expect(vertical.Locator(".tm-chart__legend-item--hidden"))
            .ToHaveCountAsync(1);

        await SaveSectionScreenshotAsync(page, "edge-stacked-series-hidden");
        AssertNoBlazorErrors(handle);
    }

    private static async Task<(double X, double Y, double Width, double Height)> BoxAsync(ILocator locator)
    {
        static double Value(string? attribute)
            => double.Parse(attribute ?? throw new InvalidOperationException("SVG geometry attribute is missing."),
                CultureInfo.InvariantCulture);

        var attributes = await Task.WhenAll(
            locator.GetAttributeAsync("x"),
            locator.GetAttributeAsync("y"),
            locator.GetAttributeAsync("width"),
            locator.GetAttributeAsync("height"));

        return (Value(attributes[0]), Value(attributes[1]), Value(attributes[2]), Value(attributes[3]));
    }

    private static void AssertNoBlazorErrors(DemoPageHandle handle)
        => Assert.AreEqual(0, handle.Errors.Count,
            "The page raised unhandled exceptions: " + string.Join(" | ", handle.Errors));

    private static async Task SaveSectionScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "charts-stacked");
        Directory.CreateDirectory(directory);
        await page.Locator(StackedSection).ScreenshotAsync(new LocatorScreenshotOptions
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
