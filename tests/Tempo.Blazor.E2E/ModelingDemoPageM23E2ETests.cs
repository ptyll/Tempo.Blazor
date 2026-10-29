using System.Diagnostics;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for the modeling editor demo route phase M23.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingDemoPageM23E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Demo route loads quickly after WASM boot and supports the full generate/open/close workflow")]
    public async Task DemoRoute_LoadsAndRunsHappyDayWorkflow()
    {
        var context = await CreateEnglishContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}?notation=bpmn&viewpoint=overview", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await WaitForLoadedEditorAsync(page);

        await page.GotoAsync(BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var stopwatch = Stopwatch.StartNew();
        await page.EvaluateAsync("path => Blazor.navigateTo(path)", $"{ModelingEditorUrl}?notation=bpmn&viewpoint=overview");
        await WaitForLoadedEditorAsync(page);
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"The warmed /modeling-editor route should load within 3s, but took {stopwatch.Elapsed.TotalMilliseconds:N0} ms.");
        await AssertNotationOptionsAsync(page);
        Assert.IsTrue(await ReadNumericAttributeAsync(page, "[data-testid='modeling-model-tree']", "data-visible-count") > 0, "Demo provider tree should not be empty.");

        await page.Locator("[data-testid='modeling-generate-diagram-button']").ClickAsync();
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);
        Assert.IsTrue(await ReadNumericAttributeAsync(page, "[data-testid='modeling-diagram-preview']", "data-node-count") > 0, "Generated preview should contain nodes.");

        await SaveStableScreenshotAsync(page, "demo-desktop-light.png");

        await page.Locator("[data-testid='modeling-open-in-editor-button']").ClickAsync();
        await page.Locator("[data-testid='modeling-open-diagram-editor']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.Locator("[data-testid='modeling-open-diagram-editor'] [data-testid='diagram-editor']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await page.Locator("[data-testid='modeling-open-diagram-close']").ClickAsync();
        await page.Locator("[data-testid='modeling-open-diagram-editor']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10000 });
        await page.Locator("[data-testid='modeling-preview-panel']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
    }

    [TestMethod]
    [Description("Dark mode toggles on the demo route without introducing blank or overflowing editor surfaces")]
    public async Task DemoRoute_DarkModeRendersCleanly()
    {
        var page = await OpenLoadedModelingPageAsync();

        // Review fix (phase 5): the /modeling-editor demo page hides the demo sidebar from 1264px
        // of viewport up (the editor needs the full width for the 1200px desktop dock), so the
        // sidebar's toggle is display:none — dispatch the click instead of pointing a cursor at it.
        await page.Locator("[data-testid='theme-toggle']").Last.DispatchEventAsync("click");
        await page.Locator("div[data-theme='dark']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.WaitForTimeoutAsync(300);

        Assert.IsTrue(await HasNoHorizontalOverflowAsync(page), "Dark mode should not introduce horizontal overflow.");
        Assert.IsFalse(await EditorHasTransparentSurfaceAsync(page), "Dark mode editor should retain token-backed surfaces.");
        await SaveStableScreenshotAsync(page, "demo-desktop-dark.png");
    }

    [TestMethod]
    [DataRow(390, 844)]
    [DataRow(320, 640)]
    [Description("Mobile viewport (Fáze 7 band): the tablist panels switch and keep only one panel active at a time, without horizontal overflow")]
    public async Task DemoRoute_MobilePanelsAreSwitchableWithoutOverlap(int width, int height)
    {
        var page = await OpenLoadedTouchModelingPageAsync(width, height);

        await AssertPanelTabsVisibleAsync(page);
        await WaitForEditorAttributeAsync(page, "data-active-panel", "preview");
        Assert.AreEqual(1, await CountVisiblePanelsAsync(page));

        // The tab strip is a real tablist ordered Preview | Tree | Inspector | Issues.
        var tablist = page.Locator("[data-testid='modeling-panel-tabs'] [role='tablist']");
        Assert.AreEqual(1, await tablist.CountAsync(), "the mobile panel strip is a tablist");
        Assert.AreEqual(4, await tablist.Locator("[role='tab']").CountAsync());

        await page.Locator("[data-testid='modeling-panel-tab-tree']").ClickAsync();
        await WaitForEditorAttributeAsync(page, "data-active-panel", "tree");
        Assert.AreEqual(1, await CountVisiblePanelsAsync(page));

        await page.Locator("[data-testid='modeling-panel-tab-inspector']").ClickAsync();
        await WaitForEditorAttributeAsync(page, "data-active-panel", "inspector");
        Assert.AreEqual(1, await CountVisiblePanelsAsync(page));

        await page.Locator("[data-testid='modeling-panel-tab-issues']").ClickAsync();
        await WaitForEditorAttributeAsync(page, "data-active-panel", "issues");
        Assert.AreEqual(1, await CountVisiblePanelsAsync(page));

        Assert.IsTrue(await HasNoHorizontalOverflowAsync(page), "Mobile layout should not create horizontal overflow.");

        await SaveStableScreenshotAsync(page, $"demo-mobile-{width}x{height}.png");
    }

    [TestMethod]
    [Description("Tablet viewport uses the compact panel mode instead of squeezing the desktop grid")]
    public async Task DemoRoute_TabletLayoutUsesCompactPanels()
    {
        var page = await OpenLoadedModelingPageAsync(width: 768, height: 1024);

        await AssertPanelTabsVisibleAsync(page);
        await WaitForEditorAttributeAsync(page, "data-active-panel", "preview");
        Assert.AreEqual(1, await CountVisiblePanelsAsync(page));
        Assert.IsTrue(await HasNoHorizontalOverflowAsync(page), "Tablet layout should stay contained.");

        await SaveStableScreenshotAsync(page, "demo-tablet-768.png");
    }

    [TestMethod]
    [Description("Refreshing the demo route keeps the page loaded without 404 or hydration errors")]
    public async Task DemoRoute_RefreshKeepsRouteLoaded()
    {
        var page = await OpenLoadedModelingPageAsync();

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await WaitForLoadedEditorAsync(page);

        Assert.IsTrue(await ReadNumericAttributeAsync(page, "[data-testid='modeling-model-tree']", "data-visible-count") > 0, "Tree should remain populated after refresh.");
        Assert.IsFalse(await PageContainsFatalRouteTextAsync(page), "Refresh should not render a 404 or hydration failure.");
    }

    private async Task<IPage> OpenLoadedModelingPageAsync(int width = 1280, int height = 720)
    {
        var context = await CreateEnglishContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}?notation=bpmn&viewpoint=overview", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await WaitForLoadedEditorAsync(page);
        return page;
    }

    /// <summary>Loads the demo editor inside a touch-capable context (the mobile band needs
    /// real touch targets, Fáze 7).</summary>
    private async Task<IPage> OpenLoadedTouchModelingPageAsync(int width, int height)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = true,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        await context.AddInitScriptAsync(
            """
            localStorage.setItem('tm-demo-culture', 'en');
            localStorage.removeItem('tm-demo-theme');
            """);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}?notation=bpmn&viewpoint=overview", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await WaitForLoadedEditorAsync(page);
        return page;
    }

    private async Task<IBrowserContext> CreateEnglishContextAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync(
            """
            localStorage.setItem('tm-demo-culture', 'en');
            localStorage.removeItem('tm-demo-theme');
            """);
        return context;
    }

    private static async Task WaitForLoadedEditorAsync(IPage page)
    {
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    private static Task WaitForEditorAttributeAsync(IPage page, string attributeName, string expectedValue) =>
        page.WaitForFunctionAsync(
            """
            ([attributeName, expectedValue]) => {
                const editor = document.querySelector("[data-testid='modeling-editor']");
                return editor?.getAttribute(attributeName) === expectedValue;
            }
            """,
            new[] { attributeName, expectedValue },
            new PageWaitForFunctionOptions { Timeout = 10000 });

    private static Task WaitForPreviewNodeCountAtLeastAsync(IPage page, int minimumCount) =>
        page.WaitForFunctionAsync(
            """
            minimumCount => {
                const preview = document.querySelector("[data-testid='modeling-diagram-preview']");
                return Number(preview?.getAttribute('data-node-count') ?? '0') >= minimumCount;
            }
            """,
            minimumCount,
            new PageWaitForFunctionOptions { Timeout = 10000 });

    private static async Task AssertNotationOptionsAsync(IPage page)
    {
        var options = await page.Locator("[data-testid='modeling-notation-select'] option").EvaluateAllAsync<string[]>(
            "options => options.map(option => option.value)");

        CollectionAssert.Contains(options, "bpmn", "Demo route should offer BPMN.");
        CollectionAssert.Contains(options, "uml", "Demo route should offer UML.");
        CollectionAssert.Contains(options, "archimate", "Demo route should offer ArchiMate.");
    }

    private static async Task AssertPanelTabsVisibleAsync(IPage page)
    {
        await page.Locator("[data-testid='modeling-panel-tabs']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await page.Locator("[data-testid='modeling-panel-tabs']").EvaluateAsync<bool>(
            "element => getComputedStyle(element).display !== 'none'"),
            "Compact panel tabs should be visible in mobile/tablet viewport.");
    }

    private static async Task<int> ReadNumericAttributeAsync(IPage page, string selector, string attributeName)
    {
        var value = await page.Locator(selector).GetAttributeAsync(attributeName);
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static Task<int> CountVisiblePanelsAsync(IPage page) =>
        page.EvaluateAsync<int>(
            """
            () => {
                const selectors = [
                    "[data-testid='modeling-model-tree-panel']",
                    "[data-testid='modeling-preview-panel']",
                    "[data-testid='modeling-inspector-panel']",
                    "[data-testid='modeling-issues-panel']"
                ];

                return selectors
                    .map(selector => document.querySelector(selector))
                    .filter(element => {
                        if (!element) return false;
                        const style = getComputedStyle(element);
                        const rect = element.getBoundingClientRect();
                        return style.display !== 'none'
                            && style.visibility !== 'hidden'
                            && rect.width > 0
                            && rect.height > 0;
                    }).length;
            }
            """);

    private static Task<bool> HasNoHorizontalOverflowAsync(IPage page) =>
        page.EvaluateAsync<bool>(
            """
            () => {
                const doc = document.documentElement;
                const body = document.body;
                return Math.max(doc.scrollWidth, body.scrollWidth) <= window.innerWidth + 2;
            }
            """);

    private static Task<bool> EditorHasTransparentSurfaceAsync(IPage page) =>
        page.EvaluateAsync<bool>(
            """
            () => {
                const editor = document.querySelector("[data-testid='modeling-editor']");
                if (!editor) return true;
                const color = getComputedStyle(editor).backgroundColor;
                return !color || color === 'transparent' || color === 'rgba(0, 0, 0, 0)';
            }
            """);

    private static Task<bool> PageContainsFatalRouteTextAsync(IPage page) =>
        page.EvaluateAsync<bool>(
            """
            () => {
                const text = document.body.innerText.toLowerCase();
                return text.includes('not found')
                    || text.includes('404')
                    || text.includes('hydration failed')
                    || text.includes('unhandled exception');
            }
            """);

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingDemoPageM23E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m23");
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
