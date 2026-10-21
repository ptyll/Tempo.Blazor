using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Wireframe redesign phase 1 "before" baseline: the editor (empty selection and one selected
/// element), the export dialog, the layers panel and the shared <c>TmDocumentOpenDialog</c> (list and
/// grid) captured at desktop 1440×900, tablet 1024×768 (touch), mobile 390×844 and 320×640, in light
/// and dark. Editor / export / layers land in <c>__screenshots__/wireframe/baseline/</c>, the open
/// dialog in <c>__screenshots__/document-library/baseline/</c> — committed, durable directories
/// (vstest deletes the per-run TestResults folder); every file is also attached to the test result.
/// Review them against <c>output/imagegen/tempo-reimagined/Tempo.Blazor.Wireframe/</c> (rewrite the committed set with <c>TM_E2E_UPDATE_BASELINE=1</c>)
/// (the findings table lives in the phase notes). Besides capturing, each state asserts that the
/// component really rendered, so a blank/failed page cannot become a "baseline".
/// </summary>
[TestClass]
public sealed class WireframeResponsiveScreenshotTests : WasmTestBase
{
    private const string WireframeEditorUrl = "/wireframe-editor";
    private const string OpenDialogUrl = "/document-open-dialog";

    private static string ScreenshotRoot =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__");

    /// <summary>
    /// The committed baselines are only rewritten on purpose: set <c>TM_E2E_UPDATE_BASELINE=1</c>. Without it the
    /// captures go to the (git-ignored) per-run TestResults folder and are attached to the test result, so the
    /// full lane never dirties the working tree.
    /// </summary>
    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string WireframeDirectory => UpdateBaseline
        ? Path.Combine(ScreenshotRoot, "wireframe", "baseline")
        : Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "wireframe-baseline");

    private static string DocumentLibraryDirectory => UpdateBaseline
        ? Path.Combine(ScreenshotRoot, "document-library", "baseline")
        : Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "document-library-baseline");

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1440×900 light+dark: editor, selected element, export dialog, layers panel, open dialog (list+grid)")]
    public async Task Desktop_1440_CapturesEditorExportLayersAndOpenDialog() =>
        await CaptureAllAsync(1440, 900, touch: false, "desktop-1440");

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1024×768 touch light+dark: editor, selected element, export dialog, layers panel, open dialog (list+grid)")]
    public async Task Tablet_1024Touch_CapturesEditorExportLayersAndOpenDialog() =>
        await CaptureAllAsync(1024, 768, touch: true, "tablet-1024-touch");

    [TestMethod]
    [TestCategory("WASM")]
    [Description("390×844 light+dark: editor, selected element, export dialog, layers panel, open dialog (list+grid)")]
    public async Task Mobile_390_CapturesEditorExportLayersAndOpenDialog() =>
        await CaptureAllAsync(390, 844, touch: false, "mobile-390");

    [TestMethod]
    [TestCategory("WASM")]
    [Description("320×640 light+dark: editor, selected element, export dialog, layers panel, open dialog (list+grid)")]
    public async Task Mobile_320_CapturesEditorExportLayersAndOpenDialog() =>
        await CaptureAllAsync(320, 640, touch: false, "mobile-320");

    // ── Capture flow ──────────────────────────────────────────────────────────

    private async Task CaptureAllAsync(int width, int height, bool touch, string prefix)
    {
        Directory.CreateDirectory(WireframeDirectory);
        Directory.CreateDirectory(DocumentLibraryDirectory);

        foreach (var dark in new[] { false, true })
        {
            var theme = dark ? "dark" : "light";
            var tag = $"{prefix}-{theme}";

            // 1. Editor page: empty selection → selected element → export dialog → layers panel.
            var page = await OpenPageAsync(WireframeEditorUrl, width, height, touch, dark);
            // Attached, not Visible: on a phone the fixed-width toolbox + properties panels squeeze the
            // canvas to ~0 px (that IS the baseline finding), so a visibility wait would fail the capture.
            await page.WaitForSelectorAsync(".tm-wd-editor .tm-wd-canvas__svg",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 30_000 });
            await page.WaitForSelectorAsync(".tm-wd-editor .tm-wd-toolbox__item",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 30_000 });
            await SettleAsync(page);

            var editor = page.Locator(".tm-wd-editor").First;
            await editor.ScrollIntoViewIfNeededAsync();
            Assert.IsTrue(await editor.IsVisibleAsync(), $"{tag}: the editor must be visible");

            var canvasWidth = await page.EvaluateAsync<double>(
                "() => document.querySelector('.tm-wd-editor .tm-wd-canvas__svg').getBoundingClientRect().width");
            TestContext.WriteLine($"[baseline] {tag}: canvas width = {canvasWidth:0}px of a {width}px viewport");
            await ShotLocatorAsync(editor, WireframeDirectory, $"{tag}--editor-empty");

            // Add an element through the toolbox (click/tap insertion — phase-1 behaviour) so the
            // "selected element" state shows the properties panel with real content.
            var before = await editor.Locator("g[data-el-id]").CountAsync();
            var item = editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First;
            await item.ScrollIntoViewIfNeededAsync();
            await item.DispatchEventAsync("click");
            await page.WaitForFunctionAsync(
                "n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length > n",
                before, new PageWaitForFunctionOptions { Timeout = 10_000 });
            await SettleAsync(page);
            await ShotLocatorAsync(editor, WireframeDirectory, $"{tag}--editor-selected");

            // Export dialog (PDF, via the menu → proves the preselect in the baseline too).
            // DOM-level clicks: on a phone the toolbar overflows its container (baseline finding), so the
            // Export button can sit outside the clickable viewport region while still being rendered.
            await editor.Locator("[data-testid='wd-export'] button.tm-dropdown-trigger").First
                .DispatchEventAsync("click");
            await page.Locator("[data-testid='wd-export-pdf']").First
                .DispatchEventAsync("click");
            await page.WaitForSelectorAsync(".tm-wd-export-dialog",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
            await SettleAnimationsAsync(page);
            await ShotPageAsync(page, WireframeDirectory, $"{tag}--export-dialog");
            await page.Locator(".tm-modal button", new() { HasTextString = "Cancel" }).First
                .ClickAsync(new LocatorClickOptions { Force = true });
            await page.WaitForSelectorAsync(".tm-wd-export-dialog",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });

            // Layers panel (a tab of the left panel since phase 4).
            await editor.Locator("[data-region='left'] [role='tab']", new() { HasTextString = "Layers" }).First.DispatchEventAsync("click");
            await page.WaitForTimeoutAsync(400);
            var layers = editor.Locator(".tm-wd-layers").First;
            if (await layers.CountAsync() > 0)
            {
                await layers.ScrollIntoViewIfNeededAsync();
                await ShotLocatorAsync(layers, WireframeDirectory, $"{tag}--layers");
            }

            // 2. Open document dialog (shared TmDocumentOpenDialog): list, then grid.
            var dialogPage = await OpenPageAsync(OpenDialogUrl, width, height, touch, dark);
            await dialogPage.ClickAsync("[data-testid=open-dialog-btn]");
            await dialogPage.WaitForSelectorAsync(".tm-document-open-dialog",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
            await dialogPage.Locator(".tm-dod-tree-node", new() { HasTextString = "Designs" }).First
                .DispatchEventAsync("click");
            // Attached: at 320 px the fixed-width tree can push the list out of the visible area (baseline finding).
            await dialogPage.WaitForSelectorAsync(".tm-dod-row",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 20_000 });
            await SettleAnimationsAsync(dialogPage);
            await ShotPageAsync(dialogPage, DocumentLibraryDirectory, $"{tag}--open-document");

            await dialogPage.Locator(".tm-dod-view-grid").First.DispatchEventAsync("click");
            await dialogPage.WaitForSelectorAsync(".tm-dod-card",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 10_000 });
            await SettleAnimationsAsync(dialogPage);
            await ShotPageAsync(dialogPage, DocumentLibraryDirectory, $"{tag}--open-document-grid");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IPage> OpenPageAsync(string path, int width, int height, bool touch, bool dark)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = touch,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            AcceptDownloads = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        await page.GotoAsync($"{BaseUrl}{path}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000
        });
        await WaitForAppReadyAsync(page);

        if (dark)
        {
            await page.EvaluateAsync(
                """
                () => {
                    document.documentElement.setAttribute('data-theme', 'dark');
                    document.documentElement.classList.add('dark');
                }
                """);
        }

        return page;
    }

    private static async Task SettleAsync(IPage page)
    {
        await page.WaitForTimeoutAsync(500);
        await page.Mouse.MoveAsync(0, 0);
    }

    /// <summary>Waits for the modal open/close animations so a frame is never captured mid-slide.</summary>
    private static async Task SettleAnimationsAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            async () => {
                const animations = document.getAnimations();
                await Promise.all(animations.map(a => a.finished.catch(() => { })));
            }
            """);
        await page.WaitForTimeoutAsync(250);
    }

    private async Task ShotLocatorAsync(ILocator locator, string directory, string name)
    {
        var path = Path.Combine(directory, name + ".png");
        await locator.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
    }

    private async Task ShotPageAsync(IPage page, string directory, string name)
    {
        var path = Path.Combine(directory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
