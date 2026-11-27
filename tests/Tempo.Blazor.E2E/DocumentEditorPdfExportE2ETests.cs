using System.Text;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 1 (PDF renderer) E2E: exporting from the canvas document editor must produce a vector PDF
/// with a searchable text layer whose pagination mirrors the editor (WYSIWYG parity), and the
/// exported file must open in TmPdfViewer. Part of the smoke lane — this is the first PDF gate.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[TestCategory("Smoke")]
[DoNotParallelize]
public sealed class DocumentEditorPdfExportE2ETests : WasmTestBase
{
    private const string DocumentId = "phase-12-canvas-history-save";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    public async Task ExportPdf_FromCanvasEditor_ProducesTextLayerPdfAndOpensInTmPdfViewer()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add($"pageerror: {message}");
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={DocumentId}&showToolbar=true&preferLocalDraft=false", WaitUntilState.DOMContentLoaded);
        await WaitForEditorReadyAsync(page);

        // Export through the production toolbar path (References → Export PDF).
        await page.GetByTestId("document-ribbon-tab-references").ClickAsync();
        var exportButton = page.GetByTestId("document-export-pdf");
        await Assertions.Expect(exportButton).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var downloadTask = page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = 60_000 });
        await exportButton.ClickAsync();
        var download = await downloadTask;
        StringAssert.EndsWith(download.SuggestedFilename, ".pdf", StringComparison.OrdinalIgnoreCase);

        // Blob downloads are flaky to read from disk under the .NET runner — fetch the blob URL instead.
        var base64 = await page.EvaluateAsync<string>(
            """
            async url => {
                const response = await fetch(url);
                const buffer = await response.arrayBuffer();
                const bytes = new Uint8Array(buffer);
                let binary = '';
                for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
                return btoa(binary);
            }
            """,
            download.Url);
        var pdf = Convert.FromBase64String(base64);
        Assert.AreEqual("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5), "export must be a real PDF");

        // Text-layer proof: PDF.js must extract the document's own text (not raster pixels).
        var textContent = await page.EvaluateAsync<string>(
            """
            async base64 => {
                const pdfjs = await import('/_content/Tempo.Blazor.PdfViewer/js/pdf.min.mjs');
                pdfjs.GlobalWorkerOptions.workerSrc = '/_content/Tempo.Blazor.PdfViewer/js/pdf.worker.min.mjs';
                const bytes = Uint8Array.from(atob(base64), ch => ch.charCodeAt(0));
                const doc = await pdfjs.getDocument({ data: bytes }).promise;
                const parts = [];
                for (let pageNumber = 1; pageNumber <= doc.numPages; pageNumber++) {
                    const pdfPage = await doc.getPage(pageNumber);
                    const content = await pdfPage.getTextContent();
                    parts.push(content.items.map(item => item.str).join(' '));
                }
                return `pages=${doc.numPages}::${parts.join(' ')}`;
            }
            """,
            base64);
        StringAssert.Contains(textContent, "Formatting", "body text must survive into the PDF text layer");
        StringAssert.Contains(textContent, "Category", "table content must survive into the PDF text layer");

        // WYSIWYG parity: the PDF page count equals the editor's pagination.
        var editorPageCount = await page.EvaluateAsync<int>(
            "() => document.querySelectorAll('[data-testid=\"document-canvas-page\"]').length");
        var pdfPageCount = int.Parse(textContent.Split("::")[0].Replace("pages=", ""), System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreEqual(editorPageCount, pdfPageCount, "PDF pagination must mirror the editor layout");

        // Open the export in the real TmPdfViewer demo (served by the last-export API endpoint).
        var viewerPage = await context.NewPageAsync();
        await GotoAppReadyAsync(viewerPage,
            $"{BaseUrl}/pdf-viewer?url={Uri.EscapeDataString($"https://localhost:5100/api/document-editor/{DocumentId}/export/pdf/last")}", WaitUntilState.DOMContentLoaded);
        var viewerCanvas = viewerPage.Locator("[data-testid='pdf-basic-usage-card'] .tm-pdf-viewer__canvas").First;
        await Assertions.Expect(viewerCanvas).ToBeVisibleAsync(new() { Timeout = 45_000 });
        // Wait for the canvas to reach the real rendered page size (pre-render default is 300×150).
        await WaitForPredicateAsync(viewerPage, """
            () => {
                const canvas = document.querySelector('[data-testid="pdf-basic-usage-card"] .tm-pdf-viewer__canvas');
                return !!canvas && canvas.clientHeight > 400;
            }
            """, 45_000);

        var screenshotDir = Path.Combine(FindRepositoryRootDirectory(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-pdf-export");
        Directory.CreateDirectory(screenshotDir);
        var screenshotPath = Path.Combine(screenshotDir, "exported-pdf-in-tm-pdf-viewer.png");
        await viewerPage.Locator("[data-testid='pdf-basic-usage-card']").ScreenshotAsync(new LocatorScreenshotOptions { Path = screenshotPath });
        TestContext.AddResultFile(screenshotPath);

        Assert.AreEqual(0, errors.Count, $"Unexpected page errors:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
    }

    /// <summary>Edge case: exporting an empty document still yields a valid single-page PDF.</summary>
    [TestMethod]
    public async Task ExportPdf_EmptyDocument_YieldsValidPdf()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-3-canvas-empty&showToolbar=true&preferLocalDraft=false", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelector('[data-testid="document-export-pdf"], [data-testid="document-ribbon-tab-references"]')
            """, 45_000);

        await page.GetByTestId("document-ribbon-tab-references").ClickAsync();
        var downloadTask = page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = 60_000 });
        await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-export-pdf");
        var download = await downloadTask;

        var header = await page.EvaluateAsync<string>(
            """
            async url => {
                const response = await fetch(url);
                const buffer = await response.arrayBuffer();
                const bytes = new Uint8Array(buffer.slice(0, 5));
                return String.fromCharCode(...bytes);
            }
            """,
            download.Url);
        Assert.AreEqual("%PDF-", header, "even an empty document must export a structurally valid PDF");
    }

    private static Task WaitForEditorReadyAsync(IPage page)
        => WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-testid="document-canvas-page"]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-references"]')
            """, 45_000);

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
