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

        // A click on the sliver above the sheet hits the backdrop. An inerted backdrop swallows it.
        await page.Mouse.ClickAsync(width / 2, 8);
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
        await page.GetByTestId("sheet-open-dialog").ClickAsync();
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
        await panel.WaitForAsync();
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

        var inside = await page.EvaluateAsync<bool>(
            """
            () => {
                const host = document.querySelector('[style*="position: relative"]');
                const panel = document.querySelector('.tm-drawer--inline .tm-drawer__panel');
                if (!host || !panel) return false;
                const h = host.getBoundingClientRect();
                const p = panel.getBoundingClientRect();
                return p.left >= h.left - 1 && p.right <= h.right + 1 && p.bottom <= h.bottom + 1 && p.top >= h.top - 1;
            }
            """);
        Assert.IsTrue(inside, "the inline panel is positioned inside its host, not the viewport");

        await page.GetByTestId("sheet-canvas").FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        Assert.IsTrue(await panel.IsVisibleAsync(), "Escape on the canvas behind an inline sheet does not close it");
    }
}
