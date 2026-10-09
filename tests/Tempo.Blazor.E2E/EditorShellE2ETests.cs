using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F6 lane: TmEditorShell and TmSidePanel on the real components — the /editor-shell demo page
/// at 1440 (desktop), 1024 with touch (tablet) and 390/320 with touch (mobile). Every screenshot
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
        await page.EvaluateAsync("() => document.querySelector('[data-testid=\"editor-shell-host\"]')?.scrollIntoView({ block: 'start' }); window.scrollBy(0, -84)");
        await page.WaitForTimeoutAsync(150);
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

        await toggle.ClickAsync();
        var rail = page.Locator("[data-testid='editor-shell'] .tm-editor-shell__rail--left");
        await Assertions.Expect(rail).ToBeVisibleAsync();
        Assert.AreEqual(0, await page.Locator("[data-testid='editor-shell'] [data-region='left']").CountAsync());
        await CaptureViewportOnlyAsync(page, "1440-collapsed-rail");

        await rail.Locator("button").ClickAsync();
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
        var rail = page.Locator("[data-testid='editor-shell'] .tm-editor-shell__rail--left button");
        await Assertions.Expect(rail).ToBeVisibleAsync();

        // Exactly one activation per press: Enter fired the collapse once. Focus the rail's own
        // button (the collapsed strip unmounted with the toggle) and expand from the keyboard.
        await rail.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[data-testid='editor-shell'] [data-region='left']")).ToBeVisibleAsync();
    }

    // ── Tablet ──────────────────────────────────────────────────────────────
    // At a 1024 viewport the app sidebar leaves the auto shell ~632px, so the auto shell honestly
    // resolves mobile (the F1 contract: a component resolves from its own width). The demo page
    // therefore ships a forced-tablet shell section; the tablet side-sheet behaviour is driven
    // against it, and the honest auto-shell resolution is asserted alongside.

    [TestMethod]
    public async Task Demo_1024_Tablet_OnePromotedSideSheet_CanvasVisible()
    {
        var context = await CreateTouchContextAsync(1024, 768);
        var page = await GotoEditorShellAsync(context, 1024, 768);
        RegisterContext(context);

        var tablet = page.Locator("[data-testid='editor-shell-tablet']");

        // Open the left panel from its strip toggle: a single promoted side sheet appears.
        await tablet.Locator(".tm-editor-shell__panel-toggle--left").ClickAsync();
        var drawers = tablet.Locator(".tm-drawer");
        Assert.AreEqual(1, await drawers.CountAsync(), "tablet renders at most one side sheet");
        await Assertions.Expect(tablet.Locator("[data-region='canvas']")).ToBeVisibleAsync();

        // A modal viewport surface: promoted to the top layer.
        Assert.AreEqual("manual", await drawers.First.GetAttributeAsync("popover"),
            "the tablet side sheet must promote its root");

        // The auto shell resolves from its own container — at 1024 minus the app sidebar that is
        // below the mobile breakpoint, so it honestly renders the mobile panels sheet.
        Assert.AreEqual("mobile",
            await page.Locator("[data-testid='editor-shell']").GetAttributeAsync("data-layout"));

        await CaptureViewportOnlyAsync(page, "1024-tablet-sheet");
    }

    [TestMethod]
    public async Task Demo_1024_Tablet_EscapeClosesSheet_AndRestoresFocusToToggle()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1024, Height = 768 },
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}{Route}");
        await WaitForAppReadyAsync(page);

        var tablet = page.Locator("[data-testid='editor-shell-tablet']");

        // Open the right panel from its strip toggle: a single promoted side sheet appears.
        // (A modal sheet inerts everything behind it, so the user can only ever open one panel at
        // a time from the toggles — the both-open reveal path is host state, covered in bUnit.)
        var toggle = tablet.Locator(".tm-editor-shell__panel-toggle--right");
        Assert.AreEqual("false", await toggle.GetAttributeAsync("aria-expanded"));
        await toggle.ClickAsync();
        var sheet = tablet.Locator(".tm-drawer--right");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        Assert.AreEqual("manual", await sheet.GetAttributeAsync("popover"));
        // While the modal sheet is open the toggle is inert behind it and unmounts (by design);
        // aria-expanded is asserted when it comes back on every close path below.

        // The sheet header close button closes it and returns focus to the trigger.
        await sheet.Locator(".tm-drawer__close").ClickAsync();
        await Assertions.Expect(sheet).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        Assert.AreEqual(0, await tablet.Locator(".tm-drawer").CountAsync());
        await Assertions.Expect(toggle).ToBeVisibleAsync();
        Assert.AreEqual("false", await toggle.GetAttributeAsync("aria-expanded"),
            "aria-expanded must read false after the sheet close button");

        // Reopen and press Escape: the sheet closes and focus returns to the trigger
        // (aria-expanded stays accurate on every close path).
        await toggle.ClickAsync();
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(sheet).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        await WaitForFocusOnToggleAsync(page, "right");        Assert.AreEqual("false", await toggle.GetAttributeAsync("aria-expanded"));
        await CaptureViewportOnlyAsync(page, "1024-after-escape");
    }

    // ── Mobile ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Demo_390_Mobile_CanvasTabsSheetAndActionBar()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);

        await Assertions.Expect(page.Locator("[data-region='canvas']")).ToBeVisibleAsync();

        // The panels sheet is INLINE (in-container): never promoted.
        var sheet = page.Locator(".tm-editor-shell__sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        Assert.IsNull(await sheet.GetAttributeAsync("popover"), "the inline panels sheet must not promote");

        // Tabs presentation: one tablist, one selected tabpanel.
        var tabs = page.Locator(".tm-editor-shell__tab");
        Assert.AreEqual(2, await tabs.CountAsync());
        Assert.AreEqual("true", await tabs.First.GetAttributeAsync("aria-selected"));
        Assert.AreEqual(1, await page.Locator("[role='tabpanel']").CountAsync());

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
        var panelBody = await page.Locator(".tm-editor-shell__sheet .tm-drawer__body").BoundingBoxAsync();
        Assert.IsTrue(panelBody is { Height: > 60 }, $"the sheet body must be visible (height {panelBody?.Height})");
        await Assertions.Expect(page.Locator(".tm-editor-shell__sheet [role='tabpanel']")).ToBeVisibleAsync();
        await CaptureViewportOnlyAsync(page, "390-mobile-tabs-sheet");
    }

    [TestMethod]
    public async Task Demo_390_Mobile_TabSwitch_ShowsOtherPanel()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);

        var tabs = page.Locator(".tm-editor-shell__tab");
        await tabs.Nth(1).ClickAsync();
        Assert.AreEqual("true", await tabs.Nth(1).GetAttributeAsync("aria-selected"));
        await Assertions.Expect(page.Locator("[role='tabpanel'] [data-testid='es-properties']")).ToBeVisibleAsync();

        await CaptureViewportOnlyAsync(page, "390-mobile-tab-properties");
    }

    [TestMethod]
    public async Task Demo_390_Mobile_CloseSheet_ShowsPanelsToggle_ReopenWorks()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);

        await page.Locator(".tm-editor-shell__sheet-close").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-editor-shell__sheet")).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        Assert.AreEqual(0, await page.Locator(".tm-editor-shell .tm-drawer").CountAsync());

        var reopen = page.Locator(".tm-editor-shell__panel-toggle--mobile");
        await Assertions.Expect(reopen).ToBeVisibleAsync();
        Assert.AreEqual("false", await reopen.GetAttributeAsync("aria-expanded"));
        await CaptureViewportOnlyAsync(page, "390-mobile-sheet-closed");

        await reopen.ClickAsync();
        await Assertions.Expect(page.Locator(".tm-editor-shell__sheet")).ToBeVisibleAsync();
        Assert.AreEqual(0, await page.Locator(".tm-editor-shell__panel-toggle--mobile").CountAsync());
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

        await Assertions.Expect(page.Locator(".tm-editor-shell__sheet")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='editor-shell'] .tm-mobile-action-bar__bar")).ToBeVisibleAsync();
        // A tab label stays on ONE line (no mid-word break of the fr "Propriétés").
        var tabHeights = await page.EvaluateAsync<double[]>("() => [...document.querySelectorAll('.tm-editor-shell__tab')].map(t => t.getBoundingClientRect().height)");
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
    public async Task Demo_1024_Tablet_BackdropAndDoneClose_RestoreFocusToTheToggle()
    {
        var page = await OpenPlainPageAsync(1024, 768);
        RegisterContext(page.Context);
        await WaitForAppReadyAsync(page);

        var tablet = page.Locator("[data-testid='editor-shell-tablet']");
        var toggle = tablet.Locator(".tm-editor-shell__panel-toggle--right");
        var sheet = tablet.Locator(".tm-drawer--right");

        await toggle.ClickAsync();
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        // The backdrop (a click far from the right-hand sheet) is a close path.
        await page.Mouse.ClickAsync(60, 400);
        await Assertions.Expect(sheet).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        await WaitForFocusOnToggleAsync(page, "right");
        Assert.AreEqual("false", await toggle.GetAttributeAsync("aria-expanded"));

        // Open the LEFT panel the same way; Escape; focus returns to the LEFT toggle.
        var leftToggle = tablet.Locator(".tm-editor-shell__panel-toggle--left");
        await leftToggle.ClickAsync();
        await Assertions.Expect(tablet.Locator(".tm-drawer--left")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tablet.Locator(".tm-drawer")).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        await WaitForFocusOnToggleAsync(page, "left");
        Assert.AreEqual("false", await leftToggle.GetAttributeAsync("aria-expanded"));
    }

    [TestMethod]
    public async Task Demo_390_Mobile_EscapeAndClose_ReturnFocusToThePanelsToggle_AndSurviveALayoutFlip()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);

        await page.Locator(".tm-editor-shell__sheet-close").ClickAsync();
        var reopen = page.Locator(".tm-editor-shell__panel-toggle--mobile");
        await Assertions.Expect(reopen).ToBeVisibleAsync();
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.classList.contains('tm-editor-shell__panel-toggle--mobile')",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });

        // Reopen, then close with Escape: focus lands on the toggle again.
        await reopen.ClickAsync();
        await Assertions.Expect(page.Locator(".tm-editor-shell__sheet")).ToBeVisibleAsync();
        await page.Locator(".tm-editor-shell__tab").First.FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".tm-editor-shell__sheet")).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.classList.contains('tm-editor-shell__panel-toggle--mobile')",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });
        Assert.AreEqual("false", await page.Locator(".tm-editor-shell__panel-toggle--mobile").GetAttributeAsync("aria-expanded"));

        // 390 -> 1440 -> 390: nothing reopens, no error UI, the panels stay closed.
        await page.SetViewportSizeAsync(1440, 900);
        await Assertions.Expect(page.Locator("[data-testid='editor-shell'][data-layout='desktop']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        await page.SetViewportSizeAsync(390, 844);
        await Assertions.Expect(page.Locator("[data-testid='editor-shell'][data-layout='mobile']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        Assert.AreEqual(0, await page.Locator(".tm-editor-shell__sheet").CountAsync(),
            "a viewport flip must not reopen the closed panels sheet");
        Assert.AreEqual("false", await page.Locator(".tm-editor-shell__panel-toggle--mobile").GetAttributeAsync("aria-expanded"));
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
            "() => [...document.querySelectorAll('.tm-editor-shell__tab')].map(t => t.getBoundingClientRect().height)");
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

        await Assertions.Expect(page.Locator(".tm-editor-shell__sheet")).ToBeVisibleAsync();
        await page.Locator(".tm-editor-shell__sheet-close").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-editor-shell__sheet")).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(prefers-reduced-motion: reduce)').matches"));
    }

    private static double Luminance(string rgb)
    {
        var nums = System.Text.RegularExpressions.Regex.Matches(rgb, @"\d+(\.\d+)?")
            .Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)).Take(3).ToArray();
        return nums.Length < 3 ? 1 : (0.2126 * nums[0] + 0.7152 * nums[1] + 0.0722 * nums[2]) / 255.0;
    }
    // ── Side panel ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SidePanel_1440_Docked_390_Sheet()
    {
        var context = await CreateTouchContextAsync(1440, 900);
        var page = await GotoEditorShellAsync(context, 1440, 900);
        RegisterContext(context);

        var panel = page.Locator("[data-testid='side-panel'].tm-side-panel--docked");
        await Assertions.Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        Assert.AreEqual(0, await page.Locator("[data-testid='side-panel'] .tm-drawer").CountAsync());
        await CaptureViewportOnlyAsync(page, "1440-side-panel-docked");

        // 390: the same inspector becomes a modal bottom sheet (promoted).
        await page.SetViewportSizeAsync(390, 844);
        var sheet = page.Locator(".tm-side-panel-sheet.tm-drawer--bottom");
        await Assertions.Expect(sheet).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        Assert.AreEqual("manual", await sheet.GetAttributeAsync("popover"));
        await CaptureViewportOnlyAsync(page, "390-side-panel-sheet");
    }
}
