using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F3: anchored panels present as a bottom sheet on a narrow viewport and as the anchored popover
/// otherwise — against the real demo components at 1440 / 1024 / 390. The 390 leg runs with touch
/// emulation; screenshots are viewport-sized because a full-page capture resets touch emulation.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class OverlayMobilePresentationE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "f3-mobile-presentation");

    private async Task<IPage> OpenOverlayPageAsync(int width, int height = 844, bool touch = false, string culture = "en")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = touch,
            IsMobile = touch,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            Locale = culture + "-US",
        });
        // The demo's default culture follows the browser — pin it so the localized Done label is
        // deterministic.
        await context.AddInitScriptAsync(
            $"""
            localStorage.setItem('tm-demo-culture', '{culture}');
            document.cookie = 'tm-demo-culture={culture};path=/;max-age=31536000;samesite=lax';
            """);
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/overlay", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
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

    private static async Task AssertCoarsePointerStillMatchesAsync(IPage page)
    {
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"),
            "a viewport screenshot must not reset touch emulation — (pointer: coarse) still expected");
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

    [TestMethod]
    [DataRow(1440, 900)]
    [DataRow(1024, 768)]
    public async Task Dropdown_AtDesktopWidths_OpensAsAnchoredPopover(int width, int height)
    {
        var page = await OpenOverlayPageAsync(width, height);

        var trigger = page.Locator("[data-testid='overlay-mobile-dropdown'] .tm-dropdown-trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.ClickAsync();

        var menu = page.Locator("[data-testid='overlay-mobile-dropdown'] .tm-dropdown-menu");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Popover contract: a live top-layer panel anchored below the trigger, no sheet anywhere.
        Assert.IsTrue(await menu.EvaluateAsync<bool>("el => el.matches(':popover-open')"),
            "at desktop widths the menu must stay a top-layer popover");
        Assert.AreEqual(0, await page.Locator(".tm-overlay-panel-sheet").CountAsync(),
            "no sheet may render at desktop widths");

        var triggerBox = await trigger.BoundingBoxAsync();
        var menuBox = await menu.BoundingBoxAsync();
        Assert.IsNotNull(triggerBox);
        Assert.IsNotNull(menuBox);
        Assert.IsTrue(menuBox!.Y >= triggerBox!.Y + triggerBox.Height - 1,
            $"the popover must sit at/below the trigger (menu {menuBox.Y}, trigger bottom {triggerBox.Y + triggerBox.Height})");

        await ShootAsync(page, $"{width}-dropdown-popover");
        await page.Keyboard.PressAsync("Escape");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    [TestMethod]
    public async Task Dropdown_At390_OpensAsBottomSheet_WithTitleAndDone()
    {
        var page = await OpenOverlayPageAsync(390, 844, touch: true);

        var trigger = page.Locator("[data-testid='overlay-mobile-dropdown'] .tm-dropdown-trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.ClickAsync();

        // The same menu is a content-height bottom sheet, titled with the trigger text.
        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        Assert.IsTrue(await page.EvaluateAsync<bool>(
            "() => { const s = document.querySelector('.tm-overlay-panel-sheet'); return !!s && s.classList.contains('tm-drawer--bottom') && s.classList.contains('tm-sheet'); }"),
            "the sheet must compose TmDrawer Position=Bottom");

        Assert.AreEqual("Export", await sheet.Locator(".tm-overlay-panel-sheet__title").InnerTextAsync());
        Assert.AreEqual("Done", (await sheet.Locator(".tm-overlay-panel-sheet__done").InnerTextAsync()).Trim());
        // The sheet dialog is named (title without a label is an axe failure).
        Assert.AreEqual("Export", await sheet.GetAttributeAsync("aria-label"));
        // Menu items render inside the sheet body, each a 44px tap target on the coarse pointer.
        Assert.AreEqual(3, await sheet.Locator(".tm-dropdown-item").CountAsync());
        var firstItemBox = await sheet.Locator(".tm-dropdown-item").First.BoundingBoxAsync();
        Assert.IsTrue(firstItemBox!.Height >= 44,
            $"a menu item must be a 44px tap target on touch, got {firstItemBox.Height}");
        // No floating popover in sheet mode.
        Assert.AreEqual(0, await page.Locator(".tm-dropdown-menu").CountAsync());
        Assert.AreEqual("true", await trigger.GetAttributeAsync("aria-expanded"));

        // The sheet panel spans the viewport width and, once the slide-up settles, is anchored to
        // its bottom edge (during the animation the transform shifts the rect — wait first).
        await page.WaitForTimeoutAsync(400);
        var panelBox = await sheet.Locator(".tm-drawer__panel").BoundingBoxAsync();
        Assert.IsNotNull(panelBox);
        Assert.IsTrue(panelBox!.X <= 1 && panelBox.Width >= 389,
            $"the sheet panel must be full-bleed at 390px (x {panelBox.X}, width {panelBox.Width})");
        Assert.IsTrue(Math.Abs(panelBox.Y + panelBox.Height - 844) <= 2,
            $"the sheet panel bottom must sit on the viewport bottom ({panelBox.Y + panelBox.Height})");

        await ShootAsync(page, "390-dropdown-sheet");
        await AssertCoarsePointerStillMatchesAsync(page);

        // Done closes without selecting and returns focus to the trigger.
        await sheet.Locator(".tm-overlay-panel-sheet__done").TapAsync();
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-expanded"));
        var active = await ActiveElementDescriptionAsync(page);
        Assert.IsTrue(active.Contains("tm-dropdown-trigger"),
            $"focus must return to the trigger after Done, got '{active}'");

        await ShootAsync(page, "390-dropdown-sheet-done-closed");
        await AssertCoarsePointerStillMatchesAsync(page);
    }

    [TestMethod]
    public async Task Dropdown_At390_Escape_ClosesSheet_AndReturnsFocus()
    {
        var page = await OpenOverlayPageAsync(390, 844, touch: true);

        var trigger = page.Locator("[data-testid='overlay-mobile-dropdown'] .tm-dropdown-trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.ClickAsync();

        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);

        await page.Keyboard.PressAsync("Escape");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });

        var active = await ActiveElementDescriptionAsync(page);
        Assert.IsTrue(active.Contains("tm-dropdown-trigger"),
            $"Escape must hand focus back to the trigger, got '{active}'");
    }

    [TestMethod]
    public async Task Popover_At390_OpensAsSheet_WithItsOwnTitle()
    {
        var page = await OpenOverlayPageAsync(390, 844, touch: true);

        await page.GetByTestId("overlay-mobile-popover").Locator(".tm-popover__trigger").TapAsync();

        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        Assert.AreEqual("Filter options", await sheet.Locator(".tm-overlay-panel-sheet__title").InnerTextAsync());
        Assert.IsTrue(await sheet.GetByText("Any content").IsVisibleAsync(),
            "the popover body content must render inside the sheet");
        await page.WaitForTimeoutAsync(400);
        await ShootAsync(page, "390-popover-sheet");
        await AssertCoarsePointerStillMatchesAsync(page);

        await page.Keyboard.PressAsync("Escape");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
    }

    /// <summary>
    /// The architect's stacking rule: a sheet opened inside a TmModal stays inside the modal's DOM
    /// (no teleport), stacks above the modal content, and the two focus traps nest — the first
    /// Escape closes the calendar sheet, the second closes the modal.
    /// </summary>
    [TestMethod]
    public async Task DatePicker_At390_InModalSheet_OpensNestedSheet()
    {
        var page = await OpenOverlayPageAsync(390, 844, touch: true);

        await page.GetByTestId("overlay-open-modal").TapAsync();
        var modal = page.Locator(".tm-modal-overlay");
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var trigger = page.Locator("[data-testid='overlay-datepicker'] .tm-date-picker-trigger");
        await trigger.EvaluateAsync("el => el.scrollIntoView({ block: 'center' })");
        await page.WaitForTimeoutAsync(150);
        await trigger.TapAsync();

        // The calendar popup presents as a sheet…
        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        Assert.AreEqual(2, await page.Locator(".tm-drawer--bottom, .tm-modal--sheet").CountAsync(),
            "modal sheet + calendar sheet");

        // …rendered INSIDE the modal's DOM (stacking — no teleport to the body root)…
        Assert.IsTrue(await page.EvaluateAsync<bool>(
            "() => !!document.querySelector('.tm-modal-overlay .tm-overlay-panel-sheet')"),
            "the calendar sheet must stay inside the modal's DOM");

        // …with the picker's label as its title and a live calendar inside.
        Assert.AreEqual("Date near the modal's bottom edge",
            await sheet.Locator(".tm-overlay-panel-sheet__title").InnerTextAsync());
        Assert.AreEqual(1, await sheet.Locator(".tm-calendar").CountAsync());

        await page.WaitForTimeoutAsync(400);
        await ShootAsync(page, "390-datepicker-nested-sheet");
        await AssertCoarsePointerStillMatchesAsync(page);

        // One gesture peels one layer: calendar first, modal second.
        await page.Keyboard.PressAsync("Escape");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
        Assert.AreEqual(1, await modal.CountAsync(), "the modal must survive the calendar's Escape");

        await page.Keyboard.PressAsync("Escape");
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
    }
}
