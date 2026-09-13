using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests covering table blocks in the Notion editor.
/// Phase 8: Table rendering, cell editing, navigation, row/column CRUD,
/// header row/column toggles, and row drag-drop reordering.
/// </summary>
[TestClass]
public class NotionTableE2ETests : WasmTestBase
{
    private const string TablePageSeedEndpoint = "https://localhost:5100/api/notion/e2e/seed/seedTablePage";

    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> OpenNotionEditorAsync()
    {
        using var http = new HttpClient();
        try { await http.PostAsync("https://localhost:5100/api/notion/reset", null); }
        catch { /* API may not be running; ignore */ }

        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(2000);
        return page;
    }

    private ILocator TableBlock(IPage page) =>
        page.Locator("[data-block-type='Table']").First;

    private ILocator TableRows(IPage page) =>
        page.Locator(".tm-notion-table tbody tr");

    private ILocator TableCells(IPage page) =>
        page.Locator(".tm-notion-table__cell[contenteditable='true']");

    private ILocator CellAt(IPage page, int rowIndex, int colIndex) =>
        page.Locator($".tm-notion-table tbody tr:nth-child({rowIndex + 1}) .tm-notion-table__cell-td:nth-child({colIndex + 2}) .tm-notion-table__cell[contenteditable='true']").First;

    private async Task SeedTablePageAsync(IPage page)
    {
        using var http = new HttpClient();
        using var response = await http.PostAsync(TablePageSeedEndpoint, null);
        response.EnsureSuccessStatusCode();

        await page.ReloadAsync(new PageReloadOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync("[data-block-id='eb700000-0000-0000-0000-000000000010'] .tm-notion-table", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    private async Task CaptureBaselineAsync(IPage page, string state, ILocator region)
    {
        var outputDir = BaselineOutput.DirectoryFor(TestContext, "notion", "tables");
        outputDir = Path.GetFullPath(outputDir);
        Directory.CreateDirectory(outputDir);

        var fullPath = Path.Combine(outputDir, $"{state}.png");
        var regionPath = Path.Combine(outputDir, $"{state}.region.png");

        await page.WaitForTimeoutAsync(250);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            Type = ScreenshotType.Png,
            FullPage = true
        });
        await region.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = regionPath,
            Type = ScreenshotType.Png,
            OmitBackground = false
        });

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(regionPath);
    }

    private static async Task AssertNoHorizontalPageOverflowAsync(IPage page)
    {
        var hasOverflow = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.IsFalse(hasOverflow, "Wide Notion table must scroll inside its wrapper without creating page-level horizontal overflow.");
    }

    private static ILocator TableCell(ILocator tableBlock, int rowIndex, int columnIndex) =>
        tableBlock.Locator($"[data-tm-row='{rowIndex}'][data-tm-col='{columnIndex}']").First;

    private static async Task DragSelectCellsAsync(IPage page, ILocator startCell, ILocator endCell)
    {
        var start = await startCell.BoundingBoxAsync();
        var end = await endCell.BoundingBoxAsync();
        Assert.IsNotNull(start, "Start table cell should have a visible bounding box.");
        Assert.IsNotNull(end, "End table cell should have a visible bounding box.");

        await page.Mouse.MoveAsync((float)(start.X + start.Width / 2), (float)(start.Y + start.Height / 2));
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(end.X + end.Width / 2), (float)(end.Y + end.Height / 2), new MouseMoveOptions
        {
            Steps = 12
        });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(250);
    }

    private async Task CaptureBaselineAndAssertAsync(IPage page, string state, ILocator region)
    {
        await region.ScrollIntoViewIfNeededAsync();
        await CaptureBaselineAsync(page, state, region);

        var outputDir = BaselineOutput.DirectoryFor(TestContext, "notion", "tables");
        Assert.IsTrue(File.Exists(Path.Combine(outputDir, $"{state}.png")), $"CF11 full-page baseline should be written for {state}.");
        Assert.IsTrue(File.Exists(Path.Combine(outputDir, $"{state}.region.png")), $"CF11 region baseline should be written for {state}.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  EB7 screenshot recovery
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("EB7 captures desktop table, wide table, empty table, and long-cell wrapping baselines.")]
    public async Task EB7_TableDesktopWideEmptyAndLongContent_CaptureBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedTablePageAsync(page);
        await page.SetViewportSizeAsync(1366, 768);

        var desktopTable = page.Locator("[data-block-id='eb700000-0000-0000-0000-000000000010']").First;
        await desktopTable.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await Assertions.Expect(desktopTable.Locator(".tm-notion-table__cell").First).ToHaveCSSAsync("white-space", "pre-wrap");
        await Assertions.Expect(desktopTable.Locator(".tm-notion-table__cell-td").First).ToHaveCSSAsync("border-top-style", "solid");
        await Assertions.Expect(desktopTable).ToContainTextAsync("intentionally long enough to wrap");
        await CaptureBaselineAsync(page, "desktop-table", desktopTable);
        await CaptureBaselineAsync(page, "long-cell-content-desktop", desktopTable);

        var wideTable = page.Locator("[data-block-id='eb700000-0000-0000-0000-000000000020']").First;
        await wideTable.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(wideTable.Locator(".tm-notion-table__cell-td").First).ToHaveCSSAsync("border-top-style", "solid");
        await CaptureBaselineAsync(page, "desktop-wide-table", wideTable);

        var emptyTable = page.Locator("[data-block-id='eb700000-0000-0000-0000-000000000030']").First;
        await emptyTable.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(emptyTable.Locator(".tm-notion-table-block__empty-grid")).ToBeVisibleAsync();
        await Assertions.Expect(emptyTable.Locator(".tm-notion-table-block__add-col")).ToBeVisibleAsync();
        await Assertions.Expect(emptyTable.Locator(".tm-notion-table-block__add-row")).ToBeVisibleAsync();
        await CaptureBaselineAsync(page, "empty-table-desktop", emptyTable);

        await AssertNoHorizontalPageOverflowAsync(page);
    }

    [TestMethod]
    [Description("EB7 captures mobile horizontal scroll for a wide Notion table.")]
    public async Task EB7_TableMobileHorizontalScroll_CaptureBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await page.SetViewportSizeAsync(390, 844);
        await SeedTablePageAsync(page);

        var wideTable = page.Locator("[data-block-id='eb700000-0000-0000-0000-000000000020']").First;
        await wideTable.ScrollIntoViewIfNeededAsync();
        var wrapper = wideTable.Locator(".tm-notion-table-block__wrapper").First;
        await wrapper.EvaluateAsync("element => element.scrollLeft = element.scrollWidth");
        await page.WaitForTimeoutAsync(250);

        var scrollMetrics = await wrapper.EvaluateAsync<ScrollMetrics>("element => ({ scrollLeft: element.scrollLeft, scrollWidth: element.scrollWidth, clientWidth: element.clientWidth })");
        Assert.IsTrue(scrollMetrics.ScrollWidth > scrollMetrics.ClientWidth, "Wide table should overflow inside its wrapper on mobile.");
        Assert.IsTrue(scrollMetrics.ScrollLeft > 0, "Mobile baseline should capture the horizontally scrolled state.");

        await CaptureBaselineAsync(page, "mobile-wide-table-horizontal-scroll", wideTable);
        await AssertNoHorizontalPageOverflowAsync(page);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Phase 4 verifies historical table HTML and CSS are sanitized before browser render.")]
    public async Task Phase4_HistoricalUnsafeTableCell_IsSafeAndReadable()
    {
        var page = await OpenNotionEditorAsync();
        await SeedTablePageAsync(page);
        await page.SetViewportSizeAsync(1366, 768);

        var securityTable = page
            .Locator("[data-block-id='cf120000-0000-0000-0000-000000000010']")
            .First;
        await securityTable.ScrollIntoViewIfNeededAsync();
        await securityTable.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var cell = securityTable.Locator(".tm-notion-table__cell").First;
        await Assertions.Expect(cell).ToContainTextAsync("Safe historical content remains visible");
        Assert.AreEqual(0, await cell.Locator("img").CountAsync(), "Unsafe historical elements must not enter the DOM.");
        Assert.IsFalse(
            await page.EvaluateAsync<bool>("() => window.__notionXssTriggered === true"),
            "The historical onerror payload must never execute.");
        var style = await securityTable.Locator(".tm-notion-table__cell-td").First.GetAttributeAsync("style");
        Assert.IsTrue(string.IsNullOrEmpty(style), "Unsafe historical CSS must be dropped completely.");

        await CaptureBaselineAndAssertAsync(page, "phase4-historical-cell-sanitized", securityTable);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CF11 screenshot recovery
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("CF11 captures merged cells, split state, colored/header cells, sorted column state, and mobile horizontal scroll.")]
    public async Task CF11_AdvancedTableMergedSplitColorSortAndMobile_CaptureBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedTablePageAsync(page);
        await page.SetViewportSizeAsync(1366, 768);

        var advancedTable = page.Locator("[data-block-id='cf110000-0000-0000-0000-000000000010']").First;
        await advancedTable.ScrollIntoViewIfNeededAsync();
        await advancedTable.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var mergedHeader = TableCell(advancedTable, 0, 0);
        await Assertions.Expect(mergedHeader).ToHaveAttributeAsync("colspan", "2");
        await Assertions.Expect(TableCell(advancedTable, 1, 2)).ToHaveAttributeAsync("rowspan", "2");
        await Assertions.Expect(advancedTable.Locator(".tm-notion-table-row--header .tm-notion-table__cell").First).ToHaveCSSAsync("font-weight", "600");
        await Assertions.Expect(advancedTable.Locator(".tm-notion-table__cell-td--header-col").First).ToBeVisibleAsync();
        await Assertions.Expect(advancedTable.Locator("[style*='rgba']").First).ToBeVisibleAsync();

        await DragSelectCellsAsync(page, TableCell(advancedTable, 1, 0), TableCell(advancedTable, 3, 0));
        await advancedTable.Locator("button[title='Sort column']").ClickAsync();
        await page.WaitForTimeoutAsync(500);
        await Assertions.Expect(advancedTable.Locator(".tm-notion-table-row").Nth(1)).ToContainTextAsync("Discovery");
        await CaptureBaselineAndAssertAsync(page, "cf11-advanced-merged-colored-header-sorted", advancedTable);

        await DragSelectCellsAsync(page, TableCell(advancedTable, 1, 2), TableCell(advancedTable, 2, 3));
        await advancedTable.Locator("button[title='Split cells']").ClickAsync();
        await page.WaitForTimeoutAsync(500);
        await Assertions.Expect(TableCell(advancedTable, 1, 2)).ToHaveAttributeAsync("rowspan", "1");
        await Assertions.Expect(TableCell(advancedTable, 2, 2)).ToBeVisibleAsync();
        await Assertions.Expect(TableCell(advancedTable, 2, 2).Locator(".tm-notion-table__cell")).ToHaveTextAsync(string.Empty);
        await Assertions.Expect(TableCell(advancedTable, 2, 3)).ToContainTextAsync("Ivan");
        await CaptureBaselineAndAssertAsync(page, "cf11-split-unmerged-state", advancedTable);

        page = await OpenNotionEditorAsync();
        await page.SetViewportSizeAsync(390, 844);
        await SeedTablePageAsync(page);

        var mobileTable = page.Locator("[data-block-id='cf110000-0000-0000-0000-000000000010']").First;
        await mobileTable.ScrollIntoViewIfNeededAsync();
        var wrapper = mobileTable.Locator(".tm-notion-table-block__wrapper").First;
        await wrapper.EvaluateAsync("element => element.scrollLeft = element.scrollWidth");
        await page.WaitForTimeoutAsync(250);

        var scrollMetrics = await wrapper.EvaluateAsync<ScrollMetrics>("element => ({ scrollLeft: element.scrollLeft, scrollWidth: element.scrollWidth, clientWidth: element.clientWidth })");
        Assert.IsTrue(scrollMetrics.ScrollWidth > scrollMetrics.ClientWidth, "CF11 advanced table should scroll inside its wrapper on mobile.");
        Assert.IsTrue(scrollMetrics.ScrollLeft > 0, "CF11 mobile baseline should capture the horizontally scrolled merged table.");
        await Assertions.Expect(TableCell(mobileTable, 0, 0)).ToHaveAttributeAsync("colspan", "2");

        await CaptureBaselineAndAssertAsync(page, "cf11-mobile-merged-horizontal-scroll", mobileTable);
        await AssertNoHorizontalPageOverflowAsync(page);
    }

    private sealed class ScrollMetrics
    {
        public double ScrollLeft { get; set; }
        public double ScrollWidth { get; set; }
        public double ClientWidth { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Rendering
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Default table block renders with expected rows and columns")]
    public async Task Table_Renders_WithDefaultRowsAndCols()
    {
        var page = await OpenNotionEditorAsync();
        var table = TableBlock(page);
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var rows = TableRows(page);
        var rowCount = await rows.CountAsync();
        Assert.AreEqual(3, rowCount, "Default table should have 3 rows (header + 2 data)");

        // Each row should have 3 data cells
        var firstRowCells = rows.First.Locator(".tm-notion-table__cell-td");
        Assert.AreEqual(3, await firstRowCells.CountAsync(), "Each row should have 3 columns");

        // Verify header row styling is applied
        var tableEl = table.Locator(".tm-notion-table").First;
        var tableClasses = await tableEl.GetAttributeAsync("class");
        Assert.IsTrue(tableClasses?.Contains("tm-notion-table--has-header-row") == true,
            "Table should have header-row class by default");

        await TakeScreenshotAsync(page, "table_default_render");
    }

    [TestMethod]
    [Description("Clicking a table cell focuses it and makes it editable")]
    public async Task Table_CellClick_BecomesEditable()
    {
        var page = await OpenNotionEditorAsync();
        var table = TableBlock(page);
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var cell = CellAt(page, 1, 0); // second row, first column
        await cell.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await cell.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var activeElement = await page.EvaluateAsync<string>("() => document.activeElement?.className || ''");
        Assert.IsTrue(activeElement.Contains("tm-notion-table__cell"),
            "Active element should be the table cell after click");

        await TakeScreenshotAsync(page, "table_cell_focus");
    }

    [TestMethod]
    [Description("Typing text into a cell and blurring saves the content")]
    public async Task Table_CellType_SavesContent()
    {
        var page = await OpenNotionEditorAsync();
        var table = TableBlock(page);
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        // Add a new row so we have an empty cell to type into
        var addRowBtn = table.Locator(".tm-notion-table-block__add-row").First;
        await addRowBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await addRowBtn.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var rows = TableRows(page);
        var newRowIndex = await rows.CountAsync() - 1;
        var cell = CellAt(page, newRowIndex, 0);
        await cell.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await cell.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var testText = "E2E Test";
        await page.Keyboard.TypeAsync(testText);
        await page.WaitForTimeoutAsync(300);

        // Blur the cell by pressing Escape
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(500);

        // Verify the text is still in the cell
        var cellText = await cell.InnerTextAsync();
        Assert.AreEqual(testText, cellText, "Cell should contain the typed text after blur");

        await TakeScreenshotAsync(page, "table_cell_typed");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Keyboard navigation
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Pressing Tab in a cell moves focus to the next cell")]
    public async Task Table_Tab_MovesFocusToNextCell()
    {
        var page = await OpenNotionEditorAsync();
        var table = TableBlock(page);
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var firstCell = CellAt(page, 1, 0);
        await firstCell.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await firstCell.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Press Tab to move to next cell
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(400);

        var activeDataCol = await page.EvaluateAsync<int>("""
            () => {
                const el = document.activeElement;
                if (!el) return -1;
                const td = el.closest('td');
                return td ? parseInt(td.dataset.tmCol || '-1', 10) : -1;
            }
            """);

        Assert.AreEqual(1, activeDataCol, "Tab should move focus to the next cell (column 1)");
        await TakeScreenshotAsync(page, "table_tab_nav");
    }

    [TestMethod]
    [Description("Pressing Shift+Tab in a cell moves focus to the previous cell")]
    public async Task Table_ShiftTab_MovesFocusToPrevCell()
    {
        var page = await OpenNotionEditorAsync();
        var table = TableBlock(page);
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var secondCell = CellAt(page, 1, 1);
        await secondCell.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await secondCell.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Press Shift+Tab to move to previous cell
        await page.Keyboard.PressAsync("Shift+Tab");
        await page.WaitForTimeoutAsync(400);

        var activeDataCol = await page.EvaluateAsync<int>("""
            () => {
                const el = document.activeElement;
                if (!el) return -1;
                const td = el.closest('td');
                return td ? parseInt(td.dataset.tmCol || '-1', 10) : -1;
            }
            """);

        Assert.AreEqual(0, activeDataCol, "Shift+Tab should move focus to the previous cell (column 0)");
        await TakeScreenshotAsync(page, "table_shift_tab_nav");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Row & Column CRUD
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the add-row button inserts a new row")]
    public async Task Table_AddRow_Button_AddsRow()
    {
        var page = await OpenNotionEditorAsync();
        var table = TableBlock(page);
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var rowsBefore = await TableRows(page).CountAsync();

        var addRowBtn = table.Locator(".tm-notion-table-block__add-row").First;
        await addRowBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await addRowBtn.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var rowsAfter = await TableRows(page).CountAsync();
        Assert.AreEqual(rowsBefore + 1, rowsAfter, "Row count should increase by 1 after clicking add row");
        await TakeScreenshotAsync(page, "table_add_row");
    }

    [TestMethod]
    [Description("Clicking the add-column button inserts a new column")]
    public async Task Table_AddColumn_Button_AddsColumn()
    {
        var page = await OpenNotionEditorAsync();
        var table = TableBlock(page);
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var rows = TableRows(page);
        var colsBefore = await rows.First.Locator(".tm-notion-table__cell-td").CountAsync();

        var addColBtn = table.Locator(".tm-notion-table-block__add-col").First;
        await addColBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await addColBtn.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var colsAfter = await rows.First.Locator(".tm-notion-table__cell-td").CountAsync();
        Assert.AreEqual(colsBefore + 1, colsAfter, "Column count should increase by 1 after clicking add column");
        await TakeScreenshotAsync(page, "table_add_col");
    }

    [TestMethod]
    [Description("Clicking the delete-row button removes the hovered row")]
    public async Task Table_DeleteRow_RemovesRow()
    {
        var page = await OpenNotionEditorAsync();
        var table = TableBlock(page);
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var rows = TableRows(page);
        var rowsBefore = await rows.CountAsync();
        Assert.IsTrue(rowsBefore >= 2, "Need at least 2 rows to test delete");

        // Hover over the last data row to reveal the delete button
        var lastRow = rows.Last;
        await lastRow.HoverAsync();
        await page.WaitForTimeoutAsync(400);

        var deleteBtn = lastRow.Locator(".tm-notion-table__row-delete").First;
        await deleteBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await deleteBtn.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var rowsAfter = await TableRows(page).CountAsync();
        Assert.AreEqual(rowsBefore - 1, rowsAfter, "Row count should decrease by 1 after deleting a row");
        await TakeScreenshotAsync(page, "table_delete_row");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Header row / column toggles
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Toggling header row off removes the header-row CSS class")]
    public async Task Table_HeaderRow_Toggle_AppliesStyle()
    {
        var page = await OpenNotionEditorAsync();
        var table = TableBlock(page);
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var tableEl = table.Locator(".tm-notion-table").First;
        var classesBefore = await tableEl.GetAttributeAsync("class");
        Assert.IsTrue(classesBefore?.Contains("tm-notion-table--has-header-row") == true,
            "Table should initially have header-row class");

        // Click the header-row toggle button
        var toggleBtn = table.Locator(".tm-notion-table-block__toggle").First;
        await toggleBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await toggleBtn.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var classesAfter = await tableEl.GetAttributeAsync("class");
        Assert.IsTrue(classesAfter?.Contains("tm-notion-table--has-header-row") != true,
            "Table should NOT have header-row class after toggle");

        await TakeScreenshotAsync(page, "table_header_row_toggle");
    }

    [TestMethod]
    [Description("Toggling header column on adds the header-col CSS class to cells")]
    public async Task Table_HeaderColumn_Toggle_AppliesStyle()
    {
        var page = await OpenNotionEditorAsync();
        var table = TableBlock(page);
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var tableEl = table.Locator(".tm-notion-table").First;
        var classesBefore = await tableEl.GetAttributeAsync("class");
        Assert.IsTrue(classesBefore?.Contains("tm-notion-table--has-header-col") != true,
            "Table should NOT initially have header-col class");

        // Click the header-column toggle button (second toggle)
        var toggleBtns = table.Locator(".tm-notion-table-block__toggle");
        Assert.IsTrue(await toggleBtns.CountAsync() >= 2, "There should be 2 toggle buttons");
        var headerColToggle = toggleBtns.Nth(1);
        await headerColToggle.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var classesAfter = await tableEl.GetAttributeAsync("class");
        Assert.IsTrue(classesAfter?.Contains("tm-notion-table--has-header-col") == true,
            "Table should have header-col class after toggle");

        await TakeScreenshotAsync(page, "table_header_col_toggle");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Drag & drop reorder
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Row drag handle is visible on hover and has draggable attribute")]
    public async Task Table_RowDragHandle_VisibleAndDraggable()
    {
        var page = await OpenNotionEditorAsync();
        var table = TableBlock(page);
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var rows = TableRows(page);
        var rowCount = await rows.CountAsync();
        Assert.IsTrue(rowCount >= 2, "Need at least 2 rows to test drag handle");

        var firstRow = rows.First;

        // Hover over the row to reveal the drag handle
        await firstRow.HoverAsync();
        await page.WaitForTimeoutAsync(400);

        var handle = firstRow.Locator(".tm-notion-table__drag-icon").First;
        await handle.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        // Verify the handle is in the DOM and has draggable attribute
        var draggableAttr = await handle.GetAttributeAsync("draggable");
        Assert.AreEqual("true", draggableAttr, "Drag handle should have draggable='true'");

        // Verify the handle has a bounding box (is rendered)
        var box = await handle.BoundingBoxAsync();
        Assert.IsNotNull(box, "Drag handle should have a bounding box");
        Assert.IsTrue(box.Width > 0 && box.Height > 0, "Drag handle should have positive dimensions");

        await TakeScreenshotAsync(page, "table_row_drag_handle");
    }
}
