using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E coverage for the drop zones phase 3 of plan a98e4a71 added (D3/D4): a collapsed swimlane renders
/// one drop slot per column, the board root reports the gesture with <c>tm-kanban--dragging</c>, the slot
/// under the drag is highlighted, and a drop in a slot commits a real move.
/// <para>
/// The gesture is driven with synthetic DOM drag events dispatched from the page, because a screenshot
/// has to be taken WHILE the card is in flight — Playwright's <c>DragToAsync</c> releases the pointer
/// before a capture is possible. The handlers are the component's own <c>ondragstart</c> /
/// <c>ondragover</c> / <c>ondrop</c>, so the state the screenshots show is the state a real drag produces.
/// The captures land in <c>TM_E2E_SHOT_DIR</c> (default <c>/tmp/tm-kanban-shots</c>) for a hands-on
/// light/dark review.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class KanbanDropZonesE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Environment.GetEnvironmentVariable("TM_E2E_SHOT_DIR")
        ?? Path.Combine(Path.GetTempPath(), "tm-kanban-shots");

    private static string CardSelector(string lane, string column) =>
        $"[data-testid='swimlane-{lane}'] [data-testid='cell-{column}-{lane}'] .tm-kanban__card";

    private static string SlotSelector(string column, string lane) =>
        $"[data-testid='lane-slot-{column}-{lane}']";

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, name + ".png"),
            Type = ScreenshotType.Png,
            FullPage = true
        });
        await TakeScreenshotAsync(page, name);
    }

    private async Task<IPage> OpenKanbanAsync()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/kanban");
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-kanban__column-toggle").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        return page;
    }

    private static async Task SetDarkAsync(IPage page, bool dark)
    {
        // The demo shell carries its own data-theme (the ThemeService owns it), so drive the real toggle.
        var toggle = page.Locator("aside [data-testid='theme-toggle']");
        var isDark = string.Equals(
            await toggle.GetAttributeAsync("aria-label"), "Switch to light mode", StringComparison.Ordinal);
        if (isDark != dark)
        {
            await toggle.ClickAsync();
        }

        await page.WaitForTimeoutAsync(250);
    }

    /// <summary>
    /// Starts a drag on <paramref name="cardSelector"/> and hovers <paramref name="targetSelector"/>
    /// without releasing it, so the caller can screenshot the in-flight target state.
    /// </summary>
    private static Task StartDragOverAsync(IPage page, string cardSelector, string targetSelector)
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

    private static Task CancelDragAsync(IPage page, string cardSelector)
        => page.EvaluateAsync(
            """
            (cardSelector) => {
                const card = document.querySelector(cardSelector);
                card?.dispatchEvent(new DragEvent('dragend', {
                    bubbles: true, cancelable: true, dataTransfer: window.__tmKanbanDrag }));
            }
            """,
            cardSelector);

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
    [Description("E2E-FZE3.1: a collapsed lane renders one drop slot per column, highlights the slot under the drag in both themes and commits the drop")]
    public async Task CollapsedLane_RendersColumnSlots_AndScreenshotsTheTargetDuringDrag_InBothThemes()
    {
        var page = await OpenKanbanAsync();
        var board = page.Locator(".tm-kanban--swimlanes").First;
        var lane = board.Locator("[data-testid='swimlane-Alice']");

        await lane.Locator(".tm-kanban__swimlane-toggle").ClickAsync();
        await page.WaitForTimeoutAsync(200);

        StringAssert.Contains(await lane.GetAttributeAsync("class"), "tm-kanban__swimlane--collapsed",
            "the lane collapses");
        Assert.AreEqual(0, await lane.Locator(".tm-kanban__swimlane-body").CountAsync(),
            "a collapsed lane drops its body");
        Assert.AreEqual(1, await lane.Locator(".tm-kanban__swimlane-slots").CountAsync(),
            "a collapsed lane renders one slots strip");
        Assert.AreEqual(5, await lane.Locator("[data-testid^='lane-slot-']").CountAsync(),
            "the strip holds one slot per column, so the grid stays aligned");

        var target = SlotSelector("todo", "Alice");
        await ShotAsync(page, "kanban-fze3-lane-slots-idle-light");

        // ── In flight, light ──
        await StartDragOverAsync(page, CardSelector("Bob", "doing"), target);
        await page.Locator(".tm-kanban--swimlanes.tm-kanban--dragging").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await page.Locator($"{target}.tm-kanban__lane-slot--drop-target").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await page.WaitForTimeoutAsync(150);
        await ShotAsync(page, "kanban-fze3-lane-slots-drag-light");

        await CancelDragAsync(page, CardSelector("Bob", "doing"));
        await page.Locator(".tm-kanban--swimlanes.tm-kanban--dragging").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
        Assert.AreEqual(0, await page.Locator($"{target}.tm-kanban__lane-slot--drop-target").CountAsync(),
            "dragend clears the target highlight");

        // ── In flight, dark ──
        await SetDarkAsync(page, true);
        await StartDragOverAsync(page, CardSelector("Bob", "doing"), target);
        await page.Locator($"{target}.tm-kanban__lane-slot--drop-target").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await page.WaitForTimeoutAsync(150);
        await ShotAsync(page, "kanban-fze3-lane-slots-drag-dark");

        // ── The drop commits a real move ──
        await DropAsync(page, target);
        var change = page.Locator("p:has-text('Last change')").Last;
        await change.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var text = await change.InnerTextAsync();
        StringAssert.Contains(text, "column todo", "the slot dropped into the To Do column");
        StringAssert.Contains(text, "lane 'Alice'", "the slot dropped into the Alice lane");
        StringAssert.Contains(text, "ToSwimlane=\"Alice\"", "the move event carries the lane id");
        StringAssert.Contains(text, "TargetIndex=2", "(todo, Alice) held two cards, so the drop appends");

        // Phase 5 (fze-5): BOTH halves of the contract the slot drop produces — ToColumn and ToSwimlane —
        // are reported on the one "Last change" line, in the order the demo formats them.
        StringAssert.Contains(text, "→ column todo, lane 'Alice' (ToSwimlane=\"Alice\", TargetIndex=2)",
            "the slot drop reports ToColumn AND ToSwimlane in one event");

        await page.WaitForTimeoutAsync(200);
        Assert.AreEqual(0, await page.Locator(".tm-kanban--swimlanes.tm-kanban--dragging").CountAsync(),
            "the gesture class is gone once the drop committed");
        Assert.AreEqual("4", (await lane.Locator(".tm-kanban__swimlane-count").InnerTextAsync()).Trim(),
            "the collapsed lane's count includes the moved card");
        Assert.AreEqual(0, await lane.Locator(".tm-kanban__swimlane-body").CountAsync(),
            "the lane stays collapsed after the drop");

        await SetDarkAsync(page, false);
    }

    [TestMethod]
    [Description("E2E-FZE3.2: the classic collapsed strip stays a whole drop target — the card is appended to the strip's column")]
    public async Task ClassicCollapsedColumn_StaysADropTarget_AndAppendsTheCard()
    {
        var page = await OpenKanbanAsync();
        var board = page.Locator(".tm-kanban:not(.tm-kanban--swimlanes)").First;
        var strip = board.Locator("[data-testid='board-column-todo']");

        await strip.Locator("[data-testid='column-toggle-todo']").ClickAsync();
        await page.WaitForTimeoutAsync(200);

        StringAssert.Contains(await strip.GetAttributeAsync("class"), "tm-kanban__column--collapsed");

        // "Fix navigation bug" lives in In Progress (doing) and is dragged onto the collapsed To Do strip.
        await StartDragOverAsync(
            page,
            "[data-testid='board-column-doing'] .tm-kanban__card",
            "[data-testid='board-column-todo']");
        await page.Locator("[data-testid='board-column-todo'].tm-kanban__column--drop-target")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await ShotAsync(page, "kanban-fze3-classic-strip-drop-target-light");

        await DropAsync(page, "[data-testid='board-column-todo']");

        var change = page.Locator("p:has-text('Last change')").First;
        await change.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        StringAssert.Contains(await change.InnerTextAsync(), "moved from doing to todo",
            "the whole collapsed strip catches the card");

        // To Do held two cards ("Write API docs", "Implement auth module") → the drop appends at index 2.
        Assert.AreEqual("3", (await strip.Locator(".tm-kanban__count").InnerTextAsync()).Trim(),
            "the collapsed strip's count reflects the appended card");

        // Phase 5 (fze-5, subtask 1): the appended card lands at the END of the column — expanding the
        // strip must show it last, both on the board and in the demo's "Persisted order" list.
        await strip.Locator("[data-testid='column-toggle-todo']").ClickAsync();
        await page.WaitForTimeoutAsync(250);
        Assert.IsFalse((await strip.GetAttributeAsync("class") ?? string.Empty)
                .Contains("tm-kanban__column--collapsed", StringComparison.Ordinal),
            "the strip expands again with a click on its toggle");

        var expanded = await strip.Locator(".tm-kanban__cards .tm-kanban__card").AllInnerTextsAsync();
        Assert.AreEqual(3, expanded.Count, "the expanded To Do column renders the appended card");
        StringAssert.Contains(expanded[^1], "Code review PR #42",
            "a drop on the collapsed strip APPENDS the card to the end of the column");

        var persistedTodo = page.Locator("section", new PageLocatorOptions { HasText = "Persisted order" })
            .Locator("div.rounded-lg")
            .Filter(new LocatorFilterOptions { HasText = "To Do" })
            .Locator("ol li");
        var persisted = await persistedTodo.AllInnerTextsAsync();
        Assert.AreEqual(3, persisted.Count, "the persisted To Do list holds the appended card");
        StringAssert.Contains(persisted[^1], "Code review PR #42",
            "the persisted order keeps the same appended position");
    }
}
