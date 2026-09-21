using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for modeling editor phase M8.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingEditorM8E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Modeling editor route shows loading and resolves to the four-panel loaded shell")]
    public async Task ModelingEditor_LoadsDemoProvider_WithVisiblePanels()
    {
        var page = await OpenPageWithoutInitialReadyWaitAsync($"{ModelingEditorUrl}?delay=500");

        var loading = page.Locator("[data-testid='modeling-editor-loading']");
        // The loading element only exists once Blazor has booted and the component's first load
        // started, so the observe window has to cover WASM boot, not just the ?delay=500 provider.
        await loading.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await loading.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });

        await WaitForLoadedEditorAsync(page);

        await ExpectVisibleAsync(page, "[data-testid='modeling-model-tree-panel']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-preview-panel']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-inspector-panel']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-status-strip']");
    }

    [TestMethod]
    [Description("Unknown provider renders an explicit empty state without a blank page")]
    public async Task ModelingEditor_UnknownProvider_ShowsEmptyState()
    {
        var page = await OpenModelingPageAsync("?provider=neexistujici");

        var empty = page.Locator("[data-testid='modeling-editor-empty']");
        await empty.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var text = await empty.TextContentAsync() ?? string.Empty;
        StringAssert.Contains(text, "No modeling provider available");
        Assert.AreEqual("empty", await page.Locator("[data-testid='modeling-editor']").GetAttributeAsync("data-state"));
    }

    [TestMethod]
    [Description("Throwing provider renders an error state and does not stay frozen in loading")]
    public async Task ModelingEditor_ThrowingProvider_ShowsErrorState()
    {
        var page = await OpenModelingPageAsync("?scenario=error");

        var error = page.Locator("[data-testid='modeling-editor-error']");
        await error.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var text = await error.TextContentAsync() ?? string.Empty;
        StringAssert.Contains(text, "Model could not be loaded");
        Assert.AreEqual("error", await page.Locator("[data-testid='modeling-editor']").GetAttributeAsync("data-state"));
    }

    [TestMethod]
    [Description("Modeling editor remains usable at 900px viewport and simulated 150 percent zoom")]
    public async Task ModelingEditor_ResponsiveAndZoomed_DoesNotOverflow()
    {
        var page = await OpenModelingPageAsync();

        await page.SetViewportSizeAsync(900, 720);
        await page.ReloadAsync();
        await WaitForAppReadyAsync(page);
        await WaitForLoadedEditorAsync(page);

        Assert.IsTrue(await HasNoHorizontalOverflowAsync(page), "Modeling editor should not create horizontal overflow at 900px.");

        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setPageScaleFactor", new Dictionary<string, object>
        {
            ["pageScaleFactor"] = 1.5
        });
        await page.WaitForTimeoutAsync(300);

        await ExpectVisibleAsync(page, "[data-testid='modeling-editor']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-preview-panel']");
        Assert.IsTrue(await HasNoHorizontalOverflowAsync(page), "Modeling editor should remain usable at simulated 150% zoom.");
    }

    [TestMethod]
    [Description("Captures required M8 screenshots for loaded, dark, error and empty states")]
    public async Task ModelingEditor_CapturesM8Screenshots()
    {
        var loaded = await OpenModelingPageAsync();
        await WaitForLoadedEditorAsync(loaded);
        await TakeScreenshotAsync(loaded, "modeling-editor-loaded-light");

        await loaded.EvaluateAsync(
            """
            () => {
                document.documentElement.setAttribute('data-theme', 'dark');
                document.documentElement.classList.add('tm-dark', 'dark');
                document.body.classList.add('tm-dark', 'dark');
            }
            """);
        await loaded.WaitForTimeoutAsync(300);
        await TakeScreenshotAsync(loaded, "modeling-editor-loaded-dark");

        var error = await OpenModelingPageAsync("?scenario=error");
        await error.Locator("[data-testid='modeling-editor-error']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await TakeScreenshotAsync(error, "modeling-editor-error-light");

        var empty = await OpenModelingPageAsync("?provider=neexistujici");
        await empty.Locator("[data-testid='modeling-editor-empty']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await TakeScreenshotAsync(empty, "modeling-editor-empty-light");
    }

    private async Task<IPage> OpenModelingPageAsync(string query = "")
    {
        var page = await OpenPageWithoutInitialReadyWaitAsync($"{ModelingEditorUrl}{query}");
        await WaitForAppReadyAsync(page);
        await ExpectVisibleAsync(page, "[data-testid='modeling-editor']");
        return page;
    }

    private async Task<IPage> OpenPageWithoutInitialReadyWaitAsync(string pathAndQuery)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{pathAndQuery}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        return page;
    }

    private static async Task WaitForLoadedEditorAsync(IPage page)
    {
        var root = page.Locator("[data-testid='modeling-editor'][data-state='loaded']");
        await root.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector)
    {
        await page.Locator(selector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
    }

    private static Task<bool> HasNoHorizontalOverflowAsync(IPage page) =>
        page.EvaluateAsync<bool>(
            """
            () => {
                const doc = document.documentElement;
                const body = document.body;
                return Math.max(doc.scrollWidth, body.scrollWidth) <= window.innerWidth + 2;
            }
            """);
}
