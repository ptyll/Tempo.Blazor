using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 1 (wave 0): the "before" SCREENSHOTS of the editor chrome. Capture only - the
/// behaviour assertions (modality, tokens, layout shift, localized text) live in
/// <see cref="DocumentEditorModalityE2ETests"/>, which writes no files.
/// <para>
/// Like the other generators it only runs when <c>TM_WRITE_BASELINES</c> is set:
/// <code>
/// TM_WRITE_BASELINES=1 dotnet test tests/Tempo.Blazor.E2E --filter "DocumentEditorRedesignBaselineE2ETests"
/// </code>
/// The PNGs land in <c>__screenshots__/document-editor-redesign/phase-1-baseline/</c> (default) or in the folder
/// named by <c>TM_DOCUMENT_EDITOR_BASELINE_DIR</c>, so a later phase captures into a sibling folder and diffs.
/// </para>
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public class DocumentEditorRedesignBaselineE2ETests : BaselineGeneratorTestBase
{
    private static string OutputDirectory
    {
        get
        {
            var target = Environment.GetEnvironmentVariable("TM_DOCUMENT_EDITOR_BASELINE_DIR");
            var name = string.IsNullOrWhiteSpace(target) ? "phase-1-baseline" : target.Trim();
            var directory = Path.Combine(
                DocumentEditorRedesignE2EHelpers.FindRepositoryRootDirectory(),
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-redesign", name);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    [Description("Writes the pre-redesign main-surface screenshots at 1440, 1024 (touch), 390 and 320")]
    public async Task CaptureMainSurface()
    {
        foreach (var viewport in DocumentEditorRedesignE2EHelpers.SurfaceViewports)
        {
            var page = await OpenAsync(DocumentEditorRedesignE2EHelpers.HistoryDocumentId, viewport.Width, viewport.Height, viewport.Touch);
            await Shot(page, $"editor-{viewport.Name}.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Writes the find panel baselines (light + dark) at 1440 and 390")]
    public async Task CaptureFindPanel()
    {
        foreach (var viewport in DocumentEditorRedesignE2EHelpers.PanelViewports)
        {
            var page = await OpenAsync(DocumentEditorRedesignE2EHelpers.HistoryDocumentId, viewport.Width, viewport.Height, viewport.Touch);
            await page.GetByTestId("document-canvas-engine-host").ClickAsync();
            await page.Keyboard.PressAsync("Control+F");
            await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.GetByTestId("document-find-input").FillAsync("history");
            await page.GetByTestId("document-find-panel").ScrollIntoViewIfNeededAsync();
            await Shot(page, $"find-panel-light-{viewport.Name}.png");
            await DocumentEditorRedesignE2EHelpers.ApplyDarkThemeAsync(page);
            await Shot(page, $"find-panel-dark-{viewport.Name}.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Writes the compare dialog baselines (light + dark) at 1440 and 390")]
    public async Task CaptureCompareDialog()
    {
        foreach (var viewport in DocumentEditorRedesignE2EHelpers.PanelViewports)
        {
            var page = await OpenAsync(DocumentEditorRedesignE2EHelpers.HistoryDocumentId, viewport.Width, viewport.Height, viewport.Touch);
            await page.GetByTestId("document-ribbon-tab-review").ClickAsync();
            await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-compare-open");
            await Assertions.Expect(page.GetByTestId("document-compare-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"compare-dialog-{viewport.Name}.png");
            await DocumentEditorRedesignE2EHelpers.ApplyDarkThemeAsync(page);
            await Shot(page, $"compare-dialog-dark-{viewport.Name}.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Writes the version dialog and version panel baselines at 1440 and 390")]
    public async Task CaptureVersionDialogAndPanel()
    {
        foreach (var viewport in DocumentEditorRedesignE2EHelpers.PanelViewports)
        {
            // The canvas-engine-host demo hides the version history; the full editor page offers it.
            var page = await OpenAsync(DocumentEditorRedesignE2EHelpers.HistoryDocumentId, viewport.Width, viewport.Height, viewport.Touch, "/document-editor");
            await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-ribbon-tab-view");
            await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-open-versions");
            await Assertions.Expect(page.GetByTestId("document-version-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.GetByTestId("document-version-create-open").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-version-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"version-dialog-{viewport.Name}.png");
            await page.GetByTestId("document-version-label").FillAsync("Baseline");
            await page.GetByTestId("document-version-create-submit").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-version-dialog")).ToBeHiddenAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(page.GetByTestId("document-version-item").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"version-panel-{viewport.Name}.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Writes the revision panel baselines at 1440 and 390")]
    public async Task CaptureRevisionPanel()
    {
        foreach (var viewport in DocumentEditorRedesignE2EHelpers.PanelViewports)
        {
            var page = await OpenAsync(DocumentEditorRedesignE2EHelpers.RevisionsDocumentId, viewport.Width, viewport.Height, viewport.Touch);
            await page.GetByTestId("document-side-panel-tab-revisions").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-revision-item").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Shot(page, $"revision-panel-{viewport.Name}.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Writes the JSON and clipboard-HTML debug modal baselines (JSON also dark) at 1440 and 390")]
    public async Task CaptureDebugModals()
    {
        foreach (var viewport in DocumentEditorRedesignE2EHelpers.PanelViewports)
        {
            var page = await OpenAsync(DocumentEditorRedesignE2EHelpers.HistoryDocumentId, viewport.Width, viewport.Height, viewport.Touch);
            await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-ribbon-tab-view");
            await Shot(page, $"ribbon-view-tab-{viewport.Name}.png");

            await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-view-json");
            await Assertions.Expect(page.GetByTestId("document-json-debug-modal")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"json-debug-modal-{viewport.Name}.png");
            await DocumentEditorRedesignE2EHelpers.ApplyDarkThemeAsync(page);
            await Shot(page, $"json-debug-modal-dark-{viewport.Name}.png");
            await page.GetByTestId("document-json-debug-close").ClickAsync();

            await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-view-clipboard-html");
            await Assertions.Expect(page.GetByTestId("document-clipboard-html-debug-modal")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"clipboard-html-debug-modal-{viewport.Name}.png");
            await page.Context.CloseAsync();
        }
    }

    private async Task<IPage> OpenAsync(string documentId, int width, int height, bool touch, string route = "/canvas-engine-host")
    {
        var context = await CreateContextAsync(DocumentEditorRedesignE2EHelpers.ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, DocumentEditorRedesignE2EHelpers.OpenUrl(BaseUrl, documentId, route), WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 45_000);
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private static async Task Shot(IPage page, string fileName)
    {
        await page.WaitForTimeoutAsync(300);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, fileName),
            Type = ScreenshotType.Png,
            FullPage = false
        });
    }
}
