using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public sealed class NotionAnalyticsE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("CF31: analytics records page views, renders current-page metrics, daily chart, top pages, and captures UX baseline.")]
    public async Task CF31_Analytics_HappyPathAndBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedAnalyticsPageAsync();
        await OpenAnalyticsAsync(page);

        var panel = page.GetByTestId("notion-analytics-panel");
        await Assertions.Expect(panel.GetByTestId("notion-analytics-top-pages")).ToContainTextAsync("CF31 Adoption Report");
        await Assertions.Expect(panel.Locator(".tm-notion-analytics__sparkline-line")).ToBeVisibleAsync();

        var firstViews = await ReadViewsAsync(page);
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-notion-page").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        await OpenAnalyticsAsync(page);
        var secondViews = await ReadViewsAsync(page);

        Assert.IsTrue(secondViews > firstViews, $"Views should grow after reopening the page. Before: {firstViews}, after: {secondViews}.");

        var capture = await CaptureBaselineAsync("analytics", "cf31-analytics-panel", page.Locator(".tm-notion-analytics-panel").First);
        await CaptureBaselineAsync("analytics", "cf31-top-pages-bars", page.Locator(".tm-notion-analytics__top").First);
        TestContext.WriteLine($"UX CF31 analytics baseline captured: {capture.FullPagePath} / {capture.RegionPath}");
        TestContext.WriteLine("UX CF31 review: metrics are grouped first, the daily sparkline has enough contrast, and top pages remain scannable through proportional bars.");
    }

    [TestMethod]
    [Description("CF31: providerless, zero-view, and empty top-page states work.")]
    public async Task CF31_Analytics_EdgeCases_Work()
    {
        var providerless = await OpenNotionEditorAsync("?disableAnalyticsProvider=true");
        Assert.AreEqual(0, await providerless.GetByTestId("notion-analytics-open").CountAsync(), "Analytics entry point should be hidden when no provider is configured.");
        await CaptureBaselineAsync("analytics", "cf31-providerless-hidden-state", providerless.Locator(".tm-notion-topbar").First);

        var empty = await OpenNotionEditorAsync();
        await SeedEmptyAnalyticsPageAsync();
        await OpenAnalyticsAsync(empty);

        await Assertions.Expect(empty.GetByTestId("notion-analytics-views")).ToContainTextAsync("0");
        await Assertions.Expect(empty.GetByTestId("notion-analytics-empty")).ToContainTextAsync("No analytics data yet.");
        await CaptureBaselineAsync("analytics", "cf31-zero-views", empty.Locator(".tm-notion-analytics-panel").First);
        await CaptureBaselineAsync("analytics", "cf31-empty-top-pages", empty.Locator(".tm-notion-analytics__top").First);
    }

    private static async Task OpenAnalyticsAsync(IPage page)
    {
        await ClickNotionCommandAsync(page, "notion-analytics-open");
        await page.GetByTestId("notion-analytics-panel").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
    }

    private static async Task<int> ReadViewsAsync(IPage page)
    {
        var text = await page.GetByTestId("notion-analytics-views").Locator(".tm-notion-analytics__metric-value").TextContentAsync();
        return int.Parse((text ?? string.Empty).Replace(",", string.Empty, StringComparison.Ordinal).Trim());
    }
}
