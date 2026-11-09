using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E coverage for the non-drag card move (plan a98e4a71, phase 4 — WCAG 2.5.7): the card's keyboard
/// activation, the "Move…" menu and its mobile bottom sheet, the live-region announcement and the focus
/// hand-back to the moved card or to the collapsed strip it landed on.
/// <para>
/// The keyboard path is driven with REAL key presses (no mouse, no synthetic DOM events), which is what
/// the WCAG requirement is about: focus a card, open its move menu with M, walk the menu with Tab and
/// commit with Enter, then assert the demo's "Last change" line, the live region and
/// <c>document.activeElement</c>. The captures land in <c>TM_E2E_SHOT_DIR</c> (default
/// <c>/tmp/tm-kanban-shots</c>) for a hands-on light/dark and desktop/mobile review.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class KanbanMoveE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Environment.GetEnvironmentVariable("TM_E2E_SHOT_DIR")
        ?? Path.Combine(Path.GetTempPath(), "tm-kanban-shots");

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

    private async Task<IPage> OpenKanbanAsync(int? width = null)
    {
        var page = await CreatePageAsync();
        if (width is int viewportWidth)
        {
            await page.SetViewportSizeAsync(viewportWidth, 844);
        }

        await page.GotoAsync($"{BaseUrl}/kanban");
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-kanban__card-move").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 15000 });
        return page;
    }

    private static async Task SetDarkAsync(IPage page, bool dark)
    {
        // The demo shell carries its own data-theme (the ThemeService owns it), so drive the real
        // toggle — the aside's on a desktop and the mobile header's on a phone, hence :visible.
        var toggle = page.Locator("[data-testid='theme-toggle']:visible").First;
        var isDark = string.Equals(
            await toggle.GetAttributeAsync("aria-label"), "Switch to light mode", StringComparison.Ordinal);
        if (isDark != dark)
        {
            await toggle.ClickAsync();
        }

        await page.WaitForTimeoutAsync(250);
    }

    private static ILocator ClassicBoard(IPage page) => page.Locator(".tm-kanban:not(.tm-kanban--swimlanes)").First;

    /// <summary>The card with the given title inside one column of the board.</summary>
    private static ILocator Card(ILocator board, string columnId, string title)
        => board.Locator($"[data-testid='board-column-{columnId}'] .tm-kanban__card",
                new LocatorLocatorOptions { HasText = title });

    /// <summary>Focuses a card the way a keyboard reader arrives at it (an explicit focus, no mouse).</summary>
    private static async Task FocusCardAsync(ILocator card)
    {
        await card.ScrollIntoViewIfNeededAsync();
        await card.EvaluateAsync("element => element.focus()");
        Assert.IsTrue(await card.EvaluateAsync<bool>("element => element === document.activeElement"),
            "the card under test takes the focus before any key is pressed");
    }

    private static async Task<bool> ActiveElementIsAsync(IPage page, string cssClass)
        => await page.EvaluateAsync<bool>(
            "cssClass => document.activeElement?.classList.contains(cssClass) === true", cssClass);

    private static async Task<string> ActiveElementTextAsync(IPage page)
        => await page.EvaluateAsync<string>("() => document.activeElement?.textContent ?? ''");

    /// <summary>
    /// Moves the menu's focus onto the item with the given label with REAL ArrowDown presses — the APG
    /// menu model the shared <c>TmContextMenu</c> implements: the menu opens with its first ENABLED item
    /// focused and the arrows walk the items.
    /// </summary>
    private static async Task ArrowToMenuItemAsync(IPage page, string label)
    {
        for (var press = 0; press < 20; press++)
        {
            var active = (await ActiveElementTextAsync(page)).Trim();
            if (active.Contains(label, StringComparison.Ordinal)) return;
            await page.Keyboard.PressAsync("ArrowDown");
        }

        Assert.Fail($"The move menu never focused the item '{label}'.");
    }

    /// <summary>Opens the focused card's move menu with the keyboard.</summary>
    private static async Task OpenMoveMenuAsync(IPage page, ILocator card)
    {
        await FocusCardAsync(card);
        await page.Keyboard.PressAsync("m");
        await page.Locator(".tm-context-menu__item").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // The menu hands the focus to its first ENABLED item on open. Waiting for that is what makes
        // the following arrow presses walk the MENU instead of the board's roving card stop.
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.classList.contains('tm-context-menu__item') === true",
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });
        await page.WaitForTimeoutAsync(200);
    }

    /// <summary>Dismisses the open menu (Escape is the documented dismiss) and waits for it to unmount.</summary>
    private static async Task CloseMoveMenuAsync(IPage page)
    {
        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-context-menu__item").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
        await page.WaitForTimeoutAsync(150);
    }

    [TestMethod]
    [Description("E2E-FZE4.1: a card moves between columns with the keyboard only (focus → M → Tab → Enter), the live region announces it and the focus stays on the moved card")]
    public async Task AKeyboardMove_MovesTheCard_AnnouncesIt_AndKeepsTheFocusOnTheCard()
    {
        var page = await OpenKanbanAsync();
        var board = ClassicBoard(page);
        var card = Card(board, "todo", "Write API docs");

        await FocusCardAsync(card);
        Assert.IsTrue(await ActiveElementIsAsync(page, "tm-kanban__card"),
            "a keyboard reader can focus a card");

        // ── The menu, light (desktop popover anchored to the card's trigger) ──
        await OpenMoveMenuAsync(page, card);
        await ShotAsync(page, "kanban-move-menu-desktop-light");
        await CloseMoveMenuAsync(page);

        // ── The menu, dark (the theme toggle is a click outside the menu, so it is reopened) ──
        await SetDarkAsync(page, true);
        await OpenMoveMenuAsync(page, card);
        await ShotAsync(page, "kanban-move-menu-desktop-dark");
        await CloseMoveMenuAsync(page);
        await SetDarkAsync(page, false);

        // ── Walk the menu with the keyboard and commit ──
        await OpenMoveMenuAsync(page, card);
        await ArrowToMenuItemAsync(page, "Done");
        await page.Keyboard.PressAsync("Enter");

        var doneColumn = board.Locator("[data-testid='board-column-done']");
        await doneColumn.Locator(".tm-kanban__card", new LocatorLocatorOptions { HasText = "Write API docs" })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });

        var status = board.Locator("[data-testid='kanban-status']");
        StringAssert.Contains(await status.InnerTextAsync(), "Write API docs moved to Done",
            "one polite live region announces the result");

        var lastChange = page.Locator("p:has-text('Last change')").First;
        await page.WaitForTimeoutAsync(200);
        var change = await lastChange.InnerTextAsync();
        StringAssert.Contains(change, "moved from todo to done",
            "the move goes through the SAME OnItemMoved contract as a drop");
        StringAssert.Contains(change, "index 2", "Done held two cards, so a menu move appends");

        // ── The focus is handed back to the moved card ──
        await page.WaitForTimeoutAsync(300);
        Assert.IsTrue(await ActiveElementIsAsync(page, "tm-kanban__card"),
            "the focus lands on the card that moved, not on <body> or the closed menu");
        StringAssert.Contains(await ActiveElementTextAsync(page), "Write API docs",
            "the focused element is the moved card itself");

        await ShotAsync(page, "kanban-move-after-keyboard-move-light");
    }

    [TestMethod]
    [Description("E2E-FZE4.2: on a phone the move menu is the shared bottom sheet (light and dark) and a tap commits the move")]
    public async Task TheMoveMenu_IsABottomSheetOnAPhone()
    {
        var page = await OpenKanbanAsync(width: 390);
        var board = ClassicBoard(page);
        var card = Card(board, "todo", "Implement auth module");

        await OpenMoveMenuAsync(page, card);

        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        StringAssert.Contains(await sheet.Locator(".tm-overlay-panel-sheet__title").InnerTextAsync(),
            "Move Implement auth module", "the sheet is titled with the card's own name");
        await page.WaitForTimeoutAsync(250);
        await ShotAsync(page, "kanban-move-sheet-mobile-light");
        await CloseMoveMenuAsync(page);

        // The sheet is modal, so the theme toggle under its backdrop is reachable only while closed.
        await SetDarkAsync(page, true);
        await OpenMoveMenuAsync(page, card);
        await page.WaitForTimeoutAsync(250);
        await ShotAsync(page, "kanban-move-sheet-mobile-dark");
        await CloseMoveMenuAsync(page);
        await SetDarkAsync(page, false);

        // The move itself is committed from the sheet's own target list.
        await OpenMoveMenuAsync(page, card);
        await sheet.Locator(".tm-context-menu__item", new LocatorLocatorOptions { HasText = "Done" }).ClickAsync();

        await board.Locator("[data-testid='board-column-done'] .tm-kanban__card",
                new LocatorLocatorOptions { HasText = "Implement auth module" })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });

        StringAssert.Contains(await board.Locator("[data-testid='kanban-status']").InnerTextAsync(),
            "Implement auth module moved to Done");
    }

    [TestMethod]
    [Description("E2E-FZE4.4: a cell owns ONE tab stop — Tab leaves the card for the NEXT cell instead of walking the card's own move trigger")]
    public async Task OneTabStopPerCell_TakesTheReaderToTheNextCell()
    {
        var page = await OpenKanbanAsync();
        var board = ClassicBoard(page);

        // Every cell exposes exactly one [tabindex="0"], and every card's move trigger is outside the
        // tab order: the card's M / Alt+ArrowDown shortcut is what opens that menu for a keyboard reader.
        var cells = await board.EvaluateAsync<string[]>(
            "board => Array.from(board.querySelectorAll('[data-testid^=\"board-column-\"]'))" +
            ".map(cell => cell.querySelectorAll('[tabindex=\"0\"]').length + '/' + " +
            "cell.querySelectorAll('.tm-kanban__card-move .tm-context-menu__trigger').length + '/' + " +
            "cell.querySelectorAll('.tm-kanban__card-move .tm-context-menu__trigger[tabindex=\"-1\"]').length)");
        Assert.IsTrue(cells.Length > 0, "the board renders its columns");
        foreach (var cell in cells)
        {
            var counts = cell.Split('/');
            Assert.AreEqual("1", counts[0], "every cell owns exactly one tab stop");
            Assert.AreEqual(counts[1], counts[2], "every move trigger is outside the tab order");
        }

        var card = Card(board, "todo", "Write API docs");
        await FocusCardAsync(card);
        Assert.IsTrue(await ActiveElementIsAsync(page, "tm-kanban__card"));

        // A REAL Tab press (no mouse, no synthetic event): the next stop is the NEXT cell's own control
        // — the card's trigger and its sibling cards are all outside the tab order.
        await page.Keyboard.PressAsync("Tab");

        var nextCell = await page.EvaluateAsync<string>(
            "() => document.activeElement?.closest('[data-testid^=\"board-column-\"]')?.getAttribute('data-testid') ?? ''");
        Assert.AreNotEqual("board-column-todo", nextCell, "Tab must leave the cell the card sits in");
        Assert.IsTrue(nextCell.StartsWith("board-column-", StringComparison.Ordinal),
            $"Tab lands in the next cell, not on the card's own move trigger (got '{nextCell}')");
    }

    [TestMethod]
    [Description("E2E-FZE4.3: a move into a COLLAPSED column hands the focus back to the strip that received the card")]
    public async Task AMoveIntoACollapsedColumn_FocusesTheStrip()
    {
        var page = await OpenKanbanAsync();
        var board = ClassicBoard(page);
        var strip = board.Locator("[data-testid='board-column-done']");

        await strip.Locator("[data-testid='column-toggle-done']").ClickAsync();
        await page.WaitForTimeoutAsync(200);
        StringAssert.Contains(await strip.GetAttributeAsync("class"), "tm-kanban__column--collapsed");

        var card = Card(board, "review", "Update dependencies");
        await FocusCardAsync(card);
        await page.Keyboard.PressAsync("m");
        await ArrowToMenuItemAsync(page, "Done");
        await page.Keyboard.PressAsync("Enter");

        var lastChange = page.Locator("p:has-text('Last change')").First;
        await lastChange.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        StringAssert.Contains(await lastChange.InnerTextAsync(), "moved from review to done");

        // The card itself is not rendered inside the collapsed strip, so the STRIP takes the focus.
        await page.WaitForTimeoutAsync(300);
        Assert.IsTrue(await page.EvaluateAsync<bool>(
                "() => document.activeElement?.getAttribute('data-testid') === 'board-column-done'"),
            "the collapsed strip that received the card receives the focus too");
        Assert.AreEqual("3", (await strip.Locator(".tm-kanban__count").InnerTextAsync()).Trim(),
            "the strip's count includes the moved card (Done held two)");

        await ShotAsync(page, "kanban-move-collapsed-target-focus-light");
    }
}
