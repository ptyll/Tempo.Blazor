using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 22 E2E coverage for canvas performance and large-document virtualization.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasPerformanceE2ETests : WasmTestBase
{
    private const string Phase22DocumentId = "phase-22-canvas-performance";

    [TestMethod]
    public async Task Phase22_Performance_LargeDocumentVirtualizesAndPublishesLatencyMetrics()
    {
        var context = await CreateContextAsync(new BrowserNewContextOptions(), trace: false);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhase22DocumentAsync(page);

        var firstPaint = await ReadPerformanceProbeAsync(page);
        Assert.IsTrue(firstPaint.PageCount >= 10, $"Expected a large paginated document, got {firstPaint.PageCount} pages.");
        Assert.IsTrue(firstPaint.MountedPageCount > 0, "First paint must mount at least one visible page.");
        Assert.IsTrue(firstPaint.MountedPageCount < firstPaint.PageCount, $"Virtualization should mount fewer pages than total pages. {JsonSerializer.Serialize(firstPaint)}");
        Assert.IsTrue(firstPaint.PaintedCommandCount > 0, "First visible canvas page must not be blank.");
        Assert.IsTrue(firstPaint.VirtualizationProgressive, "Visible pages should fill progressively for a large document.");
        Assert.IsTrue(firstPaint.FirstPaintMs > 0 && firstPaint.FirstPaintMs <= 5_000,
            $"Large document first paint should stay within the phase 22 target. {JsonSerializer.Serialize(firstPaint)}");

        await ScrollCanvasSurfaceAsync(page, 5_600);
        await page.WaitForFunctionAsync(
            """
            previous => {
                const indexes = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]')?.getAttribute('data-canvas-visible-page-indexes') || '';
                return indexes && indexes !== previous;
            }
            """,
            firstPaint.VisiblePageIndexes,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        var afterScroll = await ReadPerformanceProbeAsync(page);
        Assert.AreNotEqual(firstPaint.VisiblePageIndexes, afterScroll.VisiblePageIndexes, "Scroll should mount a different visible page window.");
        Assert.IsTrue(afterScroll.ScrollFrameCount > 0, "Scroll smoothness metrics should record scroll frames.");

        await ScrollCanvasSurfaceAsync(page, 0);
        await WaitForCanvasTextRectAsync(page, "canvas-phase22-p000");
        await ClickCanvasTextAsync(page, "canvas-phase22-p000", 24);
        await page.GetByTestId("document-canvas-hidden-input").FocusAsync();
        await page.Keyboard.TypeAsync(" swift");
        await page.WaitForFunctionAsync(
            """
            () => Number(document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]')?.getAttribute('data-canvas-typing-latency-count') || '0') > 0
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        var afterTyping = await ReadPerformanceProbeAsync(page);
        Assert.IsTrue(afterTyping.TypingLatencyP50Ms > 0, "Typing p50 latency metric must be published after real typing.");
        Assert.IsTrue(afterTyping.TypingLatencyP95Ms >= afterTyping.TypingLatencyP50Ms, "Typing p95 must be at least p50.");
        Assert.IsTrue(afterTyping.TypingLatencyP95Ms <= 100,
            $"Typing p95 should stay comfortably below the phase 22 hot-path budget. {JsonSerializer.Serialize(afterTyping)}");
        Assert.IsTrue(afterTyping.TileCacheEntryCount > 0, "Per-page tile cache should contain painted page entries.");
        Assert.IsTrue(afterTyping.RecalcFirstDirtyBlockIndex >= 0, "Incremental recalc should expose the first dirty block index.");

        await ReopenAndAssertNoMountedPageLeakAsync(page);

        var output = CreateOutputDirectory(nameof(Phase22_Performance_LargeDocumentVirtualizesAndPublishesLatencyMetrics));
        var screenshotPath = Path.Combine(output, "phase22-performance.png");
        var manifestPath = Path.Combine(output, "manifest.json");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = screenshotPath,
            Type = ScreenshotType.Png,
            FullPage = false
        });

        var finalProbe = await ReadPerformanceProbeAsync(page);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase22_Performance_LargeDocumentVirtualizesAndPublishesLatencyMetrics),
            seedDocumentId = Phase22DocumentId,
            expectedVisibleChanges = "The first viewport paints real document pages immediately, and only visible pages plus buffer are mounted while scrolling fills pages progressively.",
            expectedModelChanges = "Typing in the large document records incremental dirty-block recalc and p50/p95 typing latency metrics.",
            screenshotPath,
            firstPaint,
            afterScroll,
            afterTyping,
            finalProbe
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(screenshotPath);
        TestContext.AddResultFile(manifestPath);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
    }

    private async Task OpenPhase22DocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={Phase22DocumentId}&showToolbar=true", WaitUntilState.DOMContentLoaded);
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]') !== null
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        // Perf plan N11: the first render is budgeted (progressive layout); this test asserts
        // document-final page counts, so wait for the idle continuations to finish the layout.
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-layout-complete') === 'true'
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    private static Task ScrollCanvasSurfaceAsync(IPage page, int scrollTop)
        => page.EvaluateAsync(
            """
            top => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                if (!root) return;
                const target = Math.max(0, Number(top) || 0);
                window.scrollTo({ top: target, left: 0, behavior: 'instant' });
                document.documentElement.scrollTop = target;
                document.body.scrollTop = target;
                window.dispatchEvent(new Event('scroll', { bubbles: true }));
                document.dispatchEvent(new Event('scroll', { bubbles: true }));
                root.dispatchEvent(new Event('scroll', { bubbles: true }));
            }
            """,
            scrollTop);

    private static async Task ClickCanvasTextAsync(IPage page, string blockId, int offset)
    {
        await WaitForCanvasTextRectAsync(page, blockId);
        var point = await page.EvaluateAsync<CanvasPoint>(
            """
            ([blockId, offset]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`));
                const node = rects.find(item => Number(item.getAttribute('data-canvas-start-offset') || '0') <= offset && Number(item.getAttribute('data-canvas-end-offset') || '0') >= offset) || rects[0];
                const rect = node.getBoundingClientRect();
                const start = Number(node.getAttribute('data-canvas-start-offset') || '0');
                const end = Math.max(start + 1, Number(node.getAttribute('data-canvas-end-offset') || '0'));
                const t = Math.max(0, Math.min(1, (offset - start) / (end - start)));
                return {
                    x: rect.left + Math.max(2, rect.width * t),
                    y: rect.top + rect.height / 2
                };
            }
            """,
            new object[] { blockId, offset });
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
    }

    private static Task WaitForCanvasTextRectAsync(IPage page, string blockId)
        => page.WaitForFunctionAsync(
            """
            blockId => document.querySelector(`[data-canvas-text-rect][data-block-id="${blockId}"]`) !== null
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private async Task ReopenAndAssertNoMountedPageLeakAsync(IPage page)
    {
        var observedMountedCounts = new List<int>();
        for (var index = 0; index < 2; index++)
        {
            await OpenPhase22DocumentAsync(page);
            var probe = await ReadPerformanceProbeAsync(page);
            observedMountedCounts.Add(probe.MountedPageCount);
        }

        Assert.IsTrue(observedMountedCounts.All(count => count > 0 && count < 8),
            $"Repeated open/close should not leak mounted page surfaces. Counts: {string.Join(", ", observedMountedCounts)}");
    }

    private static Task<PerformanceProbe> ReadPerformanceProbeAsync(IPage page)
        => page.EvaluateAsync<PerformanceProbe>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                const firstPage = root?.querySelector('[data-testid="document-canvas-page"]');
                return {
                    pageCount: Number(root?.getAttribute('data-canvas-page-count') || '0'),
                    mountedPageCount: Number(root?.getAttribute('data-canvas-mounted-page-count') || '0'),
                    visiblePageIndexes: root?.getAttribute('data-canvas-visible-page-indexes') || '',
                    virtualizationEnabled: root?.getAttribute('data-canvas-virtualization-enabled') === 'true',
                    virtualizationProgressive: root?.getAttribute('data-canvas-virtualization-progressive') === 'true',
                    firstPaintMs: Number(root?.getAttribute('data-canvas-first-paint-ms') || '0'),
                    typingLatencyP50Ms: Number(root?.getAttribute('data-canvas-typing-latency-p50-ms') || '0'),
                    typingLatencyP95Ms: Number(root?.getAttribute('data-canvas-typing-latency-p95-ms') || '0'),
                    typingLatencyCount: Number(root?.getAttribute('data-canvas-typing-latency-count') || '0'),
                    scrollFrameCount: Number(root?.getAttribute('data-canvas-scroll-frame-count') || '0'),
                    tileCacheEntryCount: Number(root?.getAttribute('data-canvas-tile-cache-entry-count') || '0'),
                    tileCacheHitCount: Number(root?.getAttribute('data-canvas-tile-cache-hit-count') || '0'),
                    recalcFirstDirtyBlockIndex: Number(root?.getAttribute('data-canvas-recalc-first-dirty-block-index') || '-1'),
                    inputHotPathMs: Number(root?.getAttribute('data-canvas-input-hot-path-ms') || '0'),
                    inputModelSetMs: Number(root?.getAttribute('data-canvas-input-model-set-ms') || '0'),
                    inputSideEffectsMs: Number(root?.getAttribute('data-canvas-input-side-effects-ms') || '0'),
                    inputRecalcMs: Number(root?.getAttribute('data-canvas-input-recalc-ms') || '0'),
                    inputSelectionMs: Number(root?.getAttribute('data-canvas-input-selection-ms') || '0'),
                    paintedCommandCount: Number(firstPage?.getAttribute('data-canvas-painted-command-count') || '0')
                };
            }
            """);

    private static string CreateOutputDirectory(string testName)
    {
        var path = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phase22-performance",
            "2026-06-04",
            testName,
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from test output directory.");
    }

    private sealed class CanvasPoint
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    private sealed class PerformanceProbe
    {
        public int PageCount { get; set; }

        public int MountedPageCount { get; set; }

        public string VisiblePageIndexes { get; set; } = string.Empty;

        public bool VirtualizationEnabled { get; set; }

        public bool VirtualizationProgressive { get; set; }

        public double FirstPaintMs { get; set; }

        public double TypingLatencyP50Ms { get; set; }

        public double TypingLatencyP95Ms { get; set; }

        public int TypingLatencyCount { get; set; }

        public int ScrollFrameCount { get; set; }

        public int TileCacheEntryCount { get; set; }

        public int TileCacheHitCount { get; set; }

        public int RecalcFirstDirtyBlockIndex { get; set; }

        public double InputHotPathMs { get; set; }

        public double InputModelSetMs { get; set; }

        public double InputSideEffectsMs { get; set; }

        public double InputRecalcMs { get; set; }

        public double InputSelectionMs { get; set; }

        public int PaintedCommandCount { get; set; }
    }
}
