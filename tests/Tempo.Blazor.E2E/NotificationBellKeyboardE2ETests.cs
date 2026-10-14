using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F25a: the TmNotificationBell entries are native <button> elements — the old div @onclick rows
/// were a keyboard trap (a keyboard user could open the dropdown but never activate a
/// notification). Browser proof on the /feedback demo: Tab/Enter reach the entries and one Enter
/// activates EXACTLY ONE entry (the native click — no emulation to double-fire, no dead key).
/// </summary>
[TestClass]
public class NotificationBellKeyboardE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task Feedback_Bell_EntriesAreNativeButtons_EnterActivatesExactlyOne()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/feedback");
        await WaitForAppReadyAsync(page);

        // Guarantee at least one unread entry: the demo's "Add Notification" button seeds one.
        var add = page.Locator("button").Filter(new() { HasText = "Add Notification" }).First;
        await add.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await add.ClickAsync();

        // Keyboard story from the start: the bell trigger is a native button, Enter opens the panel.
        var bell = page.Locator("[data-testid='feedback-bell-mobile'] .tm-notification-bell__button");
        await bell.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await bell.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var dropdown = page.Locator("[data-testid='feedback-bell-mobile'] .tm-notification-bell__dropdown");
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var items = dropdown.Locator(".tm-notification-bell__item");
        Assert.IsTrue(await items.CountAsync() > 0, "the dropdown lists the seeded notification");
        var first = items.First;
        Assert.AreEqual("BUTTON", await first.EvaluateAsync<string>("el => el.tagName"),
            "a notification entry must be a native button (the div @onclick was a keyboard trap)");
        Assert.AreEqual("button", await first.GetAttributeAsync("type"),
            "type=button — the entry never submits anything");

        // Enter on the entry activates it exactly once: the focused entry flips to read and the
        // OTHERS keep their unread state (a double fire would consume two, a dead key none).
        var unreadBefore = await dropdown
            .Locator(".tm-notification-bell__item--unread").CountAsync();
        Assert.IsTrue(unreadBefore > 0, "the seeded entry is unread");
        await first.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "before => document.querySelectorAll(\"[data-testid='feedback-bell-mobile'] .tm-notification-bell__item--unread\").length === before - 1",
            unreadBefore,
            new PageWaitForFunctionOptions { Timeout = 10000 });
        var unreadAfter = await dropdown
            .Locator(".tm-notification-bell__item--unread").CountAsync();
        Assert.AreEqual(unreadBefore - 1, unreadAfter,
            "one Enter marks exactly the focused entry read — no double fire, no dead key");
    }
}
