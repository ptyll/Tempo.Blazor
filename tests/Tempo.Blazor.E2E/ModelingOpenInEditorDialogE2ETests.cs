using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Modeling redesign phase 4 — the "Open in editor" dialog on the core TmModal. Covers the modal
/// contracts the phase introduces: the Tab cycle stays inside the dialog (focus trap), Escape
/// closes it and restores the trigger focus, Escape consumed by the embedded diagram editor
/// (active search, focus inside the editor container) does NOT close it, a scrim click does not
/// close it (live changes propagate without a footer), and the dialog shows ONE header — the
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
    [Description("Escape with the diagram editor's search open does not close the dialog — the editor consumes Escape before the dialog layer")]
    public async Task Escape_with_active_diagram_editor_search_does_not_close_the_dialog()
    {
        var page = await OpenPageWithDialogAsync();

        // Open the diagram editor's search, then put the focus ON THE CANVAS — the editor's
        // canvas layer consumes Escape (draw.io ordering: cancel/search/selection before any
        // dialog layer), so the focus trap never sees an unhandled Escape.
        var searchToggle = page.Locator(
            "[data-testid='modeling-open-diagram-editor'] [data-tm-group='search'] button");
        await searchToggle.ClickAsync(new LocatorClickOptions { Timeout = 10000 });
        await page.Locator("[data-testid='modeling-open-diagram-editor'] .tm-diagram-search-panel")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.Locator("[data-testid='modeling-open-diagram-editor'] .tm-diagram-canvas")
            .ClickAsync(new LocatorClickOptions { Timeout = 10000 });

        await page.Keyboard.PressAsync("Escape");

        var overlay = page.Locator("[data-testid='modeling-open-diagram-editor']");
        await Assertions.Expect(overlay).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        await Assertions.Expect(page.Locator("[data-testid='modeling-open-diagram-editor'] .tm-diagram-search-panel"))
            .ToHaveCountAsync(1);
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

    /// <summary>Opens the loaded modeling page and the open-editor dialog.</summary>
    private async Task<IPage> OpenPageWithDialogAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
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
