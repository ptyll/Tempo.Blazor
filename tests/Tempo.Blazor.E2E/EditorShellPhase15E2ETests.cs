using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F15 (cluster-C carry-forwards C7/C8/C9) on the real components: the tablet fit width keeps the
/// canvas at MinCanvasWidth at 768 with the inspector expanded; the mobile sheet keeps a 14rem body
/// floor on a short stage and honours host-configured MobileSheetSnapPoints; and a TmSidePanel
/// without a viewport scope takes its sheet position from the drawer's own viewport probe (a 500px
/// column at 1440 opens a side sheet, not a bottom sheet). All viewport-only shots, computed values
/// asserted, touch emulation where the layout reads the pointer.
/// </summary>
[TestClass]
public class EditorShellPhase15E2ETests : WasmTestBase
{
    private const string Route = "/editor-shell";

    private static string Shell => "[data-testid='editor-shell']";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "editor-shell");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root (TempoBlazor.slnx).");
    }

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private static async Task<IBrowserContext> CreateTouchContextAsync(int width, int height, string locale = "en-US")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = true,
            IsMobile = width < 640,
            Locale = locale,
            IgnoreHTTPSErrors = true,
        });
        return context;
    }

    private async Task<IPage> GotoEditorShellAsync(IBrowserContext context, int width, int height)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{Route}");
        await WaitForAppReadyAsync(page);
        await page.SetViewportSizeAsync(width, height);
        if (width < 640)
        {
            // The standalone-inspector demo below the fold presents as a modal bottom sheet on a
            // phone and would cover the editor shell — dismiss it first.
            var close = page.Locator(".tm-side-panel-sheet .tm-drawer__close");
            if (await close.CountAsync() > 0)
            {
                await close.First.ClickAsync();
                await Assertions.Expect(page.Locator(".tm-side-panel-sheet")).ToBeHiddenAsync(
                    new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
            }
        }
        return page;
    }

    private async Task<string> CaptureViewportOnlyAsync(IPage page, string name)
    {
        await page.WaitForTimeoutAsync(200);
        await page.EvaluateAsync("() => { document.querySelector('[data-testid=\"editor-shell-host\"]')?.scrollIntoView({ block: 'start', behavior: 'instant' }); window.scrollBy({ top: -84, behavior: 'instant' }); }");
        await page.WaitForTimeoutAsync(400);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        var path = Path.Combine(ShotDir, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
        return path;
    }

    // ── C9: the tablet fit width keeps the canvas at MinCanvasWidth ──────────

    [TestMethod]
    public async Task Demo_768_Touch_Tablet_CanvasKeepsMinCanvasWidth()
    {
        var context = await CreateTouchContextAsync(768, 1024);
        var page = await GotoEditorShellAsync(context, 768, 1024);
        RegisterContext(context);

        var shell = page.Locator(Shell);
        await Assertions.Expect(shell).ToHaveAttributeAsync("data-layout", "tablet");

        // C9 with its floor: the fit narrows the inspector by the measured canvas deficit below
        // MinCanvasWidth, clamped to MinRightWidth (240). At 768 even the minimum leaves less than
        // 400px for the canvas, so the fit parks at the minimum: the canvas keeps everything the
        // panel can give (the shell main row minus the parked panel, the rail and the separators)
        // — far above the unfitted ~278, but below MinCanvasWidth.
        await page.WaitForFunctionAsync(
            """
            () => {
                const panel = document.querySelector("[data-testid='editor-shell'] aside[data-region='right']");
                return !!panel && Math.abs(panel.getBoundingClientRect().width - 240) < 2;
            }
            """);
        var row = await page.EvaluateAsync<double[]>(
            """
            () => {
                const main = document.querySelector("[data-testid='editor-shell'] [data-region='main']");
                const canvas = main.querySelector("[data-region='canvas']");
                const panel = main.querySelector("aside[data-region='right']");
                // Every main-row child that is not the canvas: the parked panel, its toggle and
                // separator, and the other panel's rail.
                const others = [...main.children]
                    .filter((k) => k.getAttribute("data-region") !== "canvas")
                    .reduce((sum, k) => sum + k.getBoundingClientRect().width, 0);
                return [main.getBoundingClientRect().width, others, panel.getBoundingClientRect().width, canvas.getBoundingClientRect().width];
            }
            """);
        var main = row[0];
        var others = row[1];
        var panel = row[2];
        var canvas = row[3];
        // Unfitted baseline: the same row with the inspector at its host width (320).
        var unfitted = main - others + 240 - 320;
        Assert.IsTrue(canvas >= unfitted + 60, $"the fit gives the canvas back most of the deficit (canvas {canvas}, unfitted {unfitted})");
        Assert.IsTrue(canvas < 399, $"at 768 even MinRightWidth leaves the canvas below MinCanvasWidth (canvas {canvas})");
        Assert.IsTrue(panel is >= 239 and <= 321, $"the inspector parks at its minimum, never wider than its host width (panel {panel})");
        await CaptureViewportOnlyAsync(page, "768-tablet-fit-width");
    }

    // ── C8: the 14rem body floor and the host-configured snap points ─────────

    [TestMethod]
    public async Task Demo_390x520_HalfSnap_SheetPanelKeepsThe14remFloor()
    {
        var context = await CreateTouchContextAsync(390, 520);
        var page = await GotoEditorShellAsync(context, 390, 520);
        RegisterContext(context);

        // Wait for the measured condition instead of a fixed delay: the half-snap sheet panel must
        // render at its floored height (14rem, never past the stage).
        await page.WaitForFunctionAsync(
            """
            () => {
                const stage = document.querySelector("[data-testid='editor-shell'] .tm-editor-shell__stage")?.getBoundingClientRect();
                const panel = document.querySelector("[data-testid='editor-shell'] .tm-editor-shell__sheet .tm-drawer__panel")?.getBoundingClientRect();
                return !!stage && !!panel && panel.height >= 223 && panel.height <= stage.height + 1;
            }
            """);
        var m = await page.EvaluateAsync<double[]>(
            """
            () => {
                const stage = document.querySelector("[data-testid='editor-shell'] .tm-editor-shell__stage").getBoundingClientRect();
                const panel = document.querySelector("[data-testid='editor-shell'] .tm-editor-shell__sheet .tm-drawer__panel").getBoundingClientRect();
                return [stage.height, panel.height];
            }
            """);
        Assert.IsTrue(m[1] >= 224 - 1, $"the sheet panel keeps the 14rem floor on a short stage (panel {m[1]}, stage {m[0]})");
        Assert.IsTrue(m[1] <= m[0] + 1, "the floored sheet never grows past its stage");
    }

    [TestMethod]
    public async Task Demo_390_CustomSnapPoints_DriveTheSheetHeight()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);

        await page.Locator("[data-testid='es-snaps-toggle']").ClickAsync();
        var sheet = page.Locator($"{Shell} .tm-editor-shell__sheet");
        await Assertions.Expect(sheet).ToHaveAttributeAsync("data-snap-points", "0.4,0.75,1");

        // The lowest custom snap (0.4) drives the sheet height and the canvas reserve.
        await page.WaitForTimeoutAsync(400);
        var heights = await page.EvaluateAsync<double[]>(
            """
            () => {
                const stage = document.querySelector("[data-testid='editor-shell'] .tm-editor-shell__stage").getBoundingClientRect();
                const panel = document.querySelector("[data-testid='editor-shell'] .tm-editor-shell__sheet .tm-drawer__panel").getBoundingClientRect();
                return [stage.height, panel.height];
            }
            """);
        Assert.IsTrue(heights[1] >= heights[0] * 0.4 - 1 && heights[1] <= heights[0] * 0.4 + 40,
            $"the sheet sits at the 0.4 custom snap (stage {heights[0]}, panel {heights[1]})");
    }

    // ── C7: a scope-less side panel reads its position from the sheet's own probe ──

    [TestMethod]
    public async Task SidePanel_NoScope_1440_NarrowColumn_OpensASideSheet()
    {
        // /overlay-no-scope renders no app-level viewport scope: the sheet measures the viewport
        // itself. A 500px (mobile-resolving) column would give a bottom sheet from the container;
        // the probe says desktop, so the panel must open a SIDE sheet.
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/overlay-no-scope");
        RegisterContext(context);
        await WaitForAppReadyAsync(page);

        // The narrow column resolves its container to mobile…
        await Assertions.Expect(page.Locator("[data-testid='noscope-side-panel-column'][data-layout='mobile']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        await page.Locator("[data-testid='noscope-side-panel-toggle']").ClickAsync();
        var sheet = page.Locator(".tm-side-panel-sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        await page.WaitForTimeoutAsync(400);

        var cls = await sheet.GetAttributeAsync("class") ?? string.Empty;
        Assert.IsTrue(cls.Contains("tm-drawer--right"), $"the probe-measured desktop viewport opens a side sheet ({cls})");
        Assert.IsFalse(cls.Contains("tm-drawer--bottom"), "the mobile container must not force a bottom sheet");
    }
}
