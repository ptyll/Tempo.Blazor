using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for the TOUCH drag of <c>TmKanbanBoard</c> (plan 92e96b25, phase 5 / TG-005, decision O7), driven
/// through Chromium's own touch pipeline (CDP <c>Input.dispatchTouchEvent</c> on a <c>hasTouch</c> context)
/// so the pointer events, the 400 ms long-press gate, pointer capture and <c>touch-action</c> are the real
/// browser ones, not synthetic DOM events. Covers: the long-press-vs-scroll disambiguation (moving before
/// the hold completes must not drag), a commit onto an expanded column, a collapsed strip and a collapsed
/// lane's slot, a virtualized column scrolled before the drop (the anchor is the hovered card's
/// full-list index, not its visible-window position), the edge auto-scroll (the board scrolls near its
/// edge and stays still away from it), and the cancel paths (<c>pointercancel</c>, a second finger) that
/// roll back and announce.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class KanbanTouchDragE2ETests : WasmTestBase
{
    private async Task<IPage> OpenKanbanTouchAsync()
    {
        var page = await CreatePageAsync(hasTouch: true);
        await page.GotoAsync($"{BaseUrl}/kanban");
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-kanban__column-toggle").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        return page;
    }

    private static async Task<ICDPSession> CdpAsync(IPage page) => await page.Context.NewCDPSessionAsync(page);

    private static Task TouchStartAsync(ICDPSession session, int x, int y)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchStart",
            ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y } },
        });

    private static Task TouchMoveAsync(ICDPSession session, int x, int y)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchMove",
            ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y } },
        });

    private static Task TouchEndAsync(ICDPSession session)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchEnd",
            ["touchPoints"] = Array.Empty<object>(),
        });

    /// <summary>Presses at <paramref name="from"/>, holds past the 400 ms long-press, travels in steps and releases.</summary>
    private static async Task LongPressDragAsync(ICDPSession session, (int X, int Y) from, (int X, int Y) to)
    {
        await TouchStartAsync(session, from.X, from.Y);
        await Task.Delay(500);
        const int steps = 6;
        for (var i = 1; i <= steps; i++)
        {
            await TouchMoveAsync(session,
                from.X + (to.X - from.X) * i / steps,
                from.Y + (to.Y - from.Y) * i / steps);
            await Task.Delay(30);
        }

        await Task.Delay(60);
        await TouchEndAsync(session);
    }

    private static async Task<(int X, int Y)> CenterAsync(ILocator locator)
    {
        var box = await locator.BoundingBoxAsync()
                  ?? throw new InvalidOperationException("the element has no box");
        return ((int)(box.X + box.Width / 2), (int)(box.Y + box.Height / 2));
    }

    private static async Task<string> StatusTextAsync(IPage page, string boardSelector)
        => (await page.Locator($"{boardSelector} [data-testid='kanban-status']").InnerTextAsync()).Trim();

    [TestMethod]
    [Description("E2E-TG5-T1: a 400 ms long-press lifts a card and drops it on another column — the move commits, " +
                 "the card does not also click, and the one live region announces it")]
    public async Task LongPressDrag_CrossColumn_Commits_Announces_AndDoesNotClick()
    {
        var page = await OpenKanbanTouchAsync();
        var board = page.Locator(".tm-kanban:not(.tm-kanban--swimlanes)").First;
        var card = board.Locator("[data-testid='board-column-backlog'] .tm-kanban__card",
            new LocatorLocatorOptions { HasText = "Setup CI/CD pipeline" });
        var target = board.Locator("[data-testid='board-column-doing'] .tm-kanban__cards");

        var from = await CenterAsync(card);
        var to = await CenterAsync(target);
        var session = await CdpAsync(page);

        await TouchStartAsync(session, from.X, from.Y);
        await Task.Delay(500);
        // The card is lifted while the finger still travels.
        await TouchMoveAsync(session, from.X + 10, from.Y);
        var lifted = await page.Locator(".tm-kanban__card--touch-dragging").CountAsync();
        Assert.AreEqual(1, lifted, "the long-press lifts exactly the pressed card");

        // Mid-flight capture over the expanded column: the dragged card must be visible under the
        // finger and the board must not have scrolled away under the gesture.
        await TouchMoveAsync(session, (from.X + to.X) / 2, from.Y);
        await page.WaitForTimeoutAsync(100);
        var scrollBefore = await page.EvaluateAsync<int>(
            "() => document.querySelector('.tm-kanban:not(.tm-kanban--swimlanes)').scrollLeft");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = ShotPath("kanban-touch-drag-mid-flight-light.png"), FullPage = false
        });
        var scrollAfter = await page.EvaluateAsync<int>(
            "() => document.querySelector('.tm-kanban:not(.tm-kanban--swimlanes)').scrollLeft");
        Assert.AreEqual(scrollBefore, scrollAfter,
            "the board does not scroll while the drag owns the gesture");

        await TouchMoveAsync(session, to.X, to.Y);
        await TouchEndAsync(session);

        var change = page.Locator("p:has-text('Last change')").First;
        await change.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var text = await change.InnerTextAsync();
        StringAssert.Contains(text, "'Setup CI/CD pipeline' moved from backlog to doing",
            "the touch drop reports the same contract as an HTML5 drop");

        StringAssert.Contains(await StatusTextAsync(page, ".tm-kanban:not(.tm-kanban--swimlanes)"),
            "Setup CI/CD pipeline moved to In Progress",
            "the one polite live region announces the touch drop");

        Assert.AreEqual(0, await page.Locator("p:has-text('Clicked:')").CountAsync(),
            "the click that follows a real drag is swallowed — OnCardClick must not fire");
    }

    [TestMethod]
    [Description("E2E-TG5-T2: moving before the 400 ms hold completes is a scroll, not a drag — the card never lifts")]
    public async Task MovingBeforeTheHold_IsNotADrag()
    {
        var page = await OpenKanbanTouchAsync();
        var board = page.Locator(".tm-kanban:not(.tm-kanban--swimlanes)").First;
        var card = board.Locator("[data-testid='board-column-backlog'] .tm-kanban__card",
            new LocatorLocatorOptions { HasText = "Design login page" });
        var from = await CenterAsync(card);
        var session = await CdpAsync(page);

        await TouchStartAsync(session, from.X, from.Y);
        // Move immediately, well past the drag threshold, inside the hold window.
        for (var i = 1; i <= 4; i++)
        {
            await TouchMoveAsync(session, from.X, from.Y - 30 * i);
            await Task.Delay(25);
        }

        await Task.Delay(500);
        Assert.AreEqual(0, await page.Locator(".tm-kanban__card--touch-dragging").CountAsync(),
            "a swipe that started before the hold never became a drag");
        await TouchEndAsync(session);
        Assert.AreEqual(0, await page.Locator("p:has-text('Last change')").CountAsync(),
            "nothing moved");
    }

    [TestMethod]
    [Description("E2E-TG5-T3: a long-press drop onto a COLLAPSED column strip appends the card — collapsed zones " +
                 "are full citizens of the touch path")]
    public async Task LongPressDrop_OnACollapsedStrip_Appends()
    {
        var page = await OpenKanbanTouchAsync();
        var board = page.Locator(".tm-kanban:not(.tm-kanban--swimlanes)").First;
        var strip = board.Locator("[data-testid='board-column-done']");
        await strip.Locator("[data-testid='column-toggle-done']").TapAsync();
        await page.WaitForTimeoutAsync(200);

        var card = board.Locator("[data-testid='board-column-doing'] .tm-kanban__card",
            new LocatorLocatorOptions { HasText = "Fix navigation bug" });
        var from = await CenterAsync(card);
        var to = await CenterAsync(strip);
        var session = await CdpAsync(page);

        await TouchStartAsync(session, from.X, from.Y);
        await Task.Delay(500);
        await TouchMoveAsync(session, to.X, to.Y);
        await page.WaitForTimeoutAsync(100);
        StringAssert.Contains(await strip.GetAttributeAsync("class") ?? string.Empty,
            "tm-kanban__column--drop-target", "the collapsed strip highlights under the finger");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = ShotPath("kanban-touch-drag-collapsed-strip-light.png"), FullPage = false
        });
        await TouchEndAsync(session);

        var change = page.Locator("p:has-text('Last change')").First;
        await change.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        StringAssert.Contains(await change.InnerTextAsync(), "moved from doing to done");
        Assert.AreEqual("3", (await strip.Locator(".tm-kanban__count").InnerTextAsync()).Trim(),
            "the collapsed strip's count reflects the appended card");
    }

    [TestMethod]
    [Description("E2E-TG5-T4: a long-press drop into a COLLAPSED lane's slot commits a (column, lane) move")]
    public async Task LongPressDrop_OnACollapsedLaneSlot_CommitsColumnAndLane()
    {
        var page = await OpenKanbanTouchAsync();
        var board = page.Locator(".tm-kanban--swimlanes").First;
        var lane = board.Locator("[data-testid='swimlane-Alice']");
        await lane.Locator(".tm-kanban__swimlane-toggle").TapAsync();
        await page.WaitForTimeoutAsync(200);

        var card = board.Locator("[data-testid='cell-doing-Bob'] .tm-kanban__card",
            new LocatorLocatorOptions { HasText = "Implement auth module" });
        var slot = board.Locator("[data-testid='lane-slot-todo-Alice']");
        var from = await CenterAsync(card);
        var to = await CenterAsync(slot);
        var session = await CdpAsync(page);

        await LongPressDragAsync(session, from, to);

        var change = page.Locator("p:has-text('Last change')").Last;
        await change.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var text = await change.InnerTextAsync();
        StringAssert.Contains(text, "column todo", "the slot dropped into the To Do column");
        StringAssert.Contains(text, "lane 'Alice'", "the slot dropped into the Alice lane");
    }

    [TestMethod]
    [Description("E2E-TG5-T5: pointercancel rolls the touch drag back and announces the cancel — nothing moves")]
    public async Task PointerCancel_RollsBack_AndAnnounces()
    {
        var page = await OpenKanbanTouchAsync();
        var board = page.Locator(".tm-kanban:not(.tm-kanban--swimlanes)").First;
        var card = board.Locator("[data-testid='board-column-backlog'] .tm-kanban__card",
            new LocatorLocatorOptions { HasText = "Setup CI/CD pipeline" });
        var target = board.Locator("[data-testid='board-column-doing'] .tm-kanban__cards");
        var from = await CenterAsync(card);
        var to = await CenterAsync(target);
        var session = await CdpAsync(page);

        await TouchStartAsync(session, from.X, from.Y);
        await Task.Delay(500);
        await TouchMoveAsync(session, (from.X + to.X) / 2, (from.Y + to.Y) / 2);
        Assert.AreEqual(1, await page.Locator(".tm-kanban__card--touch-dragging").CountAsync());

        await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchCancel",
            ["touchPoints"] = Array.Empty<object>(),
        });
        await page.WaitForTimeoutAsync(200);

        Assert.AreEqual(0, await page.Locator(".tm-kanban__card--touch-dragging").CountAsync(),
            "the lifted card is back");
        Assert.AreEqual(0, await page.Locator(".tm-kanban--dragging").CountAsync(),
            "no zone stays advertised");
        StringAssert.Contains(await StatusTextAsync(page, ".tm-kanban:not(.tm-kanban--swimlanes)"),
            "Move of Setup CI/CD pipeline cancelled — the card stayed in Backlog",
            "the rollback is announced through the one polite live region");
        Assert.AreEqual(0, await page.Locator("p:has-text('Last change')").CountAsync(),
            "a cancelled gesture commits nothing");
    }

    [TestMethod]
    [Description("E2E-TG5-T6: a second finger can neither move nor end the gesture — the first finger's " +
                 "drag still commits; a browser/system takeover arrives as pointercancel (E2E-TG5-T5)")]
    public async Task SecondFinger_NeitherMovesNorEnds_TheFirstFingerCommits()
    {
        var page = await OpenKanbanTouchAsync();
        var board = page.Locator(".tm-kanban:not(.tm-kanban--swimlanes)").First;
        var card = board.Locator("[data-testid='board-column-backlog'] .tm-kanban__card",
            new LocatorLocatorOptions { HasText = "Design login page" });
        var target = board.Locator("[data-testid='board-column-doing'] .tm-kanban__cards");
        var from = await CenterAsync(card);
        var to = await CenterAsync(target);
        var session = await CdpAsync(page);

        await TouchStartAsync(session, from.X, from.Y);
        await Task.Delay(500);
        await TouchMoveAsync(session, (from.X + to.X) / 2, (from.Y + to.Y) / 2);
        Assert.AreEqual(1, await page.Locator(".tm-kanban__card--touch-dragging").CountAsync());

        // The second finger lands and both move — the hijack attempt. The shared helper ignores a
        // non-primary pointer, so the drag stays owned by finger 1.
        await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchStart",
            ["touchPoints"] = new[]
            {
                new Dictionary<string, object> { ["x"] = (from.X + to.X) / 2, ["y"] = (from.Y + to.Y) / 2 },
                new Dictionary<string, object> { ["x"] = (from.X + to.X) / 2 + 90, ["y"] = (from.Y + to.Y) / 2 },
            },
        });
        for (var i = 1; i <= 2; i++)
        {
            await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchMove",
                ["touchPoints"] = new[]
                {
                    new Dictionary<string, object> { ["x"] = (from.X + to.X) / 2 + 10 * i, ["y"] = (from.Y + to.Y) / 2 },
                    new Dictionary<string, object> { ["x"] = (from.X + to.X) / 2 + 90 + 60 * i, ["y"] = (from.Y + to.Y) / 2 },
                },
            });
            await Task.Delay(30);
        }

        // Finger 1 was not moved by the second finger's drift: the gesture is still alive and the
        // card still follows finger 1's coordinates, not the second finger's.
        Assert.AreEqual(1, await page.Locator(".tm-kanban__card--touch-dragging").CountAsync(),
            "the second finger neither cancels nor re-targets the drag");

        // Finger 2 lifts (one touch point remains); finger 1 completes the drag onto the target.
        await TouchMoveAsync(session, to.X, to.Y);
        await TouchEndAsync(session);

        var change = page.Locator("section", new PageLocatorOptions { HasText = "Reorderable board" })
            .Locator("p:has-text('Last change')");
        await change.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        StringAssert.Contains(await change.InnerTextAsync(), "'Design login page' moved from backlog to doing",
            "the first finger's drag commits normally after the second-finger attempt");
    }

    [TestMethod]
    [Description("E2E-TG5-T7: scrolling a virtualized column before dropping — the touch drop anchors on " +
                 "the hovered card's FULL-LIST index, so a windowed cell lands where the finger saw it")]
    public async Task LongPressDrop_AfterScrollingAVirtualizedColumn_UsesTheFullListIndex()
    {
        var page = await OpenKanbanTouchAsync();
        var board = page.Locator("[data-testid='virtual-board-column-todo']").Locator("..");
        var todoWindow = board.Locator("[data-testid='virtual-cards-virtual-todo']");

        // Scroll the virtual column so the rendered window no longer starts at index 0. The real
        // card pitch (78 px here) differs from the estimated CardItemSize (92 px), so no manual
        // item math: the chosen card is revealed inside the container with scrollIntoView.
        await todoWindow.EvaluateAsync("el => el.scrollTop = 1840");
        await page.WaitForTimeoutAsync(300);
        var firstIndex = int.Parse((await todoWindow.Locator(".tm-kanban__card").First
            .GetAttributeAsync("data-tm-kanban-index"))!);
        Assert.IsTrue(firstIndex > 0, "scrolling shifted the rendered window past the first cards");

        // The virtual board sits far down the page: keep its section in the viewport.
        await page.Locator("section", new PageLocatorOptions { HasText = "Virtualized columns" })
            .ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);

        // Long-press a fully-visible card of the scrolled window (the first card can sit partially
        // clipped at the container's top edge after the scroll — scrollIntoView reveals the chosen
        // one completely) and drop it onto the backlog column BEFORE the backlog card the finger
        // hovers: the reported index must be that card's FULL-LIST index, not its position inside
        // the visible window (0 — the value a window-relative report would send).
        var card = todoWindow.Locator(".tm-kanban__card").Nth(2);
        await card.EvaluateAsync("el => el.scrollIntoView({ block: 'nearest' })");
        await page.WaitForTimeoutAsync(150);
        var target = board.Locator("[data-testid='virtual-board-column-backlog']");
        var backlogWindow = target.Locator(".tm-kanban__cards");
        var anchor = backlogWindow.Locator(".tm-kanban__card").First;
        var anchorIndex = int.Parse((await anchor.GetAttributeAsync("data-tm-kanban-index"))!);

        var from = await CenterAsync(card);
        var anchorBox = await anchor.BoundingBoxAsync()
                        ?? throw new InvalidOperationException("the anchor card has no box");
        var session = await CdpAsync(page);

        await TouchStartAsync(session, from.X, from.Y);
        await Task.Delay(500);
        // Release above the anchor's midpoint: the drop lands immediately before it.
        await TouchMoveAsync(session, (int)(anchorBox.X + anchorBox.Width / 2), (int)(anchorBox.Y + 2));
        await TouchEndAsync(session);

        var change = page.Locator("section", new PageLocatorOptions { HasText = "Virtualized columns" })
            .Locator("p:has-text('Last change')");
        await change.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var text = await change.InnerTextAsync();
        StringAssert.Contains(text, "moved from todo to backlog");
        StringAssert.Contains(text, $"at index {anchorIndex}",
            "the drop anchors on the hovered backlog card's FULL-LIST index — a window-relative " +
            "report would have said 0");
    }

    [TestMethod]
    [Description("E2E-TG5-T8: edge auto-scroll — the board scrolls horizontally while a dragged card rests " +
                 "near its edge, and stays still when the finger moves away from it")]
    public async Task LongPressDrag_NearTheBoardEdge_AutoScrolls_AndStaysStillAwayFromIt()
    {
        var page = await OpenKanbanTouchAsync();
        var board = page.Locator(".tm-kanban:not(.tm-kanban--swimlanes)").First;
        var card = board.Locator("[data-testid='board-column-backlog'] .tm-kanban__card").First;
        var from = await CenterAsync(card);
        var session = await CdpAsync(page);

        await TouchStartAsync(session, from.X, from.Y);
        await Task.Delay(500);

        // Rest the finger 10 px from the board's right edge (inside the 36 px edge band).
        var boardBox = await board.BoundingBoxAsync()
                       ?? throw new InvalidOperationException("the board has no box");
        var nearEdgeX = (int)(boardBox.X + boardBox.Width - 10);
        var midY = (int)(boardBox.Y + Math.Min(boardBox.Height, 300) / 2);
        await TouchMoveAsync(session, nearEdgeX, midY);
        await Task.Delay(500);
        var scrolled = await board.EvaluateAsync<int>("el => el.scrollLeft");
        Assert.IsTrue(scrolled > 0, "the board auto-scrolls towards the hidden columns near its edge");

        // Away from the edge the scroll stops: park the finger in the middle of the board.
        await TouchMoveAsync(session, (int)(boardBox.X + boardBox.Width / 2), midY);
        await Task.Delay(400);
        var rest = await board.EvaluateAsync<int>("el => el.scrollLeft");
        Assert.AreEqual(scrolled, rest, "away from the edge the board stays still");

        await TouchEndAsync(session);
        await page.WaitForTimeoutAsync(200);
        Assert.AreEqual(rest, await board.EvaluateAsync<int>("el => el.scrollLeft"),
            "the scroll loop stops on release");
    }

    private static string ShotPath(string name)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            "TestResults", "kanban-collapsed-drop");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx"))) return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
