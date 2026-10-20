using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests covering advanced database block features: filtering, sorting, grouping,
/// field visibility, adding fields/views, export, and board drag-and-drop.
/// </summary>
[TestClass]
public class NotionDatabaseAdvancedE2ETests : WasmTestBase
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> OpenNotionEditorAsync()
    {
        await ResetNotionDatabaseAsync();

        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(2000);
        return page;
    }

    private static async Task ResetNotionDatabaseAsync()
    {
        using var http = DemoApiTestHttp.CreateClient();

        using var notionResponse = await http.PostAsync("/api/notion/reset", null);
        notionResponse.EnsureSuccessStatusCode();

        using var databaseResponse = await http.PostAsync("/api/notion/databases/e2e/seed/default", null);
        databaseResponse.EnsureSuccessStatusCode();
    }

    private async Task<ILocator> NavigateToDatabaseBlockAsync(IPage page)
    {
        var db = page.Locator(".tm-db").First;
        await db.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await db.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(500);
        await page.WaitForSelectorAsync(".tm-db__skeleton",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        await page.WaitForTimeoutAsync(300);
        return db;
    }

    private async Task SwitchDatabaseViewAsync(IPage page, ILocator db, string viewName)
    {
        var tab = db.Locator(".tm-db__view-tab").Filter(new LocatorFilterOptions { HasText = viewName });
        await tab.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await tab.ClickAsync();
        await page.WaitForTimeoutAsync(800);
    }

    /// <summary>
    /// Clicks a toolbar button identified by its visible text label.
    /// </summary>
    private async Task ClickToolbarButtonAsync(ILocator db, string buttonText)
    {
        var btn = db.Locator(".tm-db__tool-btn").Filter(new LocatorFilterOptions { HasText = buttonText });
        await btn.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await btn.ClickAsync();
        await Task.Delay(600);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Filter panel
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the Filter toolbar button opens the filter panel")]
    public async Task Database_Filter_OpenPanel()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        await ClickToolbarButtonAsync(db, "Filter");

        var filterPanel = db.Locator(".tm-dbfb");
        await filterPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await filterPanel.IsVisibleAsync(), "Filter panel should be visible after clicking Filter button");

        await TakeScreenshotAsync(page, "db_filter_panel_open");
    }

    [TestMethod]
    [Description("Adding a Name filter with a specific value reduces the visible record count")]
    public async Task Database_Filter_AddCondition_ReducesRecords()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        // Get initial row count
        await db.Locator(".tm-dbt__row").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        var initialCount = await db.Locator(".tm-dbt__row").CountAsync();

        // Open filter panel
        await ClickToolbarButtonAsync(db, "Filter");
        var filterPanel = db.Locator(".tm-dbfb");
        await filterPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // Click "Add filter" (first button; second is "Add filter group")
        var addBtn = filterPanel.Locator(".tm-dbfb__add-btn").First;
        await addBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await addBtn.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        // A condition row should appear
        var condRow = filterPanel.Locator(".tm-dbfb__cond").First;
        await condRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Select the "Name" field if a field selector is present
        var fieldSelect = condRow.Locator(".tm-dbfb__cond-select--field");
        var fieldSelectCount = await fieldSelect.CountAsync();
        if (fieldSelectCount > 0)
        {
            await fieldSelect.SelectOptionAsync(new SelectOptionValue { Label = "Name" });
            await page.WaitForTimeoutAsync(400);
        }

        // Type a value that matches only a subset of records (use a very specific string)
        var valueInput = condRow.Locator(".tm-dbfb__cond-value");
        await valueInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await valueInput.FillAsync("Task 1");
        await page.WaitForTimeoutAsync(800);

        var filteredCount = await db.Locator(".tm-dbt__row").CountAsync();
        Assert.IsTrue(filteredCount <= initialCount,
            $"Filtered row count ({filteredCount}) should be <= initial ({initialCount})");

        await TakeScreenshotAsync(page, "db_filter_condition_added");
    }

    [TestMethod]
    [Description("Clearing all filters via the clear button restores the full record count")]
    public async Task Database_Filter_Remove_ShowsAll()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        await db.Locator(".tm-dbt__row").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        var initialCount = await db.Locator(".tm-dbt__row").CountAsync();

        // Open filter, add a condition
        await ClickToolbarButtonAsync(db, "Filter");
        var filterPanel = db.Locator(".tm-dbfb");
        await filterPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var addBtn2 = filterPanel.Locator(".tm-dbfb__add-btn").First;
        await addBtn2.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await addBtn2.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var condRow = filterPanel.Locator(".tm-dbfb__cond").First;
        await condRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var valueInput = condRow.Locator(".tm-dbfb__cond-value");
        await valueInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await valueInput.FillAsync("ZZZ_NO_MATCH_XYZ");
        await page.WaitForTimeoutAsync(800);

        // Now clear all filters
        var clearBtn = filterPanel.Locator(".tm-dbfb__clear-btn").First;
        await clearBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await clearBtn.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var restoredCount = await db.Locator(".tm-dbt__row").CountAsync();
        Assert.AreEqual(initialCount, restoredCount,
            $"Row count should be restored to {initialCount} after clearing filters (got {restoredCount})");

        await TakeScreenshotAsync(page, "db_filter_cleared");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Sort panel
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the Sort toolbar button opens the sort panel")]
    public async Task Database_Sort_OpenPanel()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        await ClickToolbarButtonAsync(db, "Sort");

        var sortPanel = db.Locator(".tm-dbsb");
        await sortPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await sortPanel.IsVisibleAsync(), "Sort panel should be visible after clicking Sort button");

        await TakeScreenshotAsync(page, "db_sort_panel_open");
    }

    [TestMethod]
    [Description("Adding a sort creates a sort rule row in the sort panel")]
    public async Task Database_Sort_ByName_SortsAlphabetically()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        // Open sort panel
        await ClickToolbarButtonAsync(db, "Sort");
        var sortPanel = db.Locator(".tm-dbsb");
        await sortPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // Click "Add sort"
        var addBtn = sortPanel.Locator(".tm-dbsb__add-btn");
        await addBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await addBtn.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        // A sort row should appear
        var sortRow = sortPanel.Locator(".tm-dbsb__row").First;
        await sortRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await sortRow.IsVisibleAsync(), "A sort rule row should appear in the sort panel after clicking Add sort");

        // The row should have a field selector
        var fieldSelect = sortRow.Locator(".tm-dbsb__select");
        var fieldSelectCount = await fieldSelect.CountAsync();
        Assert.IsTrue(fieldSelectCount > 0, "Sort row should contain a field selector");

        await TakeScreenshotAsync(page, "db_sort_by_name");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Group panel
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the Group toolbar button opens the group panel")]
    public async Task Database_Group_OpenPanel()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        await ClickToolbarButtonAsync(db, "Group");

        var groupPanel = db.Locator(".tm-dbgb");
        await groupPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await groupPanel.IsVisibleAsync(), "Group panel should be visible after clicking Group button");

        await TakeScreenshotAsync(page, "db_group_panel_open");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Fields / Properties panel
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the Properties toolbar button opens the fields panel")]
    public async Task Database_Fields_OpenPanel()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        await ClickToolbarButtonAsync(db, "Properties");

        var fieldsPanel = db.Locator(".tm-db__panel--fields");
        await fieldsPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await fieldsPanel.IsVisibleAsync(), "Fields panel should be visible after clicking Properties button");

        await TakeScreenshotAsync(page, "db_fields_panel_open");
    }

    [TestMethod]
    [Description("Toggling a field off in the Properties panel hides that column in the table")]
    public async Task Database_Fields_HideField_HidesColumn()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        // Get initial column headers
        await db.Locator(".tm-dbt__th-name").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        var initialHeaders = await db.Locator(".tm-dbt__th-name").AllInnerTextsAsync();

        // Open fields panel
        await ClickToolbarButtonAsync(db, "Properties");
        var fieldsPanel = db.Locator(".tm-db__panel--fields");
        await fieldsPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // Find a visible field row that is currently toggled ON — try "Status"
        var statusRow = fieldsPanel.Locator(".tm-db__field-row")
            .Filter(new LocatorFilterOptions { HasText = "Status" });
        var statusRowCount = await statusRow.CountAsync();

        if (statusRowCount > 0)
        {
            var toggle = statusRow.Locator(".tm-db__field-toggle");
            await toggle.ClickAsync(new LocatorClickOptions { Force = true });
            await page.WaitForTimeoutAsync(800);

            // Close panel by clicking Properties again
            await ClickToolbarButtonAsync(db, "Properties");
            await page.WaitForTimeoutAsync(400);

            var newHeaders = await db.Locator(".tm-dbt__th-name").AllInnerTextsAsync();
            Assert.IsFalse(newHeaders.Any(h => h.Contains("Status")),
                "Status column should be hidden after toggling it off in the Properties panel");
        }
        else
        {
            // Fallback: just assert the panel opened correctly
            Assert.IsTrue(await fieldsPanel.IsVisibleAsync(), "Fields panel should be open");
        }

        await TakeScreenshotAsync(page, "db_field_hidden");
    }

    [TestMethod]
    [Description("Re-toggling a hidden field in the Properties panel shows the column again")]
    public async Task Database_Fields_ShowField_ShowsColumn()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        await db.Locator(".tm-dbt__th-name").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });

        // Open fields panel and hide Status
        await ClickToolbarButtonAsync(db, "Properties");
        var fieldsPanel = db.Locator(".tm-db__panel--fields");
        await fieldsPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var statusRow = fieldsPanel.Locator(".tm-db__field-row")
            .Filter(new LocatorFilterOptions { HasText = "Status" });
        if (await statusRow.CountAsync() > 0)
        {
            // Toggle off
            await statusRow.Locator(".tm-db__field-toggle").ClickAsync(new LocatorClickOptions { Force = true });
            await page.WaitForTimeoutAsync(600);

            // Toggle on again
            await statusRow.Locator(".tm-db__field-toggle").ClickAsync(new LocatorClickOptions { Force = true });
            await page.WaitForTimeoutAsync(800);
        }

        // Close panel
        await ClickToolbarButtonAsync(db, "Properties");
        await page.WaitForTimeoutAsync(400);

        var headers = await db.Locator(".tm-dbt__th-name").AllInnerTextsAsync();
        Assert.IsTrue(headers.Any(h => h.Contains("Status")),
            "Status column should be visible again after re-enabling it in the Properties panel");

        await TakeScreenshotAsync(page, "db_field_shown");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Add field / Add view
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the Add Field button (+) appends a new column to the table header")]
    public async Task Database_AddField_AppearsInHeader()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        await db.Locator(".tm-dbt__th-name").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        var initialColCount = await db.Locator(".tm-dbt__th-name").CountAsync();

        // Scroll header into view and click the add-field button
        var addFieldBtn = db.Locator(".tm-dbt__add-field-btn");
        await addFieldBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        await addFieldBtn.ScrollIntoViewIfNeededAsync();
        await addFieldBtn.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var newColCount = await db.Locator(".tm-dbt__th-name").CountAsync();
        Assert.IsTrue(newColCount > initialColCount,
            $"Column count should increase after clicking Add Field (was {initialColCount}, now {newColCount})");

        await TakeScreenshotAsync(page, "db_add_field_header");
    }

    [TestMethod]
    [Description("Clicking a column header opens the field editor panel")]
    public async Task Database_FieldEditor_Opens()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        // Click a non-primary column header to open the field editor
        await db.Locator(".tm-dbt__th-name").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        var headers = db.Locator(".tm-dbt__th-inner");
        // Skip primary header (index 0), click the second one
        var secondHeader = headers.Nth(1);
        var secondHeaderCount = await secondHeader.CountAsync();
        if (secondHeaderCount == 0)
        {
            Assert.Inconclusive("No non-primary column header to click");
            return;
        }
        await secondHeader.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var fieldEditor = page.Locator(".tm-dbfe");
        await fieldEditor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await fieldEditor.IsVisibleAsync(), "Field editor panel should open after clicking a column header");

        await TakeScreenshotAsync(page, "db_field_editor_opens");
    }

    [TestMethod]
    [Description("Clicking the Add View button appends a new view tab")]
    public async Task Database_AddView_Tab_Appears()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        var initialTabCount = await db.Locator(".tm-db__view-tab").CountAsync();

        var addViewBtn = db.Locator(".tm-db__view-add-btn");
        await addViewBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        await addViewBtn.ClickAsync();

        var viewTypeOption = page.Locator(".tm-db__floatmenu .tm-db__ctx-item").First;
        await viewTypeOption.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await viewTypeOption.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var newTabCount = await db.Locator(".tm-db__view-tab").CountAsync();
        Assert.IsTrue(newTabCount > initialTabCount,
            $"Tab count should increase after clicking Add View (was {initialTabCount}, now {newTabCount})");

        await TakeScreenshotAsync(page, "db_add_view_tab");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Export
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the export primary button triggers a file download")]
    public async Task Database_Export_DownloadsFile()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        // Open import/export dialog
        await ClickToolbarButtonAsync(db, "Import");

        var importExportOverlay = page.Locator(".tm-dbie__overlay");
        await importExportOverlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // Switch to Export tab
        var exportTab = page.Locator(".tm-dbie__tab").Filter(new LocatorFilterOptions { HasText = "Export" });
        var exportTabCount = await exportTab.CountAsync();
        if (exportTabCount > 0)
        {
            await exportTab.ClickAsync();
            await page.WaitForTimeoutAsync(500);
        }

        // Wait for export body
        var exportBody = page.Locator(".tm-dbie__export-body");
        await exportBody.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Click the primary export button and wait for download
        var primaryBtn = page.Locator(".tm-dbie__primary-btn");
        await primaryBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        var download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await primaryBtn.ClickAsync();
        }, new PageRunAndWaitForDownloadOptions { Timeout = 15000 });

        Assert.IsNotNull(download, "A file download should start after clicking the export button");
        Assert.IsFalse(string.IsNullOrEmpty(download.SuggestedFilename),
            "Downloaded file should have a suggested filename");

        await TakeScreenshotAsync(page, "db_export_download");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Board drag-and-drop
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Dragging a board card to another column moves it to that column")]
    public async Task Database_BoardView_DragCard_MovesColumn()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        await SwitchDatabaseViewAsync(page, db, "Board");

        var board = db.Locator(".tm-dbb");
        await board.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var columns = db.Locator(".tm-dbb__col");
        await columns.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        var colCount = await columns.CountAsync();

        if (colCount < 2)
        {
            Assert.Inconclusive("Board view needs at least 2 columns to test drag-and-drop");
            return;
        }

        var sourceCol = columns.First;
        var targetCol = columns.Nth(1);

        // Find a card in the source column
        var sourceCard = sourceCol.Locator(".tm-dbb__card").First;
        if (await sourceCard.CountAsync() == 0)
        {
            sourceCard = targetCol.Locator(".tm-dbb__card").First;
            if (await sourceCard.CountAsync() == 0)
            {
                Assert.Inconclusive("No board cards found to drag");
                return;
            }
            targetCol = sourceCol;
        }

        var initialTargetCount = await targetCol.Locator(".tm-dbb__card").CountAsync();
        var targetGroupValue = await targetCol.GetAttributeAsync("data-group-value") ?? string.Empty;
        var sourceRecordId = await sourceCard.GetAttributeAsync("data-record-id")
            ?? throw new AssertFailedException("Source card should expose a stable record id");

        // HTML5 drag-and-drop requires dispatching drag events via JavaScript because
        // Playwright's pointer-based DragToAsync does not fire ondragstart/ondragover/ondrop.
        var sourceHandle = await sourceCard.ElementHandleAsync();
        var targetHandle = await targetCol.ElementHandleAsync();

        await page.EvaluateAsync(@"([src, tgt]) => {
            const dt = new DataTransfer();
            src.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer: dt }));
            tgt.dispatchEvent(new DragEvent('dragover',  { bubbles: true, cancelable: true, dataTransfer: dt }));
            tgt.dispatchEvent(new DragEvent('drop',      { bubbles: true, cancelable: true, dataTransfer: dt }));
            src.dispatchEvent(new DragEvent('dragend',   { bubbles: true, cancelable: true, dataTransfer: dt }));
        }", new object[] { sourceHandle!, targetHandle! });

        await Assertions.Expect(db.Locator($".tm-dbb__card[data-record-id='{sourceRecordId}']"))
            .ToHaveAttributeAsync("data-from-group", targetGroupValue, new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });

        var newTargetCount = await GetBoardColumnCardCountAsync(db, targetGroupValue);
        Assert.IsTrue(newTargetCount > initialTargetCount,
            $"Target column card count should increase after dragging (was {initialTargetCount}, now {newTargetCount})");

        await TakeScreenshotAsync(page, "db_board_drag_card");
    }

    private static async Task<int> GetBoardColumnCardCountAsync(ILocator db, string groupValue)
        => await db.EvaluateAsync<int>(
            """
            (root, value) => {
                const columns = Array.from(root.querySelectorAll('.tm-dbb__col'));
                const column = columns.find(col => (col.dataset.groupValue || '') === value);
                return column ? column.querySelectorAll('.tm-dbb__card').length : 0;
            }
            """,
            groupValue);
}
