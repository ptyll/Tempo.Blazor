using System.Collections.Generic;
using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 1 — E2E coverage for TmKanbanBoard in-column reordering, drop index reporting and the
/// visual drop indicator. Drag &amp; drop is HTML5-native, so we drive it with the canonical Playwright
/// recipe: dispatch real <c>dragstart</c>/<c>dragenter</c>/<c>dragover</c>/<c>drop</c>/<c>dragend</c> DOM
/// events sharing a single in-page <c>DataTransfer</c>, so Blazor's handlers run exactly as they would
/// for a user's pointer.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class KanbanReorderE2ETests : WasmTestBase
{
    // MSTest deletes its deployment directory after a successful run, so also persist screenshots
    // to a stable folder for out-of-band UX review.
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

    private async Task<(IPage page, ILocator board)> OpenKanbanAsync()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/kanban");
        await WaitForAppReadyAsync(page);
        var board = page.Locator(".tm-kanban").First;
        await board.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.Locator(".tm-kanban__card").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return (page, board);
    }

    // The /kanban demo renders THREE TmKanbanBoard instances (reorderable, persisted-order list is
    // plain HTML, swimlanes) and every board emits data-testid='board-column-{id}' for the same
    // column ids — page-wide locators trip strict mode. Always scope to the first board.
    private static ILocator ColumnCards(ILocator board, string columnId)
        => board.Locator($"[data-testid='board-column-{columnId}'] .tm-kanban__card");

    private static ILocator Column(ILocator board, string columnId)
        => board.Locator($"[data-testid='board-column-{columnId}']");

    private static async Task<IJSHandle> NewDataTransferAsync(IPage page)
        => await page.EvaluateHandleAsync("() => new DataTransfer()");

    private static Dictionary<string, object> Init(IJSHandle dataTransfer)
        => new() { ["dataTransfer"] = dataTransfer, ["bubbles"] = true, ["cancelable"] = true };

    /// <summary>Begins a drag on <paramref name="source"/> and hovers <paramref name="over"/> (without dropping).</summary>
    private static async Task<IJSHandle> BeginDragOverAsync(IPage page, ILocator source, ILocator over)
    {
        var dt = await NewDataTransferAsync(page);
        await source.DispatchEventAsync("dragstart", Init(dt));
        await over.DispatchEventAsync("dragenter", Init(dt));
        await over.DispatchEventAsync("dragover", Init(dt));
        return dt;
    }

    private static async Task DropAsync(ILocator source, ILocator dropTarget, IJSHandle dt)
    {
        await dropTarget.DispatchEventAsync("drop", Init(dt));
        await source.DispatchEventAsync("dragend", Init(dt));
    }

    // ── Board renders ────────────────────────────────────────────────────────

    [TestMethod]
    [Description("E2E-P1.0: Kanban demo renders columns, cards and the persisted-order section")]
    public async Task Kanban_Board_Renders_With_Columns_And_PersistedOrder()
    {
        var (page, board) = await OpenKanbanAsync();

        Assert.IsTrue(await board.Locator(".tm-kanban__column").CountAsync() >= 3, "Expected multiple columns");
        Assert.IsTrue(await page.Locator(".tm-kanban__card").CountAsync() > 0, "Expected cards to render");
        // The Phase-1 demo adds a "Persisted order" section — proves the fresh page is being served.
        Assert.IsTrue(await page.Locator("text=Persisted order").CountAsync() > 0, "Expected the persisted-order section");

        await ShotAsync(page, "kanban_p1_board_light");
    }

    // ── In-column reorder persists order ─────────────────────────────────────

    [TestMethod]
    [Description("E2E-P1.1: Reordering a card within a column reports a reorder and persists the new order")]
    public async Task Kanban_Reorder_WithinColumn_Persists_Order()
    {
        var (page, board) = await OpenKanbanAsync();

        // "backlog" column starts as [Design login page, Setup CI/CD pipeline]
        var backlog = ColumnCards(board, "backlog");
        var firstBefore = (await backlog.Nth(0).InnerTextAsync()).Trim();

        await ShotAsync(page, "kanban_p1_reorder_before");

        // Drag card 0 downward past card 1 → drops after it (end of column)
        var dt = await BeginDragOverAsync(page, backlog.Nth(0), backlog.Nth(1));
        await page.WaitForTimeoutAsync(120);
        await DropAsync(backlog.Nth(0), Column(board, "backlog"), dt);
        await page.WaitForTimeoutAsync(250);

        var lastChange = page.Locator("p:has-text('Last change')");
        await lastChange.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var text = await lastChange.InnerTextAsync();
        StringAssert.Contains(text, "reordered within backlog", $"Unexpected last-change text: {text}");

        var firstAfter = (await ColumnCards(board, "backlog").Nth(0).InnerTextAsync()).Trim();
        Assert.AreNotEqual(firstBefore, firstAfter, "Card order within the column should have changed");

        await ShotAsync(page, "kanban_p1_reorder_after");
    }

    // ── Drop indicator visible during drag ───────────────────────────────────

    [TestMethod]
    [Description("E2E-P1.2: A visual drop indicator appears between cards while dragging")]
    public async Task Kanban_DropIndicator_Visible_During_Drag()
    {
        var (page, board) = await OpenKanbanAsync();

        var todo = ColumnCards(board, "todo");
        var dt = await BeginDragOverAsync(page, todo.Nth(0), todo.Nth(1));
        await page.WaitForTimeoutAsync(150);

        Assert.IsTrue(await page.Locator(".tm-kanban__drop-indicator").CountAsync() > 0,
            "A drop indicator should be shown while dragging over a card");

        await ShotAsync(page, "kanban_p1_drop_indicator");

        await DropAsync(todo.Nth(0), Column(board, "todo"), dt);
        await page.WaitForTimeoutAsync(200);
        Assert.AreEqual(0, await page.Locator(".tm-kanban__drop-indicator").CountAsync(), "Indicator should clear after drop");
    }

    // ── Cross-column move reports target index ───────────────────────────────

    [TestMethod]
    [Description("E2E-P1.3: Moving a card to another column reports the move with a target index")]
    public async Task Kanban_CrossColumn_Move_Reports_TargetIndex()
    {
        var (page, board) = await OpenKanbanAsync();

        var todoCountBefore = await ColumnCards(board, "todo").CountAsync();

        // Drag a backlog card onto the todo column background → appended to end of todo
        var dt = await BeginDragOverAsync(page, ColumnCards(board, "backlog").Nth(0), Column(board, "todo"));
        await page.WaitForTimeoutAsync(120);
        await DropAsync(ColumnCards(board, "backlog").Nth(0), Column(board, "todo"), dt);
        await page.WaitForTimeoutAsync(250);

        var lastChange = page.Locator("p:has-text('Last change')");
        await lastChange.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var text = await lastChange.InnerTextAsync();
        StringAssert.Contains(text, "moved from backlog to todo", $"Unexpected last-change text: {text}");
        StringAssert.Contains(text, "index", $"Move should report a target index: {text}");

        var todoCountAfter = await ColumnCards(board, "todo").CountAsync();
        Assert.AreEqual(todoCountBefore + 1, todoCountAfter, "The todo column should have gained the moved card");

        await ShotAsync(page, "kanban_p1_crosscolumn_move");
    }

    // ── Edge case: reorder to the very top of a column ───────────────────────

    [TestMethod]
    [Description("E2E-P1.4 (edge): Dragging a lower card upward onto the first card inserts it at the top")]
    public async Task Kanban_Reorder_ToTop_EdgeCase()
    {
        var (page, board) = await OpenKanbanAsync();

        var backlog = ColumnCards(board, "backlog");
        var secondBefore = (await backlog.Nth(1).InnerTextAsync()).Trim();

        // Drag card 1 upward onto card 0 → inserts BEFORE card 0 (top of column)
        var dt = await BeginDragOverAsync(page, backlog.Nth(1), backlog.Nth(0));
        await page.WaitForTimeoutAsync(120);
        await DropAsync(backlog.Nth(1), Column(board, "backlog"), dt);
        await page.WaitForTimeoutAsync(250);

        var firstAfter = (await ColumnCards(board, "backlog").Nth(0).InnerTextAsync()).Trim();
        Assert.AreEqual(secondBefore, firstAfter, "The dragged (previously second) card should now be first");

        await ShotAsync(page, "kanban_p1_reorder_to_top");
    }

    // ── Dark mode rendering ──────────────────────────────────────────────────

    [TestMethod]
    [Description("E2E-P1.5: Drop indicator and board render correctly in dark mode")]
    public async Task Kanban_DarkMode_Renders_DropIndicator()
    {
        var (page, board) = await OpenKanbanAsync();

        // The component's dark styling keys off a [data-theme="dark"] ancestor.
        await page.EvaluateAsync("() => document.documentElement.setAttribute('data-theme','dark')");
        await page.WaitForTimeoutAsync(200);

        var todo = ColumnCards(board, "todo");
        await BeginDragOverAsync(page, todo.Nth(0), todo.Nth(1));
        await page.WaitForTimeoutAsync(150);

        Assert.IsTrue(await page.Locator(".tm-kanban__drop-indicator").CountAsync() > 0,
            "Indicator should also show in dark mode");

        await ShotAsync(page, "kanban_p1_dark_mode");
    }
}
