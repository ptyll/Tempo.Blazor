using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 7ba95ed6, Fáze 10 — the LANE-AGNOSTIC behaviour of <c>/ai-agent-visualizations</c>: the simulator plays, pauses,
/// changes its rate and resets, the lanes take a block from the keyboard and switch to their table, the graph walks its
/// nodes with the arrow keys, the delegate board takes a card by pointer drag AND by its Assign menu, a remembered fact
/// is forgotten and undone, every conflict is settled from the keyboard before the merge, the autonomy dial steps by its
/// own keys, and every graphical surface reaches a keyboard-readable table — plus one scoped axe scan of every section.
/// <para>
/// The walks live here and are inherited by the WASM, Server and InteractiveAuto subclasses in
/// <c>AiAgentVisualizationsE2ELanes.cs</c>, so a circuit or interop regression can no longer hide behind a green WASM
/// run. The screenshots, the phone geometry and the two-minute budget are different concerns and derive from
/// <see cref="AiAgentVisualizationsPageBase"/> directly.
/// </para>
/// </summary>
public abstract class AiAgentVisualizationsE2EBase : AiAgentVisualizationsPageBase
{
    /// <summary>
    /// Every section of the page scanned on every lane: the same surfaces the WASM lane asserts behaviourally are also
    /// checked for critical and serious accessibility violations over a Server circuit and an InteractiveAuto host.
    /// </summary>
    [TestMethod]
    public async Task TheSectionsPassAScopedAxeScan()
        => await AssertNoSeriousAxeViolationsAsync(await OpenAsync(), AllSections);

    [TestMethod]
    public async Task TheSimulatorPlaysPausesChangesItsRate_AndResets()
    {
        var page = await OpenAsync();

        Assert.AreEqual("Paused", await TextAsync(page, "ai-viz-state"));
        Assert.AreEqual("0", await TextAsync(page, "ai-viz-events"));
        StringAssert.Contains(await TextAsync(page, "ai-viz-last-event"), "Nothing has happened yet");

        await PlayAsync(page);

        Assert.AreEqual("Running", await TextAsync(page, "ai-viz-state"));
        await page.WaitForFunctionAsync(
            "() => Number(document.querySelector(\"[data-testid='ai-viz-sequence']\")?.textContent) >= 3",
            new PageWaitForFunctionOptions { Timeout = 15000 });

        // The run really reaches the surfaces: the lanes grow a block the seeded snapshot did not have.
        await ScrollToAsync(page, LanesSection);
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='ai-viz-lanes'] [data-testid='agent-lanes-block']\").length >= 7",
            new PageWaitForFunctionOptions { Timeout = 15000 });

        var before = int.Parse(await TextAsync(page, "ai-viz-events"), System.Globalization.CultureInfo.InvariantCulture);
        await ScrollToAsync(page, SimulatorSection);
        await page.Locator("[data-testid='ai-viz-play']").ClickAsync();
        await page.WaitForTimeoutAsync(500);
        Assert.AreEqual("Paused", await TextAsync(page, "ai-viz-state"));
        var paused = int.Parse(await TextAsync(page, "ai-viz-events"), System.Globalization.CultureInfo.InvariantCulture);
        await page.WaitForTimeoutAsync(600);
        Assert.AreEqual(paused, int.Parse(
            await TextAsync(page, "ai-viz-events"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.IsTrue(paused >= before, "a paused generator never loses what it already applied");

        // The fastest choice raises the rate; the page's one live region says what changed.
        await page.Locator("[data-testid='ai-viz-speed'] [data-testid='segmented-option']").Last.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-viz-rate']\")?.textContent?.trim() === '20'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        StringAssert.Contains(await page.Locator("[data-testid='ai-viz-announcer']").TextContentAsync(), "20");

        // A reset clears the counters and stops the generator.
        await page.Locator("[data-testid='ai-viz-reset']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-viz-events']\")?.textContent?.trim() === '0'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual("Paused", await TextAsync(page, "ai-viz-state"));
        Assert.AreEqual("0", await TextAsync(page, "ai-viz-sequence"));

        await ShootAsync(page, "ai-viz-simulator-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-viz-simulator");
    }

    [TestMethod]
    public async Task TheLanesTakeABlockByKeyboard_AndSwitchToTheirTable()
    {
        var page = await OpenAsync();
        await ScrollToAsync(page, LanesSection);

        var block = page.Locator("[data-testid='ai-viz-lanes'] [data-testid='agent-lanes-block']").First;
        await block.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await block.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await page.WaitForFunctionAsync(
            "() => !document.querySelector(\"[data-testid='ai-viz-lanes-selected']\")?.textContent?.includes('No block selected')",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        await page.Locator("[data-testid='ai-viz-lanes'] [data-testid='agent-lanes-view'] button").Last.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='ai-viz-lanes'] [data-testid='agent-lanes-table-row']\").length > 0",
            new PageWaitForFunctionOptions { Timeout = 15000 });
        Assert.AreEqual(0, await page.Locator("[data-testid='ai-viz-lanes'] [data-testid='agent-lanes-block']").CountAsync(),
            "the table replaces the timeline, so nothing is announced twice");

        var row = page.Locator("[data-testid='ai-viz-lanes'] [data-testid='agent-lanes-table-row']").First;
        await row.ScrollIntoViewIfNeededAsync();
        await ShootAsync(page, "ai-viz-lanes-table-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-viz-lanes-section");
    }

    [TestMethod]
    public async Task TheGraphWalksItsNodesWithTheArrowKeys_AndSwitchesToItsTable()
    {
        var page = await OpenAsync();
        await ScrollToAsync(page, GraphSection);

        var node = page.Locator("[data-testid='ai-viz-graph'] [data-testid='agent-graph-node'][data-node-id='planner']");
        await node.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await node.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-viz-graph-selected']\")?.textContent?.includes('Planner')",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        var firstNode = await page.EvaluateAsync<string>(
            "() => document.activeElement?.getAttribute('data-node-id') ?? ''");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.WaitForTimeoutAsync(300);
        var nextNode = await page.EvaluateAsync<string>(
            "() => document.activeElement?.getAttribute('data-node-id') ?? ''");
        Assert.AreNotEqual(firstNode, nextNode, "the arrow key walks the graph instead of the page");

        // A call pulse is the incremental path the page feeds: the graph continues to render while the run plays.
        await ScrollToAsync(page, SimulatorSection);
        await PlayAsync(page);
        await page.WaitForTimeoutAsync(1200);
        await ScrollToAsync(page, GraphSection);
        Assert.IsTrue(await page.Locator("[data-testid='ai-viz-graph'] [data-testid='agent-graph-node']").CountAsync() >= 7);

        await page.Locator("[data-testid='ai-viz-graph'] [data-testid='agent-graph-view'] button").Last.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='ai-viz-graph'] [data-testid='agent-graph-table-row']\").length > 0",
            new PageWaitForFunctionOptions { Timeout = 15000 });
        await ShootAsync(page, "ai-viz-graph-table-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-viz-graph-section");
    }

    [TestMethod]
    public async Task TheDelegateBoardTakesACardByPointerDrag_AndOffersItsMenuAlternative()
    {
        var page = await OpenAsync();
        await ScrollToAsync(page, DelegateSection);

        var board = page.Locator("[data-testid='ai-viz-delegate']");
        var card = board.Locator("[data-task-id='task-summary']").First;
        await card.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // The grip is a SIBLING of the card's content (the row owns it), so the drag starts on the Row of the first
        // backlog card — which is the one the assertion below follows into the planner column.
        var row = board.Locator("[data-testid='delegate-backlog-list'] [data-tm-sortable-item]").First;
        var handle = row.Locator("[data-tm-sortable-handle]");
        await handle.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var source = await handle.BoundingBoxAsync();
        var target = await board.Locator("[data-column='planner']").BoundingBoxAsync();
        Assert.IsNotNull(source);
        Assert.IsNotNull(target);

        // The shared pointer-drag module owns the gesture: press the handle, cross the threshold in steps, release
        // over the target column. This is the cross-list transfer phase 7 could only verify through the module.
        await page.Mouse.MoveAsync(source!.X + (source.Width / 2), source.Y + (source.Height / 2));
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(source.X + (source.Width / 2) + 20, source.Y + (source.Height / 2) + 20,
            new MouseMoveOptions { Steps = 5 });
        await page.Mouse.MoveAsync(target!.X + (target.Width / 2), target.Y + Math.Min(120, target.Height / 2),
            new MouseMoveOptions { Steps = 15 });
        await page.WaitForTimeoutAsync(200);
        await page.Mouse.UpAsync();

        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-viz-delegate'] [data-column='planner'] "
            + "[data-task-id='task-summary']\") !== null",
            new PageWaitForFunctionOptions { Timeout = 15000 });
        StringAssert.Contains(await TextAsync(page, "ai-viz-delegate-log"), "assigned to Planner");

        // The same move is reachable without a pointer: the card's Assign menu is the tap and keyboard alternative.
        const string next = "[data-task-id='task-notes']";
        await board.Locator(next).Locator(".tm-context-menu__trigger").First.ClickAsync();
        await board.Locator(".tm-context-menu__item").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-viz-delegate'] [data-task-id='task-notes']\") "
            + "?.closest('[data-column]') !== null",
            new PageWaitForFunctionOptions { Timeout = 15000 });

        var column = await page.Locator("[data-testid='ai-viz-delegate'] [data-task-id='task-notes']")
            .EvaluateAsync<string?>("card => card.closest('[data-column]')?.getAttribute('data-column')");
        Assert.AreNotEqual("backlog", column, "the Assign menu moved the card out of the backlog");

        // ONE polite live region for the board, and a full agent still refuses a move.
        Assert.AreEqual(1, await board.Locator("[aria-live]").CountAsync());
        Assert.AreEqual("true", await board.Locator("[data-column='reviewer']").GetAttributeAsync("data-full"));

        await ShootAsync(page, "ai-viz-delegate-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-viz-delegate-section");
    }

    [TestMethod]
    public async Task TheMemoryPanelForgetsAAndUndoesIt_AndTheTableListsEveryFact()
    {
        var page = await OpenAsync();
        await ScrollToAsync(page, MemorySection);

        var panel = page.Locator("[data-testid='ai-viz-memory']");
        Assert.AreEqual(20, await panel.Locator("[data-testid='memory-item']").CountAsync());
        Assert.AreEqual(4, await panel.Locator("[data-testid='memory-group']").CountAsync(),
            "twenty facts over four categories");

        var item = panel.Locator("[data-testid='memory-item']").First;
        var id = await item.GetAttributeAsync("data-item-id");
        await item.Locator("[data-testid='memory-item-forget']").ClickAsync();
        await panel.Locator("[data-testid='memory-undo']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        Assert.AreEqual(19, await panel.Locator("[data-testid='memory-item']").CountAsync());

        await panel.Locator("[data-testid='memory-undo-action']").ClickAsync();
        await panel.Locator($"[data-testid='memory-item'][data-item-id='{id}']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        Assert.AreEqual(20, await panel.Locator("[data-testid='memory-item']").CountAsync());
        Assert.AreEqual("Nothing has happened yet.", await TextAsync(page, "ai-viz-memory-log"),
            "an undone removal is never reported to the page");

        // A second removal whose undo window CLOSES is the one the host really applies (the demo window is 10 s).
        await panel.Locator("[data-testid='memory-item']").First
            .Locator("[data-testid='memory-item-forget']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-viz-memory-log']\")?.textContent?.includes('Forgotten')",
            new PageWaitForFunctionOptions { Timeout = 20000 });
        Assert.AreEqual(19, await panel.Locator("[data-testid='memory-item']").CountAsync());

        // The table presentation carries all twenty rows, so the panel is never the only way in.
        await page.Locator("[data-testid='ai-viz-memory-view']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-viz-memory-table']",
            new PageWaitForSelectorOptions { Timeout = 10000 });
        Assert.AreEqual(19,
            await page.Locator("[data-testid='ai-viz-memory-table'] [data-testid='ai-viz-memory-row']").CountAsync(),
            "the table renders the host's store, which the closed undo window has already removed the fact from");
        Assert.AreEqual(0, await page.Locator("[data-testid='ai-viz-memory']").CountAsync(),
            "the graphical panel is replaced, not duplicated");
        await AssertSwitchNamesItsActionAsync(page, "ai-viz-memory-view", "Show the visual view");

        await ShootAsync(page, "ai-viz-memory-table-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-viz-memory-section");
    }

    [TestMethod]
    public async Task TheConflictRadarSettlesEveryConflictByKeyboard_AndMerges()
    {
        var page = await OpenAsync();
        await ScrollToAsync(page, ConflictSection);

        var radar = page.Locator("[data-testid='ai-viz-conflict']");
        Assert.AreEqual(4, await radar.Locator("[data-testid='radar-row'][data-conflict='true']").CountAsync());
        Assert.AreEqual(3, await radar.Locator("[data-testid='radar-agent-header']").CountAsync());
        Assert.IsNotNull(await radar.Locator("[data-testid='radar-merge']").GetAttributeAsync("disabled"),
            "the merge waits until every conflict is decided");

        var groups = radar.Locator("[role='radiogroup']");
        for (var index = 0; index < 4; index++)
        {
            var first = groups.Nth(index).Locator("input[type='radio']").First;
            await first.FocusAsync();
            await page.Keyboard.PressAsync("ArrowRight");
            await first.PressAsync("Space");
        }

        await page.WaitForFunctionAsync(
            "() => !document.querySelector(\"[data-testid='ai-viz-conflict'] [data-testid='radar-merge']\")?.hasAttribute('disabled')",
            new PageWaitForFunctionOptions { Timeout = 15000 });
        StringAssert.Contains(await TextAsync(page, "ai-viz-conflict-log"), "settled");

        await radar.Locator("[data-testid='radar-merge']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-viz-conflict-log']\")?.textContent?.includes('Merge requested')",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual(4, await radar.Locator("[role='radiogroup'] input[type='radio']:checked").CountAsync());

        await ShootAsync(page, "ai-viz-conflict-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-viz-conflict-section");
    }

    [TestMethod]
    public async Task TheAutonomyDialStepsByItsOwnKeys_AndTheLocksStayVisible()
    {
        var page = await OpenAsync();
        await ScrollToAsync(page, AutonomySection);

        var dial = page.Locator("[data-testid='ai-viz-autonomy']");
        var slider = dial.Locator("[data-testid='autonomy-dial-track']");
        Assert.AreEqual("slider", await slider.GetAttributeAsync("role"));
        Assert.AreEqual("1", await slider.GetAttributeAsync("aria-valuenow"));
        Assert.AreEqual(2, await dial.Locator("[data-testid='autonomy-lock']").CountAsync());

        await slider.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await dial.Locator("[data-testid='autonomy-confirm']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(250);
        Assert.AreEqual("slider",
            await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('role') ?? 'none'"),
            "a cancelled change leaves the focus on the slider it took it from");
        Assert.AreEqual("1", await slider.GetAttributeAsync("aria-valuenow"));

        await slider.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await dial.Locator("[data-testid='autonomy-confirm-accept']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await dial.Locator("[data-testid='autonomy-confirm-accept']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-viz-autonomy-log']\")?.textContent?.includes('Act')",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        await ShootAsync(page, "ai-viz-autonomy-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-viz-autonomy-section");
    }

    [TestMethod]
    public async Task EveryGraphicalSurfaceReachesItsTableAlternative()
    {
        var page = await OpenAsync();

        await ScrollToAsync(page, FanOutSection);
        await page.Locator("[data-testid='ai-viz-fan-out-view']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-viz-fan-out-table']");
        Assert.AreEqual(40,
            await page.Locator("[data-testid='ai-viz-fan-out-table'] [data-testid='ai-viz-fan-out-row']").CountAsync());
        await AssertSwitchNamesItsActionAsync(page, "ai-viz-fan-out-view", "Show the visual view");

        await ScrollToAsync(page, AttemptsSection);
        await page.Locator("[data-testid='ai-viz-attempts-view']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-viz-attempts-table']");
        Assert.AreEqual(3,
            await page.Locator("[data-testid='ai-viz-attempts-table'] [data-testid='ai-viz-attempts-row']").CountAsync());
        await AssertSwitchNamesItsActionAsync(page, "ai-viz-attempts-view", "Show the visual view");

        await ScrollToAsync(page, DelegateSection);
        await page.Locator("[data-testid='ai-viz-delegate-view']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-viz-delegate-table']");
        Assert.AreEqual(8,
            await page.Locator("[data-testid='ai-viz-delegate-table'] [data-testid='ai-viz-delegate-row']").CountAsync());
        await AssertSwitchNamesItsActionAsync(page, "ai-viz-delegate-view", "Show the visual view");

        await ScrollToAsync(page, ConflictSection);
        await page.Locator("[data-testid='ai-viz-conflict-view']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-viz-conflict-table']");
        Assert.AreEqual(29,
            await page.Locator("[data-testid='ai-viz-conflict-table'] [data-testid='ai-viz-conflict-row']").CountAsync());
        Assert.AreEqual(4,
            await page.Locator("[data-testid='ai-viz-conflict-decisions'] [data-testid='ai-viz-conflict-decision']")
                .CountAsync());
        await AssertSwitchNamesItsActionAsync(page, "ai-viz-conflict-view", "Show the visual view");

        await ScrollToAsync(page, PresenceSection);
        await page.Locator("[data-testid='ai-viz-presence-view']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-viz-presence-table']");
        Assert.AreEqual(4,
            await page.Locator("[data-testid='ai-viz-presence-table'] [data-testid='ai-viz-presence-row']").CountAsync());
        await AssertSwitchNamesItsActionAsync(page, "ai-viz-presence-view", "Show the visual view");

        // The components with their own switcher carry the same alternative.
        await ScrollToAsync(page, HandoffSection);
        await page.Locator("[data-testid='ai-viz-handoff'] [data-testid='agent-handoff-view'] button").Last.ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-viz-handoff'] [data-testid='agent-handoff-table-row']",
            new PageWaitForSelectorOptions { Timeout = 15000 });

        await ScrollToAsync(page, FallbackSection);
        await page.Locator("[data-testid='ai-viz-fallback'] [data-testid='fallback-chain-view'] button").Last.ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-viz-fallback'] [data-testid='fallback-chain-table-row']",
            new PageWaitForSelectorOptions { Timeout = 15000 });

        await ScrollToAsync(page, RunsSection);
        await page.Locator("[data-testid='ai-viz-runs'] [data-testid='runs-view'] [data-testid='segmented-option'][data-value='Table']")
            .ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-viz-runs'] [data-testid='runs-table-row']",
            new PageWaitForSelectorOptions { Timeout = 15000 });

        await ShootAsync(page, "ai-viz-tables-light-1440");
    }
}
