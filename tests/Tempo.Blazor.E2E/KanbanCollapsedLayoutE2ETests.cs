using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E coverage for the collapsed COLUMN inside the swimlane layout (plan a98e4a71, phase 5 — subtask 3):
/// the sticky header cell, every lane cell of the same column and every slot of a collapsed lane share ONE
/// column grid — the same left edge and the same width — before and after a column collapses, and the
/// narrowed cell still catches a (column, lane) drop.
/// <para>
/// The assertions read real <see cref="BoundingBox"/>es from the browser rather than comparing a class list:
/// the phase-2 promise ("the header cell and every lane cell of that column narrow together, so the grid stays
/// aligned") is a GEOMETRIC claim that only a rendered box can confirm or refute. The drag is driven with the
/// same dispatched-<c>DragEvent</c> + shared <c>DataTransfer</c> recipe the sibling suites use.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class KanbanCollapsedLayoutE2ETests : WasmTestBase
{
    private static readonly string[] Columns = ["backlog", "todo", "doing", "review", "done"];

    // "none" is the lane test id of the trailing "No value" lane (the demo's unassigned card).
    private static readonly string[] Lanes = ["Alice", "Bob", "none"];

    private async Task<IPage> OpenSwimlaneBoardAsync()
    {
        var page = await CreatePageAsync();
        // Wide enough that all five 280px columns fit side by side, so the boxes of the last columns are
        // measured on the same row instead of off the scrollport.
        await page.SetViewportSizeAsync(1920, 1200);
        await page.GotoAsync($"{BaseUrl}/kanban");
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='cell-backlog-Alice']").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        return page;
    }

    /// <summary>The measured box as plain doubles, so the assertions compare numbers and not a Playwright DTO.</summary>
    private readonly record struct Box(double X, double Y, double Width, double Height);

    private static async Task<Box> BoxAsync(ILocator locator)
    {
        Assert.IsTrue(await locator.CountAsync() > 0, "the measured element is rendered");
        var box = await locator.First.BoundingBoxAsync();
        Assert.IsNotNull(box, "the measured element has a layout box");
        return new Box(box!.X, box.Y, box.Width, box.Height);
    }

    /// <summary>
    /// Every column's sticky header cell, every lane cell and every collapsed-lane slot must sit on the same
    /// left edge and carry the same width — otherwise the shared grid the swimlane layout promises is off.
    /// </summary>
    private static async Task AssertTheColumnGridIsAlignedAsync(IPage page, string context)
    {
        await WaitForLayoutToSettleAsync(
            page, "[data-testid^='colhead-'], [data-testid^='cell-'], [data-testid^='lane-slot-']");

        foreach (var column in Columns)
        {
            var header = await BoxAsync(page.Locator($"[data-testid='colhead-{column}']"));
            foreach (var lane in Lanes)
            {
                // A collapsed lane renders a slot INSTEAD of its cell; both are the drop zone of the
                // (column, lane) pair and both have to stay on the grid.
                var zone = page.Locator($"[data-testid='cell-{column}-{lane}'], [data-testid='lane-slot-{column}-{lane}']");
                if (await zone.CountAsync() == 0)
                {
                    continue;
                }

                var box = await BoxAsync(zone);
                Assert.AreEqual(header.X, box.X, 0.5,
                    $"{context}: the {column}/{lane} drop zone shares the header cell's left edge");
                Assert.AreEqual(header.Width, box.Width, 0.5,
                    $"{context}: the {column}/{lane} drop zone shares the header cell's width");
            }
        }
    }

    /// <summary>Starts a drag on the first card of <paramref name="cardSelector"/> and hovers the target.</summary>
    private static Task StartDragAsync(IPage page, string cardSelector, string targetSelector)
        => page.EvaluateAsync(
            """
            ([cardSelector, targetSelector]) => {
                const card = document.querySelector(cardSelector);
                const target = document.querySelector(targetSelector);
                if (!card) throw new Error('no card for ' + cardSelector);
                if (!target) throw new Error('no target for ' + targetSelector);
                const dataTransfer = new DataTransfer();
                window.__tmKanbanDrag = dataTransfer;
                card.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer }));
                target.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer }));
            }
            """,
            new[] { cardSelector, targetSelector });

    private static Task DropAsync(IPage page, string targetSelector)
        => page.EvaluateAsync(
            """
            (targetSelector) => {
                const target = document.querySelector(targetSelector);
                if (!target) throw new Error('no target for ' + targetSelector);
                target.dispatchEvent(new DragEvent('drop', {
                    bubbles: true, cancelable: true, dataTransfer: window.__tmKanbanDrag }));
            }
            """,
            targetSelector);

    [TestMethod]
    [Description("E2E-FZE5.3: a collapsed column in the swimlane layout keeps the header cell, every lane cell and every collapsed-lane slot on one grid, and the narrowed cell still catches a (column, lane) drop")]
    public async Task CollapsedColumn_InTheSwimlaneLayout_KeepsTheGrid_AndTheNarrowedCellCatchesADrop()
    {
        var page = await OpenSwimlaneBoardAsync();
        var board = page.Locator(".tm-kanban--swimlanes").First;

        // ── 1. Expanded: the sticky header row and every lane already share one grid ──
        await AssertTheColumnGridIsAlignedAsync(page, "expanded");

        // ── 2. Collapse To Do: the header cell and EVERY lane cell narrow together ──
        await board.Locator("[data-testid='colhead-todo'] [data-testid='column-toggle-todo']").ClickAsync();
        await page.WaitForTimeoutAsync(250);

        var header = page.Locator("[data-testid='colhead-todo']");
        StringAssert.Contains(await header.GetAttributeAsync("class"), "tm-kanban__column--collapsed",
            "the swimlane header cell collapses");
        var collapsed = await BoxAsync(header);
        Assert.IsTrue(collapsed.Width <= 60,
            $"the collapsed header cell narrows to the strip width (was {collapsed.Width}px)");
        Assert.IsTrue(collapsed.Height >= 120,
            $"the sticky header row stretches to the vertical strip (was {collapsed.Height}px)");

        await AssertTheColumnGridIsAlignedAsync(page, "collapsed column");

        foreach (var lane in Lanes)
        {
            var cell = await BoxAsync(page.Locator($"[data-testid='cell-todo-{lane}']"));
            Assert.AreEqual(collapsed.Width, cell.Width, 0.5,
                $"the {lane} lane cell of the collapsed column is the same narrow width as its header");
            Assert.AreEqual(0, await page.Locator($"[data-testid='cell-todo-{lane}'] .tm-kanban__card").CountAsync(),
                $"a collapsed column renders no cards in the {lane} lane");
        }

        // ── 3. The narrowed cell still catches a drop ──
        await StartDragAsync(
            page,
            "[data-testid='swimlane-Bob'] [data-testid='cell-doing-Bob'] .tm-kanban__card",
            "[data-testid='cell-todo-Alice']");
        await page.Locator("[data-testid='cell-todo-Alice'].tm-kanban__column--drop-target").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await DropAsync(page, "[data-testid='cell-todo-Alice']");

        var change = page.Locator("p:has-text('Last change')").Last;
        await change.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var text = await change.InnerTextAsync();
        StringAssert.Contains(text, "'Setup CI/CD pipeline' → column todo, lane 'Alice'",
            "the narrowed cell reports the ToColumn and the ToSwimlane of the drop");
        StringAssert.Contains(text, "ToSwimlane=\"Alice\"", "the move event carries the lane id");
        StringAssert.Contains(text, "TargetIndex=2", "(todo, Alice) held two cards, so the drop appends");

        await page.WaitForTimeoutAsync(250);
        Assert.AreEqual("4", (await board.Locator("[data-testid='colhead-todo'] .tm-kanban__count").InnerTextAsync()).Trim(),
            "the collapsed column's board-level count includes the moved card");
        Assert.AreEqual("4", (await board.Locator("[data-testid='swimlane-Alice'] .tm-kanban__swimlane-count").InnerTextAsync()).Trim(),
            "the Alice lane's count includes the moved card");
        Assert.AreEqual(0, await page.Locator("[data-testid='cell-todo-Alice'] .tm-kanban__card").CountAsync(),
            "the cell stays collapsed after the drop");

        // ── 4. Expanding shows the appended card at the END of the narrowed cell's column/lane ──
        await board.Locator("[data-testid='colhead-todo'] [data-testid='column-toggle-todo']").ClickAsync();
        await page.WaitForTimeoutAsync(250);
        var cards = await page.Locator("[data-testid='cell-todo-Alice'] .tm-kanban__card").AllInnerTextsAsync();
        Assert.AreEqual(3, cards.Count, "the expanded cell renders the appended card");
        StringAssert.Contains(cards[^1], "Setup CI/CD pipeline",
            "a drop on the narrowed cell appends to the end of the (column, lane) cell");

        // ── 5. A collapsed LANE keeps its slot on the same grid, and the collapsed column narrows it ──
        await board.Locator("[data-testid='swimlane-Bob'] .tm-kanban__swimlane-toggle").ClickAsync();
        await page.WaitForTimeoutAsync(250);
        StringAssert.Contains(await board.Locator("[data-testid='swimlane-Bob']").GetAttributeAsync("class"),
            "tm-kanban__swimlane--collapsed");
        await AssertTheColumnGridIsAlignedAsync(page, "collapsed lane");

        // Step 4 expanded the column again, so collapse it a second time: the slot under it has to be the
        // narrow strip, and it has to stay on the header's edge (AssertTheColumnGridIsAligned above read
        // the EXPANDED column, which is the state the expansion left behind).
        await board.Locator("[data-testid='colhead-todo'] [data-testid='column-toggle-todo']").ClickAsync();
        await page.WaitForTimeoutAsync(250);
        await AssertTheColumnGridIsAlignedAsync(page, "collapsed column + collapsed lane");

        var liveHeader = await BoxAsync(page.Locator("[data-testid='colhead-todo']"));
        var slot = await BoxAsync(page.Locator("[data-testid='lane-slot-todo-Bob']"));
        Assert.IsTrue(slot.Width <= 60,
            $"the collapsed lane's slot under the collapsed column is the narrow strip (was {slot.Width}px)");
        Assert.AreEqual(liveHeader.Width, slot.Width, 0.5,
            "the slot under the collapsed column is exactly as wide as the collapsed header");
        Assert.AreEqual(liveHeader.X, slot.X, 0.5,
            "the slot under the collapsed column shares the collapsed header's left edge");
    }
}
