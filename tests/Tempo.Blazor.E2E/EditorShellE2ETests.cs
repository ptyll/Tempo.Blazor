using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F6 lane: TmEditorShell and TmSidePanel on the real components — the /editor-shell demo page.
/// The AUTO shell is driven at real sizes: 1440 (desktop), 1280 and 768 (tablet: docked panels, one
/// expanded, no modal), and 390/320 with touch (mobile sheet / tabs); a second shell uses custom
/// thresholds (desktop from 1200, mobile below 768). Every screenshot
/// is viewport-only at the real viewport height (FullPage resets touch emulation — the F5 lesson),
/// and computed values (bounding boxes, positions, track counts) are asserted, not only tokens.
/// </summary>
[TestClass]
public class EditorShellE2ETests : WasmTestBase
{
    private const string Route = "/editor-shell";

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

    private async Task<string> CaptureViewportOnlyAsync(IPage page, string name)
    {
        await page.WaitForTimeoutAsync(200);
        // Frame the shell host at the top of the viewport so the shot shows the editor, not the
        // page chrome above it (still a viewport-only capture at the real viewport height).
        await page.EvaluateAsync("() => { document.querySelector('[data-testid=\"editor-shell-host\"]')?.scrollIntoView({ block: 'start', behavior: 'instant' }); window.scrollBy({ top: -84, behavior: 'instant' }); }");
        await page.WaitForTimeoutAsync(400);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        var path = Path.Combine(ShotDir, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
        return path;
    }

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

    private static Task WaitForFocusOnToggleAsync(IPage page, string side)
        => page.WaitForFunctionAsync(
            "side => { const a = document.activeElement; return !!a && a.classList.contains(`tm-editor-shell__panel-toggle--${side}`); }",
            side, new PageWaitForFunctionOptions { Timeout = 5000 });

    // ── Desktop ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Demo_1440_Desktop_RendersThreeSideBySideRegions()
    {
        var context = await CreateTouchContextAsync(1440, 900);
        var page = await GotoEditorShellAsync(context, 1440, 900);
        RegisterContext(context);

        var left = page.Locator("[data-testid='editor-shell'] [data-region='left']");
        var canvas = page.Locator("[data-testid='editor-shell'] [data-region='canvas']");
        var right = page.Locator("[data-testid='editor-shell'] [data-region='right']");
        await Assertions.Expect(left).ToBeVisibleAsync();
        await Assertions.Expect(canvas).ToBeVisibleAsync();
        await Assertions.Expect(right).ToBeVisibleAsync();

        // Three real columns, left before canvas before right.
        var leftBox = (await left.BoundingBoxAsync())!;
        var canvasBox = (await canvas.BoundingBoxAsync())!;
        var rightBox = (await right.BoundingBoxAsync())!;
        Assert.IsTrue(leftBox.X + leftBox.Width <= canvasBox.X + 60, "left panel must sit left of the canvas");
        Assert.IsTrue(canvasBox.X + canvasBox.Width <= rightBox.X + 60, "right panel must sit right of the canvas");

        // No overlay surfaces and no mobile bar at desktop width.
        Assert.AreEqual(0, await page.Locator("[data-testid='editor-shell'] .tm-drawer").CountAsync());
        Assert.AreEqual(0, await page.Locator("[data-testid='editor-shell'] .tm-mobile-action-bar__bar").CountAsync());

        await CaptureViewportOnlyAsync(page, "1440-desktop");
    }

    [TestMethod]
    public async Task Demo_1440_CollapseLeft_RendersRail_ExpandRestoresPanel()
    {
        var context = await CreateTouchContextAsync(1440, 900);
        var page = await GotoEditorShellAsync(context, 1440, 900);
        RegisterContext(context);

        var toggle = page.Locator("[data-testid='editor-shell'] .tm-editor-shell__panel-toggle--left");
        await Assertions.Expect(toggle).ToBeVisibleAsync();
        Assert.AreEqual("true", await toggle.GetAttributeAsync("aria-expanded"),
            "the strip next to an expanded panel is the collapse toggle");
        var controls = await toggle.GetAttributeAsync("aria-controls");
        Assert.IsFalse(string.IsNullOrEmpty(controls));
        Assert.AreEqual("ASIDE", await page.EvaluateAsync<string>("id => document.getElementById(id)?.tagName ?? ''", controls!),
            "aria-controls names the real panel, not a hidden anchor");

        await toggle.ClickAsync();
        var rail = page.Locator("[data-testid='editor-shell'] .tm-editor-shell__rail--left");
        await Assertions.Expect(rail).ToBeVisibleAsync();
        Assert.AreEqual(0, await page.Locator("[data-testid='editor-shell'] [data-region='left']").CountAsync());
        var railButton = rail.Locator("> button.tm-editor-shell__panel-toggle");
        Assert.AreEqual("false", await railButton.GetAttributeAsync("aria-expanded"), "a rail expand button reads collapsed");
        Assert.IsNull(await railButton.GetAttributeAsync("aria-controls"), "its panel is not rendered, so no aria-controls");
        await CaptureViewportOnlyAsync(page, "1440-collapsed-rail");

        await rail.Locator("> button.tm-editor-shell__panel-toggle").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='editor-shell'] [data-region='left']")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Demo_1440_HideLeftViaStripToggle_ShowsToggleWithExpandedFalse()
    {
        // The demo binds LeftOpen two-way; drive the same state through the demo host buttons is
        // not exposed, so use the shell's own strip: collapse first, then verify the rail expand
        // path returns focus to a working toggle (keyboard).
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}{Route}");
        await WaitForAppReadyAsync(page);

        var toggle = page.Locator("[data-testid='editor-shell'] .tm-editor-shell__panel-toggle--left");
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var rail = page.Locator("[data-testid='editor-shell'] .tm-editor-shell__rail--left > button.tm-editor-shell__panel-toggle");
        await Assertions.Expect(rail).ToBeVisibleAsync();

        // Exactly one activation per press: Enter fired the collapse once. Focus the rail's own
        // button (the collapsed strip unmounted with the toggle) and expand from the keyboard.
        await rail.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[data-testid='editor-shell'] [data-region='left']")).ToBeVisibleAsync();
    }

    // ── Mobile ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Demo_390_Mobile_CanvasTabsSheetAndActionBar()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);

        await Assertions.Expect(page.Locator($"{Shell} [data-region='canvas']")).ToBeVisibleAsync();

        // The panels sheet is INLINE (in-container): never promoted.
        var sheet = page.Locator($"{Shell} .tm-editor-shell__sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        Assert.IsNull(await sheet.GetAttributeAsync("popover"), "the inline panels sheet must not promote");

        // Sheet presentation with two open panels: a Left/Right tab strip inside the sheet.
        var tabs = page.Locator($"{Shell} .tm-editor-shell__sheet .tm-editor-shell__tab");
        Assert.AreEqual(2, await tabs.CountAsync());
        Assert.AreEqual("true", await tabs.First.GetAttributeAsync("aria-selected"));
        Assert.AreEqual(1, await page.Locator($"{Shell} .tm-editor-shell__sheet [role='tabpanel']:not([hidden])").CountAsync());

        // The mobile action bar: 3 tiles + More, sitting at the bottom of the shell.
        var bar = page.Locator("[data-testid='editor-shell'] .tm-mobile-action-bar__bar");
        await Assertions.Expect(bar).ToBeVisibleAsync();
        Assert.AreEqual(3, await bar.Locator(".tm-mobile-action-bar__action").CountAsync());
        await Assertions.Expect(bar.Locator(".tm-mobile-action-bar__more")).ToBeVisibleAsync();
        // Both rects in ONE evaluate: the page can still be smooth-scrolling, and two separate
        // bounding-box reads would straddle the scroll.
        var gap = await page.EvaluateAsync<double>(
            """
            () => {
                const shell = document.querySelector("[data-testid='editor-shell']").getBoundingClientRect();
                const bar = document.querySelector("[data-testid='editor-shell'] .tm-mobile-action-bar__bar").getBoundingClientRect();
                return Math.abs(bar.bottom - shell.bottom);
            }
            """);
        Assert.IsTrue(gap <= 2, $"the action bar must sit at the bottom of the shell (gap {gap}px)");

        // The sheet must show real content: header + a usable body, inside the panels host.
        var panelBody = await page.Locator($"{Shell} .tm-editor-shell__sheet .tm-drawer__body").BoundingBoxAsync();
        Assert.IsTrue(panelBody is { Height: > 60 }, $"the sheet body must be visible (height {panelBody?.Height})");
        await Assertions.Expect(page.Locator($"{Shell} .tm-editor-shell__sheet [role='tabpanel']:not([hidden])")).ToBeVisibleAsync();
        await CaptureViewportOnlyAsync(page, "390-mobile-sheet");
        var barBottom = await page.EvaluateAsync<double>("() => document.querySelector(\"[data-testid=\u0027editor-shell\u0027] .tm-mobile-action-bar__bar\").getBoundingClientRect().bottom");
        Assert.IsTrue(barBottom <= 844 + 1, $"the mobile action bar is inside the first screen after framing the host (bottom {barBottom}px)");
    }

    [TestMethod]
    public async Task Demo_390_Mobile_TabSwitch_ShowsOtherPanel()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);

        var tabs = page.Locator($"{Shell} .tm-editor-shell__sheet .tm-editor-shell__tab");
        await tabs.Nth(1).ClickAsync();
        Assert.AreEqual("true", await tabs.Nth(1).GetAttributeAsync("aria-selected"));
        await Assertions.Expect(page.Locator($"{Shell} [role='tabpanel'] [data-testid='es-properties']")).ToBeVisibleAsync();

        await CaptureViewportOnlyAsync(page, "390-mobile-tab-properties");
    }

    [TestMethod]
    public async Task Demo_390_Mobile_CloseSheet_ShowsPanelsToggle_ReopenWorks()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);

        await page.Locator($"{Shell} .tm-editor-shell__sheet-close").ClickAsync();
        await Assertions.Expect(page.Locator($"{Shell} .tm-editor-shell__sheet")).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        Assert.AreEqual(0, await page.Locator($"{Shell} .tm-drawer").CountAsync());

        var reopen = page.Locator($"{Shell} .tm-editor-shell__panel-toggle--mobile");
        await Assertions.Expect(reopen).ToBeVisibleAsync();
        Assert.AreEqual("false", await reopen.GetAttributeAsync("aria-expanded"));
        Assert.IsNull(await reopen.GetAttributeAsync("aria-controls"), "the sheet it controls is not rendered");
        StringAssert.Contains(await reopen.InnerTextAsync(), "Blocks", "the closed affordance is labelled, not a bare chevron");
        var toggleBox = (await reopen.BoundingBoxAsync())!;
        var labelBox = (await reopen.Locator(".tm-editor-shell__panel-toggle-label").BoundingBoxAsync())!;
        Assert.IsTrue(toggleBox.Width >= 160, $"the labelled bar is wide enough to SHOW its label under touch (coarse pointer) too ({toggleBox.Width}px)");
        Assert.IsTrue(labelBox.Width >= 60 && labelBox.X + labelBox.Width <= toggleBox.X + toggleBox.Width + 1, "the label is painted inside the bar, not clipped");
        await CaptureViewportOnlyAsync(page, "390-mobile-sheet-closed");

        await reopen.ClickAsync();
        await Assertions.Expect(page.Locator($"{Shell} .tm-editor-shell__sheet")).ToBeVisibleAsync();
        Assert.AreEqual(0, await page.Locator($"{Shell} .tm-editor-shell__panel-toggle--mobile").CountAsync());
    }

    [TestMethod]
    public async Task Demo_390_Mobile_BarAction_InvokesOnce()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);

        var bar = page.Locator("[data-testid='editor-shell'] .tm-mobile-action-bar__bar");
        await bar.Locator("[data-action-id='undo']").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='es-last-action']")).ToContainTextAsync("Undo");
    }

    [TestMethod]
    public async Task Demo_320_Mobile_NoHorizontalOverflow()
    {
        var context = await CreateTouchContextAsync(320, 700, locale: "fr-FR");
        var page = await GotoEditorShellAsync(context, 320, 700);
        RegisterContext(context);

        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(overflow <= 320, $"no horizontal overflow at 320 (scrollWidth {overflow})");

        await Assertions.Expect(page.Locator($"{Shell} .tm-editor-shell__sheet")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='editor-shell'] .tm-mobile-action-bar__bar")).ToBeVisibleAsync();
        // A tab label stays on ONE line (no mid-word break of the fr "Propriétés").
        var tabHeights = await page.EvaluateAsync<double[]>("""() => [...document.querySelectorAll("[data-testid='editor-shell'] .tm-editor-shell__sheet .tm-editor-shell__tab")].map(t => t.getBoundingClientRect().height)""");
        Assert.IsTrue(tabHeights.Length == 2 && tabHeights.All(h => h < 40), $"tab labels must not wrap: {string.Join(',', tabHeights)}");
        await CaptureViewportOnlyAsync(page, "320-mobile-fr");
    }

    // ── Close paths, focus restore, flips, themes (the F5 pattern, on the real components) ──

    private async Task<IPage> OpenPlainPageAsync(int width, int height, string locale = "en-US", bool touch = false, ReducedMotion? motion = null)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch && width < 640,
            Locale = locale,
            IgnoreHTTPSErrors = true,
            ReducedMotion = motion,
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{Route}");
        return page;
    }

    [TestMethod]
    public async Task Demo_390_Mobile_EscapeAndClose_ReturnFocusToThePanelsToggle_AndSurviveALayoutFlip()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);

        await page.Locator($"{Shell} .tm-editor-shell__sheet-close").ClickAsync();
        var reopen = page.Locator($"{Shell} .tm-editor-shell__panel-toggle--mobile");
        await Assertions.Expect(reopen).ToBeVisibleAsync();
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.classList.contains('tm-editor-shell__panel-toggle--mobile')",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });

        // Reopen, then close with Escape: focus lands on the toggle again.
        await reopen.ClickAsync();
        await Assertions.Expect(page.Locator($"{Shell} .tm-editor-shell__sheet")).ToBeVisibleAsync();
        await page.Locator($"{Shell} .tm-editor-shell__sheet .tm-editor-shell__tab").First.FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator($"{Shell} .tm-editor-shell__sheet")).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.classList.contains('tm-editor-shell__panel-toggle--mobile')",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });
        Assert.AreEqual("false", await page.Locator($"{Shell} .tm-editor-shell__panel-toggle--mobile").GetAttributeAsync("aria-expanded"));

        // 390 -> 1440 -> 390: nothing reopens, no error UI, the panels stay closed.
        await page.SetViewportSizeAsync(1440, 900);
        await Assertions.Expect(page.Locator($"{Shell}[data-layout='desktop']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        await page.SetViewportSizeAsync(390, 844);
        await Assertions.Expect(page.Locator("[data-testid='editor-shell'][data-layout='mobile']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        Assert.AreEqual(0, await page.Locator($"{Shell} .tm-editor-shell__sheet").CountAsync(),
            "a viewport flip must not reopen the closed panels sheet");
        Assert.AreEqual("false", await page.Locator($"{Shell} .tm-editor-shell__panel-toggle--mobile").GetAttributeAsync("aria-expanded"));
        Assert.AreEqual("none", await page.EvaluateAsync<string>("() => getComputedStyle(document.getElementById('blazor-error-ui')).display"),
            "no error UI may surface");
    }

    [TestMethod]
    public async Task Demo_1440_CollapseBothPanels_CanvasGrowsToRails()
    {
        var page = await OpenPlainPageAsync(1440, 900);
        RegisterContext(page.Context);
        await WaitForAppReadyAsync(page);

        var shell = page.Locator("[data-testid='editor-shell']");
        var before = (await shell.Locator("[data-region='canvas']").BoundingBoxAsync())!.Width;
        await shell.Locator(".tm-editor-shell__panel-toggle--left").ClickAsync();
        await shell.Locator(".tm-editor-shell__panel-toggle--right").ClickAsync();
        await Assertions.Expect(shell.Locator(".tm-editor-shell__rail")).ToHaveCountAsync(2);
        var after = (await shell.Locator("[data-region='canvas']").BoundingBoxAsync())!.Width;
        Assert.IsTrue(after > before + 300, $"the canvas must absorb the freed panel width ({before} -> {after})");
        var rail = (await shell.Locator(".tm-editor-shell__rail--left").BoundingBoxAsync())!;
        Assert.IsTrue(rail.Width is >= 43 and <= 46, $"the rail is the 44px touch-target wide (was {rail.Width})");
        await CaptureViewportOnlyAsync(page, "1440-both-collapsed");
    }

    [TestMethod]
    public async Task Demo_390_ShortViewport_CanvasScrollsAndSheetStaysInsideTheShell()
    {
        var context = await CreateTouchContextAsync(390, 520);
        var page = await GotoEditorShellAsync(context, 390, 520);
        RegisterContext(context);

        await page.WaitForTimeoutAsync(700);
        var m = await page.EvaluateAsync<double[]>(
            """
            () => {
                const stage = document.querySelector("[data-testid='editor-shell'] .tm-editor-shell__stage").getBoundingClientRect();
                const sheet = document.querySelector("[data-testid='editor-shell'] .tm-editor-shell__sheet .tm-drawer__panel").getBoundingClientRect();
                const canvas = document.querySelector("[data-testid='editor-shell'] [data-region='canvas']");
                return [stage.top, stage.bottom, sheet.top, sheet.bottom, canvas.scrollHeight, canvas.clientHeight];
            }
            """);
        Assert.IsTrue(m[2] >= m[0] - 1 && m[3] <= m[1] + 1, $"the sheet must stay inside the stage ({string.Join(',', m)})");
        Assert.IsTrue(m[4] > 0 && m[5] > 0, "the canvas keeps a scrollable box");
        await CaptureViewportOnlyAsync(page, "390x520-short");
    }

    [TestMethod]
    public async Task Demo_320_Czech_TabLabelsStayOnOneLine()
    {
        var context = await CreateTouchContextAsync(320, 700, locale: "cs-CZ");
        var page = await GotoEditorShellAsync(context, 320, 700);
        RegisterContext(context);

        var heights = await page.EvaluateAsync<double[]>(
            """() => [...document.querySelectorAll("[data-testid='editor-shell'] .tm-editor-shell__sheet .tm-editor-shell__tab")].map(t => t.getBoundingClientRect().height)""");
        Assert.AreEqual(2, heights.Length);
        Assert.IsTrue(heights.All(h => h < 40), $"tab labels must not wrap: {string.Join(',', heights)}");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= 320"));
        await CaptureViewportOnlyAsync(page, "320-mobile-cs");
    }

    [TestMethod]
    public async Task Demo_Dark_And_Indigo_PaintTheShellSurfaces()
    {
        var page = await OpenPlainPageAsync(1440, 900);
        RegisterContext(page.Context);
        await WaitForAppReadyAsync(page);
        var panel = "[data-testid='editor-shell'] [data-region='left']";

        await page.EvaluateAsync("() => document.documentElement.setAttribute('data-theme', 'dark')");
        await page.WaitForTimeoutAsync(300);
        var darkBg = await page.EvaluateAsync<string>($"() => getComputedStyle(document.querySelector(\"{panel}\")).backgroundColor");
        var darkCanvas = await page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector(\"[data-testid='editor-shell']\")).backgroundColor");
        Assert.IsTrue(Luminance(darkBg) < 0.25, $"dark panel surface must be dark, was {darkBg}");
        Assert.IsTrue(Luminance(darkCanvas) < 0.25, $"dark shell workspace must be dark, was {darkCanvas}");
        await CaptureViewportOnlyAsync(page, "1440-dark");

        await page.EvaluateAsync("() => { document.documentElement.setAttribute('data-theme', 'light'); document.documentElement.setAttribute('data-tm-theme', 'indigo'); }");
        await page.WaitForTimeoutAsync(300);
        var indigoShell = await page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector(\"[data-testid='editor-shell']\")).backgroundColor");
        Assert.IsTrue(Luminance(indigoShell) > 0.85, $"indigo workspace is the off-white, was {indigoShell}");
        await CaptureViewportOnlyAsync(page, "1440-indigo");
    }

    [TestMethod]
    public async Task Demo_390_ReducedMotion_SheetStillOpensAndCloses()
    {
        var page = await OpenPlainPageAsync(390, 844, touch: true, motion: ReducedMotion.Reduce);
        RegisterContext(page.Context);
        await WaitForAppReadyAsync(page);
        var close = page.Locator(".tm-side-panel-sheet .tm-drawer__close");
        if (await close.CountAsync() > 0) await close.First.ClickAsync();

        await Assertions.Expect(page.Locator($"{Shell} .tm-editor-shell__sheet")).ToBeVisibleAsync();
        await page.Locator($"{Shell} .tm-editor-shell__sheet-close").ClickAsync();
        await Assertions.Expect(page.Locator($"{Shell} .tm-editor-shell__sheet")).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(prefers-reduced-motion: reduce)').matches"));
    }

    private static double Luminance(string rgb)
    {
        var nums = System.Text.RegularExpressions.Regex.Matches(rgb, @"\d+(\.\d+)?")
            .Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)).Take(3).ToArray();
        return nums.Length < 3 ? 1 : (0.2126 * nums[0] + 0.7152 * nums[1] + 0.0722 * nums[2]) / 255.0;
    }
    // ── Tablet = docked panels (Q1) at real sizes, AUTO shell ───────────────

    private static string Shell => "[data-testid='editor-shell']";

    private static async Task<double> WidthOfAsync(IPage page, string selector)
        => await page.EvaluateAsync<double>("s => document.querySelector(s).getBoundingClientRect().width", selector);

    private static string ExpectedLayout(double width, int sm, int lg) => width < sm ? "mobile" : width < lg ? "tablet" : "desktop";

    [TestMethod]
    public async Task Demo_1280_AutoShell_ResolvesTablet_DockedPanelsNoModalNoInert_FocusUntouched()
    {
        var page = await OpenPlainPageAsync(1280, 800);
        RegisterContext(page.Context);
        await WaitForAppReadyAsync(page);

        var shell = page.Locator(Shell);
        await Assertions.Expect(shell).ToHaveAttributeAsync("data-layout", "tablet");
        var width = await WidthOfAsync(page, Shell);
        Assert.IsTrue(width is >= 640 and < 1024, $"1280 minus the app sidebar leaves a tablet-sized container ({width}px)");

        // Docked, not modal: no drawer, no popover, nothing inert, no focus trap, canvas operable.
        Assert.AreEqual(0, await shell.Locator(".tm-drawer").CountAsync(), "tablet opens no modal surface");
        Assert.AreEqual(0, await page.Locator("[inert]").CountAsync(), "nothing is inert on a first tablet render");
        Assert.AreEqual(0, await page.Locator("[popover]:popover-open").CountAsync());
        Assert.AreEqual(1, await shell.Locator("aside.tm-editor-shell__panel").CountAsync(), "at most one panel is expanded");
        await Assertions.Expect(shell.Locator(".tm-editor-shell__rail--left")).ToBeVisibleAsync();
        Assert.IsFalse(await page.EvaluateAsync<bool>("""() => !!document.activeElement?.closest("[data-testid='editor-shell']")"""), "the shell moved no focus into itself on load");
        Assert.AreEqual(1, await shell.Locator("[role='separator']").CountAsync(), "the one expanded docked panel is resizable");

        // The canvas is operable: its block takes a click.
        await shell.Locator("[data-testid='es-canvas-block']").ClickAsync();
        Assert.AreEqual(0, await page.Locator("[inert]").CountAsync());

        await CaptureViewportOnlyAsync(page, "1280-tablet");
    }

    [TestMethod]
    public async Task Demo_1280_Tablet_ExpandingTheRail_RailsTheOtherPanel_WithoutAModalOrAnEscape()
    {
        var page = await OpenPlainPageAsync(1280, 800);
        RegisterContext(page.Context);
        await WaitForAppReadyAsync(page);
        var shell = page.Locator(Shell);
        await Assertions.Expect(shell).ToHaveAttributeAsync("data-layout", "tablet");

        await shell.Locator(".tm-editor-shell__rail--left > button.tm-editor-shell__panel-toggle").ClickAsync();

        await Assertions.Expect(shell.Locator("aside[data-region='left']")).ToBeVisibleAsync();
        await Assertions.Expect(shell.Locator(".tm-editor-shell__rail--right")).ToBeVisibleAsync();
        Assert.AreEqual(1, await shell.Locator("aside.tm-editor-shell__panel").CountAsync(), "expanding one rails the other");
        Assert.AreEqual(0, await shell.Locator(".tm-drawer").CountAsync());
        Assert.AreEqual(0, await page.Locator("[inert]").CountAsync());
        await CaptureViewportOnlyAsync(page, "1280-tablet-left-expanded");
    }

    [TestMethod]
    public async Task Demo_1440_To_1280_Flip_KeepsFocusAndAtMostOneExpandedPanel()
    {
        var page = await OpenPlainPageAsync(1440, 900);
        RegisterContext(page.Context);
        await WaitForAppReadyAsync(page);
        var shell = page.Locator(Shell);
        await Assertions.Expect(shell).ToHaveAttributeAsync("data-layout", "desktop");
        Assert.AreEqual(2, await shell.Locator("aside.tm-editor-shell__panel").CountAsync());

        await shell.Locator(".es-toolbox input").First.FocusAsync();
        await page.SetViewportSizeAsync(1280, 800);

        await Assertions.Expect(shell).ToHaveAttributeAsync("data-layout", "tablet");
        Assert.AreEqual(1, await shell.Locator("aside.tm-editor-shell__panel").CountAsync());
        Assert.AreEqual(0, await shell.Locator(".tm-drawer").CountAsync());
        Assert.AreEqual(0, await page.Locator("[inert]").CountAsync());
        await page.WaitForFunctionAsync("""() => !!document.activeElement?.closest("[data-testid='editor-shell'] .tm-editor-shell__rail--left")""",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task Demo_1440_To_1280_Flip_FocusOnTheCanvas_StaysOnTheCanvas()
    {
        var page = await OpenPlainPageAsync(1440, 900);
        RegisterContext(page.Context);
        await WaitForAppReadyAsync(page);
        var shell = page.Locator(Shell);
        await Assertions.Expect(shell).ToHaveAttributeAsync("data-layout", "desktop");

        await shell.Locator("[data-testid='es-canvas-block']").FocusAsync();
        await page.SetViewportSizeAsync(1280, 800);

        await Assertions.Expect(shell).ToHaveAttributeAsync("data-layout", "tablet");
        await page.WaitForTimeoutAsync(500);
        Assert.AreEqual("es-canvas-block", await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('data-testid') ?? ''"),
            "a focus outside the panel that got railed is never moved");
    }

    [TestMethod]
    public async Task Demo_768_Touch_Tablet_DockedPanels()
    {
        var context = await CreateTouchContextAsync(768, 1024);
        var page = await GotoEditorShellAsync(context, 768, 1024);
        RegisterContext(context);

        var shell = page.Locator(Shell);
        await Assertions.Expect(shell).ToHaveAttributeAsync("data-layout", "tablet");
        Assert.AreEqual(0, await shell.Locator(".tm-drawer").CountAsync());
        Assert.AreEqual(1, await shell.Locator("aside.tm-editor-shell__panel").CountAsync());
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"), "touch emulation must be on");
        await CaptureViewportOnlyAsync(page, "768-tablet");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"), "touch survives the screenshot");
    }

    [TestMethod]
    public async Task Demo_1024_Touch_AutoShell_ResolvesFromItsContainer()
    {
        var context = await CreateTouchContextAsync(1024, 768);
        var page = await GotoEditorShellAsync(context, 1024, 768);
        RegisterContext(context);

        var shell = page.Locator(Shell);
        var width = await WidthOfAsync(page, Shell);
        await Assertions.Expect(shell).ToHaveAttributeAsync("data-layout", ExpectedLayout(width, 640, 1024));
        await CaptureViewportOnlyAsync(page, "1024-auto");
    }

    // ── Per-instance thresholds (Q3) ────────────────────────────────────────

    [TestMethod]
    public async Task Demo_1440_CustomThresholdShell_ClassifiesWithItsOwnPair_WhileTheDefaultShellDoesNot()
    {
        var page = await OpenPlainPageAsync(1440, 900);
        RegisterContext(page.Context);
        await WaitForAppReadyAsync(page);

        var defaults = page.Locator(Shell);
        var custom = page.Locator("[data-testid='editor-shell-thresholds']");
        var defaultWidth = await WidthOfAsync(page, Shell);
        var customWidth = await WidthOfAsync(page, "[data-testid='editor-shell-thresholds']");
        Assert.IsTrue(defaultWidth is >= 1024 and < 1200, $"the 1440 page leaves both shells a 1024-1199px container ({defaultWidth}px)");

        await Assertions.Expect(defaults).ToHaveAttributeAsync("data-layout", "desktop");
        await Assertions.Expect(custom).ToHaveAttributeAsync("data-layout", ExpectedLayout(customWidth, 768, 1200));
        Assert.AreEqual("tablet", await custom.GetAttributeAsync("data-layout"),
            "the same width is desktop with the defaults (>=1024) and tablet with an e-mail-like (768, 1200) pair");
    }

    [TestMethod]
    public async Task Demo_1280_CustomThresholdShell_TabletRail_UsesTheToolboxIconStrip()
    {
        var page = await OpenPlainPageAsync(1280, 800);
        RegisterContext(page.Context);
        await WaitForAppReadyAsync(page);
        var custom = page.Locator("[data-testid='editor-shell-thresholds']");
        await Assertions.Expect(custom).ToHaveAttributeAsync("data-layout", "tablet");

        await Assertions.Expect(custom.Locator(".tm-editor-shell__rail--left [data-testid='es-rail-icons-thresholds']")).ToBeAttachedAsync();
        Assert.AreEqual(1, await custom.Locator("aside.tm-editor-shell__panel").CountAsync());
    }

    [TestMethod]
    public async Task Demo_1280_UnboundThresholdShell_ExpandingTheImposedRail_SwapsThePanels()
    {
        // F6 r2 G1: the thresholds demo shell binds NOTHING (no CollapsedPanels, no handlers), so the
        // tablet rail is imposed by the shell itself; its expand button must still work.
        var page = await OpenPlainPageAsync(1280, 800);
        RegisterContext(page.Context);
        await WaitForAppReadyAsync(page);
        const string custom = "[data-testid='editor-shell-thresholds']";
        var shell = page.Locator(custom);
        await Assertions.Expect(shell).ToHaveAttributeAsync("data-layout", "tablet");
        await Assertions.Expect(shell.Locator("aside[data-region='right']")).ToBeVisibleAsync();
        await Assertions.Expect(shell.Locator(".tm-editor-shell__rail--left")).ToBeVisibleAsync();

        await shell.Locator(".tm-editor-shell__rail--left > button.tm-editor-shell__panel-toggle").ClickAsync();

        await Assertions.Expect(shell.Locator("aside[data-region='left']")).ToBeVisibleAsync();
        await Assertions.Expect(shell.Locator(".tm-editor-shell__rail--right")).ToBeVisibleAsync();
        Assert.AreEqual(1, await shell.Locator("aside.tm-editor-shell__panel").CountAsync(), "expanding one rails the other");

        await shell.Locator(".tm-editor-shell__rail--right > button.tm-editor-shell__panel-toggle").ClickAsync();
        await Assertions.Expect(shell.Locator("aside[data-region='right']")).ToBeVisibleAsync();
        await Assertions.Expect(shell.Locator(".tm-editor-shell__rail--left")).ToBeVisibleAsync();
    }
    [TestMethod]
    public async Task Demo_CustomThresholdShell_At700pxContainer_IsMobile_BarVisible_ToolbarSlotAbsent()
    {
        // F6 r2 G2: Breakpoints(768, 1200) makes a 700px container MOBILE. The internal action bar
        // must follow the shell (not re-measure with the 640/1024 defaults => tablet => hidden).
        var context = await CreateTouchContextAsync(1024, 900);
        var page = await GotoEditorShellAsync(context, 1024, 900);
        RegisterContext(context);
        const string custom = "[data-testid='editor-shell-thresholds']";
        await page.EvaluateAsync("() => { const h = document.querySelector(\"[data-testid='editor-shell-thresholds-host']\"); h.style.width = '700px'; h.style.maxWidth = '700px'; }");

        await Assertions.Expect(page.Locator(custom)).ToHaveAttributeAsync("data-layout", "mobile",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });
        var width = await WidthOfAsync(page, custom);
        Assert.IsTrue(width is >= 640 and < 768, $"the host is a 700px container, where the defaults say tablet ({width}px)");
        await page.WaitForTimeoutAsync(400);

        await Assertions.Expect(page.Locator($"{custom} .tm-mobile-action-bar__bar")).ToBeVisibleAsync();
        Assert.AreEqual(0, await page.Locator($"{custom} [data-region='toolbar']").CountAsync(), "the actions replace the Toolbar slot on a mobile shell");
    }
    // ── Resizer + persistence (Q4) ──────────────────────────────────────────

    private const string StorageKey = "tempo.tm-editor-shell.editor-shell-demo";

    [TestMethod]
    public async Task Demo_1440_Resizer_PointerDragAndKeyboard_ClampToMinMaxAndTheCanvas_Persist_AndRestoreAfterReload()
    {
        var page = await OpenPlainPageAsync(1440, 900);
        RegisterContext(page.Context);
        await WaitForAppReadyAsync(page);

        var separator = page.Locator($"{Shell} [role='separator'][data-side='left']");
        await Assertions.Expect(separator).ToBeVisibleAsync();
        Assert.AreEqual("vertical", await separator.GetAttributeAsync("aria-orientation"));
        Assert.AreEqual("200", await separator.GetAttributeAsync("aria-valuemin"));
        Assert.AreEqual("480", await separator.GetAttributeAsync("aria-valuemax"));
        Assert.AreEqual("280", await separator.GetAttributeAsync("aria-valuenow"));
        Assert.IsFalse(string.IsNullOrEmpty(await separator.GetAttributeAsync("aria-label")));
        Assert.IsNull(await page.EvaluateAsync<string?>($"() => localStorage.getItem('{StorageKey}')"), "nothing is stored before the user resizes");

        // At 1440 the demo canvas is ~398px — already at MinCanvasWidth — so GROWING the panel is
        // refused (the clamp working); shrinking is always allowed. Pointer drag: -60px.
        var canvasBefore = await WidthOfAsync(page, $"{Shell} [data-region='canvas']");
        Assert.IsTrue(canvasBefore <= 420, $"precondition: the canvas starts at/near the minimum ({canvasBefore}px)");
        var box = (await separator.BoundingBoxAsync())!;
        var y = box.Y + 100;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width / 2 - 30, y, new MouseMoveOptions { Steps = 4 });
        await page.Mouse.MoveAsync(box.X + box.Width / 2 - 60, y, new MouseMoveOptions { Steps = 4 });
        await page.Mouse.UpAsync();

        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "220");
        Assert.AreEqual(220, Math.Round(await WidthOfAsync(page, $"{Shell} aside[data-region='left']")), "the panel follows the pointer");
        Assert.IsTrue(await WidthOfAsync(page, $"{Shell} [data-region='canvas']") > canvasBefore + 40, "the canvas absorbed the freed width");
        var stored = await page.EvaluateAsync<string?>($"() => localStorage.getItem('{StorageKey}')");
        Assert.IsNotNull(stored, "a user resize persists under the key");
        StringAssert.Contains(stored, "\"left\"");
        Assert.IsFalse(stored.Contains("\"right\""), "only the panel the user resized is stored");

        // Dragging back past what the canvas allows stops at the canvas minimum (not at the max).
        var box2 = (await separator.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box2.X + box2.Width / 2, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box2.X + box2.Width / 2 + 150, y, new MouseMoveOptions { Steps = 6 });
        await page.Mouse.UpAsync();
        var grown = int.Parse((await separator.GetAttributeAsync("aria-valuenow"))!);
        Assert.IsTrue(grown is > 220 and < 300, $"a drag to +150 is clamped by MinCanvasWidth ({grown}px)");
        Assert.IsTrue(await WidthOfAsync(page, $"{Shell} [data-region='canvas']") >= 399, "the canvas never drops below MinCanvasWidth");

        // Keyboard: ArrowRight +16, ArrowLeft -16, Shift+ArrowLeft -64 (clamped to min), Home = min, End = clamped.
        await separator.FocusAsync();
        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "200");
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "216");
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "200");
        await page.Keyboard.PressAsync("Shift+ArrowLeft");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "200");
        await page.Keyboard.PressAsync("Shift+ArrowRight");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "264");
        await page.Keyboard.PressAsync("End");
        await page.WaitForTimeoutAsync(300);
        var endWidth = int.Parse((await separator.GetAttributeAsync("aria-valuenow"))!);
        Assert.IsTrue(endWidth is > 264 and <= 480, $"End goes to the maximum or as far as the canvas allows ({endWidth})");
        Assert.IsTrue(await WidthOfAsync(page, $"{Shell} [data-region='canvas']") >= 399, "the canvas never drops below MinCanvasWidth");

        // Settle on 264 and reload: the stored width is restored.
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("Shift+ArrowRight");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "264");

        await page.ReloadAsync();
        await WaitForAppReadyAsync(page);
        var restored = page.Locator($"{Shell} [role='separator'][data-side='left']");
        await Assertions.Expect(restored).ToHaveAttributeAsync("aria-valuenow", "264", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        Assert.AreEqual(264, Math.Round(await WidthOfAsync(page, $"{Shell} aside[data-region='left']")), "the stored width is applied on load");
        Assert.IsNotNull(await page.EvaluateAsync<string?>($"() => localStorage.getItem('{StorageKey}')"));
        await CaptureViewportOnlyAsync(page, "1440-resized-restored");
    }
    [TestMethod]
    public async Task Demo_1440_Resizer_MalformedStoredValue_IsIgnored_NeverInjectedIntoTheStyle()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        await context.AddInitScriptAsync($"localStorage.setItem('{StorageKey}', JSON.stringify({{ left: {{ w: '300px; background:red', base: '280px' }}, right: {{ w: 99999, base: '320px' }} }}))");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{Route}");
        await WaitForAppReadyAsync(page);

        var aside = page.Locator($"{Shell} aside[data-region='left']");
        await Assertions.Expect(aside).ToBeVisibleAsync();
        Assert.AreEqual(280, Math.Round(await WidthOfAsync(page, $"{Shell} aside[data-region='left']")));
        Assert.AreEqual(320, Math.Round(await WidthOfAsync(page, $"{Shell} aside[data-region='right']")), "an out-of-range stored width is ignored");
        Assert.IsFalse((await aside.GetAttributeAsync("style"))!.Contains("red"));
    }

    // ── Mobile: canvas stays alive beside the sheet (F1), host-driven snap/tab, Tabs presentation ──

    [TestMethod]
    public async Task Demo_390_Mobile_CanvasIsAliveWhileTheSheetIsOpen_TapScrollAndHostDrivenTab()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);

        await Assertions.Expect(page.Locator($"{Shell} .tm-editor-shell__sheet")).ToBeVisibleAsync();
        Assert.AreEqual("0", await page.Locator($"{Shell} .tm-editor-shell__sheet").GetAttributeAsync("data-snap-index"), "the half snap");

        var inside = await page.EvaluateAsync<bool>(
            """
            () => {
                const stage = document.querySelector("[data-testid='editor-shell'] .tm-editor-shell__stage").getBoundingClientRect();
                const hit = document.elementFromPoint(stage.left + stage.width / 2, stage.top + 40);
                return !!hit && !!hit.closest("[data-testid='editor-shell'] [data-region='canvas']");
            }
            """);
        Assert.IsTrue(inside, "the point 40px below the stage top lands in the canvas, not on a sheet wrapper");

        // Wheel scrolls the canvas (its content overflows the part the sheet leaves free).
        var canvas = page.Locator($"{Shell} .tm-editor-shell__stage [data-region='canvas']");
        var box = (await canvas.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + 40);
        await page.Mouse.WheelAsync(0, 120);
        await page.WaitForTimeoutAsync(250);
        Assert.IsTrue(await canvas.EvaluateAsync<double>("e => e.scrollTop") > 0, "a wheel over the canvas scrolls it");

        // A tap on a canvas block lands (the host then switches the sheet to Properties).
        await canvas.EvaluateAsync("e => e.scrollTop = 0");
        await page.Locator("[data-testid='es-canvas-block']").TapAsync();
        await Assertions.Expect(page.Locator($"{Shell} .tm-editor-shell__sheet .tm-editor-shell__tab").Nth(1)).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(page.Locator($"{Shell} .tm-editor-shell__sheet [role='tabpanel'][data-region='right']")).ToBeVisibleAsync();
        await CaptureViewportOnlyAsync(page, "390-canvas-alive-properties");
    }

    [TestMethod]
    public async Task Demo_390_AutoHeightHost_DoesNotCollapseTheStage()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);

        await page.EvaluateAsync("() => { const h = document.querySelector('.es-shell-host'); h.style.blockSize = 'auto'; h.style.minBlockSize = '0'; }");
        await page.WaitForTimeoutAsync(300);

        var stage = await page.EvaluateAsync<double>("() => document.querySelector(\"[data-testid='editor-shell'] .tm-editor-shell__stage\").getBoundingClientRect().height");
        Assert.IsTrue(stage >= 200, $"an auto-height host must not collapse the container-type:size stage to 0 (was {stage}px)");
    }

    [TestMethod]
    public async Task Demo_390_TabsPresentation_FullRegionTablist_NoSheet()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);

        await page.Locator("[data-testid='es-presentation-toggle']").ClickAsync();

        var shell = page.Locator(Shell);
        await Assertions.Expect(shell.Locator(".tm-editor-shell__tabs--region [role='tab']")).ToHaveCountAsync(3);
        Assert.AreEqual(0, await shell.Locator(".tm-drawer").CountAsync(), "Tabs presentation has no sheet");
        var labels = await shell.Locator(".tm-editor-shell__tabs--region [role='tab']").AllInnerTextsAsync();
        CollectionAssert.AreEqual(new[] { "Blocks", "Canvas", "Properties" }, labels.Select(l => l.Trim()).ToArray());
        await Assertions.Expect(shell.Locator("[role='tab'][aria-selected='true']")).ToHaveTextAsync("Canvas");

        var stripHeight = await shell.Locator(".tm-editor-shell__tabs--region").EvaluateAsync<double>("e => e.getBoundingClientRect().height");
        Assert.IsTrue(stripHeight is > 30 and < 64, $"the Left | Canvas | Right strip is one tab row tall, not a stretched region ({stripHeight}px)");
        await shell.Locator("[role='tab']").Nth(2).ClickAsync();
        await Assertions.Expect(shell.Locator("[role='tabpanel'][data-region='right']")).ToBeVisibleAsync();
        await Assertions.Expect(shell.Locator("[role='tabpanel'][data-region='canvas']")).ToBeHiddenAsync();
        var controls = await shell.Locator("[role='tab'][aria-selected='true']").GetAttributeAsync("aria-controls");
        Assert.IsTrue(await page.EvaluateAsync<bool>("id => !!document.getElementById(id)", controls!), "each tab controls a real tabpanel");
        await CaptureViewportOnlyAsync(page, "390-tabs-properties");
    }

    // ── Focus is never stolen (F6) ──────────────────────────────────────────

    [TestMethod]
    public async Task Demo_390_ProgrammaticClose_DoesNotStealTheFocusTheHostMoved()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);
        await Assertions.Expect(page.Locator($"{Shell} .tm-editor-shell__sheet")).ToBeVisibleAsync();

        // The host button closes the panels programmatically; it keeps the focus it just received.
        var hostButton = page.Locator("[data-testid='es-host-toggle-panels']");
        await hostButton.ScrollIntoViewIfNeededAsync();
        await hostButton.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.Locator($"{Shell} .tm-editor-shell__sheet")).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        await page.WaitForTimeoutAsync(400);
        Assert.AreEqual("es-host-toggle-panels", await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('data-testid') ?? ''"),
            "the shell must not pull focus from the host control to its own toggle");
    }

    [TestMethod]
    public async Task Demo_390_SwipeClose_DoesNotStealFocusFromAnActionBarButton_ButEscapeReturnsItToTheToggle()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);
        var sheet = page.Locator($"{Shell} .tm-editor-shell__sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();

        // Focus an action-bar tile (outside the sheet), then swipe the sheet away from its handle
        // with touch-typed pointer events — what a finger sends. (A MOUSE press on the handle
        // focuses it, which legitimately counts as focus inside the sheet.)
        await page.Locator($"{Shell} .tm-mobile-action-bar__bar [data-action-id='redo']").FocusAsync();
        await sheet.Locator(".tm-sheet__handle").EvaluateAsync(
            """
            async (handle) => {
                const r = handle.getBoundingClientRect();
                const x = r.left + r.width / 2, y = r.top + r.height / 2;
                const fire = (type, dy, t) => handle.dispatchEvent(new PointerEvent(type, { pointerId: 41, pointerType: 'touch', isPrimary: true, button: 0, clientX: x, clientY: y + dy, bubbles: true, cancelable: true, timeStamp: t }));
                const wait = ms => new Promise(res => setTimeout(res, ms));
                fire('pointerdown', 0);
                for (let i = 1; i <= 12; i++) { await wait(16); fire('pointermove', i * 35); }
                await wait(16);
                fire('pointerup', 420);
            }
            """);
        await Assertions.Expect(sheet).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        await page.WaitForTimeoutAsync(400);
        Assert.AreEqual("redo", await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('data-action-id') ?? ''"),
            "a swipe-close must leave focus on the control the user was on");

        // Escape from inside the sheet still returns focus to the toggle (focus was in the sheet).
        await page.Locator($"{Shell} .tm-editor-shell__panel-toggle--mobile").ClickAsync();
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await sheet.Locator(".tm-editor-shell__tab").First.FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(sheet).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        await page.WaitForFunctionAsync("() => document.activeElement?.classList.contains('tm-editor-shell__panel-toggle--mobile')",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });
    }

    // ── F6 r2 G4: focus never drops to <body> when the shell removes/hides the focused element ──

    private const string ActiveIsInSheet = "() => !!document.activeElement?.closest(\"[data-testid='editor-shell'] .tm-editor-shell__sheet\")";

    [TestMethod]
    public async Task Demo_390_ReopenFromTheClosedBar_WithTheKeyboard_FocusMovesToTheSelectedSheetTab()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);
        await page.Locator($"{Shell} .tm-editor-shell__sheet-close").ClickAsync();
        var reopen = page.Locator($"{Shell} .tm-editor-shell__panel-toggle--mobile");
        await Assertions.Expect(reopen).ToBeVisibleAsync();
        await reopen.FocusAsync();

        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.Locator($"{Shell} .tm-editor-shell__sheet")).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => document.activeElement?.getAttribute('role') === 'tab' && document.activeElement.getAttribute('aria-selected') === 'true'",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });
        Assert.IsTrue(await page.EvaluateAsync<bool>(ActiveIsInSheet), "focus is inside the reopened sheet, not on <body>");
    }

    [TestMethod]
    public async Task Demo_390_TabsPresentation_HostSwitchesToProperties_FocusFollowsFromTheCanvasBlock()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);
        await page.Locator("[data-testid='es-presentation-toggle']").ClickAsync();
        var shell = page.Locator(Shell);
        await Assertions.Expect(shell.Locator("[role='tab'][aria-selected='true']")).ToHaveTextAsync("Canvas");

        await shell.Locator("[data-testid='es-canvas-block']").FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(shell.Locator("[role='tab'][aria-selected='true']")).ToHaveTextAsync("Properties");
        await page.WaitForFunctionAsync("() => document.activeElement?.getAttribute('role') === 'tab' && document.activeElement.textContent.trim() === 'Properties'",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task Demo_390_TabsPresentation_HostSwitch_DoesNotStealFocusFromAHostButton()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);
        await page.Locator("[data-testid='es-presentation-toggle']").ClickAsync();
        var shell = page.Locator(Shell);
        await Assertions.Expect(shell.Locator("[role='tab'][aria-selected='true']")).ToHaveTextAsync("Canvas");

        // The presentation toggle is a host button: switching the presentation resets the active panel
        // programmatically while that button holds focus. Focus must stay on it.
        var hostButton = page.Locator("[data-testid='es-presentation-toggle']");
        await hostButton.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(500);

        Assert.AreEqual("es-presentation-toggle", await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('data-testid') ?? ''"),
            "a focus on a host control is never moved by the shell");
    }
    // ── Side panel ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SidePanel_1440_Docked_BelowDesktop_Sheet()
    {
        var context = await CreateTouchContextAsync(1440, 900);
        var page = await GotoEditorShellAsync(context, 1440, 900);
        RegisterContext(context);

        var panel = page.Locator("[data-testid='side-panel'].tm-side-panel--docked");
        await Assertions.Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        Assert.AreEqual(0, await page.Locator("[data-testid='side-panel'] .tm-drawer").CountAsync());
        Assert.AreEqual("ASIDE", await panel.EvaluateAsync<string>("e => e.tagName"));
        Assert.IsFalse(string.IsNullOrEmpty(await panel.GetAttributeAsync("aria-labelledby")), "the docked aside is named by its title");

        // Frame the side-panel section so the docked panel is IN the shot (F15).
        await page.EvaluateAsync("() => document.querySelector(\"[data-testid=\u0027side-panel-section\u0027]\").scrollIntoView({ block: \u0027center\u0027, behavior: \u0027instant\u0027 })");
        await page.WaitForTimeoutAsync(500);
        await page.Mouse.MoveAsync(0, 0);
        var inFrame = await page.EvaluateAsync<bool>("() => { const r = document.querySelector(\"[data-testid=\u0027side-panel\u0027]\").getBoundingClientRect(); return r.top >= 0 && r.bottom <= innerHeight; }");
        Assert.IsTrue(inFrame, "the docked side panel must be fully inside the viewport before the screenshot");
        var shot = Path.Combine(ShotDir, "1440-side-panel-docked.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = shot, FullPage = false });

        // Below desktop the same inspector starts closed (a modal sheet would cover the demo) and
        // opens as a sheet from the demo toggle; the sheet is promoted.
        await page.SetViewportSizeAsync(390, 844);
        await Assertions.Expect(page.Locator(".tm-side-panel-sheet")).ToHaveCountAsync(0);
        await page.Locator("[data-testid='side-panel-toggle']").ScrollIntoViewIfNeededAsync();
        await page.Locator("[data-testid='side-panel-toggle']").ClickAsync();
        var sheet = page.Locator(".tm-side-panel-sheet.tm-drawer--bottom");
        await Assertions.Expect(sheet).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        Assert.AreEqual("manual", await sheet.GetAttributeAsync("popover"));
        Assert.IsFalse(string.IsNullOrEmpty(await sheet.GetAttributeAsync("aria-label")), "the sheet has an accessible name");
        await CaptureViewportOnlyAsync(page, "390-side-panel-sheet");
    }
}
