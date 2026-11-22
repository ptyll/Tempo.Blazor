using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 4 (headless document runtime) E2E: the demo assembly template (IF/ELSE over
/// contract.amount, repeating items, computed total and due date) rendered purely server-side
/// with two different datasets must yield two correct PDFs — the high-amount dataset takes the
/// director-approval branch and sums its items, the low-amount dataset takes the ELSE branch —
/// plus per-page PNG previews captured as screenshots for UX review.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[DoNotParallelize]
public sealed class DocumentEditorHeadlessAssemblyE2ETests : WasmTestBase
{
    private const string ApiBaseUrl = "https://localhost:5100";

    [TestMethod]
    public async Task AssemblyTemplate_TwoDatasets_YieldTwoCorrectPdfsAndPngPreviews()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/", WaitUntilState.DOMContentLoaded);

        // ── Dataset A: amount 25 000 → IF branch (director approval), two items. ────────────────
        var highText = await RenderPdfTextAsync(page, amount: 25000,
            ("Servis A", "15000"), ("Servis B", "10000"));
        StringAssert.Contains(highText, "Acme", "token values must be assembled");
        StringAssert.Contains(highText, "ředitele", "amount 25000 must take the IF branch");
        Assert.IsFalse(highText.Contains("běžném", StringComparison.Ordinal), "the ELSE branch must be dropped for high amounts");
        StringAssert.Contains(highText, "Servis", "repeating rows must be expanded");
        StringAssert.Contains(highText, "Kč", "the computed total must be present");

        // ── Dataset B: amount 5 000 → ELSE branch, single item. ─────────────────────────────────
        var lowText = await RenderPdfTextAsync(page, amount: 5000, ("Servis C", "5000"));
        StringAssert.Contains(lowText, "běžném", "amount 5000 must take the ELSE branch");
        Assert.IsFalse(lowText.Contains("ředitele", StringComparison.Ordinal), "the IF branch must be dropped for low amounts");

        // ── PNG previews of both datasets — screenshots for UX review. ──────────────────────────
        var screenshotDir = Path.Combine(FindRepositoryRootDirectory(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-headless-assembly");
        Directory.CreateDirectory(screenshotDir);
        await SavePngPreviewAsync(page, screenshotDir, "assembly-dataset-high-amount.png", amount: 25000,
            ("Servis A", "15000"), ("Servis B", "10000"));
        await SavePngPreviewAsync(page, screenshotDir, "assembly-dataset-low-amount.png", amount: 5000,
            ("Servis C", "5000"));
        TestContext.AddResultFile(Path.Combine(screenshotDir, "assembly-dataset-high-amount.png"));
        TestContext.AddResultFile(Path.Combine(screenshotDir, "assembly-dataset-low-amount.png"));
    }

    private static string DatasetJson(int amount, params (string Name, string Price)[] items)
        => JsonSerializer.Serialize(new
        {
            values = new Dictionary<string, string?>
            {
                ["contract.client"] = "Acme s.r.o.",
                ["contract.amount"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            itemRows = items.Select(item => new Dictionary<string, string?>
            {
                ["name"] = item.Name,
                ["price"] = item.Price,
            }).ToArray(),
        });

    private static async Task<string> RenderPdfTextAsync(IPage page, int amount, params (string Name, string Price)[] items)
    {
        var base64 = await page.EvaluateAsync<string>(
            """
            async ([apiBase, body]) => {
                const response = await fetch(`${apiBase}/api/document-editor/assembly/render`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body,
                });
                if (!response.ok) throw new Error(`assembly render failed: ${response.status}`);
                const buffer = await response.arrayBuffer();
                const bytes = new Uint8Array(buffer);
                let binary = '';
                for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
                return btoa(binary);
            }
            """,
            new[] { ApiBaseUrl, DatasetJson(amount, items) });

        var pdf = Convert.FromBase64String(base64);
        Assert.AreEqual("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5), "the endpoint must return a real PDF");

        return await page.EvaluateAsync<string>(
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
                return parts.join(' ');
            }
            """,
            base64);
    }

    private static async Task SavePngPreviewAsync(
        IPage page, string directory, string fileName, int amount, params (string Name, string Price)[] items)
    {
        var pngBase64 = await page.EvaluateAsync<string>(
            """
            async ([apiBase, dataset]) => {
                const body = JSON.parse(dataset);
                body.format = 'png';
                body.dpi = 144;
                const response = await fetch(`${apiBase}/api/document-editor/assembly/render`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body),
                });
                if (!response.ok) throw new Error(`png render failed: ${response.status}`);
                const result = await response.json();
                if (!result.pages || result.pages.length < 1) throw new Error('no preview pages');
                return result.pages[0].png;
            }
            """,
            new[] { ApiBaseUrl, DatasetJson(amount, items) });

        var png = Convert.FromBase64String(pngBase64);
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4], "preview must be a PNG");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), png);
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
