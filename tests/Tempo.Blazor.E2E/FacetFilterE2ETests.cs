using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan ff043b2e phase 4 — <c>TmFacetFilter</c> in a real browser. The assertions read behaviour:
/// the trigger opens a dialog that is named by its visible heading, a checkbox toggle applies the
/// filter through the chip row (and the chip's own remove button takes it away again), the in-group
/// search narrows the options accent-insensitively, "Show more" reveals the truncation and focuses
/// the first revealed option, a chip removal and clear-all hand focus on instead of dropping it to
/// the page body, Escape closes the popover and hands focus back to the trigger, the phone
/// band shows the bottom sheet with a counted confirmation action, and a scoped axe scan of the new
/// section finds no critical or serious violation. Reference screenshots land in
/// <c>TestResults/facet-filter</c>.
/// </summary>
public abstract class FacetFilterE2EBase : PlaywrightTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "facet-filter");

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private async Task<IPage> OpenAsync(
        int width = 1440,
        int height = 1000,
        bool touch = false,
        bool reduceMotion = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = "en-US",
            ReducedMotion = reduceMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            IgnoreHTTPSErrors = true,
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/data-display");
        await page.WaitForSelectorAsync(
            "[data-testid='facet-filter-section'] .tm-facet-filter",
            new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private static ILocator Section(IPage page) => page.GetByTestId("facet-filter-section");

    private static ILocator PopoverCard(IPage page)
        => Section(page).Locator(".tm-facet-filter[data-presentation='popover']").First;

    private static ILocator InlineCard(IPage page)
        => Section(page).Locator(".tm-facet-filter[data-presentation='inline']").First;

    private static ILocator Trigger(IPage page) => PopoverCard(page).GetByTestId("facet-filter-trigger");

    private static ILocator Panel(IPage page) => PopoverCard(page).GetByTestId("facet-filter-panel");

    /// <summary>
    /// Opens the popover presentation. On the phone band the same trigger opens the core bottom
    /// sheet instead of the anchored panel, so the wait target differs by layout.
    /// </summary>
    private static async Task OpenPopoverAsync(IPage page, bool sheet = false)
    {
        await Trigger(page).ScrollIntoViewIfNeededAsync();
        await Trigger(page).ClickAsync();
        var surface = sheet ? PopoverCard(page).GetByTestId("facet-filter-sheet") : Panel(page);
        await surface.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForTimeoutAsync(250);
    }

    /// <summary>The id of the element that currently owns focus (empty when focus sits on the body).</summary>
    private static Task<string> FocusedIdAsync(IPage page)
        => page.EvaluateAsync<string>("() => (document.activeElement && document.activeElement.id) || ''");

    /// <summary>
    /// Waits for the component's focus handoff and pins it. Every handoff target carries a stable
    /// <c>id</c>, so the assertion reads exactly the element WCAG 2.4.3 is about — a control that
    /// replaced the one the gesture removed, never the page body.
    /// </summary>
    private static async Task AssertFocusedAsync(IPage page, string expectedId, string because)
    {
        Assert.IsFalse(string.IsNullOrEmpty(expectedId), "the handoff target must expose a DOM id");
        await page.WaitForFunctionAsync(
            "id => document.activeElement && document.activeElement.id === id",
            expectedId,
            new PageWaitForFunctionOptions { Timeout = 5000 });
        Assert.AreEqual(expectedId, await FocusedIdAsync(page), because);
    }

    [TestMethod]
    public async Task Popover_OpensAsANamedDialog_AndListsGroupsWithCounts()
    {
        var page = await OpenAsync();

        Assert.AreEqual("false", await Trigger(page).GetAttributeAsync("aria-expanded"));
        await OpenPopoverAsync(page);

        Assert.AreEqual("true", await Trigger(page).GetAttributeAsync("aria-expanded"),
            "the trigger reports the open state");
        Assert.AreEqual("dialog", await Panel(page).GetAttributeAsync("role"));

        var labelledBy = await Panel(page).GetAttributeAsync("aria-labelledby");
        Assert.IsFalse(string.IsNullOrEmpty(labelledBy), "a dialog must be named by its visible heading");
        Assert.AreEqual("Filters", (await page.Locator($"#{labelledBy}").TextContentAsync())!.Trim());

        Assert.AreEqual(3, await Panel(page).GetByTestId("facet-filter-group").CountAsync());
        // Rendered rows: every Status option (3), the truncated Category group (MaxVisible=4) and
        // no Availability row (that group starts collapsed) — the demo's deliberate mix.
        Assert.AreEqual(7, await Panel(page).Locator("[data-testid='facet-filter-option']").CountAsync(),
            "the visible groups render their options, truncated and collapsed ones included in the count");
        Assert.AreEqual(1, await Panel(page).Locator("input[type='checkbox'][disabled]").CountAsync(),
            "the demo disables one option");
        Assert.AreEqual(1, await Panel(page).GetByTestId("facet-filter-search").CountAsync(),
            "only the long group gets a search box");

        // The trigger hugs its label instead of stretching to the host card's width.
        var triggerBox = await Trigger(page).BoundingBoxAsync();
        var cardBox = await PopoverCard(page).BoundingBoxAsync();
        Assert.IsNotNull(triggerBox);
        Assert.IsNotNull(cardBox);
        Assert.IsTrue(triggerBox!.Width < 200,
            $"the trigger hugs its label instead of filling the card (trigger={triggerBox.Width:0.#}, card={cardBox.Width:0.#})");

        // The panel is a column: the group list scrolls inside it, so the footer with the primary
        // action stays reachable however long the groups are.
        var footerBox = await Panel(page).GetByTestId("facet-filter-confirm").BoundingBoxAsync();
        var panelBox = await Panel(page).BoundingBoxAsync();
        Assert.IsNotNull(footerBox);
        Assert.IsNotNull(panelBox);
        Assert.IsTrue(footerBox!.Y + footerBox.Height <= panelBox!.Y + panelBox.Height + 1,
            $"the confirmation action stays inside the panel (footer bottom={footerBox.Y + footerBox.Height:0.#}, panel bottom={panelBox.Y + panelBox.Height:0.#})");
        Assert.IsTrue(footerBox.Y > panelBox.Y + 60, "the footer sits below the scrolling body");
    }

    [TestMethod]
    public async Task TogglingAnOption_AddsAChip_AndTheChipRemoveTakesItAway()
    {
        var page = await OpenAsync();
        await OpenPopoverAsync(page);

        // The demo opens with Status = Active only, so "Archived" is a free value. A reader taps
        // the row (the label), not the visually hidden native input.
        var row = Panel(page).Locator("[data-testid='facet-filter-option'][data-option-value='archived']");
        Assert.AreEqual(1, await row.CountAsync(), "the Status group renders its Archived option");

        var input = row.Locator("input[type='checkbox']");
        Assert.IsFalse(await input.IsCheckedAsync(), "Archived starts unchecked");

        await row.Locator(".tm-checkbox-text").ClickAsync();
        await Assertions.Expect(input).ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions { Timeout = 5000 });
        Assert.IsTrue(await input.IsCheckedAsync(), "a click on the option row toggles its checkbox");

        var chipTexts = await PopoverCard(page).Locator(".tm-facet-filter__chip").AllTextContentsAsync();
        Assert.IsTrue(chipTexts.Any(text => text.Contains("Archived")),
            "the value becomes a removable chip (chips: " + string.Join(" | ", chipTexts) + ")");

        // The open dialog is modal (the background is inert), so the page-level chip row is
        // unreachable while it is up — a reader closes it first, which is what this does.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Panel(page)).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        Assert.AreEqual(0, await Panel(page).CountAsync(), "Escape closes the dialog");

        var archivedChip = PopoverCard(page).Locator(".tm-facet-filter__chip")
            .Filter(new LocatorFilterOptions { HasText = "Archived" });
        await archivedChip.Locator(".tm-filter-chip-remove").ClickAsync();

        await page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll(\"[data-testid='facet-filter-section'] .tm-facet-filter[data-presentation='popover'] .tm-facet-filter__chip\")].every(c => !c.textContent.includes('Archived'))");

        var after = await PopoverCard(page).Locator(".tm-facet-filter__chip").AllTextContentsAsync();
        Assert.IsFalse(after.Any(text => text.Contains("Archived")), "the chip's own remove takes the value away");
        Assert.IsTrue(after.Any(text => text.Contains("Active")), "the other filter stays");

        // The remove button that was clicked has left the DOM: focus must land on the remove button
        // of the chip that took its place, not on the page body (WCAG 2.4.3).
        var remainingRemoveId = await PopoverCard(page).Locator(".tm-facet-filter__chip")
            .First.Locator(".tm-filter-chip-remove").GetAttributeAsync("id");
        await AssertFocusedAsync(page, remainingRemoveId!,
            "removing a chip hands focus to the next chip's remove button");
        await OpenPopoverAsync(page);
        Assert.IsFalse(await Panel(page).Locator("[data-testid='facet-filter-option'][data-option-value='archived'] input[type='checkbox']").IsCheckedAsync(),
            "the removed value is unchecked again");
    }

    [TestMethod]
    public async Task GroupSearch_NarrowsTheOptions_AccentInsensitively()
    {
        var page = await OpenAsync();
        await OpenPopoverAsync(page);

        var categoryGroup = Panel(page).GetByTestId("facet-filter-group").Nth(1);
        var search = categoryGroup.GetByTestId("facet-filter-search");
        Assert.AreEqual("Search Category", await search.GetAttributeAsync("aria-label"),
            "the box is named by its group, so two boxes stay distinguishable");

        var all = await categoryGroup.Locator("[data-testid='facet-filter-option']").CountAsync();
        await search.FillAsync("gard");

        await Assertions.Expect(categoryGroup.Locator("[data-testid='facet-filter-option']"))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        Assert.AreEqual(1, await categoryGroup.Locator("[data-testid='facet-filter-option']").CountAsync(),
            $"the filter narrows the group (was {all} options)");
        Assert.AreEqual("garden", await categoryGroup.Locator("[data-testid='facet-filter-option']").First
            .GetAttributeAsync("data-option-value"));

        await search.FillAsync("nikde");
        await Assertions.Expect(categoryGroup.Locator("[data-testid='facet-filter-option']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        Assert.AreEqual(0, await categoryGroup.Locator("[data-testid='facet-filter-option']").CountAsync());
        Assert.AreEqual(1, await categoryGroup.Locator(".tm-facet-filter__no-results").CountAsync(),
            "an empty match says so instead of rendering nothing");
    }

    [TestMethod]
    public async Task ShowMore_RevealsTheTruncatedOptions()
    {
        var page = await OpenAsync();
        await OpenPopoverAsync(page);

        var categoryGroup = Panel(page).GetByTestId("facet-filter-group").Nth(1);
        Assert.AreEqual(4, await categoryGroup.Locator("[data-testid='facet-filter-option']").CountAsync(),
            "the demo truncates the long group at MaxVisible=4");

        var showMore = categoryGroup.GetByTestId("facet-filter-show-more");
        StringAssert.Matches((await showMore.TextContentAsync())!.Trim(), new System.Text.RegularExpressions.Regex(@"^Show \d+ more$"));

        await showMore.ClickAsync();

        // Wait on the state the disclosure changes, never on a fixed delay: this click is a circuit round
        // trip on the Server and InteractiveAuto lanes and a 200 ms sleep was not enough there.
        await Assertions.Expect(categoryGroup.Locator("[data-testid='facet-filter-option']"))
            .ToHaveCountAsync(7, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        Assert.AreEqual(7, await categoryGroup.Locator("[data-testid='facet-filter-option']").CountAsync());
        Assert.AreEqual(0, await categoryGroup.GetByTestId("facet-filter-show-more").CountAsync());

        // "Show more" removes itself, so focus moves to the checkbox of the first option it revealed
        // (the demo truncates at MaxVisible=4, so that is the fifth row) — WCAG 2.4.3.
        var revealed = categoryGroup.Locator("[data-testid='facet-filter-option']").Nth(4);
        Assert.AreEqual("clothing", await revealed.GetAttributeAsync("data-option-value"),
            "the fifth row is the first option the truncation used to hide");
        var revealedInputId = await revealed.Locator("input[type='checkbox']").GetAttributeAsync("id");
        await AssertFocusedAsync(page, revealedInputId!,
            "the revealed option's checkbox owns focus after the disclosure");
    }

    [TestMethod]
    public async Task ClearAll_EmptiesTheChipRow()
    {
        var page = await OpenAsync();

        Assert.IsTrue(await PopoverCard(page).Locator(".tm-facet-filter__chip").CountAsync() >= 1);
        await PopoverCard(page).GetByTestId("facet-filter-clear-all").ClickAsync();

        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='facet-filter-section'] .tm-facet-filter[data-presentation='popover'] .tm-facet-filter__chip\").length === 0");
        Assert.AreEqual(0, await PopoverCard(page).Locator(".tm-facet-filter__chip").CountAsync(),
            "the clear-all action removes every value");

        // The whole chip row (including the button that held focus) is gone: focus returns to the
        // trigger instead of the page body (WCAG 2.4.3).
        var triggerId = await Trigger(page).GetAttributeAsync("id");
        await AssertFocusedAsync(page, triggerId!,
            "clearing every chip hands focus back to the trigger");
    }

    [TestMethod]
    public async Task Escape_ClosesThePopover_AndHandsFocusBackToTheTrigger()
    {
        var page = await OpenAsync();
        await OpenPopoverAsync(page);

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Panel(page)).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

        Assert.AreEqual("false", await Trigger(page).GetAttributeAsync("aria-expanded"));
        Assert.AreEqual(0, await Panel(page).CountAsync(), "the dialog unmounts");

        var focusedTestId = await page.EvaluateAsync<string?>(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid')");
        Assert.AreEqual("facet-filter-trigger", focusedTestId,
            "focus returns to the trigger that opened the surface");
    }

    [TestMethod]
    public async Task Keyboard_ReachesTheGroupsAndTogglesACheckboxWithSpace()
    {
        var page = await OpenAsync();
        await OpenPopoverAsync(page);

        // The panel is an APG dialog: the first Tab moves to the first control inside it.
        var firstToggle = Panel(page).GetByTestId("facet-filter-group-toggle").First;
        await firstToggle.FocusAsync();
        Assert.AreEqual("true", await firstToggle.GetAttributeAsync("aria-expanded"));

        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(firstToggle).ToHaveAttributeAsync(
            "aria-expanded", "false", new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });
        Assert.AreEqual("false", await firstToggle.GetAttributeAsync("aria-expanded"),
            "a native button collapses its group on Enter");

        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(firstToggle).ToHaveAttributeAsync(
            "aria-expanded", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });

        var checkbox = Panel(page).Locator("[data-testid='facet-filter-option'] input[type='checkbox']").First;
        await checkbox.FocusAsync();
        var checkedBefore = await checkbox.IsCheckedAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(checkbox).ToBeCheckedAsync(
            new LocatorAssertionsToBeCheckedOptions { Checked = !checkedBefore, Timeout = 5000 });
        Assert.AreNotEqual(checkedBefore, await checkbox.IsCheckedAsync(), "Space toggles the native checkbox");
    }

    [TestMethod]
    public async Task PhoneBand_PresentsTheBottomSheet_WithACountedConfirmation()
    {
        var page = await OpenAsync(390, 844, touch: true);
        await OpenPopoverAsync(page, sheet: true);

        Assert.AreEqual(0, await Panel(page).CountAsync(), "a phone gets the sheet, not a squeezed popover");

        var sheet = PopoverCard(page).GetByTestId("facet-filter-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var confirm = PopoverCard(page).GetByTestId("facet-filter-confirm");
        StringAssert.Matches((await confirm.TextContentAsync())!.Trim(),
            new System.Text.RegularExpressions.Regex(@"^Show \d+ results$"));

        // The sheet keeps the whole body reachable (groups AND the footer action).
        Assert.AreEqual(3, await PopoverCard(page).GetByTestId("facet-filter-group").CountAsync());
        Assert.AreEqual(1, await PopoverCard(page).GetByTestId("facet-filter-panel-clear-all").CountAsync());

        var box = await confirm.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box!.Height >= 44, $"a finger gets the 44 px target (height={box.Height})");
    }

    [TestMethod]
    public async Task ReducedMotion_KeepsTheChevronStill_AndTheFilterWorking()
    {
        var page = await OpenAsync(reduceMotion: true);
        await OpenPopoverAsync(page);

        var chevron = Panel(page).Locator(".tm-facet-filter__chevron").First;
        // Chromium normalizes the clamped duration (0.01ms) to seconds in getComputedStyle.
        var duration = await chevron.EvaluateAsync<string>(
            "el => getComputedStyle(el).transitionDuration");
        Assert.IsTrue(duration is "0.01ms" or "1e-05s" or "0s",
            $"reduced motion clamps the chevron transition (was {duration})");

        var before = await Panel(page).Locator("[data-testid='facet-filter-option']").CountAsync();
        await Panel(page).GetByTestId("facet-filter-group-toggle").Nth(2).ClickAsync();
        await Assertions.Expect(Panel(page).Locator("[data-testid='facet-filter-option']")).Not.ToHaveCountAsync(
            before, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        Assert.AreNotEqual(before, await Panel(page).Locator("[data-testid='facet-filter-option']").CountAsync(),
            "the disclosure still works without the transition");
    }

    [TestMethod]
    public async Task InlinePresentation_ShowsTheGroupsWithoutATrigger()
    {
        var page = await OpenAsync();

        Assert.AreEqual(0, await InlineCard(page).GetByTestId("facet-filter-trigger").CountAsync());
        Assert.AreEqual(3, await InlineCard(page).GetByTestId("facet-filter-group").CountAsync());
        Assert.AreEqual("Refine", (await InlineCard(page).Locator(".tm-facet-filter__title").TextContentAsync())!.Trim());

        // The inline card opens with one chip and its own clear-all removes it.
        Assert.AreEqual(1, await InlineCard(page).Locator(".tm-facet-filter__chip").CountAsync());
        await InlineCard(page).GetByTestId("facet-filter-clear-all").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='facet-filter-section'] .tm-facet-filter[data-presentation='inline'] .tm-facet-filter__chip\").length === 0");
    }

    [TestMethod]
    public async Task Axe_Scoped_To_TheSection_FindsNoCriticalOrSeriousViolation()
    {
        var page = await OpenAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        await AssertNoAxeViolationsAsync(page, "[data-testid='facet-filter-section']", "closed");

        await OpenPopoverAsync(page);
        await AssertNoAxeViolationsAsync(page, "[data-testid='facet-filter-panel']", "popover open");
    }

    [TestMethod]
    public async Task Visual_Reference_LightDark_At_1440_And_375()
    {
        var desktop = await OpenAsync(1440);
        await Section(desktop).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(desktop, "[data-testid='facet-filter-section']");
        await ShootAsync(desktop, "facet-filter-light-1440");

        await OpenPopoverAsync(desktop);
        await ShootAsync(desktop, "facet-filter-popover-light-1440");
        await desktop.Keyboard.PressAsync("Escape");
        await desktop.WaitForTimeoutAsync(300);

        await ToggleDarkModeAsync(desktop);
        await Section(desktop).ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "facet-filter-dark-1440");

        await OpenPopoverAsync(desktop);
        await ShootAsync(desktop, "facet-filter-popover-dark-1440");
        await desktop.Keyboard.PressAsync("Escape");
        await desktop.WaitForTimeoutAsync(300);

        var phone = await OpenAsync(375, 812, touch: true);
        await Section(phone).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(phone, "[data-testid='facet-filter-section']");
        await ShootAsync(phone, "facet-filter-light-375");

        await OpenPopoverAsync(phone, sheet: true);
        await ShootAsync(phone, "facet-filter-sheet-light-375");
    }

    private static async Task AssertNoAxeViolationsAsync(IPage page, string selector, string state)
    {
        var violations = await page.EvaluateAsync<string[]>(
            """
            async (selector) => {
                const host = document.querySelector(selector);
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).slice(0, 4).join('; ')})`);
            }
            """, selector);
        Assert.AreEqual(0, violations.Length, $"axe [{state}]: " + string.Join(" | ", violations));
    }

    private async Task ShootAsync(IPage page, string name)
    {
        await page.WaitForTimeoutAsync(300);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, $"{name}.png"),
            FullPage = false,
        });
    }
}

/// <summary>The WASM lane (the demo default).</summary>
[TestClass]
[TestCategory("WASM")]
public class FacetFilterE2ETests : FacetFilterE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}

/// <summary>
/// The Server lane (plan 7ba95ed6, Fáze 11): the same <c>TmFacetFilter</c> over a real Blazor Server circuit.
/// The anchored dialog popover (open, Escape, focus return), the counted confirmation, the chip row with its
/// own remove/clear-all focus hand-off, the in-group search, "Show more", the phone band's bottom sheet and
/// the single polite live region are all inherited, so a circuit round trip cannot hide a focus regression.
/// </summary>
[TestClass]
[TestCategory("Server")]
public class FacetFilterServerE2ETests : FacetFilterE2EBase
{
    protected override string BaseUrl => "https://localhost:7107";
}

/// <summary>
/// The InteractiveAuto lane: the same facet filter on the host that starts over the Server circuit and finishes
/// on WebAssembly, covering the popover, the sheet and the chip row across the runtime handover.
/// </summary>
[TestClass]
[TestCategory("InteractiveAuto")]
public class FacetFilterInteractiveAutoE2ETests : FacetFilterE2EBase
{
    protected override string BaseUrl => "https://localhost:7108";
}
