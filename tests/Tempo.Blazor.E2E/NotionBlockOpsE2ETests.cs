using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests covering block operations via the block handle and context menu:
/// hover visibility, delete, duplicate, turn into, color, and add block below.
/// </summary>
[TestClass]
public class NotionBlockOpsE2ETests : WasmTestBase
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> OpenNotionEditorAsync()
    {
        using var http = DemoApiTestHttp.CreateClient();
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

    private ILocator FirstBlock(IPage page) =>
        page.Locator("[data-notion-block]").First;

    private ILocator FirstParagraphBlock(IPage page) =>
        page.Locator("[data-block-type='Paragraph']").First;

    private ILocator BlockHandle(IPage page) =>
        page.Locator(".tm-notion-handle").First;

    private ILocator AddBlockBtn(IPage page) =>
        page.Locator(".tm-notion-handle > .tm-notion-handle__btn").First;

    private ILocator MenuBtn(IPage page) =>
        page.Locator(".tm-notion-handle__menu-anchor > .tm-notion-handle__btn").First;

    private ILocator ContextMenu(IPage page) =>
        page.Locator(".tm-notion-ctx").First;

    /// <summary>
    /// Hovers over the first block so the block handle becomes visible.
    /// </summary>
    private async Task HoverFirstBlockAsync(IPage page)
    {
        var block = FirstBlock(page);
        await block.HoverAsync();
        await page.WaitForTimeoutAsync(400); // CSS transition
    }

    /// <summary>
    /// Opens the context menu for the given block (defaults to first block).
    /// </summary>
    private async Task OpenContextMenuAsync(IPage page, ILocator? block = null)
    {
        block ??= FirstBlock(page);
        await block.HoverAsync();
        await page.WaitForTimeoutAsync(400);
        // The menu button is inside the block's handle
        var menuBtn = block.Locator(".tm-notion-handle__menu-anchor > .tm-notion-handle__btn").First;
        await menuBtn.ClickAsync();
        await ContextMenu(page).WaitForAsync(new LocatorWaitForOptions
            { State = WaitForSelectorState.Visible, Timeout = 5000 });
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Block handle visibility
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Hovering over a block makes the block handle buttons visible")]
    public async Task BlockHandle_Hover_Shows()
    {
        var page = await OpenNotionEditorAsync();
        var block = FirstBlock(page);
        await block.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        // Before hover: handle should be invisible (opacity ≈ 0)
        var opacityBefore = await page.EvaluateAsync<double>(
            "() => { const h = document.querySelector('.tm-notion-handle'); return h ? parseFloat(getComputedStyle(h).opacity) : -1; }");
        Assert.AreEqual(0.0, opacityBefore, 0.1, "Handle opacity should be 0 before hover");

        // Hover over the block
        await block.HoverAsync();
        await page.WaitForTimeoutAsync(400);

        // After hover: handle should be visible (opacity = 1)
        var opacityAfter = await page.EvaluateAsync<double>(
            "() => { const h = document.querySelector('.tm-notion-handle'); return h ? parseFloat(getComputedStyle(h).opacity) : -1; }");
        Assert.AreEqual(1.0, opacityAfter, 0.1, "Handle opacity should be 1 after hover");

        await TakeScreenshotAsync(page, "block_handle_hover");
    }

    [TestMethod]
    [Description("Clicking the handle menu button opens the block context menu")]
    public async Task BlockHandle_MenuBtn_OpensContextMenu()
    {
        var page = await OpenNotionEditorAsync();
        await OpenContextMenuAsync(page);

        var menu = ContextMenu(page);
        Assert.IsTrue(await menu.IsVisibleAsync(), "Context menu should be visible after clicking menu button");
        await TakeScreenshotAsync(page, "block_handle_menu_open");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Context menu — Delete & Duplicate
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking Delete in the context menu removes the block")]
    public async Task ContextMenu_Delete_RemovesBlock()
    {
        var page = await OpenNotionEditorAsync();
        var blocks = page.Locator("[data-notion-block]");
        var countBefore = await blocks.CountAsync();
        Assert.IsTrue(countBefore > 0, "There should be at least one block before delete");

        await OpenContextMenuAsync(page);

        // Click Delete (first item with danger class)
        var deleteBtn = page.Locator(".tm-notion-ctx__item--danger").First;
        await deleteBtn.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var countAfter = await blocks.CountAsync();
        Assert.AreEqual(countBefore - 1, countAfter, "Block count should decrease by 1 after delete");
        await TakeScreenshotAsync(page, "context_menu_delete");
    }

    [TestMethod]
    [Description("Clicking Duplicate in the context menu clones the block")]
    public async Task ContextMenu_Duplicate_ClonesBlock()
    {
        var page = await OpenNotionEditorAsync();
        var blocks = page.Locator("[data-notion-block]");
        var countBefore = await blocks.CountAsync();
        Assert.IsTrue(countBefore > 0, "There should be at least one block before duplicate");

        // Capture the text of the first block before duplicating
        var firstBlock = blocks.First;
        var originalText = await firstBlock.InnerTextAsync();

        await OpenContextMenuAsync(page);

        // Click Duplicate (second menu item)
        var duplicateBtn = page.Locator(".tm-notion-ctx__item").Nth(1);
        await duplicateBtn.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var countAfter = await blocks.CountAsync();
        Assert.AreEqual(countBefore + 1, countAfter, "Block count should increase by 1 after duplicate");

        // The duplicated block should be second and have same text
        var secondBlock = blocks.Nth(1);
        var duplicatedText = await secondBlock.InnerTextAsync();
        Assert.AreEqual(originalText, duplicatedText, "Duplicated block should have same text as original");
        await TakeScreenshotAsync(page, "context_menu_duplicate");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Context menu — Turn Into
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Hovering over Turn Into in the context menu opens its submenu")]
    public async Task ContextMenu_TurnInto_SubMenuOpens()
    {
        var page = await OpenNotionEditorAsync();
        await OpenContextMenuAsync(page);

        // Hover over the "Turn into" item (third item, has submenu class)
        var turnIntoItem = page.Locator(".tm-notion-ctx__item--sub").First;
        await turnIntoItem.HoverAsync();
        await page.WaitForTimeoutAsync(400);

        // Submenu should appear
        var subMenu = page.Locator(".tm-notion-ctx-sub").First;
        await subMenu.WaitForAsync(new LocatorWaitForOptions
            { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await subMenu.IsVisibleAsync(), "Turn Into submenu should be visible");
        await TakeScreenshotAsync(page, "context_menu_turn_into_submenu");
    }

    [TestMethod]
    [Description("Selecting Heading 2 from Turn Into submenu converts the block")]
    public async Task ContextMenu_TurnInto_Heading_Converts()
    {
        var page = await OpenNotionEditorAsync();
        await OpenContextMenuAsync(page);

        // Hover over "Turn into" to open submenu
        var turnIntoItem = page.Locator(".tm-notion-ctx__item--sub").First;
        await turnIntoItem.HoverAsync();
        await page.WaitForTimeoutAsync(400);

        // Click "Heading 2" in the submenu
        var subMenuItems = page.Locator(".tm-notion-ctx-sub .tm-notion-ctx__item");
        var h2Item = subMenuItems.Filter(new LocatorFilterOptions { HasText = "Heading 2" }).First;
        await h2Item.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        // Verify the first block is now a heading
        var firstBlock = FirstBlock(page);
        var headingEl = firstBlock.Locator(".tm-notion-h2").First;
        await headingEl.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        Assert.IsTrue(await headingEl.IsVisibleAsync(), "Block should be converted to Heading 2");
        await TakeScreenshotAsync(page, "context_menu_turn_into_h2");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Context menu — Color
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Hovering over Color in the context menu opens its color submenu")]
    public async Task ContextMenu_Color_SubMenuOpens()
    {
        var page = await OpenNotionEditorAsync();
        await OpenContextMenuAsync(page);

        // Hover over the "Color" item (last item with submenu class)
        var colorItem = page.Locator(".tm-notion-ctx__item--sub").Last;
        await colorItem.HoverAsync();
        await page.WaitForTimeoutAsync(400);

        // Submenu should appear
        var subMenu = page.Locator(".tm-notion-ctx-sub").First;
        await subMenu.WaitForAsync(new LocatorWaitForOptions
            { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await subMenu.IsVisibleAsync(), "Color submenu should be visible");

        // Verify both text-color and background-color sections are present
        var swatches = page.Locator(".tm-notion-ctx-color__swatch");
        Assert.IsTrue(await swatches.CountAsync() > 0, "Color swatches should be visible in submenu");
        await TakeScreenshotAsync(page, "context_menu_color_submenu");
    }

    [TestMethod]
    [Description("Selecting a background color from the Color submenu changes the block style")]
    public async Task ContextMenu_Color_Apply_ChangesBlockStyle()
    {
        var page = await OpenNotionEditorAsync();

        // Use the first paragraph block (second overall block) for this test
        var paraBlock = FirstParagraphBlock(page);
        await paraBlock.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await OpenContextMenuAsync(page, paraBlock);

        // Hover over "Color" to open submenu
        var colorItem = page.Locator(".tm-notion-ctx__item--sub").Last;
        await colorItem.HoverAsync();
        await page.WaitForTimeoutAsync(400);

        // Click the "Gray" background color option via JS to avoid mouse-move closing submenu
        await page.EvaluateAsync("""
            () => {
                const items = document.querySelectorAll('.tm-notion-ctx-sub .tm-notion-ctx-color__item');
                for (const item of items) {
                    if (item.textContent?.trim() === 'Gray') {
                        item.click();
                        return;
                    }
                }
            }
            """);
        await page.WaitForTimeoutAsync(1000);

        // Verify the paragraph element has the background color class
        var paragraph = paraBlock.Locator(".tm-notion-paragraph").First;
        await paragraph.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        var classAttr = await paragraph.GetAttributeAsync("class");
        Assert.IsTrue(
            classAttr?.Contains("tm-notion-bg-gray") == true,
            $"Paragraph should have gray background class. Classes: {classAttr}");
        await TakeScreenshotAsync(page, "context_menu_color_gray");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Add block button
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the add-block (+) button inserts a new empty block below")]
    public async Task AddBlockButton_Click_AddsBlockBelow()
    {
        var page = await OpenNotionEditorAsync();
        var blocks = page.Locator("[data-notion-block]");
        var countBefore = await blocks.CountAsync();
        Assert.IsTrue(countBefore > 0, "There should be at least one block before adding");

        // Hover over first block to reveal handle
        await HoverFirstBlockAsync(page);

        // Click the add block button (first button in handle)
        var addBtn = AddBlockBtn(page);
        await addBtn.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var countAfter = await blocks.CountAsync();
        Assert.AreEqual(countBefore + 1, countAfter, "Block count should increase by 1 after clicking add button");

        // The new block should be second (below the first)
        var secondBlock = blocks.Nth(1);
        var secondType = await secondBlock.GetAttributeAsync("data-block-type");
        Assert.AreEqual("Paragraph", secondType, "Newly added block should be a Paragraph");
        await TakeScreenshotAsync(page, "add_block_button");
    }
}

[TestClass]
[DoNotParallelize]
public class NotionBlockOpsRecoveryE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB16: context menu, duplicate, delete, and copy-link affordance capture deterministic UX baselines.")]
    public async Task EB16_ContextMenuDuplicateDelete_CapturesBaselines()
    {
        var page = await OpenNotionEditorAsync();
        await SeedDragDropPageAsync();

        var callout = page.Locator("[data-block-id='eb160000-0000-0000-0000-000000000004']").First;
        await OpenBlockMenuAsync(page, callout);

        var menu = page.Locator(".tm-notion-ctx").First;
        await Assertions.Expect(menu).ToBeVisibleAsync();
        await Assertions.Expect(menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Duplicate" })).ToBeVisibleAsync();
        await Assertions.Expect(menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Delete" })).ToBeVisibleAsync();
        await Assertions.Expect(menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Copy link to block" })).ToBeVisibleAsync();
        await CaptureBaselineAsync("drag-drop", "context-menu-copy-duplicate-delete", menu);

        var blocks = page.Locator("[data-notion-block]");
        var countBeforeDuplicate = await blocks.CountAsync();
        await menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Duplicate" }).ClickAsync();
        await page.WaitForTimeoutAsync(900);
        var countAfterDuplicate = await blocks.CountAsync();
        Assert.AreEqual(countBeforeDuplicate + 1, countAfterDuplicate, "Duplicate should add one block through the real block provider.");
        await CaptureBaselineAsync("drag-drop", "context-menu-after-duplicate", page.Locator(".tm-notion-page").First);

        await OpenBlockMenuAsync(page, page.Locator("[data-block-id='eb160000-0000-0000-0000-000000000004']").First);
        await page.Locator(".tm-notion-ctx").First.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Delete" }).ClickAsync();
        await page.WaitForTimeoutAsync(900);
        var countAfterDelete = await blocks.CountAsync();
        Assert.AreEqual(countBeforeDuplicate, countAfterDelete, "Delete should remove the original block after duplication.");
        await CaptureBaselineAsync("drag-drop", "context-menu-after-delete", page.Locator(".tm-notion-page").First);
    }

    private static async Task OpenBlockMenuAsync(IPage page, ILocator block)
    {
        await block.ScrollIntoViewIfNeededAsync();
        await block.HoverAsync();
        var menuButton = block.Locator(".tm-notion-handle__menu-anchor > .tm-notion-handle__btn").First;
        await menuButton.ClickAsync();
        await page.Locator(".tm-notion-ctx").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }
}
