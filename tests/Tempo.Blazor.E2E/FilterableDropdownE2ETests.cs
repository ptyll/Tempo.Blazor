using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E coverage for TmFilterableDropdown's constrained-height overlay panel (N318, Tempo 2.9.1):
/// the height cap must shrink AND regrow with the room below the anchor. Before the fix the cap
/// was read back from getComputedStyle — which resolves the panel's own previous inline
/// max-height — so once the room shrank, the panel could never grow back for its open life.
/// </summary>
[TestClass]
public class FilterableDropdownE2ETests : WasmTestBase
{
    private string PageUrl => $"{BaseUrl}/overlay";

    private static async Task ScrollTriggerTopToAsync(ILocator trigger, double viewportTop)
    {
        await trigger.EvaluateAsync(
            $"el => window.scrollTo({{ top: el.getBoundingClientRect().top + window.scrollY - {viewportTop}, behavior: 'instant' }})");
        // Two rAFs cover the scroll listener's own rAF-scheduled place() pass.
        await trigger.Page.EvaluateAsync(
            "() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
    }

    /// <summary>
    /// The defect can only bite while the panel STAYS on the bottom side — with the anchor near
    /// the viewport bottom the flip logic wins over the cap and the panel simply opens upward.
    /// A short 800×360 viewport lets room.top &lt;= room.bottom while the room below is tight,
    /// so the panel stays bottom with a clamped height; scrolling the trigger back up must then
    /// let it regrow to the stylesheet cap (16rem = 256px).
    /// </summary>
    [TestMethod]
    public async Task TmFilterableDropdown_ScrollUpThenDown_ListRegainsHeight()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await AssertCapRegrowsAsync(page);
    }

    /// <summary>
    /// Firefox leg: the cap regrowth goes through the same popover/scroll path in the top layer,
    /// which must hold there too. PlaywrightTestBase runs Chromium only, so this test owns a
    /// dedicated Playwright + Firefox pair.
    /// </summary>
    [TestMethod]
    public async Task TmFilterableDropdown_Firefox_ScrollUpThenDown_ListRegainsHeight()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !TestContext.Properties.Contains("Headless") || TestContext.Properties["Headless"]?.ToString() != "false",
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
            IgnoreHTTPSErrors = true,
        });
        try
        {
            var page = await context.NewPageAsync();
            await AssertCapRegrowsAsync(page);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    /// <summary>
    /// N329 (Tempo 2.9.1): when the anchor scrolls fully out of the viewport the panel is parked
    /// with visibility:hidden — while focus sits inside (the filter input) it must be DISMISSED
    /// instead, or keys keep landing on an invisible surface, and focus returns to the trigger.
    /// </summary>
    [TestMethod]
    public async Task TmFilterableDropdown_FilterFocused_AnchorScrolledAway_PanelClosesFocusOnTrigger()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(800, 360);
        await page.GotoAsync(PageUrl);
        await WaitForAppReadyAsync(page);

        var trigger = page.Locator(
            "[data-testid='overlay-constrain-height-dropdown'] .tm-filterable-dropdown-trigger");
        await trigger.ClickAsync();

        var panel = page.Locator(".tm-filterable-dropdown-menu.tm-overlay-panel--open");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await panel.Locator(".tm-filterable-dropdown-item").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Put focus inside the panel (the real-world case is the filter input, which the
        // component autofocuses a beat after open — focusing explicitly avoids the race), then
        // prove focus really sits inside, otherwise this scenario measures nothing.
        await panel.Locator(".tm-filterable-dropdown-filter-input").FocusAsync();
        var focusInside = await page.EvaluateAsync<bool>(
            "() => document.activeElement != null && document.activeElement.closest('.tm-filterable-dropdown-menu') != null");
        Assert.IsTrue(focusInside,
            "scenario invalid: the filter input must hold focus inside the open panel");

        // Scroll the trigger fully above the viewport; two rAFs cover the scroll listener's own
        // rAF-scheduled place() pass. Under the bug the panel just went visibility:hidden and the
        // filter kept focus.
        await page.EvaluateAsync("() => window.scrollTo({ top: document.body.scrollHeight, behavior: 'instant' })");
        await page.EvaluateAsync(
            "() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");

        // The panel must be dismissed (closed), not merely hidden.
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        // Focus returns to the trigger via the shared anchor-restore path (deferred one macrotask
        // inside overlay.js — poll rather than read it immediately).
        await page.WaitForFunctionAsync(
            "el => document.activeElement === el", await trigger.ElementHandleAsync());
    }

    private async Task AssertCapRegrowsAsync(IPage page)
    {
        await page.SetViewportSizeAsync(800, 360);
        await page.GotoAsync(PageUrl);
        await WaitForAppReadyAsync(page);

        var trigger = page.Locator(
            "[data-testid='overlay-constrain-height-dropdown'] .tm-filterable-dropdown-trigger");
        await trigger.ClickAsync();

        var panel = page.Locator(".tm-filterable-dropdown-menu.tm-overlay-panel--open");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        // The item list may render a beat after the panel opens — wait for it before measuring.
        await panel.Locator(".tm-filterable-dropdown-item").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Pin the trigger near the vertical middle: room.top ~= room.bottom ~= 148px, so the
        // panel stays on the bottom side (no flip) and the 256px stylesheet cap clamps to ~148.
        await ScrollTriggerTopToAsync(trigger, 160);

        var triggerBox = await trigger.BoundingBoxAsync();
        Assert.IsNotNull(triggerBox);
        Assert.IsTrue(Math.Abs(triggerBox!.Y - 160) < 30,
            $"scenario invalid: the trigger must sit near the viewport middle (y={triggerBox.Y})");

        var clampedHeight = await panel.EvaluateAsync<float>("el => el.getBoundingClientRect().height");
        Assert.IsTrue(clampedHeight < 200,
            $"scenario invalid: with ~148px of room the cap must visibly clamp the list (got {clampedHeight}px)");

        // Scroll the page down: the trigger moves toward the viewport top, room below grows to
        // ~288px, and the cap must regrow to the stylesheet ceiling (256px). Under the N318 bug
        // it stays stuck at ~148px because getComputedStyle resolves the previous inline cap.
        await ScrollTriggerTopToAsync(trigger, 40);

        var regrownHeight = await panel.EvaluateAsync<float>("el => el.getBoundingClientRect().height");
        Assert.IsTrue(regrownHeight >= 250,
            $"panel must regrow to the stylesheet cap once the room returns (got {regrownHeight}px)");
    }
}
