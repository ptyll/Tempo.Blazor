using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F3 review round 2 fixes, verified against the real demo components:
/// U1 (BLOCKER) every modal viewport-anchored overlay root is promoted to the browser top layer
/// at activation, so the top-layer order equals the open order — a TmDialog opened from inside
/// the promoted bottom sheet painted INVISIBLE under it (arch probe4). Covered: the nested
/// dialog at 390 and 1440, a nested sheet from a promoted modal sheet, a toast pushed while a
/// sheet is open, and a light popover opened after a promoted modal.
/// U2 the promoted root resets every UA [popover] default (background, color, overflow), in
/// light and dark mode — the page behind a sheet stays dimmed, not a flat grey wall, and dark
/// body text is the themed colour, not CanvasText black.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class OverlayRound2FixE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "f3-round2");

    private async Task<IPage> OpenPageAsync(string route, int width, int height = 844, bool touch = false, string culture = "en")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = touch,
            IsMobile = touch,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            Locale = culture + "-US",
        });
        await context.AddInitScriptAsync(
            $"""
            localStorage.setItem('tm-demo-culture', '{culture}');
            document.cookie = 'tm-demo-culture={culture};path=/;max-age=31536000;samesite=lax';
            """);
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static async Task<string> ShootAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false, Timeout = 60_000 });
        return path;
    }

    /// <summary>
    /// The probe4 paint assertion: what PAINTS at the hit element's point is the top surface, not
    /// merely "the surface exists and is focusable". elementFromPoint skips inert elements, and
    /// the focus trap inerts everything behind the top overlay — so without stripping inert the
    /// test would pass even when paint order is wrong. Inert is restored synchronously after the
    /// hit-test.
    /// </summary>
    private static async Task<bool> PaintsAtAsync(IPage page, string hitSelector, string topSelector)
    {
        return await page.EvaluateAsync<bool>(
            """
            ({ hit, top }) => {
                const hitEl = document.querySelector(hit);
                const topEl = document.querySelector(top);
                if (!hitEl || !topEl) return false;
                const wasInert = [...document.querySelectorAll('[inert]')];
                wasInert.forEach(e => e.removeAttribute('inert'));
                const r = hitEl.getBoundingClientRect();
                const el = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                wasInert.forEach(e => e.setAttribute('inert', ''));
                return !!el && topEl.contains(el);
            }
            """,
            new { hit = hitSelector, top = topSelector });
    }

    private static async Task AssertCoarsePointerStillMatchesAsync(IPage page)
    {
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"),
            "a viewport screenshot must not reset touch emulation — (pointer: coarse) still expected");
    }

    // ── U1: top-layer order equals open order ─────────────────────────────────

    [TestMethod]
    [DataRow(390, 844, true)]
    [DataRow(1440, 900, false)]
    public async Task NestedDialog_FromBottomSheet_PaintsAboveTheSheet(int width, int height, bool touch)
    {
        var page = await OpenPageAsync("/feedback", width, height, touch);

        await page.Locator("[data-testid='open-bottom-sheet']").ClickAsync();
        var sheet = page.Locator(".tm-drawer--bottom");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(600);

        await page.Locator("[data-testid='sheet-open-dialog']").ClickAsync();
        var dialog = page.Locator(".tm-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForTimeoutAsync(800);

        // The dialog's overlay root is itself top-layer (review finding U1): the dialog a host
        // opens from inside a promoted sheet must not paint under it.
        Assert.IsTrue(await page.Locator(".tm-modal-overlay").EvaluateAsync<bool>("el => el.matches(':popover-open')"),
            "the nested dialog's overlay root must be a live top-layer popover");

        Assert.IsTrue(await PaintsAtAsync(page, ".tm-dialog-btn-ok", ".tm-modal-overlay"),
            "the dialog's OK button must PAINT above the sheet — the round-2 blocker painted invisible");

        await ShootAsync(page, $"u1-{width}-nested-dialog-above-sheet");
        if (touch) await AssertCoarsePointerStillMatchesAsync(page);

        // Escape order follows open order: the dialog first, the sheet stays.
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(600);
        Assert.AreEqual(0, await page.Locator(".tm-dialog").CountAsync(), "Escape closes the nested dialog first");
        Assert.AreEqual(1, await page.Locator(".tm-drawer--bottom").CountAsync(), "the sheet survives the dialog's Escape");
    }

    [TestMethod]
    public async Task DropdownSheet_ItemHandler_OpensTopLayerModal()
    {
        var page = await OpenPageAsync("/overlay", 390, 844, touch: true);

        var trigger = page.Locator("[data-testid='overlay-menu-dialog-dropdown'] .tm-dropdown-trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.TapAsync();
        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForTimeoutAsync(500);

        // The sheet item runs an async handler (a simulated 1.2 s round-trip); the menu closes
        // BEFORE it runs (N319), then the modal opens.
        await sheet.Locator(".tm-dropdown-item", new LocatorLocatorOptions { HasText = "Delete item" }).TapAsync();
        var modal = page.Locator("[data-testid='overlay-menu-dialog']");
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForTimeoutAsync(800);

        Assert.IsTrue(await page.Locator(".tm-modal-overlay").EvaluateAsync<bool>("el => el.matches(':popover-open')"),
            "the TmModal a dropdown item opens must itself be top-layer (U1)");
        Assert.IsTrue(await PaintsAtAsync(page, "[data-testid='overlay-menu-dialog'] .tm-focus-scope", ".tm-modal-overlay"),
            "the modal scope must paint");

        await ShootAsync(page, "u1-390-dropdown-item-modal-toplayer");
        await AssertCoarsePointerStillMatchesAsync(page);
    }

    [TestMethod]
    public async Task NestedSheet_FromPromotedModal_PaintsAboveTheModal()
    {
        var page = await OpenPageAsync("/overlay", 390, 844, touch: true);

        await page.Locator("[data-testid='overlay-open-modal']").TapAsync();
        var modal = page.Locator(".tm-modal-overlay");
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(600);
        Assert.IsTrue(await modal.EvaluateAsync<bool>("el => el.matches(':popover-open')"),
            "the modal sheet's overlay root is top-layer");

        // A non-modal surface opened AFTER the modal still stacks above it: the date-picker panel
        // promotes later, so its showPopover lands it on top.
        var trigger = page.Locator("[data-testid='overlay-datepicker'] .tm-date-picker-trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.TapAsync();
        await page.WaitForTimeoutAsync(900);

        Assert.AreEqual(1, await page.Locator(".tm-overlay-panel-sheet").CountAsync(),
            "the date picker presents as a sheet at 390");
        Assert.IsTrue(await PaintsAtAsync(page, ".tm-overlay-panel-sheet .tm-drawer__panel", ".tm-overlay-panel-sheet"),
            "the nested picker sheet must paint above the promoted modal sheet");

        await ShootAsync(page, "u1-390-nested-sheet-above-modal");
        await AssertCoarsePointerStillMatchesAsync(page);

        // Escape peels the picker sheet first; the modal survives.
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(600);
        Assert.AreEqual(0, await page.Locator(".tm-overlay-panel-sheet").CountAsync());
        Assert.AreEqual(1, await page.Locator(".tm-modal-overlay").CountAsync());
    }

    [TestMethod]
    public async Task PopoverOpenedAfterPromotedModal_PaintsAboveIt()
    {
        var page = await OpenPageAsync("/overlay", 1440, 900);

        await page.Locator("[data-testid='overlay-open-modal']").ClickAsync();
        var modal = page.Locator(".tm-modal-overlay");
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(600);
        Assert.IsTrue(await modal.EvaluateAsync<bool>("el => el.matches(':popover-open')"),
            "the promoted dialog-presentation modal is top-layer");

        await page.Locator("[data-testid='overlay-open-in-modal']").ClickAsync();
        var panel = page.Locator("[data-testid='overlay-panel-modal']");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(500);

        Assert.IsTrue(await panel.EvaluateAsync<bool>("el => el.matches(':popover-open')"),
            "the light popover promotes through overlay.js");
        Assert.IsTrue(await PaintsAtAsync(page, "[data-testid='overlay-panel-modal']", "[data-testid='overlay-panel-modal']"),
            "a popover opened after the modal must paint above it (later showPopover wins)");

        await ShootAsync(page, "u1-1440-popover-above-promoted-modal");
    }

    [TestMethod]
    public async Task Toast_PushedWhileSheetOpen_PaintsAboveTheSheet()
    {
        var page = await OpenPageAsync("/feedback", 390, 844, touch: true);

        await page.Locator("[data-testid='open-bottom-sheet']").TapAsync();
        var sheet = page.Locator(".tm-drawer--bottom");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(600);

        await page.Locator("[data-testid='sheet-push-toast']").TapAsync();
        // The demo renders two containers (the app chrome one and the page showcase) sharing the
        // scoped service and both promote — the assertion targets the first of them, which is
        // enough to prove the re-raise above the sheet.
        var toastContainer = page.Locator(".tm-toast-container:popover-open").First;
        await toastContainer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        var toast = toastContainer.Locator(".tm-toast").First;
        await toast.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(600);

        Assert.IsTrue(await toastContainer.EvaluateAsync<bool>("el => el.matches(':popover-open')"),
            "the toast container re-raises itself to the top layer on each push");
        Assert.IsTrue(await PaintsAtAsync(page, ".tm-toast-container:popover-open .tm-toast-message", ".tm-toast-container:popover-open"),
            "a toast pushed while a sheet is open must paint above the sheet");

        await ShootAsync(page, "u1-390-toast-above-sheet");
        await AssertCoarsePointerStillMatchesAsync(page);
    }

    // ── U2: the promoted root resets the UA popover paint ─────────────────────

    [TestMethod]
    public async Task SheetRoots_ResetUaPopoverDefaults()
    {
        var page = await OpenPageAsync("/overlay", 390, 844, touch: true);

        var trigger = page.Locator("[data-testid='overlay-mobile-dropdown'] .tm-dropdown-trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.TapAsync();
        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForTimeoutAsync(600);

        // The promoted root must not keep the UA [popover] paint: Canvas background turned the
        // page behind every sheet into a flat grey wall (R2-B1).
        var rootStyle = await page.EvaluateAsync<string>(
            """
            () => {
                const r = document.querySelector('.tm-overlay-panel-sheet');
                const cs = getComputedStyle(r);
                return [cs.backgroundColor, cs.overflow, cs.maxWidth, cs.maxHeight, cs.color].join('|');
            }
            """);
        var parts = rootStyle.Split('|');
        Assert.AreEqual("rgba(0, 0, 0, 0)", parts[0], "the sheet root background must be transparent, not UA Canvas");
        Assert.AreEqual("visible", parts[1], "the sheet root overflow must be visible, not UA auto");
        Assert.AreEqual("none", parts[2], "the sheet root max-width must be none");
        Assert.AreEqual("none", parts[3], "the sheet root max-height must be none");

        // The dimmed page must show through: the sheet's own backdrop carries the scrim, the root does not.
        var backdrop = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.tm-overlay-panel-sheet .tm-drawer__overlay')).backgroundColor");
        StringAssert.Contains(backdrop, "0.5", "the scrim lives on the drawer overlay, not on the promoted root");

        await ShootAsync(page, "u2-390-sheet-root-transparent");
        await AssertCoarsePointerStillMatchesAsync(page);
    }

    [TestMethod]
    public async Task SheetBodyText_DarkMode_UsesThemedColor()
    {
        var page = await OpenPageAsync("/overlay", 390, 844, touch: true);

        // The demo renders the toggle in both the mobile header and the desktop aside — the FIRST
        // visible one flips the shared ThemeService either way.
        await page.Locator("[data-testid='theme-toggle']").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var trigger = page.Locator("[data-testid='overlay-mobile-popover'] .tm-popover__trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.TapAsync();
        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForTimeoutAsync(600);

        var color = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.tm-overlay-panel-sheet__content p')).color");
        Assert.AreNotEqual("rgb(0, 0, 0)", color,
            "a plain paragraph in the sheet body must not inherit the UA CanvasText black (measured 1.6:1 on the slate panel)");
        Assert.AreEqual("rgb(241, 245, 249)", color,
            "the themed shell colour (text-slate-100) inherits into the sheet body in dark mode");

        await ShootAsync(page, "u2-390-sheet-dark-themed-text");
        await AssertCoarsePointerStillMatchesAsync(page);
    }

    [TestMethod]
    public async Task ModalOverlayRoot_ResetUaPopoverDefaults()
    {
        var page = await OpenPageAsync("/feedback", 1440, 900);

        await page.Locator("[data-testid='open-bottom-sheet']").ClickAsync();
        await page.Locator(".tm-drawer--bottom").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(600);

        await page.Locator("[data-testid='sheet-open-dialog']").ClickAsync();
        await page.Locator(".tm-dialog").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForTimeoutAsync(800);

        // The promoted modal overlay still paints its own dimming scrim (its author rule wins over
        // the zero-specificity reset) — the page behind stays dimmed, not opaque.
        var background = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.tm-modal-overlay')).backgroundColor");
        StringAssert.Contains(background, "0.5",
            "the modal overlay keeps its rgba(0,0,0,0.5) scrim above the sheet's — the nested dialog double-dims");
    }
}
