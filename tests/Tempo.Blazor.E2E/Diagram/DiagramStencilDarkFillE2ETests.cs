using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 8c — ArchiMate (and BPMN as the paper-fill control) node fills are readable in dark
/// mode on the real modeling canvas, plus the DiagramEditor BuiltIn sticky-note. Screenshots
/// land in gitignored <c>__screenshots__/fze-8c/</c> as untracked UX-review artefacts — never
/// stage them.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class DiagramStencilDarkFillE2ETests : WasmTestBase
{
    private const string Canvas = ".tm-diagram-canvas";
    private const string PageRect = "rect.tm-diagram-page";
    private const string ArchimateRoute = "/modeling-editor?notation=archimate32&viewpoint=Layered&scenario=archimate32-business-app";
    private const string BpmnRoute = "/modeling-editor?scenario=bpmn2-generated";

    private static string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "fze-8c");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent!;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private async Task<IPage> OpenAsync(string route, int width = 1440, int height = 1000)
    {
        var context = await CreateContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        await GotoAppReadyAsync(page, $"{BaseUrl}{route}");
        await page.Locator($"{Canvas} {PageRect}").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await page.Locator($"{Canvas} g.tm-diagram-node").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await CollapseLegendAsync(page);
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    private static async Task CollapseLegendAsync(IPage page)
    {
        var toggle = page.Locator("[data-testid='modeling-preview-legend'] .tm-modeling-diagram-legend__toggle").First;
        if (await toggle.CountAsync() == 0)
        {
            return;
        }

        if (await toggle.GetAttributeAsync("aria-expanded") == "true")
        {
            await toggle.ClickAsync();
            await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        }
    }

    private static async Task ToggleDarkAsync(IPage page)
    {
        await page.Locator("[data-testid='theme-toggle']").First.DispatchEventAsync("click");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-theme=\"dark\"]') !== null", null, new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(400);
    }

    private static string ParseHex(string rgb)
    {
        var match = Regex.Match(rgb, @"rgba?\((\d+),\s*(\d+),\s*(\d+)");
        Assert.IsTrue(match.Success, $"cannot parse '{rgb}'");
        return "#" + string.Concat(match.Groups.Cast<Group>().Skip(1)
            .Select(g => int.Parse(g.Value, CultureInfo.InvariantCulture).ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static async Task ShotCanvasAsync(IPage page, string name)
    {
        Assert.IsTrue(await page.EvaluateAsync<bool>(
            "() => !document.querySelector('#blazor-error-ui') || document.querySelector('#blazor-error-ui').offsetParent === null"),
            "the Blazor error overlay must stay hidden");
        var locator = page.Locator(Canvas).First;
        await locator.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);
        var path = Path.Combine(ShotDir, $"{name}.png");
        await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        Assert.IsTrue(new FileInfo(path).Length > 5 * 1024, $"screenshot {name} looks empty");
    }

    [TestMethod]
    public async Task ArchimateCanvas_LightKeepsThe212Fills_DarkFlipsTheFamily()
    {
        var page = await OpenAsync(ArchimateRoute);

        var application = page.Locator($"{Canvas} g.tm-diagram-node[data-stencil-id='archimate3.application.component']").First;
        await application.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // ArchiMate rectangles paint the native .tm-diagram-node__shape-bg (ShapeSvg is the
        // marker overlay with fill=none). Measuring svg-bg * hits that overlay.
        var fillPaint = application.Locator(".tm-diagram-node__shape-bg").First;

        var lightFill = await fillPaint.EvaluateAsync<string>("e => getComputedStyle(e).fill");
        Assert.AreEqual("#cfe8ff", ParseHex(lightFill), "light ArchiMate application fill stays the 2.12.0 hex");

        var lightInk = await application.Locator(".tm-diagram-node__text").First
            .EvaluateAsync<string>("e => getComputedStyle(e).color");
        Assert.AreEqual("#4b5563", ParseHex(lightInk), "light on-fill ink is gray-600, not the muted canvas ink");
        await ShotCanvasAsync(page, "archimate-light");

        await ToggleDarkAsync(page);

        var darkFill = await fillPaint.EvaluateAsync<string>("e => getComputedStyle(e).fill");
        Assert.AreEqual("#1e3a8a", ParseHex(darkFill), "dark ArchiMate application fill is the family 900-step");
        Assert.AreNotEqual("#cfe8ff", ParseHex(darkFill), "the light hex must not leak into dark");

        var darkInk = await application.Locator(".tm-diagram-node__text").First
            .EvaluateAsync<string>("e => getComputedStyle(e).color");
        Assert.AreEqual("#e5e7eb", ParseHex(darkInk), "dark on-fill ink is gray-200, not muted #a1a1aa");
        await ShotCanvasAsync(page, "archimate-dark");
    }

    [TestMethod]
    public async Task BpmnCanvas_StillPaintsPaperInBothThemes()
    {
        var page = await OpenAsync(BpmnRoute);
        var shape = page.Locator($"{Canvas} g.tm-diagram-node .tm-diagram-node__svg-bg *, {Canvas} g.tm-diagram-node path.tm-diagram-node__shape-bg").First;

        var lightFill = await shape.EvaluateAsync<string>("e => getComputedStyle(e).fill");
        Assert.AreEqual("#ffffff", ParseHex(lightFill), "BPMN still rides the canvas paper in light");
        await ShotCanvasAsync(page, "bpmn-light");

        await ToggleDarkAsync(page);
        var darkFill = await shape.EvaluateAsync<string>("e => getComputedStyle(e).fill");
        Assert.AreEqual("#27272a", ParseHex(darkFill), "BPMN still rides the dark canvas paper");
        await ShotCanvasAsync(page, "bpmn-dark");
    }

    [TestMethod]
    public async Task DiagramEditor_BuiltInColouredStencils_LightAndDark()
    {
        var context = await CreateContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1600, Height = 900 },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        await GotoAppReadyAsync(page, $"{BaseUrl}/diagram-editor");
        await page.Locator(Canvas).First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await DiagramEditorE2E.PinDesktopAsync(page);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New document" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-node').length === 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        await page.EvaluateAsync(
            """
            ({ stencilId, xRatio, yRatio }) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const rect = canvas.getBoundingClientRect();
                window.__tmDiagramDragStencil = stencilId;
                canvas.dispatchEvent(new DragEvent('drop', {
                    bubbles: true,
                    cancelable: true,
                    clientX: rect.left + rect.width * xRatio,
                    clientY: rect.top + rect.height * yRatio,
                    dataTransfer: new DataTransfer()
                }));
            }
            """,
            new { stencilId = "general.sticky-note", xRatio = 0.35, yRatio = 0.35 });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\".tm-diagram-node[data-stencil-id='general.sticky-note']\").length >= 1",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        var node = page.Locator($"{Canvas} g.tm-diagram-node[data-stencil-id='general.sticky-note']").First;
        var fillPaint = node.Locator(".tm-diagram-node__svg-bg *").First;

        var lightFill = await fillPaint.EvaluateAsync<string>("e => getComputedStyle(e).fill");
        Assert.AreEqual("#fef08a", ParseHex(lightFill), "light sticky-note fill stays the 2.12.0 hex");
        var lightInk = await node.Locator(".tm-diagram-node__text").First
            .EvaluateAsync<string>("e => getComputedStyle(e).color");
        Assert.AreEqual("#4b5563", ParseHex(lightInk), "light on-fill ink is gray-600");
        await ShotCanvasAsync(page, "diagram-editor-light");
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });
        await AssertAxeCleanAsync(page, "g.tm-diagram-node", "LIGHT");

        await ToggleDarkAsync(page);

        var darkFill = await fillPaint.EvaluateAsync<string>("e => getComputedStyle(e).fill");
        Assert.AreEqual("#713f12", ParseHex(darkFill), "dark sticky-note fill is yellow-900");
        var darkInk = await node.Locator(".tm-diagram-node__text").First
            .EvaluateAsync<string>("e => getComputedStyle(e).color");
        Assert.AreEqual("#e5e7eb", ParseHex(darkInk), "dark on-fill ink is gray-200");
        await ShotCanvasAsync(page, "diagram-editor-dark");
        await AssertAxeCleanAsync(page, "g.tm-diagram-node", "DARK");
    }

    private static async Task AssertAxeCleanAsync(IPage page, string selector, string theme)
    {
        var violations = await page.EvaluateAsync<string[]>(
            """
            async (selector) => {
                const host = document.querySelector(selector) || document.body;
                await Promise.all(document.getAnimations()
                    .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                    .map(a => a.finished.catch(() => { })));
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help}`);
            }
            """,
            selector);
        Assert.AreEqual(0, violations.Length, theme + ":" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }
}
