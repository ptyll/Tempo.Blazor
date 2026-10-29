using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Final whole-plan review E2E (Modeling redesign): the desktop canvas is usable with ZERO
/// issues (M1), the tablet presents the issues as a side sheet without starving the touch
/// canvas (M2), the recoverable error screen carries a visible Retry affordance (M4) and a
/// dock collapse re-fits the preview so no dead gap is left under the editor chrome (M5).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingFinalReviewE2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("At 1440x900 with zero issues the canvas-body drops the dock row to auto and the diagram canvas reaches 300px")]
    public async Task Editor_1440_ZeroIssues_CanvasAtLeast300()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-empty", 1440, 900);
        try
        {
            await WaitForPreviewNodeCountAtLeastAsync(page, 1);

            var metrics = await page.EvaluateAsync<CanvasMetrics>(
                """
                () => {
                    const body = document.querySelector('.tm-modeling-editor__canvas-body');
                    return {
                        bodyClass: body?.className ?? '',
                        canvasHeight: document.querySelector('.tm-modeling-diagram-preview__editor .tm-diagram-canvas')
                            ?.getBoundingClientRect().height ?? 0,
                        emptyText: document.querySelector('[data-testid="modeling-issue-empty"]')?.getBoundingClientRect().height ?? 0
                    };
                }
                """);

            StringAssert.Contains(metrics.BodyClass, "tm-modeling-editor__canvas-body--issues-empty",
                "a zero-issue canvas body uses the issues-empty grid (the dock row is auto)");
            Assert.IsTrue(metrics.CanvasHeight >= 300,
                $"At 1440x900 with zero issues the .tm-diagram-canvas must reach 300px (measured {metrics.CanvasHeight:0.#}).");
            Assert.IsTrue(metrics.EmptyText > 0 && metrics.EmptyText < 80,
                $"The empty state is one compact line (measured {metrics.EmptyText:0.#}px).");

            await SaveStableScreenshotAsync(page, "modeling-final-1440-empty-issues.png");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("At 1024x768 the tablet issues tab opens a side sheet (no dock row) and the canvas keeps 240px")]
    public async Task Tablet_1024_IssuesSheet_KeepsCanvasFloor()
    {
        var page = await OpenLoadedTabletPageAsync(1024, 768);
        try
        {
            await WaitForPreviewNodeCountAtLeastAsync(page, 1);

            // No dock under the canvas on the tablet band — the issues are the fourth panel.
            Assert.AreEqual(0, await page.Locator("[data-testid='modeling-issues-toggle']").CountAsync(),
                "the tablet band has no issues dock under the canvas");

            await page.Locator("[data-testid='modeling-panel-tab-issues']").ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
            await page.Locator("[data-testid='modeling-issues-sheet']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            Assert.AreEqual("true", await page.Locator("[data-testid='modeling-panel-tab-issues']")
                .GetAttributeAsync("aria-expanded"), "the fourth tab is an aria-expanded disclosure");

            // One sheet at a time: opening the inspector closes the issues sheet.
            await page.Locator("[data-testid='modeling-panel-tab-inspector']").ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
            await page.WaitForFunctionAsync(
                "() => !document.querySelector(\"[data-testid='modeling-issues-sheet']\")",
                null,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
            Assert.AreEqual(1, await page.Locator("[data-testid='modeling-inspector-sheet']").CountAsync());

            // Back to the issues sheet: the touch canvas keeps its floor.
            await page.Locator("[data-testid='modeling-panel-tab-issues']").ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
            await page.Locator("[data-testid='modeling-issues-sheet']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            var canvasHeight = await CanvasHeightAsync(page);
            Assert.IsTrue(canvasHeight >= 240,
                $"The tablet canvas keeps 240px with the issues sheet open (measured {canvasHeight:0.#}).");
            Assert.IsTrue(await HasNoHorizontalOverflowAsync(page), "the issues sheet must not overflow the band");

            await SaveStableScreenshotAsync(page, "modeling-tablet-issues-sheet.png");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("At 1440x900 the error screen shows a visible Retry affordance that re-arms the load")]
    public async Task Error_1440_RetryVisibleAndReloads()
    {
        var page = await OpenModelingPageAsync("?scenario=error", 1440, 900);
        try
        {
            await page.Locator("[data-testid='modeling-editor-error']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            var retry = page.Locator("[data-testid='modeling-editor-retry']");
            await retry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            Assert.IsTrue(await retry.IsVisibleAsync(), "the retry affordance is visible on the error screen");

            // The demo error loader throws immediately, so the observable reload signal is the
            // brief (>=150ms) loading state — poll it from the test side.
            var initialLoadCount = await ReadNumericAttributeAsync(page, "[data-testid='modeling-editor']", "data-load-count");
            await retry.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });

            var sawReload = false;
            for (var attempt = 0; attempt < 60 && !sawReload; attempt++)
            {
                sawReload = await page.EvaluateAsync<bool>(
                    "() => document.querySelector(\"[data-testid='modeling-editor']\")?.getAttribute('data-state') === 'loading'"
                    + " || document.querySelector(\"[data-testid='modeling-editor-loading']\") !== null");
                if (!sawReload) await page.WaitForTimeoutAsync(20);
            }

            Assert.IsTrue(sawReload, "the retry re-arms the load (a loading state appears)");
            Assert.AreEqual(initialLoadCount, await ReadNumericAttributeAsync(page, "[data-testid='modeling-editor']", "data-load-count"),
                "the failing provider keeps the load count unchanged — the retry stays offered");

            // The docked panels stay hidden while no workspace is mounted.
            Assert.AreEqual(0, await page.Locator(".tm-modeling-editor__shell .tm-editor-shell__panel--left").CountAsync(),
                "no docked tree aside on the error screen");
            Assert.AreEqual(0, await page.Locator(".tm-modeling-editor__shell .tm-editor-shell__panel--right").CountAsync(),
                "no docked inspector aside on the error screen");

            await SaveStableScreenshotAsync(page, "modeling-error-retry-1440.png");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("After a dock collapse at 1440x900 the canvas top sits directly below the DE chrome (no >16px gap)")]
    public async Task DockCollapse_1440_RefitsTheCanvasWithoutAGap()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-500", 1440, 900);
        try
        {
            await WaitForPreviewNodeCountAtLeastAsync(page, 1);

            await page.Locator("[data-testid='modeling-issues-toggle']").ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
            await page.Locator("[data-testid='modeling-issue-panel'] .tm-modeling-issue-panel__content")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

            // Give the post-collapse refit (RefreshViewportAsync → relayout) a frame.
            await page.WaitForTimeoutAsync(300);

            var gap = await page.EvaluateAsync<double>(
                """
                () => {
                    // The gap between the bottom of the diagram editor's chrome rows and the
                    // top of the canvas viewport, measured inside the preview editor.
                    const editor = document.querySelector('.tm-modeling-diagram-preview__editor');
                    if (!editor) return -1;
                    const canvas = editor.querySelector('.tm-diagram-canvas');
                    if (!canvas) return -1;
                    let chromeBottom = editor.getBoundingClientRect().top;
                    editor.querySelectorAll('.tm-diagram-editor__page-tabs, .tm-diagram-editor__toolbar, .tm-diagram-editor__header-bar')
                        .forEach(row => { chromeBottom = Math.max(chromeBottom, row.getBoundingClientRect().bottom); });
                    return canvas.getBoundingClientRect().top - chromeBottom;
                }
                """);

            Assert.IsTrue(gap >= -1 && gap <= 16,
                $"After the collapse the canvas top must sit directly below the DE chrome (gap {gap:0.#}px).");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<IPage> OpenLoadedModelingPageAsync(string query, int width, int height)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-node').length > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
        return page;
    }

    private async Task<IPage> OpenLoadedTabletPageAsync(int width, int height)
    {
        // The demo page collapses to the docked tablet band when the editor container is
        // 768-1199px wide (the demo gives the editor the full content width at these viewports).
        return await OpenLoadedModelingPageAsync(string.Empty, width, height);
    }

    private async Task<IPage> OpenModelingPageAsync(string query, int width, int height)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static Task<int> ReadNumericAttributeAsync(IPage page, string selector, string attributeName) => page.EvaluateAsync<int>(
        "([selector, attributeName]) => Number(document.querySelector(selector)?.getAttribute(attributeName) ?? '0')",
        new[] { selector, attributeName });

    private static Task<double> CanvasHeightAsync(IPage page) => page.EvaluateAsync<double>(
        "() => document.querySelector('.tm-modeling-diagram-preview .tm-diagram-canvas')?.getBoundingClientRect().height ?? 0");

    private static Task WaitForPreviewNodeCountAtLeastAsync(IPage page, int minimumCount) => page.WaitForFunctionAsync(
        """
        minimumCount => {
            const preview = document.querySelector("[data-testid='modeling-diagram-preview']");
            return Number(preview?.getAttribute('data-node-count') ?? '0') >= minimumCount;
        }
        """,
        minimumCount,
        new PageWaitForFunctionOptions { Timeout = 15_000 });

    private static Task<bool> HasNoHorizontalOverflowAsync(IPage page) => page.EvaluateAsync<bool>(
        """
        () => {
            const doc = document.documentElement;
            const body = document.body;
            return Math.max(doc.scrollWidth, body ? body.scrollWidth : 0) <= window.innerWidth + 1;
        }
        """);

    private sealed class CanvasMetrics
    {
        public string BodyClass { get; set; } = string.Empty;
        public double CanvasHeight { get; set; }
        public double EmptyText { get; set; }
    }

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingFinalReviewE2ETests).Assembly.Location)!,
            "..", "..", "..", "TestResults", "modeling-final-review");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the modeling editor.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
