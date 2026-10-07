using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E coverage for N319 (Tempo 2.9.1): a menu whose item handler opens a dialog must close
/// BEFORE the handler runs. The menu lives in the browser top layer — a still-open popover
/// would render over the new dialog and, because overlay.js consumes the Escape keydown in the
/// window capture phase, the first Escape would peel the stale menu instead of the dialog.
/// </summary>
[TestClass]
public class DropdownDialogE2ETests : WasmTestBase
{
    private const string PageUrl = "https://localhost:7106/overlay";

    [TestMethod]
    public async Task DropdownItem_OpensConfirmDialog_DialogIsTopmostAndEscapeClosesDialog()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(PageUrl);
        await WaitForAppReadyAsync(page);

        var trigger = page.Locator(
            "[data-testid='overlay-menu-dialog-dropdown'] .tm-dropdown-trigger");
        await trigger.ClickAsync();

        var menu = page.Locator(
            "[data-testid='overlay-menu-dialog-dropdown'] .tm-dropdown-menu");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await menu.Locator(".tm-dropdown-item").ClickAsync();

        // The demo handler awaits ~1.2s before opening the dialog — under the pre-N319 ordering
        // the top-layer menu stayed open for the handler's whole await, so the panel is still
        // mounted at this probe point. The menu must be gone BEFORE the dialog even exists.
        await page.WaitForTimeoutAsync(400);
        Assert.AreEqual(0, await page.Locator(".tm-overlay-panel").CountAsync(),
            "the menu's overlay panel must close before the item handler's async work runs");

        var dialog = page.Locator("[data-testid='overlay-menu-dialog'] .tm-modal");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var topmostAtCenter = await dialog.EvaluateAsync<bool>(
            "el => { const r = el.getBoundingClientRect();" +
            " const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);" +
            " return !!hit && el.contains(hit); }");
        Assert.IsTrue(topmostAtCenter,
            "the dialog must be the topmost element at its own center");

        // One Escape = the dialog. overlay.js swallows the keydown+keyup for any open entry, so
        // a stale menu would eat this press and the dialog would stay open.
        await page.Keyboard.PressAsync("Escape");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }
}
