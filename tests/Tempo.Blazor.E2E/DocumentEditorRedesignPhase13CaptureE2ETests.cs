using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 13 (overlays and dialogs): screenshots for the UX review - the compare dialog on a desktop and with the layout
/// pinned to Mobile (one column of sources on a 1440 px screen), the version dialog as a phone sheet with an emulated on-screen keyboard (the footer above
/// it), the bounded phone frame with the action bar above the keyboard, and the floating mini toolbar clipped by the frame's scroller - each in light AND
/// dark (the demo's own theme switch). Gated by
/// <c>TM_WRITE_BASELINES</c>; the PNGs land in <c>tests/Tempo.Blazor.E2E/__screenshots__/document-editor-redesign/phase-13/</c>. The behaviour is asserted in
/// <see cref="DocumentEditorPanelsResponsiveE2ETests"/> and <see cref="DocumentEditorBoundedFrameE2ETests"/>.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase13CaptureE2ETests : BaselineGeneratorTestBase
{
    private const string LongDocumentId = "phase-22-canvas-performance";

    private const string EmulateKeyboardScript = """
        height => {
            const viewport = window.visualViewport;
            Object.defineProperty(viewport, 'height', { configurable: true, get: () => height });
            viewport.dispatchEvent(new Event('resize'));
        }
        """;

    private static string OutputDirectory
    {
        get
        {
            var target = Environment.GetEnvironmentVariable("TM_DOCUMENT_EDITOR_BASELINE_DIR");
            var name = string.IsNullOrWhiteSpace(target) ? "phase-13" : target.Trim();
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

    private static readonly (bool Dark, string Name)[] Themes = [(false, "light"), (true, "dark")];

    [TestMethod]
    [Description("The compare dialog at 1440: two columns of sources, then the layout pinned to Mobile - ONE column although the screen is wide (light and dark)")]
    public async Task CaptureCompareDialog()
    {
        foreach (var (dark, theme) in Themes)
        {
            var page = await OpenFlowingAsync(1440, 900, false);
            await SetDemoDarkAsync(page, dark);
            await ClickRibbonAsync(page, "document-ribbon-tab-review");
            await ClickRibbonAsync(page, "document-compare-open");
            await Assertions.Expect(page.GetByTestId("document-compare-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(page.GetByTestId("document-compare-target-change")).ToBeFocusedAsync(new() { Timeout = 10_000 });
            await page.WaitForTimeoutAsync(500);
            await PageShot(page, $"compare-dialog-desktop-two-columns-{theme}-1440.png");

            await page.EvaluateAsync("""
                () => {
                    const select = document.querySelector('[data-testid="document-editor-layout-mode"]');
                    select.value = 'Mobile';
                    select.dispatchEvent(new Event('change', { bubbles: true }));
                }
                """);
            await WaitForPredicateAsync(page, "() => document.querySelector('.tm-modal-overlay[data-testid=\"document-compare-dialog\"]')?.dataset.layout === 'mobile'", 15_000);
            await page.WaitForTimeoutAsync(500);
            await PageShot(page, $"compare-dialog-forced-mobile-one-column-{theme}-1440.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("A 390 px version dialog as a sheet: the label focused, the (emulated) keyboard open - the Create / Cancel footer is above it (light and dark)")]
    public async Task CaptureVersionDialogWithKeyboard()
    {
        foreach (var (dark, theme) in Themes)
        {
            var page = await OpenFlowingAsync(390, 844, true);
            await SetDemoDarkAsync(page, dark);
            await OpenInspectorTabAsync(page, "versions");
            await page.GetByTestId("document-version-create-open").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-version-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(page.GetByTestId("document-version-label")).ToBeFocusedAsync(new() { Timeout = 10_000 });
            await page.WaitForTimeoutAsync(500);
            await PageShot(page, $"version-dialog-phone-no-keyboard-{theme}-390.png");

            await page.EvaluateAsync(EmulateKeyboardScript, 544);
            await page.WaitForFunctionAsync("() => document.querySelector('[data-testid=\"document-version-create-submit\"]').getBoundingClientRect().bottom <= 545", null, new() { Timeout = 10_000 });
            await page.WaitForTimeoutAsync(500);
            // The keyboard itself is not rendered by a headless browser: the shot is cropped to the visual viewport it would leave.
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(OutputDirectory, $"version-dialog-phone-keyboard-{theme}-390.png"), Type = ScreenshotType.Png, Clip = new Clip { X = 0, Y = 0, Width = 390, Height = 544 } });
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The bounded phone frame with the (emulated) keyboard open: the action bar is above it, the frame gave the room up (light and dark)")]
    public async Task CaptureBoundedPhoneWithKeyboard()
    {
        foreach (var (dark, theme) in Themes)
        {
            var context = await CreateContextAsync(ContextOptions(390, 844, true));
            var page = await context.NewPageAsync();
            await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={LongDocumentId}&showToolbar=true&preferLocalDraft=false&height=viewport", WaitUntilState.DOMContentLoaded);
            await WaitForPredicateAsync(page, """
                () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                    && document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-layout-complete') === 'true'
                    && document.querySelector('[data-testid="document-mobile-action-bar"]')
                """, 60_000);
            await SetDemoDarkAsync(page, dark);
            await page.WaitForTimeoutAsync(600);
            await PageShot(page, $"bounded-phone-no-keyboard-{theme}-390.png");

            await page.EvaluateAsync(EmulateKeyboardScript, 544);
            await WaitForPredicateAsync(page, "() => !!document.querySelector('.tm-document-editor').style.getPropertyValue('--tm-visual-viewport-inset-block-end')", 10_000);
            await page.WaitForTimeoutAsync(500);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(OutputDirectory, $"bounded-phone-keyboard-{theme}-390.png"), Type = ScreenshotType.Png, Clip = new Clip { X = 0, Y = 0, Width = 390, Height = 544 } });
            await context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The floating mini toolbar of a range selection: inside the bounded frame it follows the doc region's clip - visible with the selection (light and dark), gone once the selection scrolled out")]
    public async Task CaptureMiniToolbarClip()
    {
        foreach (var (dark, theme) in Themes)
        {
            var context = await CreateContextAsync(ContextOptions(1440, 900, false));
            var page = await context.NewPageAsync();
            await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={LongDocumentId}&showToolbar=true&preferLocalDraft=false&height=viewport&pageFit=off", WaitUntilState.DOMContentLoaded);
            await WaitForPredicateAsync(page, """
                () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                    && document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-layout-complete') === 'true'
                """, 60_000);
            await SetDemoDarkAsync(page, dark);
            await page.WaitForTimeoutAsync(600);
            var point = await page.EvaluateAsync<int[]>("""
                () => {
                    const region = document.querySelector('[data-region="doc"][data-scroll-viewport="true"]').getBoundingClientRect();
                    const node = Array.from(document.querySelectorAll('[data-canvas-text-rect]'))
                        .find(n => { const r = n.getBoundingClientRect(); return r.width > 60 && r.top > region.top + 40 && r.bottom < region.bottom - 40; });
                    const r = node.getBoundingClientRect();
                    return [Math.round(r.left + 12), Math.round(r.top + r.height / 2)];
                }
                """);
            await page.Mouse.DblClickAsync(point[0], point[1]);
            await Assertions.Expect(page.GetByTestId("document-mini-toolbar")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await PageShot(page, $"mini-toolbar-visible-{theme}-1440.png");

            if (!dark)
            {
                await page.EvaluateAsync("""
                    () => {
                        const region = document.querySelector('[data-region="doc"][data-scroll-viewport="true"]');
                        const first = document.querySelector('[data-testid="document-canvas-page"][data-page-index="0"]');
                        region.scrollTop = 9 * (first.getBoundingClientRect().height + 24);
                    }
                    """);
                await Assertions.Expect(page.GetByTestId("document-mini-toolbar")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
                // The page the reader landed on is laid out and painted (text on screen), not a blank sheet.
                await page.WaitForFunctionAsync("""
                    () => {
                        const region = document.querySelector('[data-region="doc"][data-scroll-viewport="true"]').getBoundingClientRect();
                        const page = document.querySelector('[data-testid="document-canvas-page"][data-page-index="9"]');
                        if (!page || Number(page.getAttribute('data-canvas-painted-command-count') || '0') === 0) return false;
                        return Array.from(page.querySelectorAll('[data-canvas-text-rect]')).some(n => { const r = n.getBoundingClientRect(); return r.bottom > region.top + 40 && r.top < region.bottom - 40; });
                    }
                    """, null, new() { Timeout = 20_000 });
                await page.WaitForTimeoutAsync(300);
                await PageShot(page, "mini-toolbar-scrolled-out-light-1440.png");
            }

            await context.CloseAsync();
        }
    }

    private async Task<IPage> OpenFlowingAsync(int width, int height, bool touch)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, FlowingDocumentEditorUrl(BaseUrl, "documentId=contract-demo"), WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        return page;
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

    private static Task PageShot(IPage page, string fileName)
        => page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(OutputDirectory, fileName), Type = ScreenshotType.Png });
}
