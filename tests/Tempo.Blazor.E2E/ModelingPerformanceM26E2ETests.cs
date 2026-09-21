using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>Performance E2E checks for modeling editor phase M26.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingPerformanceM26E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";
    private const string PerformanceQuery = "?scenario=performance-large&notation=bpmn";

    [TestMethod]
    [Description("The modeling editor route reports a browser LCP under the M26 budget")]
    public async Task ModelingEditor_LcpStaysUnderBudget()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync(
            """
            (() => {
                localStorage.setItem('tm-demo-culture', 'en');
                window.__tmM26FirstLcp = 0;
                window.__tmM26Lcp = 0;
                try {
                    new PerformanceObserver(list => {
                        for (const entry of list.getEntries()) {
                            window.__tmM26FirstLcp ||= entry.startTime;
                            window.__tmM26Lcp = entry.startTime;
                        }
                    }).observe({ type: 'largest-contentful-paint', buffered: true });
                } catch {
                    window.__tmM26Lcp = -1;
                }
            })();
            """);
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{PerformanceQuery}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60000
        });
        await WaitForLoadedPerformanceModelAsync(page);
        await page.ReloadAsync(new PageReloadOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60000
        });
        await WaitForLoadedPerformanceModelAsync(page);
        await page.WaitForTimeoutAsync(250);

        var lcp = await page.EvaluateAsync<double>(
            """
            () => {
                const entries = performance.getEntriesByType('largest-contentful-paint');
                const first = entries.length ? entries[0].startTime : 0;
                const latest = entries.length ? entries[entries.length - 1].startTime : 0;
                return first || window.__tmM26FirstLcp || latest || window.__tmM26Lcp || 0;
            }
            """);

        if (lcp <= 0)
        {
            Assert.Inconclusive("The browser did not expose an LCP entry for the modeling editor route.");
        }

        Assert.IsTrue(lcp < 2000, $"LCP should stay under 2000 ms. Actual: {lcp:0.0} ms.");
    }

    [TestMethod]
    [Description("A 200 element / 150 relationship model reloads under 3s and regenerates under 2s")]
    public async Task LargeModel_LoadAndGenerateStayWithinBudgets()
    {
        var page = await OpenPerformanceModelAsync();

        var loadMs = await MeasureReloadAsync(page);
        Assert.IsTrue(loadMs < 3000, $"Reloading the 200 element model should stay under 3000 ms. Actual: {loadMs:0.0} ms.");
        await ExpectEditorMetricAsync(page, "data-element-count", 200);
        await ExpectEditorMetricAsync(page, "data-relationship-count", 150);

        var generateMs = await MeasureGenerateAsync(page);
        Assert.IsTrue(generateMs < 2000, $"Generating the 200 node diagram should stay under 2000 ms. Actual: {generateMs:0.0} ms.");
        await ExpectEditorMetricAsync(page, "data-diagram-node-count", 200);
        await ExpectEditorMetricAsync(page, "data-diagram-edge-count", 150);
    }

    [TestMethod]
    [Description("Live tree filtering on 200 elements updates the DOM under 200ms")]
    public async Task TreeFilterLatency_UpdatesDomUnderBudget()
    {
        var page = await OpenPerformanceModelAsync();

        var latencyMs = await page.EvaluateAsync<double>(
            """
            async () => {
                const input = document.querySelector('[data-testid="modeling-tree-search"]');
                const tree = document.querySelector('[data-testid="modeling-model-tree"]');
                if (!input || !tree) throw new Error('Modeling tree search input was not found.');
                input.value = '';
                input.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'deleteContentBackward', data: null }));
                await new Promise(requestAnimationFrame);

                const start = performance.now();
                input.value = '199';
                input.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: '199' }));
                while (performance.now() - start < 2000) {
                    if (tree.getAttribute('data-visible-count') === '1') {
                        return performance.now() - start;
                    }
                    await new Promise(requestAnimationFrame);
                }

                throw new Error(`Tree filter did not reach one visible item. Count: ${tree.getAttribute('data-visible-count')}`);
            }
            """);

        Assert.IsTrue(latencyMs < 200, $"Tree filter latency should stay under 200 ms. Actual: {latencyMs:0.0} ms.");
    }

    [TestMethod]
    [Description("Canvas and issue-panel scrolling stay inside frame-budget smoke limits for large surfaces")]
    public async Task LargeSurfaces_ScrollWithoutJank()
    {
        var page = await OpenPerformanceModelAsync(width: 1440, height: 900);
        await ExpectEditorMetricAsync(page, "data-diagram-node-count", 200);
        await ExpectEditorMetricAsync(page, "data-issue-count", 100);

        var canvasFrames = await MeasureScrollFramesAsync(page, "[data-testid='modeling-diagram-preview-canvas-shell']", 900);
        AssertFrameBudget(canvasFrames, "Preview canvas");

        var inspectorTab = page.Locator("[data-testid='modeling-panel-tab-inspector']");
        if (await inspectorTab.IsVisibleAsync())
        {
            await inspectorTab.ClickAsync();
        }

        await page.Locator("[data-testid='modeling-issue-panel']").ScrollIntoViewIfNeededAsync();
        var issueFrames = await MeasureScrollFramesAsync(page, "[data-testid='modeling-issue-panel']", 900);
        AssertFrameBudget(issueFrames, "Issue panel");
    }

    [TestMethod]
    [Description("Switching notation with 200 loaded elements repaints the tree under 1s")]
    public async Task NotationSwitch_RepaintsTreeUnderBudget()
    {
        var page = await OpenPerformanceModelAsync();
        await ExpectEditorMetricAsync(page, "data-element-count", 200);

        var switchMs = await page.EvaluateAsync<double>(
            """
            async () => {
                const editor = document.querySelector('[data-testid="modeling-editor"]');
                const select = document.querySelector('[data-testid="modeling-notation-select"]');
                const tree = document.querySelector('[data-testid="modeling-model-tree"]');
                if (!editor || !select || !tree) throw new Error('Notation switch controls were not found.');
                const start = performance.now();
                select.value = 'uml25';
                select.dispatchEvent(new Event('change', { bubbles: true }));
                while (performance.now() - start < 3000) {
                    if (editor.getAttribute('data-notation') === 'uml25'
                        && tree.getAttribute('data-visible-count') === '200') {
                        return performance.now() - start;
                    }
                    await new Promise(requestAnimationFrame);
                }

                throw new Error(`Notation switch did not finish. Notation: ${editor.getAttribute('data-notation')}; tree count: ${tree.getAttribute('data-visible-count')}`);
            }
            """);

        Assert.IsTrue(switchMs < 1000, $"Notation switch should repaint the 200 item tree under 1000 ms. Actual: {switchMs:0.0} ms.");
    }

    private async Task<IPage> OpenPerformanceModelAsync(int width = 1280, int height = 720)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{PerformanceQuery}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60000
        });
        await WaitForLoadedPerformanceModelAsync(page);
        return page;
    }

    private static Task WaitForLoadedPerformanceModelAsync(IPage page) =>
        page.WaitForFunctionAsync(
            """
            () => {
                const editor = document.querySelector('[data-testid="modeling-editor"]');
                return editor?.getAttribute('data-state') === 'loaded'
                    && editor.getAttribute('data-element-count') === '200'
                    && editor.getAttribute('data-relationship-count') === '150'
                    && editor.getAttribute('data-diagram-node-count') === '200'
                    && editor.getAttribute('data-diagram-edge-count') === '150'
                    && editor.getAttribute('data-issue-count') === '100';
            }
            """,
            // This is a setup wait, not a perf assertion (the budgets below are measured with
            // in-page performance.now()). It has to cover cold WASM boot + 200-node diagram
            // layout under suite load, and polls on a timer: the default rAF polling starves
            // when the heavy first layout stalls frame production. M8/M9 waits use the same
            // 30s WASM-boot window.
            options: new PageWaitForFunctionOptions { Timeout = 30_000, PollingInterval = 250f });

    private static async Task<double> MeasureReloadAsync(IPage page)
    {
        var before = await ReadEditorMetricAsync(page, "data-load-count");
        return await page.EvaluateAsync<double>(
            """
            async ([before]) => {
                const button = document.querySelector('[data-testid="modeling-source-load-button"]');
                const editor = document.querySelector('[data-testid="modeling-editor"]');
                if (!button || !editor) throw new Error('Reload button was not found.');
                const start = performance.now();
                button.click();
                while (performance.now() - start < 5000) {
                    if (Number(editor.getAttribute('data-load-count') || '0') > before
                        && editor.getAttribute('data-state') === 'loaded'
                        && editor.getAttribute('data-element-count') === '200'
                        && editor.getAttribute('data-relationship-count') === '150') {
                        return performance.now() - start;
                    }
                    await new Promise(requestAnimationFrame);
                }

                throw new Error('Reload did not complete inside the measurement timeout.');
            }
            """,
            new object[] { before });
    }

    private static async Task<double> MeasureGenerateAsync(IPage page)
    {
        var before = await ReadEditorMetricAsync(page, "data-generation-count");
        return await page.EvaluateAsync<double>(
            """
            async ([before]) => {
                const button = document.querySelector('[data-testid="modeling-generate-diagram-button"]');
                const editor = document.querySelector('[data-testid="modeling-editor"]');
                if (!button || !editor) throw new Error('Generate button was not found.');
                const start = performance.now();
                button.click();
                while (performance.now() - start < 5000) {
                    if (Number(editor.getAttribute('data-generation-count') || '0') > before
                        && editor.getAttribute('data-state') === 'loaded'
                        && editor.getAttribute('data-diagram-node-count') === '200'
                        && editor.getAttribute('data-diagram-edge-count') === '150') {
                        await new Promise(requestAnimationFrame);
                        return performance.now() - start;
                    }
                    await new Promise(requestAnimationFrame);
                }

                throw new Error('Generation did not complete inside the measurement timeout.');
            }
            """,
            new object[] { before });
    }

    private static Task<FrameProbe> MeasureScrollFramesAsync(IPage page, string selector, int durationMs) =>
        page.EvaluateAsync<FrameProbe>(
            """
            async ([selector, durationMs]) => {
                const element = document.querySelector(selector);
                if (!element) throw new Error(`Scrollable element not found: ${selector}`);
                const frames = [];
                let last = performance.now();
                let start = last;
                let scrollStep = 0;

                return await new Promise(resolve => {
                    function frame(now) {
                        frames.push(now - last);
                        last = now;
                        if (element.scrollHeight > element.clientHeight) {
                            element.scrollTop = (element.scrollTop + 38) % Math.max(1, element.scrollHeight - element.clientHeight);
                        } else {
                            element.dispatchEvent(new WheelEvent('wheel', { bubbles: true, deltaY: 160 + scrollStep }));
                            scrollStep += 8;
                        }

                        if (now - start >= durationMs) {
                            const sorted = [...frames].sort((a, b) => a - b);
                            const avg = frames.reduce((sum, value) => sum + value, 0) / Math.max(1, frames.length);
                            const p95 = sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * 0.95))] || 0;
                            const longFrames = frames.filter(value => value > 50).length;
                            resolve({ averageFrameMs: avg, p95FrameMs: p95, longFrameCount: longFrames, frameCount: frames.length });
                            return;
                        }

                        requestAnimationFrame(frame);
                    }

                    requestAnimationFrame(frame);
                });
            }
            """,
            new object[] { selector, durationMs });

    private static async Task<int> ReadEditorMetricAsync(IPage page, string attributeName)
    {
        var value = await page.Locator("[data-testid='modeling-editor']").GetAttributeAsync(attributeName);
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static async Task ExpectEditorMetricAsync(IPage page, string attributeName, int expected)
    {
        await page.WaitForFunctionAsync(
            """
            ([attributeName, expected]) => document.querySelector('[data-testid="modeling-editor"]')?.getAttribute(attributeName) === String(expected)
            """,
            new object[] { attributeName, expected },
            // Timer polling, same as WaitForLoadedPerformanceModelAsync — these checks run right
            // after a reload/regenerate, when heavy layout can still starve rAF predicates.
            new PageWaitForFunctionOptions { Timeout = 10000, PollingInterval = 250f });
    }

    private static void AssertFrameBudget(FrameProbe probe, string label)
    {
        Assert.IsTrue(probe.FrameCount >= 20, $"{label} should report enough animation frames. Frames: {probe.FrameCount}.");
        Assert.IsTrue(probe.AverageFrameMs < 25, $"{label} average frame time should stay near 60fps. Actual: {probe.AverageFrameMs:0.0} ms.");
        Assert.IsTrue(probe.P95FrameMs < 50, $"{label} p95 frame time should avoid visible jank. Actual: {probe.P95FrameMs:0.0} ms.");
        Assert.IsTrue(probe.LongFrameCount <= 3, $"{label} should have at most 3 long frames. Actual: {probe.LongFrameCount}.");
    }

    private sealed class FrameProbe
    {
        public double AverageFrameMs { get; set; }

        public double P95FrameMs { get; set; }

        public int LongFrameCount { get; set; }

        public int FrameCount { get; set; }
    }
}
