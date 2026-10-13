using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F14 lane: non-button toolbar items (<c>TmToolbarItem</c>), <c>PreserveFocus</c> and dropdowns in a
/// toolbar, on the REAL components (the /toolbar-forms demo drives them as shipped). Kept in its own
/// class so <c>ToolbarOverflowE2ETests</c> stays under the source-file line budget. The same lane
/// invariants as the F4 tests: computed values and real key presses, never markup alone.
/// </summary>
[TestClass]
public class ToolbarItemsE2ETests : WasmTestBase
{
    private async Task<IPage> OpenAsync(int width, int height, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/toolbar-forms");
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='toolbar-overflow-demo']").WaitForAsync();
        return page;
    }

    private static ILocator Bar(IPage page) => page.Locator("[data-testid='toolbar-items']");

    /// <summary>Waits until the toolbar's fit measurement has settled (rAF-coalesced re-render done).</summary>
    private static async Task SettleAsync(IPage page)
    {
        await page.WaitForTimeoutAsync(600);
        await Bar(page).WaitForAsync();
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
    }

    private static async Task AssertNoPageOverflowAsync(IPage page, string where)
    {
        var overflow = await page.EvaluateAsync<double>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.IsTrue(overflow <= 1, $"{where}: the page must not scroll horizontally (overflow {overflow}px)");
    }

    // ── F14/C1: TmToolbarItem — non-button controls leave the bar like buttons ───────────────────

    [TestMethod]
    public async Task C1_Items_1280_NonButtonControlsCollapseIntoMore_AndComeBackWhenWidened()
    {
        // 1280, not 1024: the demo shell keeps a fixed 18.5rem sidebar from lg up plus lg:px-8 content
        // padding (MainLayout), so a 1024 viewport leaves only ~632px inside the card — the 760px host
        // never fits and the items collapse there. At 1280 the content column is ~920px and the host
        // is truly 760px, wide enough for every item.
        var page = await OpenAsync(1280, 768, touch: false);
        await SettleAsync(page);
        var bar = Bar(page);
        Assert.AreEqual(0, await bar.Locator(".tm-toolbar-item--collapsed").CountAsync(),
            "the 760px host fits every item at 1280");

        // Narrow the page far below the host width: the lowest-priority items (the three wrappers)
        // leave the bar — each as ONE collapsed wrapper, the select/toggle inside inert with it.
        // 390 (not just 600): at 600px the font-size item still fits (written first, it leaves last
        // among the Secondary items) — at 390 everything collapsible is in More, the font-size
        // group included (the 390 touch test pins the same).
        await page.SetViewportSizeAsync(390, 768);
        await SettleAsync(page);
        var collapsed = await bar.Locator(".tm-toolbar-item--collapsed").CountAsync();
        Assert.IsTrue(collapsed >= 1, "at least one non-button item collapsed into More");
        var first = bar.Locator(".tm-toolbar-item--collapsed").First;
        Assert.AreEqual("true", await first.GetAttributeAsync("aria-hidden"));

        await bar.Locator("button.tm-toolbar-more").ClickAsync();
        await Assertions.Expect(page.Locator("[role='menu']")).ToBeVisibleAsync();
        // The grid toggle offers its own labelled group next to the font-size one — find the group
        // by its label, not by position.
        var group = page.Locator("[role='menu'] [role='group'][aria-label='Font size']");
        await Assertions.Expect(group).ToBeVisibleAsync();
        Assert.AreEqual("Font size", await group.GetAttributeAsync("aria-label"));
        var labels = await group.Locator("[role='menuitem'], [role='menuitemcheckbox']")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");
        CollectionAssert.AreEqual(new[] { "8", "10", "12", "14", "18", "24", "36" }, labels,
            "the font-size item offers its entries as one labelled group, in the written order");
        // Checked is a bool per entry (review fix): every size is a menuitemcheckbox, exactly the
        // current one checked.
        var checkedEntry = group.Locator("[role='menuitemcheckbox'][aria-checked='true']");
        Assert.AreEqual(1, await checkedEntry.CountAsync(), "exactly one size is the checked entry");
        Assert.AreEqual("12", (await checkedEntry.TextContentAsync())?.Trim(),
            "the current size is the checked entry");

        await page.SetViewportSizeAsync(1280, 768);
        await SettleAsync(page);
        Assert.AreEqual(0, await bar.Locator(".tm-toolbar-item--collapsed").CountAsync(),
            "widening the page brings every item back");
        await AssertNoPageOverflowAsync(page, "C1 items");
    }

    [TestMethod]
    public async Task C1_Items_Keyboard_ColorPickerTriggerJoinsTheRovingSet_SelectStaysItsOwnTabStop()
    {
        // 1280 so the whole bar is on screen (see the C1 1280 test). Deterministic roving order:
        // [colour trigger, New, Open, Save, Export trigger] — the native select and the toggle keep
        // their own Tab stops and are never arrow stops.
        var page = await OpenAsync(1280, 768, touch: false);
        await SettleAsync(page);
        var bar = Bar(page);

        await page.EvaluateAsync(
            "() => [...document.querySelectorAll(\"[data-testid='toolbar-items'] button.tm-toolbar-btn\")]" +
            ".find(b => b.textContent.includes('New')).focus()");

        await page.Keyboard.PressAsync("ArrowLeft");
        Assert.IsTrue(await page.EvaluateAsync<bool>(
            "() => document.activeElement.classList.contains('tm-color-picker-trigger')"),
            "ArrowLeft from New moves to the previous roving control — the colour picker trigger");

        await page.Keyboard.PressAsync("Home");
        Assert.IsTrue(await page.EvaluateAsync<bool>(
            "() => document.activeElement.classList.contains('tm-color-picker-trigger')"),
            "Home moves to the FIRST roving control — the colour picker trigger (the native select before it is not one)");

        await page.Keyboard.PressAsync("End");
        Assert.IsTrue(await page.EvaluateAsync<bool>(
            "() => document.activeElement.classList.contains('tm-dropdown-trigger')"),
            "End moves to the last roving control — the Export dropdown trigger");

        // Exactly one tab stop (the roving set) and Tab never lands on a roving bar control.
        Assert.AreEqual(1, await bar.Locator("[tabindex='0']").CountAsync(), "exactly one tab stop in the toolbar");
        await page.Keyboard.PressAsync("Tab");
        Assert.IsFalse(await page.EvaluateAsync<bool>(
            "() => document.activeElement?.matches('button.tm-toolbar-btn, button.tm-toolbar-more, button.tm-dropdown-trigger') === true"),
            "Tab does not walk the roving controls");
    }

    [TestMethod]
    public async Task C1_Items_390_Touch_SheetShowsTheFontSizeGroup_With44pxEntries()
    {
        var page = await OpenAsync(390, 844, touch: true);
        await SettleAsync(page);
        var bar = Bar(page);

        await bar.Locator("button.tm-toolbar-more").TapAsync();
        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        // The grid toggle offers its own labelled group next to the font-size one — find the group
        // by its label, not by position.
        var group = sheet.Locator("[role='group'][aria-label='Font size']");
        await Assertions.Expect(group).ToBeVisibleAsync();
        Assert.AreEqual("Font size", await group.GetAttributeAsync("aria-label"));

        foreach (var item in await group.Locator("[role='menuitem'], [role='menuitemcheckbox']").AllAsync())
        {
            var b = await item.BoundingBoxAsync();
            Assert.IsTrue(b!.Height >= 43.5, $"grouped entries are 44px targets in the sheet (height {b.Height})");
        }

        // The colour picker item is a plain entry that opens its dialog with the menu kept open.
        var colorEntry = page.Locator("[role='menu'] [role='menuitem']", new PageLocatorOptions { HasText = "Text colour" });
        await colorEntry.TapAsync();
        await Assertions.Expect(page.Locator("[data-testid='toolbar-items-color-dialog']")).ToBeVisibleAsync();
        await AssertNoPageOverflowAsync(page, "C1 items 390");
    }

    // ── F14/C6: a TmDropdown in a toolbar is one arrow stop, not its own Tab stop ────────────────

    [TestMethod]
    public async Task C6_DropdownInToolbar_IsOneArrowStop_AndTabLeavesTheToolbarOnce()
    {
        // 1280 so the whole bar is on screen (see the C1 1280 test).
        var page = await OpenAsync(1280, 768, touch: false);
        await SettleAsync(page);
        var bar = Bar(page);

        var trigger = bar.Locator("button.tm-dropdown-trigger");
        Assert.AreEqual(1, await trigger.CountAsync());
        Assert.IsNotNull(await trigger.GetAttributeAsync("data-tm-toolbar-control"),
            "the dropdown trigger carries the roving hook inside a toolbar");

        // ArrowRight walks to the trigger (it is the last control before the pinned Save).
        await page.EvaluateAsync(
            "() => document.querySelector(\"[data-testid='toolbar-items'] [tabindex='0']\").focus()");
        for (var i = 0; i < 10; i++)
        {
            var onTrigger = await page.EvaluateAsync<bool>(
                "() => document.activeElement.classList.contains('tm-dropdown-trigger')");
            if (onTrigger) break;
            await page.Keyboard.PressAsync("ArrowRight");
        }

        Assert.IsTrue(await page.EvaluateAsync<bool>(
            "() => document.activeElement.classList.contains('tm-dropdown-trigger')"),
            "ArrowRight reaches the dropdown trigger");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[role='menu']")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("[role='menu']")).ToHaveCountAsync(0);

        // Tab out of the trigger leaves the toolbar in one press: the trigger is the toolbar's tab
        // stop (not a separate one), and the text-entry controls (the native select and the toggle
        // checkbox) keep their own Tab stops but are written BEFORE the trigger, so the next Tab
        // target is outside the toolbar.
        await page.Keyboard.PressAsync("Tab");
        var landedInside = await page.EvaluateAsync<bool>(
            "() => !!document.activeElement?.closest(\"[data-testid='toolbar-items']\")");
        Assert.IsFalse(landedInside, "Tab does not walk into another toolbar control");
    }

    // ── F14/C4-a: PreserveFocus — the text selection survives a pointer press ────────────────────

    [TestMethod]
    public async Task C4_PreserveFocus_PointerPress_KeepsTheFocusAndSelectionInTheTextField()
    {
        var page = await OpenAsync(1024, 768, touch: false);
        var card = page.Locator("[data-testid='toolbar-preservefocus-card']");
        await Assertions.Expect(card).ToBeVisibleAsync();

        var input = card.Locator("input");
        await input.ClickAsync();
        await input.PressAsync("ControlOrMeta+a");
        var before = await page.EvaluateAsync<string>(
            "() => { const a = document.activeElement; return a.tagName + ':' + (a.selectionStart ?? '') + ':' + (a.selectionEnd ?? ''); }");

        // A pointer press on the PreserveFocus button must not move focus to the button.
        await card.Locator("button.tm-toolbar-btn").First.ClickAsync();
        var after = await page.EvaluateAsync<string>(
            "() => { const a = document.activeElement; return a.tagName + ':' + (a.selectionStart ?? '') + ':' + (a.selectionEnd ?? ''); }");

        Assert.AreEqual(before, after, "focus and selection survive the PreserveFocus button press");
    }
}
