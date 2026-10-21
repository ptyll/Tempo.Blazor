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

        // Scoped to the default table: the section also demonstrates EditActionsMode.Footer in a
        // second table, which exposes its own Edit row actions.
        var defaultTable = section.Locator("[data-testid='transaction-table']");
        var editButtons = defaultTable.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit row" });
        Assert.AreEqual(3, await editButtons.CountAsync(), "Every editable row should expose an accessible Edit row action.");
        await editButtons.First.ClickAsync();

        var editing = section.Locator(".tm-data-table-row--editing");
        await editing.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        Assert.AreEqual(1, await editing.CountAsync(), "Only one row may be edited at a time.");
        await section.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).WaitForAsync();
        await section.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel" }).WaitForAsync();

        // Starting a second row replaces the active edit row instead of leaving two editing rows.
        await defaultTable.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit row" }).First.ClickAsync();
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
    public async Task DataTable_InlineEdit_FooterActions_FitTheirLabelsAndDoNotOverlap()
    {
        // EditActionsMode.Footer renders icon + visible label, but the base rule squeezes every
        // edit action into a 32px icon square: the label would overflow and the legacy sibling
        // margin would double the flex gap. Both are guarded here by live geometry.
        var page = await OpenPageAsync();

        var table = page.Locator("[data-testid='transaction-table-footer']");
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await table.ScrollIntoViewIfNeededAsync();

        await table.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit row" }).First.ClickAsync();
        var footer = table.Locator(".tm-data-table__edit-footer-actions");
        await footer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        var save = footer.Locator(".tm-data-table__edit-action--save");
        var cancel = footer.Locator(".tm-data-table__edit-action--cancel");

        var geometry = await save.EvaluateAsync<string>(@"el => {
            const r = el.getBoundingClientRect();
            const pair = el.nextElementSibling ? el.nextElementSibling.getBoundingClientRect() : null;
            return JSON.stringify({
                width: r.width,
                scrollWidth: el.scrollWidth,
                nextX: pair ? pair.x : null,
                end: r.x + r.width
            });
        }");
        using var doc = System.Text.Json.JsonDocument.Parse(geometry);
        var root = doc.RootElement;
        Assert.IsTrue(root.GetProperty("width").GetDouble() >= root.GetProperty("scrollWidth").GetDouble() - 0.5,
            $"the Save button must fit its label (width {root.GetProperty("width")}, scrollWidth {root.GetProperty("scrollWidth")})");
        var nextX = root.GetProperty("nextX");
        Assert.IsFalse(nextX.ValueKind == System.Text.Json.JsonValueKind.Null, "the Cancel button must sit beside Save");
        Assert.IsTrue(nextX.GetDouble() >= root.GetProperty("end").GetDouble() - 0.5,
            "the Save and Cancel buttons must not overlap");
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

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DataTable_ExportSelected_DisabledScopeHint_StaysReadable()
    {
        // WCAG 1.4.3: the unavailable CONTROL may fade, the instructional text must not. The
        // "Select rows to export them" hint has to keep opacity 1 and >=4.5:1 contrast against
        // the menu surface in both themes while the option is aria-disabled. Opacity is read
        // from the export-scope ITEM (the element the generic dropdown fade actually targets) —
        // opacity does not inherit, so the description span's own computed value would read 1
        // even while its ancestor fades it out of visibility.
        var page = await OpenPageAsync();
        var section = page.Locator("[data-testid='dt-export-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();
        var trigger = section.Locator(".tm-data-table__export .tm-dropdown-trigger");

        const string probe = """
            () => {
                const description = document.querySelector("[data-export-scope='selected'] .tm-data-table__export-description");
                if (!description) return null;
                const scope = description.closest('[data-export-scope]');
                if (!scope) return null;
                const parse = value => (value.match(/[\d.]+/g) ?? []).map(Number);
                const channel = v => { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
                const luminance = rgb => 0.2126 * channel(rgb[0]) + 0.7152 * channel(rgb[1]) + 0.0722 * channel(rgb[2]);
                let node = description, background = null;
                while (node && node !== document.documentElement) {
                    const c = parse(getComputedStyle(node).backgroundColor);
                    if (c.length >= 3 && (c.length < 4 || c[3] > 0)) { background = c.slice(0, 3); break; }
                    node = node.parentElement;
                }
                if (!background) return null;
                const ink = parse(getComputedStyle(description).color).slice(0, 3);
                const l1 = luminance(ink), l2 = luminance(background);
                const contrast = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
                return JSON.stringify({ opacity: getComputedStyle(scope).opacity, contrast });
            }
            """;

        foreach (var (theme, dark) in new[] { ("light", false), ("dark", true) })
        {
            if (dark) await ToggleDarkModeAsync(page);

            await trigger.ClickAsync();
            var state = await page.WaitForFunctionAsync(
                probe, null, new PageWaitForFunctionOptions { Timeout = 10000 });
            var json = await state.JsonValueAsync<string>();

            Assert.IsNotNull(json, $"the disabled scope hint was not rendered in {theme} mode");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.AreEqual("1", doc.RootElement.GetProperty("opacity").GetString(),
                $"hint opacity must stay 1 in {theme} mode (the disabled fade must not cover the hint)");
            var contrast = doc.RootElement.GetProperty("contrast").GetDouble();
            Assert.IsTrue(contrast >= 4.5, $"hint contrast must be >= 4.5:1 in {theme} mode, got {contrast:0.00}");
            await page.Keyboard.PressAsync("Escape");
        }
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
