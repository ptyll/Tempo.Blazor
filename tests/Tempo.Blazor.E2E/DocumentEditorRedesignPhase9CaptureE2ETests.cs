using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 9 (the final UX review): the "after" screenshots of every surface the plan rebuilt, at 1440 / 1024 (touch) / 390 / 320, in
/// light and dark and in the default and indigo brand. Gated by <c>TM_WRITE_BASELINES</c>; the PNGs land in
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/document-editor-redesign/phase-9/</c> (override the folder name with <c>TM_DOCUMENT_EDITOR_BASELINE_DIR</c>).
/// The specs that assert behaviour are <see cref="DocumentEditorRedesignPhase9E2ETests"/> and the earlier phases' classes.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase9CaptureE2ETests : BaselineGeneratorTestBase
{
    private static readonly (string Name, int Width, int Height, bool Touch)[] Viewports =
    [
        ("1440", 1440, 900, false),
        ("1024-touch", 1024, 768, true),
        ("390", 390, 844, true),
        ("320", 320, 640, true),
    ];

    private static string OutputDirectory
    {
        get
        {
            var target = Environment.GetEnvironmentVariable("TM_DOCUMENT_EDITOR_BASELINE_DIR");
            var name = string.IsNullOrWhiteSpace(target) ? "phase-9" : target.Trim();
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
    [Description("The idle editor and the inspector (revisions) at every width, in the four light / dark x default / indigo combinations")]
    public async Task CaptureThemeMatrix()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenAsync(width, height, touch, RevisionsDocumentId);
            foreach (var (dark, indigo, theme) in ThemeMatrix)
            {
                await ApplyThemeAsync(page, dark, indigo);
                await Shot(page, $"editor-{theme}-{name}.png");
            }

            await ApplyThemeAsync(page, false, false);
            await OpenInspectorTabAsync(page, "revisions");
            foreach (var (dark, indigo, theme) in ThemeMatrix)
            {
                await ApplyThemeAsync(page, dark, indigo);
                await Shot(page, $"revisions-{theme}-{name}.png");
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The other panels, find and the command palette, light default")]
    public async Task CapturePanelsFindAndPalette()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenAsync(width, height, touch, RevisionsDocumentId);
            // On a desktop Outline and Pages are the left column (the navigation panel), not tabs of the inspector.
            foreach (var tab in width >= 1280 ? new[] { "comments", "versions", "properties" } : new[] { "comments", "versions", "outline", "pages", "properties" })
            {
                await OpenInspectorTabAsync(page, tab);
                await Shot(page, $"panel-{tab}-{name}.png");
            }

            await page.Context.CloseAsync();
            page = await OpenAsync(width, height, touch, RevisionsDocumentId);
            await page.GetByTestId("document-header-search").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.GetByTestId("document-find-input").FillAsync("text");
            await Assertions.Expect(page.GetByTestId("document-find-result").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"find-{name}.png");
            await page.Keyboard.PressAsync("Escape");

            await page.GetByTestId("document-canvas-hidden-input").FocusAsync();
            await page.Keyboard.PressAsync("Control+Shift+P");
            await Assertions.Expect(page.GetByTestId("document-command-palette")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.GetByTestId("document-command-palette-search").FillAsync("re");
            await page.WaitForTimeoutAsync(300);
            await Shot(page, $"command-palette-{name}.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The print preview, the compare dialog and the version dialog, light default")]
    public async Task CapturePreviewAndDialogs()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenAsync(width, height, touch, RevisionsDocumentId);
            if (width >= 640)
            {
                // comparing against sources is tablet-and-up (decision G2)
                await ClickPopupItemAsync(page, "document-ribbon-tab-review", "");
                await ClickRibbonAsync(page, "document-compare-open");
                await Assertions.Expect(page.GetByTestId("document-compare-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
                await Shot(page, $"compare-dialog-{name}.png");
                await page.Keyboard.PressAsync("Escape");
            }

            await OpenInspectorTabAsync(page, "versions");
            await page.GetByTestId("document-version-create-open").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-version-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"version-dialog-{name}.png");
            await page.Keyboard.PressAsync("Escape");
            await page.Context.CloseAsync();

            var printPage = await OpenAsync(width, height, touch, RevisionsDocumentId);
            if (width < 640)
            {
                await printPage.GetByTestId("document-mobile-print-preview").ClickAsync();
            }
            else
            {
                await ClickPopupItemAsync(printPage, "document-ribbon-tab-view", "");
                await ClickRibbonAsync(printPage, "document-open-print-preview");
            }

            await Assertions.Expect(printPage.GetByTestId("document-print-preview-actions")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Shot(printPage, $"print-preview-{name}.png");
            await ApplyThemeAsync(printPage, true, false);
            await Shot(printPage, $"print-preview-dark-{name}.png");
            await printPage.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The comparison mode after two versions were made on a desktop page, at every width")]
    public async Task CaptureDiff()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            await DocumentEditorE2EReset.ResetAsync();
            var page = await OpenAsync(1440, 900, false, "contract-demo");
            await DocumentEditorRedesignPhase7E2ETests.CreateTwoVersionsAsync(page);
            await page.SetViewportSizeAsync(width, height);
            await page.WaitForTimeoutAsync(800);
            await OpenInspectorTabAsync(page, "versions");
            await DocumentEditorRedesignPhase7E2ETests.VersionEntry(page, "Draft").GetByTestId("document-version-diff-base").ClickAsync();
            await DocumentEditorRedesignPhase7E2ETests.VersionEntry(page, "Final").GetByTestId("document-version-diff-compare").ClickAsync();
            await page.GetByTestId("document-version-show-diff").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await page.WaitForTimeoutAsync(600);
            await Shot(page, $"diff-{name}.png");
            await ApplyThemeAsync(page, true, true);
            await Shot(page, $"diff-dark-indigo-{name}.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The phone chrome: the action bar More, the header kebab with the host's action, the ribbon Home row")]
    public async Task CapturePhoneChrome()
    {
        foreach (var (name, width, height, touch) in Viewports.Skip(2))
        {
            var page = await OpenAsync(width, height, touch, "contract-demo");
            await Shot(page, $"phone-home-{name}.png");
            await page.GetByTestId("document-mobile-action-bar").Locator(".tm-mobile-action-bar__more").ClickAsync();
            await Shot(page, $"action-bar-more-{name}.png");
            await page.Keyboard.PressAsync("Escape");
            await page.GetByTestId("document-editor-header-slots").CheckAsync();
            await page.GetByTestId("document-header-kebab").ClickAsync();
            await Shot(page, $"header-kebab-{name}.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The image inspector (tablet side sheet, phone bottom sheet) and a forced phone layout on a wide screen")]
    public async Task CaptureForcedPhoneOnWide()
    {
        var page = await OpenAsync(1280, 800, false, RevisionsDocumentId);
        await page.GetByTestId("document-editor-layout-mode").SelectOptionAsync("Mobile");
        await page.WaitForFunctionAsync("() => document.querySelector('.tm-document-editor')?.dataset.layout === 'mobile'");
        await page.WaitForTimeoutAsync(500);
        await Shot(page, "forced-mobile-1280.png");
        await page.GetByTestId("document-canvas-hidden-input").FocusAsync();
        await page.Keyboard.PressAsync("Control+Shift+P");
        await Assertions.Expect(page.GetByTestId("document-command-palette")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Shot(page, "forced-mobile-palette-1280.png");
        await page.Context.CloseAsync();
    }

    private static async Task WaitReadyAsync(IPage page)
        => await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);

    private async Task<IPage> OpenAsync(int width, int height, bool touch, string documentId)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId={documentId}", WaitUntilState.DOMContentLoaded);
        await WaitReadyAsync(page);
        await page.WaitForTimeoutAsync(600);
        return page;
    }

    private static async Task Shot(IPage page, string fileName)
    {
        await page.EvaluateAsync("() => { document.querySelector('.tm-document-editor')?.scrollIntoView({ block: 'start', behavior: 'instant' }); window.scrollBy({ top: -72, behavior: 'instant' }); }");
        await page.WaitForTimeoutAsync(500);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(OutputDirectory, fileName), Type = ScreenshotType.Png, FullPage = false });
    }
}
