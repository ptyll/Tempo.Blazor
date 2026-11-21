using System.Text;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 3 (headless document runtime) E2E: the SAME document exported through the browser path
/// (editor captures its live canvas layout snapshot) and through the server path (no snapshot —
/// the demo API lays the stored document out headlessly via ITempoDocumentLayoutService) must
/// agree on pagination and carry the same text layer, and both PDFs must open in TmPdfViewer
/// (screenshots for UX review). Edge case: an empty document exported server-side still yields a
/// valid single-page PDF.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorHeadlessExportParityE2ETests : WasmTestBase
{
    private const string DocumentId = "contract-demo";
    private const string ApiBaseUrl = "https://localhost:5100";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    public async Task BrowserAndServerExports_AgreeOnPaginationAndTextLayer_AndOpenInTmPdfViewer()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add($"pageerror: {message}");
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={DocumentId}&showToolbar=true&preferLocalDraft=false", WaitUntilState.DOMContentLoaded);
        await WaitForEditorReadyAsync(page);

        // ── Browser path: the editor exports with its LIVE canvas layout snapshot. ──────────────
        await page.GetByTestId("document-ribbon-tab-references").ClickAsync();
        var exportButton = page.GetByTestId("document-export-pdf");
        await Assertions.Expect(exportButton).ToBeVisibleAsync(new() { Timeout = 15_000 });
        var downloadTask = page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = 60_000 });
        await exportButton.ClickAsync();
        var download = await downloadTask;
        var browserBase64 = await FetchAsBase64Async(page, download.Url);
        var browser = await ReadPdfAsync(page, browserBase64);

        // ── Server path: GET export carries no snapshot — the API lays out headlessly. ──────────
        var serverBase64 = await FetchAsBase64Async(page, $"{ApiBaseUrl}/api/document-editor/{DocumentId}/export/pdf");
        var server = await ReadPdfAsync(page, serverBase64);

        Assert.AreEqual(browser.PageCount, server.PageCount,
            "server-side headless layout must paginate like the editor's live canvas layout");
        foreach (var expected in new[] { "Service", "agreement", "provider" })
        {
            StringAssert.Contains(browser.Text, expected, $"browser export text layer must contain '{expected}'");
            StringAssert.Contains(server.Text, expected, $"server export text layer must contain '{expected}'");
        }

        var pdfHeader = Encoding.ASCII.GetString(Convert.FromBase64String(serverBase64), 0, 5);
        Assert.AreEqual("%PDF-", pdfHeader, "the server export must be a real PDF");

        // ── Both PDFs in TmPdfViewer — screenshots for UX review. ───────────────────────────────
        var screenshotDir = Path.Combine(FindRepositoryRootDirectory(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-headless-export");
        Directory.CreateDirectory(screenshotDir);
        await ScreenshotPdfInViewerAsync(context,
            $"{ApiBaseUrl}/api/document-editor/{DocumentId}/export/pdf/last",
            Path.Combine(screenshotDir, "browser-export-in-tm-pdf-viewer.png"));
        await ScreenshotPdfInViewerAsync(context,
            $"{ApiBaseUrl}/api/document-editor/{DocumentId}/export/pdf",
            Path.Combine(screenshotDir, "server-export-in-tm-pdf-viewer.png"));
        TestContext.AddResultFile(Path.Combine(screenshotDir, "browser-export-in-tm-pdf-viewer.png"));
        TestContext.AddResultFile(Path.Combine(screenshotDir, "server-export-in-tm-pdf-viewer.png"));

        Assert.AreEqual(0, errors.Count, $"Unexpected page errors:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
    }

    /// <summary>Edge case: a snapshot-less server export of an empty document yields a valid one-page PDF.</summary>
    [TestMethod]
    public async Task ServerExport_EmptyDocument_YieldsValidSinglePagePdf()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/", WaitUntilState.DOMContentLoaded);

        var base64 = await page.EvaluateAsync<string>(
            """
            async apiBase => {
                const response = await fetch(`${apiBase}/api/document-editor/e2e-empty-headless/export/pdf`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ documentId: 'e2e-empty-headless', document: {} }),
                });
                if (!response.ok) throw new Error(`export failed: ${response.status}`);
                const result = await response.json();
                return result.content;
            }
            """,
            ApiBaseUrl);

        var pdf = Convert.FromBase64String(base64);
        Assert.AreEqual("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5), "empty document must export a structurally valid PDF");

        var parsed = await ReadPdfAsync(page, base64);
        Assert.AreEqual(1, parsed.PageCount, "an empty document lays out as exactly one page");
    }

    private static Task WaitForEditorReadyAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-testid="document-canvas-page"]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-references"]')
            """,
            options: new PageWaitForFunctionOptions { Timeout = 45_000 });

    private static Task<string> FetchAsBase64Async(IPage page, string url)
        => page.EvaluateAsync<string>(
            """
            async url => {
                const response = await fetch(url);
                if (!response.ok) throw new Error(`fetch ${url} failed: ${response.status}`);
                const buffer = await response.arrayBuffer();
                const bytes = new Uint8Array(buffer);
                let binary = '';
                for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
                return btoa(binary);
            }
            """,
            url);

    private static async Task<(int PageCount, string Text)> ReadPdfAsync(IPage page, string base64)
    {
        var result = await page.EvaluateAsync<string>(
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
                return `${doc.numPages}::${parts.join(' ')}`;
            }
            """,
            base64);
        var separator = result.IndexOf("::", StringComparison.Ordinal);
        return (int.Parse(result[..separator], System.Globalization.CultureInfo.InvariantCulture), result[(separator + 2)..]);
    }

    private async Task ScreenshotPdfInViewerAsync(IBrowserContext context, string pdfUrl, string screenshotPath)
    {
        var viewerPage = await context.NewPageAsync();
        await GotoAppReadyAsync(viewerPage,
            $"{BaseUrl}/pdf-viewer?url={Uri.EscapeDataString(pdfUrl)}", WaitUntilState.DOMContentLoaded);
        var viewerCanvas = viewerPage.Locator("[data-testid='pdf-basic-usage-card'] .tm-pdf-viewer__canvas").First;
        await Assertions.Expect(viewerCanvas).ToBeVisibleAsync(new() { Timeout = 45_000 });
        await viewerPage.WaitForFunctionAsync(
            """
            () => {
                const canvas = document.querySelector('[data-testid="pdf-basic-usage-card"] .tm-pdf-viewer__canvas');
                return !!canvas && canvas.clientHeight > 400;
            }
            """,
            options: new PageWaitForFunctionOptions { Timeout = 45_000 });
        await viewerPage.Locator("[data-testid='pdf-basic-usage-card']").ScreenshotAsync(new LocatorScreenshotOptions { Path = screenshotPath });
        await viewerPage.CloseAsync();
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
