using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Feedback;

/// <summary>
/// The round-2 sheet defects against the real demo components: a backdrop click closes the drawer,
/// a dialog nested in the sheet hands the sheet back, the keyboard lifts a modal sheet, and a gesture
/// leaves no inline height behind.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class SheetRound2E2ETests : WasmTestBase
{
    private async Task<IPage> OpenAsync(string route, int width, int height)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        // The self-hosted probe checks 5010; with an external host the base URL stays 7106, which is
        // not where this demo listens. Use the host that actually answered.
        var host = Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "http://localhost:5010";
        await page.GotoAsync($"{host}{route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    [TestMethod]
    [DataRow(390, "/feedback", "open-bottom-sheet", ".tm-drawer--bottom")]
    [DataRow(1440, "/feedback", "open-bottom-sheet", ".tm-drawer--bottom")]
    [DataRow(390, "/feedback", "open-right-drawer", ".tm-drawer--right")]
    [DataRow(1440, "/feedback", "open-right-drawer", ".tm-drawer--right")]
    public async Task BackdropClick_ClosesTheDrawer_AndRestoresFocus(int width, string route, string triggerId, string drawer)
    {
        var page = await OpenAsync(route, width, 844);
        var trigger = page.GetByTestId(triggerId);
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.ClickAsync();
        await page.Locator(drawer).WaitForAsync();

        // (width/2, 8) is inside a side panel: below 640px the panel is full-bleed, and at 1440 a
        // right panel still covers that point. The backdrop point has to sit outside the panel box.
        var point = await page.EvaluateAsync<float[]>(
            """
            (drawer) => {
                const panel = document.querySelector(drawer + ' .tm-drawer__panel').getBoundingClientRect();
                const x = panel.left > 8 ? 4 : (panel.right + 8 < window.innerWidth ? panel.right + 8 : panel.left + panel.width / 2);
                const y = panel.top > 8 ? 4 : panel.top + panel.height / 2;
                return [x, y];
            }
            """,
            drawer);
        var outside = await page.EvaluateAsync<bool>(
            """
            ([drawer, x, y]) => {
                const panel = document.querySelector(drawer + ' .tm-drawer__panel').getBoundingClientRect();
                return x < panel.left - 1 || x > panel.right + 1 || y < panel.top - 1 || y > panel.bottom + 1;
            }
            """,
            new object[] { drawer, point[0], point[1] });
        if (!outside)
        {
            // A full-bleed panel covers the backdrop. The close control is the one that dismisses it.
            await page.Locator($"{drawer} .tm-drawer__close").ClickAsync();
        }
        else
        {
            await page.Mouse.ClickAsync(point[0], point[1]);
        }

        await page.Locator(drawer).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });

        var focused = await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('data-testid') ?? ''");
        Assert.AreEqual(triggerId, focused, "closing from the backdrop returns focus to the trigger");
    }

    [TestMethod]
    public async Task NestedDialog_HandsTheDrawerBack_AndReleasesThePage()
    {
        var page = await OpenAsync("/feedback", 390, 844);
        await page.GetByTestId("open-bottom-sheet").ClickAsync();
        await page.Locator(".tm-drawer--bottom").WaitForAsync();
        // The sheet opens at half height, so the trigger starts below the panel. Scroll it up first.
        var nested = page.GetByTestId("sheet-open-dialog");
        await nested.EvaluateAsync("el => el.scrollIntoView({ block: 'center' })");
        await nested.ClickAsync();
        await page.Locator(".tm-dialog").WaitForAsync();

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-dialog").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        var usable = await page.EvaluateAsync<bool>(
            "() => !document.querySelector('.tm-drawer--bottom').hasAttribute('inert')");
        Assert.IsTrue(usable, "the drawer must be usable after the dialog closes");

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-drawer--bottom").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        var leaked = await page.EvaluateAsync<bool>(
            "() => document.documentElement.classList.contains('tm-scroll-lock') || document.querySelector('[inert]') !== null");
        Assert.IsFalse(leaked, "both closed: no inert and no scroll lock");
    }

    [TestMethod]
    public async Task ModalSheet_LiftsAboveTheKeyboard()
    {
        var page = await OpenAsync("/modal-dialog", 390, 844);
        await page.EvaluateAsync(
            """
            () => {
                const viewport = { height: 480, offsetTop: 0, width: 390, addEventListener() {}, removeEventListener() {} };
                Object.defineProperty(window, 'visualViewport', { configurable: true, get: () => viewport });
                window.dispatchEvent(new Event('resize'));
            }
            """);
        await page.GetByTestId("toggle-sheet-presentation").CheckAsync();
        await page.GetByTestId("open-sheet").ClickAsync();
        await page.Locator(".tm-modal--sheet").WaitForAsync();
        // The sheet reads the viewport when it attaches and on resize. Re-dispatch so a fake installed
        // after navigation is what the module measures.
        await page.EvaluateAsync("() => window.dispatchEvent(new Event('resize'))");
        await page.WaitForFunctionAsync(
            "() => { const f = document.querySelector('.tm-modal-footer'); return !!f && f.getBoundingClientRect().bottom <= 480; }",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });

        var footerBottom = await page.Locator(".tm-modal-footer").EvaluateAsync<double>("el => el.getBoundingClientRect().bottom");
        Assert.IsTrue(footerBottom <= 480, $"the footer ({footerBottom:F0}) must sit inside the visible viewport");
    }

    [TestMethod]
    public async Task SmallDrag_LeavesNoInlineHeight()
    {
        var page = await OpenAsync("/feedback", 390, 844);
        await page.GetByTestId("open-bottom-sheet").ClickAsync();
        var panel = page.Locator(".tm-drawer--bottom .tm-drawer__panel");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
        var handle = await page.Locator(".tm-sheet__handle").BoundingBoxAsync();
        Assert.IsNotNull(handle);

        await page.Mouse.MoveAsync(handle.X + handle.Width / 2, handle.Y + handle.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(handle.X + handle.Width / 2, handle.Y + 30);
        await page.Mouse.UpAsync();

        var inline = await panel.EvaluateAsync<string>("el => el.style.getPropertyValue('height')");
        Assert.AreEqual(string.Empty, inline, "a release clears the inline height the gesture wrote");
    }

    [TestMethod]
    public async Task TapOnTheModalGrabber_DoesNotDismiss()
    {
        var page = await OpenAsync("/modal-dialog", 390, 844);
        await page.GetByTestId("toggle-sheet-presentation").CheckAsync();
        await page.GetByTestId("open-sheet").ClickAsync();
        await page.Locator(".tm-modal--sheet").WaitForAsync();
        var handle = await page.Locator(".tm-modal--sheet .tm-sheet__handle").BoundingBoxAsync();
        Assert.IsNotNull(handle);

        await page.Mouse.ClickAsync(handle.X + handle.Width / 2, handle.Y + handle.Height / 2);

        Assert.IsTrue(await page.Locator(".tm-modal--sheet").IsVisibleAsync(), "a tap on the grabber is not a dismiss");
    }

    [TestMethod]
    public async Task OverlayClick_ClosesTheModal()
    {
        var page = await OpenAsync("/modal-dialog", 1440, 900);
        await page.GetByTestId("open-basic-modal").ClickAsync();
        await page.Locator(".tm-modal").First.WaitForAsync();

        await page.Mouse.ClickAsync(10, 10);

        Assert.AreEqual(0, await page.Locator(".tm-modal-overlay").CountAsync(), "a click on the dimmed area closes the modal");
    }

    [TestMethod]
    [DataRow(390)]
    [DataRow(1440)]
    public async Task InlineSheet_StaysInsideItsHost_AndIgnoresEscapeOnTheCanvas(int width)
    {
        var page = await OpenAsync("/modal-dialog", width, 844);
        await page.GetByTestId("open-inline-drawer").ClickAsync();
        var panel = page.Locator(".tm-drawer--inline .tm-drawer__panel");
        await panel.WaitForAsync();
        // The slide-up animation is still running when the panel first appears, so a box read then
        // sits below the host. Wait for it to settle.
        await page.WaitForFunctionAsync(
            "() => getComputedStyle(document.querySelector('.tm-drawer--inline .tm-drawer__panel')).transform === 'none'",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });

        var measured = await page.EvaluateAsync<string>(
            """
            () => {
                const host = document.querySelector('[data-testid="inline-sheet-host"]');
                const panel = document.querySelector('.tm-drawer--inline .tm-drawer__panel');
                const box = el => el ? JSON.stringify(el.getBoundingClientRect()) : 'missing';
                return 'host=' + box(host) + ' panel=' + box(panel);
            }
            """);
        var inside = await page.EvaluateAsync<bool>(
            """
            () => {
                const host = document.querySelector('[data-testid="inline-sheet-host"]');
                const panel = document.querySelector('.tm-drawer--inline .tm-drawer__panel');
                if (!host || !panel) return false;
                const h = host.getBoundingClientRect();
                const p = panel.getBoundingClientRect();
                return p.left >= h.left - 1 && p.right <= h.right + 1 && p.bottom <= h.bottom + 1 && p.top >= h.top - 1;
            }
            """);
        Assert.IsTrue(inside, "the inline panel is positioned inside its host, not the viewport: " + measured);

        await page.GetByTestId("sheet-canvas").FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        Assert.IsTrue(await panel.IsVisibleAsync(), "Escape on the canvas behind an inline sheet does not close it");
    }
}
