using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for modeling source panel phase M9.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingSourcePanelM9E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Source panel shows demo provider source system and version")]
    public async Task SourcePanel_ShowsDemoProviderMetadata()
    {
        var page = await OpenLoadedModelingPageAsync();

        await ExpectVisibleAsync(page, "[data-testid='modeling-source-panel']");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-source-system']", "Tempo.Blazor Demo");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-source-version']", "2026.06");
    }

    [TestMethod]
    [Description("Load model button triggers a reload with loading feedback")]
    public async Task SourcePanel_LoadButton_ShowsLoadingAndReturnsLoaded()
    {
        var page = await OpenLoadedModelingPageAsync("?delay=500");
        var initialLoadCount = await GetLoadCountAsync(page);

        await page.Locator("[data-testid='modeling-source-load-button']").ClickAsync();
        await ExpectVisibleAsync(page, "[data-testid='modeling-editor-loading']");
        await WaitForLoadedEditorAsync(page);

        Assert.AreEqual(initialLoadCount + 1, await GetLoadCountAsync(page));
    }

    [TestMethod]
    [Description("Stale source metadata shows a readable warning with icon")]
    public async Task SourcePanel_StaleMetadata_ShowsWarning()
    {
        var page = await OpenLoadedModelingPageAsync("?fresh=false");

        await ExpectVisibleAsync(page, "[data-testid='modeling-source-freshness-warning']");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-source-freshness-warning']", "Source data may be out of date");
        await ExpectVisibleAsync(page, ".tm-modeling-source-panel__warning-icon");
    }

    [TestMethod]
    [Description("Double clicking load model does not complete two reloads")]
    public async Task SourcePanel_DoubleClick_DoesNotReloadTwice()
    {
        var page = await OpenLoadedModelingPageAsync("?delay=500");
        var initialLoadCount = await GetLoadCountAsync(page);

        await page.EvaluateAsync(
            """
            () => {
                const button = document.querySelector("[data-testid='modeling-source-load-button']");
                button?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                button?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
            }
            """);

        await WaitForLoadCountAsync(page, initialLoadCount + 1);

        Assert.AreEqual(initialLoadCount + 1, await GetLoadCountAsync(page));
    }

    [TestMethod]
    [Description("Slow provider keeps loading visible and shows a timeout message")]
    public async Task SourcePanel_SlowProvider_ShowsLongLoadingMessage()
    {
        var page = await OpenPageWithoutInitialReadyWaitAsync("?delay=6000");

        // ?delay=6000 holds the loading state ~6s, but the element can only appear after WASM
        // boot — the default 5s window expires before the first render on a cold runtime.
        await ExpectVisibleAsync(page, "[data-testid='modeling-editor-loading']", timeout: 30_000);
        await ExpectVisibleAsync(page, "[data-testid='modeling-editor-timeout-message']", timeout: 15_000);
        await ExpectVisibleAsync(page, "[data-testid='modeling-editor-loading']");
    }

    [TestMethod]
    [Description("Captures M9 fresh and stale source panel screenshots")]
    public async Task SourcePanel_CapturesM9Screenshots()
    {
        var fresh = await OpenLoadedModelingPageAsync();
        await TakeScreenshotAsync(fresh, "source-panel-fresh");
        await SaveStableScreenshotAsync(fresh, "source-panel-fresh.png");

        var stale = await OpenLoadedModelingPageAsync("?fresh=false");
        await ExpectVisibleAsync(stale, "[data-testid='modeling-source-freshness-warning']");
        await TakeScreenshotAsync(stale, "source-panel-stale");
        await SaveStableScreenshotAsync(stale, "source-panel-stale.png");
    }

    private async Task<IPage> OpenLoadedModelingPageAsync(string query = "")
    {
        var page = await OpenPageWithoutInitialReadyWaitAsync(query);
        await WaitForAppReadyAsync(page);
        await WaitForLoadedEditorAsync(page);
        return page;
    }

    private async Task<IPage> OpenPageWithoutInitialReadyWaitAsync(string query = "")
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        return page;
    }

    private static async Task WaitForLoadedEditorAsync(IPage page)
    {
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 5000)
    {
        await page.Locator(selector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
    }

    private static async Task ExpectTextContainsAsync(IPage page, string selector, string expectedText)
    {
        var text = await page.Locator(selector).TextContentAsync() ?? string.Empty;
        StringAssert.Contains(text, expectedText);
    }

    private static async Task<int> GetLoadCountAsync(IPage page)
    {
        var value = await page.Locator("[data-testid='modeling-editor']").GetAttributeAsync("data-load-count");
        return int.TryParse(value, out var count) ? count : 0;
    }

    private static async Task WaitForLoadCountAsync(IPage page, int expectedCount)
    {
        await page.WaitForFunctionAsync(
            """
            expected => {
                const editor = document.querySelector("[data-testid='modeling-editor']");
                return Number(editor?.getAttribute('data-load-count') ?? '0') === expected;
            }
            """,
            expectedCount,
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingSourcePanelM9E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m9");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the rendered modeling editor.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
