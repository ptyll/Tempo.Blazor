using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class SpreadsheetPhase6E2ETests : WasmTestBase
{
    [TestMethod]
    public async Task NamedRange_CreateAndUseInFormula()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        // Navigate to A1 via Name Box
        await page.FillAsync(".tm-spreadsheet-formula-bar__ref", "A1");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(200);

        // Open Name Manager via Data tab
        await page.ClickAsync(".tm-spreadsheet-toolbar__tab:text-is('Data')");
        await page.ClickAsync("button[title='Name Manager']");
        await page.WaitForSelectorAsync(".tm-spreadsheet-name-manager", new() { State = WaitForSelectorState.Visible });

        // Create named range
        await page.ClickAsync(".tm-spreadsheet-name-manager__toolbar button:has-text('New')");
        await page.WaitForSelectorAsync(".tm-spreadsheet-named-range-edit", new() { State = WaitForSelectorState.Visible });
        await page.FillAsync("#nr-name", "TestRange");
        await page.FillAsync("#nr-refers", "=Sheet1!A1:A3");
        await page.ClickAsync(".tm-spreadsheet-named-range-edit__btn--ok");

        // Close manager
        await page.ClickAsync(".tm-spreadsheet-name-manager__btn--close");

        // Navigate to B1 via Name Box and enter formula
        await page.FillAsync(".tm-spreadsheet-formula-bar__ref", "B1");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(200);
        await page.Keyboard.TypeAsync("=SUM(TestRange)");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(500);

        // Navigate back to B1 to verify formula result (formula bar shows display value, not input, after commit)
        await page.FillAsync(".tm-spreadsheet-formula-bar__ref", "B1");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);
        await page.WaitForSelectorAsync(".tm-spreadsheet-formula-bar__display");

        // Verify formula accepted (cell shows result, not #NAME?)
        var displayValue = await page.TextContentAsync(".tm-spreadsheet-formula-bar__display");
        Assert.IsFalse(displayValue?.Contains("#NAME?") ?? false, $"Display should not contain #NAME?, was: {displayValue}");
    }

    [TestMethod]
    public async Task Hyperlink_InsertWebLink()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        await page.ClickAsync(".tm-spreadsheet-canvas-grid");
        await page.WaitForTimeoutAsync(300);

        // Open Insert tab → Link
        await page.ClickAsync(".tm-spreadsheet-toolbar__tab:text-is('Insert')");
        await page.ClickAsync("button[title='Insert link']");
        await page.WaitForSelectorAsync(".tm-spreadsheet-hyperlink", new() { State = WaitForSelectorState.Visible });

        // Fill dialog
        await page.Locator("#hl-type .tm-radio-segment").First.ClickAsync(); // Web page is the first segment
        await page.FillAsync("#hl-target", "https://example.com");
        await page.FillAsync("#hl-display", "Example");
        await page.ClickAsync(".tm-spreadsheet-hyperlink__btn--ok");

        // Verify cell has blue hyperlink styling (canvas renders it)
        await page.WaitForTimeoutAsync(300);
        var cellText = await page.TextContentAsync(".tm-spreadsheet-canvas-grid");
        StringAssert.Contains(cellText, "Example");
    }

    [TestMethod]
    public async Task NameBox_NavigateToNamedRange()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        // Pre-create a named range via Name Manager
        await page.ClickAsync(".tm-spreadsheet-toolbar__tab:text-is('Data')");
        await page.ClickAsync("button[title='Name Manager']");
        await page.WaitForSelectorAsync(".tm-spreadsheet-name-manager", new() { State = WaitForSelectorState.Visible });
        await page.ClickAsync(".tm-spreadsheet-name-manager__toolbar button:has-text('New')");
        await page.WaitForSelectorAsync(".tm-spreadsheet-named-range-edit", new() { State = WaitForSelectorState.Visible });
        await page.FillAsync("#nr-name", "JumpTarget");
        await page.FillAsync("#nr-refers", "=Sheet1!D10");
        await page.ClickAsync(".tm-spreadsheet-named-range-edit__btn--ok");
        await page.ClickAsync(".tm-spreadsheet-name-manager__btn--close");

        // Use Name Box to navigate
        await page.ClickAsync(".tm-spreadsheet-formula-bar__ref");
        await page.FillAsync(".tm-spreadsheet-formula-bar__ref", "JumpTarget");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(500);

        // Name box should now show D10
        var nameBoxValue = await page.InputValueAsync(".tm-spreadsheet-formula-bar__ref");
        Assert.AreEqual("D10", nameBoxValue);
    }

    // ── Phase 1 (wave 0): dialogs are real modal overlays, alerts are alertdialogs ─────
    // The three dialogs used to render as an unstyled block in the document flow UNDER the
    // grid. These tests prove the fix in a real browser (a bounding box over the grid + a fixed
    // overlay on the modal z-band), plus the keyboard contracts of the two selection lists and
    // the validation alert. Screenshots land in __screenshots__/spreadsheet/ (desktop 1440) for
    // the UX review against output/imagegen/tempo-reimagined/Tempo.Blazor.Spreadsheet/.

    private const string ScreenshotDirectory = "spreadsheet";

    /// <summary>The grid's top in DOCUMENT coordinates, so a page scroll cannot fake a layout shift.</summary>
    private static Task<double> GridDocumentTopAsync(ILocator grid)
        => grid.EvaluateAsync<double>("el => el.getBoundingClientRect().top + window.scrollY");

    private static ILocator DemoComponent(IPage page)
        => page.Locator("[data-testid='spreadsheet-demo']");

    private static ILocator DemoGrid(IPage page)
        => DemoComponent(page).Locator(".tm-spreadsheet-canvas-grid");

    private static ILocator ToolbarTab(IPage page, string text)
        => DemoComponent(page).Locator(".tm-spreadsheet-toolbar__tab", new() { HasTextString = text });

    private async Task<IPage> OpenDesktopDemoAsync()
    {
        var page = await CreatePageAsync();
        await page.SetViewportSizeAsync(1440, 960);
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);
        await DemoGrid(page).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    private static Task SelectToolbarTabAsync(IPage page, string text)
        => ToolbarTab(page, text).ClickAsync();

    /// <summary>
    /// Opens the manager. The caller switches the toolbar tab FIRST and measures the grid after
    /// that, because switching the tab re-lays the toolbar out — the dialog must be the only
    /// layout-relevant change between the measurement and the assertion.
    /// </summary>
    private static async Task OpenNameManagerAsync(IPage page)
    {
        await DemoComponent(page).Locator("button[title='Name Manager']").ClickAsync();
        await page.Locator(".tm-spreadsheet-name-manager").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await page.WaitForTimeoutAsync(200);
    }

    private static async Task OpenHyperlinkDialogAsync(IPage page)
    {
        await DemoComponent(page).Locator("button[title='Insert link']").ClickAsync();
        await page.Locator(".tm-spreadsheet-hyperlink").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await page.WaitForTimeoutAsync(200);
    }

    /// <summary>Seeds one named range through the Name Manager → New flow (the manager must be open).</summary>
    private static async Task CreateNamedRangeAsync(IPage page, string name, string refersTo)
    {
        await page.Locator(".tm-spreadsheet-name-manager__toolbar button", new() { HasTextString = "New" }).ClickAsync();
        var editor = page.Locator(".tm-spreadsheet-named-range-edit");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.FillAsync("#nr-name", name);
        await page.FillAsync("#nr-refers", refersTo);
        await page.Locator(".tm-spreadsheet-named-range-edit__btn--ok").ClickAsync();
        try
        {
            await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        }
        catch (TimeoutException)
        {
            var fieldValues = await editor.EvaluateAsync<string>(
                @"el => Array.from(el.querySelectorAll('input')).map(i => i.id + '=' + i.value).join(', ')
                       + ' | errors: ' + Array.from(el.querySelectorAll('.tm-spreadsheet-named-range-edit__error')).map(e => e.textContent).join(', ')");
            Assert.Fail($"the named-range editor stayed open while creating '{name}' ({refersTo}): {fieldValues}");
        }

        await page.WaitForTimeoutAsync(150);
    }

    /// <summary>
    /// The core of the fix, measured in the browser: the card is a fixed overlay centred over the
    /// grid — it paints inside the grid's own vertical band, its backdrop sits on the modal z-band,
    /// and opening it does not move the grid down the page (which is exactly what a dialog in the
    /// document flow does).
    /// </summary>
    private static async Task AssertDialogFloatsOverGridAsync(
        IPage page,
        ILocator dialogRoot,
        ILocator grid,
        double gridDocumentTopBefore)
    {
        // The dialog's root class sits on the core TmModal overlay; the CARD is the focus-scope
        // panel inside it (.tm-modal), and the overlay itself is the backdrop.
        var dialog = dialogRoot.Locator(".tm-modal").First;
        var backdrop = dialogRoot;
        var box = await dialog.BoundingBoxAsync();
        Assert.IsNotNull(box, "the dialog must have a box");
        var viewport = page.ViewportSize!;

        Assert.IsTrue(box!.Y >= 0, $"the dialog must start inside the viewport, was Y={box.Y}");
        Assert.IsTrue(box.Y + box.Height <= viewport.Height + 1,
            $"the dialog must fit the viewport, bottom={box.Y + box.Height} viewport={viewport.Height}");
        Assert.IsTrue(box.X >= 0 && box.X + box.Width <= viewport.Width + 1,
            $"the dialog must fit the viewport horizontally, was {box.X}..{box.X + box.Width}");

        // The card overlaps the grid instead of being pushed below the component.
        var gridBox = await grid.BoundingBoxAsync();
        Assert.IsNotNull(gridBox);
        Assert.IsTrue(box.Y < gridBox!.Y + gridBox.Height && box.Y + box.Height > gridBox.Y,
            $"the dialog must overlap the grid vertically; dialog={box.Y}..{box.Y + box.Height}, "
            + $"grid={gridBox.Y}..{gridBox.Y + gridBox.Height}");

        // ... and the grid itself did not move down the page. The document-relative top is the
        // scroll-independent measure of that: a dialog in the flow grows its container, which moves
        // every following box; an overlay leaves the layout alone.
        var gridDocumentTopAfter = await GridDocumentTopAsync(grid);
        Assert.AreEqual(gridDocumentTopBefore, gridDocumentTopAfter, 1.0,
            "opening the dialog must not push the grid down (it is an overlay, not an in-flow block)");

        var backdropPosition = await backdrop.EvaluateAsync<string>(
            "el => getComputedStyle(el).position");
        Assert.AreEqual("fixed", backdropPosition, "the overlay must be position: fixed");

        var backdropZIndex = await backdrop.EvaluateAsync<string>(
            "el => getComputedStyle(el).zIndex");
        Assert.IsTrue(int.TryParse(backdropZIndex, out var z) && z >= 1000,
            $"the overlay must sit on the modal z-band, was z-index '{backdropZIndex}'");
    }

    [TestMethod]
    public async Task Phase1_NameManagerDialog_FloatsOverGrid_AndEscapeCloses()
    {
        var page = await OpenDesktopDemoAsync();
        var grid = DemoGrid(page);

        await SelectToolbarTabAsync(page, "Data");
        var gridDocumentTopBefore = await GridDocumentTopAsync(grid);
        await OpenNameManagerAsync(page);

        var dialog = page.Locator(".tm-spreadsheet-name-manager");
        await AssertDialogFloatsOverGridAsync(page, page.Locator(".tm-modal-overlay.tm-spreadsheet-name-manager"), grid, gridDocumentTopBefore);

        var scope = page.Locator(".tm-spreadsheet-name-manager [role='dialog']").First;
        Assert.AreEqual("true", await scope.GetAttributeAsync("aria-modal"));
        var labelledBy = await scope.GetAttributeAsync("aria-labelledby");
        Assert.IsFalse(string.IsNullOrWhiteSpace(labelledBy));
        StringAssert.Contains(await page.Locator($"[id='{labelledBy}']").TextContentAsync() ?? string.Empty,
            "Name Manager");

        // Populate the list so the screenshot shows the table (and the selected row) rather than
        // the empty state, which has its own edge-case test and screenshot.
        await CreateNamedRangeAsync(page, "Sales", "=Sheet1!B2:B9");
        await CreateNamedRangeAsync(page, "Costs", "=Sheet1!C2:C9");
        Assert.AreEqual(2, await dialog.Locator("tbody tr").CountAsync());

        await SaveScreenshotAsync(page, "name-manager-dialog-desktop");

        // Creating the two ranges destroyed the focused editor nodes, so focus is re-stated on the
        // manager before Escape (returning focus to the opener is the TmModal/TmFocusScope contract
        // of phase 3, not part of this wave).
        await dialog.Locator(".tm-spreadsheet-name-manager__table").FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        await dialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 10000
        });
        await page.WaitForTimeoutAsync(300);
        Assert.IsTrue(await page.EvaluateAsync<bool>(
                "() => !!document.activeElement?.closest('.tm-spreadsheet-canvas-grid')"),
            "closing the Name Manager restores focus to the grid (the focus scope's restore target)");
    }

    [TestMethod]
    public async Task Phase1_NameManagerDialog_EmptyList_ShowsEmptyState()
    {
        // Edge case: a workbook with no named ranges at all — the dialog must still be a proper
        // overlay and say so instead of collapsing to an empty card.
        var page = await OpenDesktopDemoAsync();
        var grid = DemoGrid(page);

        await SelectToolbarTabAsync(page, "Data");
        var gridDocumentTopBefore = await GridDocumentTopAsync(grid);
        await OpenNameManagerAsync(page);

        var dialog = page.Locator(".tm-spreadsheet-name-manager");
        await AssertDialogFloatsOverGridAsync(page, page.Locator(".tm-modal-overlay.tm-spreadsheet-name-manager"), grid, gridDocumentTopBefore);

        var empty = dialog.Locator(".tm-spreadsheet-name-manager__empty");
        await empty.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        StringAssert.Contains(await empty.TextContentAsync() ?? string.Empty, "No named ranges");

        await SaveScreenshotAsync(page, "name-manager-dialog-empty-desktop");

        await page.Keyboard.PressAsync("Escape");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
    }

    [TestMethod]
    public async Task Phase1_NamedRangeEditDialog_FloatsOverGrid_AndEscapeCloses()
    {
        var page = await OpenDesktopDemoAsync();
        var grid = DemoGrid(page);

        await SelectToolbarTabAsync(page, "Data");
        await OpenNameManagerAsync(page);
        var gridDocumentTopBefore = await GridDocumentTopAsync(grid);
        await page.Locator(".tm-spreadsheet-name-manager__toolbar button", new() { HasTextString = "New" }).ClickAsync();

        var dialog = page.Locator(".tm-spreadsheet-named-range-edit");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(200);

        await AssertDialogFloatsOverGridAsync(page, page.Locator(".tm-modal-overlay.tm-spreadsheet-named-range-edit"), grid, gridDocumentTopBefore);

        var scope = page.Locator(".tm-spreadsheet-named-range-edit [role='dialog']").First;
        Assert.AreEqual("true", await scope.GetAttributeAsync("aria-modal"));
        var labelledBy = await scope.GetAttributeAsync("aria-labelledby");
        StringAssert.Contains(await page.Locator($"[id='{labelledBy}']").TextContentAsync() ?? string.Empty, "New Name");

        await SaveScreenshotAsync(page, "named-range-edit-dialog-desktop");

        await page.Keyboard.PressAsync("Escape");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });

        // Escape closed only the editor: the manager underneath is still up.
        await page.Locator(".tm-spreadsheet-name-manager").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });
    }

    [TestMethod]
    public async Task Phase1_NamedRangeEditDialog_InvalidName_ShowsError()
    {
        // Edge case: an invalid name shows the inline error inside the (still floating) dialog.
        var page = await OpenDesktopDemoAsync();
        var grid = DemoGrid(page);

        await SelectToolbarTabAsync(page, "Data");
        await OpenNameManagerAsync(page);
        var gridDocumentTopBefore = await GridDocumentTopAsync(grid);
        await page.Locator(".tm-spreadsheet-name-manager__toolbar button", new() { HasTextString = "New" }).ClickAsync();

        var dialog = page.Locator(".tm-spreadsheet-named-range-edit");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await page.FillAsync("#nr-name", "1 not a name");
        await page.FillAsync("#nr-refers", "=Sheet1!A1");
        await page.Locator(".tm-spreadsheet-named-range-edit__btn--ok").ClickAsync();

        var error = dialog.Locator(".tm-spreadsheet-named-range-edit__error");
        await error.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        StringAssert.Contains(await error.TextContentAsync() ?? string.Empty, "not valid");

        await AssertDialogFloatsOverGridAsync(page, page.Locator(".tm-modal-overlay.tm-spreadsheet-named-range-edit"), grid, gridDocumentTopBefore);

        await SaveScreenshotAsync(page, "named-range-edit-dialog-error-desktop");

        await page.Keyboard.PressAsync("Escape");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
    }

    [TestMethod]
    public async Task Phase1_HyperlinkDialog_FloatsOverGrid_AndEscapeCloses()
    {
        var page = await OpenDesktopDemoAsync();
        var grid = DemoGrid(page);

        await SelectToolbarTabAsync(page, "Insert");
        var gridDocumentTopBefore = await GridDocumentTopAsync(grid);
        await OpenHyperlinkDialogAsync(page);

        var dialog = page.Locator(".tm-spreadsheet-hyperlink");
        await AssertDialogFloatsOverGridAsync(page, page.Locator(".tm-modal-overlay.tm-spreadsheet-hyperlink"), grid, gridDocumentTopBefore);

        var scope = page.Locator(".tm-spreadsheet-hyperlink [role='dialog']").First;
        Assert.AreEqual("true", await scope.GetAttributeAsync("aria-modal"));
        var labelledBy = await scope.GetAttributeAsync("aria-labelledby");
        StringAssert.Contains(await page.Locator($"[id='{labelledBy}']").TextContentAsync() ?? string.Empty, "Hyperlink");

        await SaveScreenshotAsync(page, "hyperlink-dialog-desktop");

        await page.Keyboard.PressAsync("Escape");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await page.WaitForTimeoutAsync(300);
        Assert.IsTrue(await page.EvaluateAsync<bool>(
                "() => !!document.activeElement?.closest('.tm-spreadsheet-canvas-grid')"),
            "closing the hyperlink dialog restores focus to the grid");
    }

    [TestMethod]
    public async Task Phase1_NameManager_RowsAreKeyboardOperable()
    {
        var page = await OpenDesktopDemoAsync();

        await SelectToolbarTabAsync(page, "Data");
        await OpenNameManagerAsync(page);

        // An empty list has no roving stop, so the focus scope puts the initial focus on the New
        // button (declared as data-initial-focus on the scope root).
        var managerScope = page.Locator(".tm-spreadsheet-name-manager [role='dialog']").First;
        var initialFocusId = await managerScope.GetAttributeAsync("data-initial-focus");
        Assert.IsFalse(string.IsNullOrWhiteSpace(initialFocusId));
        var newButton = page.Locator($"[id='{initialFocusId}']");
        StringAssert.Contains(await newButton.TextContentAsync() ?? string.Empty, "New");
        await page.WaitForTimeoutAsync(300);
        Assert.IsTrue(await newButton.EvaluateAsync<bool>("el => el === document.activeElement"),
            "the focus scope moves the initial focus onto the New button");

        await newButton.ClickAsync();
        await page.Locator(".tm-spreadsheet-named-range-edit").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });
        await page.FillAsync("#nr-name", "KeyboardRange");
        await page.FillAsync("#nr-refers", "=Sheet1!C3:C9");
        await page.Locator(".tm-spreadsheet-named-range-edit__btn--ok").ClickAsync();
        await page.Locator(".tm-spreadsheet-named-range-edit").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden
        });

        var table = page.Locator(".tm-spreadsheet-name-manager__table");
        var row = table.Locator("tbody tr").First;
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // A roving tabindex needs exactly one tab stop: the first row is selected and is the stop.
        Assert.AreEqual("true", await row.GetAttributeAsync("aria-selected"),
            "the grid selects its first row");
        Assert.AreEqual("0", await row.GetAttributeAsync("tabindex"));

        // ↓ moves the selection AND the DOM focus with it (the list has one row, so it stays put).
        await row.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(200);
        Assert.IsTrue(await row.EvaluateAsync<bool>("el => el === document.activeElement"),
            "the roving tabindex keeps the DOM focus on the active row");

        // Enter edits the selected row — the editor opens on top of the manager.
        await page.Keyboard.PressAsync("Enter");
        var editor = page.Locator(".tm-spreadsheet-named-range-edit");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.AreEqual("KeyboardRange", await page.InputValueAsync("#nr-name"));

        await page.Keyboard.PressAsync("Escape");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });

        // The editor destroyed the focused node, so focus is re-stated on the manager before the
        // next Escape (returning focus to the opener is the TmModal/TmFocusScope contract of phase 3).
        await page.Locator(".tm-spreadsheet-name-manager").FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-spreadsheet-name-manager").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden
        });
    }

    [TestMethod]
    public async Task Phase1_FormatCells_CategoryListbox_IsKeyboardOperable()
    {
        var page = await OpenDesktopDemoAsync();
        var grid = DemoGrid(page);

        // Right-click a cell in the grid to open the context menu → Format Cells.
        await grid.ClickAsync(new LocatorClickOptions
        {
            Button = MouseButton.Right,
            Position = new() { X = 120, Y = 60 }
        });
        await DemoComponent(page).Locator(".tm-spreadsheet-context-menu__item", new() { HasTextString = "Format Cells" })
            .ClickAsync();

        var listbox = page.Locator(".tm-fcd__listbox[role='listbox']");
        await listbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var options = listbox.Locator("[role='option']");
        Assert.AreEqual(8, await options.CountAsync());
        Assert.AreEqual("true", await options.First.GetAttributeAsync("aria-selected"));
        Assert.AreEqual("0", await options.First.GetAttributeAsync("tabindex"),
            "the roving tabindex puts the single tab stop on the selected option");

        // The selected option owns the DOM focus, and ↓ moves both the selection and the focus.
        await options.First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(200);

        Assert.AreEqual("true", await options.Nth(1).GetAttributeAsync("aria-selected"),
            "↓ moves the listbox selection to the next category");
        Assert.AreEqual("false", await options.First.GetAttributeAsync("aria-selected"));
        Assert.IsTrue(await options.Nth(1).EvaluateAsync<bool>("el => el === document.activeElement"),
            "the roving tabindex must move the DOM focus with the selection");

        await SaveScreenshotAsync(page, "format-cells-category-listbox-desktop");

        await page.Keyboard.PressAsync("Escape");
        await listbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
    }

    [TestMethod]
    public async Task Phase1_ValidationError_IsAlertDialog_KeepsBackdropEscapeAndRefocusesGrid()
    {
        var page = await OpenDesktopDemoAsync();
        var grid = DemoGrid(page);

        await SpreadsheetE2ETests.SetWholeNumberValidationAsync(page, grid, "H6", "1", "10");

        // Enter an out-of-range value in the validated cell.
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = 300, Y = 200 }
        });
        await page.WaitForTimeoutAsync(200);
        await page.FillAsync(".tm-spreadsheet-formula-bar__ref", "H6");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);
        await page.Keyboard.TypeAsync("99");
        await page.Keyboard.PressAsync("Enter");

        var alert = page.Locator("[role='alertdialog']");
        await alert.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual("true", await alert.GetAttributeAsync("aria-modal"));

        var labelledBy = await alert.GetAttributeAsync("aria-labelledby");
        var describedBy = await alert.GetAttributeAsync("aria-describedby");
        Assert.IsFalse(string.IsNullOrWhiteSpace(labelledBy), "the alertdialog needs an accessible name");
        Assert.IsFalse(string.IsNullOrWhiteSpace(describedBy), "the alertdialog needs a description");
        Assert.IsFalse(string.IsNullOrWhiteSpace(await page.Locator($"[id='{labelledBy}']").TextContentAsync()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(await page.Locator($"[id='{describedBy}']").TextContentAsync()));

        // Focus moved to the primary action.
        var primaryId = await page.EvaluateAsync<string>(
            "() => document.activeElement?.closest('.tm-spreadsheet-alert__actions') ? 'primary' : (document.activeElement?.className || '')");
        Assert.AreEqual("primary", primaryId, "an alertdialog focuses its primary action");

        await SaveScreenshotAsync(page, "validation-error-desktop");

        // A Stop alert ignores a backdrop click (it is a hard block).
        await page.Mouse.ClickAsync(20, 20);
        await page.WaitForTimeoutAsync(300);
        Assert.IsTrue(await alert.IsVisibleAsync(), "a Stop alert must not close on a backdrop click");

        // Escape closes it and focus returns to the grid.
        await page.Keyboard.PressAsync("Escape");
        await alert.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        await page.WaitForTimeoutAsync(300);

        var activeIsGrid = await page.EvaluateAsync<bool>(
            "() => !!document.activeElement?.closest('.tm-spreadsheet-canvas-grid')");
        Assert.IsTrue(activeIsGrid, "closing the alert must return focus to the grid");
    }

    [TestMethod]
    public async Task Phase1_NameManagerDialog_LongList_KeepsTheFooterInsideAShortViewport()
    {
        // A 30-row list in a 500px-tall viewport: the card must cap itself to the viewport and scroll
        // its middle, or the Close button ends up below the fold and the dialog is a keyboard trap.
        var page = await CreatePageAsync();
        await page.SetViewportSizeAsync(1440, 500);
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);
        await DemoGrid(page).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        await page.WaitForTimeoutAsync(500);

        await SelectToolbarTabAsync(page, "Data");
        await OpenNameManagerAsync(page);

        for (var i = 1; i <= 30; i++)
        {
            // "Range01" would be rejected: a name that is letters followed by digits looks like a cell
            // reference to SpreadsheetNamedRange.IsValidName. The underscore keeps it a real name.
            await CreateNamedRangeAsync(page, $"Range_{i:00}", $"=Sheet1!A{i + 1}");
        }

        var dialog = page.Locator(".tm-spreadsheet-name-manager");
        Assert.AreEqual(30, await dialog.Locator("tbody tr").CountAsync());

        var closeButton = dialog.Locator(".tm-spreadsheet-name-manager__btn--close");
        var box = await closeButton.BoundingBoxAsync();
        var viewport = page.ViewportSize!;
        Assert.IsNotNull(box, "the Close button must be rendered");
        Assert.IsTrue(box!.Y >= 0 && box.Y + box.Height <= viewport.Height,
            $"the footer must stay inside the viewport; close button at {box.Y}..{box.Y + box.Height} of {viewport.Height}");

        var cardBox = await dialog.BoundingBoxAsync();
        Assert.IsTrue(cardBox!.Height <= viewport.Height,
            $"the card must not exceed the viewport height, was {cardBox.Height} of {viewport.Height}");

        // The middle scrolls, and the sticky header still exists inside it.
        var scroller = page.Locator(".tm-spreadsheet-name-manager .tm-modal-body");
        var scrollRange = await scroller.EvaluateAsync<double>("el => el.scrollHeight - el.clientHeight");
        Assert.IsTrue(scrollRange > 0, "a 30-row list must overflow its scroller in a 500px viewport");

        await SaveScreenshotAsync(page, "name-manager-dialog-long-list-short-viewport-desktop");

        await page.Keyboard.PressAsync("Escape");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", ScreenshotDirectory);
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(directory, fileName + ".png"),
            FullPage = false
        });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
