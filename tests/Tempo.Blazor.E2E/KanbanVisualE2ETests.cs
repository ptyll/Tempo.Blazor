using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan a98e4a71 phase 5 — the committed visual baselines of the collapsed-column / collapsed-lane surface of
/// <c>TmKanbanBoard</c>. Every state lands in <c>__screenshots__/kanban/</c> in BOTH themes, so the review
/// judges the artefact next to the code it judges: the expanded board, a collapsed column, a collapsed lane,
/// a collapsed column inside the swimlane layout, the drop-target highlight over a collapsed destination, the
/// WIP over-limit state a drop onto a collapsed limited column produces, and the keyboard focus ring on the
/// collapse toggle and on a card.
/// <para>
/// The theme is driven through the demo's own toggle — the demo shell owns <c>data-theme</c>, so setting the
/// attribute on <c>&lt;html&gt;</c> is overridden by the inner light island. The captures are ELEMENT
/// screenshots of the board (or of the focused header) at a 1920px viewport, where all five 280px columns fit
/// side by side: a full-page shot at the default 1280px would clip the last columns and hide the very state
/// the phase is about.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class KanbanVisualE2ETests : WasmTestBase
{
    private static readonly string ShotDir = Path.Combine(
        TestContextHelper.RepositoryRoot, "tests", "Tempo.Blazor.E2E", "__screenshots__", "kanban");

    private async Task ShotAsync(IPage page, ILocator target, string name)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, name + ".png");
        await target.First.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            Timeout = 60_000
        });

        var info = new FileInfo(path);
        Assert.IsTrue(info.Length > 2 * 1024, $"screenshot {name}.png must be a real capture, got {info.Length} bytes");
        await AssertNoBlazorErrorUiAsync(page);
    }

    /// <summary>
    /// A visible <c>#blazor-error-ui</c> means an unhandled Blazor exception — a capture must never be taken
    /// over such a page (the guard <c>SmokeLaneE2ETests</c> asserts).
    /// </summary>
    private static async Task AssertNoBlazorErrorUiAsync(IPage page)
    {
        var visible = await page.EvaluateAsync<bool>(
            """
            () => {
                const el = document.querySelector('#blazor-error-ui');
                return !!el && getComputedStyle(el).display !== 'none';
            }
            """);
        Assert.IsFalse(visible, "#blazor-error-ui is visible — an unhandled Blazor exception occurred.");
    }

    private async Task<IPage> OpenBoardAsync()
    {
        var page = await CreatePageAsync();
        await page.SetViewportSizeAsync(1920, 1200);
        await page.GotoAsync($"{BaseUrl}/kanban");
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-kanban__column-toggle").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        return page;
    }

    private static ILocator ClassicBoard(IPage page) => page.Locator(".tm-kanban:not(.tm-kanban--swimlanes)").First;

    private static ILocator SwimlaneBoard(IPage page) => page.Locator(".tm-kanban--swimlanes").First;

    private static async Task SetDarkAsync(IPage page, bool dark)
    {
        var toggle = page.Locator("[data-testid='theme-toggle']:visible").First;
        var isDark = string.Equals(
            await toggle.GetAttributeAsync("aria-label"), "Switch to light mode", StringComparison.Ordinal);
        if (isDark != dark)
        {
            await toggle.ClickAsync();
        }

        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>
    /// Presses Tab (a REAL key press — that is what makes <c>:focus-visible</c> match) until
    /// <paramref name="predicate"/> holds for <c>document.activeElement</c>. The tab order is not asserted
    /// here: the sibling suites own the roving-tabindex contract, this only parks the keyboard focus.
    /// </summary>
    private static async Task TabUntilAsync(IPage page, string predicate, string description, int maxPresses = 200)
    {
        await page.EvaluateAsync(
            "() => { if (document.activeElement instanceof HTMLElement) document.activeElement.blur(); }");
        for (var press = 0; press < maxPresses; press++)
        {
            if (await page.EvaluateAsync<bool>(predicate))
            {
                return;
            }

            await page.Keyboard.PressAsync("Tab");
        }

        Assert.Fail($"Tab never reached {description}.");
    }

    /// <summary>Starts a drag on a card and hovers the target WITHOUT releasing it.</summary>
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
    [Description("E2E-FZE5.1: the expanded classic board is captured in both themes as the reference state")]
    public async Task ExpandedBoard_BothThemes()
    {
        var page = await OpenBoardAsync();
        var board = ClassicBoard(page);

        await ShotAsync(page, board, "kanban-board-1920-light");
        await SetDarkAsync(page, true);
        await ShotAsync(page, board, "kanban-board-1920-dark");
        await SetDarkAsync(page, false);
    }

    [TestMethod]
    [Description("E2E-FZE5.2: a collapsed column (vertical title, count, WIP badge, still a drop zone) is captured in both themes")]
    public async Task CollapsedColumn_BothThemes()
    {
        var page = await OpenBoardAsync();
        var board = ClassicBoard(page);

        await board.Locator("[data-testid='column-toggle-todo']").ClickAsync();
        await page.WaitForTimeoutAsync(250);
        StringAssert.Contains(
            await board.Locator("[data-testid='board-column-todo']").GetAttributeAsync("class"),
            "tm-kanban__column--collapsed");

        await ShotAsync(page, board, "kanban-column-collapsed-1920-light");
        await SetDarkAsync(page, true);
        await ShotAsync(page, board, "kanban-column-collapsed-1920-dark");
        await SetDarkAsync(page, false);
    }

    [TestMethod]
    [Description("E2E-FZE5.3: a collapsed swimlane (one quiet drop slot per column) is captured in both themes")]
    public async Task CollapsedLane_BothThemes()
    {
        var page = await OpenBoardAsync();
        var board = SwimlaneBoard(page);
        await board.ScrollIntoViewIfNeededAsync();

        await board.Locator("[data-testid='swimlane-Alice'] .tm-kanban__swimlane-toggle").ClickAsync();
        await page.WaitForTimeoutAsync(250);
        StringAssert.Contains(
            await board.Locator("[data-testid='swimlane-Alice']").GetAttributeAsync("class"),
            "tm-kanban__swimlane--collapsed");

        await ShotAsync(page, board, "kanban-swimlane-lane-collapsed-1920-light");
        await SetDarkAsync(page, true);
        await ShotAsync(page, board, "kanban-swimlane-lane-collapsed-1920-dark");
        await SetDarkAsync(page, false);
    }

    [TestMethod]
    [Description("E2E-FZE5.4: a collapsed column inside the swimlane layout (narrowed header cell, every lane cell narrowed with it) is captured in both themes")]
    public async Task CollapsedColumnInTheSwimlaneLayout_BothThemes()
    {
        var page = await OpenBoardAsync();
        var board = SwimlaneBoard(page);
        await board.ScrollIntoViewIfNeededAsync();

        await board.Locator("[data-testid='colhead-todo'] [data-testid='column-toggle-todo']").ClickAsync();
        await page.WaitForTimeoutAsync(250);
        StringAssert.Contains(
            await board.Locator("[data-testid='colhead-todo']").GetAttributeAsync("class"),
            "tm-kanban__column--collapsed");

        await ShotAsync(page, board, "kanban-swimlane-column-collapsed-1920-light");
        await SetDarkAsync(page, true);
        await ShotAsync(page, board, "kanban-swimlane-column-collapsed-1920-dark");
        await SetDarkAsync(page, false);
    }

    [TestMethod]
    [Description("E2E-FZE5.5: the drop-target highlight while a card is in flight over a COLLAPSED column is captured in both themes")]
    public async Task DropTargetOnACollapsedColumn_BothThemes()
    {
        const string Card = "[data-testid='board-column-doing'] .tm-kanban__card";
        const string Strip = "[data-testid='board-column-todo']";
        const string Highlighted = "[data-testid='board-column-todo'].tm-kanban__column--drop-target";

        var page = await OpenBoardAsync();
        var board = ClassicBoard(page);

        await board.Locator("[data-testid='column-toggle-todo']").ClickAsync();
        await page.WaitForTimeoutAsync(250);

        await StartDragOverAsync(page, Card, Strip);
        await page.Locator(Highlighted).WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await page.WaitForTimeoutAsync(150);
        await ShotAsync(page, board, "kanban-collapsed-drop-target-1920-light");
        await CancelDragAsync(page, Card);
        await page.Locator(Highlighted).WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });

        await SetDarkAsync(page, true);
        await StartDragOverAsync(page, Card, Strip);
        await page.Locator(Highlighted).WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await page.WaitForTimeoutAsync(150);
        await ShotAsync(page, board, "kanban-collapsed-drop-target-1920-dark");
        await CancelDragAsync(page, Card);
        await SetDarkAsync(page, false);
    }

    [TestMethod]
    [Description("E2E-FZE5.6: dropping onto a collapsed WIP-limited column past its limit shows the over-limit state — captured in both themes")]
    public async Task OverLimitCollapsedColumn_BothThemes()
    {
        const string Review = "[data-testid='board-column-review']";

        var page = await OpenBoardAsync();
        var board = ClassicBoard(page);

        // Review is the demo's tightest limited column (MaxItems 2, one card) — a collapsed strip that
        // receives two cards crosses the limit and has to SAY so without its body to tint.
        await board.Locator("[data-testid='column-toggle-review']").ClickAsync();
        await page.WaitForTimeoutAsync(250);

        for (var drop = 0; drop < 2; drop++)
        {
            // Each drop takes the current first card of Backlog, which shifts after every move.
            await StartDragOverAsync(page, "[data-testid='board-column-backlog'] .tm-kanban__card", Review);
            await page.Locator($"{Review}.tm-kanban__column--drop-target").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
            await DropAsync(page, Review);
            await page.WaitForTimeoutAsync(250);
        }

        var strip = board.Locator(Review);
        StringAssert.Contains(await strip.GetAttributeAsync("class"), "tm-kanban__column--over-limit",
            "the collapsed strip reports the WIP breach");
        Assert.AreEqual("3", (await strip.Locator(".tm-kanban__count").InnerTextAsync()).Trim(),
            "Review now holds three cards against a limit of two");
        Assert.AreEqual("/ 2", (await strip.Locator(".tm-kanban__wip-limit").InnerTextAsync()).Trim(),
            "the strip keeps its WIP badge");
        StringAssert.Contains(await strip.GetAttributeAsync("aria-label"), "over limit",
            "the strip's accessible name carries the breach too");

        await ShotAsync(page, board, "kanban-collapsed-over-limit-1920-light");
        await SetDarkAsync(page, true);
        await ShotAsync(page, board, "kanban-collapsed-over-limit-1920-dark");
        await SetDarkAsync(page, false);
    }

    [TestMethod]
    [Description("E2E-FZE5.7: the keyboard focus ring on the column-collapse toggle is captured in both themes")]
    public async Task FocusRingOnTheCollapseToggle_BothThemes()
    {
        var page = await OpenBoardAsync();
        var header = page.Locator("[data-testid='board-column-todo'] .tm-kanban__header");

        await TabUntilAsync(
            page,
            "() => document.activeElement?.matches(\"[data-testid='column-toggle-todo']\") === true",
            "the To Do column toggle");
        await ShotAsync(page, header, "kanban-focus-toggle-1920-light");

        await SetDarkAsync(page, true);
        await TabUntilAsync(
            page,
            "() => document.activeElement?.matches(\"[data-testid='column-toggle-todo']\") === true",
            "the To Do column toggle");
        await ShotAsync(page, header, "kanban-focus-toggle-1920-dark");
        await SetDarkAsync(page, false);
    }

    [TestMethod]
    [Description("E2E-FZE5.8: the keyboard focus ring on a card (the non-drag move entry point) is captured in both themes")]
    public async Task FocusRingOnACard_BothThemes()
    {
        var page = await OpenBoardAsync();
        var column = page.Locator("[data-testid='board-column-todo']");
        const string Predicate =
            "() => document.activeElement?.classList.contains('tm-kanban__card') === true"
            + " && document.activeElement?.closest(\"[data-testid='board-column-todo']\") !== null";

        await TabUntilAsync(page, Predicate, "the first card of the To Do column");
        await ShotAsync(page, column, "kanban-focus-card-1920-light");

        await SetDarkAsync(page, true);
        await TabUntilAsync(page, Predicate, "the first card of the To Do column");
        await ShotAsync(page, column, "kanban-focus-card-1920-dark");
        await SetDarkAsync(page, false);
    }
}
