using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for TmRedactionLayer on the /redaction demo page (WASM demo at 7106). The
/// CRITICAL contract is asserted here: after exporting the redacted PDF, the personal
/// ID number is (a) absent from the text extracted with PDF.js from every page of the
/// export, (b) absent from the raw export bytes, and (c) it WAS extractable from the
/// source document — proving the export removes content rather than overlaying it.
/// The image path is verified by sampling the redacted pixel region of the exported
/// bitmap. Screenshots land in <c>__screenshots__/redaction/</c>.
/// </summary>
[TestClass]
public class RedactionE2ETests : WasmTestBase
{
    private const string DemoPage = "/redaction";
    private const string PersonalId = "760512/1234";

    private sealed record DemoPageHandle(IPage Page, List<string> Errors);

    private async Task<DemoPageHandle> OpenPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);

        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error" && msg.Text.Contains("Unhandled exception"))
            {
                errors.Add(msg.Text);
            }
        };

        await page.GotoAsync($"{BaseUrl}{DemoPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        try
        {
            await WaitForAppReadyAsync(page);
        }
        catch (TimeoutException)
        {
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
            await WaitForAppReadyAsync(page);
        }

        await page.Locator("[data-testid='redaction-demo-pdf'] [data-testid='redaction-layer']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 90000 });
        return new DemoPageHandle(page, errors);
    }

    private static void AssertNoBlazorErrors(DemoPageHandle handle)
        => Assert.AreEqual(0, handle.Errors.Count,
            "The page raised unhandled exceptions: " + string.Join(" | ", handle.Errors));

    /// <summary>Extracts the joined text of every page of a PDF (by URL) with PDF.js inside the page.</summary>
    private static Task<string> ExtractPdfTextAsync(IPage page, string url)
        => page.EvaluateAsync<string>(
            @"async (url) => {
                const mod = await import('/_content/Tempo.Blazor.PdfViewer/js/pdf.min.mjs');
                mod.GlobalWorkerOptions.workerSrc = '/_content/Tempo.Blazor.PdfViewer/js/pdf.worker.min.mjs';
                const doc = await mod.getDocument(url).promise;
                let text = '';
                for (let i = 1; i <= doc.numPages; i++) {
                    const content = await (await doc.getPage(i)).getTextContent();
                    text += content.items.map(it => it.str).join(' ') + '\n';
                }
                await doc.destroy();
                return text;
            }", url);

    // ── CRITICAL contract: the export removes the content ────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Redaction_ExportedPdf_DoesNotContainTheRedactedValue()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var layer = page.Locator("[data-testid='redaction-demo-pdf']");

        // The SOURCE document really contains the personal ID (otherwise the test proves
        // nothing) — regenerate the identical sample deterministically and extract its text.
        var sourceBlobUrl = await page.EvaluateAsync<string>(
            @"() => tmRedaction.createSampleDocument(JSON.stringify([
                'SMLOUVA O POSKYTOVANI SLUZEB',
                'Klient: Bedrich Novak',
                'Adresa: Dlouha 12, Praha 1',
                'Rodne cislo: 760512/1234',
                'Bankovni ucet: 123456789/0800',
                'Datum podpisu: 12. 5. 2026'
            ]))");
        var sourceText = await ExtractPdfTextAsync(page, sourceBlobUrl);
        StringAssert.Contains(sourceText, PersonalId,
            "The source sample document must contain the personal ID in its text layer.");

        // The pre-seeded redaction covers the ID; wait for it to load, preview, export.
        await layer.Locator("[data-testid='redaction-rect']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        await SaveScreenshotAsync(page, "pdf-marked");
        await layer.Locator("[data-testid='redaction-preview-toggle']").ClickAsync();
        await SaveScreenshotAsync(page, "pdf-applied-preview");

        var download = await page.RunAndWaitForDownloadAsync(
            () => layer.Locator("[data-testid='redaction-export']").ClickAsync(),
            new PageRunAndWaitForDownloadOptions { Timeout = 120000 });
        Assert.AreEqual("contract-redacted.pdf", download.SuggestedFilename);

        // (a) PDF.js text extraction of the EXPORT finds nothing of the ID (nor any text at all).
        var exportedText = await ExtractPdfTextAsync(page, download.Url);
        Assert.IsFalse(exportedText.Contains(PersonalId),
            $"The exported PDF still contains the redacted value! Extracted: '{exportedText}'");
        Assert.IsFalse(exportedText.Contains("760512"),
            "The exported PDF still contains a fragment of the redacted value.");
        Assert.AreEqual(0, exportedText.Trim().Length,
            "The rasterized export must carry no text layer at all.");

        // (b) The raw export bytes do not embed the value as plaintext either.
        var containsRaw = await page.EvaluateAsync<bool>(
            @"async (url) => {
                const bytes = new Uint8Array(await (await fetch(url)).arrayBuffer());
                let ascii = '';
                for (let i = 0; i < bytes.length; i++) ascii += String.fromCharCode(bytes[i]);
                return ascii.includes('760512/1234');
            }", download.Url);
        Assert.IsFalse(containsRaw, "The raw exported bytes contain the redacted value.");

        await SaveScreenshotAsync(page, "pdf-exported");
        AssertNoBlazorErrors(handle);
    }

    // ── Drawing + categories + persistence UI ────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Redaction_DrawCategorizeSave_OnThePdf()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var layer = page.Locator("[data-testid='redaction-demo-pdf']");

        await layer.Locator("[data-testid='redaction-rect']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        // Draw another rectangle over the bank account line. The seeded rect exists in the
        // DOM before pdf.js finishes painting the page, so the overlay can still be 0-height
        // at that point — wait for the surface to actually reach the rendered page size
        // (state-based, not a fixed delay).
        var surface = layer.Locator("[data-testid='redaction-surface']");
        await page.WaitForFunctionAsync(
            "() => { const s = document.querySelector('[data-testid=\"redaction-demo-pdf\"] [data-testid=\"redaction-surface\"]'); return !!s && s.getBoundingClientRect().height > 400; }",
            null,
            new PageWaitForFunctionOptions { Timeout = 30000 });
        var box = await surface.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box!.Height > 400, "The overlay must be synced to the rendered page size.");
        await page.Mouse.MoveAsync(box.X + box.Width * 0.08f, box.Y + box.Height * 0.19f);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width * 0.5f, box.Y + box.Height * 0.23f, new MouseMoveOptions { Steps = 5 });
        await page.Mouse.UpAsync();

        await Assertions.Expect(layer.Locator("[data-testid='redaction-rect']"))
            .ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        // Categorize it and persist.
        await layer.Locator("[data-testid='redaction-category']").Nth(1)
            .SelectOptionAsync(new SelectOptionValue { Value = "BankAccount" });
        await layer.Locator("[data-testid='redaction-save']").ClickAsync();
        await layer.Locator("[data-testid='redaction-saved']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        StringAssert.Contains(
            await layer.Locator("[data-testid='redaction-count']").InnerTextAsync(), "2");
        await SaveScreenshotAsync(page, "pdf-two-areas");
        AssertNoBlazorErrors(handle);
    }

    // ── Image mode: exported pixels are black (edge) ─────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Redaction_ExportedImage_HasBlackPixels_WhereRedacted()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var section = page.Locator("[data-testid='redaction-demo-image']");
        await section.ScrollIntoViewIfNeededAsync();

        // Draw a rectangle over the personal-ID line of the bitmap.
        var surface = section.Locator("[data-testid='redaction-surface']");
        await surface.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        var box = await surface.BoundingBoxAsync();
        Assert.IsNotNull(box);
        await page.Mouse.MoveAsync(box!.X + box.Width * 0.05f, box.Y + box.Height * 0.38f);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width * 0.7f, box.Y + box.Height * 0.5f, new MouseMoveOptions { Steps = 5 });
        await page.Mouse.UpAsync();
        await Assertions.Expect(section.Locator("[data-testid='redaction-rect']"))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "image-marked");

        var download = await page.RunAndWaitForDownloadAsync(
            () => section.Locator("[data-testid='redaction-export']").ClickAsync(),
            new PageRunAndWaitForDownloadOptions { Timeout = 60000 });
        Assert.AreEqual("id-card-redacted.png", download.SuggestedFilename);

        // Sample the middle of the redacted band in the exported bitmap: must be pure black.
        var pixel = await page.EvaluateAsync<int[]>(
            @"async (url) => {
                const blob = await (await fetch(url)).blob();
                const bitmap = await createImageBitmap(blob);
                const canvas = document.createElement('canvas');
                canvas.width = bitmap.width;
                canvas.height = bitmap.height;
                const ctx = canvas.getContext('2d');
                ctx.drawImage(bitmap, 0, 0);
                const d = ctx.getImageData(Math.round(bitmap.width * 0.3), Math.round(bitmap.height * 0.44), 1, 1).data;
                return [d[0], d[1], d[2]];
            }", download.Url);
        CollectionAssert.AreEqual(new[] { 0, 0, 0 }, pixel,
            $"The redacted region of the exported image is not black: rgb({pixel[0]},{pixel[1]},{pixel[2]}).");
        AssertNoBlazorErrors(handle);
    }

    // ── Edge: empty layer export is disabled, remove restores empty state ────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Redaction_EmptyImageLayer_ExportDisabled_AndRemoveRestoresEmpty()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var section = page.Locator("[data-testid='redaction-demo-image']");
        await section.ScrollIntoViewIfNeededAsync();

        await section.Locator("[data-testid='redaction-empty']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        await Assertions.Expect(section.Locator("[data-testid='redaction-export']")).ToBeDisabledAsync();

        // Draw + remove → empty again, export disabled again.
        var surface = section.Locator("[data-testid='redaction-surface']");
        var box = await surface.BoundingBoxAsync();
        await page.Mouse.MoveAsync(box!.X + 40, box.Y + 40);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + 200, box.Y + 120, new MouseMoveOptions { Steps = 3 });
        await page.Mouse.UpAsync();
        await Assertions.Expect(section.Locator("[data-testid='redaction-export']")).ToBeEnabledAsync(
            new LocatorAssertionsToBeEnabledOptions { Timeout = 15000 });

        await section.Locator("[data-testid='redaction-remove']").ClickAsync();
        await section.Locator("[data-testid='redaction-empty']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await Assertions.Expect(section.Locator("[data-testid='redaction-export']")).ToBeDisabledAsync();
        await SaveScreenshotAsync(page, "edge-empty-state");
        AssertNoBlazorErrors(handle);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "redaction");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, $"{fileName}.png"),
            FullPage = true
        });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
