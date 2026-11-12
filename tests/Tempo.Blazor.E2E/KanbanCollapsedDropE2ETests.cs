using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E edge cases for the collapsed drop zones of <c>TmKanbanBoard</c> (plan 92e96b25, phase 5 / TG-005,
/// filling the gaps left after plan a98e4a71's suites): a drop on a collapsed column inside a collapsed
/// lane, a drop on the "no value" lane slot, an over-limit collapsed strip, a <see cref="bool">Disabled</see>
/// board that never highlights, the virtualized board with a <c>TestIdPrefix</c>, and two boards on one
/// page staying independent. The gesture is the same synthetic DOM drag the drop-zone suite uses (a
/// screenshot has to be possible while the card is in flight); locator screenshots land in
/// <c>tests/Tempo.Blazor.E2E/TestResults/kanban-collapsed-drop/</c> in both themes.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class KanbanCollapsedDropE2ETests : WasmTestBase
{
    private static string ShotDir
    {
        get
        {
            var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
                "TestResults", "kanban-collapsed-drop");
            Directory.CreateDirectory(dir);
            return dir;
        }
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

    private static async Task ShotAsync(IPage page, string name)
        => await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, name + ".png"),
            Type = ScreenshotType.Png,
            FullPage = true
        });

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
        var toggle = page.Locator("aside [data-testid='theme-toggle']");
        var isDark = string.Equals(
            await toggle.GetAttributeAsync("aria-label"), "Switch to light mode", StringComparison.Ordinal);
        if (isDark != dark)
        {
            await toggle.ClickAsync();
        }

        await page.WaitForTimeoutAsync(250);
    }

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

    private static Task DragOverAsync(IPage page, string targetSelector)
        => page.EvaluateAsync(
            """
            (targetSelector) => {
                const target = document.querySelector(targetSelector);
                if (!target) throw new Error('no target for ' + targetSelector);
                target.dispatchEvent(new DragEvent('dragover', {
                    bubbles: true, cancelable: true, dataTransfer: window.__tmKanbanDrag }));
            }
            """,
            targetSelector);

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

    private static Task EndDragAsync(IPage page, string cardSelector)
        => page.EvaluateAsync(
            """
            (cardSelector) => {
                const card = document.querySelector(cardSelector);
                card?.dispatchEvent(new DragEvent('dragend', {
                    bubbles: true, cancelable: true, dataTransfer: window.__tmKanbanDrag }));
            }
            """,
            cardSelector);

    private async Task<string> SwimChangeAsync(IPage page)
    {
        var change = page.Locator("section", new PageLocatorOptions { HasText = "Swimlanes" })
            .Locator("p:has-text('Last change')");
        await change.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        return await change.InnerTextAsync();
    }

    private async Task<string> ClassicChangeAsync(IPage page)
    {
        var change = page.Locator("section", new PageLocatorOptions { HasText = "Reorderable board" })
            .Locator("p:has-text('Last change')");
        await change.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        return await change.InnerTextAsync();
    }

    [TestMethod]
    [Description("E2E-TG5-E1: a drop on the slot of a COLLAPSED column inside a COLLAPSED lane commits " +
                 "the (column, lane) move; the slot is the narrow width and names its target")]
    public async Task CollapsedColumnInCollapsedLane_SlotCatchesTheDrop()
    {
        var page = await OpenKanbanAsync();
        var board = page.Locator(".tm-kanban--swimlanes").First;

        // Collapse the todo column first (narrows its header cell AND every lane cell and slot)…
        await board.Locator("[data-testid='column-toggle-todo']").First.ClickAsync();
        // …then collapse the Alice lane (its slots strip appears with the narrowed todo slot).
        var lane = board.Locator("[data-testid='swimlane-Alice']");
        await lane.Locator(".tm-kanban__swimlane-toggle").ClickAsync();
        await page.WaitForTimeoutAsync(250);

        var slot = board.Locator("[data-testid='lane-slot-todo-Alice']");
        StringAssert.Contains(await slot.GetAttributeAsync("class") ?? string.Empty,
            "tm-kanban__lane-slot--collapsed", "the slot under the collapsed column is the narrow width");
        Assert.AreEqual("To Do in lane Alice", await slot.GetAttributeAsync("title"),
            "the slot names its (column, lane) target");
        Assert.AreEqual("To Do in lane Alice", await slot.GetAttributeAsync("aria-label"));

        var card = "[data-testid='cell-doing-Bob'] .tm-kanban__card";
        await StartDragOverAsync(page, card, "[data-testid='lane-slot-todo-Alice']");
        await page.Locator("[data-testid='lane-slot-todo-Alice'].tm-kanban__lane-slot--drop-target")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await page.WaitForTimeoutAsync(150);
        await ShotAsync(page, "kanban-collapsed-slot-in-collapsed-lane-drag-light");
        await DropAsync(page, "[data-testid='lane-slot-todo-Alice']");

        var text = await SwimChangeAsync(page);
        StringAssert.Contains(text, "→ column todo, lane 'Alice'",
            "the narrowed slot of a collapsed lane accepts the drop and reports the (column, lane) pair");
    }

    [TestMethod]
    [Description("E2E-TG5-E2: the trailing \"no value\" lane's slot catches a drop and reports ToSwimlane = null")]
    public async Task NoValueLaneSlot_CatchesTheDrop_AndReportsNullLane()
    {
        var page = await OpenKanbanAsync();
        var board = page.Locator(".tm-kanban--swimlanes").First;

        // The swimlane demo holds one unassigned card, so the "No value" lane exists; collapse it.
        var noValue = board.Locator("[data-testid='swimlane-none']");
        await noValue.Locator(".tm-kanban__swimlane-toggle").ClickAsync();
        await page.WaitForTimeoutAsync(250);

        var slot = board.Locator("[data-testid='lane-slot-done-none']");
        Assert.AreEqual("Done in lane No value", await slot.GetAttributeAsync("title"),
            "the no-value slot names the localized lane");

        await StartDragOverAsync(
            page,
            "[data-testid='cell-done-Alice'] .tm-kanban__card",
            "[data-testid='lane-slot-done-none']");
        await page.Locator("[data-testid='lane-slot-done-none'].tm-kanban__lane-slot--drop-target")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await ShotAsync(page, "kanban-collapsed-no-value-slot-drag-light");
        await DropAsync(page, "[data-testid='lane-slot-done-none']");

        var text = await SwimChangeAsync(page);
        StringAssert.Contains(text, "→ column done, lane 'No value'",
            "the drop names the no-value lane for a reader");
        StringAssert.Contains(text, "ToSwimlane=null",
            "the move event still maps the no-value lane to null");
    }

    [TestMethod]
    [Description("E2E-TG5-E3: an over-limit collapsed strip keeps catching drops and stays visibly over limit " +
                 "in both themes (danger edge + count)")]
    public async Task OverLimitCollapsedStrip_KeepsCatchingDrops_InBothThemes()
    {
        var page = await OpenKanbanAsync();
        var board = page.Locator(".tm-kanban:not(.tm-kanban--swimlanes)").First;
        var strip = board.Locator("[data-testid='board-column-review']");

        // Review allows 2; fill it to 3 through two committed drops, then collapse it.
        for (var i = 0; i < 2; i++)
        {
            var source = i == 0
                ? "[data-testid='board-column-backlog'] .tm-kanban__card"
                : "[data-testid='board-column-todo'] .tm-kanban__card";
            await StartDragOverAsync(page, source, "[data-testid='board-column-review']");
            await DropAsync(page, "[data-testid='board-column-review']");
            await page.WaitForTimeoutAsync(250);
        }

        await strip.Locator("[data-testid='column-toggle-review']").ClickAsync();
        await page.WaitForTimeoutAsync(250);

        var cls = await strip.GetAttributeAsync("class") ?? string.Empty;
        StringAssert.Contains(cls, "tm-kanban__column--over-limit", "the collapsed strip shows the WIP breach");

        // ── In flight, dark: the over-limit strip still states the breach while it is a target ──
        await SetDarkAsync(page, true);
        await StartDragOverAsync(
            page,
            "[data-testid='board-column-doing'] .tm-kanban__card",
            "[data-testid='board-column-review']");
        await page.Locator("[data-testid='board-column-review'].tm-kanban__column--drop-target")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await page.WaitForTimeoutAsync(150);
        await ShotAsync(page, "kanban-collapsed-over-limit-strip-drag-dark");
        await DropAsync(page, "[data-testid='board-column-review']");

        var text = await ClassicChangeAsync(page);
        StringAssert.Contains(text, "moved from doing to review",
            "the over-limit strip still accepts the drop");

        Assert.AreEqual("4", (await strip.Locator(".tm-kanban__count").InnerTextAsync()).Trim(),
            "the breach count includes the dropped card");
        cls = await strip.GetAttributeAsync("class") ?? string.Empty;
        StringAssert.Contains(cls, "tm-kanban__column--over-limit",
            "the strip stays over limit in dark mode after the drop");
        await SetDarkAsync(page, false);
    }

    [TestMethod]
    [Description("E2E-TG5-E4: a Disabled board never highlights a zone and never commits a drop")]
    public async Task DisabledBoard_NoHighlight_NoDrop()
    {
        var page = await OpenKanbanAsync();
        var board = page.Locator("[data-testid='disabled-board-column-todo']").Locator("..");

        await StartDragOverAsync(
            page,
            "[data-testid='disabled-board-column-todo'] .tm-kanban__card",
            "[data-testid='disabled-board-column-doing']");
        await page.WaitForTimeoutAsync(250);

        Assert.AreEqual(0, await page.Locator(".tm-kanban--dragging").CountAsync(),
            "a Disabled board never reports the dragging state");
        Assert.AreEqual(0, await page.Locator(".tm-kanban__column--drop-target").CountAsync(),
            "no zone of the disabled board highlights");
        Assert.AreEqual(0, await page.Locator(".tm-kanban__lane-slot--drop-target").CountAsync());

        await DropAsync(page, "[data-testid='disabled-board-column-doing']");
        await page.WaitForTimeoutAsync(250);
        Assert.AreEqual(0, await page.Locator("p:has-text('Last change')").CountAsync(),
            "the disabled board has no move to report — and the enabled boards were untouched");
        await ShotAsync(page, "kanban-disabled-board-no-highlight-light");
    }

    [TestMethod]
    [Description("E2E-TG5-E5: the virtualized board (TestIdPrefix=\"virtual\") collapses and its collapsed " +
                 "strip catches a drop — the move commits through the full list even though only a window " +
                 "renders; the unprefixed boards on the same page are untouched")]
    public async Task VirtualizedBoard_WithTestIdPrefix_CollapsedStripCatchesTheDrop()
    {
        var page = await OpenKanbanAsync();
        var board = page.Locator("[data-testid='virtual-board-column-todo']").Locator("..");

        await board.Locator("[data-testid='virtual-column-toggle-todo']").ClickAsync();
        await page.WaitForTimeoutAsync(250);

        var strip = board.Locator("[data-testid='virtual-board-column-todo']");
        StringAssert.Contains(await strip.GetAttributeAsync("class") ?? string.Empty,
            "tm-kanban__column--collapsed", "the virtualized column collapses to the strip");

        await StartDragOverAsync(
            page,
            "[data-testid='virtual-board-column-backlog'] .tm-kanban__card",
            "[data-testid='virtual-board-column-todo']");
        await page.Locator("[data-testid='virtual-board-column-todo'].tm-kanban__column--drop-target")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await ShotAsync(page, "kanban-virtual-collapsed-strip-drag-light");
        await DropAsync(page, "[data-testid='virtual-board-column-todo']");

        // The virtualized board binds the same move contract as the first board: the drop commits
        // and the count follows it (159 generated todo cards + the appended one).
        var change = page.Locator("section", new PageLocatorOptions { HasText = "Virtualized columns" })
            .Locator("p:has-text('Last change')");
        await change.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        StringAssert.Contains(await change.InnerTextAsync(), "moved from backlog to todo",
            "the virtualized board reports the append through the same KanbanMoveEvent contract");
        Assert.AreEqual("161", (await strip.Locator(".tm-kanban__count").InnerTextAsync()).Trim(),
            "the collapsed strip's count reflects the appended card");
        Assert.AreEqual("2", (await page.Locator(".tm-kanban:not(.tm-kanban--swimlanes)").First
                .Locator("[data-testid='board-column-todo'] .tm-kanban__count").InnerTextAsync()).Trim(),
            "the unprefixed board above is untouched — the two boards on one page stay independent");

        // Expand again: the appended card sits at the END of the virtualized column's full list.
        await board.Locator("[data-testid='virtual-column-toggle-todo']").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        var cards = await board.Locator("[data-testid='virtual-cards-virtual-todo'] .tm-kanban__card")
            .AllInnerTextsAsync();
        Assert.IsTrue(cards.Count >= 1, "the expanded virtualized column renders its window");
    }

    [TestMethod]
    [Description("E2E-TG5-E6: the collapsed lane's slots keep their colour strips and quiet idle look in both " +
                 "themes — the UX reference screenshots")]
    public async Task CollapsedLaneSlots_ColourStrips_BothThemes()
    {
        var page = await OpenKanbanAsync();
        var board = page.Locator(".tm-kanban--swimlanes").First;
        var lane = board.Locator("[data-testid='swimlane-Alice']");
        await lane.Locator(".tm-kanban__swimlane-toggle").ClickAsync();
        await page.WaitForTimeoutAsync(250);

        Assert.AreEqual(5, await board.Locator(".tm-kanban__slot-color").CountAsync(),
            "every slot of the collapsed lane carries its column's colour strip");

        await ShotAsync(page, "kanban-collapsed-lane-slots-idle-light");
        await SetDarkAsync(page, true);
        await ShotAsync(page, "kanban-collapsed-lane-slots-idle-dark");
        await SetDarkAsync(page, false);
    }

    [TestMethod]
    [Description("E2E-TG5-E7: column dots resolve the theme tokens — strong tones in light, lifted 400-tones " +
                 "in dark (computed-style proof of the TG-005 contract)")]
    public async Task ColumnDots_FollowTheThemeTokens_InBothThemes()
    {
        var page = await OpenKanbanAsync();

        var done = page.Locator(".tm-kanban:not(.tm-kanban--swimlanes)").First
            .Locator("[data-testid='board-column-done'] .tm-kanban__header-color");
        var light = await done.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        StringAssert.Contains(light, "21, 128, 61", "light: --tm-color-success-strong #15803d");

        await SetDarkAsync(page, true);
        var dark = await done.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        StringAssert.Contains(dark, "74, 222, 128", "dark: the lifted green-400 #4ade80");
        await SetDarkAsync(page, false);
    }
}
