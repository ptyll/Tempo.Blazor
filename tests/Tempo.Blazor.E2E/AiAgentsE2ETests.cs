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
    protected const string HandoffSection = "[data-testid='ai-agents-handoff-section']";
    protected const string FallbackSection = "[data-testid='ai-agents-fallback-section']";
    protected const string AutonomySection = "[data-testid='ai-agents-autonomy-section']";
    protected const string MemorySection = "[data-testid='ai-agents-memory-section']";
    protected const string DelegateSection = "[data-testid='ai-agents-delegate-section']";
    protected const string RunsSection = "[data-testid='ai-agents-runs-section']";
    protected const string ConflictSection = "[data-testid='ai-agents-conflict-section']";
    protected const string PresenceSection = "[data-testid='ai-agents-presence-section']";
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

    /// <summary>The text of an element nested under another test id, trimmed.</summary>
    protected static async Task<string> TextAsync(IPage page, string rootTestId, string testId)
        => ((await page.Locator($"[data-testid='{rootTestId}'] [data-testid='{testId}']").TextContentAsync())
            ?? string.Empty).Trim();

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
                    const describe = (node) => {
                        const el = document.querySelector(node.target.join(' '));
                        if (!el) return node.target.join(' ');
                        const style = getComputedStyle(el);
                        let background = '';
                        let walker = el;
                        while (walker && (!background || background === 'rgba(0, 0, 0, 0)')) {
                            background = getComputedStyle(walker).backgroundColor;
                            walker = walker.parentElement;
                        }
                        // The pair is what a reader sees: a colour the scan reports without its background is a
                        // number nobody can act on.
                        return node.target.join(' ') + ' [' + style.color + ' on ' + background + ']';
                    };
                    return result.violations
                        .filter(v => v.impact === 'critical' || v.impact === 'serious')
                        .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => v.id === 'color-contrast' ? describe(n) : n.target.join(' ')).slice(0, 4).join('; ')})`);
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
public partial class AiAgentsE2ETests : AiAgentsE2EBase
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

    /// <summary>
    /// The handoff chain is a real disclosure: the Details button is a native button reached with Tab, Enter and Space
    /// open and close it IN THE BROWSER (one activation per press), the lost items keep their reasons, the chain-wide
    /// sentence names what was dropped, and the page's direction switch really turns the chain on its side.
    /// </summary>
    [TestMethod]
    public async Task TheHandoffChainOpensByKeyboard_AndThePageTurnsItSideways()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, HandoffSection);

        var handoff = page.Locator("[data-testid='ai-agents-handoff']");
        Assert.AreEqual(3, await handoff.Locator("[data-testid='agent-handoff-step']").CountAsync());
        Assert.AreEqual("vertical", await handoff.GetAttributeAsync("data-orientation"));

        var toggle = handoff.Locator("[data-testid='agent-handoff-step-toggle']").First;
        await toggle.FocusAsync();
        Assert.IsTrue(await toggle.EvaluateAsync<bool>("el => el === document.activeElement"),
            "the Details button is a native button and takes the focus");
        Assert.AreEqual("true", await toggle.GetAttributeAsync("aria-expanded"),
            "every step starts open — the two lists are the reason to look at a chain");

        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual("false", await toggle.GetAttributeAsync("aria-expanded"),
            "Enter activated the native button exactly once");
        Assert.AreEqual(0,
            await handoff.Locator("[data-testid='agent-handoff-step'][data-step-id='h-fetch']")
                .Locator("[data-testid='agent-handoff-step-detail']").CountAsync(),
            "the collapsed region is not in the DOM, so aria-controls points at nothing while it is closed");

        await page.Keyboard.PressAsync("Space");
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual("true", await toggle.GetAttributeAsync("aria-expanded"),
            "Space re-opens it — no key is emulated twice");

        var lost = handoff.Locator("[data-testid='agent-handoff-step'][data-step-id='h-fetch']")
            .Locator("[data-testid='agent-handoff-lost-item']");
        Assert.AreEqual(1, await lost.CountAsync());
        StringAssert.Contains(await lost.First.TextContentAsync() ?? string.Empty, "Conversation history");
        StringAssert.Contains(await lost.First.TextContentAsync() ?? string.Empty, "context window was full",
            "a lost item keeps the reason it never arrived");
        StringAssert.Contains(await handoff.Locator("[data-testid='agent-handoff-lost-summary']").TextContentAsync()
            ?? string.Empty, "Lost across the chain");

        await ShootAsync(page, "ai-agents-handoff-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-handoff-section");

        // The page owns the direction; the reader's pick really lays the same list across the surface.
        await page.Locator("[data-testid='ai-agents-handoff-orientation'] button").Nth(1).ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-handoff']\")?.dataset.orientation === 'horizontal'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual(3, await handoff.Locator("[data-testid='agent-handoff-step']").CountAsync(),
            "the horizontal chain reads the same three steps");
        await ShootAsync(page, "ai-agents-handoff-horizontal-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-handoff-section");

        await ToggleThemeAsync(page);
        await ScrollToSection(page, HandoffSection);
        await ShootAsync(page, "ai-agents-handoff-dark-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-handoff-section");
    }

    /// <summary>
    /// The fallback chain answers, switches to its table, and the EDITOR over the same chain really reorders it: the
    /// keyboard grab/move/drop of the shared sortable list reaches the page, which records the new order — and a
    /// reorder is announced once, by the list, not twice.
    /// </summary>
    [TestMethod]
    public async Task TheFallbackChainAnswers_AndTheEditorReordersTheChain()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, FallbackSection);

        var fallback = page.Locator("[data-testid='ai-agents-fallback']");
        Assert.AreEqual(3, await fallback.Locator("[data-testid='fallback-chain-node']").CountAsync());
        Assert.AreEqual("Answered by Nimbus 2",
            await fallback.Locator("[data-testid='fallback-chain-answer-model']").TextContentAsync(),
            "the first successful attempt ends the chain");
        Assert.AreEqual("Chain: Atlas Large → Aurora Pro → Nimbus 2",
            await TextAsync(page, "ai-agents-fallback-log"));

        // The text alternative carries the same attempts.
        await fallback.Locator("[data-testid='fallback-chain-view'] button").Nth(1).ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-agents-fallback'] [data-testid='fallback-chain-table-row']",
            new PageWaitForSelectorOptions { Timeout = 10000 });
        Assert.AreEqual(3, await fallback.Locator("[data-testid='fallback-chain-table-row']").CountAsync());
        Assert.AreEqual(0, await fallback.Locator("[data-testid='fallback-chain-node']").CountAsync(),
            "the table replaces the flow instead of duplicating it");
        await ShootAsync(page, "ai-agents-fallback-table-light-1440");

        // Back to the flow, then the editor.
        await fallback.Locator("[data-testid='fallback-chain-view'] button").Nth(0).ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-agents-fallback'] [data-testid='fallback-chain-node']",
            new PageWaitForSelectorOptions { Timeout = 10000 });
        await ShootAsync(page, "ai-agents-fallback-light-1440");

        await page.Locator("[data-testid='ai-agents-fallback-mode'] button").Nth(1).ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-agents-fallback'] [data-testid='fallback-chain-editor-row']",
            new PageWaitForSelectorOptions { Timeout = 10000 });
        Assert.AreEqual(3, await fallback.Locator("[data-testid='fallback-chain-editor-row']").CountAsync());

        // The shared sortable list's keyboard model: grab the first row, move it down one place and drop it.
        var firstRow = fallback.Locator("[data-testid='fallback-chain-sortable'] [data-tm-sortable-item]").First;
        await firstRow.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(200);
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(200);
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-fallback-log']\")?.textContent?.includes('Aurora Pro → Atlas Large')",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        await ShootAsync(page, "ai-agents-fallback-editor-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-fallback-section");

        await ToggleThemeAsync(page);
        await ScrollToSection(page, FallbackSection);
        await ShootAsync(page, "ai-agents-fallback-editor-dark-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-fallback-section");
    }

    /// <summary>
    /// The fallback chain editor's inline rename: the Rename button opens an editor pre-filled with the current label,
    /// Enter commits the new label and the page records it, Escape rolls back without reporting. The keyboard is the
    /// only input — no pointer involved.
    /// </summary>
    [TestMethod]
    public async Task TheFallbackChainEditor_RenamesByKeyboard()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, FallbackSection);

        await page.Locator("[data-testid='ai-agents-fallback-mode'] button").Nth(1).ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-agents-fallback'] [data-testid='fallback-chain-editor-row']",
            new PageWaitForSelectorOptions { Timeout = 10000 });

        // Rename the first row: click Rename, type a new label, commit with Enter.
        var fallback = page.Locator("[data-testid='ai-agents-fallback']");
        await fallback.Locator("[data-testid='fallback-chain-editor-rename']").First.ClickAsync();
        var input = fallback.Locator("[data-testid='fallback-chain-editor-rename-input']");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await input.ClickAsync();
        await input.FillAsync("Atlas Large v2");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(400);

        // The row shows the new label.
        var firstRowLabel = await fallback.Locator("[data-testid='fallback-chain-editor-row']")
            .First.Locator(".tm-ai-fallback__editor-row-label").TextContentAsync();
        Assert.AreEqual("Atlas Large v2", firstRowLabel?.Trim(),
            "the committed rename updates the row label");

        // WCAG 2.4.3: the focus returns to the rename button of the same row, not to the document body.
        var activeElementTestId = await page.EvaluateAsync<string>(
            "() => document.activeElement?.getAttribute('data-testid') ?? document.activeElement?.tagName ?? 'none'");
        Assert.AreEqual("fallback-chain-editor-rename", activeElementTestId,
            "after Enter the focus lands on the rename button of the renamed row");

        // Rename the second row, then Escape: the draft is discarded.
        await fallback.Locator("[data-testid='fallback-chain-editor-rename']").Nth(1).ClickAsync();
        var input2 = fallback.Locator("[data-testid='fallback-chain-editor-rename-input']");
        await input2.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await input2.ClickAsync();
        await input2.FillAsync("discarded");
        await input2.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);

        var secondRowLabel = await fallback.Locator("[data-testid='fallback-chain-editor-row']")
            .Nth(1).Locator(".tm-ai-fallback__editor-row-label").TextContentAsync();
        Assert.AreNotEqual("discarded", secondRowLabel?.Trim(),
            "Escape rolls back the rename without applying it");

        // WCAG 2.4.3: Escape also hands the focus back to the rename button of the same row.
        var activeElementTestIdAfterEscape = await page.EvaluateAsync<string>(
            "() => document.activeElement?.getAttribute('data-testid') ?? document.activeElement?.tagName ?? 'none'");
        Assert.AreEqual("fallback-chain-editor-rename", activeElementTestIdAfterEscape,
            "after Escape the focus lands on the rename button of the row that was being edited");

        await ShootAsync(page, "ai-agents-fallback-rename-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-fallback-section");
    }

    /// <summary>
    /// The autonomy dial is a real slider in the browser: the arrows step it, a grade at the threshold opens the
    /// confirmation, Escape drops the change AND leaves the focus on the slider, and only the confirmed one reaches the
    /// page. The plan's ceiling still shows the grade it does not allow, with the tenant's locks beside it.
    /// </summary>
    [TestMethod]
    public async Task TheAutonomyDialStepsByKeyboard_AndTheConfirmationGuardsTheChange()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, AutonomySection);

        var dial = page.Locator("[data-testid='ai-agents-autonomy']");
        var slider = dial.Locator("[data-testid='autonomy-dial-track']");
        Assert.AreEqual("slider", await slider.GetAttributeAsync("role"));
        Assert.AreEqual("1", await slider.GetAttributeAsync("aria-valuenow"));
        Assert.AreEqual("Assist", await slider.GetAttributeAsync("aria-valuetext"),
            "the value text is the grade, not its number");
        Assert.AreEqual("2", await slider.GetAttributeAsync("aria-valuemax"),
            "the ceiling lowers the maximum the slider may reach");
        Assert.AreEqual("Nothing received yet.", await TextAsync(page, "ai-agents-autonomy-log"));

        await slider.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await dial.Locator("[data-testid='autonomy-confirm']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        StringAssert.Contains(await TextAsync(page, "ai-agents-autonomy", "autonomy-confirm-message"),
            "Act", "the confirmation names the grade it is about to commit");
        Assert.AreEqual("1", await slider.GetAttributeAsync("aria-valuenow"),
            "the grade is not committed before the reader confirms");

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(250);
        Assert.AreEqual(0, await dial.Locator("[data-testid='autonomy-confirm']").CountAsync());
        Assert.AreEqual("slider",
            await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('role') ?? 'none'"),
            "the focus came back to the slider the confirmation took it from");
        Assert.AreEqual("Nothing received yet.", await TextAsync(page, "ai-agents-autonomy-log"),
            "a cancelled change never reaches the page");

        await ShootAsync(page, "ai-agents-autonomy-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-autonomy-section");

        // The screenshot helper blurs the active element, so the slider is focused again before the next key press.
        await slider.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await dial.Locator("[data-testid='autonomy-confirm-accept']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await ShootAsync(page, "ai-agents-autonomy-confirm-light-1440");
        await dial.Locator("[data-testid='autonomy-confirm-accept']").ClickAsync();

        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-autonomy-log']\")?.textContent?.includes('Act')",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual("2", await slider.GetAttributeAsync("aria-valuenow"));
        Assert.AreEqual("Act", await TextAsync(page, "ai-agents-autonomy", "autonomy-current"));

        var limit = await TextAsync(page, "ai-agents-autonomy", "autonomy-limit");
        StringAssert.Contains(limit, "Act", "the ceiling names the highest grade the plan still allows");
        Assert.AreEqual(2, await dial.Locator("[data-testid='autonomy-lock']").CountAsync());
        StringAssert.Contains(
            await dial.Locator("[data-testid='autonomy-lock-source']").First.TextContentAsync() ?? string.Empty,
            "Enforced by Tenant policy");
        Assert.AreEqual("true",
            await dial.Locator("[data-testid='autonomy-lock-icon']").First.GetAttributeAsync("aria-hidden"));

        await ToggleThemeAsync(page);
        await ScrollToSection(page, AutonomySection);
        await ShootAsync(page, "ai-agents-autonomy-confirmed-dark-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-autonomy-section");
    }

    /// <summary>
    /// The memory panel is the page's store: pausing a row changes the page's list, an Enter-committed edit reaches the
    /// page, a removal keeps its undo window (Undo brings the row back and the page never hears about it) and approving
    /// the agent's proposal turns it active. The empty search state is honest, and the phone keeps the card inside 375 px.
    /// </summary>
    [TestMethod]
    public async Task TheMemoryPanelEditsPausesAndUndoes_AndThePageOwnsTheStore()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, MemorySection);

        var panel = page.Locator("[data-testid='ai-agents-memory']");
        Assert.AreEqual(6, await panel.Locator("[data-testid='memory-item']").CountAsync());
        Assert.AreEqual(3, await panel.Locator("[data-testid='memory-group']").CountAsync());
        Assert.AreEqual("Nothing received yet.", await TextAsync(page, "ai-agents-memory-log"));

        var units = "[data-testid='ai-agents-memory'] [data-testid='memory-item'][data-item-id='mem-units']";
        await page.Locator(units + " [data-testid='memory-item-pause']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-memory'] [data-item-id='mem-units']\")?.dataset.status === 'paused'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        StringAssert.Contains(await TextAsync(page, "ai-agents-memory-log"), "Pause");

        // A Blazor-bound textarea is driven by real key events, not by FillAsync.
        await page.Locator(units + " [data-testid='memory-item-edit']").ClickAsync();
        var box = page.Locator(units + " [data-testid='memory-item-edit-box']");
        await box.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await box.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.TypeAsync("Prefers metric units only.");
        await page.WaitForTimeoutAsync(250);
        Assert.AreEqual("Prefers metric units only.", await box.InputValueAsync(),
            "the textarea carries the draft the reader typed");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);
        await ShootAsync(page, "ai-agents-memory-edit-light-1440");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-memory'] [data-item-id='mem-units'] [data-testid='memory-item-text']\")?.textContent?.includes('only.')",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        StringAssert.Contains(await TextAsync(page, "ai-agents-memory-log"), "Prefers metric units only.");

        await page.Locator(units + " [data-testid='memory-item-forget']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-agents-memory'] [data-testid='memory-undo']",
            new PageWaitForSelectorOptions { Timeout = 10000 });
        Assert.AreEqual(0, await page.Locator(units).CountAsync(), "the row leaves at once");
        await ShootAsync(page, "ai-agents-memory-undo-light-1440");

        await page.Locator("[data-testid='ai-agents-memory'] [data-testid='memory-undo-action']").ClickAsync();
        await page.WaitForSelectorAsync(units, new PageWaitForSelectorOptions { Timeout = 10000 });
        Assert.IsFalse((await TextAsync(page, "ai-agents-memory-log")).Contains("Forget", StringComparison.Ordinal),
            "an undone removal is never reported to the page");

        var tone = "[data-testid='ai-agents-memory'] [data-testid='memory-item'][data-item-id='mem-tone']";
        await page.Locator(tone + " [data-testid='memory-item-approve']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-memory'] [data-item-id='mem-tone']\")?.dataset.status === 'active'",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        // The master switch turns storage off but keeps what is known readable.
        await panel.Locator("[data-testid='memory-enabled'] .tm-toggle-label").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-memory']\")?.dataset.enabled === 'false'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual(0, await page.Locator("[data-testid='ai-agents-memory'] [data-testid='memory-item-pause']")
            .CountAsync(), "no action is offered while memory is off");
        Assert.AreEqual(6, await page.Locator("[data-testid='ai-agents-memory'] [data-testid='memory-item']")
            .CountAsync(), "the undone memory and the approved one stay, and nothing was lost");
        await panel.Locator("[data-testid='memory-enabled'] .tm-toggle-label").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-memory']\")?.dataset.enabled === 'true'",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        // The search is a view: a hit narrows the rows and the empty state says so.
        await page.Locator("[data-testid='ai-agents-memory'] [data-testid='memory-search']").ClickAsync();
        await page.Keyboard.TypeAsync("zzz");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-memory'] [data-testid='memory-empty']\")?.textContent?.includes('No memory matches')",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        await ShootAsync(page, "ai-agents-memory-search-empty-light-1440");
        await page.Locator("[data-testid='ai-agents-memory'] [data-testid='memory-search']").ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Backspace");
        await page.WaitForTimeoutAsync(300);

        await ShootAsync(page, "ai-agents-memory-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-memory-section");

        await ToggleThemeAsync(page);
        await ScrollToSection(page, MemorySection);
        await ShootAsync(page, "ai-agents-memory-dark-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-memory-section");

        // A phone keeps the whole card inside 375 px and every row action at the 44 px touch size.
        var phone = await OpenAsync(375, 812, coarsePointer: true);
        await ScrollToSection(phone, MemorySection);
        var box375 = await phone.Locator("[data-testid='ai-agents-memory']").BoundingBoxAsync();
        Assert.IsNotNull(box375);
        Assert.IsTrue(box375!.Width <= 375, $"the memory card fits a 375 px phone (width {box375.Width})");
        var actionBox = await phone.Locator("[data-testid='ai-agents-memory'] [data-testid='memory-item-pause']")
            .First.BoundingBoxAsync();
        Assert.IsNotNull(actionBox);
        Assert.IsTrue(actionBox!.Height >= 44, $"a row action keeps a touch target (height {actionBox.Height})");
        var stepBox = await phone.Locator("[data-testid='ai-agents-autonomy'] [data-testid='autonomy-dial-higher']")
            .BoundingBoxAsync();
        Assert.IsNotNull(stepBox);
        Assert.IsTrue(stepBox!.Height >= 44, $"a dial step button keeps a touch target (height {stepBox.Height})");
        await ShootAsync(phone, "ai-agents-memory-phone-375");
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
            "ai-agents-handoff-section", "ai-agents-fallback-section",
            "ai-agents-autonomy-section", "ai-agents-memory-section",
            "ai-agents-delegate-section", "ai-agents-runs-section",
            "ai-agents-conflict-section", "ai-agents-presence-section",
            "ai-agents-monitor-section", "ai-agents-context-section", "ai-agents-prompts-section");
    }
}
