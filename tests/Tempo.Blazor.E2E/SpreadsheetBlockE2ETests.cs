using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests for the Spreadsheet block type embedded in the Notion editor.
/// The page pre-seeds an empty Spreadsheet block (order 31_5 on Page1).
/// </summary>
[TestClass]
public class SpreadsheetBlockE2ETests : WasmTestBase
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> OpenNotionEditorAsync()
    {
        using var http = new HttpClient();
        try { await http.PostAsync("https://localhost:5100/api/notion/reset", null); }
        catch { /* ignore if API unavailable or cert untrusted */ }

        var context = await CreateContextAsync();
        var page    = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(2000);
        return page;
    }

    private async Task<ILocator> ScrollToSpreadsheetBlockAsync(IPage page)
    {
        var block = page.Locator(".tm-notion-spreadsheet-block").First;
        await block.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await block.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(500);
        return block;
    }

    /// <summary>
    /// Clicks "Create Spreadsheet" on an empty block and waits until the modal
    /// save button is visible, proving the modal fully loaded.
    /// </summary>
    private async Task<ILocator> OpenSpreadsheetModalFromCreateAsync(IPage page, ILocator block)
    {
        var createBtn = block.Locator(".tm-notion-media-upload-zone--spreadsheet").First;
        await createBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await createBtn.ClickAsync();

        var modal = page.Locator(".tm-notion-spreadsheet-edit-modal").First;
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var saveBtn = modal.Locator(".tm-notion-spreadsheet-edit-modal__btn--primary").First;
        await saveBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        return modal;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SS-E2E-01  Empty block shows Create button
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("SS-E2E-01: An empty Spreadsheet block shows the 'Create Spreadsheet' upload-zone button")]
    public async Task SpreadsheetBlock_Empty_ShowsCreateButton()
    {
        var page  = await OpenNotionEditorAsync();
        var block = await ScrollToSpreadsheetBlockAsync(page);

        var createBtn = block.Locator(".tm-notion-media-upload-zone--spreadsheet").First;
        await createBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await createBtn.IsVisibleAsync(), "Create Spreadsheet button should be visible on an empty Spreadsheet block");

        await TakeScreenshotAsync(page, "spreadsheet_block_empty");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SS-E2E-02  Clicking Create opens the edit modal
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("SS-E2E-02: Clicking 'Create Spreadsheet' opens the spreadsheet editor modal")]
    public async Task SpreadsheetBlock_Create_OpensEditorModal()
    {
        var page  = await OpenNotionEditorAsync();
        var block = await ScrollToSpreadsheetBlockAsync(page);
        var modal = await OpenSpreadsheetModalFromCreateAsync(page, block);

        Assert.IsTrue(await modal.IsVisibleAsync(), "Spreadsheet edit modal should be visible after clicking Create");

        await TakeScreenshotAsync(page, "spreadsheet_modal_open");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SS-E2E-03  Discard closes the modal
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("SS-E2E-03: Clicking the Discard button in the modal closes it")]
    public async Task SpreadsheetBlock_Modal_DiscardButton_Closes()
    {
        var page  = await OpenNotionEditorAsync();
        var block = await ScrollToSpreadsheetBlockAsync(page);
        var modal = await OpenSpreadsheetModalFromCreateAsync(page, block);

        var discardBtn = modal.Locator(".tm-notion-spreadsheet-edit-modal__btn--discard").First;
        await discardBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });
        await discardBtn.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        Assert.IsFalse(await modal.IsVisibleAsync(), "Spreadsheet edit modal should be closed after clicking Discard");

        await TakeScreenshotAsync(page, "spreadsheet_modal_discarded");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SS-E2E-04  Save closes modal and shows embedded spreadsheet
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("SS-E2E-04: Clicking Save closes the modal and shows the embedded spreadsheet")]
    public async Task SpreadsheetBlock_Modal_SaveButton_ClosesAndShowsEmbed()
    {
        var page  = await OpenNotionEditorAsync();
        var block = await ScrollToSpreadsheetBlockAsync(page);
        var modal = await OpenSpreadsheetModalFromCreateAsync(page, block);

        var saveBtn = modal.Locator(".tm-notion-spreadsheet-edit-modal__btn--primary").First;
        await saveBtn.ClickAsync();

        // Modal should disappear
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        Assert.IsFalse(await modal.IsVisibleAsync(), "Spreadsheet edit modal should close after Save");

        // Embedded spreadsheet wrap should be visible
        await block.ScrollIntoViewIfNeededAsync();
        var embedWrap = block.Locator(".tm-notion-spreadsheet-block__embed-wrap").First;
        await embedWrap.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await embedWrap.IsVisibleAsync(), "Embedded spreadsheet wrap should be visible after saving");

        await TakeScreenshotAsync(page, "spreadsheet_embed_after_save");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SS-E2E-05  Edit button reopens the modal
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("SS-E2E-05: Clicking the Edit button on a saved Spreadsheet block reopens the modal")]
    public async Task SpreadsheetBlock_Edit_OpensSameModal()
    {
        var page  = await OpenNotionEditorAsync();
        var block = await ScrollToSpreadsheetBlockAsync(page);

        // 1. Create & save
        var modal   = await OpenSpreadsheetModalFromCreateAsync(page, block);
        var saveBtn = modal.Locator(".tm-notion-spreadsheet-edit-modal__btn--primary").First;
        await saveBtn.ClickAsync();
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        // 2. Hover the embed wrap so the overlay appears, then click Edit
        await block.ScrollIntoViewIfNeededAsync();
        var embedWrap = block.Locator(".tm-notion-spreadsheet-block__embed-wrap").First;
        await embedWrap.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await embedWrap.HoverAsync();

        var editBtn = block.Locator(".tm-notion-spreadsheet-block__edit-btn").First;
        await editBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await editBtn.ClickAsync();

        // 3. Modal should reopen
        var modal2 = page.Locator(".tm-notion-spreadsheet-edit-modal").First;
        await modal2.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await modal2.IsVisibleAsync(), "Spreadsheet edit modal should reopen when clicking Edit");

        await TakeScreenshotAsync(page, "spreadsheet_edit_reopen");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SS-E2E-06  Slash menu inserts a Spreadsheet block
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("SS-E2E-06: Typing /spreadsheet in the slash menu and selecting it inserts a Spreadsheet block")]
    public async Task SpreadsheetBlock_SlashMenu_InsertsBlock()
    {
        var page = await OpenNotionEditorAsync();

        // Open a new block via Enter in first paragraph, then type /spreadsheet
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await para.ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(800);

        await page.Keyboard.TypeAsync("/spreadsheet");
        await page.WaitForSelectorAsync(".tm-notion-slash",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        // The Spreadsheet item should be visible in the menu
        var spreadsheetItem = page.Locator(".tm-notion-slash__item").Filter(new LocatorFilterOptions
        {
            HasText = "Spreadsheet"
        }).First;
        await spreadsheetItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await spreadsheetItem.ClickAsync();

        // A new spreadsheet block should appear
        await page.WaitForTimeoutAsync(1000);
        var spreadsheetBlocks = page.Locator(".tm-notion-spreadsheet-block");
        var count = await spreadsheetBlocks.CountAsync();
        Assert.IsTrue(count >= 2, $"At least 2 spreadsheet blocks expected (pre-seeded + newly inserted), got {count}");

        await TakeScreenshotAsync(page, "spreadsheet_slash_menu_insert");
    }
}
