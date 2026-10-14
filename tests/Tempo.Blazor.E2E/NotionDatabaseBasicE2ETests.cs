using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests covering basic database block features: table/board/gallery/list/calendar/timeline
/// views, adding records, cell editing, and record detail modal.
/// </summary>
[TestClass]
public class NotionDatabaseBasicE2ETests : WasmTestBase
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

    /// <summary>
    /// Scrolls to the first database block on the page and waits for it to finish loading.
    /// </summary>
    private async Task<ILocator> NavigateToDatabaseBlockAsync(IPage page)
    {
        var db = page.Locator(".tm-db").First;
        await db.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await db.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(500);
        // Wait for loading skeleton to disappear (returns immediately if skeleton is absent)
        await page.WaitForSelectorAsync(".tm-db__skeleton",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        await page.WaitForTimeoutAsync(300);
        return db;
    }

    /// <summary>
    /// Clicks the named view tab and waits for the view to render.
    /// </summary>
    private async Task SwitchDatabaseViewAsync(IPage page, ILocator db, string viewName)
    {
        var tab = db.Locator(".tm-db__view-tab").Filter(new LocatorFilterOptions { HasText = viewName });
        await tab.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await tab.ClickAsync();
        await page.WaitForTimeoutAsync(800);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Table view – basic load
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Database block renders in table view mode after page load")]
    public async Task Database_TableView_LoadsData()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        var tableWrap = db.Locator(".tm-dbt-wrap");
        await tableWrap.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await tableWrap.IsVisibleAsync(), "Table view wrapper should be visible");

        await TakeScreenshotAsync(page, "db_table_view_loads");
    }

    [TestMethod]
    [Description("Table view shows Name, Status, and Priority column headers")]
    public async Task Database_TableView_ShowsFields()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        var firstHeader = db.Locator(".tm-dbt__th-name").First;
        await firstHeader.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });

        var texts = await db.Locator(".tm-dbt__th-name").AllInnerTextsAsync();
        Assert.IsTrue(texts.Any(t => t.Contains("Name")),     "Name column header should be visible");
        Assert.IsTrue(texts.Any(t => t.Contains("Status")),   "Status column header should be visible");
        Assert.IsTrue(texts.Any(t => t.Contains("Priority")), "Priority column header should be visible");

        await TakeScreenshotAsync(page, "db_table_view_fields");
    }

    [TestMethod]
    [Description("Table view shows at least one data record row")]
    public async Task Database_TableView_ShowsRecords()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        var rows = db.Locator(".tm-dbt__row");
        await rows.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        var rowCount = await rows.CountAsync();
        Assert.IsTrue(rowCount > 0, $"At least one record row should be visible (found {rowCount})");

        await TakeScreenshotAsync(page, "db_table_view_records");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  View switching
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Switching to Board view shows kanban columns")]
    public async Task Database_SwitchView_Board_ShowsKanban()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);
        await SwitchDatabaseViewAsync(page, db, "Board");

        var board = db.Locator(".tm-dbb");
        await board.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var columns = db.Locator(".tm-dbb__col");
        await columns.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        Assert.IsTrue(await columns.CountAsync() > 0, "Board view should show at least one kanban column");

        await TakeScreenshotAsync(page, "db_board_view");
    }

    [TestMethod]
    [Description("Switching to Gallery view shows gallery cards")]
    public async Task Database_SwitchView_Gallery_ShowsCards()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);
        await SwitchDatabaseViewAsync(page, db, "Gallery");

        var gallery = db.Locator(".tm-dbg");
        await gallery.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var cards = db.Locator(".tm-dbg__card");
        await cards.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        Assert.IsTrue(await cards.CountAsync() > 0, "Gallery view should show at least one card");

        await TakeScreenshotAsync(page, "db_gallery_view");
    }

    [TestMethod]
    [Description("Switching to List view shows the list view container")]
    public async Task Database_SwitchView_List_ShowsList()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);
        await SwitchDatabaseViewAsync(page, db, "List");

        var listView = db.Locator(".tm-dblv");
        await listView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await listView.IsVisibleAsync(), "List view container should be visible");

        await TakeScreenshotAsync(page, "db_list_view");
    }

    [TestMethod]
    [Description("Switching to Calendar view shows the calendar grid with a month title")]
    public async Task Database_SwitchView_Calendar_ShowsCalendar()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);
        await SwitchDatabaseViewAsync(page, db, "Calendar");

        var calendar = db.Locator(".tm-dbcal");
        await calendar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var monthTitle = db.Locator(".tm-dbcal__month-title");
        await monthTitle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await monthTitle.IsVisibleAsync(), "Calendar should display a month title");

        await TakeScreenshotAsync(page, "db_calendar_view");
    }

    [TestMethod]
    [Description("Switching to Timeline view shows timeline zoom controls")]
    public async Task Database_SwitchView_Timeline_ShowsTimeline()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);
        await SwitchDatabaseViewAsync(page, db, "Timeline");

        // The timeline view has a unique zoom-group toolbar (.tm-dbt__zoom-group)
        var zoomGroup = db.Locator(".tm-dbt__zoom-group");
        await zoomGroup.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await zoomGroup.IsVisibleAsync(), "Timeline view should show zoom controls");

        await TakeScreenshotAsync(page, "db_timeline_view");
    }

    [TestMethod]
    [Description("After switching to Board, switching back to Table shows the table view again")]
    public async Task Database_SwitchView_BackToTable()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);
        await SwitchDatabaseViewAsync(page, db, "Board");
        await SwitchDatabaseViewAsync(page, db, "All Tasks");

        var tableWrap = db.Locator(".tm-dbt-wrap");
        await tableWrap.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await tableWrap.IsVisibleAsync(), "Table view should be visible after switching back from Board");

        await TakeScreenshotAsync(page, "db_back_to_table");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Record manipulation
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the New button adds a new record and the row count increases")]
    public async Task Database_AddRecord_AppearsInTable()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        await db.Locator(".tm-dbt__row").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        var initialCount = await db.Locator(".tm-dbt__row").CountAsync();

        var newBtn = db.Locator(".tm-db__new-btn");
        await newBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await newBtn.ClickAsync();
        await page.WaitForTimeoutAsync(1000);

        var newCount = await db.Locator(".tm-dbt__row").CountAsync();
        Assert.IsTrue(newCount > initialCount,
            $"Row count should increase after adding a record (was {initialCount}, now {newCount})");

        await TakeScreenshotAsync(page, "db_add_record");
    }

    [TestMethod]
    [Description("Clicking a text cell starts editing; Tab commits the value and it appears in the cell")]
    public async Task Database_CellEdit_Text_SavesValue()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        var firstRow = db.Locator(".tm-dbt__row").First;
        await firstRow.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });

        // Click the primary (Name) cell display area to start editing
        var primaryCellDisplay = firstRow.Locator(".tm-dbt__cell--primary .tm-dbc").First;
        await primaryCellDisplay.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        // Input should appear
        var cellInput = firstRow.Locator(".tm-dbc__input").First;
        await cellInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        const string testValue = "E2E Test Name";
        await cellInput.FillAsync(testValue);
        // Tab triggers onblur which commits the value
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(600);

        // Display span should show the new value
        var displayText = await firstRow.Locator(".tm-dbt__cell--primary .tm-dbc__text").First.InnerTextAsync();
        Assert.AreEqual(testValue, displayText.Trim(),
            "Edited primary cell should display the newly committed value");

        await TakeScreenshotAsync(page, "db_cell_edit_text");
    }

    [TestMethod]
    [Description("Clicking a checkbox cell toggles its aria-checked state")]
    public async Task Database_CellEdit_Checkbox_Toggles()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        await db.Locator(".tm-dbt__row").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });

        // Find first database checkbox cell (Done field)
        var checkboxCell = db.Locator(".tm-dbc[role='checkbox']").First;
        await checkboxCell.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        var initialState = await checkboxCell.GetAttributeAsync("aria-checked");
        Assert.IsTrue(initialState is "true" or "false",
            $"Checkbox aria-checked should be explicitly rendered before clicking (was '{initialState}')");
        var expectedState = initialState == "true" ? "false" : "true";

        await checkboxCell.ScrollIntoViewIfNeededAsync();
        await checkboxCell.ClickAsync();
        await Assertions.Expect(checkboxCell).ToHaveAttributeAsync(
            "aria-checked",
            expectedState,
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });

        var newState = await checkboxCell.GetAttributeAsync("aria-checked");
        Assert.AreEqual(expectedState, newState,
            $"Checkbox aria-checked should toggle after clicking (was '{initialState}', expected '{expectedState}', now '{newState}')");

        await TakeScreenshotAsync(page, "db_cell_checkbox_toggle");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Record detail modal
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens the record detail modal for the first table row by clicking its spacer cell.
    /// </summary>
    private async Task OpenFirstRecordDetailAsync(IPage page, ILocator db)
    {
        var firstRow = db.Locator(".tm-dbt__row").First;
        await firstRow.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        var spacer = firstRow.Locator(".tm-dbt__cell--spacer");
        await spacer.ScrollIntoViewIfNeededAsync();
        await spacer.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForTimeoutAsync(800);
    }

    [TestMethod]
    [Description("Clicking the row spacer area opens the record detail modal")]
    public async Task Database_RecordDetail_Opens()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        await OpenFirstRecordDetailAsync(page, db);

        var modal = page.Locator(".tm-dbrd__modal");
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await modal.IsVisibleAsync(), "Record detail modal should open after clicking the row spacer");

        await TakeScreenshotAsync(page, "db_record_detail_open");
    }

    [TestMethod]
    [Description("Clicking the close button on the record detail modal dismisses it")]
    public async Task Database_RecordDetail_Close()
    {
        var page = await OpenNotionEditorAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        await OpenFirstRecordDetailAsync(page, db);

        var modal = page.Locator(".tm-dbrd__modal");
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // Close it
        var closeBtn = page.Locator(".tm-dbrd__close-btn");
        await closeBtn.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
        Assert.IsFalse(await modal.IsVisibleAsync(), "Record detail modal should be hidden after clicking Close");

        await TakeScreenshotAsync(page, "db_record_detail_close");
    }
}
