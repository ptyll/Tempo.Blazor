using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan ff043b2e phase 1 — the 2.10.0 data-display surface in a real browser. The <c>/data-display</c>
/// page renders the KPI stat cards (delta, hidden direction word, sparkline, loading skeleton), the
/// whole-card link/button targets and the animated numbers. Assertions read behaviour, not markup:
/// the ticker is caught MID-FLIGHT after a bump (so the JavaScript module really ran), the target is
/// landed on, reduced motion lands immediately, the delta carries its direction as text as well as
/// colour, and a scoped axe scan of the two new sections finds no critical or serious violation.
/// Reference screenshots land in <c>TestResults/data-display-kpi</c>.
/// </summary>
public abstract class DataDisplayKpiE2EBase : PlaywrightTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "data-display-kpi");

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

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1000, bool touch = false, bool reduceMotion = false)
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
        await context.AddInitScriptAsync(FrameRecorderScript);
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/data-display");
        await page.WaitForSelectorAsync("[data-testid='animated-numbers']", new PageWaitForSelectorOptions { Timeout = 30000 });
        // The card grid's own first paint and a settled layout, not a pause: every test reads the cards' text,
        // their geometry or their colours.
        await Assertions.Expect(KpiRow(page).Locator(".tm-stat-card")).Not.ToHaveCountAsync(
            0, new LocatorAssertionsToHaveCountOptions { Timeout = 30000 });
        return page;
    }

    private static ILocator KpiRow(IPage page) => page.GetByTestId("stat-cards-kpi");

    private static ILocator BumpButton(IPage page) => page.GetByTestId("animated-bump");

    /// <summary>The first animated number of the showcase (the one the bump button drives).</summary>
    private static ILocator ShowcaseValue(IPage page)
        => page.Locator("[data-testid='animated-numbers'] .tm-animated-number__value").First;

    /// <summary>
    /// Records every text the demo's first animated value node ever shows, from the moment the document starts
    /// parsing. Polling the node from .NET cannot prove what the browser painted — a frame can appear and be
    /// replaced between two polls, and how long the first animation takes relative to the test's first read is
    /// exactly what differs between a WASM boot, a Server circuit (whose prerendered markup is the first value)
    /// and InteractiveAuto. The page owns the log, so the same walk means the same thing on all three lanes.
    /// </summary>
    private const string FrameRecorderScript = """
        (() => {
            const state = { values: [] };
            window.__tmAnimatedFrames = state;
            const record = () => {
                const el = document.querySelector("[data-testid='animated-numbers'] .tm-animated-number__value");
                if (!el) return;
                const text = (el.textContent || '').trim();
                if (!text || state.values[state.values.length - 1] === text) return;
                state.values.push(text);
            };
            const start = () => {
                record();
                new MutationObserver(record).observe(document.documentElement, {
                    subtree: true, childList: true, characterData: true
                });
            };
            if (document.documentElement) start();
            else document.addEventListener('DOMContentLoaded', start, { once: true });
        })();
        """;

    /// <summary>Every text the animated value node showed, in paint order (installed before navigation).</summary>
    private static async Task<string[]> PaintedFramesAsync(IPage page)
        => await page.EvaluateAsync<string[]>(
            "() => (window.__tmAnimatedFrames && window.__tmAnimatedFrames.values) || []");

    /// <summary>The FIRST text the browser ever showed for the node — the value the component rendered itself.</summary>
    private static async Task<string> FirstPaintedValueAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => ((window.__tmAnimatedFrames && window.__tmAnimatedFrames.values.length) || 0) > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 30000 });
        return (await PaintedFramesAsync(page))[0];
    }

    /// <summary>The numeric value of one painted frame ("12,450" / "$12,450" → 12450).</summary>
    private static long ParseFrame(string frame)
    {
        var digits = new string(frame.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var parsed) ? parsed : -1;
    }

    [TestMethod]
    public async Task AnimatedNumber_CountsUpToItsTarget_AndRetargetsOnBump()
    {
        var page = await OpenAsync();
        var value = ShowcaseValue(page);

        // The component renders the FINAL text itself — the module only repaints that node between frames — so the
        // very first value the browser shows is the target, never a flash of zero. The frame log proves it on a
        // host whose first animation may still be running when the test starts reading.
        Assert.AreEqual("12,450", await FirstPaintedValueAsync(page),
            "the first text of the node is the value the component rendered for readers");
        await Assertions.Expect(value).ToHaveTextAsync("12,450", new LocatorAssertionsToHaveTextOptions { Timeout = 5000 });

        // A bump retargets the running ticker: the intermediate frames have to be observable, and the
        // last one has to be the new target.
        await BumpButton(page).ClickAsync();
        await Assertions.Expect(value).ToHaveTextAsync("13,450", new LocatorAssertionsToHaveTextOptions { Timeout = 5000 });

        var frames = (await PaintedFramesAsync(page)).Select(ParseFrame).ToList();
        Assert.IsTrue(frames.Any(v => v > 12450 && v < 13450),
            "the count-up must be visible between the old and the new target; painted: " + string.Join(", ", frames));
        Assert.AreEqual(13450, frames[^1], "the ticker lands on the exact target, not one step before it");
    }

    [TestMethod]
    public async Task WithoutReducedMotion_TheTickerRuns_AndWithIt_TheValueLandsImmediately()
    {
        var animatedPage = await OpenAsync();
        await BumpButton(animatedPage).ClickAsync();
        await Assertions.Expect(ShowcaseValue(animatedPage))
            .ToHaveTextAsync("13,450", new LocatorAssertionsToHaveTextOptions { Timeout = 5000 });

        var animatedFrames = (await PaintedFramesAsync(animatedPage)).Select(ParseFrame).ToList();
        Assert.IsTrue(animatedFrames.Any(v => v > 12450 && v < 13450),
            "a motion-friendly context animates; painted: " + string.Join(", ", animatedFrames));

        var reducedPage = await OpenAsync(reduceMotion: true);
        Assert.AreEqual("12,450", await FirstPaintedValueAsync(reducedPage),
            "the reduced-motion host renders the final value from the start as well");
        await BumpButton(reducedPage).ClickAsync();
        await Assertions.Expect(ShowcaseValue(reducedPage))
            .ToHaveTextAsync("13,450", new LocatorAssertionsToHaveTextOptions { Timeout = 5000 });

        CollectionAssert.AreEqual(
            new[] { "12,450", "13,450" },
            await PaintedFramesAsync(reducedPage),
            "prefers-reduced-motion: reduce means the target appears at once — no intermediate frame at all");
    }

    [TestMethod]
    public async Task StatCard_DeltaCarriesDirectionAsTextNotOnlyColour()
    {
        var page = await OpenAsync();
        var card = KpiRow(page).Locator(".tm-stat-card").First;

        var trend = card.Locator(".tm-stat-card__trend");
        Assert.AreEqual("up", await trend.GetAttributeAsync("data-trend"));

        var delta = await card.Locator(".tm-stat-card__delta").TextContentAsync();
        StringAssert.Matches(delta ?? string.Empty, new System.Text.RegularExpressions.Regex(@"^\+12[.,]5\s?%$"),
            "the delta is the culture-formatted signed percentage");

        Assert.AreEqual("Increase", (await card.Locator(".tm-stat-card__trend .tm-sr-only").TextContentAsync())?.Trim(),
            "colour is never the only carrier — the direction travels as a visually hidden word");
        Assert.AreEqual("vs. last month", (await card.Locator(".tm-stat-card__comparison").TextContentAsync())?.Trim());

        Assert.AreEqual(1, await card.Locator(".tm-stat-card__sparkline .tm-sparkline svg").CountAsync(),
            "the sparkline renders as a real SVG chart");
        Assert.AreEqual(1, await card.Locator(".tm-stat-card__icon svg[aria-hidden='true']").CountAsync(),
            "the decorative icon stays out of the accessibility tree");

        // The tone is the OUTCOME, not the direction: a falling churn rate (PositiveIsGood=false) is
        // painted exactly like a rise, while a flat delta takes the neutral tone.
        string DeltaColour(int index) => $"getComputedStyle(document.querySelectorAll(\"[data-testid='stat-cards-kpi'] .tm-stat-card\")[{index}].querySelector('.tm-stat-card__delta')).color";
        var riseColour = await page.EvaluateAsync<string>(DeltaColour(0));
        var fallIsGoodColour = await page.EvaluateAsync<string>(DeltaColour(1));
        var flatColour = await page.EvaluateAsync<string>(DeltaColour(2));

        Assert.AreEqual(riseColour, fallIsGoodColour,
            $"a fall is good news for this metric, so it paints like a rise ({riseColour})");
        Assert.AreNotEqual(riseColour, flatColour, "a flat delta takes the neutral tone, not the good one");
    }

    [TestMethod]
    public async Task StatCard_LoadingCard_IsBusyAndSkeletonised()
    {
        var page = await OpenAsync();
        var card = KpiRow(page).Locator(".tm-stat-card").Nth(3);

        Assert.AreEqual("true", await card.GetAttributeAsync("aria-busy"));
        Assert.IsTrue(await card.Locator(".tm-skeleton").CountAsync() >= 3, "three skeletons replace the value");
        Assert.AreEqual(0, await card.Locator(".tm-stat-value").CountAsync(), "no stale value stays on screen");
        StringAssert.Contains(await card.TextContentAsync() ?? string.Empty, "Loading",
            "the state is named for a reader, not only painted");
    }

    [TestMethod]
    public async Task StatCard_WholeCardTargets_AreOneNativeElementEach()
    {
        var page = await OpenAsync();
        var row = page.GetByTestId("stat-cards-interactive");

        var link = row.Locator("a.tm-stat-card").First;
        Assert.AreEqual("data-display", await link.GetAttributeAsync("href"));
        Assert.IsTrue((await link.GetAttributeAsync("class") ?? string.Empty).Contains("tm-stat-card--interactive"));

        var button = row.Locator("button.tm-stat-card").First;
        Assert.AreEqual("button", await button.EvaluateAsync<string>("el => el.tagName.toLowerCase()"));
        Assert.AreEqual("button", await button.GetAttributeAsync("type"));
        Assert.AreEqual(0, await row.Locator("a.tm-stat-card button, button.tm-stat-card a").CountAsync(),
            "one native target per card — never a nested control");

        // A native button is keyboard-activatable without any key emulation from the component. The click the
        // Enter fires is a demo action, so the wait is on the focus the assertion reads: a focused button that
        // lost focus to the activation would show up here, and a native Enter never blurs.
        await button.FocusAsync();
        await Assertions.Expect(button).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 5000 });
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(button).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 5000 });
        Assert.AreEqual("button", await page.EvaluateAsync<string>(
            "() => document.activeElement && document.activeElement.tagName.toLowerCase()"),
            "Enter fires the native click without moving the focus away");
    }

    // ── axe and the visual reference ───────────────────────────────────────────

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

    [TestMethod]
    public async Task Axe_Scoped_To_TheNewSections_FindsNoCriticalOrSeriousViolation()
    {
        var page = await OpenAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        await AssertNoAxeViolationsAsync(page, "[data-testid='stat-cards-kpi']", "KPI cards");
        await AssertNoAxeViolationsAsync(page, "[data-testid='stat-cards-interactive']", "whole-card targets");
        await AssertNoAxeViolationsAsync(page, "[data-testid='animated-numbers']", "animated numbers");
    }

    [TestMethod]
    public async Task Visual_Reference_LightDark_At_1440_And_375()
    {
        var desktop = await OpenAsync(1440, 1000);
        await desktop.GetByTestId("stat-cards-kpi").ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "data-display-kpi-cards-light-1440", "[data-testid='stat-cards-kpi']");

        await desktop.GetByTestId("animated-numbers").ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "data-display-kpi-numbers-light-1440", "[data-testid='animated-numbers']");

        await ToggleDarkModeAsync(desktop);
        await desktop.GetByTestId("stat-cards-kpi").ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "data-display-kpi-cards-dark-1440", "[data-testid='stat-cards-kpi']");
        await desktop.GetByTestId("animated-numbers").ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "data-display-kpi-numbers-dark-1440", "[data-testid='animated-numbers']");

        var phone = await OpenAsync(375, 812, touch: true);
        await phone.GetByTestId("stat-cards-kpi").ScrollIntoViewIfNeededAsync();
        await ShootAsync(phone, "data-display-kpi-cards-light-375", "[data-testid='stat-cards-kpi']");
        await phone.GetByTestId("animated-numbers").ScrollIntoViewIfNeededAsync();
        await ShootAsync(phone, "data-display-kpi-numbers-light-375", "[data-testid='animated-numbers']");
    }

    /// <summary>
    /// Captures a reference shot of ONE section. The settle is a state, not a pause: fonts loaded, no CSS
    /// transition running (both the theme colour transition and the skeleton shimmer count), and the section's
    /// box identical across three consecutive frames — the same signal
    /// <see cref="PlaywrightTestBase.WaitForLayoutToSettleAsync"/> uses for geometry assertions.
    /// </summary>
    private async Task ShootAsync(IPage page, string name, string selector)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
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
public class DataDisplayKpiE2ETests : DataDisplayKpiE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}

/// <summary>
/// The Server lane (plan 7ba95ed6, Fáze 11): the <c>/data-display</c> KPI surface over a real Blazor Server
/// circuit. The ticker is still caught MID-FLIGHT (the JavaScript module runs on the circuit-driven render,
/// never only on the initial markup), the whole-card link/button targets land, reduced motion lands
/// immediately, the delta keeps its direction word next to its tone, and the scoped axe scans hold.
/// </summary>
[TestClass]
[TestCategory("Server")]
public class DataDisplayKpiServerE2ETests : DataDisplayKpiE2EBase
{
    protected override string BaseUrl => "https://localhost:7107";
}

/// <summary>
/// The InteractiveAuto lane: the same KPI cards on the host that prerenders over the Server circuit and then
/// hands the page to WebAssembly, so the ticker, the targets and the reduced-motion branch are proven across
/// the runtime handover too.
/// </summary>
[TestClass]
[TestCategory("InteractiveAuto")]
public class DataDisplayKpiInteractiveAutoE2ETests : DataDisplayKpiE2EBase
{
    protected override string BaseUrl => "https://localhost:7108";
}
