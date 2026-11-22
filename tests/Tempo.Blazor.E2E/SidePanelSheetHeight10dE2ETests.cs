using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 10d: the BOTTOM-sheet height of <c>TmSidePanel</c> (<c>SheetHeight</c>, <c>SheetSnapPoints</c>,
/// <c>SheetMaxHeight</c>) on a 390 × 844 phone, in the /editor-shell "Edit interval" demo. The headline claim —
/// a form of four fields plus its footer is entirely visible WITHOUT dragging in Content mode — is asserted from
/// real geometry, next to the default half-height sheet that still hides it. Screenshots (light + dark) land in
/// <c>__screenshots__/side-panel-10d</c> for the UX review.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class SidePanelSheetHeight10dE2ETests : WasmTestBase
{
    private static readonly string[] Themes = ["light", "dark"];

    private const string Panel = "[data-testid='interval-panel']";
    private const string FieldTestIds = "interval-day,interval-start,interval-end,interval-temperature";

    private async Task<IPage> OpenAsync(int width, int height, string theme = "light")
    {
        var context = await CreateContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            HasTouch = true,
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/editor-shell");
        await page.WaitForSelectorAsync("[data-testid='interval-open']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 60000,
        });

        // The theme is switched on the wide page (where the toggle is always reachable), THEN the viewport shrinks.
        await SetThemeAsync(page, theme);
        await page.SetViewportSizeAsync(width, height);
        await page.WaitForTimeoutAsync(300);
        return page;
    }

    private static async Task SetThemeAsync(IPage page, string theme)
    {
        // The CURRENT theme is read from the shell's own attribute: not every demo page's toggle carries aria-pressed.
        var current = await page.Locator(".demo-shell").GetAttributeAsync("data-theme");
        if (!string.Equals(current, theme, StringComparison.Ordinal))
        {
            await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        }

        await Assertions.Expect(page.Locator(".demo-shell"))
            .ToHaveAttributeAsync("data-theme", theme, new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.getAnimations().every(a => a.playState !== 'running' || a.effect?.getComputedTiming().iterations === Infinity)",
            null, new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private static async Task SelectModeAsync(IPage page, string label)
    {
        var option = page.Locator("[data-testid='interval-sheet-mode'] [role='radio']", new PageLocatorOptions { HasText = label });
        await option.ScrollIntoViewIfNeededAsync();
        await option.ClickAsync();
        await Assertions.Expect(option).ToHaveAttributeAsync("aria-checked", "true");
    }

    private static async Task OpenSheetAsync(IPage page)
    {
        var open = page.Locator("[data-testid='interval-open']");
        await open.ScrollIntoViewIfNeededAsync();
        await open.ClickAsync();
        await Assertions.Expect(page.Locator(Panel)).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        // The slide-up animation (0.25 s) must settle before the geometry is read.
        await page.WaitForFunctionAsync(
            "sel => document.querySelector(sel)?.getAnimations({ subtree: true }).every(a => a.playState !== 'running')",
            Panel, new PageWaitForFunctionOptions { Timeout = 10000 });
        await page.WaitForTimeoutAsync(150);
    }

    private static async Task CloseSheetAsync(IPage page)
    {
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(Panel)).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10000 });
    }

    private sealed class Geometry
    {
        public double ViewportHeight { get; set; }
        public double PanelTop { get; set; }
        public double PanelHeight { get; set; }
        public double BodyScroll { get; set; }
        public double BodyClient { get; set; }
        public double FooterBottom { get; set; }
        public double FooterTop { get; set; }
        public double[] FieldTops { get; set; } = [];
        public double[] FieldBottoms { get; set; } = [];
        public bool HasSnapAttr { get; set; }
        public string SnapPoints { get; set; } = string.Empty;
    }

    private static async Task<Geometry> MeasureAsync(IPage page)
        => await page.EvaluateAsync<Geometry>(
            """
            ids => {
                const root = document.querySelector("[data-testid='interval-panel']");
                const panel = root.querySelector('.tm-drawer__panel');
                const body = root.querySelector('.tm-drawer__body');
                const footer = root.querySelector('.tm-drawer__footer');
                const fields = ids.split(',').map(id => root.querySelector(`[data-testid='${id}']`).getBoundingClientRect());
                const p = panel.getBoundingClientRect(), f = footer.getBoundingClientRect();
                return {
                    viewportHeight: window.innerHeight,
                    panelTop: p.top, panelHeight: p.height,
                    bodyScroll: body.scrollHeight, bodyClient: body.clientHeight,
                    footerBottom: f.bottom, footerTop: f.top,
                    fieldTops: fields.map(r => r.top), fieldBottoms: fields.map(r => r.bottom),
                    hasSnapAttr: root.hasAttribute('data-snap-points'),
                    snapPoints: root.getAttribute('data-snap-points') ?? '',
                };
            }
            """, FieldTestIds);

    private string ShotPath(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
        var dir = Path.Combine(root, "tests", "Tempo.Blazor.E2E", "__screenshots__", "side-panel-10d");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{name}.png");
    }

    private async Task ShootViewportAsync(IPage page, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(250);
        var path = ShotPath(name);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
        TestContext.AddResultFile(path);
    }

    // ── Content: the headline claim ──────────────────────────────────────────

    [TestMethod]
    public async Task Phone390_ContentHeight_ShowsTheWholeFormAndFooter_WithoutDragging()
    {
        var page = await OpenAsync(390, 844);
        await SelectModeAsync(page, "Fit content");
        await OpenSheetAsync(page);

        var g = await MeasureAsync(page);
        Assert.IsTrue(g.PanelHeight < 0.85 * g.ViewportHeight,
            $"content height stays under the 0.85 cap, was {g.PanelHeight}px of {g.ViewportHeight}px");
        Assert.IsTrue(g.BodyScroll <= g.BodyClient + 1,
            $"the body must not need scrolling (scrollHeight {g.BodyScroll} vs clientHeight {g.BodyClient})");
        foreach (var (top, bottom) in g.FieldTops.Zip(g.FieldBottoms))
        {
            Assert.IsTrue(top >= g.PanelTop && bottom <= g.FooterTop + 1,
                $"every field sits between the sheet top and the footer: {top}–{bottom}, footer top {g.FooterTop}");
        }

        Assert.IsTrue(g.FooterBottom <= g.ViewportHeight + 1, $"the footer is inside the viewport, bottom {g.FooterBottom}");
        Assert.AreEqual(string.Empty, g.SnapPoints, "a content sheet has no snaps");
        Assert.AreEqual(0, await page.Locator($"{Panel} [role='slider']").CountAsync(), "no keyboard slider: there is nothing to snap between");

        await Assertions.Expect(page.Locator($"{Panel} [data-testid='interval-save']")).ToBeInViewportAsync();
        await Assertions.Expect(page.Locator($"{Panel} [data-testid='interval-temperature']")).ToBeInViewportAsync();
    }

    [TestMethod]
    public async Task Phone390_DefaultHeight_StillOpensAtHalf_AndHidesTheFormBelowTheFold()
    {
        var page = await OpenAsync(390, 844);
        await SelectModeAsync(page, "Default (half)");
        await OpenSheetAsync(page);

        var g = await MeasureAsync(page);
        Assert.AreEqual(0.5 * g.ViewportHeight, g.PanelHeight, 2.0, "the default snap is half of the viewport, unchanged");
        Assert.AreEqual("0.5,0.85", g.SnapPoints, "the default snaps are the drawer's, unchanged");
        Assert.IsTrue(g.BodyScroll > g.BodyClient + 1,
            "the four-field form does NOT fit the half-height sheet — the very problem Content mode fixes");
        Assert.AreEqual(1, await page.Locator($"{Panel} [role='slider']").CountAsync(), "the handle stays a slider between snaps");
    }

    [TestMethod]
    public async Task Phone390_Snaps_OpenAtTheLowestSnap_AndTheHandleStepsToTheCap()
    {
        var page = await OpenAsync(390, 844);
        await SelectModeAsync(page, "Snaps 70 % / 100 %");
        await OpenSheetAsync(page);

        var g = await MeasureAsync(page);
        Assert.AreEqual(0.7 * g.ViewportHeight, g.PanelHeight, 2.0, "SheetSnapPoints[0] = 0.7");
        Assert.AreEqual("0.7,0.95", g.SnapPoints, "the 1.0 snap is clamped to SheetMaxHeight = 0.95");
        Assert.IsTrue(g.BodyScroll <= g.BodyClient + 1, "70 % of the phone is enough for the whole form");

        var handle = page.Locator($"{Panel} .tm-sheet__handle");
        await handle.FocusAsync();
        await handle.PressAsync("ArrowUp");
        await Assertions.Expect(page.Locator(Panel)).ToHaveAttributeAsync("data-snap-index", "1");
        await page.WaitForTimeoutAsync(300);
        var raised = await MeasureAsync(page);
        Assert.AreEqual(0.95 * raised.ViewportHeight, raised.PanelHeight, 2.0, "the raised snap is the SheetMaxHeight cap");
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ShortPhone_ContentHeight_IsCappedAndTheBodyScrolls_WhileTheFooterStaysPinned()
    {
        var page = await OpenAsync(390, 520);
        await SelectModeAsync(page, "Fit content");
        await OpenSheetAsync(page);

        var g = await MeasureAsync(page);
        Assert.IsTrue(g.PanelHeight <= 0.85 * g.ViewportHeight + 1,
            $"the cap still wins on a short viewport: {g.PanelHeight}px of {g.ViewportHeight}px");
        Assert.IsTrue(g.BodyScroll > g.BodyClient + 1, "the form is longer than the capped body, so the body scrolls");
        Assert.IsTrue(g.FooterBottom <= g.ViewportHeight + 1 && g.FooterTop >= g.PanelTop,
            $"the footer stays inside the viewport, pinned below the scrolling body (footer {g.FooterTop}–{g.FooterBottom})");
        await Assertions.Expect(page.Locator($"{Panel} [data-testid='interval-save']")).ToBeInViewportAsync();

        // The last field is reachable by scrolling the body, never by dragging the sheet.
        var temperature = page.Locator($"{Panel} [data-testid='interval-temperature']");
        await temperature.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(temperature).ToBeInViewportAsync();
        await Assertions.Expect(page.Locator($"{Panel} [data-testid='interval-save']")).ToBeInViewportAsync();
        await ShootViewportAsync(page, "390x520-content-capped-scrolled-light");
    }

    [TestMethod]
    public async Task Phone390_ContentHeight_SaveClosesTheSheet_AndReturnsFocusToTheOpener()
    {
        var page = await OpenAsync(390, 844);
        await SelectModeAsync(page, "Fit content");
        await OpenSheetAsync(page);

        await page.Locator($"{Panel} [data-testid='interval-save']").ClickAsync();
        await Assertions.Expect(page.Locator(Panel)).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10000 });
        await Assertions.Expect(page.Locator("[data-testid='interval-saved']")).ToContainTextAsync("06:00–08:30, 21 °C");
        await Assertions.Expect(page.Locator("[data-testid='interval-open']")).ToBeFocusedAsync(
            new LocatorAssertionsToBeFocusedOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task Phone390_ContentHeight_BackdropAndEscapeStillDismiss()
    {
        var page = await OpenAsync(390, 844);
        await SelectModeAsync(page, "Fit content");

        await OpenSheetAsync(page);
        await page.Locator($"{Panel} .tm-drawer__overlay").ClickAsync(new LocatorClickOptions { Position = new Position { X = 195, Y = 40 } });
        await Assertions.Expect(page.Locator(Panel)).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10000 });

        await OpenSheetAsync(page);
        await CloseSheetAsync(page);
    }

    [TestMethod]
    public async Task Desktop1440_TheSameSheet_IsASideSheet_WithoutAHeightSetting()
    {
        var page = await OpenAsync(1440, 900);
        await Assertions.Expect(page.Locator("[data-testid='interval-phone-only']")).ToBeVisibleAsync();
        Assert.AreEqual("true", await page.Locator("[data-testid='interval-sheet-mode'] [role='radio']").First
            .GetAttributeAsync("aria-disabled") ?? "true",
            "the height setting has no meaning for a side sheet, so the control is disabled");

        await OpenSheetAsync(page);
        var root = page.Locator(Panel);
        await Assertions.Expect(root).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"(^|\s)tm-drawer--right(\s|$)"));
        Assert.AreEqual(0, await root.Locator(".tm-sheet__handle").CountAsync(), "a side sheet has no grabber");
        Assert.IsFalse(await root.EvaluateAsync<bool>("el => el.classList.contains('tm-sheet--content')"),
            "the content-height class never reaches a side sheet");
    }

    // ── Screenshots (light + dark) ───────────────────────────────────────────

    [TestMethod]
    public async Task Screenshots_Phone390_ContentHeight_LightAndDark()
    {
        foreach (var theme in Themes)
        {
            var page = await OpenAsync(390, 844, theme);
            await SelectModeAsync(page, "Fit content");
            await OpenSheetAsync(page);
            await ShootViewportAsync(page, $"390-content-{theme}");
            await CloseSheetAsync(page);
        }
    }

    [TestMethod]
    public async Task Screenshots_Phone390_DefaultHalf_LightAndDark()
    {
        foreach (var theme in Themes)
        {
            var page = await OpenAsync(390, 844, theme);
            await SelectModeAsync(page, "Default (half)");
            await OpenSheetAsync(page);
            await ShootViewportAsync(page, $"390-default-half-{theme}");
            await CloseSheetAsync(page);
        }
    }

    [TestMethod]
    public async Task Screenshots_Phone390_Snaps_LightAndDark()
    {
        foreach (var theme in Themes)
        {
            var page = await OpenAsync(390, 844, theme);
            await SelectModeAsync(page, "Snaps 70 % / 100 %");
            await OpenSheetAsync(page);
            await ShootViewportAsync(page, $"390-snaps-{theme}");
            await CloseSheetAsync(page);
        }
    }

    [TestMethod]
    public async Task Screenshots_ShortPhone390x520_ContentCapped_Dark()
    {
        var page = await OpenAsync(390, 520, "dark");
        await SelectModeAsync(page, "Fit content");
        await OpenSheetAsync(page);
        await ShootViewportAsync(page, "390x520-content-capped-dark");
    }
}
