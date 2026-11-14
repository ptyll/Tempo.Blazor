using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan ff043b2e phase 9 — the assembled <c>/analytics-kit</c> page in a real browser. The assertions read
/// behaviour, not markup: the comparison chart is ONE tab stop whose arrow keys walk the buckets and whose
/// single polite live region narrates them, the facet filter applies on every toggle and leaves removable
/// chips (with host-computed counts that re-count the other groups), its phone band is a bottom sheet with a
/// counted confirmation, choosing a preset moves the period AND its comparison window, the segmented trays
/// move with the arrow keys, a 375 px viewport never scrolls sideways, and a scoped axe scan finds no
/// critical or serious violation. Reference screenshots (light and dark, 1440 and 375 px) land in
/// <c>TestResults/analytics-kit</c>.
/// </summary>
public abstract class AnalyticsKitE2EBase : PlaywrightTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "analytics-kit");

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
        await GotoAppReadyAsync(page, $"{BaseUrl}/analytics-kit");
        await page.WaitForSelectorAsync(
            "[data-testid='analytics-kit-kpis'] .tm-stat-card",
            new PageWaitForSelectorOptions { Timeout = 60000 });
        await page.WaitForSelectorAsync(
            "[data-testid='analytics-kit-chart'] .tm-chart__index-zone",
            new PageWaitForSelectorOptions { Timeout = 60000 });
        // The two selectors above are the page's own first paint; a page-wide settle is left to the test that reads
        // geometry, which frames its section first (right after a cold load the page is still re-laying out).
        return page;
    }

    private static ILocator Section(IPage page, string testId) => page.GetByTestId(testId);

    /// <summary>The chart ROOT — it carries the <c>data-index-ordinal</c> state the keyboard test reads.</summary>
    private static ILocator ChartRoot(IPage page)
        => page.Locator("[data-testid='analytics-kit-chart'] .tm-chart");

    /// <summary>The focusable SVG inside the chart root (its box is what a pointer would hover).</summary>
    private static ILocator ChartSvg(IPage page) => ChartRoot(page).Locator("svg");

    private static ILocator FacetTrigger(IPage page)
        => page.Locator("[data-testid='analytics-kit-facets'] .tm-facet-filter__trigger");

    private static ILocator FacetPanel(IPage page)
        => page.Locator("[data-testid='analytics-kit-facets'] [data-testid='facet-filter-panel']");

    private static ILocator FacetSheet(IPage page)
        => page.Locator("[data-testid='analytics-kit-facets'] [data-testid='facet-filter-sheet']");

    private static async Task OpenFacetPanelAsync(IPage page, bool sheet = false)
    {
        await FacetTrigger(page).ScrollIntoViewIfNeededAsync();
        await FacetTrigger(page).ClickAsync();
        var surface = sheet ? FacetSheet(page) : FacetPanel(page);
        await surface.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        // The surface is only usable once its groups and option rows have rendered — that is the state the
        // callers read, so it is what this waits for.
        await Assertions.Expect(surface.Locator("[data-testid='facet-filter-group']")).Not.ToHaveCountAsync(
            0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
    }

    private static async Task<int> ResultCountAsync(IPage page)
    {
        var text = (await page.GetByTestId("analytics-kit-facet-result").TextContentAsync())!.Trim();
        var match = Regex.Match(text, @"([\d\s.,\u00a0\u202f]+)\s*orders");
        Assert.IsTrue(match.Success, $"the result line states a count (was '{text}')");
        return int.Parse(Regex.Replace(match.Groups[1].Value, @"[^\d]", string.Empty), CultureInfo.InvariantCulture);
    }

    /// <summary>The channel rows of the breakdown card, in rendered order.</summary>
    private static Task<string[]> BreakdownOrderAsync(IPage page)
        => page.Locator("[data-testid='analytics-kit-breakdown'] .tm-breakdown-card__row")
            .EvaluateAllAsync<string[]>("rows => rows.map(row => row.getAttribute('data-part-id'))");

    /// <summary>
    /// Plan 7ba95ed6 phase 8 — the comparison chart is NAMED by its section heading through the additive
    /// <c>TmChart.AriaLabel</c>, and the <c>&lt;figure aria-labelledby&gt;</c> wrapper it used to need is
    /// gone. The accessible name now lives on the <c>role="img"</c> SVG itself, which is the single node
    /// both a reader and axe resolve; the default type-based text alternative ("Line chart") stays for
    /// every chart whose host passes nothing.
    /// </summary>
    [TestMethod]
    public async Task Chart_IsNamedByItsHeading_ThroughTheAriaLabelParameter()
    {
        var page = await OpenAsync();

        var svg = ChartSvg(page);
        var heading = (await page.Locator("[data-testid='analytics-kit-chart'] h2").TextContentAsync())!.Trim();
        Assert.IsFalse(string.IsNullOrWhiteSpace(heading), "the section carries a visible heading to name the plot");
        Assert.AreEqual(heading, await svg.GetAttributeAsync("aria-label"),
            "the plot's accessible name is the section heading, passed through TmChart.AriaLabel");
        Assert.AreEqual("img", await svg.GetAttributeAsync("role"));

        Assert.AreEqual(0, await page.Locator("[data-testid='analytics-kit-chart'] figure").CountAsync(),
            "the figure wrapper that only existed to name the plot is gone");
        Assert.AreEqual(0, await page.Locator("[data-testid='analytics-kit-chart'] [aria-labelledby]").CountAsync(),
            "nothing needs aria-labelledby once the SVG names itself");

        // The accessible name resolves the way a screen reader sees it — through the SVG's own aria-label,
        // which axe validates as a real image alternative (not through a wrapper it cannot describe).
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
            """,
            "[data-testid='analytics-kit-chart']");
        Assert.AreEqual(0, violations.Length, "axe [analytics-kit-chart]: " + string.Join(" | ", violations));
    }

    [TestMethod]
    public async Task Chart_ArrowKeysWalkTheBuckets_WithOneLiveRegion()
    {
        var page = await OpenAsync();
        var chart = ChartRoot(page);

        Assert.AreEqual(1, await page.Locator("[data-testid='analytics-kit-chart'] [aria-live]").CountAsync(),
            "the chart renders exactly one live region");
        Assert.AreEqual(0, await page.Locator("[data-testid='analytics-kit-chart'] .tm-chart__crosshair").CountAsync(),
            "nothing is drawn before the plot is explored");

        await ChartSvg(page).FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");

        // The crosshair is an SVG <line>: it paints, but its bounding box has zero width, so
        // Playwright's visibility test never passes for it. The chart root's own ordinal (it is -1
        // until the plot is explored) is the honest signal; the crosshair is then asserted by count.
        await page.WaitForFunctionAsync(
            "() => Number(document.querySelector(\"[data-testid='analytics-kit-chart'] .tm-chart\").getAttribute('data-index-ordinal')) >= 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual(1, await page.Locator("[data-testid='analytics-kit-chart'] .tm-chart__crosshair").CountAsync(),
            "the active index draws the crosshair");

        var first = await chart.GetAttributeAsync("data-index-ordinal");
        Assert.AreEqual("0", first, "the first press lands on the first bucket");

        var card = page.Locator("[data-testid='analytics-kit-chart'] .tm-chart__tooltip--index");
        Assert.AreEqual(1, await card.CountAsync(), "the index card lists every series at the bucket");
        Assert.AreEqual(2, await card.Locator(".tm-chart__tooltip-series").CountAsync(),
            "the page's chart draws the period and its comparison as two series");

        var live = (await page.Locator("[data-testid='analytics-kit-chart'] [aria-live]").TextContentAsync())!.Trim();
        Assert.IsFalse(string.IsNullOrEmpty(live), "the explored bucket is announced");

        await page.Keyboard.PressAsync("ArrowRight");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='analytics-kit-chart'] .tm-chart\").getAttribute('data-index-ordinal') !== '0'",
            null,
            new PageWaitForFunctionOptions { Timeout = 10000 });

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(card).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        Assert.AreEqual(0, await card.CountAsync(), "Escape hides the card");
        Assert.AreEqual(0, await page.Locator("[data-testid='analytics-kit-chart'] .tm-chart__crosshair").CountAsync(),
            "the crosshair follows the card");
    }

    [TestMethod]
    public async Task FacetFilter_AppliesOnEveryToggle_AndItsChipsBringTheNumbersBack()
    {
        var page = await OpenAsync();
        var initial = await ResultCountAsync(page);
        await OpenFacetPanelAsync(page);
        var countsBefore = string.Join(
            "|",
            await page.Locator("[data-testid='analytics-kit-facets'] .tm-facet-filter__option").AllTextContentsAsync());

        var row = FacetPanel(page).Locator("[data-testid='facet-filter-option'][data-option-value='paid']");
        await row.Locator(".tm-checkbox-text").ClickAsync();

        // The applied value becomes a chip (and the result line re-counts): both are render-driven state.
        await Assertions.Expect(page.Locator("[data-testid='analytics-kit-facets'] [data-testid='facet-filter-chip']"))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

        var filtered = await ResultCountAsync(page);
        Assert.IsTrue(filtered < initial,
            $"the paid status is one of four, so it must exclude orders ({filtered} of {initial})");
        Assert.AreEqual(1, await page.Locator("[data-testid='analytics-kit-facets'] [data-testid='facet-filter-chip']").CountAsync(),
            "the picked value is a removable chip");

        var countsAfter = string.Join(
            "|",
            await page.Locator("[data-testid='analytics-kit-facets'] .tm-facet-filter__option").AllTextContentsAsync());
        Assert.IsFalse(string.IsNullOrEmpty(countsBefore), "the open dialog lists the option rows with their counts");
        Assert.AreNotEqual(countsBefore, countsAfter,
            "the counts are host-computed over the other groups, so filtering one group re-counts the rest");

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(FacetPanel(page)).ToHaveCountAsync(
            0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        Assert.AreEqual(0, await FacetPanel(page).CountAsync(), "Escape closes the dialog");

        await page.Locator("[data-testid='analytics-kit-facets'] .tm-filter-chip-remove").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='analytics-kit-facets'] [data-testid='facet-filter-chip']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        // The result line is the host's re-count, so wait for it to report the unfiltered number again.
        await Assertions.Expect(page.GetByTestId("analytics-kit-facet-result")).ToContainTextAsync(
            initial.ToString("N0", CultureInfo.InvariantCulture),
            new LocatorAssertionsToContainTextOptions { Timeout = 5000 });
        Assert.AreEqual(initial, await ResultCountAsync(page), "removing the chip restores the unfiltered period");
    }

    [TestMethod]
    public async Task FacetFilter_PhoneBand_PresentsASheetWithTheCountedConfirmation()
    {
        var page = await OpenAsync(390, 844, touch: true);
        await OpenFacetPanelAsync(page, sheet: true);

        Assert.AreEqual(0, await FacetPanel(page).CountAsync(), "a phone gets the sheet, not a squeezed popover");

        var confirm = page.Locator("[data-testid='analytics-kit-facets'] [data-testid='facet-filter-confirm']");
        StringAssert.Matches((await confirm.TextContentAsync())!.Trim(), new Regex(@"^Show \d+ results$"),
            "the confirmation carries the host's result count");

        var sheetOption = FacetSheet(page)
            .Locator("[data-testid='facet-filter-option'][data-option-value='search']");
        await sheetOption.Locator(".tm-checkbox-text").ClickAsync();
        // The option's own checkbox is the state the click mutates (and the counted confirmation follows it).
        await Assertions.Expect(sheetOption.Locator("input[type='checkbox']")).ToBeCheckedAsync(
            new LocatorAssertionsToBeCheckedOptions { Timeout = 5000 });

        var filtered = await ResultCountAsync(page);
        await confirm.ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='analytics-kit-facets'] [data-testid='facet-filter-sheet']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Detached, Timeout = 10000 });

        Assert.IsTrue(
            (await page.GetByTestId("analytics-kit-facet-applied").TextContentAsync())!.Contains(
                filtered.ToString("N0", CultureInfo.InvariantCulture)),
            "the confirmation reports the number the filter left behind");
    }

    [TestMethod]
    public async Task Period_ChoosingAPresetMovesTheComparisonWindowToo()
    {
        var page = await OpenAsync();
        var picker = page.Locator("[data-testid='analytics-kit-picker'] .tm-date-range-picker");
        var today = DateOnly.FromDateTime(DateTime.Now);

        Assert.AreEqual("previousPeriod", await picker.GetAttributeAsync("data-comparison"));
        Assert.AreEqual(
            today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            await picker.GetAttributeAsync("data-comparison-end"),
            "the comparison window ends the day before the 30-day window starts");

        await page.Locator("[data-testid='analytics-kit-picker'] .tm-date-range-trigger").ClickAsync();
        await page.Locator(".tm-date-range-preset-btn", new PageLocatorOptions { HasTextString = "Today" }).ClickAsync();

        // The picker's own comparison window is the state the assertions read, so wait for the edge it moves.
        await Assertions.Expect(picker).ToHaveAttributeAsync(
            "data-comparison-end",
            today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });

        var end = await picker.GetAttributeAsync("data-comparison-end");
        var start = await picker.GetAttributeAsync("data-comparison-start");
        Assert.AreEqual(today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), end,
            "a one-day period is compared against the day before it");
        Assert.AreEqual(start, end, "the comparison window is one day long");
        StringAssert.Contains((await page.GetByTestId("analytics-kit-comparison-days").TextContentAsync())!,
            "1", "the page reports the comparison window's length");
    }

    [TestMethod]
    public async Task SegmentedTrays_MoveWithTheArrowKeys()
    {
        var page = await OpenAsync();

        // The breakdown's period switcher: the arrow key moves the selection AND the rows re-sort.
        var orderBefore = await BreakdownOrderAsync(page);
        var switcher = page.Locator("[data-testid='analytics-kit-breakdown'] .tm-breakdown-card__period [role='radio']");
        await switcher.First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");

        await Assertions.Expect(switcher.Nth(1)).ToHaveAttributeAsync(
            "aria-checked", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });
        // The rows re-sort with the same render: wait for the rendered ranking to leave the previous one.
        await page.WaitForFunctionAsync(
            """
            (before) => [...document.querySelectorAll("[data-testid='analytics-kit-breakdown'] .tm-breakdown-card__row")]
                .map(row => row.getAttribute('data-part-id')).join('|') !== before
            """,
            string.Join("|", orderBefore),
            new PageWaitForFunctionOptions { Timeout = 5000 });

        // The granularity tray: Home/End jump to the first/last segment.
        var granularity = page.Locator("[data-testid='analytics-kit-granularity'] [role='radio']");
        await granularity.Nth(1).FocusAsync();
        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(granularity.Nth(2)).ToHaveAttributeAsync(
            "aria-checked", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });
        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(granularity.First).ToHaveAttributeAsync(
            "aria-checked", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });

        // The day granularity cuts the same window into more points than the week one.
        var dayPoints = await page.Locator("[data-testid='analytics-kit-chart'] .tm-chart__index-zone").CountAsync();
        await granularity.Nth(1).ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='analytics-kit-chart'] .tm-chart__index-zone"))
            .Not.ToHaveCountAsync(dayPoints, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        var weekPoints = await page.Locator("[data-testid='analytics-kit-chart'] .tm-chart__index-zone").CountAsync();
        Assert.IsTrue(dayPoints > weekPoints, $"the day tray draws more points ({dayPoints} vs {weekPoints})");
    }

    [TestMethod]
    public async Task PhoneBand_NeverScrollsSideways()
    {
        var page = await OpenAsync(375, 812, touch: true);
        await Section(page, "analytics-kit-kpis").ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, "[data-testid='analytics-kit-kpis']");

        var scrollWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(scrollWidth <= 376, $"the page does not scroll sideways (scrollWidth {scrollWidth})");

        foreach (var testId in new[]
                 {
                     "analytics-kit-kpis",
                     "analytics-kit-trend",
                     "analytics-kit-chart",
                     "analytics-kit-breakdown",
                     "analytics-kit-facets",
                     "analytics-kit-usage",
                 })
        {
            var box = await Section(page, testId).BoundingBoxAsync();
            Assert.IsNotNull(box, $"{testId} has a box");
            Assert.IsTrue(box!.Width <= 375, $"{testId} stays inside a 375 px phone (width {box.Width})");
            Assert.IsTrue(box.X >= 0, $"{testId} starts inside the phone (x {box.X})");
        }
    }

    [TestMethod]
    public async Task ScopedAxeScan_HasNoCriticalOrSeriousViolations()
    {
        var page = await OpenAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        foreach (var testId in new[] { "analytics-kit-kpis", "analytics-kit-chart", "analytics-kit-facets" })
        {
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
                """,
                $"[data-testid='{testId}']");
            Assert.AreEqual(0, violations.Length, $"axe [{testId}]: " + string.Join(" | ", violations));
        }
    }

    [TestMethod]
    public async Task VisualSweep_LightAndDark_DesktopAndPhone()
    {
        var desktop = await OpenAsync(1440, 1000);
        await Section(desktop, "analytics-kit-range").ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "analytics-kit-light-1440", "[data-testid='analytics-kit-range']");

        await ToggleThemeAsync(desktop);
        await Section(desktop, "analytics-kit-range").ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "analytics-kit-dark-1440", "[data-testid='analytics-kit-range']");

        var phone = await OpenAsync(375, 812, touch: true);
        await Section(phone, "analytics-kit-kpis").ScrollIntoViewIfNeededAsync();
        await ShootAsync(phone, "analytics-kit-light-375", "[data-testid='analytics-kit-kpis']");

        await ToggleThemeAsync(phone);
        await Section(phone, "analytics-kit-kpis").ScrollIntoViewIfNeededAsync();
        await ShootAsync(phone, "analytics-kit-dark-375", "[data-testid='analytics-kit-kpis']");
    }

    /// <summary>Switches the demo's own theme (the header toggle), so the whole frame repaints, not just a section.</summary>
    private static async Task ToggleThemeAsync(IPage page)
    {
        // The demo renders TWO toggles (the phone header and the sidebar footer); only one is visible per band.
        var before = await page.EvaluateAsync<string?>(
            "() => { const shell = document.querySelector('.demo-shell'); return shell ? `${shell.getAttribute('data-theme')}|${shell.classList.contains('dark')}` : null; }");
        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        // The shell's own theme signature plus the finished colour transition — a fixed pause could screenshot
        // the frame mid-repaint.
        await page.WaitForFunctionAsync(
            """
            (before) => {
                const shell = document.querySelector('.demo-shell');
                return !!shell && `${shell.getAttribute('data-theme')}|${shell.classList.contains('dark')}` !== before;
            }
            """,
            before,
            new PageWaitForFunctionOptions { Timeout = 10000 });
        await WaitForLayoutToSettleAsync(page, ".demo-shell");
    }

    private async Task ShootAsync(IPage page, string name, string selector)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        // The settle reads the real frames (fonts loaded, no CSS transition running, the framed section's box
        // stable) instead of pausing for a guessed interval.
        await WaitForLayoutToSettleAsync(page, selector);
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
public class AnalyticsKitE2ETests : AnalyticsKitE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}

/// <summary>
/// The Server lane (plan 7ba95ed6, Fáze 11): the assembled <c>/analytics-kit</c> scenario over a real Blazor
/// Server circuit. The comparison chart's single tab stop and arrow walk with its one polite live region, the
/// facet filter's toggle/chip path and its phone bottom sheet with the counted confirmation, the preset that
/// moves the period and its comparison window, the segmented trays, the 375 px geometry and the scoped axe scan
/// are all inherited.
/// </summary>
[TestClass]
[TestCategory("Server")]
public class AnalyticsKitServerE2ETests : AnalyticsKitE2EBase
{
    protected override string BaseUrl => "https://localhost:7107";
}

/// <summary>
/// The InteractiveAuto lane: the same assembled page on the host that prerenders over the Server circuit and
/// then hands it to WebAssembly, so the chart walk and the facet sheet are proven across the handover.
/// </summary>
[TestClass]
[TestCategory("InteractiveAuto")]
public class AnalyticsKitInteractiveAutoE2ETests : AnalyticsKitE2EBase
{
    protected override string BaseUrl => "https://localhost:7108";
}
