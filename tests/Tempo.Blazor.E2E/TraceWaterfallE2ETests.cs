using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan ff043b2e phase 6 — <c>TmTraceWaterfall</c> in a real browser. The assertions read behaviour:
/// the demo's generated agent run renders as a treegrid whose rows carry the ARIA tree semantics, the
/// disclosure and the ←/→ keys collapse and expand a subtree, a click selects a span and opens the
/// detail panel, the zoom buttons narrow and restore the window, the navigation keys the rows handle
/// cancel their browser scroll default while Tab keeps its own (and the disclosure's native Space
/// still works), the bars keep a visible border under forced colors, the detail panel moves BELOW the
/// grid on the phone band (the container query, proven by geometry rather than by a screenshot), the
/// phone band keeps its overflow INSIDE the grid and scrolls the header together with the rows, and a
/// scoped axe scan of the new section finds no critical or serious violation. Reference screenshots
/// land in <c>TestResults/trace-waterfall</c>.
/// </summary>
public abstract class TraceWaterfallE2EBase : PlaywrightTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "trace-waterfall");

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

    private static ILocator Section(IPage page) => page.Locator("[data-testid='trace-waterfall-section']");

    private static ILocator Rows(IPage page) => Section(page).Locator("[role='row'][data-span-id]");

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1000, bool reduceMotion = false)
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
        await GotoAppReadyAsync(page, $"{BaseUrl}/data-display");
        await page.WaitForSelectorAsync(
            "[data-testid='trace-waterfall-section'] [role='treegrid']",
            new PageWaitForSelectorOptions { Timeout = 30000 });
        // The demo run's own eight spans on screen, then the settled layout — every test in this base reads
        // either that row set or the grid's geometry.
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(8, new LocatorAssertionsToHaveCountOptions { Timeout = 30000 });
        // The phone tests frame the section themselves and settle it there: a page-wide settle right after a cold
        // load is not a state the demo is obliged to reach (its CDN paint is still in flight).
        return page;
    }

    [TestMethod]
    public async Task RendersTheTreegrid_WithAriaSemantics()
    {
        var page = await OpenAsync();

        var grid = page.Locator("[data-testid='trace-waterfall-section'] [role='treegrid']");
        Assert.AreEqual("Agent run — support triage", await grid.GetAttributeAsync("aria-label"));

        var rows = page.Locator("[data-testid='trace-waterfall-section'] [role='row'][data-span-id]");
        Assert.AreEqual(8, await rows.CountAsync(), "every span of the demo run renders as a row");

        var root = page.Locator("[data-testid='trace-waterfall-section'] [data-span-id='run']");
        Assert.AreEqual("1", await root.GetAttributeAsync("aria-level"));
        Assert.AreEqual("true", await root.GetAttributeAsync("aria-expanded"));
        Assert.AreEqual(
            "2",
            await page.Locator("[data-testid='trace-waterfall-section'] [data-span-id='answer']")
                .GetAttributeAsync("aria-level"));

        var headers = page.Locator("[data-testid='trace-waterfall-section'] [role='columnheader']");
        Assert.IsTrue(await headers.CountAsync() >= 3, "the grid declares its name / timeline / duration columns");

        await ShootAsync(page, "trace-waterfall-desktop");
    }

    [TestMethod]
    public async Task KeyboardCollapsesAndExpands_AndSelectsWithEnter()
    {
        var page = await OpenAsync();

        var root = page.Locator("[data-testid='trace-waterfall-section'] [data-span-id='run']");
        await root.FocusAsync();
        Assert.AreEqual("0", await root.GetAttributeAsync("tabindex"),
            "the focused row owns the roving tabindex");

        await page.Keyboard.PressAsync("ArrowLeft");

        // The rendered row set IS the collapsed state: one row left after collapsing the root.
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        Assert.AreEqual("false", await root.GetAttributeAsync("aria-expanded"));

        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(8, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

        var db = Section(page).Locator("[data-span-id='lookup-db']");
        await db.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(db).ToHaveAttributeAsync(
            "aria-selected", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });
        await Assertions.Expect(Section(page).Locator("[data-testid='trace-detail']")).ToContainTextAsync(
            "SELECT customers", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        await ShootAsync(page, "trace-waterfall-selected");
    }

    [TestMethod]
    public async Task ZoomNarrowsTheWindow_AndResetRestoresIt()
    {
        var page = await OpenAsync();

        var section = page.Locator("[data-testid='trace-waterfall-section']");
        var window = section.Locator(".tm-trace-waterfall__window");
        var before = (await window.TextContentAsync())?.Trim();

        await section.Locator("[data-testid='trace-zoom-in']").ClickAsync();

        // The reported window label is what the assertion compares, so wait for it to move off the whole trace
        // (and then back) rather than pausing after each click.
        await Assertions.Expect(window).Not.ToHaveTextAsync(
            before!, new LocatorAssertionsToHaveTextOptions { Timeout = 5000 });
        var zoomed = (await window.TextContentAsync())?.Trim();
        Assert.AreNotEqual(before, zoomed, "zooming in narrows the visible window");

        await section.Locator("[data-testid='trace-zoom-reset']").ClickAsync();
        await Assertions.Expect(window).ToHaveTextAsync(
            before!, new LocatorAssertionsToHaveTextOptions { Timeout = 5000 });
        Assert.AreEqual(before, (await window.TextContentAsync())?.Trim(), "reset restores the whole trace");
    }

    [TestMethod]
    public async Task PhoneBand_PutsTheDetailPanelBelowTheGrid()
    {
        var page = await OpenAsync(width: 390, height: 844);
        await Section(page).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, "[data-testid='trace-waterfall-section']");

        var section = page.Locator("[data-testid='trace-waterfall-section']");
        var grid = await section.Locator("[role='treegrid']").BoundingBoxAsync();
        var detail = await section.Locator("[data-testid='trace-detail']").BoundingBoxAsync();

        Assert.IsNotNull(grid);
        Assert.IsNotNull(detail);
        Assert.IsTrue(detail!.Y >= grid!.Y + grid.Height - 1,
            $"on a 390 px container the detail panel stacks BELOW the grid (grid bottom {grid.Y + grid.Height}, detail top {detail.Y})");

        await ShootAsync(page, "trace-waterfall-phone");
    }

    [TestMethod]
    public async Task RowKeys_CancelTheirScrollDefault_ButNeverTab()
    {
        var page = await OpenAsync();

        var section = page.Locator("[data-testid='trace-waterfall-section']");
        var grid = section.Locator("[role='treegrid']");
        var rows = section.Locator("[role='row'][data-span-id]");

        // Put the whole grid inside the viewport first: focus moves to another row must not need a
        // scroll of their own, so any scroll that DOES happen is the key's default action.
        await grid.ScrollIntoViewIfNeededAsync();
        await section.Locator("[data-span-id='run']").FocusAsync();

        // A bubble-phase listener on the document runs AFTER any listener on the grid, so
        // defaultPrevented here is exactly the decision the on-row guard made for that key.
        await page.EvaluateAsync(
            """
            () => {
                window.__traceKeys = [];
                document.addEventListener('keydown', (event) => {
                    const target = event.target;
                    if (target instanceof HTMLElement && target.dataset && target.dataset.spanId) {
                        window.__traceKeys.push(`${event.key}|${event.defaultPrevented}`);
                    }
                });
                window.__traceScrollTops = () => {
                    const positions = [];
                    const row = document.querySelector("[data-testid='trace-waterfall-section'] [data-span-id='run']");
                    for (let element = row; element; element = element.parentElement) {
                        const style = getComputedStyle(element);
                        if (/(auto|scroll)/.test(style.overflowY) && element.scrollHeight > element.clientHeight) {
                            positions.push(element.scrollTop);
                        }
                    }
                    if (document.scrollingElement) positions.push(document.scrollingElement.scrollTop);
                    return positions;
                };
            }
            """);

        var before = await page.EvaluateAsync<int[]>("() => window.__traceScrollTops()");
        Assert.IsTrue(before.Length > 0, "the row must sit inside a scrollable chain for this assertion to mean anything");

        var secondRowId = await rows.Nth(1).GetAttributeAsync("data-span-id");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Space");

        // Space selects on keyup, so the selection is the state that proves BOTH keys were handled; only then is
        // it meaningful to compare the scroll positions they must not have changed.
        await Assertions.Expect(section.Locator("[role='row'][aria-selected='true']")).ToHaveAttributeAsync(
            "data-span-id", secondRowId!, new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });

        var after = await page.EvaluateAsync<int[]>("() => window.__traceScrollTops()");
        CollectionAssert.AreEqual(before, after, "no scroll container moved while the rows answered the keys");

        await page.Keyboard.PressAsync("Tab");

        // The bubble-phase recorder logs one entry per handled key; three entries is the Tab having been seen
        // (and logged with its own defaultPrevented decision) — a fixed pause could read the log too early.
        await page.WaitForFunctionAsync(
            "() => ((window.__traceKeys && window.__traceKeys.length) || 0) >= 3",
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });

        var keys = await page.EvaluateAsync<string[]>("() => window.__traceKeys");
        CollectionAssert.AreEqual(
            new[] { "ArrowDown|true", " |true", "Tab|false" },
            keys,
            "the keys the row handles are cancelled; Tab keeps its default (a blanket preventDefault on "
            + "the grid would trap focus inside it)");

        // The disclosure is a native button inside the row: its native Space must stay untouched, so
        // the guard only cancels keys that target the row itself.
        await section.Locator("[data-span-id='run'] .tm-trace-waterfall__toggle").FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(rows).ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task PhoneBand_KeepsTheOverflowInsideTheGrid_AndScrollsTheHeaderWithTheRows()
    {
        var page = await OpenAsync(width: 390, height: 844);
        await Section(page).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, "[data-testid='trace-waterfall-section']");

        var section = page.Locator("[data-testid='trace-waterfall-section']");
        var grid = section.Locator("[role='treegrid']");

        var pageOverflow = await page.EvaluateAsync<double>(
            "() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.IsTrue(pageOverflow <= 1, $"a 390 px phone must not scroll the PAGE sideways (overflow {pageOverflow}px)");

        var metricWidth = await grid.EvaluateAsync<string>(
            "el => getComputedStyle(el).getPropertyValue('--tm-trace-waterfall-metric-width').trim()");
        Assert.AreEqual("3.75rem", metricWidth,
            "the narrow-container query tightens the template (the root's 5.25rem is not what a 390 px grid uses)");

        var durationHeader = section.Locator(".tm-trace-waterfall__head-cell--duration");
        var firstDurationCell = section.Locator(".tm-trace-waterfall__row .tm-trace-waterfall__duration").First;

        var headerBefore = await durationHeader.BoundingBoxAsync();
        var cellBefore = await firstDurationCell.BoundingBoxAsync();
        Assert.IsNotNull(headerBefore);
        Assert.IsNotNull(cellBefore);
        Assert.AreEqual(cellBefore!.X, headerBefore!.X, 1.0,
            "before scrolling, the duration header sits over the row's duration cell");

        var scroll = await grid.EvaluateAsync<double[]>(
            "el => { el.scrollLeft = el.scrollWidth; return [el.scrollLeft, el.scrollWidth, el.clientWidth]; }");

        // The header tracking the rows IS the state the geometry below reads: wait for the grid to be scrolled
        // AND the duration header to sit over its own row's cell again — the exact invariant asserted below,
        // instead of a pause after the scroll assignment.
        await page.WaitForFunctionAsync(
            """
            () => {
                const section = document.querySelector("[data-testid='trace-waterfall-section']");
                const grid = section && section.querySelector("[role='treegrid']");
                if (!grid || grid.scrollLeft <= 0) return false;
                const header = section.querySelector(".tm-trace-waterfall__head-cell--duration");
                const cell = section.querySelector(".tm-trace-waterfall__row .tm-trace-waterfall__duration");
                if (!header || !cell) return false;
                return Math.abs(header.getBoundingClientRect().x - cell.getBoundingClientRect().x) <= 1;
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });

        Assert.IsTrue(scroll[0] > 0, "the template is wider than a 390 px grid, so the grid scrolls sideways");
        Assert.IsTrue(scroll[1] > scroll[2], "the overflow lives on the grid, never on the document");

        var headerAfter = await durationHeader.BoundingBoxAsync();
        var cellAfter = await firstDurationCell.BoundingBoxAsync();
        Assert.IsNotNull(headerAfter);
        Assert.IsNotNull(cellAfter);
        Assert.AreEqual(cellAfter!.X, headerAfter!.X, 1.0,
            "after scrolling the grid sideways the header tracked the rows, so the columns stay in line");
        Assert.IsTrue(headerAfter!.X < headerBefore!.X - 10,
            $"the header really moved with the scroll (was {headerBefore.X}, now {headerAfter.X})");

        await ShootAsync(page, "trace-waterfall-phone-scrolled");
    }

    [TestMethod]
    public async Task ForcedColors_KeepTheBarsBordered()
    {
        var page = await OpenAsync();
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ForcedColors = ForcedColors.Active });

        // Wait for the emulated media to be live and the bar to have re-resolved its border — the exact property
        // the assertion below reads.
        await page.WaitForFunctionAsync(
            """
            () => {
                if (!matchMedia('(forced-colors: active)').matches) return false;
                const bar = document.querySelector("[data-testid='trace-waterfall-section'] .tm-trace-waterfall__bar");
                if (!bar) return false;
                const style = getComputedStyle(bar);
                return parseFloat(style.borderTopWidth) > 0 && style.borderTopStyle !== 'none';
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 10000 });

        var border = await page.EvaluateAsync<string>(
            """
            () => {
                const bar = document.querySelector("[data-testid='trace-waterfall-section'] .tm-trace-waterfall__bar");
                if (!bar) return "missing";
                const style = getComputedStyle(bar);
                return `${style.borderTopWidth} ${style.borderTopStyle}`;
            }
            """);

        Assert.AreNotEqual("missing", border);
        StringAssert.Contains(border, "solid", "a bar keeps a real border when the UA replaces its fill");
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
            """, "[data-testid='trace-waterfall-section']");

        Assert.AreEqual(0, violations.Length, "axe: " + string.Join(" | ", violations));
    }

    private async Task ShootAsync(IPage page, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        // The settle reads the real frames (fonts loaded, no CSS transition running, the section's box stable)
        // instead of pausing for a guessed interval.
        await WaitForLayoutToSettleAsync(page, "[data-testid='trace-waterfall-section']");
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
public class TraceWaterfallE2ETests : TraceWaterfallE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}

/// <summary>
/// The Server lane (plan 7ba95ed6, Fáze 11): the same <c>TmTraceWaterfall</c> over a real Blazor Server
/// circuit. The treegrid's roving tabindex and its ←/→ subtree walk, the selected-span detail panel, the zoom
/// group, the key-default cancellation, the forced-colors borders, the container-query detail placement on the
/// phone band and the scroll containment are all inherited, so the circuit's key round trips are measured by
/// the same expectations as the WASM lane.
/// </summary>
[TestClass]
[TestCategory("Server")]
public class TraceWaterfallServerE2ETests : TraceWaterfallE2EBase
{
    protected override string BaseUrl => "https://localhost:7107";
}

/// <summary>
/// The InteractiveAuto lane: the same waterfall on the host that prerenders over the Server circuit and then
/// hands the page to WebAssembly, covering the roving keys and the detail placement across the handover.
/// </summary>
[TestClass]
[TestCategory("InteractiveAuto")]
public class TraceWaterfallInteractiveAutoE2ETests : TraceWaterfallE2EBase
{
    protected override string BaseUrl => "https://localhost:7108";
}
