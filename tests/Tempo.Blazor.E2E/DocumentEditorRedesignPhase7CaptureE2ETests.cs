using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 7 (DE-1.8 + DE-2.6): the "after" screenshots of the print preview, the compare dialog (sources as cards, the
/// result, the picker), the comparison mode (side by side, unified, the version panel that opens it) and the status bar at 1440, 1024 (touch)
/// and 390. Gated by <c>TM_WRITE_BASELINES</c>; the PNGs land in
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/document-editor-redesign/phase-7/</c> (override the folder name with
/// <c>TM_DOCUMENT_EDITOR_BASELINE_DIR</c>). The specs that assert behaviour are <see cref="DocumentEditorRedesignPhase7E2ETests"/>.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase7CaptureE2ETests : BaselineGeneratorTestBase
{
    private static readonly (string Name, int Width, int Height, bool Touch)[] Viewports =
    [
        ("1440", 1440, 900, false),
        ("1024-touch", 1024, 768, true),
        ("390", 390, 844, true),
    ];

    private static string OutputDirectory
    {
        get
        {
            var target = Environment.GetEnvironmentVariable("TM_DOCUMENT_EDITOR_BASELINE_DIR");
            var name = string.IsNullOrWhiteSpace(target) ? "phase-7" : target.Trim();
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
    [Description("The print preview at 1440 / 1024 touch / 390 (1440 also dark)")]
    public async Task CapturePrintPreview()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenHistoryAsync(width, height, touch);
            await ClickPopupItemAsync(page, "document-ribbon-tab-view", "");
            await ClickRibbonAsync(page, "document-open-print-preview");
            await Assertions.Expect(page.GetByTestId("document-print-preview-actions")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Shot(page, $"print-preview-{name}.png");
            if (width == 1440)
            {
                await ApplyDarkThemeAsync(page);
                await Shot(page, $"print-preview-dark-{name}.png");
                await ApplyLightThemeAsync(page);
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The compare dialog: the two source cards, the picker opened under a card, and the result with its summary and the list of changes")]
    public async Task CaptureCompareDialog()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenHistoryAsync(width, height, touch);
            await ClickPopupItemAsync(page, "document-ribbon-tab-review", "");
            await ClickRibbonAsync(page, "document-compare-open");
            await Assertions.Expect(page.GetByTestId("document-compare-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"compare-dialog-{name}.png");

            await page.GetByTestId("document-compare-target-change").ClickAsync();
            await Shot(page, $"compare-dialog-picker-{name}.png");

            await page.GetByTestId("document-compare-target-source").SelectOptionAsync("Current");
            await page.GetByTestId("document-compare-run").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-compare-no-differences")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Shot(page, $"compare-dialog-no-differences-{name}.png");
            if (width == 1440)
            {
                await ApplyDarkThemeAsync(page);
                await Shot(page, $"compare-dialog-dark-{name}.png");
                await ApplyLightThemeAsync(page);
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The comparison mode opened from the version panel: side by side (all viewports), unified (1440 and 390) and dark (1440)")]
    public async Task CaptureComparisonMode()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            await DocumentEditorE2EReset.ResetAsync();
            var page = await OpenEditorAsync(width, height, touch);
            await DocumentEditorRedesignPhase7E2ETests.CreateTwoVersionsAsync(page);
            if (width < 1024)
            {
                // On a phone the inspector is a sheet; the panel is already the open one here.
                await page.WaitForTimeoutAsync(200);
            }

            await DocumentEditorRedesignPhase7E2ETests.VersionEntry(page, "Draft").GetByTestId("document-version-diff-base").ClickAsync();
            await DocumentEditorRedesignPhase7E2ETests.VersionEntry(page, "Final").GetByTestId("document-version-diff-compare").ClickAsync();
            await Shot(page, $"version-panel-show-diff-{name}.png");
            await page.GetByTestId("document-version-show-diff").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await page.GetByTestId("document-diff-next").ClickAsync();
            await Shot(page, $"diff-side-by-side-{name}.png");

            if (width != 1024)
            {
                await page.GetByTestId("document-diff-view-mode").SelectOptionAsync("Unified");
                await Shot(page, $"diff-unified-{name}.png");
                await page.GetByTestId("document-diff-view-mode").SelectOptionAsync("SideBySide");
            }

            if (width == 1440)
            {
                await ApplyDarkThemeAsync(page);
                await Shot(page, $"diff-side-by-side-dark-{name}.png");
                await ApplyLightThemeAsync(page);
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The status bar with the page, the words, the zoom controls and the fullscreen switch at 1440 and 390")]
    public async Task CaptureStatusBar()
    {
        foreach (var (name, width, height, touch) in new[] { Viewports[0], Viewports[2] })
        {
            var page = await OpenHistoryAsync(width, height, touch);
            await Assertions.Expect(page.GetByTestId("document-status-bar")).ToBeVisibleAsync();
            await page.GetByTestId("document-status-bar").ScrollIntoViewIfNeededAsync();
            await page.GetByTestId("document-status-bar").ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(OutputDirectory, $"status-bar-{name}.png"), Type = ScreenshotType.Png });
            await page.Context.CloseAsync();
        }
    }

    private async Task<IPage> OpenHistoryAsync(int width, int height, bool touch)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={HistoryDocumentId}&showToolbar=true&preferLocalDraft=false", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, $$"""
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect][data-block-id="{{DocumentEditorRedesignPhase7E2ETests.HistoryText}}"]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        return page;
    }

    private async Task<IPage> OpenEditorAsync(int width, int height, bool touch)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?bounded=false&documentId=contract-demo", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        return page;
    }

    private static async Task Shot(IPage page, string fileName)
    {
        await page.WaitForTimeoutAsync(500);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(OutputDirectory, fileName), Type = ScreenshotType.Png, FullPage = false });
    }
}
