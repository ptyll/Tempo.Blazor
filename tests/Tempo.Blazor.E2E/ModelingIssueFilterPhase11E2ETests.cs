using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 11 E2E for the modeling issue FILTER BAR across all three bands: the desktop dock, the
/// tablet side sheet and the mobile Issues tab. The bar turns on with the editor
/// (<c>ShowFilterBar="true"</c>), the filter/sort state is the EDITOR's (it survives a dock
/// collapse, a sheet close/reopen and a tab switch), the chips filter by severity with counts,
/// the text matches message/category/source name case-insensitively, the sort select re-orders
/// and the 500-issue scenario answers a filter inside the phase budget. Screenshots per band
/// are reviewed against output/imagegen/tempo-reimagined/Tempo.Blazor.Modeling/
/// modeling-issue-panel--{desktop,tablet,mobile}.png.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingIssueFilterPhase11E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    // ── Desktop dock ─────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Desktop dock: severity chips show counts and filter, the text matches the source name, the sort select re-orders, and navigation works from a FILTERED issue")]
    public async Task Desktop_FilterChipTextSortAndFilteredNavigation()
    {
        var page = await OpenModelingPageAsync("?scenario=issues-mixed", 1440, 900, touch: false);

        var panel = page.Locator("[data-testid='modeling-issue-panel']");
        var filterbar = panel.Locator("[data-testid='modeling-issue-filterbar']");
        Assert.AreEqual(1, await filterbar.CountAsync(), "the editor turns the filter bar on in the dock");

        // Severity chips: counts and the unfiltered start.
        var errorChip = page.Locator("[data-testid='modeling-issue-severity-chip-error']");
        Assert.AreEqual("false", await errorChip.GetAttributeAsync("aria-pressed"));
        StringAssert.Contains(await errorChip.TextContentAsync() ?? string.Empty, "1",
            "the chip shows its per-severity count");

        // Chip filter: only the error row survives, aria-pressed flips.
        await errorChip.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-issue-panel']\")?.getAttribute('data-visible-issue-count') === '1'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.AreEqual("true", await errorChip.GetAttributeAsync("aria-pressed"));

        // Navigation from a FILTERED issue still selects its element.
        await page.Locator("[data-testid='modeling-issue-panel'] [data-source-element-id='bpmn-ship-order']")
            .First.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
        await page.Locator("[data-testid='modeling-editor'][data-selected-element-id='bpmn-ship-order']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // Toggle the chip back off (no dedicated clear button while rows are visible)…
        await page.Locator("[data-testid='modeling-issue-severity-chip-error']").ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-issue-panel']\")?.getAttribute('data-visible-issue-count') === '4'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // …then the text filter over the SOURCE NAME ("Validate order" is the element's name,
        // matched case-insensitively — the two Validate-order issues survive).
        await page.Locator("[data-testid='modeling-issue-filter-input']").FillAsync("validate order");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-issue-panel']\")?.getAttribute('data-visible-issue-count') === '2'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // Sort by source over the FULL list: "Ship order" first, the two stable
        // "Validate order" rows keep their input order, the sourceless orphan last.
        await page.Locator("[data-testid='modeling-issue-filter-input']").FillAsync(string.Empty);
        await page.Locator("[data-testid='modeling-issue-sort'] select")
            .SelectOptionAsync(new SelectOptionValue { Label = "Source" });
        await page.WaitForFunctionAsync(
            """
            () => {
                const rows = [...document.querySelectorAll("[data-testid='modeling-issue-list'] > li > *")]
                    .map(row => row.getAttribute('data-testid'));
                return rows.join(',') === 'modeling-issue-2,modeling-issue-0,modeling-issue-1,modeling-issue-3';
            }
            """,
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // The bar is ONE compact row (~40px at a fine pointer): measured and guarded here so
        // the single-row UX contract cannot silently regress.
        var barHeight = await filterbar.EvaluateAsync<float>(
            "el => el.getBoundingClientRect().height");
        Assert.IsTrue(barHeight >= 36 && barHeight <= 46,
            $"the filter bar is a single compact row (~40px), measured {barHeight:0.#} px at 1440x900");

        // The no-match state with a one-press clear.
        await page.Locator("[data-testid='modeling-issue-filter-input']").FillAsync("no-such-issue-anywhere");
        await page.WaitForFunctionAsync(
            "() => !!document.querySelector(\"[data-testid='modeling-issue-no-match']\")",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.Locator("[data-testid='modeling-issue-clear-filters']").ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-issue-panel']\")?.getAttribute('data-visible-issue-count') === '4'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    // ── Tablet sheet ─────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Tablet sheet: the bar renders inside the issues sheet and the filter survives a sheet close/reopen")]
    public async Task Tablet_FilterSurvivesSheetCloseAndReopen()
    {
        var page = await OpenModelingPageAsync("?scenario=issues-mixed&notation=bpmn&viewpoint=overview", 1024, 768, touch: true);

        await page.Locator("[data-testid='modeling-panel-tab-issues']")
            .TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        var sheet = page.Locator("[data-testid='modeling-issues-sheet']");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.AreEqual(1, await sheet.Locator("[data-testid='modeling-issue-filterbar']").CountAsync(),
            "the sheet body carries the filter bar too");

        // The demo's generated issues ride along in the display list, so the assertions are
        // RELATIVE to the total (the exact pair is the panel's own bUnit contract).
        var tabletTotal = await page.EvaluateAsync<int>(
            "() => Number(document.querySelector(\"[data-testid='modeling-issues-sheet'] [data-testid='modeling-issue-panel']\")?.getAttribute('data-total-issue-count') ?? '0')");
        await sheet.Locator("[data-testid='modeling-issue-filter-input']").FillAsync("ship");
        await page.WaitForFunctionAsync(
            """
            () => {
                const panel = document.querySelector("[data-testid='modeling-issues-sheet'] [data-testid='modeling-issue-panel']");
                const visible = Number(panel?.getAttribute('data-visible-issue-count') ?? '-1');
                const total = Number(panel?.getAttribute('data-total-issue-count') ?? '0');
                return total > 0 && visible >= 0 && visible < total;
            }
            """,
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // Close the sheet (the tab toggles) and reopen: the editor state re-seeds the filter.
        await page.Locator("[data-testid='modeling-panel-tab-issues']")
            .TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => !document.querySelector(\"[data-testid='modeling-issues-sheet']\")",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.Locator("[data-testid='modeling-panel-tab-issues']")
            .TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-issues-sheet'] [data-testid='modeling-issue-filter-input']\")?.value === 'ship'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    // ── Mobile tab ───────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Mobile 390×844: the Issues tab bar wraps without horizontal overflow and the filter survives a tab switch")]
    public async Task Mobile_390_FilterBarWrapsAndSurvivesTabSwitch()
    {
        var page = await OpenModelingPageAsync("?scenario=issues-mixed", 390, 844, touch: true);

        await page.Locator("[data-testid='modeling-panel-tab-issues']")
            .TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        var panel = page.Locator("[data-testid='modeling-issues-panel'] [data-testid='modeling-issue-panel']");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.AreEqual(1, await panel.Locator("[data-testid='modeling-issue-filterbar']").CountAsync(),
            "the mobile tab body carries the filter bar");

        // The bar wraps (multi-row layout) and the page never overflows horizontally.
        var wrapProbe = await panel.Locator("[data-testid='modeling-issue-filterbar']").EvaluateAsync<int>(
            "el => el.getBoundingClientRect().width > 0 ? el.getBoundingClientRect().height : -1");
        Assert.IsTrue(wrapProbe > 0, "the filter bar is visible");

        var mobileTotal = await page.EvaluateAsync<int>(
            "() => Number(document.querySelector(\"[data-testid='modeling-issues-panel'] [data-testid='modeling-issue-panel']\")?.getAttribute('data-total-issue-count') ?? '0')");
        await panel.Locator("[data-testid='modeling-issue-filter-input']").FillAsync("ship");
        await page.WaitForFunctionAsync(
            """
            () => {
                const panel = document.querySelector("[data-testid='modeling-issues-panel'] [data-testid='modeling-issue-panel']");
                const visible = Number(panel?.getAttribute('data-visible-issue-count') ?? '-1');
                const total = Number(panel?.getAttribute('data-total-issue-count') ?? '0');
                return total > 0 && visible >= 0 && visible < total;
            }
            """,
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.IsTrue(await HasNoHorizontalOverflowAsync(page), "390 must not overflow horizontally with the bar");

        // Switch away and back: one panel at a time, the filter comes back from the editor state.
        await page.Locator("[data-testid='modeling-panel-tab-preview']")
            .TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => !document.querySelector(\"[data-testid='modeling-issues-panel'] [data-testid='modeling-issue-filter-input']\")",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.Locator("[data-testid='modeling-panel-tab-issues']")
            .TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-issues-panel'] [data-testid='modeling-issue-filter-input']\")?.value === 'ship'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.IsTrue(await HasNoHorizontalOverflowAsync(page));
    }

    // ── 500-issue latency (M26 budget for the filter) ────────────────────────

    [TestMethod]
    [Description("500-issue scenario: a text filter narrows the list within the 150ms debounce + 200ms processing budget and the chips report counts")]
    public async Task FiveHundredIssues_FilterNarrowsWithinBudget()
    {
        var page = await OpenModelingPageAsync("?scenario=issues-500", 1440, 900, touch: false);

        await page.WaitForFunctionAsync(
            "() => Number(document.querySelector(\"[data-testid='modeling-issue-panel']\")?.getAttribute('data-total-issue-count') ?? '0') >= 500",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });

        var chipCounts = await page.EvaluateAsync<int>(
            """
            () => [...document.querySelectorAll("[data-testid='modeling-issue-panel'] .tm-modeling-issue-panel__chip-count")]
                .map(el => Number(el.textContent)).reduce((a, b) => a + b, 0)
            """);
        var total = await page.EvaluateAsync<int>(
            "() => Number(document.querySelector(\"[data-testid='modeling-issue-panel']\")?.getAttribute('data-total-issue-count') ?? '0')");
        Assert.AreEqual(total, chipCounts, "the chip counts sum to the total");

        // One input event → the visible count drops below the total; the wall-clock budget
        // covers Blazor's event round-trip plus the memoized 500-row recompute and patch.
        var elapsedMs = await page.EvaluateAsync<double>(
            """
            async () => {
                const input = document.querySelector("[data-testid='modeling-issue-panel'] [data-testid='modeling-issue-filter-input']");
                if (!input) return -1;
                const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
                const t0 = performance.now();
                setter.call(input, 'validation');
                input.dispatchEvent(new Event('input', { bubbles: true }));
                await new Promise((resolve) => {
                    const check = () => {
                        const visible = Number(document.querySelector("[data-testid='modeling-issue-panel']")?.getAttribute('data-visible-issue-count') ?? '0');
                        const total = Number(document.querySelector("[data-testid='modeling-issue-panel']")?.getAttribute('data-total-issue-count') ?? '0');
                        (visible > 0 && visible < total) ? resolve() : setTimeout(check, 5);
                    };
                    setTimeout(check, 0);
                });
                return performance.now() - t0;
            }
            """
                );

        Assert.IsTrue(elapsedMs >= 0, "the filter input exists in the dock");
        // Fáze 11 review: the input delivers through TmSearchInput's 150ms debounce, which is
        // part of the wall clock — the budget is the debounce + the original 200ms processing
        // budget (Blazor's event round-trip plus the memoized 500-row recompute and patch).
        Assert.IsTrue(elapsedMs < 350, $"the 500-issue filter must answer within 150ms debounce + 200ms processing, took {elapsedMs:F0} ms");
    }

    // ── Screenshots per band (UX review against the mockups) ─────────────────

    [TestMethod]
    [Description("Captures the issue panel with the filter bar at 1440 / 1024 / 390 for the UX review")]
    public async Task Capture_IssueFilterBar_ShotsPerBand()
    {
        foreach (var (width, height, touch) in new[] { (1440, 900, false), (1024, 768, true), (390, 844, true) })
        {
            var page = await OpenModelingPageAsync("?scenario=issues-mixed", width, height, touch);
            if (width <= 500)
            {
                await page.Locator("[data-testid='modeling-panel-tab-issues']")
                    .TapAsync(new LocatorTapOptions { Timeout = 10_000 });
            }
            else if (width < 1200)
            {
                await page.Locator("[data-testid='modeling-panel-tab-issues']")
                    .TapAsync(new LocatorTapOptions { Timeout = 10_000 });
                await page.Locator("[data-testid='modeling-issues-sheet']")
                    .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            }

            await page.WaitForFunctionAsync(
                "() => !!document.querySelector(\"[data-testid='modeling-issue-filterbar']\")",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });
            await page.Locator("[data-testid='modeling-issue-panel']")
                .ScreenshotAsync(new LocatorScreenshotOptions
                {
                    Path = Path.Combine(ScreenshotDirectory, $"issues-mixed-filterbar-{width}x{height}.png"),
                    Timeout = 30_000
                });
            await page.Context.CloseAsync();
        }
    }

    private string ScreenshotDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                directory = directory.Parent!;
            }

            var dir = Path.Combine(directory!.FullName, "TestResults", "modeling-phase11");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    // ── Local helpers ────────────────────────────────────────────────────────

    private async Task<IPage> OpenModelingPageAsync(string query, int width, int height, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = false,
            Locale = "en-US",
            IgnoreHTTPSErrors = true
        });
        RegisterContext(context);
        await context.AddInitScriptAsync(
            """
            localStorage.setItem('tm-demo-culture', 'en');
            localStorage.removeItem('tm-demo-theme');
            """
                );
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000
        });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        // The generation render must land BEFORE any tab/sheet interaction: the post-generate
        // re-render re-grids the shell and would swallow a tap racing it (the tablet E2E
        // convention). The panel itself is NOT awaited here — on the tablet band it lives
        // inside the CLOSED issues sheet and on mobile behind the inactive Issues tab.
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-node').length > 0",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });
        return page;
    }

    private static Task<bool> HasNoHorizontalOverflowAsync(IPage page) =>
        page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1");
}
