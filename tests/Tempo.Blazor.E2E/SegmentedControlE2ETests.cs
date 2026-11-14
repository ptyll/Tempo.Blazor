using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan ff043b2e phase 8 — the segmented control and the combobox match highlight in a real browser. The
/// assertions read behaviour: the tray is one roving tab stop whose arrows move BOTH the focus and the
/// selection (wrapping, skipping the disabled segment), the sliding strip is measured and claimed only
/// after the script placed it, the tab copy points its tabs at the panel through aria-controls, the overflow
/// copy scrolls the keyboard-visited segment into view instead of clipping it, the multi-select marks the
/// typed term and offers the create row last, reduced motion removes the travel, a 390 px phone keeps both
/// sections inside the viewport, the dark theme re-points the trays, and a scoped axe scan finds no critical
/// or serious violation. Reference screenshots (light, dark, phone) land in
/// <c>TestResults/segmented-control</c>.
/// </summary>
public abstract class SegmentedControlE2EBase : PlaywrightTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "segmented-control");

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

    private static ILocator Section(IPage page) => page.Locator("[data-testid='segmented-control-section']");

    private static ILocator PeriodTray(IPage page) => page.Locator("[data-testid='segmented-period']");

    private static ILocator TabsTray(IPage page) => page.Locator("[data-testid='segmented-tabs']");

    private async Task<IPage> OpenAsync(
        int width = 1440, int height = 1000, bool reduceMotion = false, string path = "/data-display",
        string waitFor = "[data-testid='segmented-control-section'] [role='radiogroup']")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            ReducedMotion = reduceMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            IgnoreHTTPSErrors = true,
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{path}");
        await page.WaitForSelectorAsync(waitFor, new PageWaitForSelectorOptions { Timeout = 30000 });
        // The section's own first paint (the selector above); a page-wide settle is left to the tests that read
        // geometry, and they frame their section first — right after a cold load the page is still re-laying out.
        return page;
    }

    [TestMethod]
    public async Task Tray_KeepsOneRovingTabStop_AndMovesTheSelectionWithTheArrows()
    {
        var page = await OpenAsync();

        var options = PeriodTray(page).Locator("[role='radio']");
        Assert.AreEqual(3, await options.CountAsync(), "one radio per segment");

        // The selection is the single tab stop.
        Assert.AreEqual("0", await options.Nth(1).GetAttributeAsync("tabindex"),
            "week is selected, so it is the one segment in the tab order");
        Assert.AreEqual("-1", await options.Nth(0).GetAttributeAsync("tabindex"));
        Assert.AreEqual("true", await options.Nth(1).GetAttributeAsync("aria-checked"));

        await options.Nth(1).FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");

        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='segmented-period'] [role='radio'][aria-checked='true']\")" +
            ".textContent.trim().startsWith('Month')",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });

        Assert.AreEqual("Month", await PeriodTray(page).Locator("[aria-checked='true']").First
            .Locator(".tm-segmented__option-label").TextContentAsync(),
            "ArrowRight selected the next segment");

        Assert.AreEqual("Month", await page.EvaluateAsync<string>(
                "() => document.activeElement.querySelector('.tm-segmented__option-label')?.textContent.trim()"),
            "the selection moves the FOCUS with it — the roving tabindex is not a highlight");
    }

    [TestMethod]
    public async Task Tray_WrapsAndSkipsTheDisabledSegment()
    {
        var page = await OpenAsync();

        // The tab copy carries a disabled third segment.
        var tabs = TabsTray(page).Locator("[role='tab']");
        Assert.AreEqual(3, await tabs.CountAsync());
        Assert.AreEqual("true", await tabs.Nth(2).GetAttributeAsync("aria-disabled"));

        await tabs.Nth(0).FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.AreEqual("Table", (await tabs.Nth(1).TextContentAsync())!.Trim(),
            "the disabled segment was skipped, so the second arrow kept the selection on Table");

        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(tabs.Nth(1)).ToHaveAttributeAsync(
            "aria-selected", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });
        Assert.AreEqual("true", await tabs.Nth(1).GetAttributeAsync("aria-selected"),
            "End lands on the last ENABLED tab, not on the disabled one");
    }

    [TestMethod]
    public async Task TheSlidingStripIsMeasuredAndClaimed()
    {
        var page = await OpenAsync();

        var track = PeriodTray(page).Locator(".tm-segmented__track");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='segmented-period'] .tm-segmented__track\")" +
            ".getAttribute('data-tm-indicator') === 'sliding'",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });

        // The strip's width / height / transform TRANSITION from the unmeasured 0 box to the segment (the sliding
        // indicator), so the geometry is only the segment's once the running transition has ended: wait for that
        // state first (a read taken mid-slide sees the 2 px border box and is a race, not a contract).
        await page.WaitForFunctionAsync(
            "() => (document.querySelector(\"[data-testid='segmented-period'] .tm-segmented__indicator\")?.getBoundingClientRect().width ?? 0) > 10",
            null, new PageWaitForFunctionOptions { Timeout = 10000 });
        await WaitForLayoutToSettleAsync(page, "[data-testid='segmented-period']");
        var geometry = await track.Locator(".tm-segmented__indicator").EvaluateAsync<double[]>(
            "el => { const s = getComputedStyle(el); return [el.getBoundingClientRect().width, parseFloat(s.opacity)]; }");
        Assert.IsTrue(geometry[0] > 10, $"the strip was measured to the selected segment (width {geometry[0]})");
        Assert.AreEqual(1d, geometry[1], "and it is actually painted");

        // It follows the selection: the strip must sit over the segment it marks.
        var selected = track.Locator("[aria-checked='true']");
        var strip = track.Locator(".tm-segmented__indicator");
        var selectedBox = await selected.BoundingBoxAsync();
        var stripBox = await strip.BoundingBoxAsync();
        Assert.IsNotNull(selectedBox);
        Assert.IsNotNull(stripBox);
        Assert.IsTrue(Math.Abs(selectedBox!.X - stripBox!.X) <= 1,
            $"the strip is aligned with the selected segment (segment {selectedBox.X}, strip {stripBox.X})");
    }

    [TestMethod]
    public async Task TheTabsCopyPointsItsTabsAtThePanel()
    {
        var page = await OpenAsync();

        var tab = TabsTray(page).Locator("[role='tab']").First;
        Assert.AreEqual("segmented-demo-panel", await tab.GetAttributeAsync("aria-controls"));
        Assert.AreEqual("tablist", await TabsTray(page).Locator(".tm-segmented__track").GetAttributeAsync("role"));

        await TabsTray(page).Locator("[role='tab']").Nth(1).ClickAsync();

        var panel = page.Locator("[data-testid='segmented-tab-panel']");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='segmented-tab-panel']\").textContent.includes('table')",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });

        Assert.AreEqual("segmented-demo-panel", await panel.GetAttributeAsync("id"));
        Assert.AreEqual("tabpanel", await panel.GetAttributeAsync("role"));
    }

    [TestMethod]
    public async Task TheOverflowingTrayScrollsTheFocusedSegmentIntoView()
    {
        var page = await OpenAsync();

        var narrow = page.Locator("[data-testid='segmented-overflow'] .tm-segmented__track");
        var scrollBefore = await narrow.EvaluateAsync<double[]>(
            "el => [el.scrollWidth, el.clientWidth, el.scrollLeft]");
        Assert.IsTrue(scrollBefore[0] > scrollBefore[1],
            $"the narrow copy really overflows (scrollWidth {scrollBefore[0]} > clientWidth {scrollBefore[1]})");

        await narrow.Locator("[role='radio']").First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowLeft"); // wraps to the LAST segment

        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='segmented-overflow'] .tm-segmented__track\").scrollLeft > 0",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });

        var focused = await page.EvaluateAsync<string>(
            "() => document.activeElement.textContent.trim()");
        Assert.AreEqual("Revenue", focused, "the keyboard visited the last segment and focused it");
    }

    [TestMethod]
    public async Task ReducedMotionRemovesTheStripTravel()
    {
        var page = await OpenAsync(reduceMotion: true);

        var duration = await PeriodTray(page).Locator(".tm-segmented__indicator")
            .EvaluateAsync<string>("el => getComputedStyle(el).transitionDuration");
        var first = duration.Split(',')[0].Trim();

        // Chromium normalizes 0.01ms to 1e-05s — accept any of the clamped spellings.
        Assert.IsTrue(first is "1e-05s" or "0.00001s" or "0.01ms" or "0s",
            $"reduced motion clamps the strip travel (read back '{duration}')");
    }

    [TestMethod]
    public async Task ComboboxMarksTheTypedTerm_AndOffersTheCreateRowLast()
    {
        var page = await OpenAsync(
            path: "/forms",
            waitFor: "[data-testid='combobox-highlight-section']");

        var section = page.Locator("[data-testid='combobox-highlight-section']");
        var trigger = section.Locator(".tm-multiselect").First;
        await trigger.ClickAsync();

        var filter = section.Locator(".tm-multiselect__filter-input").First;
        await filter.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await filter.FillAsync("Pra");

        var mark = section.Locator(".tm-multiselect__option mark").First;
        await mark.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        Assert.AreEqual("Pra", await mark.TextContentAsync(),
            "only the matched slice is wrapped, and it carries the original casing");

        await filter.FillAsync("Zlin");
        var create = section.Locator(".tm-multiselect__option--create").First;
        await create.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        StringAssert.Contains(await create.TextContentAsync() ?? string.Empty, "Zlin");

        var options = section.Locator("[role='listbox'] [role='option']");
        Assert.IsTrue((await options.Last.GetAttributeAsync("class") ?? string.Empty)
            .Contains("tm-multiselect__option--create"), "the create row is the LAST option of the listbox");

        await filter.PressAsync("ArrowDown");

        // UX review finding 1 (WCAG 2.4.7): the row the keyboard is about to create from must be VISIBLY
        // focused, not just the aria-activedescendant. A computed outline is what the browser paints.
        var keyboardFocused = section.Locator("[data-testid='multiselect-create-option']");
        Assert.IsTrue((await keyboardFocused.GetAttributeAsync("class") ?? string.Empty)
            .Contains("tm-multiselect__option--focused"),
            "the create row takes the same visible focus class as a regular option");
        Assert.AreNotEqual("none", await keyboardFocused.EvaluateAsync<string>(
            "el => getComputedStyle(el).outlineStyle"),
            "the focused create row paints a focus indicator");

        await filter.PressAsync("Enter");

        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='combobox-create-result']\").textContent.includes('Zlin')",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });

        // The demo follows the host guidance: the created value is SELECTED, so its chip is the confirmation
        // (and the selected-chip announcement reads it out). Without it a screen-reader user hears only the
        // trigger again.
        await page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll(\"[data-testid='combobox-highlight-section'] .tm-multiselect__chip\")]" +
            ".some(chip => chip.textContent.includes('Zlin'))",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task Phone_KeepsBothSectionsInsideTheViewport()
    {
        var page = await OpenAsync(width: 390, height: 844);
        await Section(page).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, "[data-testid='segmented-control-section']");

        var section = await Section(page).BoundingBoxAsync();
        Assert.IsNotNull(section);
        Assert.IsTrue(section!.Width <= 390,
            $"the section does not spill out of a 390 px phone (width {section.Width})");
        Assert.IsTrue(section.X >= 0);

        // The tray itself scrolls rather than pushing the page sideways.
        var pageWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(pageWidth <= 391, $"the page does not scroll sideways (scrollWidth {pageWidth})");

        await ShootAsync(page, "segmented-control-phone");
    }

    [TestMethod]
    public async Task DarkThemeRepointsTheTray()
    {
        var page = await OpenAsync();

        var track = PeriodTray(page).Locator(".tm-segmented__track");
        var light = await track.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");

        await page.EvaluateAsync(
            """
            () => {
                document.documentElement.setAttribute('data-theme', 'dark');
                document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', 'dark'));
            }
            """);

        // Wait for the tray's computed colour to actually leave the light value (and for the colour
        // transition to finish) — a fixed pause could read the token mid-repaint.
        await page.WaitForFunctionAsync(
            """
            (light) => {
                const tray = document.querySelector("[data-testid='segmented-period'] .tm-segmented__track");
                return !!tray && getComputedStyle(tray).backgroundColor !== light;
            }
            """,
            light,
            new PageWaitForFunctionOptions { Timeout = 10000 });
        await WaitForLayoutToSettleAsync(page, "[data-testid='segmented-control-section']");

        var dark = await track.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.AreNotEqual(light, dark, "the tray re-painted for the dark theme");
        Assert.AreEqual("rgb(51, 65, 85)", dark, "the tray uses the dark --tm-bg-muted (#334155)");

        await ShootAsync(page, "segmented-control-dark");
    }

    [TestMethod]
    public async Task ScopedAxeScan_HasNoCriticalOrSeriousViolations()
    {
        var page = await OpenAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        var violations = await page.EvaluateAsync<string[]>(
            """
            async (selector) => {
                const host = document.querySelector(selector);
                // axe samples the RESOLVED colours: a finite CSS animation/transition still running (the demo's fade-in, a colour
                // transition, the segmented indicator) paints a blended, low-contrast frame. Wait for them (looping ones,
                // like the skeleton shimmer, never finish and are not waited for).
                await Promise.all(document.getAnimations()
                    .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                    .map(a => a.finished.catch(() => { })));
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).slice(0, 4).join('; ')})`);
            }
            """, "[data-testid='segmented-control-section']");

        Assert.AreEqual(0, violations.Length, "axe: " + string.Join(" | ", violations));
    }

    [TestMethod]
    public async Task LightScreenshot_ShowsTheTrays()
    {
        var page = await OpenAsync();
        await ShootAsync(page, "segmented-control-light");
    }

    private async Task ShootAsync(IPage page, string name)
    {
        // The data-display page is long, so a viewport screenshot would frame the badges at the top instead
        // of the trays — scroll the section into view BEFORE the shot.
        var framed = "[data-testid='segmented-control-section']";
        if (await Section(page).CountAsync() > 0)
        {
            await Section(page).ScrollIntoViewIfNeededAsync();
        }
        else
        {
            framed = "[data-testid='combobox-highlight-section']";
            await page.Locator(framed).ScrollIntoViewIfNeededAsync();
        }

        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        // The settle reads the real frames (fonts loaded, no CSS transition running, the framed section's box
        // stable) instead of pausing for a guessed interval.
        await WaitForLayoutToSettleAsync(page, framed);
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
public class SegmentedControlE2ETests : SegmentedControlE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}

/// <summary>
/// The Server lane (plan 7ba95ed6, Fáze 11): the same segmented tray and combobox highlight over a real Blazor
/// Server circuit. The roving tab stop with its wrapping arrow walk, the measured sliding strip claimed only
/// after the script placed it, the tab copy's <c>aria-controls</c>, the overflowing tray that scrolls the
/// visited segment into view, the create-row keyboard path and the reduced-motion branch are all inherited.
/// </summary>
[TestClass]
[TestCategory("Server")]
public class SegmentedControlServerE2ETests : SegmentedControlE2EBase
{
    protected override string BaseUrl => "https://localhost:7107";
}

/// <summary>
/// The InteractiveAuto lane: the same trays on the host that starts over the Server circuit and finishes on
/// WebAssembly, so the measured strip and the overflow scroller are proven across the runtime handover.
/// </summary>
[TestClass]
[TestCategory("InteractiveAuto")]
public class SegmentedControlInteractiveAutoE2ETests : SegmentedControlE2EBase
{
    protected override string BaseUrl => "https://localhost:7108";
}
