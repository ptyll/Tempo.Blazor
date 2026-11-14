using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 7ba95ed6 phase 12 — the TOUCH emulation sweep. No real device is involved: the contexts are
/// <c>HasTouch</c> + <c>IsMobile</c> at a 375/390 px phone band, which is what makes
/// <c>(any-pointer: coarse)</c> match and what a finger's event order reaches the page through. Swipe and
/// tap gestures go through Chromium's own touch pipeline (CDP <c>Input.dispatchTouchEvent</c>), so the
/// component's pointer handling, <c>touch-action</c> and native scrolling are exercised — not a synthetic
/// <c>click</c>.
/// <para>
/// The spec fills the gaps the per-component lanes leave at a coarse pointer: the facet filter's sheet
/// (content-height scrolling under a pinned footer, and the swipe back down), the trace waterfall's
/// tap-to-select plus its 44 px disclosure, the usage meter's call to action and the segmented tray's
/// horizontal swipe. It then walks the two affected screens end to end (<c>/analytics-kit</c> and
/// <c>/trace-waterfall</c>) at the phone band, where the chart's tap-pinned readout is the whole point.
/// Captures land in <c>TestResults/touch-sweep</c>.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class Fze12TouchEmulationSweepE2ETests : PlaywrightTestBase
{
    private const string DataDisplayPath = "/data-display";
    private const string TraceWaterfallPath = "/trace-waterfall";
    private const string AnalyticsKitPath = "/analytics-kit";

    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "touch-sweep");

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

    /// <summary>
    /// Opens one demo page in a device-like context: a phone-sized viewport, a coarse pointer
    /// (<c>HasTouch</c>), mobile emulation (<c>IsMobile</c>) and the culture pinned to English.
    /// </summary>
    private async Task<IPage> OpenAsync(string path, int width = 390, int height = 844, string waitFor = "")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = true,
            IsMobile = true,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{path}");
        if (!string.IsNullOrEmpty(waitFor))
        {
            await page.WaitForSelectorAsync(waitFor, new PageWaitForSelectorOptions { Timeout = 60000 });
        }

        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private async Task ShootAsync(IPage page, string name, bool fullPage = false)
    {
        await page.WaitForTimeoutAsync(250);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, $"{name}.png"),
            FullPage = fullPage,
        });
    }

    // ── Chromium's own touch pipeline ────────────────────────────────────────────────────────

    private static Task DispatchTouchAsync(ICDPSession session, string type, params (int Id, double X, double Y)[] points)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = points.Select(point => (object)new Dictionary<string, object>
            {
                ["x"] = point.X,
                ["y"] = point.Y,
                ["id"] = point.Id,
            }).ToArray(),
        });

    private static async Task TapAsync(ICDPSession session, double x, double y)
    {
        await DispatchTouchAsync(session, "touchStart", (1, x, y));
        await DispatchTouchAsync(session, "touchEnd");
    }

    /// <summary>
    /// A finger drag: down at <paramref name="from"/>, <paramref name="steps"/> moves, up. The moves are
    /// dispatched in separate round trips, so the browser sees time pass between them and derives a real
    /// velocity (a single teleporting move reports velocity 0 and never dismisses a sheet).
    /// </summary>
    private static async Task SwipeAsync(
        ICDPSession session,
        (double X, double Y) from,
        (double X, double Y) to,
        int steps = 10,
        int delayMs = 16)
    {
        await DispatchTouchAsync(session, "touchStart", (1, from.X, from.Y));
        for (var step = 1; step <= steps; step++)
        {
            await DispatchTouchAsync(session, "touchMove",
                (1, from.X + (to.X - from.X) * step / steps, from.Y + (to.Y - from.Y) * step / steps));
            await Task.Delay(delayMs);
        }

        await DispatchTouchAsync(session, "touchEnd");
    }

    private static async Task<(double X, double Y, double Width, double Height)> BoxAsync(ILocator locator)
    {
        var box = await locator.BoundingBoxAsync()
                  ?? throw new InvalidOperationException("the element has no bounding box");
        return (box.X, box.Y, box.Width, box.Height);
    }

    // ── the facet filter sheet: scrolled body, pinned footer, swipe down ─────────────────────

    [TestMethod]
    public async Task FacetSheet_ScrollsItsBodyUnderAPinnedFooter_AndASwipeBackDownDismissesIt()
    {
        // A short phone: half of 640 px is not enough for three groups, so the body really overflows.
        var page = await OpenAsync(DataDisplayPath, 390, 640,
            "[data-testid='facet-filter-section'] .tm-facet-filter");

        var card = page.Locator("[data-testid='facet-filter-section'] .tm-facet-filter[data-presentation='popover']").First;
        var trigger = card.GetByTestId("facet-filter-trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.TapAsync();

        var sheet = card.GetByTestId("facet-filter-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        var drawer = page.Locator(".tm-drawer--bottom").First;
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var body = drawer.Locator(".tm-drawer__body");
        var footer = drawer.Locator(".tm-drawer__footer");
        var measured = await body.EvaluateAsync<bool>("el => el.scrollHeight > el.clientHeight + 1");
        Assert.IsTrue(measured,
            "the sheet's body is the scroll owner: on a 390×640 phone the groups do not fit the half snap");

        // The sheet slides in: measuring while the slide-in is still running reads a footer that is lower than its final
        // position (595 vs 542 px) and mistakes the settling for the body scroll moving it.
        await WaitForLayoutToSettleAsync(page, ".tm-drawer--bottom .tm-drawer__panel, .tm-drawer--bottom .tm-drawer__footer");
        var footerBefore = await BoxAsync(footer);
        var panelBefore = await BoxAsync(drawer.Locator(".tm-drawer__panel"));

        await body.EvaluateAsync("el => el.scrollTo({ top: el.scrollHeight, behavior: 'instant' })");
        await page.WaitForTimeoutAsync(300);

        var footerAfter = await BoxAsync(footer);
        var panelAfter = await BoxAsync(drawer.Locator(".tm-drawer__panel"));
        Assert.AreEqual(footerBefore.Y, footerAfter.Y, 1.5,
            "the confirmation footer is pinned: scrolling the body must not move it");
        Assert.AreEqual(panelBefore.Height, panelAfter.Height, 1.5,
            "the sheet keeps its height while the body scrolls, so the footer never leaves the panel");
        Assert.IsTrue(footerAfter.Y + footerAfter.Height <= panelAfter.Y + panelAfter.Height + 1,
            "the footer stays inside the sheet");
        await ShootAsync(page, "facet-sheet-scrolled-footer-pinned-390");

        // The sheet starts at its lowest snap, so a downward flick (> 0.5 px/ms) is a dismiss.
        var session = await page.Context.NewCDPSessionAsync(page);
        var handle = await BoxAsync(drawer.Locator(".tm-sheet__handle"));
        await SwipeAsync(session,
            (handle.X + handle.Width / 2, handle.Y + handle.Height / 2),
            (handle.X + handle.Width / 2, handle.Y + handle.Height / 2 + 220),
            steps: 8,
            delayMs: 12);

        await page.WaitForFunctionAsync(
            "() => !document.querySelector('.tm-drawer--bottom')",
            null,
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual(0, await card.GetByTestId("facet-filter-sheet").CountAsync(),
            "the swipe dismissed the sheet instead of leaving it half-open");
        await ShootAsync(page, "facet-sheet-dismissed-390");
    }

    // ── the trace waterfall: tap-to-select and the 44 px disclosure ─────────────────────────

    [TestMethod]
    public async Task TraceWaterfall_TapSelectsARow_AndTheDisclosureKeepsAFortyFourPixelTarget()
    {
        var page = await OpenAsync(TraceWaterfallPath, 390, 844, "[data-testid='trace-page-waterfall'] [role='treegrid']");
        var grid = page.Locator("[data-testid='trace-page-waterfall'] [role='treegrid']");
        var parent = grid.Locator("[role='row'][data-span-id='sub-agent']");

        // The row has to be in the MIDDLE of its scrollport before a touch point can be dispatched at it:
        // CDP coordinates are viewport-relative, and a row parked at the scrollport's top edge sits UNDER the
        // sticky column head — the tap would land on the head and the row would never hear it.
        await parent.EvaluateAsync(
            "el => el.scrollIntoView({ block: 'center', inline: 'nearest', behavior: 'instant' })");
        await page.WaitForTimeoutAsync(400);

        // The disclosure is the only finger-sized control a coarse pointer gets for folding a subtree.
        var toggle = await BoxAsync(parent.Locator(".tm-trace-waterfall__toggle"));
        Assert.IsTrue(toggle.Width >= 44 && toggle.Height >= 44,
            $"the disclosure keeps the shared 44 px target on a finger (was {toggle.Width}×{toggle.Height})");

        // Tap the row's own LABEL, never the chevron: the row owns the selection, and the label sits inside
        // the horizontally scrolled name column (the duration cell can be past the scrollport's edge).
        var target = await BoxAsync(parent.Locator(".tm-trace-waterfall__label"));
        var viewport = page.ViewportSize ?? new PageViewportSizeResult { Width = 390, Height = 844 };
        var tapX = target.X + Math.Min(target.Width / 2, 20);
        var tapY = target.Y + target.Height / 2;
        Assert.IsTrue(tapY > 0 && tapY < viewport.Height && tapX > 0 && tapX < viewport.Width,
            $"the tap point is inside the viewport (x {tapX}, y {tapY}, viewport {viewport.Width}×{viewport.Height})");

        // A self-describing guard: the point must belong to the row, not to the sticky head or the page chrome.
        var hit = await page.EvaluateAsync<string>(
            """
            ([x, y]) => {
                const el = document.elementFromPoint(x, y);
                if (!el) return 'none';
                const row = el.closest("[role='row'][data-span-id]");
                return `${el.className || el.tagName} -> ${row ? row.getAttribute('data-span-id') : 'no-row'}`;
            }
            """,
            new object[] { tapX, tapY });
        StringAssert.Contains(hit, "sub-agent", $"the tap point lands on the row (hit {hit})");

        var session = await page.Context.NewCDPSessionAsync(page);
        await TapAsync(session, tapX, tapY);

        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='trace-page-waterfall'] [role='row'][data-span-id='sub-agent']\")" +
            "?.getAttribute('aria-selected') === 'true'",
            null,
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual("sub-agent", await page.GetByTestId("trace-page-detail").GetAttributeAsync("data-span-id"),
            "the detail panel follows the tapped row");
        await ShootAsync(page, "trace-waterfall-tap-selected-390");

        // The same finger folds the subtree: a tap on the disclosure must expand/collapse, not select.
        await TapAsync(session, toggle.X + toggle.Width / 2, toggle.Y + toggle.Height / 2);
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='trace-page-waterfall'] [role='row'][data-span-id='sub-agent']\")" +
            "?.getAttribute('aria-expanded') === 'false'",
            null,
            new PageWaitForFunctionOptions { Timeout = 10000 });
        await ShootAsync(page, "trace-waterfall-tap-collapsed-390");
    }

    // ── the usage meter: the near-limit call to action on a finger ──────────────────────────

    [TestMethod]
    public async Task UsageMeter_CallToActionKeepsAFortyFourPixelTarget_AndATapReachesTheHost()
    {
        var page = await OpenAsync(DataDisplayPath, 390, 844, "[data-testid='usage-meter-section'] [role='meter']");

        var cta = page.GetByTestId("usage-meter-upgrade");
        await cta.ScrollIntoViewIfNeededAsync();
        var box = await BoxAsync(cta);
        Assert.IsTrue(box.Height >= 44,
            $"the near-limit call to action is a host slot, and the meter sizes it for a finger (height {box.Height})");

        var session = await page.Context.NewCDPSessionAsync(page);
        await TapAsync(session, box.X + box.Width / 2, box.Y + box.Height / 2);

        await page.WaitForSelectorAsync(".tm-toast", new PageWaitForSelectorOptions { Timeout = 10000 });
        StringAssert.Contains((await page.Locator(".tm-toast").First.TextContentAsync()) ?? string.Empty, "credits",
            "the tap reached the host's own handler, not just the button's focus ring");
        await ShootAsync(page, "usage-meter-cta-tapped-390");
    }

    // ── the segmented tray: a horizontal swipe on an overflowing tray ───────────────────────

    [TestMethod]
    public async Task SegmentedTray_SwipesHorizontally_UnderACoarsePointer()
    {
        var page = await OpenAsync(DataDisplayPath, 390, 844, "[data-testid='segmented-control-section'] [role='radiogroup']");
        var track = page.Locator("[data-testid='segmented-overflow'] .tm-segmented__track");

        await track.ScrollIntoViewIfNeededAsync();
        var before = await track.EvaluateAsync<double[]>("el => [el.scrollWidth, el.clientWidth, el.scrollLeft]");
        Assert.IsTrue(before[0] > before[1],
            $"the narrow tray really overflows (scrollWidth {before[0]} > clientWidth {before[1]})");
        Assert.AreEqual(0, before[2], "the tray opens at its start");

        var session = await page.Context.NewCDPSessionAsync(page);
        var box = await BoxAsync(track);
        var y = box.Y + box.Height / 2;
        await SwipeAsync(session, (box.X + box.Width - 24, y), (box.X + 24, y), steps: 8, delayMs: 12);

        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='segmented-overflow'] .tm-segmented__track\")?.scrollLeft > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 10000 });
        var after = await track.EvaluateAsync<double[]>("el => [el.scrollWidth, el.clientWidth, el.scrollLeft]");
        Assert.IsTrue(after[2] > 0, $"a leftward swipe scrolls the tray (scrollLeft {after[2]})");

        var option = await BoxAsync(track.Locator("[role='radio']").Last);
        Assert.IsTrue(option.Height >= 44,
            $"a coarse pointer gets the 44 px segment target (height {option.Height})");
        await ShootAsync(page, "segmented-tray-swiped-390");
    }

    // ── the two affected screens end to end at the phone band ───────────────────────────────

    [TestMethod]
    public async Task AnalyticsKit_TapPinsTheChartReadout_AndThePageNeverScrollsSideways()
    {
        var page = await OpenAsync(AnalyticsKitPath, 390, 844, "[data-testid='analytics-kit-chart'] .tm-chart");

        var pageWidth = await page.EvaluateAsync<double>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(pageWidth <= 391, $"the assembled reporting page never scrolls sideways (width {pageWidth})");

        var chart = page.Locator("[data-testid='analytics-kit-chart'] .tm-chart").First;
        await chart.ScrollIntoViewIfNeededAsync();
        var zone = await BoxAsync(chart.Locator(".tm-chart__index-zone").Nth(1));
        var session = await page.Context.NewCDPSessionAsync(page);
        await TapAsync(session, zone.X + zone.Width / 2, zone.Y + zone.Height / 2);

        // The tap PINS the readout: it is still there after the pointer is gone (the bug a touch
        // context reproduces is the synthesized mouseout clearing it a frame later).
        await Assertions.Expect(chart.Locator(".tm-chart__tooltip--index")).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        await page.WaitForTimeoutAsync(700);
        await Assertions.Expect(chart.Locator(".tm-chart__tooltip--index")).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        await ShootAsync(page, "analytics-kit-chart-tap-pinned-390");

        // The same finger uses the faceted filter: its sheet is the phone presentation of the panel.
        var facets = page.GetByTestId("analytics-kit-facets");
        await facets.ScrollIntoViewIfNeededAsync();
        var trigger = facets.GetByTestId("facet-filter-trigger");
        await trigger.TapAsync();
        await facets.GetByTestId("facet-filter-sheet")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        var confirm = facets.GetByTestId("facet-filter-confirm");
        StringAssert.Matches((await confirm.TextContentAsync())!.Trim(),
            new System.Text.RegularExpressions.Regex(@"^Show \d+ results$"));
        await ShootAsync(page, "analytics-kit-facet-sheet-390");
    }

    [TestMethod]
    public async Task TraceWaterfallPage_KeepsItsPhoneBand_WithTheGridAsTheOnlyScroller()
    {
        var page = await OpenAsync(TraceWaterfallPath, 375, 812, "[data-testid='trace-page-waterfall'] [role='treegrid']");

        var pageWidth = await page.EvaluateAsync<double>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(pageWidth <= 376, $"the page itself does not scroll sideways (width {pageWidth})");

        var grid = page.Locator("[data-testid='trace-page-waterfall'] [role='treegrid']");
        var overflow = await grid.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1");
        Assert.IsTrue(overflow, "the treegrid keeps the full template and scrolls inside its own scrollport");

        var row = await BoxAsync(grid.Locator("[role='row'][data-span-id]").First);
        Assert.IsTrue(row.Height >= 32, $"a row is still a readable finger target (height {row.Height})");
        await ShootAsync(page, "trace-waterfall-page-phone-375");
    }
}
