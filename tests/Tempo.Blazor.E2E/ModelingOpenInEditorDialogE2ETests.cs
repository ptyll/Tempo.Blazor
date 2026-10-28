using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Modeling redesign phase 4 — the "Open in editor" dialog on the core TmModal. Covers the modal
/// contracts the phase introduces: the Tab cycle stays inside the dialog (focus trap), Escape
/// closes it and restores the trigger focus, the editor's CAPTURE-phase Escape consumers close
/// THEIR surface first (open search panel, open context menu) and keep the dialog open, while an
/// Escape nothing consumes (a focused canvas without a selection) closes it, a scrim click does
/// not close it (live changes propagate without a footer), and the dialog shows ONE header — the
/// diagram editor renders without its own document title.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingOpenInEditorDialogE2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("The Tab cycle stays inside the open-editor dialog (focus trap)")]
    public async Task Tab_cycle_stays_inside_the_dialog()
    {
        var page = await OpenPageWithDialogAsync();

        // The dialog opens with focus inside it (the modal's first focusable — the close button).
        var opensInside = await page.EvaluateAsync<bool>(
            """
            () => {
                const overlay = document.querySelector("[data-testid='modeling-open-diagram-editor']");
                return !!overlay && overlay.contains(document.activeElement);
            }
            """);
        Assert.IsTrue(opensInside, "the dialog opens with focus inside it");

        Assert.IsTrue(await FocusStaysInsideAsync(page, 40),
            "40 Tabs from inside the dialog must land back inside the dialog (focus trap)");
    }

    [TestMethod]
    [Description("Escape closes the open-editor dialog and restores focus to the trigger")]
    public async Task Escape_closes_the_dialog_and_restores_focus_to_the_trigger()
    {
        var page = await OpenPageWithDialogAsync();

        // The initial focus is the dialog's close button (the first focusable of the modal) —
        // OUTSIDE the embedded editor container, so the diagram editor does not consume Escape.
        await page.Keyboard.PressAsync("Escape");

        var overlay = page.Locator("[data-testid='modeling-open-diagram-editor']");
        await Assertions.Expect(overlay).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Assertions.Expect(page.Locator("[data-testid='modeling-open-in-editor-button']")).ToBeFocusedAsync(
            new LocatorAssertionsToBeFocusedOptions { Timeout = 10000 });
    }

    [TestMethod]
    [Description("Escape with the search panel open closes the SEARCH (and keeps the dialog), the second Escape closes the dialog")]
    public async Task Escape_with_open_search_closes_the_search_first_and_the_dialog_second()
    {
        var page = await OpenPageWithDialogAsync();

        // Open the diagram editor's search and focus its INPUT: the editor's capture-phase Escape
        // consumer owns this Escape (it closes the search surface and marks the event), so the
        // dialog behind the editor must not close on it.
        var dialog = page.Locator("[data-testid='modeling-open-diagram-editor']");
        await dialog.Locator("[data-tm-group='search'] button")
            .ClickAsync(new LocatorClickOptions { Timeout = 10000 });
        var searchPanel = dialog.Locator(".tm-diagram-search-panel");
        await searchPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await searchPanel.Locator("input").First.FocusAsync();

        await page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        await Assertions.Expect(searchPanel).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

        // The close path moved the focus back onto the canvas (no selection, no session) — the
        // SECOND Escape is consumed by nobody, so the dialog layer closes.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialog).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
    }

    [TestMethod]
    [Description("Escape on a focused canvas with no selection consumes nothing — the dialog closes")]
    public async Task Escape_on_empty_canvas_closes_the_dialog()
    {
        var page = await OpenPageWithDialogAsync();

        // Focus the canvas (a press on the empty canvas area selects nothing) — the canvas Escape
        // branch has nothing to consume and stays unmarked, so the trap closes the dialog.
        await page.Locator("[data-testid='modeling-open-diagram-editor'] .tm-diagram-canvas")
            .ClickAsync(new LocatorClickOptions { Timeout = 10000, Position = new Position { X = 10, Y = 10 } });

        await page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(page.Locator("[data-testid='modeling-open-diagram-editor']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
    }

    [TestMethod]
    [Description("Escape with the diagram context menu open closes the MENU and keeps the dialog open")]
    public async Task Escape_with_open_context_menu_closes_the_menu_and_keeps_the_dialog()
    {
        var page = await OpenPageWithDialogAsync();

        // Open the editor's custom context menu on a node, then press Escape: the menu's
        // capture-phase key handler marks the Escape as handled (menu closes), the dialog stays.
        var dialog = page.Locator("[data-testid='modeling-open-diagram-editor']");
        await dialog.Locator(".tm-diagram-node").First
            .ClickAsync(new LocatorClickOptions { Button = MouseButton.Right, Timeout = 10000 });
        var contextMenu = dialog.Locator(".tm-diagram-editor__context-menu");
        await contextMenu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(contextMenu).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        await Assertions.Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
    }

    [TestMethod]
    [Description("A scrim click does not close the dialog — changes keep propagating live")]
    public async Task Scrim_click_does_not_close_the_dialog()
    {
        var page = await OpenPageWithDialogAsync();

        // The overlay covers the viewport; the 90vw panel leaves a scrim strip at the edges.
        await page.Mouse.ClickAsync(8, 8);

        var overlay = page.Locator("[data-testid='modeling-open-diagram-editor']");
        await Assertions.Expect(overlay).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        await Assertions.Expect(page.Locator("[data-testid='modeling-open-diagram-editor'] .tm-modal-footer"))
            .ToHaveCountAsync(0);
    }

    [TestMethod]
    [Description("The dialog has one header: the TmModal title shows the document title, the embedded editor renders no title of its own")]
    public async Task Dialog_shows_one_header_without_the_editor_title()
    {
        var page = await OpenPageWithDialogAsync();

        var overlay = page.Locator("[data-testid='modeling-open-diagram-editor']");
        var titles = overlay.Locator(".tm-modal-title");
        await Assertions.Expect(titles).ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        Assert.IsFalse(string.IsNullOrWhiteSpace(await titles.First.InnerTextAsync()),
            "the TmModal header carries the document title");

        await Assertions.Expect(overlay.Locator(".tm-diagram-editor__title"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
    }

    [TestMethod]
    [Description("The embedded editor resolves DESKTOP from the dialog body at 1280x800 (LayoutBreakpoints 768/1024)")]
    public async Task Embedded_editor_resolves_desktop_inside_the_dialog_at_1280x800()
    {
        // The dialog body (~90vw ≈ 1152px) is its own layout scope: the embedded editor carries
        // LayoutBreakpoints 768/1024 and must resolve desktop even at a 1280x800 VIEWPORT.
        var page = await OpenPageWithDialogAsync(1280, 800);

        var editor = page.Locator("[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor");
        await Assertions.Expect(editor).ToHaveAttributeAsync(
            "data-editor-layout", "desktop", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
    }

    /// <summary>Opens the loaded modeling page and the open-editor dialog.</summary>
    private async Task<IPage> OpenPageWithDialogAsync(int width = 1440, int height = 900)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.Locator("[data-testid='modeling-diagram-preview']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await page.Locator("[data-testid='modeling-generate-diagram-button']").ClickAsync();
        await page.Locator("[data-testid='modeling-open-in-editor-button']")
            .ClickAsync(new LocatorClickOptions { Timeout = 10000 });
        await page.Locator("[data-testid='modeling-open-diagram-editor'] .tm-modal")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        return page;
    }

    /// <summary>Presses <paramref name="tabs"/> Tabs from inside the dialog and reports whether
    /// the focus returned into the overlay (the trap walked the cycle).</summary>
    private static async Task<bool> FocusStaysInsideAsync(IPage page, int tabs)
    {
        for (var index = 0; index < tabs; index++)
        {
            await page.Keyboard.PressAsync("Tab");
        }

        return await page.EvaluateAsync<bool>(
            """
            () => {
                const overlay = document.querySelector("[data-testid='modeling-open-diagram-editor']");
                return !!overlay && overlay.contains(document.activeElement);
            }
            """);
    }
}
