using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 12 (the viewport-bounded frame): screenshots for the UX review. Each shot is the whole PAGE at the viewport size - the point of
/// the phase is that the editor, the demo top bar and the status bar share one screen without the page scrolling - plus ELEMENT shots of the editor. Gated by
/// <c>TM_WRITE_BASELINES</c>; the PNGs land in <c>tests/Tempo.Blazor.E2E/__screenshots__/document-editor-redesign/phase-12/</c>. The behaviour is asserted in
/// <see cref="DocumentEditorBoundedFrameE2ETests"/>.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase12CaptureE2ETests : BaselineGeneratorTestBase
{
    private const string LongDocumentId = "phase-22-canvas-performance";

    private static string OutputDirectory
    {
        get
        {
            var target = Environment.GetEnvironmentVariable("TM_DOCUMENT_EDITOR_BASELINE_DIR");
            var name = string.IsNullOrWhiteSpace(target) ? "phase-12" : target.Trim();
            var directory = Path.Combine(
                FindRepositoryRootDirectory(),
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-redesign", name);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    [Description("1440x900 bounded: the top of the document and page 10, light and dark - ribbon on top, status bar pinned, no page scroll")]
    public async Task CaptureDesktopFrame()
    {
        var page = await OpenAsync(1440, 900, false, "height=viewport");
        foreach (var (dark, theme) in new[] { (false, "light"), (true, "dark") })
        {
            await SetDemoDarkAsync(page, dark);
            await PageShot(page, $"frame-top-{theme}-1440.png");
        }

        await SetDemoDarkAsync(page, false);
        await ScrollToPageAsync(page, 9);
        await WaitForPageMountedAsync(page, 9);
        await PageShot(page, "frame-page10-light-1440.png");
        await ElementShot(page, "frame-page10-editor-light-1440.png");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("PageFitMode.Off at a 1048 px column: the horizontal scrollbar is at the bottom of the visible workspace from the first screen")]
    public async Task CaptureHorizontalScrollbar()
    {
        var page = await OpenAsync(1440, 900, false, "height=viewport&pageFit=off");
        await PageShot(page, "hscrollbar-top-light-1048.png");
        await page.EvaluateAsync("() => { document.querySelector('[data-region=\"doc\"][data-scroll-viewport=\"true\"]').scrollLeft = 140; }");
        await page.WaitForTimeoutAsync(300);
        await PageShot(page, "hscrollbar-scrolled-light-1048.png");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Find 'go to result' on a late page with the frame bounded")]
    public async Task CaptureFindOnALatePage()
    {
        var page = await OpenAsync(1440, 900, false, "height=viewport");
        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        await page.Keyboard.PressAsync("Control+F");
        await page.GetByTestId("document-find-input").FillAsync("Performance paragraph 140.");
        await Assertions.Expect(page.GetByTestId("document-find-result").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await page.GetByTestId("document-find-result").First.ClickAsync();
        await WaitForPredicateAsync(page, "() => document.querySelector('.tm-document-canvas-search-highlight--active')", 20_000);
        await page.WaitForTimeoutAsync(400);
        await PageShot(page, "find-late-page-light-1440.png");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A tablet (820) and a phone (390): the frame, the in-flow action bar of the phone, light and dark")]
    public async Task CaptureTabletAndPhone()
    {
        foreach (var (name, width, height) in new[] { ("820", 820, 1180), ("390", 390, 844) })
        {
            var page = await OpenAsync(width, height, true, "height=viewport");
            foreach (var (dark, theme) in new[] { (false, "light"), (true, "dark") })
            {
                await SetDemoDarkAsync(page, dark);
                await PageShot(page, $"frame-top-{theme}-{name}.png");
            }

            await SetDemoDarkAsync(page, false);
            await ScrollToPageAsync(page, 6);
            await WaitForPageMountedAsync(page, 6);
            await PageShot(page, $"frame-scrolled-light-{name}.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The other document-region modes inside the frame: the print preview (its page list is sticky inside the frame) and the tablet's side sheet parked in the scroll area")]
    public async Task CaptureModesInTheFrame()
    {
        var page = await OpenAsync(1440, 900, false, "height=viewport&pageFit=off");
        await page.GetByTestId("document-header-print-preview").ClickAsync();
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-print-preview-pages\"]') && document.querySelector('[data-testid=\"document-canvas-page\"][data-canvas-painted-command-count]')", 20_000);
        await page.WaitForTimeoutAsync(400);
        await PageShot(page, "print-preview-light-1440.png");
        await page.Context.CloseAsync();

        page = await OpenAsync(820, 1180, true, "height=viewport");
        await page.GetByTestId("document-side-panel-edge-toggle").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-side-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await ScrollToPageAsync(page, 4);
        await WaitForPageMountedAsync(page, 4);
        await PageShot(page, "tablet-sheet-scrolled-light-820.png");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The demo page /document-editor, bounded by default: the controls above, the editor under the demo top bar, the status bar on screen, no page scroll")]
    public async Task CaptureDemoPage()
    {
        foreach (var (name, width, height, touch) in new[] { ("1440", 1440, 900, false), ("390", 390, 844, true) })
        {
            var context = await CreateContextAsync(ContextOptions(width, height, touch));
            var page = await context.NewPageAsync();
            await GotoAppReadyAsync(page, BoundedDocumentEditorUrl(BaseUrl, "documentId=contract-demo"), WaitUntilState.DOMContentLoaded);
            await WaitForPredicateAsync(page, """
                () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                    && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                """, 60_000);
            await page.WaitForTimeoutAsync(400);
            await PageShot(page, $"demo-page-light-{name}.png");
            await SetDemoDarkAsync(page, true);
            await PageShot(page, $"demo-page-dark-{name}.png");
            await context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Fullscreen is the bounded frame at the viewport height")]
    public async Task CaptureFullscreen()
    {
        var page = await OpenAsync(1440, 900, false, null);
        await page.GetByTestId("document-ribbon-tab-view").ClickAsync();
        await page.GetByTestId("document-fullscreen").ClickAsync();
        await WaitForPredicateAsync(page, "() => document.querySelector('.tm-document-editor')?.getAttribute('data-fullscreen') === 'true'", 10_000);
        await page.WaitForTimeoutAsync(400);
        await PageShot(page, "fullscreen-light-1440.png");
        await page.Context.CloseAsync();
    }

    // The demo's own theme switch (ThemeService): the shell carries `dark` and the page background follows, which a hand-set data-theme does not do.
    private static async Task SetDemoDarkAsync(IPage page, bool dark)
    {
        var isDark = await page.EvaluateAsync<bool>("() => !!document.querySelector('.demo-shell')?.classList.contains('dark')");
        if (isDark != dark)
        {
            await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        }

        await WaitForPredicateAsync(page, $"() => !!document.querySelector('.demo-shell')?.classList.contains('dark') === {(dark ? "true" : "false")}", 10_000);
        await page.WaitForTimeoutAsync(450); // the shell's colour transition
    }

    private static Task WaitForPageMountedAsync(IPage page, int pageIndex)
        => page.WaitForFunctionAsync(
            "index => { const el = document.querySelector(`[data-testid=\"document-canvas-page\"][data-page-index=\"${index}\"]`); return !!el && Number(el.getAttribute('data-canvas-painted-command-count') || '0') > 0; }",
            pageIndex,
            new PageWaitForFunctionOptions { Timeout = 20_000 });

    private static Task ScrollToPageAsync(IPage page, int pageIndex)
        => page.EvaluateAsync(
            """
            index => {
                const region = document.querySelector('[data-region="doc"][data-scroll-viewport="true"]');
                const first = document.querySelector('[data-testid="document-canvas-page"][data-page-index="0"]');
                region.scrollTop = index * (first.getBoundingClientRect().height + 24);
            }
            """,
            pageIndex);

    private async Task<IPage> OpenAsync(int width, int height, bool touch, string? query)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        var url = $"{BaseUrl}/canvas-engine-host?documentId={LongDocumentId}&showToolbar=true&preferLocalDraft=false{(query is null ? string.Empty : "&" + query)}";
        await GotoAppReadyAsync(page, url, WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-layout-complete') === 'true'
                && document.querySelector('[data-testid="document-ribbon-tab-view"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(900);
        return page;
    }

    // The viewport itself: the demo's top bar, the editor and the status bar on one screen (the page does not scroll).
    private static Task PageShot(IPage page, string fileName)
        => page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(OutputDirectory, fileName), Type = ScreenshotType.Png });

    private static Task ElementShot(IPage page, string fileName)
        => page.Locator(".tm-document-editor").First.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, fileName),
            Type = ScreenshotType.Png,
        });
}
