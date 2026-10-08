using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F3 review round 3 fixes, verified against the real demo components:
/// V1 (UX R3-M1) a toast that is ALREADY on screen must stay above every modal surface — the
/// toast container is pinned on top and re-raises after every later promotion; a push at the
/// MaxVisible cap (visible count unchanged) re-raises too, and the re-raise never steals focus.
/// V2 (architect) the remaining modal viewport-anchored roots promote themselves: a
/// TmCommandPalette opened from inside a promoted drawer paints above it.
/// V3 a click on a toast's dismiss button while a sheet is open dismisses the toast — it no
/// longer passes through the inert container to the backdrop and closes the sheet.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class OverlayRound3FixE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "f3-round3");

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
    /// The probe4 paint assertion: what PAINTS at the hit element's point is the top surface. Inert
    /// is stripped synchronously around the hit-test and restored after (the focus trap inerts
    /// everything behind the top overlay, and elementFromPoint would skip it).
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

    // ── V1: a toast already on screen stays above every modal surface ──────────

    [TestMethod]
    [DataRow(390, 844, true)]
    [DataRow(1440, 900, false)]
    public async Task ToastAlreadyVisible_ThenModalSurfaceOpens_ToastPaintsOnTop(int width, int height, bool touch)
    {
        var page = await OpenPageAsync("/feedback", width, height, touch);

        // Push a long toast, THEN open the modal surface — the order that regressed in R3-M1.
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Long (10s)" }).ClickAsync();
        var toast = page.Locator(".tm-toast-container:popover-open .tm-toast").First;
        await toast.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(500);

        if (touch)
        {
            await page.Locator("[data-testid='open-bottom-sheet']").TapAsync();
            await page.Locator(".tm-drawer--bottom").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        }
        else
        {
            await page.Locator("[data-testid='open-right-drawer']").ClickAsync();
            await page.Locator(".tm-drawer--right").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        }
        await page.WaitForTimeoutAsync(700);

        // The drawer's promotion must have re-raised the pinned toast container above itself.
        Assert.IsTrue(await PaintsAtAsync(page, ".tm-toast-container:popover-open .tm-toast", ".tm-toast-container:popover-open"),
            "a toast already on screen must paint above the drawer/sheet opened after it (R3-M1)");

        await ShootAsync(page, $"v1-{width}-toast-then-{(touch ? "sheet" : "drawer")}");
        if (touch) await AssertCoarsePointerStillMatchesAsync(page);
    }

    [TestMethod]
    public async Task ToastPushedAtMaxVisibleCap_FromOpenSheet_NewestPaintsOnTop()
    {
        var page = await OpenPageAsync("/feedback", 1440, 900);

        // Fill the container to its MaxVisible=5 cap before the sheet opens.
        var push = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Long (10s)" });
        for (var i = 0; i < 5; i++)
        {
            await push.ClickAsync();
            await page.WaitForTimeoutAsync(150);
        }
        Assert.AreEqual(5, await page.Locator(".tm-toast-container:popover-open .tm-toast").CountAsync(),
            "the demo container caps at five visible toasts");

        await page.Locator("[data-testid='open-bottom-sheet']").ClickAsync();
        await page.Locator(".tm-drawer--bottom").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(600);

        // Push from inside the sheet: the visible count stays 5 (the oldest rotated out), which is
        // exactly the case that never re-raised before the newest-id tracking.
        await page.Locator("[data-testid='sheet-push-toast']").ClickAsync();
        var newest = page.Locator(".tm-toast-container:popover-open .tm-toast-message", new PageLocatorOptions { HasText = "Saved from the sheet" });
        await newest.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(600);

        Assert.IsTrue(await PaintsAtAsync(page, ".tm-toast-container:popover-open .tm-toast-message", ".tm-toast-container:popover-open"),
            "a toast pushed at the MaxVisible cap must paint above the open sheet");

        await ShootAsync(page, "v1-1440-toast-at-cap-above-sheet");
    }

    [TestMethod]
    public async Task ToastReRaise_DoesNotStealFocusFromTheOpenSheet()
    {
        var page = await OpenPageAsync("/feedback", 1440, 900);

        await page.Locator("[data-testid='open-bottom-sheet']").ClickAsync();
        await page.Locator(".tm-drawer--bottom").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(600);

        // Focus something inside the sheet, then push a toast: the raise must not move focus.
        await page.Locator("[data-testid='sheet-push-toast']").FocusAsync();
        await page.Locator("[data-testid='sheet-push-toast']").ClickAsync();
        await page.WaitForTimeoutAsync(700);

        var activeIsInSheet = await page.EvaluateAsync<bool>(
            "() => !!document.activeElement?.closest('.tm-drawer--bottom')");
        Assert.IsTrue(activeIsInSheet,
            "re-raising the pinned toast container must not steal focus from the sheet (focus stayed: " +
            await page.EvaluateAsync<string>("() => (document.activeElement?.className || document.activeElement?.tagName || '').toString()") + ")");
    }

    // ── V2: the palette promotes above a drawer opened before it ───────────────

    [TestMethod]
    public async Task CommandPalette_OpenedFromDrawer_PaintsAboveTheDrawer()
    {
        var page = await OpenPageAsync("/feedback", 1440, 900);

        await page.Locator("[data-testid='open-right-drawer']").ClickAsync();
        var drawer = page.Locator(".tm-drawer--right");
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(600);

        await page.Locator("[data-testid='drawer-open-palette']").ClickAsync();
        var palette = page.Locator(".tm-command-palette-backdrop");
        await palette.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(700);

        Assert.IsTrue(await palette.EvaluateAsync<bool>("el => el.matches(':popover-open')"),
            "the palette backdrop must be a live top-layer popover (V2)");
        Assert.IsTrue(await PaintsAtAsync(page, ".tm-command-palette", ".tm-command-palette-backdrop"),
            "the palette opened from inside the drawer must paint above it");

        await ShootAsync(page, "v2-1440-palette-above-drawer");

        // Escape peels the palette first; the drawer survives.
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(600);
        Assert.AreEqual(0, await page.Locator(".tm-command-palette-backdrop").CountAsync(),
            "Escape closes the palette first");
        Assert.AreEqual(1, await page.Locator(".tm-drawer--right").CountAsync(),
            "the drawer survives the palette's Escape");
    }

    // ── V3: a toast's dismiss button works while a sheet is open ───────────────

    [TestMethod]
    [DataRow(390, 844, true)]
    [DataRow(1440, 900, false)]
    public async Task ToastDismiss_WhileSheetOpen_DismissesToastAndSheetSurvives(int width, int height, bool touch)
    {
        var page = await OpenPageAsync("/feedback", width, height, touch);

        await page.Locator("[data-testid='open-bottom-sheet']").TapAsync();
        await page.Locator(".tm-drawer--bottom").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(600);

        await page.Locator("[data-testid='sheet-push-toast']").TapAsync();
        var toastContainer = page.Locator(".tm-toast-container:popover-open");
        await toastContainer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(600);

        // The toast container is exempt from the trap's inert background: a REAL click on the
        // dismiss button must land on the toast (pre-V3 it passed through to the backdrop and
        // closed the sheet).
        var dismiss = toastContainer.Locator(".tm-toast-dismiss").Last;
        var box = await dismiss.BoundingBoxAsync();
        Assert.IsNotNull(box, "the dismiss button must be laid out");
        await page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.WaitForTimeoutAsync(700);

        Assert.AreEqual(0, await toastContainer.Locator(".tm-toast").CountAsync(),
            "the click dismisses the toast");
        Assert.AreEqual(1, await page.Locator(".tm-drawer--bottom").CountAsync(),
            "the sheet stays open — the click must not pass through to the backdrop");

        await ShootAsync(page, $"v3-{width}-toast-dismiss-sheet-survives");
        if (touch) await AssertCoarsePointerStillMatchesAsync(page);
    }
}
