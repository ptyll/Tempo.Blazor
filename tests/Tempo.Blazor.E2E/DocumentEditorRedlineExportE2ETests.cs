using System.IO.Compression;
using System.Text;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 2 redline flow: compare the edited document against its saved version, export the
/// comparison as a tracked-changes DOCX (w:ins/w:del), reimport it, and see the revisions in the
/// editor's track-changes UI.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedlineExportE2ETests : WasmTestBase
{
    private const string DocumentId = "phase-12-canvas-history-save";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    public async Task CompareExportRedlineDocx_Reimport_ShowsTrackedChanges()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page);

        // 1) Edit the current document so current-vs-saved has a real diff.
        var marker = $"redline{DateTimeOffset.UtcNow:HHmmssfff}";
        await TypeMarkerAsync(page, marker);

        // 2) Compare the SAVED version (base = v1) against the edited current document (target = v2),
        //    so the typed marker is an INSERTION in the redline.
        await page.GetByTestId("document-ribbon-tab-review").ClickAsync();
        var workspaceBefore = await DocumentEditorLayoutShiftGuard.MeasureWorkspaceAsync(page);
        await page.GetByTestId("document-compare-open").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-compare-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await DocumentEditorLayoutShiftGuard.AssertWorkspaceUnchangedAsync(page, workspaceBefore, "opening the compare dialog");
        await page.GetByTestId("document-compare-base-source").SelectOptionAsync("DocumentId");
        await page.GetByTestId("document-compare-base-document-id").FillAsync(DocumentId);
        await page.GetByTestId("document-compare-target-source").SelectOptionAsync("Current");
        await page.GetByTestId("document-compare-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await DocumentEditorLayoutShiftGuard.AssertWorkspaceUnchangedAsync(page, workspaceBefore, "rendering the compare result");

        var screenshotDir = Path.Combine(FindRepositoryRootDirectory(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-redline");
        Directory.CreateDirectory(screenshotDir);
        await page.GetByTestId("document-compare-dialog").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(screenshotDir, "01-compare-dialog-with-export.png"),
            Type = ScreenshotType.Png
        });

        // 3) Export the redline DOCX through the new dialog action.
        var exportButton = page.GetByTestId("document-compare-export-redline");
        await Assertions.Expect(exportButton).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var download = await page.RunAndWaitForDownloadAsync(async () => await exportButton.ClickAsync());
        StringAssert.EndsWith(download.SuggestedFilename, ".docx", StringComparison.OrdinalIgnoreCase);
        var docxPath = await download.PathAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(docxPath), "redline DOCX must be downloadable");

        // 4) The DOCX carries real w:ins tracked changes (the deleted saved-side text yields w:del too
        //    when present; the typed marker is an insertion).
        var documentXml = ReadDocumentXml(docxPath!);
        StringAssert.Contains(documentXml, "<w:ins ", "redline DOCX must contain w:ins tracked changes");
        StringAssert.Contains(documentXml, marker, "the typed marker must be part of the tracked changes");

        await page.GetByTestId("document-compare-close").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-compare-dialog")).ToBeHiddenAsync(new() { Timeout = 5_000 });

        // 5) Reimport the redline DOCX and verify the revisions arrive in the track-changes UI.
        await page.GetByTestId("document-ribbon-tab-references").ClickAsync();
        await page.GetByTestId("document-import-docx-label").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-import-docx-panel")).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await page.GetByTestId("document-import-docx").SetInputFilesAsync(docxPath!);
        await WaitForPredicateAsync(page, """
            () => {
                const pages = Array.from(document.querySelectorAll('[data-testid="document-canvas-page"]'));
                return pages.reduce((total, p) => total + Number(p.getAttribute('data-canvas-revision-anchor-count') || '0'), 0) > 0;
            }
            """, 30_000);

        // 6) The revisions side panel lists the imported tracked changes.
        await page.GetByTestId("document-side-panel-tab-revisions").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-revision-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var revisionEntries = page.Locator("[data-testid='document-revision-panel'] [data-revision-id]");
        await Assertions.Expect(revisionEntries.First).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-canvas-a11y-mirror"))
            .ToContainTextAsync(marker, new() { Timeout = 10_000 });

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(screenshotDir, "02-reimported-redline-track-changes.png"),
            Type = ScreenshotType.Png
        });
    }

    /// <summary>Edge case: comparing identical documents offers no redline export.</summary>
    [TestMethod]
    public async Task Compare_IdenticalDocuments_OffersNoRedlineExport()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page);

        await page.GetByTestId("document-ribbon-tab-review").ClickAsync();
        await page.GetByTestId("document-compare-open").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-compare-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-compare-target-document-id").FillAsync(DocumentId);
        await page.GetByTestId("document-compare-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Assertions.Expect(page.GetByTestId("document-compare-export-redline")).ToHaveCountAsync(0);
    }

    private async Task OpenDocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={DocumentId}&showToolbar=true&preferLocalDraft=false", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-history-text"]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 45_000);
    }

    private static async Task TypeMarkerAsync(IPage page, string marker)
    {
        var point = await page.EvaluateAsync<int[]>(
            """
            () => {
                const rects = Array.from(document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-history-text"]'))
                    .map(node => node.getBoundingClientRect());
                const last = rects[rects.length - 1];
                return [Math.round(last.right - 4), Math.round(last.top + last.height / 2)];
            }
            """);
        await page.Mouse.ClickAsync(point[0], point[1]);
        await page.EvaluateAsync("() => document.querySelector('[data-testid=\"document-canvas-hidden-input\"]')?.focus()");
        await page.Keyboard.TypeAsync($" {marker}");
        await Assertions.Expect(page.GetByTestId("document-canvas-a11y-mirror"))
            .ToContainTextAsync(marker, new() { Timeout = 10_000 });
    }

    private static string ReadDocumentXml(string docxPath)
    {
        using var archive = ZipFile.OpenRead(docxPath);
        var entry = archive.GetEntry("word/document.xml")!;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string FindRepositoryRootDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
