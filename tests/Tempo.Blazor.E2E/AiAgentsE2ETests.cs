using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 3e96f6d8 phase 7 — the <c>/ai-agents</c> demo page drives the WHOLE AI package through one scenario.
/// The assertions read behaviour rather than markup: the plan is reviewed, a step reorders by keyboard and by a
/// pointer drag on its grip, the run walks its steps on the component's own clock, one step fails and Retry
/// resumes it, the last step waits for a REAL human decision in the monitor's approval slot, the run tree walks
/// with the APG tree keys, the model picker carries the pick AND its effort, the prompt library fills a template
/// and hands the assembled prompt to the page, and a scoped axe scan of every section finds no critical or
/// serious violation. Reference screenshots (light, dark, phone) land in <c>TestResults/ai-agents</c>.
/// </summary>
public abstract class AiAgentsE2EBase : PlaywrightTestBase
{
    protected const string PlanSection = "[data-testid='ai-agents-plan-section']";
    protected const string TreeSection = "[data-testid='ai-agents-tree-section']";
    protected const string LanesSection = "[data-testid='ai-agents-lanes-section']";
    protected const string GraphSection = "[data-testid='ai-agents-graph-section']";
    protected const string FanOutSection = "[data-testid='ai-agents-fan-out-section']";
    protected const string AttemptsSection = "[data-testid='ai-agents-attempts-section']";
    protected const string MonitorSection = "[data-testid='ai-agents-monitor-section']";
    protected const string ModelsSection = "[data-testid='ai-agents-models-section']";
    protected const string ContextSection = "[data-testid='ai-agents-context-section']";
    protected const string PromptsSection = "[data-testid='ai-agents-prompts-section']";

    protected string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "ai-agents");

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

    protected static ILocator Section(IPage page, string selector) => page.Locator(selector);

    protected static ILocator Plan(IPage page) => page.Locator("[data-testid='ai-agents-plan']");

    protected static ILocator RunningPlan(IPage page) => page.Locator("[data-testid='ai-agents-plan-run']");

    protected static ILocator PlanRows(IPage page)
        => RunningPlan(page).Locator("[data-testid='agent-plan-step']");

    protected static ILocator TreeRows(IPage page)
        => page.Locator("[data-testid='ai-agents-tree'] [data-testid='agent-run-tree-row']");

    protected static Task<string[]> PlanStatusesAsync(IPage page)
        => PlanRows(page).EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.status)");

    protected static Task<string[]> PlanTitlesAsync(IPage page)
        => page.Locator("[data-testid='ai-agents-plan'] [data-testid='agent-plan-step-title']")
            .EvaluateAllAsync<string[]>("rows => rows.map(row => row.textContent.trim())");

    protected static async Task<string> TextAsync(IPage page, string testId)
        => ((await page.Locator($"[data-testid='{testId}']").TextContentAsync()) ?? string.Empty).Trim();

    protected async Task<IPage> OpenAsync(int width = 1440, int height = 1100, bool coarsePointer = false,
        bool reduceMotion = false)
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
        await GotoAppReadyAsync(page, $"{BaseUrl}/ai-agents");
        await page.WaitForSelectorAsync(
            $"{PlanSection} [data-testid='ai-agents-plan'] [data-testid='agent-plan-step']",
            new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    /// <summary>Starts the simulated run and waits until the purpose-built failure is on screen.</summary>
    protected static async Task StartRunUntilFailureAsync(IPage page)
    {
        await Section(page, PlanSection).ScrollIntoViewIfNeededAsync();
        await Plan(page).Locator("[data-testid='agent-plan-run']").ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='ai-agents-plan-run']",
            new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.Locator("[data-testid='ai-agents-plan-run'] [data-testid='agent-plan-step'][data-status='failed']")
            .First.WaitForAsync(new LocatorWaitForOptions { Timeout = 20000 });
    }

    protected static async Task ScrollToSection(IPage page, string selector)
        => await page.Locator(selector).ScrollIntoViewIfNeededAsync();

    /// <summary>Switches the demo's own theme (the header toggle), so the whole frame repaints.</summary>
    protected static async Task ToggleThemeAsync(IPage page)
    {
        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await page.WaitForTimeoutAsync(600);
    }

    /// <summary>
    /// Frames the section that is being reviewed, in the viewport: a full-page shot of a long demo page simply
    /// frames the top of the page and proves nothing about this component.
    /// </summary>
    protected async Task ShootAsync(IPage page, string name)
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

    /// <summary>A scoped axe scan of every section of the page.</summary>
    protected static async Task AssertNoSeriousAxeViolationsAsync(IPage page, params string[] testIds)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        foreach (var testId in testIds)
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
}

/// <summary>The WASM lane (the demo default): the whole scenario.</summary>
[TestClass]
[TestCategory("WASM")]
public class AiAgentsE2ETests : AiAgentsE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";

    [TestMethod]
    public async Task AStepReordersByKeyboardAndByItsGrip()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, PlanSection);

        var row = Plan(page).Locator(".tm-sortable-list__item").First;
        await row.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(150);
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(150);
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(400);

        CollectionAssert.AreEqual(
            new[] { "Match the payments", "Collect the invoices", "Reconcile the ledger", "Publish the summary" },
            await PlanTitlesAsync(page));
        Assert.AreEqual("Moved the step from position 1 to 2.",
            await TextAsync(page, "ai-agents-plan-last-edit"));

        var handleBox = await Plan(page).Locator(".tm-sortable-list__handle").First.BoundingBoxAsync();
        var targetRow = await Plan(page).Locator(".tm-sortable-list__item").Nth(2).BoundingBoxAsync();
        Assert.IsNotNull(handleBox);
        Assert.IsNotNull(targetRow);

        var startX = handleBox!.X + handleBox.Width / 2;
        var startY = handleBox.Y + handleBox.Height / 2;
        var endY = targetRow!.Y + targetRow.Height * 0.75;

        await page.Mouse.MoveAsync(startX, startY);
        await page.Mouse.DownAsync();
        for (var step = 1; step <= 8; step++)
        {
            await page.Mouse.MoveAsync(startX, (float)(startY + (endY - startY) * step / 8.0));
            await page.WaitForTimeoutAsync(30);
        }

        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(450);

        Assert.AreEqual("Match the payments", (await PlanTitlesAsync(page))[2],
            "the dragged row landed where the pointer dropped it");
        Assert.AreEqual("Moved the step from position 1 to 3.",
            await TextAsync(page, "ai-agents-plan-last-edit"));
    }

    [TestMethod]
    public async Task TheRunFailsOneStep_AndRetryThenApprovalFinishIt()
    {
        var page = await OpenAsync();
        await StartRunUntilFailureAsync(page);

        StringAssert.Contains(await TextAsync(page, "ai-agents-run-state"), "failed");
        CollectionAssert.AreEqual(new[] { "done", "failed", "pending", "pending" }, await PlanStatusesAsync(page));
        Assert.AreEqual(1, await RunningPlan(page).Locator("[data-testid='agent-plan-step-retry']").CountAsync());

        await ShootAsync(page, "ai-agents-plan-failed-light-1440");

        await RunningPlan(page).Locator("[data-testid='agent-plan-step-retry']").ClickAsync();
        await page.Locator("[data-testid='ai-agents-monitor'] [data-testid='approval-approve']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 20000 });
        Assert.AreEqual("The last step is waiting for your approval.",
            await TextAsync(page, "ai-agents-run-state"));
        CollectionAssert.AreEqual(new[] { "done", "done", "done", "pending" }, await PlanStatusesAsync(page));

        await ScrollToSection(page, MonitorSection);
        Assert.AreEqual("Publish the summary?",
            (await page.Locator("[data-testid='ai-agents-monitor'] [data-testid='approval-title']")
                .TextContentAsync())?.Trim());
        await ShootAsync(page, "ai-agents-monitor-approval-light-1440");

        await page.Locator("[data-testid='ai-agents-monitor'] [data-testid='approval-approve']").ClickAsync();
        await page.Locator("[data-testid='ai-agents-run-state']", new PageLocatorOptions
        {
            HasTextString = "finished all 4 steps",
        }).WaitForAsync(new LocatorWaitForOptions { Timeout = 20000 });

        CollectionAssert.AreEqual(new[] { "done", "done", "done", "done" }, await PlanStatusesAsync(page));
        Assert.AreEqual("The page received Approve and the run is finishing.",
            await TextAsync(page, "ai-agents-monitor-decision"));
    }

    [TestMethod]
    public async Task RejectingTheApprovalSkipsTheLastStep()
    {
        var page = await OpenAsync();
        await StartRunUntilFailureAsync(page);

        await RunningPlan(page).Locator("[data-testid='agent-plan-step-retry']").ClickAsync();
        await page.Locator("[data-testid='ai-agents-monitor'] [data-testid='approval-reject']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 20000 });
        await page.Locator("[data-testid='ai-agents-monitor'] [data-testid='approval-reject']").ClickAsync();

        await page.Locator("[data-testid='ai-agents-run-state']", new PageLocatorOptions
        {
            HasTextString = "finished without it",
        }).WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        CollectionAssert.AreEqual(new[] { "done", "done", "done", "skipped" }, await PlanStatusesAsync(page));
        Assert.AreEqual("The page received Reject and the last step was skipped.",
            await TextAsync(page, "ai-agents-monitor-decision"));
    }

    [TestMethod]
    public async Task TheTreegridWalksWithTheKeyboard_AndRollsTheRootUp()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, TreeSection);

        Assert.AreEqual(7, await TreeRows(page).CountAsync());

        // At 1440 px the six metric columns FIT: a Tokens column clipped by the scrollport hides a number
        // the reader came for (the browser review caught exactly that when the tree had a side column).
        var grid = page.Locator("[data-testid='ai-agents-tree'] .tm-ai-agent-run-tree__grid");
        var gridMetrics = await grid.EvaluateAsync<int[]>("el => [el.scrollWidth, el.clientWidth]");
        Assert.IsTrue(gridMetrics[0] <= gridMetrics[1] + 1,
            $"the treegrid columns fit at 1440 (scrollWidth {gridMetrics[0]}, clientWidth {gridMetrics[1]})");

        var levels = await TreeRows(page).EvaluateAllAsync<string[]>(
            "rows => rows.map(row => row.getAttribute('aria-level'))");
        CollectionAssert.AreEqual(new[] { "1", "2", "3", "2", "3", "2", "3" }, levels);

        var rootCost = TreeRows(page).First.Locator(".tm-ai-agent-run-tree__cost");
        Assert.AreEqual("true", await rootCost.GetAttributeAsync("data-rollup"));
        StringAssert.StartsWith(await rootCost.GetAttributeAsync("aria-label"),
            "Total for Reconciliation agent including its sub-agents:");

        await TreeRows(page).First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(200);
        Assert.AreEqual("0", await TreeRows(page).Nth(1).GetAttributeAsync("tabindex"));
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual("Selected: Collect the invoices",
            await TextAsync(page, "ai-agents-tree-selected"));
    }

    [TestMethod]
    public async Task TheModelPickerPicksAModel_AndItsEffort()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, ModelsSection);

        Assert.AreEqual("A fictional catalogue: the provider names, prices and scores are invented for the demo "
                        + "and are not a real price list.", await TextAsync(page, "ai-agents-models-notice"));

        var picker = page.Locator("[data-testid='ai-agents-model-picker']");
        await picker.Locator("[data-testid='model-picker-trigger']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-agents-model-picker'] [data-testid='model-picker-option']");
        Assert.AreEqual(6, await picker.Locator("[data-testid='model-picker-option']").CountAsync());

        await picker.Locator("[data-model-id='nimbus-2']").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        StringAssert.Contains(await TextAsync(page, "ai-agents-model-selection"), "Nimbus 2");
        StringAssert.Contains(await TextAsync(page, "ai-agents-model-selection"), "extended thinking: off");

        await picker.Locator("[data-testid='model-picker-trigger']").ClickAsync();
        // The switch's native input is visually hidden by design, so wait for the visible label instead
        // (the reader clicks that label, never the input).
        await picker.Locator("[data-testid='model-picker-thinking'] .tm-toggle-label")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await picker.Locator("[data-testid='model-picker-thinking'] .tm-toggle-label").ClickAsync();
        await page.WaitForTimeoutAsync(250);
        await picker.Locator("[data-testid='model-picker-effort'] [data-value='high']").ClickAsync();
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual("Model: Nimbus 2 · extended thinking: on · effort: High",
            await TextAsync(page, "ai-agents-model-selection"));
        Assert.AreEqual("Nimbus 2",
            (await page.Locator("[data-testid='ai-agents-monitor'] .tm-ai-agent-monitor__metric--model "
                                + ".tm-ai-agent-monitor__metric-value").TextContentAsync())?.Trim());

        await ShootAsync(page, "ai-agents-models-light-1440");
    }

    [TestMethod]
    public async Task TheLanesSelectABlockByKeyboard_AndTheTableCarriesTheSameData()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, LanesSection);

        var lanes = page.Locator("[data-testid='ai-agents-lanes']");
        Assert.AreEqual(4, await lanes.Locator("[data-testid='agent-lanes-lane']").CountAsync());
        Assert.AreEqual(7, await lanes.Locator("[data-testid='agent-lanes-block']").CountAsync());
        Assert.AreEqual(4, await lanes.Locator("[data-testid='agent-lanes-handoff']").CountAsync());
        Assert.IsTrue(await lanes.Locator("[data-testid='agent-lanes-now']").CountAsync() == 1,
            "a block is still running, so the live now line is painted");

        // A block is a native button: focus it and press Enter, exactly like a reader.
        var block = lanes.Locator("[data-testid='agent-lanes-block'][data-block-id='b-match']");
        await block.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForSelectorAsync("[data-testid='ai-agents-lanes-detail']",
            new PageWaitForSelectorOptions { Timeout = 10000 });

        StringAssert.Contains(await TextAsync(page, "ai-agents-lanes-selected"), "Match invoices to lines");
        Assert.AreEqual("true", await block.GetAttributeAsync("aria-pressed"));

        // Fit to width measures the real scrollport in the browser and lands on a scale that hides no part of the run.
        await lanes.Locator("[data-testid='agent-lanes-zoom-fit']").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        var zoom = (await lanes.Locator("[data-testid='agent-lanes-zoom-value']").TextContentAsync())?.Trim() ?? string.Empty;
        StringAssert.Contains(zoom, "%");
        Assert.IsTrue(
            await lanes.Locator("[data-testid='agent-lanes-viewport']")
                .EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 2"),
            "fit to width makes the whole run fit the scrollport (zoom " + zoom + ")");

        await ShootAsync(page, "ai-agents-lanes-light-1440");

        // The text alternative carries exactly the same blocks and handoffs.
        await lanes.Locator("[data-testid='agent-lanes-view'] button")
            .Filter(new LocatorFilterOptions { HasTextString = "Table" })
            .ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-agents-lanes'] [data-testid='agent-lanes-table-row']",
            new PageWaitForSelectorOptions { Timeout = 10000 });

        Assert.AreEqual(7, await lanes.Locator("[data-testid='agent-lanes-table-row']").CountAsync());
        Assert.AreEqual(4, await lanes.Locator("[data-testid='agent-lanes-handoff-row']").CountAsync());
        Assert.AreEqual(0, await lanes.Locator("[data-testid='agent-lanes-block']").CountAsync(),
            "the table replaces the drawing instead of duplicating it");

        await ShootAsync(page, "ai-agents-lanes-table-light-1440");
    }

    [TestMethod]
    public async Task TheGraphWalksItsConnectionsByKeyboard_AndTheTableCarriesTheSameNumbers()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, GraphSection);

        var graph = page.Locator("[data-testid='ai-agents-graph']");
        Assert.AreEqual(7, await graph.Locator("[data-testid='agent-graph-node']").CountAsync(),
            "the human, three agents, the ledger and two tools");
        Assert.AreEqual(6, await graph.Locator("[data-testid='agent-graph-edge']").CountAsync());
        Assert.AreEqual(1, await graph.Locator("[data-testid='agent-graph-group']").CountAsync(),
            "the two core agents are grouped");

        // The graph is ONE tab stop: the first node in painting order owns it.
        var human = graph.Locator("[data-testid='agent-graph-node'][data-node-id='human']");
        Assert.AreEqual("0", await human.GetAttributeAsync("tabindex"),
            "the roving tab stop starts on the first node");

        await human.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await page.WaitForTimeoutAsync(200);

        Assert.AreEqual("planner", await page.EvaluateAsync<string>(
                "() => document.activeElement?.dataset.nodeId ?? document.activeElement?.tagName ?? 'none'"),
            "the arrow moved the DOM focus onto the connected node");

        var planner = graph.Locator("[data-testid='agent-graph-node'][data-node-id='planner']");
        Assert.AreEqual("0", await planner.GetAttributeAsync("tabindex"),
            "ArrowRight walks the CONNECTION from the human to the planner");
        Assert.AreEqual("-1", await human.GetAttributeAsync("tabindex"));

        // Enter is the button's own activation — the component never emulates it.
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForSelectorAsync("[data-testid='ai-agents-graph-detail']",
            new PageWaitForSelectorOptions { Timeout = 10000 });
        StringAssert.Contains(await TextAsync(page, "ai-agents-graph-selected"), "Planner");
        Assert.AreEqual("true", await planner.GetAttributeAsync("aria-pressed"));

        // The page pushes a REAL AgentGraphCall: the edge thickens and the node's name counts it.
        var search = graph.Locator("[data-testid='agent-graph-node'][data-node-id='search']");
        StringAssert.Contains(await search.GetAttributeAsync("aria-label") ?? string.Empty, "Calls: 3");

        await page.Locator("[data-testid='ai-agents-graph-call']").ClickAsync();
        await page.WaitForTimeoutAsync(400);

        StringAssert.Contains(await TextAsync(page, "ai-agents-graph-call-log"), "1");
        StringAssert.Contains(await search.GetAttributeAsync("aria-label") ?? string.Empty, "Calls: 4",
            "the component folded the call the page pushed into its edge");

        await ShootAsync(page, "ai-agents-graph-light-1440");

        // The text alternative carries every connection and every node with the same numbers.
        await graph.Locator("[data-testid='agent-graph-view'] button")
            .Filter(new LocatorFilterOptions { HasTextString = "Table" })
            .ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-agents-graph'] [data-testid='agent-graph-table-row']",
            new PageWaitForSelectorOptions { Timeout = 10000 });

        Assert.AreEqual(6, await graph.Locator("[data-testid='agent-graph-table-row']").CountAsync());
        Assert.AreEqual(7, await graph.Locator("[data-testid='agent-graph-node-row']").CountAsync());
        Assert.AreEqual(0, await graph.Locator("[data-testid='agent-graph-node']").CountAsync(),
            "the table replaces the drawing instead of duplicating it");

        await ShootAsync(page, "ai-agents-graph-table-light-1440");
    }

    /// <summary>
    /// The fan-out is a real APG grid and the retry really re-queues: the arrow keys move the DOM focus between tiles,
    /// the detail follows the focused tile, the page's own handler re-queues the failed unit, and the attempts compare
    /// three drafts whose winner is picked with the native radio and only recorded after the confirmation.
    /// </summary>
    [TestMethod]
    public async Task TheFanOutGridWalksByKeyboard_AndTheAttemptsConfirmAWinner()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, FanOutSection);

        var grid = page.Locator("[data-testid='ai-agents-fan-out']");
        Assert.AreEqual(18, await grid.Locator("[data-testid='fan-out-tile']").CountAsync());

        // ONE tab stop: the first tile owns the roving tabindex.
        var first = grid.Locator("[data-testid='fan-out-tile'][data-task-id='unit-01']");
        Assert.AreEqual("0", await first.GetAttributeAsync("tabindex"),
            "the roving tab stop starts on the first tile");

        await first.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await page.WaitForTimeoutAsync(250);

        Assert.AreEqual("unit-02",
            await page.EvaluateAsync<string>("() => document.activeElement?.dataset.taskId ?? 'none'"),
            "ArrowRight moved the DOM focus onto the next tile");
        await page.WaitForSelectorAsync("[data-testid='ai-agents-fan-out'] [data-testid='fan-out-detail']",
            new PageWaitForSelectorOptions { Timeout = 10000 });

        // Ctrl+End crosses the whole grid (a windowed grid mounts the tile the focus jumps to).
        await page.Keyboard.PressAsync("Control+End");
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual("unit-18",
            await page.EvaluateAsync<string>("() => document.activeElement?.dataset.taskId ?? 'none'"),
            "Ctrl+End moved the focus to the last tile, which the window had to mount");

        // The retry is the PAGE's handler: the failed unit goes back to the queue.
        var failed = grid.Locator("[data-testid='fan-out-tile'][data-status='failed']");
        Assert.AreEqual(1, await failed.CountAsync(), "exactly one unit failed on purpose");
        await failed.ClickAsync();
        await grid.Locator("[data-testid='fan-out-detail-retry']").ClickAsync();

        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"ai-agents-fan-out-log\"]')?.textContent?.includes('re-queued')",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual(0, await grid.Locator("[data-testid='fan-out-tile'][data-status='failed']").CountAsync(),
            "the page re-queued the failed unit");

        await ShootAsync(page, "ai-agents-fan-out-light-1440");

        // The filter narrows the grid and announces the count.
        await grid.Locator("[data-testid='fan-out-filter'][data-filter='running']").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual(3, await grid.Locator("[data-testid='fan-out-tile']").CountAsync(),
            "the running filter shows the three units still in flight");
        StringAssert.Contains(
            await grid.Locator("[data-testid='fan-out-announcement']").TextContentAsync() ?? string.Empty,
            "Showing 3 of 18",
            "the filter change is announced once");
        await grid.Locator("[data-testid='fan-out-filter'][data-filter='all']").ClickAsync();
        await page.WaitForTimeoutAsync(200);

        // The attempts: three drafts, Draft B best, a two-step winner.
        await ScrollToSection(page, AttemptsSection);
        var attempts = page.Locator("[data-testid='ai-agents-attempts']");
        Assert.AreEqual(3, await attempts.Locator("[data-testid='parallel-attempt']").CountAsync());
        Assert.AreEqual("draft-b",
            await attempts.Locator("[data-testid='parallel-attempt'][data-best='true']")
                .GetAttributeAsync("data-attempt-id"),
            "the highest score is marked best");

        await attempts.Locator("[data-testid='parallel-attempt-radio'][data-attempt-id='draft-a']").CheckAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-agents-attempts'] [data-testid='parallel-attempts-confirm']",
            new PageWaitForSelectorOptions { Timeout = 10000 });
        Assert.AreEqual("No winner chosen yet.", await TextAsync(page, "ai-agents-attempts-log"),
            "choosing is not confirming");

        await attempts.Locator("[data-testid='parallel-attempts-confirm-yes']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"ai-agents-attempts-log\"]')?.textContent?.includes('accepted')",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual("draft-a",
            await attempts.Locator("[data-testid='parallel-attempt'][data-selected='true']")
                .GetAttributeAsync("data-attempt-id"));

        // The cancel path really reverts the radio IN THE BROWSER: a controlled `checked` attribute only sets the
        // default checkedness, so the surface re-mounts the inputs when a pick is dropped.
        var radioA = attempts.Locator("[data-testid='parallel-attempt-radio'][data-attempt-id='draft-a']");
        var radioC = attempts.Locator("[data-testid='parallel-attempt-radio'][data-attempt-id='draft-c']");
        await radioC.CheckAsync();
        await attempts.Locator("[data-testid='parallel-attempts-confirm-no']").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        Assert.IsFalse(await radioC.IsCheckedAsync(), "the dropped pick is no longer checked");
        Assert.IsTrue(await radioA.IsCheckedAsync(), "the confirmed winner is the checked radio again");

        var votes = attempts.Locator("[data-testid='parallel-attempt'][data-attempt-id='draft-b']")
            .Locator("[data-testid='parallel-attempt-votes-toggle']");
        await votes.ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='ai-agents-attempts'] [data-testid='parallel-attempt-vote-rationale']",
            new PageWaitForSelectorOptions { Timeout = 10000 });

        await ShootAsync(page, "ai-agents-attempts-light-1440");

        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-fan-out-section", "ai-agents-attempts-section");

        await ToggleThemeAsync(page);
        await ScrollToSection(page, FanOutSection);
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-fan-out-section", "ai-agents-attempts-section");
        await ShootAsync(page, "ai-agents-fan-out-dark-1440");
        await ScrollToSection(page, AttemptsSection);
        await ShootAsync(page, "ai-agents-attempts-dark-1440");
    }

    [TestMethod]
    public async Task ThePromptLibraryFillsATemplate_AndUseHandsItOver()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, PromptsSection);

        Assert.AreEqual(5, await page.Locator("[data-testid='ai-agents-prompts'] [data-testid='prompt-library-template']")
            .CountAsync());
        Assert.AreEqual("No prompt has been used yet.", await TextAsync(page, "ai-agents-used-empty"));
        Assert.AreEqual(string.Empty,
            await page.Locator("[data-testid='ai-agents-prompts'] [data-testid='prompt-library-search']")
                .InputValueAsync(),
            "the search field starts empty, not with its field name");

        // A Blazor-bound input must be driven by real key/input events (FillAsync does not reach @oninput).
        var product = page.Locator("[data-testid='ai-agents-prompts'] "
                                   + "[data-testid='prompt-library-variable'][data-variable='product']");
        await product.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.TypeAsync("Tempo Suite");
        await page.WaitForTimeoutAsync(300);

        await page.Locator("[data-testid='ai-agents-prompts'] [data-testid='prompt-library-use']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-agents-used-prompt']", new PageWaitForSelectorOptions
        {
            Timeout = 10000,
        });

        var used = await TextAsync(page, "ai-agents-used-prompt");
        StringAssert.Contains(used, "Tempo Suite version 2.10.0");
        StringAssert.Contains(used, "new features");
        Assert.AreEqual("Used “Draft release notes”:", await TextAsync(page, "ai-agents-used-template"));

        await ShootAsync(page, "ai-agents-prompts-light-1440");
    }

    [TestMethod]
    public async Task TheContextVariantsDropAnEntry_AndCompact()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, ContextSection);

        StringAssert.Contains(await TextAsync(page, "ai-agents-context-readout"), "13,800");
        var bar = page.Locator("[data-testid='ai-agents-context-bar']");

        await bar.Locator("[data-section-key='tools'] [data-testid='context-usage-toggle']").ClickAsync();
        await bar.Locator("[data-item-id='search'] [data-testid='context-usage-remove-item']").ClickAsync();
        await page.WaitForTimeoutAsync(400);

        StringAssert.Contains(await TextAsync(page, "ai-agents-context-readout"), "13,300");
        Assert.AreEqual("Removed Web search from Tools.", await TextAsync(page, "ai-agents-context-log"));

        await page.Locator("[data-testid='ai-agents-context-bar'] [data-testid='context-usage-compact']").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        Assert.AreEqual("Compacted the optional sections.", await TextAsync(page, "ai-agents-context-log"));

        await ShootAsync(page, "ai-agents-context-light-1440");
    }

    [TestMethod]
    public async Task ThePhoneKeepsThePageInside375Px()
    {
        var page = await OpenAsync(375, 812, coarsePointer: true);

        foreach (var section in new[]
                 {
                     PlanSection, TreeSection, LanesSection, GraphSection, FanOutSection, AttemptsSection,
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
        await ScrollToSection(desktop, ContextSection);
        await ShootAsync(desktop, "ai-agents-context-dark-1440");
        await ScrollToSection(desktop, PromptsSection);
        await ShootAsync(desktop, "ai-agents-prompts-dark-1440");

        var phone = await OpenAsync(375, 812, coarsePointer: true);
        await ScrollToSection(phone, LanesSection);
        await ShootAsync(phone, "ai-agents-lanes-phone-375");
        await ScrollToSection(phone, GraphSection);
        await ShootAsync(phone, "ai-agents-graph-phone-375");
        await ScrollToSection(phone, MonitorSection);
        await ShootAsync(phone, "ai-agents-monitor-phone-375");
        await ScrollToSection(phone, ContextSection);
        await ShootAsync(phone, "ai-agents-context-phone-375");
    }
}

/// <summary>The Server lane smoke: the same page renders its scenario on the Server host.</summary>
[TestClass]
[TestCategory("Server")]
public class AiAgentsServerSmokeE2ETests : AiAgentsE2EBase
{
    protected override string BaseUrl => "https://localhost:7107";

    [TestMethod]
    public async Task ThePageRenders_AndTheRunFailsAndRetriesOnTheServerHost()
    {
        var page = await OpenAsync();

        Assert.AreEqual("The plan is waiting for you to start it.",
            await TextAsync(page, "ai-agents-run-state"));

        await StartRunUntilFailureAsync(page);
        StringAssert.Contains(await TextAsync(page, "ai-agents-run-state"), "failed");
        Assert.AreEqual(1, await RunningPlan(page).Locator("[data-testid='agent-plan-step-retry']").CountAsync());
    }

    [TestMethod]
    public async Task TheSectionsPassAScopedAxeScan_OnTheServerHost()
    {
        var page = await OpenAsync();

        await AssertNoSeriousAxeViolationsAsync(page,
            "ai-agents-plan-section", "ai-agents-tree-section", "ai-agents-lanes-section",
            "ai-agents-fan-out-section", "ai-agents-attempts-section",
            "ai-agents-monitor-section", "ai-agents-context-section", "ai-agents-prompts-section");
    }
}
