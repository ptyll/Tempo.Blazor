using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 3: DocumentWatermarkOptions must survive into the PDF export — the watermark visible in
/// the canvas view (text, diagonal, opacity, every page) prints in the exported PDF, and the
/// forensic variant stamps reader name + export time + IP on every page.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorWatermarkPdfE2ETests : WasmTestBase
{
    private const string WatermarkDocumentId = "phase-e12-canvas-hyphenation-advanced-tables";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    public async Task WatermarkedDocument_ExportsPdfWithDiagonalWatermarkMatchingView()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenAsync(page, $"/canvas-engine-host?documentId={WatermarkDocumentId}&showToolbar=true&preferLocalDraft=false");

        var screenshotDir = ScreenshotDir();
        await page.Locator("[data-testid='document-canvas-page']").First.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(screenshotDir, "01-watermark-in-editor-view.png"),
            Type = ScreenshotType.Png
        });

        var pdfBase64 = await ExportPdfAsync(page);
        var probe = await ProbePdfAsync(page, pdfBase64);

        Assert.IsTrue(probe.RotatedTexts.Contains("E12"),
            $"the E12 watermark must print as rotated text (rotated items: {string.Join(", ", probe.RotatedTexts)})");

        // Render page 1 of the exported PDF next to the editor view for the visual diff.
        await RenderPdfPageToCanvasAsync(page, pdfBase64);
        await page.Locator("#tm-e2e-pdf-preview").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(screenshotDir, "02-watermark-in-exported-pdf.png"),
            Type = ScreenshotType.Png
        });
    }

    [TestMethod]
    public async Task ForensicWatermark_StampsUserTimeAndIpOnEveryPage()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenAsync(page, $"/canvas-engine-host?documentId={WatermarkDocumentId}&showToolbar=true&preferLocalDraft=false&forensicPdf=true");

        var pdfBase64 = await ExportPdfAsync(page);
        var probe = await ProbePdfAsync(page, pdfBase64);

        var forensicPerPage = probe.PageTexts
            .Select(pageText => pageText.Contains("Canvas Demo User") && pageText.Contains("UTC"))
            .ToList();
        Assert.IsTrue(forensicPerPage.All(hasStamp => hasStamp),
            $"every page must carry the forensic stamp (pages: {string.Join(", ", forensicPerPage)})");
        Assert.IsTrue(probe.FullText.Contains("Canvas Demo User"), "stamp carries the exporting user's name");
        Assert.IsTrue(probe.FullText.Contains("UTC"), "stamp carries the export timestamp");
    }

    /// <summary>Edge case: a document without watermark options exports without any rotated stamp.</summary>
    [TestMethod]
    public async Task DocumentWithoutWatermark_ExportsWithoutRotatedText()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render&showToolbar=true&preferLocalDraft=false");

        var pdfBase64 = await ExportPdfAsync(page);
        var probe = await ProbePdfAsync(page, pdfBase64);

        Assert.AreEqual(0, probe.RotatedTexts.Count,
            $"no watermark configured → no rotated text may print (got: {string.Join(", ", probe.RotatedTexts)})");
    }

    private async Task OpenAsync(IPage page, string url)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}{url}", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-testid="document-canvas-page"]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-references"]')
            """, 45_000);
    }

    private static async Task<string> ExportPdfAsync(IPage page)
    {
        await page.GetByTestId("document-ribbon-tab-references").ClickAsync();
        var exportButton = page.GetByTestId("document-export-pdf");
        await Assertions.Expect(exportButton).ToBeEnabledAsync(new() { Timeout = 15_000 });
        var download = await page.RunAndWaitForDownloadAsync(async () => await exportButton.ClickAsync());
        return await page.EvaluateAsync<string>(
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
    }

    private sealed record PdfProbe(List<string> RotatedTexts, List<string> PageTexts, string FullText);

    private static async Task<PdfProbe> ProbePdfAsync(IPage page, string pdfBase64)
    {
        var json = await page.EvaluateAsync<string>(
            """
            async base64 => {
                const pdfjs = await import('/_content/Tempo.Blazor.PdfViewer/js/pdf.min.mjs');
                pdfjs.GlobalWorkerOptions.workerSrc = '/_content/Tempo.Blazor.PdfViewer/js/pdf.worker.min.mjs';
                const bytes = Uint8Array.from(atob(base64), ch => ch.charCodeAt(0));
                const doc = await pdfjs.getDocument({ data: bytes }).promise;
                const rotated = [];
                const pageTexts = [];
                for (let pageNumber = 1; pageNumber <= doc.numPages; pageNumber++) {
                    const pdfPage = await doc.getPage(pageNumber);
                    const content = await pdfPage.getTextContent();
                    pageTexts.push(content.items.map(item => item.str).join(' '));
                    for (const item of content.items) {
                        if (Math.abs(item.transform[1]) > 0.01 && item.str.trim().length > 0) {
                            rotated.push(item.str.trim());
                        }
                    }
                }
                return JSON.stringify({ rotated, pageTexts });
            }
            """,
            pdfBase64);
        using var parsed = JsonDocument.Parse(json);
        var rotated = parsed.RootElement.GetProperty("rotated").EnumerateArray().Select(item => item.GetString() ?? "").ToList();
        var pageTexts = parsed.RootElement.GetProperty("pageTexts").EnumerateArray().Select(item => item.GetString() ?? "").ToList();
        return new PdfProbe(rotated, pageTexts, string.Join(" ", pageTexts));
    }

    private static Task RenderPdfPageToCanvasAsync(IPage page, string pdfBase64)
        => page.EvaluateAsync(
            """
            async base64 => {
                const pdfjs = await import('/_content/Tempo.Blazor.PdfViewer/js/pdf.min.mjs');
                pdfjs.GlobalWorkerOptions.workerSrc = '/_content/Tempo.Blazor.PdfViewer/js/pdf.worker.min.mjs';
                const bytes = Uint8Array.from(atob(base64), ch => ch.charCodeAt(0));
                const doc = await pdfjs.getDocument({ data: bytes }).promise;
                const pdfPage = await doc.getPage(1);
                const viewport = pdfPage.getViewport({ scale: 1.2 });
                let canvas = document.getElementById('tm-e2e-pdf-preview');
                if (!canvas) {
                    canvas = document.createElement('canvas');
                    canvas.id = 'tm-e2e-pdf-preview';
                    canvas.style.position = 'fixed';
                    canvas.style.top = '0';
                    canvas.style.left = '0';
                    canvas.style.zIndex = '99999';
                    canvas.style.background = '#fff';
                    document.body.appendChild(canvas);
                }
                canvas.width = viewport.width;
                canvas.height = viewport.height;
                await pdfPage.render({ canvasContext: canvas.getContext('2d'), viewport }).promise;
            }
            """,
            pdfBase64);

    private static string ScreenshotDir()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        var dir = Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-watermark");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
