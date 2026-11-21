using System.Collections.Generic;
using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 2 — E2E coverage for TmKanbanBoard swimlanes (grouping, per-lane counts, board-level column
/// counts, collapse, and cross-lane drag reassignment). HTML5 drag-and-drop is driven with the same
/// dispatched-DragEvent + shared DataTransfer recipe used by <see cref="KanbanReorderE2ETests"/>.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class KanbanSwimlaneE2ETests : WasmTestBase
{
    private static readonly string StableShotDir = Path.Combine(
        Environment.GetEnvironmentVariable("TM_E2E_SHOT_DIR") ?? Path.GetTempPath(), "kanban-e2e-shots");

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(StableShotDir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(StableShotDir, name + ".png"),
            Type = ScreenshotType.Png,
            FullPage = true
        });
        await TakeScreenshotAsync(page, name);
    }

    private async Task<(IPage page, ILocator board)> OpenSwimlaneBoardAsync()
    {
        var page = await CreatePageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/kanban");
        var board = page.Locator(".tm-kanban--swimlanes").First;
        await board.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.Locator(".tm-kanban__swimlane").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        // scroll the swimlane board into view for full-page screenshots
        await board.ScrollIntoViewIfNeededAsync();
        return (page, board);
    }

    private static ILocator Cell(IPage page, string columnId, string laneTestId)
        => page.Locator($"[data-testid='cell-{columnId}-{laneTestId}']");

    private static async Task<IJSHandle> NewDataTransferAsync(IPage page)
        => await page.EvaluateHandleAsync("() => new DataTransfer()");

    private static Dictionary<string, object> Init(IJSHandle dt)
        => new() { ["dataTransfer"] = dt, ["bubbles"] = true, ["cancelable"] = true };

    private static async Task DragCardToCellAsync(IPage page, ILocator sourceCard, ILocator targetCell)
    {
        var dt = await NewDataTransferAsync(page);
        await sourceCard.DispatchEventAsync("dragstart", Init(dt));
        await targetCell.DispatchEventAsync("dragenter", Init(dt));
        await targetCell.DispatchEventAsync("dragover", Init(dt));
        await targetCell.DispatchEventAsync("drop", Init(dt));
        await sourceCard.DispatchEventAsync("dragend", Init(dt));
    }

    // ── Renders lanes + board-level headers ──────────────────────────────────

    [TestMethod]
    [Description("E2E-P2.0: Swimlane board renders a shared column-header row and one lane per assignee (+ no-value lane)")]
    public async Task Swimlanes_Render_Lanes_And_SharedHeaders()
    {
        var (page, board) = await OpenSwimlaneBoardAsync();

        Assert.IsTrue(await board.Locator(".tm-kanban__column-headers").CountAsync() == 1, "Expected one shared column-header row");
        // Alice, Bob + the trailing "no value" lane for the unassigned card
        Assert.IsTrue(await board.Locator(".tm-kanban__swimlane").CountAsync() >= 3, "Expected at least 3 swimlanes");
        Assert.IsTrue(await board.Locator("[data-testid='swimlane-none']").CountAsync() == 1, "Expected a no-value lane");

        await ShotAsync(page, "kanban_p2_swimlanes_light");
    }

    // ── No-value lane holds the unassigned card ──────────────────────────────

    [TestMethod]
    [Description("E2E-P2.1: The unassigned card lands in the trailing 'No value' lane")]
    public async Task Swimlanes_NoValueLane_Holds_Unassigned_Card()
    {
        var (page, board) = await OpenSwimlaneBoardAsync();

        var noneLane = board.Locator("[data-testid='swimlane-none']");
        var title = await noneLane.Locator(".tm-kanban__swimlane-title").InnerTextAsync();
        StringAssert.Contains(title, "No value", $"Unexpected no-value lane title: {title}");
        StringAssert.Contains(await Cell(page, "todo", "none").InnerTextAsync(), "Triage inbox");

        await ShotAsync(page, "kanban_p2_no_value_lane");
    }

    // ── Collapse hides the lane body ─────────────────────────────────────────

    [TestMethod]
    [Description("E2E-P2.2: Clicking the swimlane toggle collapses the lane body")]
    public async Task Swimlanes_Collapse_HidesBody()
    {
        var (page, board) = await OpenSwimlaneBoardAsync();

        var alice = board.Locator("[data-testid='swimlane-Alice']");
        Assert.AreEqual(1, await alice.Locator(".tm-kanban__swimlane-body").CountAsync(), "Alice lane body should be visible");

        await alice.Locator(".tm-kanban__swimlane-toggle").ClickAsync();
        await page.WaitForTimeoutAsync(250);

        Assert.AreEqual(0, await alice.Locator(".tm-kanban__swimlane-body").CountAsync(), "Alice lane body should be collapsed");

        await ShotAsync(page, "kanban_p2_lane_collapsed");
    }

    // ── Cross-lane drag reassigns ────────────────────────────────────────────

    [TestMethod]
    [Description("E2E-P2.3: Dragging a card into another lane's cell reassigns it (ToSwimlane)")]
    public async Task Swimlanes_DragAcrossLanes_Reassigns()
    {
        var (page, _) = await OpenSwimlaneBoardAsync();

        // Drag an Alice/todo card into Bob's (currently empty) todo cell
        var aliceCard = Cell(page, "todo", "Alice").Locator(".tm-kanban__card").First;
        await DragCardToCellAsync(page, aliceCard, Cell(page, "todo", "Bob"));
        await page.WaitForTimeoutAsync(300);

        var lastChange = page.Locator("p:has-text('Last change')").Last;
        await lastChange.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var text = await lastChange.InnerTextAsync();
        StringAssert.Contains(text, "lane 'Bob'", $"Unexpected swimlane change text: {text}");

        await ShotAsync(page, "kanban_p2_cross_lane_reassign");
    }

    // ── Edge case: reorder within a lane ─────────────────────────────────────

    [TestMethod]
    [Description("E2E-P2.4 (edge): Reordering two cards inside one lane fires an in-lane reorder")]
    public async Task Swimlanes_ReorderWithinLane()
    {
        var (page, _) = await OpenSwimlaneBoardAsync();

        // Alice/todo holds two cards (Design login page, Write API docs)
        var cards = Cell(page, "todo", "Alice").Locator(".tm-kanban__card");
        Assert.IsTrue(await cards.CountAsync() >= 2, "Alice/todo needs two cards for a reorder");

        await DragCardToCellAsync(page, cards.Nth(0), Cell(page, "todo", "Alice"));
        await page.WaitForTimeoutAsync(300);

        var lastChange = page.Locator("p:has-text('Last change')").Last;
        var text = await lastChange.InnerTextAsync();
        StringAssert.Contains(text, "reordered within todo/Alice", $"Unexpected reorder text: {text}");

        await ShotAsync(page, "kanban_p2_reorder_within_lane");
    }

    // ── Dark mode ────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("E2E-P2.5: Swimlane board renders correctly in dark mode")]
    public async Task Swimlanes_DarkMode()
    {
        var (page, _) = await OpenSwimlaneBoardAsync();

        await page.EvaluateAsync("() => document.documentElement.setAttribute('data-theme','dark')");
        await page.WaitForTimeoutAsync(250);

        await ShotAsync(page, "kanban_p2_swimlanes_dark");
    }
}
