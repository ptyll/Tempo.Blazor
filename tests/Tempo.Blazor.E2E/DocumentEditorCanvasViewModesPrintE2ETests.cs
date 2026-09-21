using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase E11 E2E coverage for canvas view modes, zoom presets, Ctrl-wheel zoom, and print preview.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasViewModesPrintE2ETests : WasmTestBase
{
    private const string PhaseE11DocumentId = "phase-e11-canvas-viewmodes-print";

    [TestMethod]
    public async Task PhaseE11_ViewModesZoomAndPrintPreviewUseRealCanvasModel()
    {
        await DocumentEditorE2EReset.ResetAsync();
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.Console += (_, message) => TestContext.WriteLine($"[browser:{message.Type}] {message.Text}");
        page.PageError += (_, error) => TestContext.WriteLine($"[page-error] {error}");
        await page.SetViewportSizeAsync(1440, 1000);
        await page.AddInitScriptAsync("window.__tempoPrintInvoked = false; window.print = () => { window.__tempoPrintInvoked = true; };");
        await OpenPhaseE11DocumentAsync(page);

        var output = CreateOutputDirectory("phasee11-viewmodes-print");
        var beforePath = Path.Combine(output, "00-phasee11-print-layout.png");
        var readingPath = Path.Combine(output, "01-phasee11-reading.png");
        var previewPath = Path.Combine(output, "02-phasee11-print-preview.png");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        var initial = await ReadViewProbeAsync(page);
        Assert.AreEqual("print", initial.ViewMode);
        Assert.AreEqual(100, initial.ZoomPercent);
        Assert.IsFalse(initial.Dirty, initial.Debug);

        var reading = await ExecuteCanvasCommandAsync(page, "readingMode", new { });
        Assert.IsTrue(reading.Handled && reading.ViewChanged, reading.Debug);
        await WaitForViewModeAsync(page, "reading");
        await WaitForToolbarVisibilityAsync(page, visible: false);
        var readingProbe = await ReadViewProbeAsync(page);
        Assert.IsTrue(readingProbe.ToolbarHidden, readingProbe.Debug);
        Assert.IsFalse(readingProbe.ToolbarVisible, readingProbe.Debug);
        Assert.IsFalse(readingProbe.Dirty, readingProbe.Debug);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = readingPath,
            Type = ScreenshotType.Png
        });

        Assert.IsTrue((await ExecuteCanvasCommandAsync(page, "webLayout", new { })).ViewChanged);
        await WaitForViewModeAsync(page, "web");
        Assert.IsTrue((await ExecuteCanvasCommandAsync(page, "outlineView", new { })).ViewChanged);
        await WaitForViewModeAsync(page, "outline");
        Assert.IsTrue((await ExecuteCanvasCommandAsync(page, "printLayout", new { })).ViewChanged);
        await WaitForViewModeAsync(page, "print");

        var fitWidth = await ExecuteCanvasCommandAsync(page, "fitWidth", new { });
        Assert.IsTrue(fitWidth.Handled && fitWidth.ViewChanged, fitWidth.Debug);
        await WaitForZoomPresetAsync(page, "fitWidth");
        await WaitForFitWidthRenderAsync(page);
        var fitProbe = await ReadViewProbeAsync(page);
        // fitWidth is defined against the scroll viewport (host clientWidth); the content-sized
        // mount width tracks the page and is not a stable reference for the expected page width.
        var expectedFitWidth = Math.Max(1, Math.Round(fitProbe.HostClientWidth - 48));
        Assert.AreEqual("fitWidth", fitProbe.ZoomPreset, fitProbe.Debug);
        Assert.IsTrue(fitProbe.ZoomPercent > 0, fitProbe.Debug);
        Assert.IsTrue(fitProbe.LogicalPageWidth > 0, fitProbe.Debug);
        Assert.IsTrue(Math.Abs(fitProbe.CssPageWidth - expectedFitWidth) <= 24, fitProbe.Debug);

        var custom = await ExecuteCanvasCommandAsync(page, "setZoom", new { percent = 75 });
        Assert.IsTrue(custom.ViewChanged, custom.Debug);
        await WaitForZoomPercentAsync(page, 75);
        var customProbe = await ReadViewProbeAsync(page);
        Assert.AreEqual(75, customProbe.ZoomPercent);
        Assert.IsTrue(customProbe.TextRectWidth > 0, customProbe.Debug);

        await page.Locator("[data-testid='document-canvas-engine-root']").EvaluateAsync(
            "el => el.dispatchEvent(new WheelEvent('wheel', { deltaY: -120, ctrlKey: true, bubbles: true, cancelable: true }))");
        await page.WaitForFunctionAsync(
            "() => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-zoom-percent') || '0') > 75",
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        await page.GetByTestId("document-ribbon-tab-view").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-open-print-preview")).ToBeEnabledAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-open-print-preview").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-print-preview-active') === 'true'",
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-print-preview-actions")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var printPreview = await ReadPrintPreviewAsync(page);
        Assert.IsTrue(printPreview.Active, printPreview.Debug);
        Assert.AreEqual(PhaseE11DocumentId, printPreview.DocumentId);
        Assert.IsTrue(printPreview.PageCount >= 1, printPreview.Debug);
        Assert.IsTrue(printPreview.PrintableCommandCount > 0, printPreview.Debug);
        Assert.IsTrue(printPreview.TextRunCount > 0, printPreview.Debug);
        Assert.IsFalse(printPreview.IsBlank, printPreview.Debug);

        await page.GetByTestId("document-print-preview-browser-print").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__tempoPrintInvoked === true", new PageWaitForFunctionOptions { Timeout = 10_000 });

        await Assertions.Expect(page.GetByTestId("document-print-preview-export-pdf")).ToBeEnabledAsync(new() { Timeout = 10_000 });
        var pdfDownload = await page.RunAndWaitForDownloadAsync(
            async () => await page.GetByTestId("document-print-preview-export-pdf").ClickAsync());
        var pdfPath = await AssertDownloadedFileAsync(pdfDownload, ".pdf", 64, "Print preview PDF export");
        var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
        Assert.IsTrue(Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(pdfBytes.Length, 8)).StartsWith("%PDF", StringComparison.Ordinal));
        // The Skia PDF backend embeds text as subset glyph ids inside compressed content streams
        // plus a ToUnicode CMap, so a raw byte scan can never find the marker. Extract the real
        // text layer through PDF.js — the same probe DocumentEditorCanvasImportExportE2ETests uses.
        var pdfBase64 = Convert.ToBase64String(pdfBytes);
        var pdfText = await page.EvaluateAsync<string>(
            """
            async base64 => {
                const pdfjs = await import('/_content/Tempo.Blazor.PdfViewer/js/pdf.min.mjs');
                pdfjs.GlobalWorkerOptions.workerSrc = '/_content/Tempo.Blazor.PdfViewer/js/pdf.worker.min.mjs';
                const bytes = Uint8Array.from(atob(base64), ch => ch.charCodeAt(0));
                const doc = await pdfjs.getDocument({ data: bytes }).promise;
                const parts = [];
                for (let p = 1; p <= doc.numPages; p++) {
                    const pdfPage = await doc.getPage(p);
                    const content = await pdfPage.getTextContent();
                    parts.push(content.items.map(i => i.str).join(' '));
                }
                return parts.join(' ');
            }
            """,
            pdfBase64);
        // PDF.js joins text items with per-item whitespace, so collapse runs of whitespace
        // before matching the seeded sentence.
        var normalizedPdfText = Regex.Replace(pdfText ?? string.Empty, @"\s+", " ").Trim();
        Assert.IsTrue(
            normalizedPdfText.Contains("Print preview is generated from the current canvas display list", StringComparison.Ordinal),
            $"the seeded print-preview paragraph must survive into the PDF text layer. Extracted: {TrimTo(normalizedPdfText, 600)}");

        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertAnyCanvasNonBlankAsync(page, "[data-testid='document-canvas-engine-root'] [data-canvas-layer='content']");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = previewPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE11_ViewModesZoomAndPrintPreviewUseRealCanvasModel),
            seedDocumentId = PhaseE11DocumentId,
            userActions = new[]
            {
                "Open the phase E11 canvas seed document.",
                "Switch print layout, reading, web, and outline view modes through real canvas commands.",
                "Apply fit-width and custom zoom, then trigger Ctrl-wheel zoom on the canvas root.",
                "Open print preview through the View ribbon and request browser print from the rendered canvas display list.",
                "Export print preview to PDF through the host PDF provider and validate the downloaded PDF bytes."
            },
            screenshotPaths = new[] { beforePath, readingPath, previewPath },
            printPreviewPdfPath = pdfPath,
            customProbe,
            printPreview,
            contentMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(readingPath);
        TestContext.AddResultFile(previewPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenPhaseE11DocumentAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/canvas-engine-host?documentId={PhaseE11DocumentId}&showToolbar=true", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000
        });
        await WaitForPhaseE11ReadyAsync(page);
    }

    private static Task WaitForPhaseE11ReadyAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => {
                const hostReady = document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-engine-ready') === 'true';
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                return hostReady
                    && first?.getAttribute('data-canvas-model-document-id') === 'phase-e11-canvas-viewmodes-print'
                    && document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-e11-zoom-target"]').length >= 1;
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

    private static Task WaitForViewModeAsync(IPage page, string viewMode)
        => page.WaitForFunctionAsync(
            "viewMode => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-view-mode') === viewMode",
            viewMode,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForToolbarVisibilityAsync(IPage page, bool visible)
        => page.WaitForFunctionAsync(
            """
            visible => {
                const toolbar = document.querySelector('[data-testid="document-toolbar"]');
                const isVisible = !!toolbar && toolbar.getBoundingClientRect().height > 0;
                return isVisible === visible;
            }
            """,
            visible,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForZoomPresetAsync(IPage page, string preset)
        => page.WaitForFunctionAsync(
            "preset => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-zoom-preset') === preset",
            preset,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForZoomPercentAsync(IPage page, int percent)
        => page.WaitForFunctionAsync(
            "percent => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-zoom-percent') || '0') === percent",
            percent,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForFitWidthRenderAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => {
                // The engine's fit-width preset is computed from the clipping scroll viewport
                // (the host's clientWidth), not the content-sized mount: the mount grows with
                // the laid-out page, so reading it feeds back into the assertion. See
                // getViewportMetrics()/resolveScrollViewportElement in document-editor-canvas/entry.mjs.
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const canvasPage = document.querySelector('[data-testid="document-canvas-page"]');
                const viewportWidth = host?.clientWidth || 0;
                const cssPageWidth = Number(canvasPage?.getAttribute('data-canvas-page-css-width') || '0');
                if (viewportWidth <= 0 || cssPageWidth <= 0) {
                    return false;
                }

                const expected = Math.max(1, Math.round(viewportWidth - 48));
                return Math.abs(cssPageWidth - expected) <= 24;
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static async Task<PhaseE11CommandProbe> ExecuteCanvasCommandAsync(IPage page, string commandId, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return await page.EvaluateAsync<PhaseE11CommandProbe>(
            """
            async ({ commandId, json }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const raw = module.execCommand(handle, commandId, json);
                const parsed = JSON.parse(raw || '{}');
                return {
                    handled: parsed?.handled === true,
                    viewChanged: parsed?.result?.viewChanged === true,
                    printRequested: parsed?.result?.printRequested === true,
                    debug: JSON.stringify(parsed)
                };
            }
            """,
            new { commandId, json });
    }

    private static Task<PhaseE11ViewProbe> ReadViewProbeAsync(IPage page)
        => page.EvaluateAsync<PhaseE11ViewProbe>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const page = document.querySelector('[data-testid="document-canvas-page"]');
                const rect = document.querySelector('[data-canvas-text-rect][data-block-id="canvas-e11-zoom-target"]')?.getBoundingClientRect();
                const toolbar = document.querySelector('[data-testid="document-toolbar"]');
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const mount = document.querySelector('[data-testid="document-canvas-engine-mount"]');
                return {
                    viewMode: root?.getAttribute('data-canvas-view-mode') || '',
                    toolbarHidden: root?.getAttribute('data-canvas-view-toolbar-hidden') === 'true',
                    toolbarVisible: !!toolbar && toolbar.getBoundingClientRect().height > 0,
                    zoomPercent: Number(root?.getAttribute('data-canvas-zoom-percent') || '0'),
                    zoomPreset: root?.getAttribute('data-canvas-zoom-preset') || '',
                    logicalPageWidth: Number(page?.getAttribute('data-canvas-page-logical-width') || '0'),
                    cssPageWidth: Number(page?.getAttribute('data-canvas-page-css-width') || '0'),
                    hostViewportWidth: host?.getBoundingClientRect?.().width || 0,
                    hostClientWidth: host?.clientWidth || 0,
                    mountViewportWidth: mount?.getBoundingClientRect?.().width || 0,
                    textRectWidth: rect?.width || 0,
                    dirty: host?.getAttribute('data-canvas-engine-dirty') === 'true',
                    debug: JSON.stringify({
                        root: root?.outerHTML?.slice(0, 600) || '',
                        toolbarVisible: !!toolbar,
                        hostDirty: host?.getAttribute('data-canvas-engine-dirty') || '',
                        hostViewportWidth: host?.getBoundingClientRect?.().width || 0,
                        mountViewportWidth: mount?.getBoundingClientRect?.().width || 0,
                        cssPageWidth: Number(page?.getAttribute('data-canvas-page-css-width') || '0')
                    })
                };
            }
            """);

    private static async Task<PhaseE11PrintPreviewProbe> ReadPrintPreviewAsync(IPage page)
    {
        return await page.EvaluateAsync<PhaseE11PrintPreviewProbe>(
            """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const parsed = JSON.parse(module.getPrintPreviewStateJson(handle) || '{}');
                return {
                    active: parsed.active === true,
                    documentId: parsed.documentId || '',
                    pageCount: Number(parsed.pageCount || 0),
                    printableCommandCount: Number(parsed.printableCommandCount || 0),
                    textRunCount: Number(parsed.textRunCount || 0),
                    isBlank: parsed.isBlank === true,
                    debug: JSON.stringify(parsed)
                };
            }
            """);
    }

    private static string CreateOutputDirectory(string testName)
    {
        var path = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            testName,
            "2026-06-04",
            "desktop-1440x1000");
        Directory.CreateDirectory(path);
        return path;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
        {
            current = current.Parent;
        }

        return current ?? new DirectoryInfo(AppContext.BaseDirectory);
    }

    private static async Task<string> AssertDownloadedFileAsync(IDownload download, string expectedExtension, long minBytes, string label)
    {
        var path = await download.PathAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(path), $"{label} must provide a downloaded file path.");
        Assert.IsTrue(File.Exists(path), $"{label} must exist at '{path}'.");
        Assert.IsTrue(new FileInfo(path).Length >= minBytes, $"{label} must contain at least {minBytes} bytes.");
        Assert.AreEqual(expectedExtension, Path.GetExtension(download.SuggestedFilename), ignoreCase: true, $"{label} suggested filename should use {expectedExtension}.");
        return path!;
    }

    private sealed class PhaseE11CommandProbe
    {
        public bool Handled { get; set; }
        public bool ViewChanged { get; set; }
        public bool PrintRequested { get; set; }
        public string Debug { get; set; } = string.Empty;
    }

    private static string TrimTo(string value, int maxLength)
    {
        var text = (value ?? string.Empty).ReplaceLineEndings(" ");
        return text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength), "…");
    }

    private sealed class PhaseE11ViewProbe
    {
        public string ViewMode { get; set; } = string.Empty;
        public bool ToolbarHidden { get; set; }
        public bool ToolbarVisible { get; set; }
        public int ZoomPercent { get; set; }
        public string ZoomPreset { get; set; } = string.Empty;
        public double LogicalPageWidth { get; set; }
        public double CssPageWidth { get; set; }
        public double HostViewportWidth { get; set; }
        public double HostClientWidth { get; set; }
        public double MountViewportWidth { get; set; }
        public double TextRectWidth { get; set; }
        public bool Dirty { get; set; }
        public string Debug { get; set; } = string.Empty;
    }

    private sealed class PhaseE11PrintPreviewProbe
    {
        public bool Active { get; set; }
        public string DocumentId { get; set; } = string.Empty;
        public int PageCount { get; set; }
        public int PrintableCommandCount { get; set; }
        public int TextRunCount { get; set; }
        public bool IsBlank { get; set; }
        public string Debug { get; set; } = string.Empty;
    }
}
