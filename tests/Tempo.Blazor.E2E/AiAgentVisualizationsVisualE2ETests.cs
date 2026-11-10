using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan d67b33af phase 9 — the visual sweep of <c>/ai-agent-visualizations</c>: reference screenshots of every section
/// in light and dark at 1440 px, the phone sweep at 375 px with the width and coarse-target assertions, and the
/// reduced-motion presentation. The wall-clock budget spec that holds the simulator at ten events per second for two
/// minutes lives in <c>AiAgentVisualizationsE2ELanes.Performance.cs</c> with its lane base (category
/// <c>Performance</c>, so the full lane skips it).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class AiAgentVisualizationsVisualE2ETests : AiAgentVisualizationsPageBase
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
