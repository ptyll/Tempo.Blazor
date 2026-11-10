using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 3e96f6d8 phase 7 — the PAGE-WIDE specs of the <c>/ai-agents</c> scenario: the 375 px layout, the scoped axe scan
/// of every section, the selected graph in both themes, the light/dark visual sweep and the <c>/ai-agent-visualizations</c>
/// captures.
/// <para>
/// The class, its base and every test name are shared with the main scenario file (one partial per concern), so each file
/// stays inside the repository's 1200-line source ceiling (N270/F13).
/// </para>
/// </summary>
public partial class AiAgentsE2ETests
{
    [TestMethod]
    public async Task ThePhoneKeepsThePageInside375Px()
    {
        var page = await OpenAsync(375, 812, coarsePointer: true);

        foreach (var section in new[]
                 {
                     PlanSection, TreeSection, LanesSection, GraphSection, FanOutSection, AttemptsSection,
                     HandoffSection, FallbackSection, AutonomySection, MemorySection,
                     DelegateSection, RunsSection, ConflictSection, PresenceSection,
                     MonitorSection, ModelsSection, ContextSection, PromptsSection,
                 })
        {
            await ScrollToSection(page, section);
            var box = await Section(page, section).BoundingBoxAsync();
            Assert.IsNotNull(box, section);
            Assert.IsTrue(box!.X >= 0 && box.Width <= 375,
                $"{section} stays inside a 375 px phone (x {box.X}, width {box.Width})");
        }

        var width = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(width <= 376, $"the page never scrolls sideways (scrollWidth {width})");

        // The plan's own actions keep the 44 px touch target on a coarse pointer.
        var run = await Plan(page).Locator("[data-testid='agent-plan-run']").BoundingBoxAsync();
        Assert.IsNotNull(run);
        Assert.IsTrue(run!.Height >= 44, $"the Run action keeps a touch target (height {run.Height})");

        await ScrollToSection(page, PlanSection);
        await ShootAsync(page, "ai-agents-plan-phone-375");
        await ScrollToSection(page, PromptsSection);
        await ShootAsync(page, "ai-agents-prompts-phone-375");
    }

    [TestMethod]
    public async Task ScopedAxeScan_HasNoCriticalOrSeriousViolations()
    {
        var page = await OpenAsync();

        await AssertNoSeriousAxeViolationsAsync(page,
            "ai-agents-plan-section", "ai-agents-tree-section", "ai-agents-lanes-section",
            "ai-agents-graph-section", "ai-agents-fan-out-section", "ai-agents-attempts-section",
            "ai-agents-handoff-section", "ai-agents-fallback-section",
            "ai-agents-autonomy-section", "ai-agents-memory-section",
            "ai-agents-delegate-section", "ai-agents-runs-section",
            "ai-agents-conflict-section", "ai-agents-presence-section",
            "ai-agents-monitor-section", "ai-agents-models-section",
            "ai-agents-context-section", "ai-agents-prompts-section");
    }

    /// <summary>
    /// The dimmed neighbourhood is still readable in BOTH themes: selecting a node dims everything that is not one
    /// connection away, and that dim is a TOKEN change instead of an opacity, so the same scoped axe scan that passes
    /// on the resting graph also passes with the selection painted (WCAG 1.4.3, worst in dark mode).
    /// </summary>
    [TestMethod]
    public async Task TheSelectedGraphPassesAScopedAxeScan_InBothThemes()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, GraphSection);

        await page.Locator("[data-testid='agent-graph-node'][data-node-id='planner']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-agents-graph-detail']",
            new PageWaitForSelectorOptions { Timeout = 10000 });

        var dimmed = page.Locator("[data-testid='agent-graph-node'].tm-ai-agent-graph__node--dimmed");
        Assert.IsTrue(await dimmed.CountAsync() > 0,
            "a selection dims the nodes that are not one connection away, which is the state under review");

        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-graph-section");
        await ShootAsync(page, "ai-agents-graph-selected-light-1440");

        await ToggleThemeAsync(page);
        await ScrollToSection(page, GraphSection);
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-graph-section");
        await ShootAsync(page, "ai-agents-graph-selected-dark-1440");
    }

    [TestMethod]
    public async Task VisualSweep_LightAndDark_DesktopAndPhone()
    {
        var desktop = await OpenAsync(1440, 1000);

        await ScrollToSection(desktop, PlanSection);
        await ShootAsync(desktop, "ai-agents-plan-light-1440");

        await ScrollToSection(desktop, TreeSection);
        await ShootAsync(desktop, "ai-agents-tree-light-1440");

        await ScrollToSection(desktop, LanesSection);
        await ShootAsync(desktop, "ai-agents-lanes-idle-light-1440");

        await ScrollToSection(desktop, GraphSection);
        await ShootAsync(desktop, "ai-agents-graph-idle-light-1440");

        await ScrollToSection(desktop, HandoffSection);
        await ShootAsync(desktop, "ai-agents-handoff-idle-light-1440");

        await ScrollToSection(desktop, FallbackSection);
        await ShootAsync(desktop, "ai-agents-fallback-idle-light-1440");

        await ScrollToSection(desktop, AutonomySection);
        await ShootAsync(desktop, "ai-agents-autonomy-idle-light-1440");

        await ScrollToSection(desktop, MemorySection);
        await ShootAsync(desktop, "ai-agents-memory-idle-light-1440");

        await ScrollToSection(desktop, ConflictSection);
        await ShootAsync(desktop, "ai-agents-conflict-idle-light-1440");

        await ScrollToSection(desktop, PresenceSection);
        await ShootAsync(desktop, "ai-agents-presence-idle-light-1440");

        await ScrollToSection(desktop, MonitorSection);
        await ShootAsync(desktop, "ai-agents-monitor-light-1440");

        await ScrollToSection(desktop, ModelsSection);
        await ShootAsync(desktop, "ai-agents-models-idle-light-1440");

        await ScrollToSection(desktop, ContextSection);
        await ShootAsync(desktop, "ai-agents-context-idle-light-1440");

        await ScrollToSection(desktop, PromptsSection);
        await ShootAsync(desktop, "ai-agents-prompts-idle-light-1440");

        await ToggleThemeAsync(desktop);
        await ScrollToSection(desktop, PlanSection);
        await ShootAsync(desktop, "ai-agents-plan-dark-1440");
        await ScrollToSection(desktop, LanesSection);
        await ShootAsync(desktop, "ai-agents-lanes-dark-1440");
        await ScrollToSection(desktop, GraphSection);
        await ShootAsync(desktop, "ai-agents-graph-dark-1440");
        await ScrollToSection(desktop, HandoffSection);
        await ShootAsync(desktop, "ai-agents-handoff-dark-1440-sweep");
        await ScrollToSection(desktop, FallbackSection);
        await ShootAsync(desktop, "ai-agents-fallback-dark-1440");
        await ScrollToSection(desktop, AutonomySection);
        await ShootAsync(desktop, "ai-agents-autonomy-dark-1440");
        await ScrollToSection(desktop, MemorySection);
        await ShootAsync(desktop, "ai-agents-memory-dark-1440");
        await ScrollToSection(desktop, ConflictSection);
        await ShootAsync(desktop, "ai-agents-conflict-dark-1440-sweep");
        await ScrollToSection(desktop, PresenceSection);
        await ShootAsync(desktop, "ai-agents-presence-dark-1440-sweep");
        await ScrollToSection(desktop, ContextSection);
        await ShootAsync(desktop, "ai-agents-context-dark-1440");
        await ScrollToSection(desktop, PromptsSection);
        await ShootAsync(desktop, "ai-agents-prompts-dark-1440");

        var phone = await OpenAsync(375, 812, coarsePointer: true);
        await ScrollToSection(phone, LanesSection);
        await ShootAsync(phone, "ai-agents-lanes-phone-375");
        await ScrollToSection(phone, GraphSection);
        await ShootAsync(phone, "ai-agents-graph-phone-375");
        await ScrollToSection(phone, HandoffSection);
        await ShootAsync(phone, "ai-agents-handoff-phone-375");
        await ScrollToSection(phone, FallbackSection);
        await ShootAsync(phone, "ai-agents-fallback-phone-375");
        await ScrollToSection(phone, AutonomySection);
        await ShootAsync(phone, "ai-agents-autonomy-phone-375");
        await ScrollToSection(phone, MemorySection);
        await ShootAsync(phone, "ai-agents-memory-phone-375");
        await ScrollToSection(phone, ConflictSection);
        await ShootAsync(phone, "ai-agents-conflict-phone-375");
        await ScrollToSection(phone, PresenceSection);
        await ShootAsync(phone, "ai-agents-presence-phone-375");
        await ScrollToSection(phone, MonitorSection);
        await ShootAsync(phone, "ai-agents-monitor-phone-375");
        await ScrollToSection(phone, ContextSection);
        await ShootAsync(phone, "ai-agents-context-phone-375");
    }

    /// <summary>
    /// FZE-3 — the fan-out opts into <c>ColumnsMode.Auto</c>: at 375 px the grid measures its own box and renders
    /// FEWER columns than the 1440 px fallback, and the page never scrolls sideways. The measured number is the
    /// single source for the stylesheet and the window math, so what a reader sees is what the aria-colcount says.
    /// </summary>
    [TestMethod]
    public async Task TheFanOutMeasuresItsOwnColumns_OnAPhone_NeverScrollingSideways()
    {
        var phone = await OpenAsync(375, 812, coarsePointer: true);
        await StartRunUntilFailureAsync(phone);
        await ScrollToSection(phone, FanOutSection);

        var grid = phone.Locator("[data-testid='ai-agents-fan-out']");
        await grid.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        var phoneColumns = int.Parse(
            (await grid.GetAttributeAsync("data-columns")) ?? "0");
        phoneColumns.Should().BeLessThan(4,
            "a 375 px box fits fewer tiles than the 4-column fallback");

        var noSidewaysScroll = await phone.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1");
        noSidewaysScroll.Should().BeTrue("the measured grid never overflows the page sideways");

        await ShootAsync(phone, "ai-agents-fan-out-phone-375-auto-columns");

        var desktop = await OpenAsync();
        await StartRunUntilFailureAsync(desktop);
        await ScrollToSection(desktop, FanOutSection);
        var desktopGrid = desktop.Locator("[data-testid='ai-agents-fan-out']");
        var desktopColumns = int.Parse(
            (await desktopGrid.GetAttributeAsync("data-columns")) ?? "0");
        desktopColumns.Should().BeGreaterThan(phoneColumns,
            "the same grid measures wider on a desktop viewport");
        await ShootAsync(desktop, "ai-agents-fan-out-desktop-auto-columns");
    }

    /// <summary>
    /// FZE-3 — the visualizations page hosts the fan-out with <c>DetailPresentation.Popover</c>: focusing a tile
    /// opens the anchored detail, Escape closes it and hands the focus back to the tile, and the in-flow detail
    /// region never exists.
    /// </summary>
    [TestMethod]
    public async Task TheVisualizationsFanOutPresentsTheDetailInAPopover_EscapeReturnsFocus()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/ai-agent-visualizations");
        await page.WaitForSelectorAsync("[data-testid='ai-viz-fan-out'] [data-testid='fan-out-tile']",
            new PageWaitForSelectorOptions { Timeout = 30000 });

        var grid = page.Locator("[data-testid='ai-viz-fan-out']");
        Assert.AreEqual(0, await page.Locator("[data-testid='fan-out-detail']").CountAsync(),
            "the popover presentation renders no in-flow detail region");

        var tile = grid.Locator("[data-testid='fan-out-tile']").First;
        await tile.FocusAsync();
        await page.WaitForSelectorAsync("[data-testid='fan-out-detail']",
            new PageWaitForSelectorOptions { Timeout = 10000 });
        await page.WaitForTimeoutAsync(400);
        // The shared ShootAsync blurs the active element (and moves the mouse) before the shot; a blur
        // dismisses the popover, so the open-panel screenshots frame the viewport directly.
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, "ai-viz-fan-out-popover-light.png"),
        });

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(400);
        Assert.AreEqual(0, await page.Locator("[data-testid='fan-out-detail']").CountAsync(),
            "Escape closes the popover");
        Assert.AreEqual("button", await page.EvaluateAsync<string>(
            "() => document.activeElement?.tagName?.toLowerCase() ?? ''"),
            "the focus returns to the tile button that owned the detail");

        await ToggleThemeAsync(page);
        await tile.FocusAsync();
        await page.WaitForSelectorAsync("[data-testid='fan-out-detail']",
            new PageWaitForSelectorOptions { Timeout = 10000 });
        await page.WaitForTimeoutAsync(400);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, "ai-viz-fan-out-popover-dark.png"),
        });
    }
}
