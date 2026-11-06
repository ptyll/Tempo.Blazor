using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan d67b33af phase 9 — the visual and budget sweep of <c>/ai-agent-visualizations</c>: reference screenshots of
/// every section in light and dark at 1440 px, the phone sweep at 375 px with the width and coarse-target assertions,
/// the reduced-motion presentation, and the wall-clock budget spec that holds the simulator at ten events per second
/// for two minutes without growing the heap (category <c>Performance</c>, so the full lane skips it).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class AiAgentVisualizationsVisualE2ETests : AiAgentVisualizationsE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";

    [TestMethod]
    public async Task EverySectionIsShotInLightAndDark_AtDesktopWidth()
    {
        var page = await OpenAsync();

        // A PAUSED run is the reproducible state: the same snapshot, the same pixels.
        foreach (var (section, name) in SectionShots())
        {
            await ScrollToTopAsync(page, section);
            await ShootAsync(page, $"ai-viz-{name}-light-1440");
        }

        await ToggleThemeAsync(page);

        foreach (var (section, name) in SectionShots())
        {
            await ScrollToTopAsync(page, section);
            await ShootAsync(page, $"ai-viz-{name}-dark-1440");
        }

        // The running state is shot once, so the live chrome (counters, last event, now line) has a reference too.
        await ScrollToTopAsync(page, SimulatorSection);
        await PlayAsync(page);
        await ShootAsync(page, "ai-viz-simulator-running-light-1440");

        await ToggleThemeAsync(page);
        await ScrollToAsync(page, SimulatorSection);
        await ShootAsync(page, "ai-viz-simulator-running-dark-1440");
    }

    [TestMethod]
    public async Task ThePhoneKeepsEverySectionInsideThreeHundredSeventyFive()
    {
        var page = await OpenAsync(375, 812, coarsePointer: true);

        foreach (var (section, name) in SectionShots())
        {
            await ScrollToAsync(page, section);
            var box = await page.Locator(section).BoundingBoxAsync();
            Assert.IsNotNull(box, $"{name} is laid out");
            Assert.IsTrue(box!.Width <= 376, $"{name} fits a 375 px phone (width {box.Width})");

            var scrollWidth = await page.Locator(section).EvaluateAsync<double>(
                "element => element.scrollWidth");
            Assert.IsTrue(scrollWidth <= box.Width + 1,
                $"{name} does not widen its own box (content {scrollWidth} vs box {box.Width})");
        }

        var pageWidth = await page.EvaluateAsync<double>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(pageWidth <= 376, $"the page itself never scrolls sideways at 375 px (width {pageWidth})");

        // The page's own action buttons keep a coarse-pointer target.
        foreach (var testId in new[]
                 {
                     "ai-viz-play", "ai-viz-reset", "ai-viz-fan-out-view", "ai-viz-attempts-view",
                     "ai-viz-memory-view", "ai-viz-delegate-view", "ai-viz-conflict-view", "ai-viz-presence-view",
                 })
        {
            var target = await page.Locator($"[data-testid='{testId}']").BoundingBoxAsync();
            Assert.IsNotNull(target, $"{testId} is on the page");
            Assert.IsTrue(target!.Height >= 44,
                $"{testId} keeps a 44 px touch target on a coarse pointer (height {target.Height})");
        }

        foreach (var (section, name) in SectionShots())
        {
            await ScrollToTopAsync(page, section);
            await ShootAsync(page, $"ai-viz-{name}-phone-375");
        }

        await ScrollToAsync(page, SimulatorSection);
        await PlayAsync(page);
        await ShootAsync(page, "ai-viz-simulator-running-phone-375");
    }

    [TestMethod]
    public async Task TheTableAlternativesAreShotOnThePhoneToo()
    {
        var page = await OpenAsync(375, 812, coarsePointer: true);

        await ScrollToAsync(page, FanOutSection);
        await page.Locator("[data-testid='ai-viz-fan-out-view']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-viz-fan-out-table']");
        await ShootAsync(page, "ai-viz-fan-out-table-phone-375");

        await ScrollToAsync(page, ConflictSection);
        await page.Locator("[data-testid='ai-viz-conflict-view']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-viz-conflict-table']");
        await ShootAsync(page, "ai-viz-conflict-table-phone-375");

        await ScrollToAsync(page, PresenceSection);
        await page.Locator("[data-testid='ai-viz-presence-view']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-viz-presence-table']");
        await ShootAsync(page, "ai-viz-presence-table-phone-375");
    }

    /// <summary>
    /// The tablet band (768 px) of the surfaces that WRAP there: the horizontal handoff chain, the delegate board's
    /// auto-fill grid, the week of scheduled runs and the conflict grid. Earlier phases could only guess at these, so
    /// the shots are the reference for a wrapped row and a two-column board.
    /// </summary>
    [TestMethod]
    public async Task TheWrappingSurfacesAreShotAtTabletWidth_AndStayInsideIt()
    {
        var page = await OpenAsync(768, 1024, coarsePointer: true);

        foreach (var (section, name) in new[]
                 {
                     (HandoffSection, "handoff"),
                     (DelegateSection, "delegate"),
                     (RunsSection, "runs"),
                     (ConflictSection, "conflict"),
                     (FanOutSection, "fan-out"),
                 })
        {
            await ScrollToTopAsync(page, section);
            var box = await page.Locator(section).BoundingBoxAsync();
            Assert.IsNotNull(box);
            Assert.IsTrue(box!.Width <= 768, $"{name} fits a 768 px tablet (width {box.Width})");
            await ShootAsync(page, $"ai-viz-{name}-tablet-768");
        }

        var pageWidth = await page.EvaluateAsync<double>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(pageWidth <= 769, $"the page itself never scrolls sideways at 768 px (width {pageWidth})");
    }

    [TestMethod]
    public async Task ReducedMotionKeepsThePresenceCursorsStill()
    {
        var page = await OpenAsync(reduceMotion: true);
        await ScrollToAsync(page, PresenceSection);

        var duration = await page.Locator("[data-testid='ai-viz-presence'] [data-presence-marker]").First
            .EvaluateAsync<string>("marker => getComputedStyle(marker).transitionDuration");
        Assert.AreEqual("0s", duration, "prefers-reduced-motion turns the travelling cursor into a jump");

        await ShootAsync(page, "ai-viz-presence-reduced-motion-1440");
    }

    private static (string Section, string Name)[] SectionShots() =>
    [
        (SimulatorSection, "simulator"),
        (LanesSection, "lanes"),
        (GraphSection, "graph"),
        (FanOutSection, "fan-out"),
        (AttemptsSection, "attempts"),
        (HandoffSection, "handoff"),
        (FallbackSection, "fallback"),
        (AutonomySection, "autonomy"),
        (MemorySection, "memory"),
        (DelegateSection, "delegate"),
        (RunsSection, "runs"),
        (ConflictSection, "conflict"),
        (PresenceSection, "presence"),
    ];
}

/// <summary>
/// The phase's wall-clock budget: the simulator holds ten events per second for two minutes without growing the heap
/// and without locking the UI. Excluded from the full lane by its category; run it from the performance lane with the
/// machine idle.
/// </summary>
[TestClass]
[TestCategory("Performance")]
public class AiAgentVisualizationsPerformanceE2ETests : AiAgentVisualizationsE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";

    [TestMethod]
    public async Task TenEventsPerSecondForTwoMinutes_KeepTheHeapFlatAndTheUiResponsive()
    {
        const int windows = 12;
        const int windowMs = 10_000;

        var page = await OpenAsync();
        await PlayAsync(page);

        var samples = new List<(int Window, long Heap, int Events)>();
        var lastEvents = int.Parse(await TextAsync(page, "ai-viz-events"), CultureInfo.InvariantCulture);

        for (var window = 1; window <= windows; window++)
        {
            await page.WaitForTimeoutAsync(windowMs);

            var heap = await page.EvaluateAsync<long>(
                "() => { if (window.gc) { window.gc(); } return performance.memory ? performance.memory.usedJSHeapSize : 0; }");
            var events = int.Parse(await TextAsync(page, "ai-viz-events"), CultureInfo.InvariantCulture);
            samples.Add((window, heap, events));

            var applied = events - lastEvents;
            lastEvents = events;

            // The rate is the budget: ten events per second, with the paint gate folding ticks instead of dropping them.
            Assert.IsTrue(applied is >= 60 and <= 130,
                $"window {window}: {applied} events applied in {windowMs / 1000}s at 1× (budget 100 ± 30)");
        }

        // The UI stays live: the pause button reacts and the read-out stops.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await page.Locator("[data-testid='ai-viz-play']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-viz-state']\")?.textContent?.trim() === 'Paused'",
            new PageWaitForFunctionOptions { Timeout = 5000 });
        stopwatch.Stop();
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 3000,
            $"the page answers a press within three seconds after two minutes of streaming ({stopwatch.ElapsedMilliseconds} ms)");

        // The heap plateaus: after the warm-up windows, two minutes of streaming must not add megabytes.
        var warm = samples[2].Heap;
        var last = samples[^1].Heap;
        Assert.IsTrue(warm > 0 && last > 0, "the browser exposes performance.memory");
        var growthMb = (last - warm) / (1024d * 1024d);
        Assert.IsTrue(growthMb < 16d,
            $"the heap grew {growthMb:0.0} MB between window 3 and window {windows} (budget 16 MB; "
            + $"samples {string.Join(", ", samples.Select(sample => $"{sample.Window}:{sample.Heap / (1024 * 1024)}MB"))})");
    }
}
