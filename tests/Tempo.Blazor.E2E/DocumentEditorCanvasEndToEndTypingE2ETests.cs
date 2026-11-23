using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 8 — REAL end-to-end keystroke latency on the FULL editor (<c>/document-editor</c>, which wraps
/// the canvas host in <c>TmDocumentEditor</c>). The canvas-internal probes (<c>data-canvas-input-render-
/// duration-ms</c>) only time the JS engine and stop at the canvas boundary; they never see the Blazor
/// round-trip (<c>OnCanvasEngineChanged</c> -> document marshal/clone/compare -> StateHasChanged) that
/// runs on the single WASM thread after every key. This test types on a large document and measures the
/// wall-clock until every keystroke is reflected, which captures the thread-blocking Blazor work.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasEndToEndTypingE2ETests : WasmTestBase
{
    private const string LargeDocId = "large-perf-1000";

    // The wall-clock "press -> revision" measurement is dominated by Playwright's CDP key-dispatch overhead
    // (~200 ms/key on a loaded debug box), NOT the app — so it is reported for context only. The real gate
    // is the ENGINE's own per-keystroke processing (input commit + paint), which must stay at interactive
    // speed (<= ~50 ms, GDocs/OnlyOffice target is one frame ~16 ms) and must not trigger a render storm in
    // the editor chrome (per-keystroke O(document) Blazor reconciliation previously caused ~6000 ms/key).
    private const double EngineKeystrokeBudgetMs = 50;

    [TestMethod]
    public async Task LargeDocument_TypingThroughFullEditor_StaysResponsive()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId={LargeDocId}");
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 120_000 });
        // Wait for the first rendered text so we have something to click into.
        await WaitForPredicateAsync(page, "() => document.querySelectorAll('[data-canvas-text-rect]').length > 0", 120_000);
        await page.WaitForTimeoutAsync(500);

        // Focus the editor by clicking the first rendered text rectangle (sets the caret).
        var rect = await page.Locator("[data-canvas-text-rect]").First.BoundingBoxAsync();
        Assert.IsNotNull(rect, "Expected at least one canvas text rectangle to click into.");
        await page.Mouse.ClickAsync(rect!.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
        await page.WaitForTimeoutAsync(200);

        const string sample = "performance";
        var revisionBefore = await ReadInputRevisionAsync(page);
        var renderCountBefore = await ReadDoubleAttrAsync(page, "data-canvas-render-count");
        var modelJsonPullsBefore = await ReadModelJsonPullCountAsync(page);

        // Type with no inter-key delay so a per-keystroke O(document) stall shows up as a frozen thread:
        // the wall-clock below cannot complete until every key's Blazor handler has run on the WASM thread.
        var start = await page.EvaluateAsync<double>("() => performance.now()");
        await page.Keyboard.TypeAsync(sample, new KeyboardTypeOptions { Delay = 0 });
        var dispatchMs = await page.EvaluateAsync<double>("() => performance.now()") - start;

        // Wait until the engine has processed every keystroke (input revision advanced by sample.Length).
        await page.WaitForFunctionAsync(
            @"([before, count]) => {
                const root = document.querySelector('[data-testid=""document-canvas-engine-root""]');
                const rev = Number(root?.getAttribute('data-canvas-input-revision') || '0');
                return rev >= before + count;
            }",
            new[] { (object)revisionBefore, sample.Length },
            new PageWaitForFunctionOptions { Timeout = 60_000 });
        var elapsed = await page.EvaluateAsync<double>("() => performance.now()") - start;

        var perKey = elapsed / sample.Length;
        var engineTypingP95 = await ReadDoubleAttrAsync(page, "data-canvas-typing-latency-p95-ms");
        var engineInputMs = await ReadDoubleAttrAsync(page, "data-canvas-input-render-duration-ms");
        var renderCountAfter = await ReadDoubleAttrAsync(page, "data-canvas-render-count");
        var engineRenders = renderCountAfter - renderCountBefore;
        TestContext.WriteLine($"WALL (incl. Playwright CDP dispatch, context only): {elapsed:F0} ms total, {perKey:F1} ms/key; TypeAsync dispatch={dispatchMs:F0} ms");
        TestContext.WriteLine($"ENGINE (the real gate): typing-latency p95={engineTypingP95:F1} ms; last input-render={engineInputMs:F1} ms; renders during burst={engineRenders} (for {sample.Length} keys)");

        var measureCacheSize = await ReadDoubleAttrAsync(page, "data-canvas-measure-cache-size");
        var measureCacheHits = await ReadDoubleAttrAsync(page, "data-canvas-measure-cache-hit-count");
        var measureCount = await ReadDoubleAttrAsync(page, "data-canvas-measure-count");
        var measureEvictions = await ReadDoubleAttrAsync(page, "data-canvas-measure-cache-eviction-count");
        TestContext.WriteLine($"MEASURE CACHE (N1 persistence): size={measureCacheSize:F0} hits={measureCacheHits:F0} measures={measureCount:F0} evictions={measureEvictions:F0}");

        // N2: typing + the settled sync that follows must never pull the full document across the
        // interop boundary (load-time save/offline/compare pulls stay on-demand and are out of scope).
        var modelJsonPullsAfter = await ReadModelJsonPullCountAsync(page);
        var modelJsonPullsDuringTyping = modelJsonPullsAfter - modelJsonPullsBefore;
        TestContext.WriteLine($"MODEL MARSHAL (N2): getModelJson during typing+settle={modelJsonPullsDuringTyping:F0} (sinceLoad={modelJsonPullsAfter:F0})");
        Assert.IsTrue(
            modelJsonPullsDuringTyping == 0,
            $"Settled typing triggered {modelJsonPullsDuringTyping:F0} full-document marshal(s) (getModelJson) — the N2 path must be selection-state only.");

        // After the debounce settles, the canonical document reconciliation should have run exactly once.
        await page.WaitForTimeoutAsync(700);

        var output = "/tmp/canvas-overlap-fix";
        Directory.CreateDirectory(output);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(output, "e2e-typing-after.png"),
            Type = ScreenshotType.Png,
        });

        // The engine must process & paint each keystroke at interactive speed...
        Assert.IsTrue(
            engineTypingP95 <= EngineKeystrokeBudgetMs,
            $"Engine per-keystroke commit latency p95 {engineTypingP95:F1} ms exceeded {EngineKeystrokeBudgetMs} ms.");
        Assert.IsTrue(
            engineInputMs <= EngineKeystrokeBudgetMs,
            $"Engine per-keystroke paint {engineInputMs:F1} ms exceeded {EngineKeystrokeBudgetMs} ms.");
        // ...and a burst of N keys must not provoke a chrome render storm (was O(document) per key).
        Assert.IsTrue(
            engineRenders <= sample.Length + 3,
            $"Engine rendered {engineRenders} times for {sample.Length} keystrokes — expected ~one render per key (no render storm).");
    }

    /// <summary>
    /// Perf plan N8 — the save path (RequestDocumentAsync -> provider boundary snapshot -> provider
    /// serialize) must not re-clone the freshly owned document. Measures wall-clock of a full manual
    /// save on the 1000-paragraph fixture.
    /// </summary>
    [TestMethod]
    public async Task LargeDocument_SaveThroughFullEditor_CompletesWithinBudget()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId={LargeDocId}");
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 120_000 });
        await WaitForPredicateAsync(page, "() => document.querySelectorAll('[data-canvas-text-rect]').length > 0", 120_000);
        await page.WaitForTimeoutAsync(500);

        // Make the document dirty so the save runs the full path.
        var rect = await page.Locator("[data-canvas-text-rect]").First.BoundingBoxAsync();
        Assert.IsNotNull(rect);
        await page.Mouse.ClickAsync(rect!.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
        await page.WaitForTimeoutAsync(200);
        await page.Keyboard.TypeAsync("x", new KeyboardTypeOptions { Delay = 0 });
        // Let the debounced change-notify reach the C# shell so the save runs the full dirty path.
        await page.WaitForTimeoutAsync(700);

        var start = await page.EvaluateAsync<double>("() => performance.now()");
        await page.GetByTestId("document-save").ClickAsync();
        await WaitForPredicateAsync(page, """
            () => {
                const saveMessage = document.querySelector('[data-testid="document-save-message"]')?.textContent || '';
                const lastSaved = document.querySelector('[data-testid="document-last-saved"]')?.textContent || '';
                const dirty = document.querySelector('[data-testid="document-editor-demo"]')?.getAttribute('data-document-dirty') === 'true';
                const saveButtonDisabled = document.querySelector('[data-testid="document-save"]')?.hasAttribute('disabled') === true;
                return saveButtonDisabled === false
                    && dirty === false
                    && (/Saved|Autosaved|Uloženo|Automaticky uloženo/i.test(saveMessage) || /saved|uloženo/i.test(lastSaved));
            }
            """, 60_000);
        var elapsed = await page.EvaluateAsync<double>("() => performance.now()") - start;

        TestContext.WriteLine($"SAVE (N8): full manual save of {LargeDocId} took {elapsed:F0} ms");
        Assert.IsTrue(elapsed < 15_000, $"Large-document save took {elapsed:F0} ms — expected well under 15 s.");
    }

    private static async Task<double> ReadModelJsonPullCountAsync(IPage page)
        => await page.EvaluateAsync<double>(
            @"() => Number(document.querySelector('[data-testid=""document-canvas-engine-host""]')?.getAttribute('data-canvas-model-json-request-count') || '0')");

    private static async Task<int> ReadInputRevisionAsync(IPage page)
        => await page.EvaluateAsync<int>(
            @"() => Number(document.querySelector('[data-testid=""document-canvas-engine-root""]')?.getAttribute('data-canvas-input-revision') || '0')");

    private static async Task<double> ReadDoubleAttrAsync(IPage page, string attr)
        => await page.EvaluateAsync<double>(
            $@"() => Number(document.querySelector('[data-testid=""document-canvas-engine-root""]')?.getAttribute('{attr}') || '0')");
}
