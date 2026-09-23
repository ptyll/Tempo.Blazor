using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests covering column layout blocks in the Notion editor.
/// Phase 9: ColumnList rendering, column editing, add column, max columns, divider visibility.
/// </summary>
[TestClass]
public class NotionLayoutE2ETests : WasmTestBase
{
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

    /// <summary>
    /// Inserts a block via the slash menu. Types searchTerm into the slash menu
    /// search input and clicks the first matching item.
    /// </summary>
    private async Task InsertBlockViaSlashMenuAsync(IPage page, string searchTerm)
    {
        // Use the last paragraph on the page so we insert at the end
        var paras = page.Locator(".tm-notion-paragraph[contenteditable='true']");
        var count = await paras.CountAsync();
        var para = count > 0 ? paras.Last : paras.First;
        await para.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await para.ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(1200);

        await page.Keyboard.TypeAsync("/");
        await page.WaitForSelectorAsync(".tm-notion-slash",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var searchInput = page.Locator(".tm-notion-slash__input");
        await searchInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await searchInput.FillAsync(searchTerm);
        await page.WaitForTimeoutAsync(600);

        var firstItem = page.Locator(".tm-notion-slash__item").First;
        await firstItem.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await firstItem.ClickAsync();
        await page.WaitForTimeoutAsync(1500);
    }

    private ILocator ColumnListBlock(IPage page) =>
        page.Locator("[data-block-type='ColumnList']").First;

    private ILocator Columns(IPage page) =>
        page.Locator(".tm-notion-column");

    // ══════════════════════════════════════════════════════════════════════════
    //  Insert & render
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Inserting '2 Columns' via slash menu creates a column list with 2 columns")]
    public async Task ColumnList_Insert_Creates2Columns()
    {
        var page = await OpenNotionEditorAsync();

        // Insert a new ColumnList block via slash menu
        await InsertBlockViaSlashMenuAsync(page, "2 col");
        await page.WaitForTimeoutAsync(1500);

        // The newly inserted ColumnList should have 2 columns
        var colList = page.Locator(".tm-notion-column-list").Last;
        await colList.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var cols = colList.Locator(".tm-notion-column");
        var count = await cols.CountAsync();
        Assert.AreEqual(2, count, "Inserted ColumnList should have 2 columns");

        await TakeScreenshotAsync(page, "column_list_insert");
    }

    [TestMethod]
    [Description("The default demo ColumnList renders with 2 visible columns")]
    public async Task ColumnList_Default_Renders2Columns()
    {
        var page = await OpenNotionEditorAsync();

        var colList = ColumnListBlock(page);
        await colList.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var cols = Columns(page);
        var count = await cols.CountAsync();
        Assert.IsTrue(count >= 2, "Demo page should have at least 2 columns in the ColumnList block");

        // Verify columns have correct data-col-index attributes
        var col0 = page.Locator(".tm-notion-column[data-col-index='0']").First;
        var col1 = page.Locator(".tm-notion-column[data-col-index='1']").First;
        Assert.IsTrue(await col0.IsVisibleAsync(), "Column 0 should be visible");
        Assert.IsTrue(await col1.IsVisibleAsync(), "Column 1 should be visible");

        await TakeScreenshotAsync(page, "column_list_default");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Content editing inside columns
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Typing text into a paragraph inside column 1 updates the DOM")]
    public async Task ColumnList_Column1_TypesContent()
    {
        var page = await OpenNotionEditorAsync();

        var colList = ColumnListBlock(page);
        await colList.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        // Find the first paragraph inside column 0
        var col0 = page.Locator(".tm-notion-column[data-col-index='0']");
        var para = col0.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await para.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var testText = "Col1 E2E";
        await page.Keyboard.TypeAsync(testText);
        await page.WaitForTimeoutAsync(300);

        // Blur to save
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(500);

        var text = await para.InnerTextAsync();
        StringAssert.Contains(text, testText, "Column 1 paragraph should contain the typed text");

        await TakeScreenshotAsync(page, "column_list_col1_type");
    }

    [TestMethod]
    [Description("Typing text into a paragraph inside column 2 updates the DOM")]
    public async Task ColumnList_Column2_TypesContent()
    {
        var page = await OpenNotionEditorAsync();

        var colList = ColumnListBlock(page);
        await colList.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        // Find the first paragraph inside column 1
        var col1 = page.Locator(".tm-notion-column[data-col-index='1']");
        var para = col1.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await para.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var testText = "Col2 E2E";
        await page.Keyboard.TypeAsync(testText);
        await page.WaitForTimeoutAsync(300);

        // Blur to save
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(500);

        var text = await para.InnerTextAsync();
        StringAssert.Contains(text, testText, "Column 2 paragraph should contain the typed text");

        await TakeScreenshotAsync(page, "column_list_col2_type");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Add column & max columns
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the add-column button inserts a third column")]
    public async Task ColumnList_AddColumn_Button_AddsColumn()
    {
        var page = await OpenNotionEditorAsync();

        var colList = ColumnListBlock(page);
        await colList.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var colsBefore = await Columns(page).CountAsync();
        Assert.IsTrue(colsBefore >= 2, "Should have at least 2 columns before adding");

        // Click the add-column button inside the ColumnList — then wait STATE-BASED for the new
        // column to render (the click round-trips through Blazor; a fixed wait reads the count
        // before the re-render lands under shared-host load).
        var addBtn = colList.Locator(".tm-notion-column-list__add-col").First;
        await addBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await addBtn.ClickAsync();
        await page.WaitForFunctionAsync(
            "expected => document.querySelectorAll('.tm-notion-column').length === expected",
            colsBefore + 1,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        var colsAfter = await Columns(page).CountAsync();
        Assert.AreEqual(colsBefore + 1, colsAfter, "Column count should increase by 1 after clicking add column");

        await TakeScreenshotAsync(page, "column_list_add_column");
    }

    [TestMethod]
    [Description("Adding columns up to MaxColumns (5) hides the add-column button")]
    public async Task ColumnList_MaxColumns_HidesAddButton()
    {
        var page = await OpenNotionEditorAsync();

        var colList = ColumnListBlock(page);
        await colList.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        // Repeatedly click add-column until button disappears or we reach 5 columns — each click
        // waits STATE-BASED for the column to render, not on a fixed delay, so the loop can never
        // fire the next click against a pre-render count under shared-host load.
        for (var i = 0; i < 10; i++)
        {
            var addBtn = colList.Locator(".tm-notion-column-list__add-col");
            var btnCount = await addBtn.CountAsync();
            if (btnCount == 0) break;

            var colsCount = await Columns(page).CountAsync();
            if (colsCount >= 5) break;

            await addBtn.First.ClickAsync();
            await page.WaitForFunctionAsync(
                "expected => document.querySelectorAll('.tm-notion-column').length >= expected",
                colsCount + 1,
                new PageWaitForFunctionOptions { Timeout = 15000 });
        }

        // Should have 5 columns now
        var finalCols = await Columns(page).CountAsync();
        Assert.AreEqual(5, finalCols, "Should reach 5 columns (MaxColumns)");

        // Add button should be hidden
        var addBtnAfter = colList.Locator(".tm-notion-column-list__add-col");
        var btnVisible = await addBtnAfter.CountAsync();
        Assert.AreEqual(0, btnVisible, "Add-column button should be hidden when MaxColumns is reached");

        await TakeScreenshotAsync(page, "column_list_max_columns");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Divider visibility
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("A vertical divider is visible between the two columns")]
    public async Task ColumnList_Divider_Visible()
    {
        var page = await OpenNotionEditorAsync();

        var colList = ColumnListBlock(page);
        await colList.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var dividers = colList.Locator(".tm-notion-column-list__divider");
        var dividerCount = await dividers.CountAsync();
        Assert.IsTrue(dividerCount >= 1, "There should be at least 1 divider between columns");

        var firstDivider = dividers.First;
        Assert.IsTrue(await firstDivider.IsVisibleAsync(), "Divider should be visible");

        // Verify the divider grip element is present
        var grip = firstDivider.Locator(".tm-notion-column-list__divider-grip").First;
        Assert.IsTrue(await grip.IsVisibleAsync(), "Divider grip should be visible");

        await TakeScreenshotAsync(page, "column_list_divider");
    }
}

[TestClass]
public class NotionLayoutRecoveryE2ETests : NotionE2ETestBase
{
    private const string TwoColumnListId = "eb800000-0000-0000-0000-000000000010";
    private const string FourColumnListId = "eb800000-0000-0000-0000-000000000030";

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    public async Task EB8_DesktopColumnsResizeAndTocStates_AreCaptured()
    {
        var page = await OpenNotionEditorAsync();
        await SeedLayoutPageAsync();

        var twoColumnList = page.Locator($"[data-block-id='{TwoColumnListId}'] .tm-notion-column-list").First;
        var fourColumnList = page.Locator($"[data-block-id='{FourColumnListId}'] .tm-notion-column-list").First;

        await twoColumnList.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await fourColumnList.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.AreEqual(2, await twoColumnList.Locator(".tm-notion-column").CountAsync(), "EB8 two-column baseline should contain exactly 2 columns.");
        Assert.AreEqual(4, await fourColumnList.Locator(".tm-notion-column").CountAsync(), "EB8 four-column baseline should contain exactly 4 columns.");

        await AddColumnWithButtonAsync(twoColumnList, TwoColumnListId, expectedColumns: 3);
        Assert.AreEqual(3, await twoColumnList.Locator(".tm-notion-column").CountAsync(), "EB8 add-column action should create a 3-column visual state.");

        await CaptureBaselineAsync("layout", "desktop-2-3-4-columns", page.Locator(".tm-notion-page").First);

        await ResizeFirstDividerAsync(page, twoColumnList, TwoColumnListId, 90);
        await AssertColumnWidthsChangedAsync(twoColumnList);
        await CaptureBaselineAsync("layout", "desktop-resized-divider", twoColumnList);

        await SeedEmptyTocPageAsync();
        var emptyToc = page.Locator("[data-block-id='eb810000-0000-0000-0000-000000000002'] .tm-toc").First;
        await emptyToc.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await CaptureBaselineAsync("layout", "toc-empty-state", emptyToc);

        await SeedLayoutPageAsync();
        var toc = page.Locator(".tm-toc").First;
        await toc.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.Locator(".tm-toc__item").Nth(3).ClickAsync();
        // State-based, not a fixed delay: scroll-spy marks the active item after the scroll
        // settles — under load a fixed 600ms reads the list before the class lands.
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\".tm-toc__item--active, .tm-toc__item[aria-current='true']\").length > 0",
            new PageWaitForFunctionOptions { Timeout = 15000 });
        var activeItems = await page.Locator(".tm-toc__item--active, .tm-toc__item[aria-current='true']").CountAsync();
        Assert.IsTrue(activeItems > 0, "EB8 TOC scroll-spy should expose an active state after navigating to a heading.");
        await CaptureBaselineAsync("layout", "toc-many-headings-scroll-spy", toc);

        await AssertNoHorizontalOverflowAsync(page);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    public async Task EB8_MobileColumnsStackWithoutOverflow_AreCaptured()
    {
        await SetViewportAsync(390, 844);
        var page = await OpenNotionEditorAsync();
        await SeedLayoutPageAsync();

        var fourColumnList = page.Locator($"[data-block-id='{FourColumnListId}'] .tm-notion-column-list").First;
        await fourColumnList.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var columns = fourColumnList.Locator(".tm-notion-column");
        Assert.AreEqual(4, await columns.CountAsync(), "EB8 mobile baseline should use the deterministic 4-column block.");

        var tops = await columns.EvaluateAllAsync<double[]>("els => els.map(el => Math.round(el.getBoundingClientRect().top))");
        Assert.IsTrue(tops.Zip(tops.Skip(1), (previous, next) => next > previous).All(BooleanIdentity),
            $"EB8 mobile columns should stack vertically. Tops: {string.Join(", ", tops)}");
        Assert.AreEqual(0, await fourColumnList.Locator(".tm-notion-column-list__divider:visible").CountAsync(), "Mobile column dividers should be hidden.");

        await CaptureBaselineAsync("layout", "mobile-columns-stacked", fourColumnList);
        await AssertNoHorizontalOverflowAsync(page);
    }

    private static bool BooleanIdentity(bool value) => value;

    private static async Task AddColumnWithButtonAsync(ILocator columnList, string blockId, int expectedColumns)
    {
        var button = columnList.Locator(".tm-notion-column-list__add-col").First;
        await button.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await button.ClickAsync();
        // State-based, not a fixed delay: the add-column click round-trips through Blazor before
        // the new column renders — under shared-host load a fixed wait observes the list before
        // the re-render lands (serial-run symptom: count still 2 instead of 3). The selector is
        // scoped by block id because the page carries multiple column lists.
        await columnList.Page.WaitForFunctionAsync(
            """
            ([scopeSelector, expected]) => {
                const scope = document.querySelector(scopeSelector);
                return scope && scope.querySelectorAll('.tm-notion-column').length === expected;
            }
            """,
            new object[] { $"[data-block-id='{blockId}'] .tm-notion-column-list", expectedColumns },
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    private static async Task ResizeFirstDividerAsync(IPage page, ILocator columnList, string blockId, int deltaX)
    {
        var divider = columnList.Locator(".tm-notion-column-list__divider").First;
        await divider.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var box = await divider.BoundingBoxAsync();
        Assert.IsNotNull(box, "EB8 resize divider should have a visible bounding box.");

        var startX = box.X + box.Width / 2;
        var startY = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(startX, startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(startX + deltaX, startY, new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();
        // State-based, not a fixed delay: the drag commits widths through a Blazor re-render —
        // wait until the first two columns actually diverge before asserting the screenshot.
        // Scoped by block id because the page carries multiple column lists.
        await page.WaitForFunctionAsync(
            """
            scopeSelector => {
                const scope = document.querySelector(scopeSelector);
                const cols = scope?.querySelectorAll('.tm-notion-column') ?? [];
                if (cols.length < 2) return false;
                const w0 = Math.round(cols[0].getBoundingClientRect().width);
                const w1 = Math.round(cols[1].getBoundingClientRect().width);
                return Math.abs(w0 - w1) > 24;
            }
            """,
            $"[data-block-id='{blockId}'] .tm-notion-column-list",
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    private static async Task AssertColumnWidthsChangedAsync(ILocator columnList)
    {
        var widths = await columnList.Locator(".tm-notion-column").EvaluateAllAsync<double[]>(
            "els => els.slice(0, 2).map(el => Math.round(el.getBoundingClientRect().width))");
        Assert.AreEqual(2, widths.Length, "EB8 resize check needs the first two columns.");
        Assert.IsTrue(Math.Abs(widths[0] - widths[1]) > 24,
            $"EB8 resize should visibly change the first two column widths. Widths: {string.Join(", ", widths)}");
    }

    private static async Task AssertNoHorizontalOverflowAsync(IPage page)
    {
        var hasOverflow = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.IsFalse(hasOverflow, "EB8 layout screenshots should not introduce document-level horizontal overflow.");
    }
}
