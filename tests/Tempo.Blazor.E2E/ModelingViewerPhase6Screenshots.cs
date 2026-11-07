using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 6 baseline GENERATOR: freezes the read-only viewer's appearance under
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/modeling-viewer/</c> — the diff scenario in light and
/// dark on desktop, the diff on tablet, the heatmap and diff on mobile touch, and the empty model.
/// Off unless <c>TM_WRITE_BASELINES=1</c>. Not part of the Smoke lane.
/// </summary>
[TestClass]
public sealed class ModelingViewerPhase6Screenshots : BaselineGeneratorTestBase
{
    private const string ViewerUrl = "/modeling-viewer";
    private const string ViewerSelector = "[data-testid='modeling-viewer-demo']";
    private const string DisableMotionStyle = "*{animation:none!important;transition:none!important;caret-color:transparent!important}";

    private static string OutputDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                directory = directory.Parent!;
            }

            var dir = Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "modeling-viewer");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    [TestMethod]
    [Description("1440x900 light: the diff scenario with the state legend and the keyboard list")]
    public async Task Capture_Diff_Desktop_Light_1440x900()
    {
        var page = await OpenViewerAsync("?scenario=diff", 1440, 900, touch: false);
        await RevealViewerAsync(page);
        await SettleAsync(page);
        await SaveViewportScreenshotAsync(page, "diff-desktop-light-1440x900");
    }

    [TestMethod]
    [Description("1440x900 dark: the diff scenario keeps token-backed surfaces and readable legend text")]
    public async Task Capture_Diff_Desktop_Dark_1440x900()
    {
        var page = await OpenViewerAsync("?scenario=diff", 1440, 900, touch: false);
        await RevealViewerAsync(page);
        await page.Locator("[data-testid='theme-toggle']").Last.DispatchEventAsync("click");
        await page.Locator("div[data-theme='dark']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForTimeoutAsync(300);
        await SettleAsync(page);
        await SaveViewportScreenshotAsync(page, "diff-desktop-dark-1440x900");
    }

    [TestMethod]
    [Description("1440x900 light: the heatmap scenario with the four-step scale and the value text")]
    public async Task Capture_Heatmap_Desktop_Light_1440x900()
    {
        var page = await OpenViewerAsync("?scenario=heatmap", 1440, 900, touch: false);
        await RevealViewerAsync(page);
        await SettleAsync(page);
        await SaveViewportScreenshotAsync(page, "heatmap-desktop-light-1440x900");
    }

    [TestMethod]
    [Description("1440x900 dark: the heat tints keep their light values on the white page")]
    public async Task Capture_Heatmap_Desktop_Dark_1440x900()
    {
        var page = await OpenViewerAsync("?scenario=heatmap", 1440, 900, touch: false);
        await RevealViewerAsync(page);
        await page.Locator("[data-testid='theme-toggle']").Last.DispatchEventAsync("click");
        await page.Locator("div[data-theme='dark']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForTimeoutAsync(300);
        await SettleAsync(page);
        await SaveViewportScreenshotAsync(page, "heatmap-desktop-dark-1440x900");
    }

    [TestMethod]
    [Description("1024x768 tablet: the diff scenario in the tablet band")]
    public async Task Capture_Diff_Tablet_1024x768()
    {
        var page = await OpenViewerAsync("?scenario=diff", 1024, 768, touch: false);
        await RevealViewerAsync(page);
        await SettleAsync(page);
        await SaveViewportScreenshotAsync(page, "diff-tablet-1024x768");
    }

    [TestMethod]
    [Description("390x844 touch: the heatmap scenario with the mobile text alternative visible")]
    public async Task Capture_Heatmap_Mobile_390x844()
    {
        var page = await OpenViewerAsync("?scenario=heatmap", 390, 844, touch: true);
        await page.Locator($"{ViewerSelector}[data-layout='mobile']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });
        await RevealViewerAsync(page);
        await SettleAsync(page);
        await SaveViewportScreenshotAsync(page, "heatmap-mobile-390x844");
    }

    [TestMethod]
    [Description("390x844 touch: the diff scenario with the legend and the removed group in the list")]
    public async Task Capture_Diff_Mobile_390x844()
    {
        var page = await OpenViewerAsync("?scenario=diff", 390, 844, touch: true);
        await page.Locator($"{ViewerSelector}[data-layout='mobile']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });
        await RevealViewerAsync(page);
        await SettleAsync(page);
        await SaveViewportScreenshotAsync(page, "diff-mobile-390x844");
    }

    [TestMethod]
    [Description("1440x900 light: the empty-model edge case")]
    public async Task Capture_Empty_Desktop_1440x900()
    {
        var page = await OpenViewerAsync("?scenario=empty", 1440, 900, touch: false);
        await RevealViewerAsync(page);
        await SettleAsync(page);
        await SaveViewportScreenshotAsync(page, "empty-desktop-1440x900");
    }

    private async Task<IPage> OpenViewerAsync(string query, int width, int height, bool touch)
    {
        var context = touch
            ? await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                HasTouch = true,
                IsMobile = width < 640,
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
            })
            : await CreateContextAsync();
        RegisterContext(context);
        await context.AddInitScriptAsync(
            """
            localStorage.setItem('tm-demo-culture', 'en');
            localStorage.removeItem('tm-demo-theme');
            """);

        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}{ViewerUrl}{query}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 90000,
        });
        await WaitForAppReadyAsync(page);
        await page.Locator(ViewerSelector)
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-viewer-demo']\")?.getAttribute('data-state') === 'populated'"
            + " || document.querySelector(\"[data-testid='modeling-viewer-demo']\")?.getAttribute('data-state') === 'empty'",
            null,
            new PageWaitForFunctionOptions { Timeout = 30000 });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = DisableMotionStyle });
        return page;
    }

    private static async Task RevealViewerAsync(IPage page)
        => await page.Locator(ViewerSelector).ScrollIntoViewIfNeededAsync();

    private static async Task SettleAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            async () => {
                await document.fonts.ready;
                const animations = document.getAnimations();
                await Promise.all(animations.map(a => a.finished.catch(() => { })));
            }
            """);
        await page.WaitForTimeoutAsync(250);
    }

    private static async Task SaveViewportScreenshotAsync(IPage page, string fileName)
    {
        // A viewport capture keeps the touch emulation honest; FullPage would reset it.
        var path = Path.Combine(OutputDirectory, fileName + ".png");
        await page.Mouse.MoveAsync(0, 0);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            FullPage = false,
            Timeout = 30000,
        });
    }
}
