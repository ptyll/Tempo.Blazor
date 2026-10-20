using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for TmDataTable K2 ergonomics on the Data Table demo page (WASM demo at 7106):
/// column pinning, multi-column sort, CSV/XLSX export downloads, and validated transaction editing.
/// Screenshots land in <c>__screenshots__/data-table/</c> for UX review.
/// </summary>
[TestClass]
public class DataTableErgonomicsE2ETests : WasmTestBase
{
    private const string DataTablePage = "/data-table";

    private async Task<IPage> OpenPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}{DataTablePage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DataTable_PinAndMultiSort_AndExportCsvAndXlsx()
    {
        var page = await OpenPageAsync();

        var section = page.Locator("[data-testid='dt-ergonomics-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        // Name column is pinned left by default.
        await section.Locator(".tm-col-pinned-left").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // Multi-column sort: plain-click one sortable header, Shift+click another → two precedence badges.
        var sortableHeaders = section.Locator("th[data-sortable='true']");
        await sortableHeaders.Nth(1).ClickAsync();
        await sortableHeaders.Nth(2).ClickAsync(new LocatorClickOptions { Modifiers = new[] { KeyboardModifier.Shift } });

        var badges = section.Locator(".tm-sort-order");
        await badges.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        Assert.AreEqual(2, await badges.CountAsync(), "Two columns should show sort-precedence badges.");
        await SaveScreenshotAsync(page, "ergonomics-pin-multisort");

        var exportSection = page.Locator("[data-testid='dt-export-section']");
        await exportSection.ScrollIntoViewIfNeededAsync();

        // The dedicated export demo registers the optional XLSX service, so both formats share one menu.
        var exportTrigger = exportSection.Locator(".tm-data-table__export .tm-dropdown-trigger");
        await exportTrigger.ClickAsync();
        await Assertions.Expect(exportSection.Locator("[data-export-format='csv']")).ToBeVisibleAsync();
        await Assertions.Expect(exportSection.Locator("[data-export-format='xlsx']")).ToBeVisibleAsync();
        await SaveElementScreenshotAsync(exportSection, "xlsx-export-menu-light");
        await ToggleDarkModeAsync(page);
        // The theme switch re-renders the page and closes the menu, so reopen it for the dark shot.
        await exportTrigger.ClickAsync();
        await Assertions.Expect(exportSection.Locator("[data-export-format='csv']")).ToBeVisibleAsync();
        await SaveElementScreenshotAsync(exportSection, "xlsx-export-menu-dark");

        // Close and reopen the menu to exercise the keyboard edge case before downloading.
        await exportTrigger.PressAsync("Escape");
        await Assertions.Expect(exportSection.Locator("[data-export-format='csv']")).ToHaveCountAsync(0);
        await exportTrigger.ClickAsync();

        // CSV export contains the complete transaction set and a UTF-8 BOM.
        var download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await exportSection.Locator("[data-export-format='csv']").ClickAsync();
        });
        Assert.AreEqual("transaction-export-demo.csv", download.SuggestedFilename);
        var path = await download.PathAsync();
        Assert.IsNotNull(path);
        var bytes = await File.ReadAllBytesAsync(path);
        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        Assert.AreEqual(4, File.ReadAllLines(path).Length,
            "CSV should contain one header and all three transactions.");

        // XLSX uses the same transaction set and produces an Open XML workbook a real reader accepts.
        await exportTrigger.ClickAsync();
        var xlsxDownload = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await exportSection.Locator("[data-export-format='xlsx']").ClickAsync();
        });
        Assert.AreEqual("transaction-export-demo.xlsx", xlsxDownload.SuggestedFilename);
        var xlsxPath = await xlsxDownload.PathAsync();
        Assert.IsNotNull(xlsxPath);
        using var workbook = SpreadsheetDocument.Open(xlsxPath, isEditable: false);
        var xlsxRows = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet
            .GetFirstChild<SheetData>()!.Elements<Row>().ToList();
        Assert.AreEqual(4, xlsxRows.Count,
            "XLSX should contain one header and all three transactions.");
        Assert.AreEqual(CellValues.Number,
            xlsxRows[1].Elements<Cell>().ElementAt(1).DataType?.Value,
            "The decimal Amount column should remain numeric in XLSX.");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DataTable_ExportSelected_DownloadsOnlyTheCheckedRows()
    {
        var page = await OpenPageAsync();

        var exportSection = page.Locator("[data-testid='dt-export-section']");
        await exportSection.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await exportSection.ScrollIntoViewIfNeededAsync();

        var exportTrigger = exportSection.Locator(".tm-data-table__export .tm-dropdown-trigger");
        await exportTrigger.ClickAsync();
        var selectedScope = exportSection.Locator("[data-export-scope='selected']");
        await Assertions.Expect(selectedScope).ToHaveAttributeAsync("aria-disabled", "true");
        Assert.IsNull(await selectedScope.GetAttributeAsync("disabled"),
            "aria-disabled stays focusable; a native disabled attribute would not");
        await Assertions.Expect(selectedScope).ToContainTextAsync("Select rows to export them");
        // Opening moves focus to the first item. ArrowDown must still reach the empty choice:
        // a native disabled button is skipped, an aria-disabled one stays in the roving order.
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.getAttribute('data-export-scope') === 'all'",
            null, new PageWaitForFunctionOptions { Timeout = 10000 });
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(selectedScope).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");

        var rowChecks = exportSection.Locator("tbody tr td input[type='checkbox']");
        await rowChecks.Nth(0).CheckAsync();
        await rowChecks.Nth(2).CheckAsync();

        await exportTrigger.ClickAsync();
        await Assertions.Expect(selectedScope).Not.ToHaveAttributeAsync("aria-disabled", "true");
        await Assertions.Expect(selectedScope).ToContainTextAsync("Selected rows (2)");
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.getAttribute('data-export-scope') === 'all'",
            null, new PageWaitForFunctionOptions { Timeout = 10000 });
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(selectedScope).ToBeFocusedAsync();
        await page.Keyboard.PressAsync(" ");
        await Assertions.Expect(exportSection.Locator("[data-export-format='csv']")).ToBeVisibleAsync();

        var download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await exportSection.Locator("[data-export-format='csv']").PressAsync("Enter");
        });
        var path = await download.PathAsync();
        Assert.IsNotNull(path);
        Assert.AreEqual(3, File.ReadAllLines(path).Length,
            "CSV should contain the header and the two checked transactions, not the unchecked one.");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DataTable_InlineEdit_ActionsKeyboardAndSingleRowMode_WorkInLightAndDarkThemes()
    {
        var page = await OpenPageAsync();

        var section = page.Locator("[data-testid='dt-inline-edit-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var editButtons = section.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit row" });
        Assert.AreEqual(3, await editButtons.CountAsync(), "Every editable row should expose an accessible Edit row action.");
        await editButtons.First.ClickAsync();

        var editing = section.Locator(".tm-data-table-row--editing");
        await editing.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        Assert.AreEqual(1, await editing.CountAsync(), "Only one row may be edited at a time.");
        await section.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).WaitForAsync();
        await section.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel" }).WaitForAsync();

        // Starting a second row replaces the active edit row instead of leaving two editing rows.
        await section.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit row" }).First.ClickAsync();
        Assert.AreEqual(1, await editing.CountAsync(), "Starting another row must preserve the single-row edit invariant.");
        Assert.AreEqual(49.5m, decimal.Parse(
            await editing.Locator("input[type='number']").InputValueAsync(),
            CultureInfo.InvariantCulture));

        await SaveElementScreenshotAsync(section, "inline-edit-actions-light");
        await ToggleDarkModeAsync(page);
        await SaveElementScreenshotAsync(section, "inline-edit-actions-dark");

        await editing.Locator("input").First.PressAsync("Escape");
        await Assertions.Expect(editing).ToHaveCountAsync(0);

        await section.Locator("tbody tr").First.DblClickAsync();
        await Assertions.Expect(editing).ToHaveCountAsync(1);
        await editing.Locator("input").First.PressAsync("Enter");
        await Assertions.Expect(editing).ToHaveCountAsync(0);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DataTable_InlineEdit_InvalidRow_ShowsMessagesAndRecovers()
    {
        var page = await OpenPageAsync();
        var section = page.Locator("[data-testid='dt-inline-edit-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        await section.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit row" }).First.ClickAsync();
        var editing = section.Locator(".tm-data-table-row--editing");
        var amount = editing.Locator("[data-testid='transaction-amount-input']");
        var note = editing.Locator("[data-testid='transaction-note-input']");
        await amount.FillAsync("0");
        await note.FillAsync("");
        await section.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        await Assertions.Expect(editing).ToHaveClassAsync(new Regex("tm-data-table-row--invalid"));
        var validationMessages = editing.Locator(".validation-message");
        await Assertions.Expect(validationMessages.Nth(0)).ToContainTextAsync("between 0.01 and 1000000");
        await Assertions.Expect(validationMessages.Nth(1)).ToContainTextAsync("required");
        await SaveElementScreenshotAsync(section, "inline-edit-validation-light");

        await ToggleDarkModeAsync(page);
        await SaveElementScreenshotAsync(section, "inline-edit-validation-dark");

        await amount.FillAsync("88.25");
        await note.FillAsync("Updated expense");
        await section.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();
        await Assertions.Expect(editing).ToHaveCountAsync(0);
        await Assertions.Expect(section.Locator("tbody tr").First).ToContainTextAsync("88.25");
        await Assertions.Expect(section.Locator("[data-testid='dt-inline-save-result']")).ToContainTextAsync("OnRowSave");
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "data-table");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png"), FullPage = true });
    }

    private static async Task SaveElementScreenshotAsync(ILocator locator, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "data-table");
        Directory.CreateDirectory(dir);
        await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png") });
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

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
