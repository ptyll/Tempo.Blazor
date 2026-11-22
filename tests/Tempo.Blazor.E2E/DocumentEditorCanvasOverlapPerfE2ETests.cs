using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 0/1 evidence for the canvas engine performance + rendering fix
/// (planning/tmdocumenteditor-canvas-performance-and-rendering-fix-todo-2026-06-08.md).
/// Opens the live /document-editor (canvas default) contract demo, captures screenshots,
/// asserts that text runs from different blocks do not overlap, and records a perf baseline.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasOverlapPerfE2ETests : WasmTestBase
{
    private const string OutputDir = "/tmp/canvas-overlap-fix";

    [TestMethod]
    public async Task ContractDemo_CanvasEngine_RendersWithoutOverlaps_AndCapturesBaseline()
    {
        Directory.CreateDirectory(OutputDir);

        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor", WaitUntilState.NetworkIdle);

        // Wait for the canvas engine host to mount and report ready.
        await page.WaitForSelectorAsync("[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 120_000,
        });
        await page.WaitForTimeoutAsync(800);

        // Confirm the page is really running the canvas engine, not legacy.
        var renderEngine = await page.GetAttributeAsync("[data-testid='document-editor-demo']", "data-render-engine");
        Assert.AreEqual("CanvasEnginePreview", renderEngine, "The /document-editor demo must run the canvas engine by default.");

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDir, "contract-demo-full.png"),
            FullPage = true,
            Type = ScreenshotType.Png,
        });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(OutputDir, "contract-demo-editor.png"),
            Type = ScreenshotType.Png,
        });

        // Collect text-run rectangles from the canvas metadata layer and detect cross-block overlaps.
        var overlapJson = await page.EvaluateAsync<string>(@"() => {
            const rects = [...document.querySelectorAll('[data-canvas-text-rect]')].map(el => {
                const r = el.getBoundingClientRect();
                return {
                    blockId: el.getAttribute('data-block-id') || '',
                    text: (el.getAttribute('data-canvas-text') || '').slice(0, 24),
                    x: r.x, y: r.y, w: r.width, h: r.height,
                };
            }).filter(r => r.w > 1 && r.h > 4 && r.text.trim().length > 0);

            const overlaps = [];
            for (let i = 0; i < rects.length; i++) {
                for (let j = i + 1; j < rects.length; j++) {
                    const a = rects[i], b = rects[j];
                    if (!a.blockId || !b.blockId || a.blockId === b.blockId) continue;
                    const ox = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x);
                    const oy = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y);
                    // require a meaningful 2D overlap (ignore 1px touching / shared baselines)
                    if (ox > 3 && oy > 4) {
                        overlaps.push({ a, b, ox: Math.round(ox), oy: Math.round(oy) });
                    }
                }
            }

            const objects = [...document.querySelectorAll('[data-canvas-object][data-wrap-mode]')].map(el => {
                const r = el.getBoundingClientRect();
                return { id: el.getAttribute('data-object-id') || '', wrap: el.getAttribute('data-wrap-mode') || '', x: r.x, y: r.y, w: r.width, h: r.height };
            }).filter(o => (o.wrap === 'Square' || o.wrap === 'Tight' || o.wrap === 'TopBottom') && o.w > 1 && o.h > 1);

            const objectOverlaps = [];
            for (let i = 0; i < objects.length; i++) {
                for (let j = i + 1; j < objects.length; j++) {
                    const a = objects[i], b = objects[j];
                    const ox = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x);
                    const oy = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y);
                    if (ox > 2 && oy > 2) objectOverlaps.push({ a, b });
                }
            }

            const root = document.querySelector('[data-testid=""document-canvas-engine-root""]');
            const perf = root ? {
                firstPaintMs: root.getAttribute('data-canvas-first-paint-ms'),
                renderCount: root.getAttribute('data-canvas-render-count'),
                renderP95Ms: root.getAttribute('data-canvas-render-p95-ms'),
                mountedPageCount: root.getAttribute('data-canvas-mounted-page-count'),
                pageCount: root.getAttribute('data-canvas-page-count'),
            } : {};

            return JSON.stringify({
                rectCount: rects.length,
                overlapCount: overlaps.length,
                overlaps: overlaps.slice(0, 8),
                floatingObjectCount: objects.length,
                objectOverlapCount: objectOverlaps.length,
                objectOverlaps: objectOverlaps.slice(0, 8),
                perf,
            });
        }");

        await File.WriteAllTextAsync(Path.Combine(OutputDir, "overlap-report.json"), Prettify(overlapJson));
        TestContext.WriteLine("Overlap/perf report: " + overlapJson);

        using var report = JsonDocument.Parse(overlapJson);
        var root = report.RootElement;
        var rectCount = root.GetProperty("rectCount").GetInt32();
        var overlapCount = root.GetProperty("overlapCount").GetInt32();
        var objectOverlapCount = root.GetProperty("objectOverlapCount").GetInt32();

        Assert.IsTrue(rectCount > 20, $"Expected the contract demo to render many text runs, got {rectCount}.");
        Assert.AreEqual(0, objectOverlapCount, $"Floating images must not overlap each other. Report: {overlapJson}");
        Assert.AreEqual(0, overlapCount, $"Text runs from different blocks must not overlap. Report: {overlapJson}");
    }

    [TestMethod]
    public async Task Scrolling_RepaintsFromCachedPlan_WithoutRecomputingLayout()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor", WaitUntilState.NetworkIdle);
        await page.WaitForSelectorAsync("[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 120_000,
        });
        await page.WaitForTimeoutAsync(800);

        var before = await ReadScrollCountersAsync(page);

        // Drive several scroll frames. The engine listens to window + host scroll, so window
        // scrolling exercises the paint-only path.
        for (var i = 0; i < 8; i++)
        {
            await page.Mouse.WheelAsync(0, 320);
            await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
        }

        await page.WaitForTimeoutAsync(400);
        var after = await ReadScrollCountersAsync(page);

        TestContext.WriteLine($"renderCount {before.RenderCount} -> {after.RenderCount}; scrollFrames {before.ScrollFrames} -> {after.ScrollFrames}");

        Assert.IsTrue(after.ScrollFrames > before.ScrollFrames, "Scrolling must register scroll frames on the engine.");
        // The headline Phase 2 guarantee: scrolling repaints from the cached plan and must NOT run
        // the document layout (which would bump the full-render counter). A tiny tolerance absorbs an
        // unrelated background recalc (e.g. a collaboration sync tick).
        Assert.IsTrue(
            after.RenderCount - before.RenderCount <= 1,
            $"Scrolling must not re-run the full render/layout. renderCount {before.RenderCount} -> {after.RenderCount}.");
    }

    [TestMethod]
    public async Task ReRender_ReusesCachedBlockLayout_InsteadOfFullRelayout()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor", WaitUntilState.NetworkIdle);
        await page.WaitForSelectorAsync("[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 120_000,
        });
        await page.WaitForTimeoutAsync(800);

        // Toggle a host option (read-only) to force a fresh full render with an unchanged model. The
        // incremental layout cache must reuse every cached paragraph instead of re-laying them out.
        await page.GetByTestId("document-editor-readonly").ClickAsync();
        await page.WaitForTimeoutAsync(500);
        var warm = await ReadCacheStatsAsync(page);

        TestContext.WriteLine($"warm layout cache hits={warm.LayoutHits}/{warm.LayoutMisses}; command cache hits={warm.CommandHits}/{warm.CommandMisses}");
        Assert.IsTrue(warm.LayoutHits > 0, "A re-render with an unchanged model must reuse cached block layouts.");
        Assert.IsTrue(warm.LayoutHits >= warm.LayoutMisses, $"Most blocks should reuse layout on a no-op re-render (hits={warm.LayoutHits}, misses={warm.LayoutMisses}).");
        // Phase 4: the display-command assembly must also be reused for unchanged blocks.
        Assert.IsTrue(warm.CommandHits > 0, "A re-render with an unchanged model must reuse cached display commands.");
        Assert.IsTrue(warm.CommandHits >= warm.CommandMisses, $"Most blocks should reuse commands on a no-op re-render (hits={warm.CommandHits}, misses={warm.CommandMisses}).");
    }

    private static async Task<(int LayoutHits, int LayoutMisses, int CommandHits, int CommandMisses)> ReadCacheStatsAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string>(@"() => {
            const root = document.querySelector('[data-testid=""document-canvas-engine-root""]');
            const n = name => Number(root?.getAttribute(name) || 0);
            return JSON.stringify({
                layoutHits: n('data-canvas-layout-cache-hit-count'),
                layoutMisses: n('data-canvas-layout-cache-miss-count'),
                commandHits: n('data-canvas-command-cache-hit-count'),
                commandMisses: n('data-canvas-command-cache-miss-count'),
            });
        }");
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        return (r.GetProperty("layoutHits").GetInt32(), r.GetProperty("layoutMisses").GetInt32(),
            r.GetProperty("commandHits").GetInt32(), r.GetProperty("commandMisses").GetInt32());
    }

    private static async Task<(int RenderCount, int ScrollFrames)> ReadScrollCountersAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string>(@"() => {
            const root = document.querySelector('[data-testid=""document-canvas-engine-root""]');
            return JSON.stringify({
                renderCount: Number(root?.getAttribute('data-canvas-render-count') || 0),
                scrollFrames: Number(root?.getAttribute('data-canvas-scroll-frame-count') || 0),
            });
        }");
        using var doc = JsonDocument.Parse(json);
        return (doc.RootElement.GetProperty("renderCount").GetInt32(), doc.RootElement.GetProperty("scrollFrames").GetInt32());
    }

    private static string Prettify(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }
}
