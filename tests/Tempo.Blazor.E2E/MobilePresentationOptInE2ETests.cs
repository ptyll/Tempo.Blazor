using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F11: TmContextMenu / TmSplitButton / TmNotificationBell opt into the shared mobile bottom
/// sheet (<c>MobilePresentation=Auto</c>) — at 390 with touch the panel opens as a titled sheet,
/// Escape closes it and focus returns to the trigger; the default presentation stays the anchored
/// popover at every width. Runs against the /feedback demo page.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class MobilePresentationOptInE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "f11-mobile-presentation-optin");

    private async Task<IPage> OpenFeedbackPageAsync(int width, int height, bool touch = false, string culture = "en")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = touch,
            IsMobile = touch,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            Locale = culture + "-US",
        });
        // Pin the culture so the localized sheet titles are deterministic.
        await context.AddInitScriptAsync(
            $"""
            localStorage.setItem('tm-demo-culture', '{culture}');
            document.cookie = 'tm-demo-culture={culture};path=/;max-age=31536000;samesite=lax';
            """);
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/feedback", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static async Task<string> ShootAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, $"{name}.png");
        // FullPage resets touch emulation; a viewport shot keeps (pointer: coarse) honest.
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            FullPage = false,
            Timeout = 60_000,
        });
        return path;
    }

    private static async Task<string> ActiveElementDescriptionAsync(IPage page)
    {
        return await page.EvaluateAsync<string>(
            """
            () => {
                const a = document.activeElement;
                if (!a) return '(null)';
                return a.tagName.toLowerCase() + '.' + (a.className || '').toString().trim().split(/\s+/).join('.');
            }
            """);
    }

    private static async Task WaitForSheetAnimationAsync(IPage page)
    {
        // The sheet slides up with a transform; the rect is only truthful once it settles.
        await page.WaitForFunctionAsync(
            """
            () => {
                const panel = document.querySelector('.tm-overlay-panel-sheet .tm-drawer__panel');
                if (!panel) return false;
                const rect = panel.getBoundingClientRect();
                return Math.abs(rect.bottom - window.innerHeight) <= 2;
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task ContextMenu_At390_Touch_OpensAsTitledSheet_EscapeReturnsFocusToTrigger()
    {
        var page = await OpenFeedbackPageAsync(390, 844, touch: true);

        // The opt-in variant lives in the "Mobile presentation (opt-in)" section (the DEFAULT
        // menu earlier on the page keeps the popover — asserted by the 1440 test).
        var trigger = page.GetByTestId("feedback-contextmenu-sheet").Locator(".tm-context-menu__trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.TapAsync();

        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        Assert.IsTrue(await page.EvaluateAsync<bool>(
            "() => { const s = document.querySelector('.tm-overlay-panel-sheet'); return !!s && s.classList.contains('tm-drawer--bottom') && s.classList.contains('tm-sheet'); }"),
            "the opt-in context menu must compose TmDrawer Position=Bottom on a phone viewport");

        // The demo's localized sheet title (TmDemo_ActionsSheetTitle) and the Done action.
        Assert.AreEqual("Actions", await sheet.Locator(".tm-overlay-panel-sheet__title").InnerTextAsync());
        Assert.AreEqual("Done", (await sheet.Locator(".tm-overlay-panel-sheet__done").InnerTextAsync()).Trim());
        // The menu role survives the popover→sheet switch on the content wrapper.
        Assert.AreEqual(1, await sheet.Locator(".tm-overlay-panel-sheet__content[role='menu']").CountAsync(),
            "menu ownership (axe aria-required-parent) must survive the presentation switch");
        Assert.AreEqual(0, await page.Locator(".tm-context-menu.tm-overlay-panel").CountAsync(),
            "no floating popover may render beside the sheet");

        // 44px tap targets + rows flush with the sheet header (the T15 inset contract).
        var firstItemBox = await sheet.Locator(".tm-context-menu__item").First.BoundingBoxAsync();
        Assert.IsTrue(firstItemBox!.Height >= 44,
            $"a context-menu item must be a 44px tap target on touch, got {firstItemBox.Height}");

        await WaitForSheetAnimationAsync(page);
        await ShootAsync(page, "390-contextmenu-sheet");

        // Escape closes the sheet and focus returns to the trigger.
        await page.Keyboard.PressAsync("Escape");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-expanded"));
        var active = await ActiveElementDescriptionAsync(page);
        Assert.IsTrue(active.Contains("tm-context-menu__trigger"),
            $"Escape must hand focus back to the context-menu trigger, got '{active}'");
    }

    [TestMethod]
    public async Task ContextMenu_At390_Touch_ItemTap_ClosesSheet_AndRunsTheAction()
    {
        var page = await OpenFeedbackPageAsync(390, 844, touch: true);

        var trigger = page.GetByTestId("feedback-contextmenu-sheet").Locator(".tm-context-menu__trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.TapAsync();

        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        // Any item activation closes the sheet (N319: before the handler runs) — the toast proves
        // the action still fired through the sheet presentation.
        await sheet.GetByText("Edit", new LocatorGetByTextOptions { Exact = true }).TapAsync();
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
        await Assertions.Expect(page.Locator(".tm-toast")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task SplitButton_At390_Touch_OpensAsTitledSheet_EscapeReturnsFocusToToggle()
    {
        var page = await OpenFeedbackPageAsync(390, 844, touch: true);

        var toggle = page.GetByTestId("feedback-splitbutton-sheet").Locator(".tm-split-button__toggle");
        await toggle.ScrollIntoViewIfNeededAsync();
        await toggle.TapAsync();

        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        Assert.AreEqual("More actions", await sheet.Locator(".tm-overlay-panel-sheet__title").InnerTextAsync());
        Assert.AreEqual(0, await page.Locator(".tm-split-button__dropdown").CountAsync(),
            "no floating popover may render beside the sheet");

        await WaitForSheetAnimationAsync(page);
        await ShootAsync(page, "390-splitbutton-sheet");

        await page.Keyboard.PressAsync("Escape");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
        var active = await ActiveElementDescriptionAsync(page);
        Assert.IsTrue(active.Contains("tm-split-button__toggle"),
            $"Escape must hand focus back to the split-button toggle, got '{active}'");
    }

    [TestMethod]
    public async Task NotificationBell_At390_Touch_OpensAsTitledSheet_InnerTitleDrops_EscapeReturnsFocus()
    {
        var page = await OpenFeedbackPageAsync(390, 844, touch: true);

        var button = page.GetByTestId("feedback-bell-mobile").Locator(".tm-notification-bell__button");
        await button.ScrollIntoViewIfNeededAsync();
        await button.TapAsync();

        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        Assert.AreEqual("Notifications", await sheet.Locator(".tm-overlay-panel-sheet__title").InnerTextAsync(),
            "the localized TmNotificationBell_SheetTitle default names the sheet");

        // The in-panel title would duplicate the sheet header — it must be dropped in sheet mode,
        // while the filter/mark-all actions stay reachable.
        Assert.IsTrue(await page.EvaluateAsync<bool>(
            "() => { const t = document.querySelector('.tm-overlay-panel-sheet .tm-notification-bell__title'); return !!t && getComputedStyle(t).display === 'none'; }"),
            "the duplicated in-panel title must be hidden inside the sheet");
        Assert.IsTrue(await sheet.Locator(".tm-notification-bell__filter").IsVisibleAsync(),
            "the filter action must stay reachable inside the sheet");

        await WaitForSheetAnimationAsync(page);
        await ShootAsync(page, "390-notificationbell-sheet");

        await page.Keyboard.PressAsync("Escape");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
        var active = await ActiveElementDescriptionAsync(page);
        Assert.IsTrue(active.Contains("tm-notification-bell__button"),
            $"Escape must hand focus back to the bell button, got '{active}'");
    }

    [TestMethod]
    public async Task At1440_DefaultsStayPopovers_AndOptInAutoResolvesToPopover()
    {
        var page = await OpenFeedbackPageAsync(1440, 900);

        // The DEFAULT context menu (first on the page) opens the anchored popover.
        var defaultTrigger = page.Locator(".tm-context-menu-wrapper").First.Locator(".tm-context-menu__trigger");
        await defaultTrigger.ScrollIntoViewIfNeededAsync();
        await defaultTrigger.ClickAsync();

        var defaultMenu = page.Locator(".tm-context-menu.tm-overlay-panel");
        await defaultMenu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.IsTrue(await defaultMenu.EvaluateAsync<bool>("el => el.matches(':popover-open')"),
            "the default presentation must stay the top-layer popover at 1440");
        Assert.AreEqual(0, await page.Locator(".tm-overlay-panel-sheet").CountAsync(),
            "no sheet may render for the default presentation");
        await ShootAsync(page, "1440-contextmenu-default-popover");
        await page.Keyboard.PressAsync("Escape");
        await defaultMenu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        // The OPT-IN (Auto) variant also resolves to the popover on a desktop viewport.
        var optInTrigger = page.GetByTestId("feedback-contextmenu-sheet").Locator(".tm-context-menu__trigger");
        await optInTrigger.ScrollIntoViewIfNeededAsync();
        await optInTrigger.ClickAsync();

        // Only one popover menu is open at a time — the opt-in one — and no sheet anywhere.
        var openMenu = page.Locator(".tm-overlay-panel.tm-context-menu");
        await openMenu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.AreEqual(1, await openMenu.CountAsync());
        Assert.AreEqual(0, await page.Locator(".tm-overlay-panel-sheet").CountAsync(),
            "Auto resolves from the viewport: desktop stays the popover");
        await ShootAsync(page, "1440-contextmenu-optin-popover");
    }
}
