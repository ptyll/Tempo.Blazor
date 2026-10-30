using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 15 baseline GENERATOR: freezes the notation palette and the preview legend appearance —
/// <c>legend-1440x900</c> (desktop preview with the expanded legend card), <c>legend-390x844</c>
/// (mobile band with the "⋯" → Legenda bottom sheet open) and <c>open-editor-palette-1440x900</c>
/// (the "Open in editor" dialog with the notation palette pinned first) — under
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/modeling/</c>, the committed durable baseline
/// directory. The later UX review diffs the legend against
/// <c>modeling-editor--tablet.png</c> (the legend was the [E] finding there).
/// <para>Off unless <c>TM_WRITE_BASELINES=1</c>. Not part of the Smoke lane.</para>
/// </summary>
[TestClass]
public sealed class ModelingPhase15BaselineScreenshots : BaselineGeneratorTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";
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

            var dir = Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "modeling");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    [TestMethod]
    [Description("1440x900: the desktop preview with the expanded legend card")]
    public async Task Capture_PreviewLegend_1440x900()
    {
        var page = await OpenModelingPageAsync("?notation=bpmn", 1440, 900, touch: false);
        await WaitForLoadedStateAsync(page);
        await page.Locator("[data-testid='modeling-preview-legend']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await SettleAsync(page);
        await SaveViewportScreenshotAsync(page, "legend-1440x900");
    }

    [TestMethod]
    [Description("390x844: the mobile band with the ⋯ → Legenda bottom sheet open")]
    public async Task Capture_PreviewLegend_390x844()
    {
        var page = await OpenModelingPageAsync("?notation=bpmn&viewpoint=overview", 390, 844, touch: true);
        await WaitForLoadedStateAsync(page);

        await page.Locator("[data-testid='modeling-editor-toolbar'] .tm-modeling-editor__more .tm-dropdown-trigger")
            .TapAsync(new LocatorTapOptions { Timeout = 15_000 });
        await page.Locator(".tm-modeling-editor__more .tm-dropdown-item")
            .Filter(new LocatorFilterOptions { HasTextString = "Legend" })
            .First.TapAsync(new LocatorTapOptions { Timeout = 15_000 });

        var sheet = page.Locator("[data-testid='modeling-legend-sheet']");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await sheet.Locator("[data-testid='modeling-preview-legend'] .tm-modeling-diagram-legend__list > li")
            .First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await SettleAsync(page);
        await SaveViewportScreenshotAsync(page, "legend-390x844");
    }

    [TestMethod]
    [Description("1440x900: the open-editor dialog with the notation palette pinned first")]
    public async Task Capture_OpenEditorPalette_1440x900()
    {
        var page = await OpenModelingPageAsync("?notation=bpmn", 1440, 900, touch: false);
        await WaitForLoadedStateAsync(page);

        await page.Locator("[data-testid='modeling-open-in-editor-button']")
            .ClickAsync(new LocatorClickOptions { Timeout = 15_000 });
        var dialog = page.Locator("[data-testid='modeling-open-diagram-editor']");
        await dialog.Locator(".tm-modal")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var pinned = dialog.Locator(".tm-diagram-toolbox__library[data-library-id='notation-palette']");
        await pinned.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await SettleAsync(page);
        await SaveViewportScreenshotAsync(page, "open-editor-palette-1440x900");
    }

    private async Task<IPage> OpenModelingPageAsync(string query, int width, int height, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch && width < 640,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{ModelingEditorUrl}{query}");
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = DisableMotionStyle });
        return page;
    }

    private static async Task WaitForLoadedStateAsync(IPage page)
    {
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-node').length > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

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
            Timeout = 30_000
        });
        Assert.IsTrue(new FileInfo(path).Length > 10_000, $"{fileName} should contain the rendered modeling screen.");
    }
}
