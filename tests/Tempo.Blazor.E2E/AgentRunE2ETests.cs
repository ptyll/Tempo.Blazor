using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 3e96f6d8 phase 3 — <c>TmAgentRunTree</c> and <c>TmAgentMonitor</c> in a real browser. The assertions
/// read behaviour rather than markup: the run is a treegrid of indented rows whose parent rows show their
/// subtree totals (marked as a summary), the reader walks it with the APG tree keys and Enter selects (the
/// page's own read-out and detail panel follow), the "show only" filter keeps the chain to the failure, a
/// malformed run shows its orphan and drops its cycle, the monitor ticks while the run is going, folds into
/// its bar on a click, keeps a pending approval visible while folded, and the approval slot answers. The phone
/// width keeps the grid scrolling INSIDE the component (the page never scrolls sideways) and the toggle at the
/// 44 px touch target; a scoped axe scan of three sections finds no critical or serious violation. Reference
/// screenshots (light, dark, phone) land in <c>TestResults/agent-run</c>.
/// </summary>
public abstract class AgentRunE2EBase : PlaywrightTestBase
{
    private const string TreeSection = "[data-testid='agent-run-tree-section']";
    private const string MonitorSection = "[data-testid='agent-run-monitor-section']";
    private const string CollapsedSection = "[data-testid='agent-run-collapsed-section']";
    private const string EdgeSection = "[data-testid='agent-run-edge-section']";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "agent-run");

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

    private static ILocator Tree(IPage page) => page.Locator("[data-testid='agent-run-tree']");

    private static ILocator Monitor(IPage page) => page.Locator("[data-testid='agent-run-monitor']");

    private static ILocator TreeRows(IPage page) => Tree(page).Locator("[data-testid='agent-run-tree-row']");

    private static Task<string[]> TreeStatusesAsync(IPage page)
        => TreeRows(page).EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.status)");

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1100, bool reduceMotion = false,
        bool coarsePointer = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            ReducedMotion = reduceMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            IgnoreHTTPSErrors = true,
            HasTouch = coarsePointer,
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/agent-run");
        await page.WaitForSelectorAsync(
            $"{TreeSection} [data-testid='agent-run-tree'] [data-testid='agent-run-tree-row']",
            new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    [TestMethod]
    public async Task TheRunTreeExposesItsStructure_AndItsSubtreeTotals()
    {
        var page = await OpenAsync();

        Assert.AreEqual(4, await TreeRows(page).CountAsync());
        var levels = await TreeRows(page).EvaluateAllAsync<string[]>(
            "rows => rows.map(row => row.getAttribute('aria-level'))");
        CollectionAssert.AreEqual(new[] { "1", "2", "3", "2" }, levels);
        CollectionAssert.AreEqual(new[] { "running", "done", "failed", "pending" },
            await TreeStatusesAsync(page));

        var headers = await Tree(page).Locator("[role='columnheader']").AllTextContentsAsync();
        CollectionAssert.AreEqual(new[] { "Name", "Status", "Model", "Time", "Cost", "Tokens" },
            headers.Select(header => header.Trim()).ToArray());

        // The parent row states that its numbers are the whole subtree's, not its own.
        var rootCost = TreeRows(page).First.Locator(".tm-ai-agent-run-tree__cost");
        Assert.AreEqual("true", await rootCost.GetAttributeAsync("data-rollup"));
        StringAssert.StartsWith(await rootCost.GetAttributeAsync("aria-label"),
            "Total for Reconciliation agent including its sub-agents:");

        // A failure keeps its word and carries the reason in the cell's accessible name.
        var failed = TreeRows(page).Nth(2).Locator(".tm-ai-agent-run-tree__status");
        Assert.AreEqual("Failed: The ledger API answered 503.", await failed.GetAttributeAsync("aria-label"));

        // The selected row feeds the page's own read-out and detail panel.
        var readout = (await page.Locator("[data-testid='agent-run-selected-readout']").TextContentAsync())?.Trim();
        Assert.AreEqual("Selected: Reconciliation agent", readout);
        var detailStatus = (await page.Locator("[data-testid='agent-run-detail-status']").TextContentAsync())?.Trim();
        Assert.AreEqual("Running", detailStatus);
        var total = (await page.Locator("[data-testid='agent-run-detail-total']").TextContentAsync())?.Trim();
        StringAssert.Contains(total, "3 sub-agents");

        // The run's single polite live region is present, and the ticking clocks are not live.
        Assert.AreEqual("polite",
            await Tree(page).Locator("[data-testid='agent-run-tree-announcement']").GetAttributeAsync("aria-live"));
        Assert.AreEqual("off",
            await TreeRows(page).First.Locator(".tm-ai-agent-run-tree__time").GetAttributeAsync("aria-live"));
    }

    [TestMethod]
    public async Task TheKeyboardWalksTheGrid_AndEnterSelects()
    {
        var page = await OpenAsync();

        await TreeRows(page).First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(250);

        Assert.AreEqual("Selected: Plan the reconciliation",
            (await page.Locator("[data-testid='agent-run-selected-readout']").TextContentAsync())?.Trim());
        var detail = (await page.Locator("[data-testid='agent-run-node-detail']").TextContentAsync())?.Trim();
        StringAssert.Contains(detail, "Plan the reconciliation");
        Assert.AreEqual("Completed",
            (await page.Locator("[data-testid='agent-run-detail-status']").TextContentAsync())?.Trim());

        // The roving tab stop moved with the focus, and the row is the selected one.
        Assert.AreEqual("true", await TreeRows(page).Nth(1).GetAttributeAsync("aria-selected"));
        Assert.AreEqual("0", await TreeRows(page).Nth(1).GetAttributeAsync("tabindex"));

        // End jumps to the last visible row; Enter selects it.
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(200);
        Assert.AreEqual("Selected: Write the summary",
            (await page.Locator("[data-testid='agent-run-selected-readout']").TextContentAsync())?.Trim());

        // The collapse keys hide and restore the descendants.
        await TreeRows(page).First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.WaitForTimeoutAsync(250);
        Assert.AreEqual(1, await TreeRows(page).CountAsync());
        await page.Keyboard.PressAsync("ArrowRight");
        await page.WaitForTimeoutAsync(250);
        Assert.AreEqual(4, await TreeRows(page).CountAsync());
    }

    [TestMethod]
    public async Task TheShowOnlyFilter_KeepsTheChainToTheFailure()
    {
        var page = await OpenAsync();

        await Tree(page).Locator(".tm-segmented__option", new LocatorLocatorOptions { HasTextString = "Failed" })
            .First.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual(3, await TreeRows(page).CountAsync());
        CollectionAssert.AreEqual(new[] { "running", "done", "failed" }, await TreeStatusesAsync(page));

        await Tree(page).Locator(".tm-segmented__option", new LocatorLocatorOptions { HasTextString = "All" })
            .First.ClickAsync();
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual(4, await TreeRows(page).CountAsync());
    }

    [TestMethod]
    public async Task TheMalformedRun_ShowsItsOrphanAndDropsItsCycle()
    {
        var page = await OpenAsync();
        await page.Locator(EdgeSection).ScrollIntoViewIfNeededAsync();

        var edgeRows = page.Locator("[data-testid='agent-run-tree-edge'] [data-testid='agent-run-tree-row']");
        Assert.AreEqual(3, await edgeRows.CountAsync());
        Assert.AreEqual(1, await page.Locator("[data-testid='agent-run-tree-edge'] [data-testid='agent-run-tree-orphan']")
            .CountAsync());
        var text = await page.Locator("[data-testid='agent-run-tree-edge']").TextContentAsync();
        Assert.IsFalse(text!.Contains("Loops to A"), "a parent cycle renders no row");
    }

    [TestMethod]
    public async Task TheMonitorTicks_FoldsAndKeepsAPendingApprovalVisible()
    {
        var page = await OpenAsync();
        await page.Locator(MonitorSection).ScrollIntoViewIfNeededAsync();

        var elapsed = Monitor(page).Locator("[data-testid='agent-run-monitor-elapsed']");
        var before = (await elapsed.TextContentAsync())?.Trim();
        await page.WaitForTimeoutAsync(2300);
        var after = (await elapsed.TextContentAsync())?.Trim();
        Assert.AreNotEqual(before, after, "a running monitor counts its live clock");
        Assert.AreEqual("off", await elapsed.GetAttributeAsync("aria-live"));

        Assert.AreEqual(3, await Monitor(page).Locator("[data-testid='agent-run-monitor-step']").CountAsync());
        Assert.AreEqual(1, await Monitor(page).Locator("[data-testid='agent-run-monitor-approval']").CountAsync());

        await Monitor(page).Locator("[data-testid='agent-run-monitor-toggle']").ClickAsync();
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual("true", await Monitor(page).GetAttributeAsync("data-collapsed"));
        Assert.AreEqual("false", await Monitor(page).Locator("[data-testid='agent-run-monitor-toggle']")
            .GetAttributeAsync("aria-expanded"));
        Assert.AreEqual(0, await Monitor(page).Locator("[data-testid='agent-run-monitor-step']").CountAsync(),
            "the folded bar drops the timeline");
        StringAssert.Contains(
            await Monitor(page).Locator("[data-testid='agent-run-monitor-announcement']").TextContentAsync() ?? "",
            "Monitor collapsed");

        // The essentials survive the fold, including the pending decision.
        Assert.AreEqual("Look up the ledger",
            (await Monitor(page).Locator("[data-testid='agent-run-monitor-current']").TextContentAsync())?.Trim());
        Assert.AreEqual(1,
            await Monitor(page).Locator("[data-testid='agent-run-monitor-approval-marker']").CountAsync());

        await Monitor(page).Locator("[data-testid='agent-run-monitor-toggle']").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual("false", await Monitor(page).GetAttributeAsync("data-collapsed"));
        Assert.AreEqual(3, await Monitor(page).Locator("[data-testid='agent-run-monitor-step']").CountAsync());
    }

    [TestMethod]
    public async Task TheApprovalSlotAnswers_AndTheButtonsDriveTheRun()
    {
        var page = await OpenAsync();
        await page.Locator(MonitorSection).ScrollIntoViewIfNeededAsync();

        await page.Locator("[data-testid='agent-run-approve']").ClickAsync();
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual("The page received Approve.",
            (await page.Locator("[data-testid='agent-run-decision']").TextContentAsync())?.Trim());
        Assert.AreEqual(0, await Monitor(page).Locator("[data-testid='agent-run-monitor-approval']").CountAsync());

        await page.Locator("[data-testid='agent-run-fail-step']").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual("failed", await Monitor(page).GetAttributeAsync("data-status"));
        Assert.AreEqual("Failed",
            (await Monitor(page).Locator("[data-testid='agent-run-monitor-state']").TextContentAsync())?.Trim(),
            "the failure is a WORD, not only a colour");

        await page.Locator("[data-testid='agent-run-advance']").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual("running", (await TreeStatusesAsync(page))[2], "the page rewrote its own snapshot");
    }

    [TestMethod]
    public async Task TheCollapsedSection_KeepsItsBar_AndDropsTheTimeline()
    {
        var page = await OpenAsync();
        await page.Locator(CollapsedSection).ScrollIntoViewIfNeededAsync();

        var collapsed = page.Locator("[data-testid='agent-run-monitor-collapsed']");
        Assert.AreEqual("true", await collapsed.GetAttributeAsync("data-collapsed"));
        Assert.AreEqual(0, await collapsed.Locator("[data-testid='agent-run-monitor-step']").CountAsync());
        Assert.AreEqual(1, await collapsed.Locator("[data-testid='agent-run-monitor-approval-marker']").CountAsync());
        StringAssert.Contains(await collapsed.TextContentAsync() ?? "", "Waiting for your approval");
    }

    [TestMethod]
    public async Task ThePhoneLayout_KeepsTheGridInside_AndItsTargetsAt44Px()
    {
        var page = await OpenAsync(375, 812, coarsePointer: true);

        var scrollWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(scrollWidth <= 376, $"the page does not scroll sideways (scrollWidth {scrollWidth})");

        // The grid is its own horizontal scroller: the page never scrolls, the component does.
        var grid = Tree(page).Locator("[data-testid='agent-run-tree-grid']");
        var metrics = await grid.EvaluateAsync<string>(
            """
            grid => JSON.stringify({
                scrollWidth: grid.scrollWidth,
                clientWidth: grid.clientWidth,
                headWidth: grid.querySelector('.tm-ai-agent-run-tree__head').scrollWidth,
                rowWidth: grid.querySelector('.tm-ai-agent-run-tree__row').scrollWidth,
                overflowX: getComputedStyle(grid).overflowX
            })
            """);
        var scrolls = await grid.EvaluateAsync<bool>("grid => grid.scrollWidth > grid.clientWidth + 1");
        Assert.IsTrue(scrolls, $"a phone scrolls the treegrid sideways inside the component ({metrics})");

        await page.Locator(MonitorSection).ScrollIntoViewIfNeededAsync();
        var toggle = await Monitor(page).Locator("[data-testid='agent-run-monitor-toggle']").BoundingBoxAsync();
        Assert.IsNotNull(toggle);
        Assert.IsTrue(toggle!.Width >= 43.5 && toggle.Height >= 43.5,
            $"the fold toggle reaches the 44 px touch target ({toggle.Width}x{toggle.Height})");

        var rowBox = await TreeRows(page).First.BoundingBoxAsync();
        Assert.IsNotNull(rowBox);
        Assert.IsTrue(rowBox!.Height >= 43.5, $"a row reaches the 44 px touch target (height {rowBox.Height})");
    }

    [TestMethod]
    public async Task ScopedAxeScan_HasNoCriticalOrSeriousViolations()
    {
        var page = await OpenAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        foreach (var testId in new[]
                 {
                     "agent-run-tree-section", "agent-run-monitor-section", "agent-run-edge-section",
                 })
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
                """,
                $"[data-testid='{testId}']");
            Assert.AreEqual(0, violations.Length, $"axe [{testId}]: " + string.Join(" | ", violations));
        }
    }

    [TestMethod]
    public async Task VisualSweep_LightAndDark_DesktopAndPhone()
    {
        var desktop = await OpenAsync(1440, 1100);

        await ScrollToSection(desktop, TreeSection);
        await ShootAsync(desktop, "agent-run-tree-light-1440");

        await ScrollToSection(desktop, MonitorSection);
        await ShootAsync(desktop, "agent-run-monitor-light-1440");

        await ScrollToSection(desktop, CollapsedSection);
        await ShootAsync(desktop, "agent-run-collapsed-light-1440");

        await ToggleThemeAsync(desktop);
        await ScrollToSection(desktop, MonitorSection);
        await ShootAsync(desktop, "agent-run-monitor-dark-1440");
        await ScrollToSection(desktop, TreeSection);
        await ShootAsync(desktop, "agent-run-tree-dark-1440");

        var phone = await OpenAsync(375, 812, coarsePointer: true);
        await ScrollToSection(phone, TreeSection);
        await ShootAsync(phone, "agent-run-tree-light-375");

        await ToggleThemeAsync(phone);
        await ScrollToSection(phone, MonitorSection);
        await ShootAsync(phone, "agent-run-monitor-dark-375");

        await ScrollToSection(phone, CollapsedSection);
        await ShootAsync(phone, "agent-run-collapsed-dark-375");
    }

    private static Task ScrollToSection(IPage page, string selector)
        => page.Locator(selector).ScrollIntoViewIfNeededAsync();

    /// <summary>Switches the demo's own theme (the header toggle), so the whole frame repaints.</summary>
    private static async Task ToggleThemeAsync(IPage page)
    {
        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await page.WaitForTimeoutAsync(600);
    }

    /// <summary>
    /// Frames the section that is being reviewed, in the viewport: a full-page shot of a long demo page
    /// simply frames the top of the page and proves nothing about this component.
    /// </summary>
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
public class AgentRunE2ETests : AgentRunE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}
