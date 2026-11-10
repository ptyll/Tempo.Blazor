using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 7ba95ed6, Fáze 5 — the follow-ups of the two agent surfaces that are pure browser behaviour, so only a real
/// browser can prove them:
/// <list type="bullet">
/// <item>the autonomy dial's six keys belong to the slider and must not scroll the page (the guard is one ES module —
/// Blazor cannot preventDefault per key), while every OTHER key keeps its browser behaviour;</item>
/// <item>the memory panel keeps an undo row PER pending removal inside its ten-second window, so the middle row of two
/// can still be taken back and the undone removal never reaches the page;</item>
/// <item>the editor carries the CATEGORY beside the text, and the committed change regroups the row;</item>
/// <item>a 375 px card shows the status filter as a real select instead of the five-segment tray.</item>
/// </list>
/// </summary>
public partial class AiAgentsE2ETests
{
    /// <summary>
    /// The four arrows (and Home/End) are the dial's own keys. The module preventDefaults them on the focused slider —
    /// a plain keydown listener on the document (in its bubble phase, so it runs after the slider's own) reports what the
    /// browser was told — and the page does not move. A key the dial does NOT own stays unprevented, which is what proves
    /// the guard is conditional rather than a blanket `@onkeydown:preventDefault`.
    /// </summary>
    [TestMethod]
    public async Task TheAutonomyDialKeys_AreSwallowed_SoThePageNeverScrolls()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, AutonomySection);

        var slider = page.Locator("[data-testid='ai-agents-autonomy'] [data-testid='autonomy-dial-track']");
        await slider.FocusAsync();
        await page.EvaluateAsync(
            "() => { window.__tmDialKeys = [];"
            + " document.addEventListener('keydown', event =>"
            + " window.__tmDialKeys.push({ key: event.key, prevented: event.defaultPrevented }), false); }");

        var before = await page.EvaluateAsync<double>("() => window.scrollY");
        Assert.AreEqual("1", await slider.GetAttributeAsync("aria-valuenow"));

        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(150);
        Assert.AreEqual("0", await slider.GetAttributeAsync("aria-valuenow"),
            "the arrow still steps the dial: the guard swallows the scroll, not the key");

        // The edge case: at the first grade ArrowDown changes nothing — and still must not scroll.
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(150);
        Assert.AreEqual("0", await slider.GetAttributeAsync("aria-valuenow"),
            "an arrow past the end of the range changes nothing");

        await page.Keyboard.PressAsync("ArrowUp");
        await page.WaitForTimeoutAsync(150);
        Assert.AreEqual("1", await slider.GetAttributeAsync("aria-valuenow"));

        var after = await page.EvaluateAsync<double>("() => window.scrollY");
        Assert.AreEqual(before, after, "not one of the dial's keys scrolled the page");

        var keys = await page.EvaluateAsync<string[]>(
            "() => window.__tmDialKeys.map(entry => entry.key + (entry.prevented ? ':prevented' : ':default'))");
        CollectionAssert.AreEqual(
            new[] { "ArrowDown:prevented", "ArrowDown:prevented", "ArrowUp:prevented" },
            keys,
            "every owned key had its default action swallowed by the module");

        await ShootAsync(page, "ai-agents-autonomy-keys-light-1440");

        // The guard is conditional: Tab is not the dial's key, so it keeps moving the focus.
        await page.EvaluateAsync("() => { window.__tmDialKeys = []; }");
        await slider.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(150);
        var afterTab = await page.EvaluateAsync<string[]>(
            "() => window.__tmDialKeys.map(entry => entry.key + (entry.prevented ? ':prevented' : ':default'))");
        CollectionAssert.AreEqual(new[] { "Tab:default" }, afterTab,
            "a key the dial does not own must keep its browser behaviour");

        // Edge case: the same guarantee inside a 375 px phone viewport, where the page is much taller.
        var phone = await OpenAsync(375, 812, coarsePointer: true);
        await ScrollToSection(phone, AutonomySection);
        var phoneSlider = phone.Locator("[data-testid='ai-agents-autonomy'] [data-testid='autonomy-dial-track']");
        await phoneSlider.FocusAsync();
        var phoneBefore = await phone.EvaluateAsync<double>("() => window.scrollY");
        await phone.Keyboard.PressAsync("ArrowDown");
        await phone.Keyboard.PressAsync("ArrowDown");
        await phone.WaitForTimeoutAsync(200);
        Assert.AreEqual(phoneBefore, await phone.EvaluateAsync<double>("() => window.scrollY"),
            "a phone keeps the section in place too");
        Assert.AreEqual("0", await phoneSlider.GetAttributeAsync("aria-valuenow"));
        await ShootAsync(phone, "ai-agents-autonomy-keys-phone-375");
    }

    /// <summary>
    /// Two removals inside the one undo window are two independent promises: the panel keeps two rows with their own
    /// countdowns, the FIRST row can be taken back on its own (and the page never hears about it), and the editor that
    /// runs beside the text carries the category — a committed change regroups the row.
    /// </summary>
    [TestMethod]
    public async Task TheMemoryPanelQueuesTwoUndoRows_AndEditsTheCategory()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, MemorySection);

        var panel = page.Locator("[data-testid='ai-agents-memory']");
        var units = "[data-testid='ai-agents-memory'] [data-testid='memory-item'][data-item-id='mem-units']";
        var tone = "[data-testid='ai-agents-memory'] [data-testid='memory-item'][data-item-id='mem-tone']";

        Assert.AreEqual("queue", await panel.GetAttributeAsync("data-undo-mode"),
            "the demo runs the queue so a reader can take back more than the newest removal");
        Assert.AreEqual("text category", await panel.GetAttributeAsync("data-editable"));

        await page.Locator(units + " [data-testid='memory-item-forget']").ClickAsync();
        await panel.Locator("[data-testid='memory-undo-row']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await page.Locator(tone + " [data-testid='memory-item-forget']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='ai-agents-memory'] [data-testid='memory-undo-row']\").length === 2",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        var rows = await panel.Locator("[data-testid='memory-undo-row']")
            .EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.itemId)");
        CollectionAssert.AreEqual(new[] { "mem-units", "mem-tone" }, rows,
            "one undo row per pending removal, in the order they were removed");

        await ShootAsync(page, "ai-agents-memory-undo-queue-light-1440");

        // The FIRST row, not merely the newest one: the queue exists so any pending removal can be taken back.
        await panel.Locator("[data-testid='memory-undo-row'][data-item-id='mem-units'] [data-testid='memory-undo-action']")
            .ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='ai-agents-memory'] [data-testid='memory-undo-row']\").length === 1"
            + " && !!document.querySelector(\"[data-testid='ai-agents-memory'] [data-item-id='mem-units']\")",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.IsFalse((await TextAsync(page, "ai-agents-memory-log")).Contains("Forget", StringComparison.Ordinal),
            "an undone removal is never reported to the page");

        // The editor carries the category beside the text, and the commit regroups the row.
        await page.Locator(units + " [data-testid='memory-item-edit']").ClickAsync();
        var category = page.Locator(units + " [data-testid='memory-item-edit-category']");
        await category.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await category.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.TypeAsync("People");
        await page.WaitForTimeoutAsync(200);
        Assert.AreEqual("People", await category.InputValueAsync(), "the field carries the draft the reader typed");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-memory'] [data-item-id='mem-units']\")?.dataset.category === 'People'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual(1, await page.Locator(
                "[data-testid='ai-agents-memory'] [data-testid='memory-group'][data-category='People'] "
                + "[data-testid='memory-item'][data-item-id='mem-units']").CountAsync(),
            "the row moved into the People group");
        StringAssert.Contains(await TextAsync(page, "ai-agents-memory-log"), "People",
            "the field-aware edit reached the page as one change");

        await ShootAsync(page, "ai-agents-memory-edit-category-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-memory-section");

        await ToggleThemeAsync(page);
        await ScrollToSection(page, MemorySection);
        await ShootAsync(page, "ai-agents-memory-edit-category-dark-1440");

        // A 375 px card cannot hold five segments: the filter becomes a real select that feeds the same view.
        var phone = await OpenAsync(375, 812, coarsePointer: true);
        await ScrollToSection(phone, MemorySection);
        var phonePanel = phone.Locator("[data-testid='ai-agents-memory']");
        var select = phonePanel.Locator("[data-testid='memory-status-filter-select'] select");
        await select.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.IsTrue(await select.IsVisibleAsync(), "the narrow card shows the filter as a select");
        Assert.IsFalse(await phonePanel.Locator("[data-testid='memory-status-filter-segmented']").IsVisibleAsync(),
            "the scrolling tray is not on screen at 375 px");

        await select.SelectOptionAsync("paused");
        await phone.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='ai-agents-memory'] [data-testid='memory-item']\").length === 1",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual("mem-zone", await phone.Locator(
                "[data-testid='ai-agents-memory'] [data-testid='memory-item']").GetAttributeAsync("data-item-id"),
            "the select narrows the same view the tray does");

        // The filtered view is shorter than the full one, so the card must be framed again before the capture.
        await ScrollToSection(phone, MemorySection);
        await ShootAsync(phone, "ai-agents-memory-filter-select-phone-375");

        // A wide card keeps the segmented tray (the default presentation).
        Assert.IsTrue(await page.Locator(
                "[data-testid='ai-agents-memory'] [data-testid='memory-status-filter-segmented']").IsVisibleAsync(),
            "the tray stays the wide-card presentation");
        Assert.IsFalse(await page.Locator(
                "[data-testid='ai-agents-memory'] [data-testid='memory-status-filter-select']").IsVisibleAsync(),
            "the select is only the narrow-card fallback");
    }
}
