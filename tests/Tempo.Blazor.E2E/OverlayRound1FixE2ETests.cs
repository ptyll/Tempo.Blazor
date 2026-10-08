using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F3 review round 1 fixes, verified against the real demo components:
/// T1 combobox typing at 390 (characters kept, popover suggestions — never a sheet),
/// T5 Enter-opened pickers land focus inside the popup (standalone and inside a TmModal),
/// T7 the promoted sheet paints above sticky app bars,
/// T8 the date-range sheet has no horizontal overflow and a single, range-gated Done,
/// T10 the real TmDataTable export menu and column picker at 1440/1024/390,
/// T16 the no-scope anchored panels resolve through the internal probe.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class OverlayRound1FixE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "f3-round1");

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

    // ── T1: focus-keeping comboboxes keep their keystrokes at 390 ────────────────────────────

    [TestMethod]
    public async Task EntityPicker_At390_TypingKeepsCharacters_AndShowsPopoverSuggestions()
    {
        var page = await OpenPageAsync("/pickers", 390, 844, touch: true);

        var input = page.Locator(".tm-entity-picker__input").First;
        await input.ScrollIntoViewIfNeededAsync();
        await input.TapAsync();
        await page.Keyboard.TypeAsync("al", new KeyboardTypeOptions { Delay = 80 });
        await page.WaitForTimeoutAsync(900);

        // Both characters land — the panel stayed a popover and never stole focus.
        Assert.AreEqual("al", await input.InputValueAsync(),
            "typing into TmEntityPicker at 390 must keep every character (the panel is a popover, not a sheet)");
        Assert.AreEqual(0, await page.Locator(".tm-overlay-panel-sheet").CountAsync(),
            "a focus-keeping combobox must never present as a sheet");

        var dropdown = page.Locator(".tm-entity-picker__dropdown");
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await dropdown.EvaluateAsync<bool>("el => el.matches(':popover-open')"),
            "the suggestions ride the top-layer popover");

        await ShootAsync(page, "t1-390-entitypicker-typing");
        await AssertCoarsePointerStillMatchesAsync(page);
    }

    [TestMethod]
    public async Task QueryInput_At390_TypingKeepsText_AndShowsSuggestions()
    {
        var page = await OpenPageAsync("/query-input", 390, 844, touch: true);

        var input = page.Locator(".tm-query-input__input").First;
        await input.ScrollIntoViewIfNeededAsync();
        await input.TapAsync();
        await page.Keyboard.TypeAsync("st", new KeyboardTypeOptions { Delay = 80 });
        await page.WaitForTimeoutAsync(900);

        Assert.AreEqual("st", await input.InputValueAsync(),
            "typing into TmQueryInput at 390 must keep every character");
        Assert.AreEqual(0, await page.Locator(".tm-overlay-panel-sheet").CountAsync(),
            "a focus-keeping combobox must never present as a sheet");

        var dropdown = page.Locator(".tm-query-input__dropdown");
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await dropdown.Locator("[role='option']").CountAsync() > 0,
            "suggestions must be visible while typing");

        await ShootAsync(page, "t1-390-queryinput-typing");
        await AssertCoarsePointerStillMatchesAsync(page);
    }

    // ── T5: Enter-opened pickers land focus inside the popup ─────────────────────────────────

    [TestMethod]
    public async Task DatePicker_At1440_EnterOpen_LandsFocusInsidePopup()
    {
        var page = await OpenPageAsync("/pickers", 1440, 900);

        var trigger = page.Locator(".tm-date-picker-trigger").First;
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        var popup = page.Locator(".tm-date-picker-popup");
        await popup.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(400);

        var active = await page.EvaluateAsync<bool>(
            "() => !!document.activeElement && !!document.querySelector('.tm-date-picker-popup')?.contains(document.activeElement)");
        Assert.IsTrue(active,
            $"a cold Enter-opened picker must land focus inside the popup, got '{await ActiveElementDescriptionAsync(page)}'");

        await ShootAsync(page, "t5-1440-datepicker-enter-open");
        await page.Keyboard.PressAsync("Escape");
        await popup.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
    }

    [TestMethod]
    public async Task DatePicker_At1440_EnterOpenInsideModal_LandsFocusInsidePopup()
    {
        // The M2 repro: a date trigger inside a TmModal, opened with Enter at desktop.
        var page = await OpenPageAsync("/overlay", 1440, 900);

        await page.GetByTestId("overlay-open-modal").ClickAsync();
        var modal = page.Locator(".tm-modal-overlay");
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var trigger = page.Locator("[data-testid='overlay-datepicker'] .tm-date-picker-trigger");
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        var popup = page.Locator(".tm-date-picker-popup");
        await popup.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(400);

        var activeInside = await page.EvaluateAsync<bool>(
            "() => !!document.activeElement && !!document.querySelector('.tm-date-picker-popup')?.contains(document.activeElement)");
        Assert.IsTrue(activeInside,
            $"Enter inside a modal must land focus inside the popup, not on body — got '{await ActiveElementDescriptionAsync(page)}'");
        Assert.IsTrue(await page.EvaluateAsync<bool>(
            "() => !document.querySelector('.tm-date-picker-trigger').closest('[inert]') || !!document.activeElement.closest('.tm-date-picker-popup')"),
            "the inert trigger must not hold focus while the popup is open");

        await ShootAsync(page, "t5-1440-datepicker-enter-open-in-modal");

        await page.Keyboard.PressAsync("Escape");
        await popup.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
        Assert.AreEqual(1, await modal.CountAsync(), "the modal must survive the popup's Escape");
        await page.Keyboard.PressAsync("Escape");
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
    }

    // ── T7: the promoted sheet paints above sticky app bars ──────────────────────────────────

    [TestMethod]
    public async Task Sheet_At390_PaintsAboveStickyAppBars()
    {
        var page = await OpenPageAsync("/overlay", 390, 844, touch: true);

        // Reproduce the reported topology: the dropdown's section becomes a sticky app bar
        // (z 1020, its own stacking context) and a bottom navigation bar sits above it (z 1030).
        await page.EvaluateAsync(
            """
            () => {
                const wrapper = document.querySelector("[data-testid='overlay-mobile-dropdown']");
                const host = wrapper.closest('section') ?? wrapper.parentElement;
                host.style.position = 'sticky';
                host.style.top = '0';
                host.style.zIndex = '1020';
                const bar = document.createElement('div');
                bar.id = 't7-bottom-bar';
                bar.style.cssText = 'position:fixed;left:0;right:0;bottom:0;height:120px;z-index:1030;background:rgba(200,30,30,0.9);';
                document.body.appendChild(bar);
            }
            """);

        var trigger = page.Locator("[data-testid='overlay-mobile-dropdown'] .tm-dropdown-trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.TapAsync();

        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);

        // The sheet is full-bleed and its bottom row sits on the viewport bottom…
        var panelBox = await sheet.Locator(".tm-drawer__panel").BoundingBoxAsync();
        Assert.IsNotNull(panelBox);
        Assert.IsTrue(Math.Abs(panelBox!.Y + panelBox.Height - 844) <= 2,
            $"the sheet bottom must sit on the viewport bottom ({panelBox.Y + panelBox.Height})");

        // …and what paints at its bottom row is the SHEET, not the z-1030 bar (top layer wins).
        // elementFromPoint skips inert elements and the sheet's trap inerts the whole page behind
        // it — so inert is stripped for the hit-test (probe4) or the assertion would pass even
        // when paint order is wrong. Restored in the round-2 pass.
        var hit = await page.EvaluateAsync<bool>(
            """
            () => {
                const sheet = document.querySelector('.tm-overlay-panel-sheet');
                const wasInert = [...document.querySelectorAll('[inert]')];
                wasInert.forEach(e => e.removeAttribute('inert'));
                const el = document.elementFromPoint(195, 844 - 20);
                wasInert.forEach(e => e.setAttribute('inert', ''));
                return !!el && sheet.contains(el);
            }
            """);
        Assert.IsTrue(hit, "elementFromPoint on the sheet's bottom row must hit the sheet — the top layer beats the z-1030 bar");

        await ShootAsync(page, "t7-390-sheet-above-sticky-bars");
        await AssertCoarsePointerStillMatchesAsync(page);
    }

    // ── T8: the date-range sheet fits and has a single, range-gated Done ─────────────────────

    [TestMethod]
    public async Task DateRangePicker_At390_SheetFits_NoHorizontalOverflow_SingleDone()
    {
        var page = await OpenPageAsync("/pickers", 390, 844, touch: true);

        var trigger = page.Locator(".tm-date-range-trigger").First;
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.TapAsync();

        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.AreEqual(0, overflow, $"the date-range sheet must not overflow horizontally (extra {overflow}px)");

        // One Done: the sheet header action; the picker's own footer is hidden in sheet mode.
        Assert.AreEqual(1, await sheet.Locator(".tm-overlay-panel-sheet__done").CountAsync());
        Assert.AreEqual("none", await sheet.Locator(".tm-date-range-footer").EvaluateAsync<string>("el => getComputedStyle(el).display"));

        // The months stack vertically inside the sheet.
        Assert.IsTrue(await sheet.Locator(".tm-calendar").CountAsync() >= 2, "both months render");
        var calendars = await sheet.Locator(".tm-date-range-calendars").EvaluateAsync<string>("el => getComputedStyle(el).flexDirection");
        Assert.AreEqual("column", calendars, "the months must stack vertically in the sheet");

        // Half range: Done is disabled.
        var day = sheet.Locator(".tm-cal-day:not(.tm-cal-day--other-month):not(.tm-cal-day--disabled)").First;
        await day.TapAsync();
        await page.WaitForTimeoutAsync(300);
        Assert.IsTrue(await sheet.Locator(".tm-overlay-panel-sheet__done").EvaluateAsync<bool>("el => el.disabled"),
            "Done must stay disabled until both dates are picked");

        await ShootAsync(page, "t8-390-date-range-sheet");
        await AssertCoarsePointerStillMatchesAsync(page);
    }

    // ── T10: the real TmDataTable export menu and column picker ──────────────────────────────

    [TestMethod]
    [DataRow(1440, 900, false)]
    [DataRow(1024, 768, false)]
    [DataRow(390, 844, true)]
    public async Task DataTable_ExportMenu_PresentsPerViewport(int width, int height, bool touch)
    {
        var page = await OpenPageAsync("/data-table", width, height, touch);

        var trigger = page.Locator("[data-testid='dt-export-section'] .tm-dropdown-trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        if (touch) await trigger.TapAsync(); else await trigger.ClickAsync();

        if (width <= 390)
        {
            var sheet = page.Locator(".tm-overlay-panel-sheet");
            await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            Assert.AreEqual("Export", await sheet.Locator(".tm-overlay-panel-sheet__title").InnerTextAsync());
            Assert.AreEqual("Done", (await sheet.Locator(".tm-overlay-panel-sheet__done").InnerTextAsync()).Trim());
            var itemBox = await sheet.Locator(".tm-dropdown-item").First.BoundingBoxAsync();
            Assert.IsTrue(itemBox!.Height >= 44, $"export items must be 44px tap targets on touch, got {itemBox.Height}");
            // The page behind the sheet must show through DIMMED, not vanish under an opaque root:
            // the promoted root resets the UA [popover] paint (Canvas background), and the scrim
            // lives on the drawer's own overlay. IsVisibleAsync cannot detect occlusion (round-2
            // review R2-B1) — assert the computed paint instead.
            var rootBg = await sheet.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
            Assert.AreEqual("rgba(0, 0, 0, 0)", rootBg,
                "the promoted sheet root must be transparent — the page behind stays dimmed, not a flat grey wall");
            var backdropBg = await sheet.Locator(".tm-drawer__overlay").EvaluateAsync<string>(
                "el => getComputedStyle(el).backgroundColor");
            StringAssert.Contains(backdropBg, "0.5", "the dimming scrim paints on the drawer overlay");
            await page.WaitForTimeoutAsync(400);
            await ShootAsync(page, $"t10-{width}-dt-export-sheet");
            await AssertCoarsePointerStillMatchesAsync(page);
        }
        else
        {
            var menu = page.Locator("[data-testid='dt-export-section'] .tm-dropdown-menu");
            await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
            Assert.IsTrue(await menu.EvaluateAsync<bool>("el => el.matches(':popover-open')"),
                "at desktop widths the export menu stays a top-layer popover");
            Assert.AreEqual(0, await page.Locator(".tm-overlay-panel-sheet").CountAsync());
            await ShootAsync(page, $"t10-{width}-dt-export-popover");
        }

        await page.Keyboard.PressAsync("Escape");
    }

    [TestMethod]
    [DataRow(1440, 900, false)]
    [DataRow(390, 844, true)]
    public async Task DataTable_ColumnPicker_PresentsPerViewport_AndToggles(int width, int height, bool touch)
    {
        var page = await OpenPageAsync("/data-table", width, height, touch);

        var toggle = page.Locator(".tm-column-picker-toggle").First;
        await toggle.ScrollIntoViewIfNeededAsync();
        if (touch) await toggle.TapAsync(); else await toggle.ClickAsync();

        ILocator panel;
        if (width <= 390)
        {
            panel = page.Locator(".tm-overlay-panel-sheet");
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            var title = await panel.Locator(".tm-overlay-panel-sheet__title").InnerTextAsync();
            Assert.IsFalse(string.IsNullOrWhiteSpace(title), "the column-picker sheet must be titled");
            var rowBox = await panel.Locator(".tm-column-picker-item").First.BoundingBoxAsync();
            Assert.IsTrue(rowBox!.Height >= 44, $"column-picker rows must be 44px on a coarse pointer, got {rowBox.Height}");
            // The dialog role lives on the drawer root (the same element carries
            // .tm-overlay-panel-sheet and .tm-focus-scope); the content wrapper is labelled.
            Assert.AreEqual("dialog", await panel.GetAttributeAsync("role"));
            await page.WaitForTimeoutAsync(400);
            await ShootAsync(page, $"t10-{width}-dt-columns-sheet");
            await AssertCoarsePointerStillMatchesAsync(page);
        }
        else
        {
            panel = page.Locator(".tm-column-picker-panel");
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
            Assert.AreEqual("dialog", await panel.GetAttributeAsync("role"));
            Assert.IsFalse(string.IsNullOrWhiteSpace(await panel.GetAttributeAsync("aria-label")),
                "the column-picker popover must be named (Title doubles as aria-label)");
            await ShootAsync(page, $"t10-{width}-dt-columns-popover");
        }

        // Toggling a checkbox keeps the picker open and actually toggles the column.
        var firstCheckbox = panel.Locator(".tm-column-picker-item input[type='checkbox']").First;
        var columnKey = await firstCheckbox.GetAttributeAsync("data-key");
        Assert.IsFalse(string.IsNullOrEmpty(columnKey));
        var before = await firstCheckbox.IsCheckedAsync();
        if (touch) await firstCheckbox.TapAsync(); else await firstCheckbox.ClickAsync();
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual(!before, await panel.Locator($".tm-column-picker-item input[data-key='{columnKey}']").First.IsCheckedAsync(),
            "toggling a checkbox must flip the column visibility state");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Done/Escape closes and returns focus to the toggle.
        await page.Keyboard.PressAsync("Escape");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
        var active = await ActiveElementDescriptionAsync(page);
        Assert.IsTrue(active.Contains("tm-column-picker-toggle"),
            $"focus must return to the toggle, got '{active}'");
    }

    // ── T16: anchored panels resolve through the probe on the no-scope page ──────────────────

    [TestMethod]
    public async Task NoScope_At390_AnchoredPanelsResolveThroughProbe()
    {
        var page = await OpenPageAsync("/overlay-no-scope", 390, 844, touch: true);

        var trigger = page.Locator("[data-testid='noscope-dropdown'] .tm-dropdown-trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.TapAsync();

        // First frame may be the desktop popover (InitialMode); the probe then flips it to a sheet.
        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForTimeoutAsync(600);
        Assert.AreEqual(0, await page.Locator(".tm-dropdown-menu").CountAsync(),
            "the probe-resolved presentation must end as a sheet, with no leftover popover");

        await ShootAsync(page, "t16-390-noscope-dropdown-sheet");
        await AssertCoarsePointerStillMatchesAsync(page);

        // The date picker resolves the same way.
        await page.Keyboard.PressAsync("Escape");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
        var picker = page.Locator("[data-testid='noscope-datepicker'] .tm-date-picker-trigger");
        await picker.ScrollIntoViewIfNeededAsync();
        await picker.TapAsync();
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await ShootAsync(page, "t16-390-noscope-datepicker-sheet");
        await AssertCoarsePointerStillMatchesAsync(page);
    }
}
