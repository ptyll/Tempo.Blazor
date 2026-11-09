using System.Net.Http;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 4 batch B (DB-3): the database dialogs ride the core TmModal / TmOverlayPanel — at 390
/// with touch they present as bottom sheets (AutoFullscreen surfaces cover the viewport). Covers
/// the properties panel, the field editor, import/export with the mobile mapping cards, the
/// record detail and the template menu / template editor.
/// </summary>
[TestClass]
public sealed class NotionDbMobileSheetE2ETests : WasmTestBase
{
    private async Task<IPage> OpenTouchEditorAsync(int width, int height)
    {
        using var http = DemoApiTestHttp.CreateClient();
        using var notionResponse = await http.PostAsync("/api/notion/reset", null);
        notionResponse.EnsureSuccessStatusCode();
        using var databaseResponse = await http.PostAsync("/api/notion/databases/e2e/seed/default", null);
        databaseResponse.EnsureSuccessStatusCode();

        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = true,
            IsMobile = true,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/notion-editor", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000,
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000,
        });
        return page;
    }

    private static async Task<ILocator> NavigateToDatabaseBlockAsync(IPage page)
    {
        var db = page.Locator(".tm-db").First;
        await db.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await page.WaitForSelectorAsync(".tm-db__skeleton",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        await page.WaitForTimeoutAsync(300);
        return db;
    }

    /// <summary>
    /// Scrolls a trigger into view RIGHT BEFORE clicking it: the table re-renders after the
    /// skeleton disappears and resets the earlier scroll, and an off-screen anchor parks the
    /// anchored popover invisible (overlay.js anchor-visibility rule).
    /// </summary>
    private static async Task EvaluateClickInViewAsync(ILocator trigger)
    {
        await trigger.EvaluateAsync("el => el.scrollIntoView({ block: 'center', inline: 'nearest' })");
        await trigger.EvaluateAsync("el => el.click()");
    }

    /// <summary>
    /// Compact layouts park secondary actions in More. The bar copy is position:fixed and inert,
    /// so a click has to land on the menu entry (data-menu-testid) when the bar copy is collapsed.
    /// </summary>
    private static async Task OpenToolbarActionAsync(ILocator db, string testId)
    {
        var bar = db.Locator($"[data-testid='{testId}']:not(.tm-toolbar-item--collapsed)");
        if (await bar.CountAsync() > 0)
        {
            await EvaluateClickInViewAsync(bar.First);
            return;
        }

        await db.Locator("button.tm-toolbar-more").ClickAsync();
        var page = db.Page;
        // A pressed toggle renders as menuitemcheckbox; a plain action as menuitem. Overflow-only
        // entries own data-testid, collapsed bar copies expose data-menu-testid.
        var entry = page.Locator($"[data-menu-testid='{testId}'], [role^='menuitem'][data-testid='{testId}']").First;
        await entry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await entry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await entry.ClickAsync();
    }

    private static async Task AssertBottomDockedAsync(IPage page, ILocator sheet, int viewportHeight, string what)
    {
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var box = await sheet.BoundingBoxAsync() ?? throw new AssertFailedException($"{what}: no bounding box");
        Assert.IsTrue(Math.Abs(box.Y + box.Height - viewportHeight) < 2,
            $"{what} must dock to the viewport bottom edge (bottom {box.Y + box.Height:F0}, viewport {viewportHeight})");
    }

    private static async Task AssertFullscreenAsync(IPage page, ILocator overlay, int viewportWidth, int viewportHeight, string what)
    {
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var box = await overlay.BoundingBoxAsync() ?? throw new AssertFailedException($"{what}: no bounding box");
        Assert.IsTrue(Math.Abs(box.Width - viewportWidth) < 2 && Math.Abs(box.Height - viewportHeight) < 2,
            $"{what} must cover the viewport ({box.Width:F0}x{box.Height:F0}, expected {viewportWidth}x{viewportHeight})");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Properties panel at 390 (touch): the anchored popover promotes to a bottom sheet with the field rows.")]
    public async Task DbPropertiesPanel_At390_PresentsAsBottomSheet()
    {
        var page = await OpenTouchEditorAsync(390, 844);
        var db = await NavigateToDatabaseBlockAsync(page);

        await OpenToolbarActionAsync(db, "notion-db-properties-open");
        var sheet = page.Locator(".tm-overlay-panel-sheet:has(.tm-db__panel--fields)").First;
        await AssertBottomDockedAsync(page, sheet, 844, "properties sheet");
        Assert.IsTrue(await sheet.Locator(".tm-db__field-row").First.IsVisibleAsync(),
            "field rows render inside the sheet");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Field editor at 390 (touch): the core modal presents as a bottom sheet containing the editor body.")]
    public async Task DbFieldEditor_At390_PresentsAsBottomSheet()
    {
        var page = await OpenTouchEditorAsync(390, 844);
        var db = await NavigateToDatabaseBlockAsync(page);

        await db.Locator(".tm-dbt__th-inner").Nth(1).EvaluateAsync("el => el.click()");
        var sheet = page.Locator(".tm-modal--sheet:has(.tm-dbfe)").First;
        await AssertBottomDockedAsync(page, sheet, 844, "field editor sheet");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Import at 390 (touch): bottom sheet; the mapping rows render as cards under 600px.")]
    public async Task DbImportExport_At390_Sheet_WithMappingCards()
    {
        var page = await OpenTouchEditorAsync(390, 844);
        var db = await NavigateToDatabaseBlockAsync(page);

        await OpenToolbarActionAsync(db, "notion-db-import-export");
        var sheet = page.Locator(".tm-modal--sheet:has(.tm-dbie)").First;
        await AssertBottomDockedAsync(page, sheet, 844, "import/export sheet");

        var csvPath = Path.Combine(Path.GetTempPath(), $"tm-db-import-{Guid.NewGuid():n}.csv");
        await File.WriteAllTextAsync(csvPath, "Name,Status,Priority\nAlpha,Active,1\nBeta,Done,2\n");
        try
        {
            await page.Locator(".tm-dbie__file-input").SetInputFilesAsync(csvPath);
            var firstRow = page.Locator(".tm-dbie__map-table tbody tr").First;
            await firstRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

            // Under 600px the mapping table collapses into bordered cards.
            var display = await firstRow.EvaluateAsync<string>("el => getComputedStyle(el).display");
            Assert.AreEqual("block", display, "mapping rows stack as cards on a 390 sheet");
            var borderTop = await firstRow.EvaluateAsync<string>("el => getComputedStyle(el).borderTopWidth");
            Assert.AreNotEqual("0px", borderTop, "card rows carry their own border");
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Record detail at 390 (touch): AutoFullscreen covers the viewport with the record body.")]
    public async Task DbRecordDetail_At390_PresentsAsFullscreenSheet()
    {
        var page = await OpenTouchEditorAsync(390, 844);
        var db = await NavigateToDatabaseBlockAsync(page);

        var firstRow = db.Locator(".tm-dbt__row").First;
        await firstRow.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        await firstRow.Locator(".tm-dbt__cell--spacer").EvaluateAsync("el => el.click()");
        await page.WaitForTimeoutAsync(800);

        var overlay = page.Locator(".tm-modal-overlay:has(.tm-dbrd)").First;
        await AssertFullscreenAsync(page, overlay, 390, 844, "record detail");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Template menu at 390 (touch): the anchored popover promotes to a bottom sheet.")]
    public async Task DbTemplateMenu_At390_PresentsAsBottomSheet()
    {
        var page = await OpenTouchEditorAsync(390, 844);
        var db = await NavigateToDatabaseBlockAsync(page);

        await EvaluateClickInViewAsync(db.Locator(".tm-db__new-chevron").First);
        var sheet = page.Locator(".tm-overlay-panel-sheet:has(.tm-dbtm)").First;
        await AssertBottomDockedAsync(page, sheet, 844, "template menu sheet");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Template editor at 390 (touch): AutoFullscreen covers the viewport.")]
    public async Task DbTemplateEditor_At390_PresentsAsFullscreenSheet()
    {
        var page = await OpenTouchEditorAsync(390, 844);
        var db = await NavigateToDatabaseBlockAsync(page);

        await EvaluateClickInViewAsync(db.Locator(".tm-db__new-chevron").First);
        var newTemplate = page.Locator(".tm-dbtm__new-btn").First;
        await newTemplate.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await newTemplate.EvaluateAsync("el => el.click()");

        var overlay = page.Locator(".tm-modal-overlay:has(.tm-dbte)").First;
        await AssertFullscreenAsync(page, overlay, 390, 844, "template editor");
    }
}
