using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 1c95309c phase 8b: opening an existing wireframe with <c>InitialZoom = Fit</c> after an async document
/// load must show the page in the viewport without a host <c>fitToView</c> workaround. The demo page
/// <c>/wireframe-editor-async-load</c> supplies the document after the first render. Diagnostic wrapping of
/// <c>applyInitialZoom</c> / <c>relayout</c> / <c>_measure</c> lives only in this test via
/// <c>window.__tmWdDebug</c> (never in production JS).
/// </summary>
[TestClass]
public sealed class WireframePhase8bInitialFitE2ETests : WasmTestBase
{
    private const string PageUrl = "/wireframe-editor-async-load";

    private static string ShotDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "fze-8b");

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(1280, 800)]
    [DataRow(390, 844)]
    [Description("After an async document load, Fit shows the page in the svg viewport without a manual fitToView")]
    public async Task AsyncLoad_Fit_ShowsThePageInTheViewport_WithoutManualFit(int width, int height)
    {
        var page = await OpenAsync(width, height);
        await WaitReadyAsync(page);

        var svg = page.Locator("[data-testid='wf-async-editor'] .tm-wd-canvas__svg").First;
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-testid=\"wf-async-editor\"] g[data-el-id]').length >= 20",
            null, new() { Timeout = 15_000 });
        await page.WaitForTimeoutAsync(700);   // settle (10 frames / 500 ms cap) plus Blazor paint

        var measure = await MeasureAsync(svg);
        TestContext.WriteLine($"viewport {width}x{height}: viewBox {measure.VbX:0.#},{measure.VbY:0.#} {measure.VbW:0.#}x{measure.VbH:0.#}; " +
                              $"svg {measure.SvgW:0.#}x{measure.SvgH:0.#}; page {measure.PageW:0.#}x{measure.PageH:0.#}; " +
                              $"scale {measure.Scale:0.000}; debug={await DebugDumpAsync(page)}");

        Assert.IsTrue(measure.PageW > 0 && measure.PageH > 0, "the page rect is painted");
        Assert.IsTrue(
            IntersectsViewport(measure),
            $"the page sits in the svg viewport (viewBox {measure.VbX:0},{measure.VbY:0} {measure.VbW:0}x{measure.VbH:0}; " +
            $"page {measure.PageW:0}x{measure.PageH:0}). A ~13 000-unit viewBox leaves it below the fold.");
        Assert.IsTrue(measure.Scale > 0.05 && measure.Scale <= 1.0 + 1e-3, $"Fit never enlarges past 100 % (scale {measure.Scale:0.000})");

        await ShotAsync(page, $"async-load-{width}x{height}-light");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Toggling to a page smaller than the canvas keeps Fit at 100 % and the page inside the viewport")]
    public async Task AsyncLoad_SmallPage_StaysAt100Percent_InsideTheViewport()
    {
        var page = await OpenAsync(1280, 800);
        await WaitReadyAsync(page);
        await page.Locator("[data-testid='wf-async-size-toggle']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"wf-async-page-size\"]')?.getAttribute('data-size') === 'small'",
            null, new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(700);

        var svg = page.Locator("[data-testid='wf-async-editor'] .tm-wd-canvas__svg").First;
        var measure = await MeasureAsync(svg);
        TestContext.WriteLine($"small page: scale {measure.Scale:0.000}, viewBox {measure.VbX:0.#},{measure.VbY:0.#} {measure.VbW:0.#}x{measure.VbH:0.#}");
        Assert.AreEqual(1.0, measure.Scale, 0.05, "a page that fits stays at ~100 %");
        Assert.IsTrue(IntersectsViewport(measure), "the small page is inside the viewport");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("A hidden tab (svg size 0) then first real size Fits the page without a 0.5 px threshold freeze")]
    public async Task HiddenTab_FirstRealSize_FitsWithoutThresholdFreeze()
    {
        var page = await OpenAsync(1280, 800, hidden: true);
        await WaitReadyAsync(page);
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-testid=\"wf-async-editor\"] g[data-el-id]').length >= 20",
            null, new() { Timeout = 15_000 });

        await page.EvaluateAsync("() => document.getElementById('tm-wd-hide-async')?.remove()");
        await page.WaitForTimeoutAsync(800);

        var svg = page.Locator("[data-testid='wf-async-editor'] .tm-wd-canvas__svg").First;
        var measure = await MeasureAsync(svg);
        TestContext.WriteLine($"unhidden: scale {measure.Scale:0.000}, viewBox {measure.VbX:0.#},{measure.VbY:0.#} {measure.VbW:0.#}x{measure.VbH:0.#}");
        Assert.IsTrue(IntersectsViewport(measure), "the first real size Fits the page");
        Assert.IsTrue(measure.Scale > 0.05 && measure.Scale <= 1.0 + 1e-3);
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Dark theme screenshot of the async-load Fit (light already captured on the 1280 case)")]
    public async Task AsyncLoad_Fit_DarkScreenshot()
    {
        var page = await OpenAsync(1280, 800, dark: true);
        await WaitReadyAsync(page);
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-testid=\"wf-async-editor\"] g[data-el-id]').length >= 20",
            null, new() { Timeout = 15_000 });
        await page.WaitForTimeoutAsync(700);
        await ShotAsync(page, "async-load-1280x800-dark");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IPage> OpenAsync(int width, int height, bool dark = false, bool hidden = false)
    {
        var page = await OpenAppPageAsync(
            $"{BaseUrl}{PageUrl}",
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
                ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            },
            beforeNavigate: async context =>
            {
                await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
                if (hidden)
                {
                    await context.AddInitScriptAsync("""
                        (() => {
                          const style = document.createElement('style');
                          style.id = 'tm-wd-hide-async';
                          style.textContent = '[data-testid="wf-async-editor"] { display: none !important; }';
                          (document.documentElement || document.head || document.body).appendChild(style);
                        })();
                        """);
                }
                await context.AddInitScriptAsync("""
                    (() => {
                      window.__tmWdDebug = { log: [], applyRects: [], relayoutRects: [], measureRects: [] };
                      const wrap = () => {
                        const api = window.tmWireframeDesigner;
                        if (!api || api.__tmWdDebugWrapped) return;
                        api.__tmWdDebugWrapped = true;
                        const origApply = api.applyInitialZoom.bind(api);
                        const origRelayout = api.relayout.bind(api);
                        const origMeasure = api._measure.bind(api);
                        api.applyInitialZoom = function (svg, mode) {
                          const r = svg && svg.getBoundingClientRect ? svg.getBoundingClientRect() : null;
                          window.__tmWdDebug.applyRects.push({ w: r && r.width, h: r && r.height, t: Date.now(), mode });
                          const scale = origApply(svg, mode);
                          const vb = svg && svg.viewBox && svg.viewBox.baseVal;
                          window.__tmWdDebug.log.push({ kind: 'applyInitialZoom', scale, rect: r && { w: r.width, h: r.height },
                            viewBox: vb && { x: vb.x, y: vb.y, w: vb.width, h: vb.height } });
                          return scale;
                        };
                        api.relayout = function (svg) {
                          const r = svg && svg.getBoundingClientRect ? svg.getBoundingClientRect() : null;
                          window.__tmWdDebug.relayoutRects.push({ w: r && r.width, h: r && r.height, t: Date.now() });
                          return origRelayout(svg);
                        };
                        api._measure = function (inst) {
                          const size = origMeasure(inst);
                          window.__tmWdDebug.measureRects.push(size ? { w: size.width, h: size.height, t: Date.now() } : { w: 0, h: 0, t: Date.now() });
                          return size;
                        };
                      };
                      const id = setInterval(() => { if (window.tmWireframeDesigner) { wrap(); clearInterval(id); } }, 10);
                      document.addEventListener('DOMContentLoaded', wrap);
                    })();
                    """);
            });

        if (dark)
        {
            await page.Locator("[data-testid='theme-toggle']").First.DispatchEventAsync("click");
            await page.WaitForFunctionAsync("() => document.querySelector('[data-theme=\"dark\"]') !== null", null, new() { Timeout = 10_000 });
        }

        await page.WaitForSelectorAsync("[data-testid='wf-async-load-page']", new() { Timeout = 30_000 });
        return page;
    }

    private static async Task WaitReadyAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"wf-async-status\"]')?.getAttribute('data-ready') === 'true'",
            null, new() { Timeout = 20_000 });
    }

    private sealed record Measure(
        double VbX, double VbY, double VbW, double VbH,
        double SvgW, double SvgH,
        double PageW, double PageH,
        double Scale);

    private static async Task<Measure> MeasureAsync(ILocator svg)
    {
        var v = await svg.EvaluateAsync<double[]>("""
            s => {
              const vb = s.viewBox.baseVal;
              const svgR = s.getBoundingClientRect();
              const page = s.querySelector('.tm-wd-canvas__page');
              const pageW = page ? +page.getAttribute('width') : 0;
              const pageH = page ? +page.getAttribute('height') : 0;
              const scale = vb.width > 0 && vb.height > 0
                ? Math.min(svgR.width / vb.width, svgR.height / vb.height)
                : 0;
              return [vb.x, vb.y, vb.width, vb.height, svgR.width, svgR.height, pageW, pageH, scale];
            }
            """);
        return new Measure(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8]);
    }

    /// <summary>
    /// The page rectangle (SVG user units, origin 0,0) sits inside the viewBox. A ~13 000-unit viewBox from a premature
    /// getBoundingClientRect leaves the page far below the fold.
    /// </summary>
    private static bool IntersectsViewport(Measure m)
    {
        var pageInsideViewBox = m.VbX <= 1 && m.VbY <= 1
            && m.VbX + m.VbW >= m.PageW - 1
            && m.VbY + m.VbH >= m.PageH - 1;
        var viewBoxNotHuge = m.VbW < 8000 && m.VbH < 8000;
        var pagePainted = m.PageW > 8 && m.PageH > 8;
        return pageInsideViewBox && viewBoxNotHuge && pagePainted && m.Scale > 0.05;
    }

    private async Task<string> DebugDumpAsync(IPage page)
    {
        try
        {
            return await page.EvaluateAsync<string>("() => JSON.stringify(window.__tmWdDebug || null)");
        }
        catch
        {
            return "(no debug)";
        }
    }

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDirectory);
        await page.Mouse.MoveAsync(0, 0);
        var path = Path.Combine(ShotDirectory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, FullPage = false, Timeout = 60_000 });
        TestContext.AddResultFile(path);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
