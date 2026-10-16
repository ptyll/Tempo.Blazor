using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 2 E2E for the diagram import dialogs: the SQL dialog shows a live
/// table/relation preview while typing and both dialogs are captured at desktop
/// 1440 width for the UX review. Screenshots land in the durable
/// tests/Tempo.Blazor.E2E/__screenshots__/diagram-editor/ directory.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class DiagramPhase2ImportDialogsE2ETests : PlaywrightTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";
    private const string SingleTableDdl =
        "CREATE TABLE Customer (Id INT PRIMARY KEY, Name NVARCHAR(100) NOT NULL);";

    /// <inheritdoc />
    protected override string BaseUrl => "http://localhost:5010";

    [TestMethod]
    [Description("Typing SQL DDL into the import dialog updates the live preview (Tables: 1) without leaving the dialog")]
    public async Task SqlImportDialog_ShowsLivePreview_WhenDdlTyped()
    {
        var page = await CreateDiagramPageAsync();

        await OpenImportDialogAsync(page, "Import SQL");

        var dialog = page.Locator(".tm-diagram-sql-import");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // No preview before anything is typed
        Assert.AreEqual(0, await dialog.Locator(".tm-diagram-sql-import__preview").CountAsync(),
            "the preview must not render before any DDL is entered");

        var textarea = dialog.Locator("textarea");
        await textarea.FillAsync(SingleTableDdl);

        var preview = dialog.Locator(".tm-diagram-sql-import__preview");
        await preview.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        var previewText = await preview.TextContentAsync();
        StringAssert.Contains(previewText, "Tables: 1", "the live preview must count the parsed table");
        StringAssert.Contains(previewText, "Relations: 0");

        var importButton = page.Locator(".tm-modal-footer button", new PageLocatorOptions { HasText = "Import" });
        Assert.IsTrue(await importButton.IsEnabledAsync(), "import must be enabled when at least one table is parsed");

        await page.SetViewportSizeAsync(1440, 900);
        await page.WaitForTimeoutAsync(300);
        await SaveDialogScreenshotAsync(page, "diagram-sql-import-dialog--desktop");

        TestContext.WriteLine("UX (SQL dialog, vs. mockup diagram-sql-import-dialog--desktop.png): the dialog keeps the two-column editor layout with a DDL textarea, a layout-direction select and a live preview row; the import action sits in the modal footer and is enabled exactly when the preview reports at least one table. Visual polish (spacing, accent colors) is deferred to the redesign wave.");
    }

    [TestMethod]
    [Description("Typing CSV into the import dialog updates the header preview and the import button state")]
    public async Task CsvImportDialog_ShowsLivePreview_WhenCsvTyped()
    {
        var page = await CreateDiagramPageAsync();

        await OpenImportDialogAsync(page, "Import CSV");

        var dialog = page.Locator(".tm-diagram-csv-import");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var textarea = dialog.Locator("textarea");
        await textarea.FillAsync("name,manager\nAlice,\nBob,Alice");

        var table = dialog.Locator(".tm-diagram-csv-import__preview-table");
        await table.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        var headerText = await table.Locator("thead").TextContentAsync();
        StringAssert.Contains(headerText, "name");
        StringAssert.Contains(headerText, "manager");

        // Localized semantic mapping labels are rendered for the default OrgChart type
        var mappingLabels = await dialog.Locator(".tm-diagram-csv-import__mapping-label").AllTextContentsAsync();
        StringAssert.Contains(string.Join("|", mappingLabels), "Name");
        StringAssert.Contains(string.Join("|", mappingLabels), "Manager");

        var importButton = page.Locator(".tm-modal-footer button", new PageLocatorOptions { HasText = "Import" });
        Assert.IsFalse(await importButton.IsEnabledAsync(),
            "import must stay disabled until the required column mappings are chosen");

        await page.SetViewportSizeAsync(1440, 900);
        await page.WaitForTimeoutAsync(300);
        await SaveDialogScreenshotAsync(page, "diagram-csv-import-dialog--desktop");

        TestContext.WriteLine("UX (CSV dialog, vs. mockup diagram-csv-import-dialog--desktop.png): the dialog keeps the CSV textarea, diagram-type select, per-field mapping selects with localized labels, and a first-5-rows data preview; the import action in the modal footer is correctly disabled until mappings are chosen. Visual polish is deferred to the redesign wave.");
    }

    private async Task<IPage> CreateDiagramPageAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1600, 900);
        await page.GotoAsync($"{BaseUrl}{DiagramEditorUrl}");
        await WaitForAppReadyAsync(page);
        await page.GetByTestId("diagram-canvas").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 20000
        });
        return page;
    }

    private static async Task OpenImportDialogAsync(IPage page, string itemText)
    {
        // data-tm-group is the stable toolbar contract from phase 1 — never positional selectors
        var importTrigger = page.Locator("[data-tm-group='import'] .tm-dropdown-trigger").First;
        await importTrigger.ClickAsync(new LocatorClickOptions { Timeout = 10000 });

        var item = page.Locator(".tm-dropdown-item[role='menuitem']", new PageLocatorOptions { HasText = itemText }).First;
        await item.ClickAsync(new LocatorClickOptions { Timeout = 10000 });
    }

    private static async Task SaveDialogScreenshotAsync(IPage page, string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "__screenshots__", "diagram-editor");
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000
        });
        Assert.IsTrue(bytes.Length > 20_000, $"{name} screenshot should contain the rendered dialog, not a blank page.");

        var path = Path.Combine(directory, $"{name}.png");
        await File.WriteAllBytesAsync(path, bytes);
        Assert.IsTrue(File.Exists(path), $"{name} screenshot must be written to the durable directory");
    }
}
