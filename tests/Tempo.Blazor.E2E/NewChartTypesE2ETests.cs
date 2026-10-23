using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for the Fáze N8 chart types (Funnel, Heatmap, Treemap) on the /charts demo page
/// (WASM demo at 7106): rendering with realistic data, hover tooltips, click events
/// surfacing the data point, and legend toggles (edge cases). Screenshots land in
/// <c>__screenshots__/charts-new-types/</c>.
/// </summary>
[TestClass]
public class NewChartTypesE2ETests : WasmTestBase
{
    private const string DemoPage = "/charts";

    private sealed record DemoPageHandle(IPage Page, List<string> Errors);

    private async Task<DemoPageHandle> OpenPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);

        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error" && msg.Text.Contains("Unhandled exception"))
            {
                errors.Add(msg.Text);
            }
        };

        await GotoAppReadyAsync(page, $"{BaseUrl}{DemoPage}");

        await page.Locator("[data-testid='charts-demo-funnel'] .tm-chart__funnel-segment").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 90000 });
        return new DemoPageHandle(page, errors);
    }

    private static void AssertNoBlazorErrors(DemoPageHandle handle)
        => Assert.AreEqual(0, handle.Errors.Count,
            "The page raised unhandled exceptions: " + string.Join(" | ", handle.Errors));

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Funnel_RendersStages_TooltipAndClick()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var section = page.Locator("[data-testid='charts-demo-funnel']");
        await section.ScrollIntoViewIfNeededAsync();

        var segments = section.Locator(".tm-chart__funnel-segment");
        Assert.AreEqual(5, await segments.CountAsync());
        StringAssert.Contains(await section.InnerTextAsync(), "%");   // conversion percentages

        await segments.Nth(1).HoverAsync();
        var tooltip = section.Locator(".tm-chart__tooltip");
        await tooltip.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        StringAssert.Contains(await tooltip.InnerTextAsync(), "Konzultace");
        await SaveScreenshotAsync(page, "funnel");

        await segments.Nth(2).ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='charts-demo-clicked']"))
            .ToContainTextAsync("Nabídka", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        AssertNoBlazorErrors(handle);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Heatmap_RendersMatrix_TooltipAndRowToggleEdge()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var section = page.Locator("[data-testid='charts-demo-heatmap']");
        await section.ScrollIntoViewIfNeededAsync();

        var cells = section.Locator(".tm-chart__heatmap-cell");
        Assert.AreEqual(25, await cells.CountAsync());   // 5 days × 5 dayparts

        await cells.Nth(7).HoverAsync();
        var tooltip = section.Locator(".tm-chart__tooltip");
        await tooltip.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        StringAssert.Contains(await tooltip.InnerTextAsync(), "Úterý");
        await SaveScreenshotAsync(page, "heatmap");

        // Edge: toggling a day drops its whole row.
        await section.Locator("[data-testid='chart-legend-0']").ClickAsync();
        await Assertions.Expect(cells).ToHaveCountAsync(20, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "edge-heatmap-row-toggle");
        AssertNoBlazorErrors(handle);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Treemap_RendersHierarchy_ClickAndSubtreeToggleEdge()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var section = page.Locator("[data-testid='charts-demo-treemap']");
        await section.ScrollIntoViewIfNeededAsync();

        Assert.AreEqual(3, await section.Locator(".tm-chart__treemap-group").CountAsync());
        var tiles = section.Locator(".tm-chart__treemap-tile");
        Assert.AreEqual(8, await tiles.CountAsync());
        await SaveScreenshotAsync(page, "treemap");

        var novak = section.Locator(".tm-chart__treemap-tile[data-path='Sporná agenda / Novák a syn']");
        await novak.ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='charts-demo-clicked']"))
            .ToContainTextAsync("Novák a syn", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

        // Edge: toggling a practice area drops its whole subtree.
        await section.Locator("[data-testid='chart-legend-2']").ClickAsync();
        await Assertions.Expect(tiles).ToHaveCountAsync(5, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "edge-treemap-subtree-toggle");
        AssertNoBlazorErrors(handle);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "charts-new-types");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, $"{fileName}.png"),
            FullPage = false
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

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
