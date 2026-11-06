using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan d67b33af phase 9 — the <c>/ai-agent-visualizations</c> demo page drives EVERY agent surface of the package
/// from ONE simulated run. The assertions read behaviour rather than markup: the generator plays, pauses, changes its
/// rate and resets, the lanes take a block from the keyboard and switch to their table, the graph walks its nodes with
/// the arrow keys, the delegate board takes a card by pointer drag AND by its Assign menu, a remembered fact is
/// forgotten and undone, every conflict is settled from the keyboard before the merge, the autonomy dial steps by its
/// own keys, and every graphical surface reaches a keyboard-readable table. A scoped axe scan runs per section.
/// </summary>
public abstract class AiAgentVisualizationsE2EBase : PlaywrightTestBase
{
    protected const string SimulatorSection = "[data-testid='ai-viz-simulator']";
    protected const string LanesSection = "[data-testid='ai-viz-lanes-section']";
    protected const string GraphSection = "[data-testid='ai-viz-graph-section']";
    protected const string FanOutSection = "[data-testid='ai-viz-fan-out-section']";
    protected const string AttemptsSection = "[data-testid='ai-viz-attempts-section']";
    protected const string HandoffSection = "[data-testid='ai-viz-handoff-section']";
    protected const string FallbackSection = "[data-testid='ai-viz-fallback-section']";
    protected const string AutonomySection = "[data-testid='ai-viz-autonomy-section']";
    protected const string MemorySection = "[data-testid='ai-viz-memory-section']";
    protected const string DelegateSection = "[data-testid='ai-viz-delegate-section']";
    protected const string RunsSection = "[data-testid='ai-viz-runs-section']";
    protected const string ConflictSection = "[data-testid='ai-viz-conflict-section']";
    protected const string PresenceSection = "[data-testid='ai-viz-presence-section']";

    protected static readonly string[] AllSections =
    [
        "ai-viz-simulator", "ai-viz-lanes-section", "ai-viz-graph-section", "ai-viz-fan-out-section",
        "ai-viz-attempts-section", "ai-viz-handoff-section", "ai-viz-fallback-section", "ai-viz-autonomy-section",
        "ai-viz-memory-section", "ai-viz-delegate-section", "ai-viz-runs-section", "ai-viz-conflict-section",
        "ai-viz-presence-section",
    ];

    protected string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "ai-agent-visualizations");

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

    /// <summary>Opens the page with the culture pinned to English, the way every AI package spec does.</summary>
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
        await GotoAppReadyAsync(page, $"{BaseUrl}/ai-agent-visualizations");
        await page.WaitForSelectorAsync(
            $"{SimulatorSection} [data-testid='ai-viz-play']",
            new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    /// <summary>The text of one test id, trimmed.</summary>
    protected static async Task<string> TextAsync(IPage page, string testId)
        => ((await page.Locator($"[data-testid='{testId}']").First.TextContentAsync()) ?? string.Empty).Trim();

    /// <summary>
    /// One of the page's graphic/table switches. It is a plain ACTION button: its label names the presentation the
    /// next press shows, so it must not also carry <c>aria-pressed</c> — a screen reader would hear "Show the visual
    /// view, pressed" while the table is on screen (WCAG 4.1.2; the APG keeps a toggle button's name fixed).
    /// </summary>
    protected static async Task AssertSwitchNamesItsActionAsync(IPage page, string testId, string expectedLabel)
    {
        var switchButton = page.Locator($"[data-testid='{testId}']");

        Assert.IsNull(await switchButton.GetAttributeAsync("aria-pressed"),
            $"{testId} must stay a plain action button: its label already names the presentation");
        StringAssert.Contains(await switchButton.InnerTextAsync(), expectedLabel,
            $"{testId} names the presentation the next press shows");
    }

    protected static async Task ScrollToAsync(IPage page, string selector)
        => await page.Locator(selector).ScrollIntoViewIfNeededAsync();

    /// <summary>
    /// Brings a section to the TOP of the viewport (a plain scroll-into-view does nothing when the element is already
    /// partly visible, which framed the wrong section in a reference shot).
    /// </summary>
    protected static async Task ScrollToTopAsync(IPage page, string selector)
    {
        // The demo's top bar is sticky, so a plain block-start scroll parks the section's heading UNDER it. The scroll
        // is INSTANT on purpose: a smooth scroll is still animating when the screenshot is taken, which framed the
        // PREVIOUS section in the reference shots.
        await page.Locator(selector).EvaluateAsync(
            "el => { el.scrollIntoView({ block: 'start', behavior: 'instant' }); window.scrollBy({ top: -112, behavior: 'instant' }); }");
        await page.WaitForTimeoutAsync(500);
    }

    /// <summary>Starts the generator and waits until the run has applied changes.</summary>
    protected static async Task PlayAsync(IPage page)
    {
        await ScrollToAsync(page, SimulatorSection);
        await page.Locator("[data-testid='ai-viz-play']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => Number(document.querySelector(\"[data-testid='ai-viz-events']\")?.textContent) > 0",
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    /// <summary>Switches the demo's own theme through the header toggle, so the whole frame repaints.</summary>
    protected static async Task ToggleThemeAsync(IPage page)
    {
        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await page.WaitForTimeoutAsync(600);
    }

    /// <summary>Frames the section under review: a full-page shot of a long demo page proves nothing.</summary>
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

    /// <summary>A scoped axe scan; a color-contrast failure reports the colour pair, not just the selector.</summary>
    protected static async Task AssertNoSeriousAxeViolationsAsync(IPage page, params string[] testIds)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        foreach (var testId in testIds)
        {
            var selector = $"[data-testid='{testId}']";
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
                        let background = style.backgroundColor;
                        let parent = el.parentElement;
                        while (parent && (background === 'rgba(0, 0, 0, 0)' || background === 'transparent')) {
                            background = getComputedStyle(parent).backgroundColor;
                            parent = parent.parentElement;
                        }
                        return `${node.target.join(' ')} [color ${style.color} on ${background}]`;
                    };
                    return result.violations
                        .filter(v => v.impact === 'critical' || v.impact === 'serious')
                        .map(v => `${v.id}: ${v.help} :: ${v.nodes.slice(0, 3).map(describe).join(' | ')}`);
                }
                """,
                selector);

            Assert.AreEqual(0, violations.Length, $"axe [{testId}]: " + string.Join(" | ", violations));
        }
    }
}

/// <summary>The WASM lane: the whole simulated run and every reader path.</summary>
[TestClass]
[TestCategory("WASM")]
public class AiAgentVisualizationsE2ETests : AiAgentVisualizationsE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";

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

/// <summary>The Server lane smoke: the same page renders its simulator and passes the scoped scan on Server.</summary>
[TestClass]
[TestCategory("Server")]
public class AiAgentVisualizationsServerSmokeE2ETests : AiAgentVisualizationsE2EBase
{
    protected override string BaseUrl => "https://localhost:7107";

    [TestMethod]
    public async Task TheSimulatorRunsOnTheServerHost()
    {
        var page = await OpenAsync();

        Assert.AreEqual("Paused", await TextAsync(page, "ai-viz-state"));
        await PlayAsync(page);

        Assert.AreEqual("Running", await TextAsync(page, "ai-viz-state"));
        await page.WaitForFunctionAsync(
            "() => Number(document.querySelector(\"[data-testid='ai-viz-sequence']\")?.textContent) >= 3",
            new PageWaitForFunctionOptions { Timeout = 20000 });
    }

    [TestMethod]
    public async Task TheSectionsPassAScopedAxeScan_OnTheServerHost()
        => await AssertNoSeriousAxeViolationsAsync(await OpenAsync(), AllSections);
}
