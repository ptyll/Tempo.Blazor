using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 643bba06, phase 6 on the REAL <c>/wireframe-editor</c> demo page (no injected CSS: the page's own full-width rule at 1400 px and up
/// is what is tested): the editor is the desktop layout at 1440, <c>InitialZoom=Fit</c> shows a 1280 px page whole and never enlarges a
/// small one, the zoom read-out and the page switch agree with the real scale, a user zoom survives a resize, the stencils follow the demo's
/// colour theme (indigo / indigo-dark) and the PNG export goes to the Demo API. Screenshots land in the git-ignored
/// <c>TestResults/wireframe-phase6-zoom</c> (or <c>__screenshots__/wireframe/phase6</c> with <c>TM_E2E_UPDATE_BASELINE=1</c>).
/// </summary>
[TestClass]
public sealed class WireframePhase6ZoomThemeE2ETests : WasmTestBase
{
    private const string EditorUrl = "/wireframe-editor";
    private const string DefaultBlue = "#3b82f6";
    private const string IndigoPrimary = "#6366f1";

    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string ShotDirectory => UpdateBaseline
        ? Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "wireframe", "phase6")
        : Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "wireframe-phase6-zoom");

    // ── Demo page: full width ─────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(1440, 900)]
    [DataRow(1920, 1080)]
    [Description("The demo page gives the editor the full viewport width: data-editor-layout=desktop at 1440 and the max-w-7xl cap is lifted (no test CSS)")]
    public async Task DemoPage_EditorIsDesktop_AtWideViewports(int width, int height)
    {
        var (page, editor) = await OpenAsync(width, height);

        Assert.AreEqual("desktop", await editor.GetAttributeAsync("data-editor-layout"));
        var box = (await editor.BoundingBoxAsync())!;
        Assert.IsTrue(box.Width > 1300, $"the editor is {box.Width:0}px wide at a {width}px viewport (it was capped at 1280 - padding before)");
        Assert.IsTrue(box.Width <= width + 0.5, "and never wider than the viewport");
        Assert.AreEqual(0, await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth"), "no horizontal page scroll");
        Assert.AreEqual(1, await page.Locator("[data-testid='wf-demo-index-link']").Locator("visible=true").CountAsync(), "the demo index link replaces the hidden sidebar");
        await ShotAsync(page, $"demo-{width}--editor");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(1200, 800)]
    [DataRow(1024, 768)]
    [Description("Edge: below 1400 the sidebar stays and the editor container is narrower (a tablet / compact desktop), so the full-width rule must not apply")]
    public async Task DemoPage_BelowTheFullWidthBreakpoint_KeepsTheSidebarLayout(int width, int height)
    {
        var (page, editor) = await OpenAsync(width, height);

        var box = (await editor.BoundingBoxAsync())!;
        Assert.IsTrue(box.Width < width - 40, $"the editor ({box.Width:0}px) leaves room for the demo chrome at {width}px");
        Assert.AreEqual(0, await page.Locator("[data-testid='wf-demo-index-link']").Locator("visible=true").CountAsync());
    }

    // ── InitialZoom ───────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Fit: the 1280 px page is shown whole (with padding) at the real scale and the read-out says so; Fit never enlarges: a small page stays 100 %")]
    public async Task Fit_ShowsThePageWhole_AndNeverEnlargesASmallPage()
    {
        var (page, editor) = await OpenAsync(1440, 900);
        var svg = editor.Locator(".tm-wd-canvas__svg");

        var scale = await ScaleAsync(svg);
        var shown = await ReadOutAsync(editor);
        var vb = await ViewBoxAsync(svg);
        TestContext.WriteLine($"1280x800 page: scale {scale:0.000}, read-out {shown}%, viewBox {vb.X:0.#},{vb.Y:0.#} {vb.W:0.#}x{vb.H:0.#}");
        Assert.IsTrue(scale <= 1.0 + 1e-6, "Fit never enlarges");
        Assert.IsTrue(vb.X <= 1e-6 && vb.X + vb.W >= 1280 - 1e-6 && vb.Y <= 1e-6 && vb.Y + vb.H >= 800 - 1e-6, "the whole page is inside the view");
        Assert.AreEqual((int)Math.Round(scale * 100), shown, 1, "the read-out reports the real scale");

        // Switch to a smaller page: it stays at exactly 100 % (never enlarged) and is centred.
        await OpenLeftTabAsync(page, editor, "Pages");
        await editor.Locator("[data-testid='wd-page-add']").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-pages__item').length >= 2", null, new() { Timeout = 10_000 });
        await editor.Locator("[data-testid='wd-canvas-width']").FillAsync("400");
        await editor.Locator("[data-testid='wd-canvas-height']").FillAsync("300");
        await editor.Locator("[data-testid='wd-canvas-apply']").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-canvas__page').getAttribute('width') === '400'",
            null, new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(500);

        var smallScale = await ScaleAsync(svg);
        var smallVb = await ViewBoxAsync(svg);
        TestContext.WriteLine($"400x300 page: scale {smallScale:0.000}, viewBox {smallVb.X:0.#},{smallVb.Y:0.#} {smallVb.W:0.#}x{smallVb.H:0.#}");
        Assert.AreEqual(1.0, smallScale, 0.01, "a page that fits stays at exactly 100 %, it is not enlarged");
        Assert.AreEqual(100, await ReadOutAsync(editor), "the read-out says 100 %");
        Assert.IsTrue(smallVb.X < 0 && smallVb.Y < 0, "the small page is centred (its origin sits inside the view)");
        await ShotAsync(page, "demo-1440--fit-small-page");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: a zoom the user chose is not overwritten - not by a container resize and not by an unrelated parent render (the grid toggle)")]
    public async Task UserZoom_SurvivesAResizeAndAParentRender()
    {
        var (page, editor) = await OpenAsync(1440, 900);
        var svg = editor.Locator(".tm-wd-canvas__svg");
        var fitted = await ScaleAsync(svg);

        await editor.Locator("button[aria-label='Zoom in']").First.ClickAsync();
        await editor.Locator("button[aria-label='Zoom in']").First.ClickAsync();
        await page.WaitForTimeoutAsync(300);
        var zoomed = await ScaleAsync(svg);
        Assert.IsTrue(zoomed > fitted * 1.2, $"the user zoomed in ({fitted:0.00} -> {zoomed:0.00})");

        // A resize: the canvas gets wider, the zoom must stay.
        await page.SetViewportSizeAsync(1700, 900);
        await page.WaitForTimeoutAsync(800);
        Assert.AreEqual(zoomed, await ScaleAsync(svg), 0.02, "a resize keeps the user's zoom");
        Assert.AreEqual((int)Math.Round(zoomed * 100), await ReadOutAsync(editor), 1, "and the read-out");

        // Toggling the grid is an unrelated parent render: still no reset.
        // A DOM click: the inspector scrolls inside its own panel and the checkbox may sit below its fold (the interaction is what matters).
        await editor.Locator("[data-testid='wd-grid-show']").EvaluateAsync("e => e.click()");
        await page.WaitForTimeoutAsync(400);
        Assert.AreEqual(zoomed, await ScaleAsync(svg), 0.02, "an unrelated parent render never resets the zoom");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("The Fit button re-fits explicitly after the user zoomed (the way back)")]
    public async Task FitButton_ReturnsToTheFittedView()
    {
        var (page, editor) = await OpenAsync(1440, 900);
        var svg = editor.Locator(".tm-wd-canvas__svg");
        var fitted = await ScaleAsync(svg);
        await editor.Locator("button[aria-label='Zoom in']").First.ClickAsync();
        await editor.Locator("button[aria-label='Zoom in']").First.ClickAsync();
        Assert.AreNotEqual(fitted, await ScaleAsync(svg), 0.01);

        await ClickCommandAsync(page, editor, "Fit to view");
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual(await ReadOutAsync(editor), (int)Math.Round(await ScaleAsync(svg) * 100), 1, "the read-out follows the real scale after Fit");
    }

    // ── StencilTheme on the demo ──────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("The stencils follow the demo colour theme: default = blue, indigo = indigo primary; the page stays white paper in both")]
    public async Task Stencils_FollowTheDemoColourTheme()
    {
        var (page, editor) = await OpenAsync(1440, 900);
        await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length >= 1", null, new() { Timeout = 10_000 });
        var elementMarkup = () => editor.Locator("g[data-el-id]").First.InnerHTMLAsync();
        Assert.IsFalse((await elementMarkup()).Contains(IndigoPrimary, StringComparison.OrdinalIgnoreCase));
        await ShotAsync(page, "demo-1440--theme-default");

        await SetColorThemeAsync(page, "indigo");

        await page.WaitForFunctionAsync("c => document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]').innerHTML.toLowerCase().includes(c)", IndigoPrimary, new() { Timeout = 10_000 });
        Assert.IsFalse((await elementMarkup()).Contains(DefaultBlue, StringComparison.OrdinalIgnoreCase), "no default blue is left on the element");
        var paper = await editor.Locator(".tm-wd-canvas__page").EvaluateAsync<string>("e => getComputedStyle(e).fill");
        StringAssert.Contains(paper, "255", "the page stays white paper in the indigo theme");
        await ShotAsync(page, "demo-1440--theme-indigo");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Indigo dark: the editor chrome is dark, the stencils carry the indigo-dark colours, the page is still paper; the toolbox previews follow too")]
    public async Task Stencils_IndigoDark()
    {
        var (page, editor) = await OpenAsync(1440, 900, dark: true);
        await SetColorThemeAsync(page, "indigo");
        await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length >= 1", null, new() { Timeout = 10_000 });

        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]').innerHTML.toLowerCase().includes('#6366f1')", null, new() { Timeout = 10_000 });
        var html = (await editor.Locator("g[data-el-id]").First.InnerHTMLAsync()).ToLowerInvariant();
        StringAssert.Contains(html, "#f1f5f9", "indigo-dark paints light ink");
        await ShotAsync(page, "demo-1440--theme-indigo-dark");
    }

    // ── Export on the demo ────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Desktop PNG export: the dialog posts to the Demo API (200, a real PNG is downloaded) and the posted SVG is the standalone page, not the fitted view")]
    public async Task Export_Png_GoesToTheDemoApi_AndIsTheStandalonePage()
    {
        var (page, editor) = await OpenAsync(1440, 900);
        await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length >= 1", null, new() { Timeout = 10_000 });

        await editor.Locator("[data-testid='wd-export']").ClickAsync();
        await page.Locator("[data-testid='wd-export-png']").ClickAsync();
        var exportButton = page.Locator(".tm-modal-footer button", new() { HasTextString = "Export" }).Last;
        await exportButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var request = page.WaitForRequestAsync(r => r.Url.Contains("api/wireframe/export/png", StringComparison.OrdinalIgnoreCase) && r.Method == "POST", new() { Timeout = 30_000 });
        var response = page.WaitForResponseAsync(r => r.Url.Contains("api/wireframe/export/png", StringComparison.OrdinalIgnoreCase), new() { Timeout = 30_000 });
        var download = await page.RunAndWaitForDownloadAsync(() => exportButton.ClickAsync(), new() { Timeout = 60_000 });

        var answered = await response;
        Assert.IsTrue(answered.Url.Contains(":5100", StringComparison.Ordinal), $"posted to the Demo API ({answered.Url})");
        Assert.AreEqual(200, answered.Status);
        var body = (await request).PostData ?? string.Empty;
        var standalone = body.Replace("\\u0022", "\"").Replace("\\\"", "\"").Contains("viewBox=\"0 0 1280 800\"", StringComparison.Ordinal);
        Assert.IsTrue(standalone, "the posted svg is the standalone 1280x800 page, not the fitted view: " + body[..Math.Min(300, body.Length)]);
        var path = await download.PathAsync();
        var bytes = await File.ReadAllBytesAsync(path!);
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take(4).ToArray(), "a PNG");
    }

    // ── Standalone layers panel demo ──────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("The demo shows TmWireframeLayersPanel standalone (collapse toggle) next to the embedded form (ShowCollapseToggle=false: no toggle, no own header)")]
    public async Task Demo_LayersPanel_StandaloneKeepsTheCollapseToggle_EmbeddedDoesNot()
    {
        var (page, _) = await OpenAsync(1440, 900);
        var standalone = page.Locator("[data-testid='wf-demo-layers-standalone']");
        var embedded = page.Locator("[data-testid='wf-demo-layers-embedded']");
        await standalone.ScrollIntoViewIfNeededAsync();

        Assert.AreEqual(1, await standalone.Locator(".tm-wd-layers").CountAsync());
        Assert.AreEqual(1, await embedded.Locator(".tm-wd-layers.tm-wd-layers--embedded").CountAsync(), "embedded modifier");
        Assert.IsTrue(await standalone.Locator(".tm-wd-layers__toggle").CountAsync() > 0, "standalone has a collapse toggle");
        Assert.AreEqual(0, await embedded.Locator(".tm-wd-layers__toggle").CountAsync(), "embedded has no collapse toggle");
        await ShotAsync(page, "demo-1440--layers-panels");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(IPage Page, ILocator Editor)> OpenAsync(int width, int height, bool dark = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        await GotoAppReadyAsync(page, $"{BaseUrl}{EditorUrl}");
        await page.WaitForSelectorAsync(".tm-wd-editor .tm-wd-canvas__svg", new() { State = WaitForSelectorState.Attached, Timeout = 30_000 });
        if (dark)
        {
            // The demo sidebar (it holds the toggle) is hidden by this very page at >= 1400 px: dispatch the click on the hidden toggle.
            await page.Locator("[data-testid='theme-toggle']").First.DispatchEventAsync("click");
            await page.WaitForFunctionAsync("() => document.querySelector('[data-theme=\"dark\"]') !== null", null, new() { Timeout = 10_000 });
        }

        // The minimap card would cover part of the capture.
        await page.AddStyleTagAsync(new() { Content = ".tm-wd-editor__minimap-wrap { display: none !important; }" });
        var editor = page.Locator(".tm-wd-editor").First;
        await editor.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(1200);   // the toolbar overflow measurement and the initial fit settle
        return (page, editor);
    }

    private static async Task SetColorThemeAsync(IPage page, string theme)
    {
        // The colour switch has two instances (sidebar / mobile header): the visible one is clicked; on a page with the sidebar hidden the hidden one is dispatched.
        var button = page.Locator("button", new() { HasTextString = char.ToUpperInvariant(theme[0]) + theme[1..] }).Locator("visible=true").First;
        if (await button.CountAsync() > 0) await button.ClickAsync();
        else await page.Locator("button", new() { HasTextString = char.ToUpperInvariant(theme[0]) + theme[1..] }).First.DispatchEventAsync("click");
        await page.WaitForFunctionAsync("t => document.documentElement.getAttribute('data-tm-theme') === t", theme, new() { Timeout = 10_000 });
    }

    /// <summary>A toolbar command: the bar button while it is on the bar, else its entry in the "More" menu.</summary>
    private static async Task ClickCommandAsync(IPage page, ILocator editor, string label)
    {
        var bar = editor.Locator($".tm-toolbar button.tm-toolbar-btn:not(.tm-toolbar-item--collapsed)[aria-label^='{label}']");
        if (await bar.CountAsync() > 0) { await bar.First.ClickAsync(); return; }
        await editor.Locator("button.tm-toolbar-more").First.ClickAsync();
        await page.Locator("[role='menu'] [role='menuitem'], [role='menu'] [role='menuitemcheckbox']").Filter(new() { HasTextString = label }).First.ClickAsync();
    }

    private static async Task OpenLeftTabAsync(IPage page, ILocator editor, string title)
    {
        await editor.Locator("[data-region='left'] [role='tab']", new() { HasTextString = title }).ClickAsync();
        await page.WaitForTimeoutAsync(300);
    }

    private static Task<double> ScaleAsync(ILocator svg) =>
        svg.EvaluateAsync<double>("s => Math.min(s.getBoundingClientRect().width / s.viewBox.baseVal.width, s.getBoundingClientRect().height / s.viewBox.baseVal.height)");

    private static async Task<(double X, double Y, double W, double H)> ViewBoxAsync(ILocator svg)
    {
        var v = await svg.EvaluateAsync<double[]>("s => [s.viewBox.baseVal.x, s.viewBox.baseVal.y, s.viewBox.baseVal.width, s.viewBox.baseVal.height]");
        return (v[0], v[1], v[2], v[3]);
    }

    private static async Task<int> ReadOutAsync(ILocator editor) =>
        (int)await editor.Locator(".tm-wd-editor__zoom-label").First.EvaluateAsync<double>("e => parseFloat(e.textContent)");

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
