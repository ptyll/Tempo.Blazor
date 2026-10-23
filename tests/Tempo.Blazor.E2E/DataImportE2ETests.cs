using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for TmDataImport on the /data-import demo page (WASM demo at 7106). Covers the
/// flagship flow — 5 000-row CSV with 3 hidden errors → dry-run finds them → partial
/// import of the valid rows → corrected follow-up import of the failed rows (continuation)
/// — with database-side asserts through the demo target panel, plus XLSX upload, the
/// windows-1250 + semicolon dialect (edge), the unsupported-file gate (edge), and
/// rollback (edge). Screenshots land in <c>__screenshots__/data-import/</c>.
/// </summary>
[TestClass]
public class DataImportE2ETests : WasmTestBase
{
    private const string DemoPage = "/data-import";

    private sealed record DemoPageHandle(IPage Page, List<string> Errors);

    private async Task<DemoPageHandle> OpenPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);

        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error" && msg.Text.Contains("Unhandled exception"))
            {
                errors.Add(msg.Text);
            }
        };

        await GotoAppReadyAsync(page, $"{BaseUrl}{DemoPage}");

        await page.Locator("[data-testid='import-demo-main'] [data-testid='data-import']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        return new DemoPageHandle(page, errors);
    }

    private static void AssertNoBlazorErrors(DemoPageHandle handle)
        => Assert.AreEqual(0, handle.Errors.Count,
            "The page raised unhandled exceptions: " + string.Join(" | ", handle.Errors));

    private static ILocator Import(IPage page) => page.Locator("[data-testid='import-demo-main']");

    private static async Task UploadAsync(IPage page, string fileName, byte[] bytes, string mimeType = "text/csv")
        => await Import(page).Locator("[data-testid='di-file'] input, input[type=file]").First.SetInputFilesAsync(
            new FilePayload { Name = fileName, MimeType = mimeType, Buffer = bytes });

    private static async Task NextAsync(IPage page)
        => await Import(page).Locator("[data-testid='wizard-next'] button").ClickAsync();

    private static async Task<string> FetchDownloadedTextAsync(IPage page, IDownload download)
        => await page.EvaluateAsync<string>("url => fetch(url).then(r => r.text())", download.Url);

    /// <summary>5 000 rows; rows 120 (empty name), 2500 (bad e-mail) and 4998 (age 999) are broken.</summary>
    private static byte[] BuildLargeCsv()
    {
        var builder = new StringBuilder("Name,Email,Age,City\r\n");
        for (var i = 1; i <= 5000; i++)
        {
            var name = i == 120 ? "" : $"Contact {i}";
            var email = i == 2500 ? "not-an-email" : $"contact{i}@example.com";
            var age = i == 4998 ? "999" : (18 + i % 60).ToString();
            builder.Append(name).Append(',').Append(email).Append(',').Append(age).Append(",Praha\r\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    // ── Flagship flow: 5k rows, 3 errors, dry-run, partial import, continuation ──

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DataImport_5kRowsWith3Errors_DryRunPartialImport_AndContinuation()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var import = Import(page);

        // Upload + preview.
        await UploadAsync(page, "contacts.csv", BuildLargeCsv());
        await Assertions.Expect(import.Locator("[data-testid='di-parse-summary']"))
            .ToContainTextAsync("5000", new LocatorAssertionsToContainTextOptions { Timeout = 60000 });
        await SaveScreenshotAsync(page, "upload-preview-5k");

        // Mapping auto-maps the four columns.
        await NextAsync(page);
        await import.Locator("[data-testid='di-step-mapping']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "mapping");

        // Dry-run finds exactly the three broken rows.
        await NextAsync(page);
        await import.Locator("[data-testid='di-validation-summary']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 180000 });
        var summary = await import.Locator("[data-testid='di-validation-summary']").InnerTextAsync();
        StringAssert.Contains(summary, "4997");
        StringAssert.Contains(summary, "3");
        Assert.AreEqual(3, await import.Locator("[data-testid='di-error-row']").CountAsync());
        await SaveScreenshotAsync(page, "dry-run-errors");

        // The error report names the rows and reasons.
        var errorDownload = await page.RunAndWaitForDownloadAsync(
            () => import.Locator("[data-testid='di-download-errors']").ClickAsync());
        var errorCsv = await FetchDownloadedTextAsync(page, errorDownload);
        StringAssert.Contains(errorCsv, "120");
        StringAssert.Contains(errorCsv, "Invalid e-mail address");
        StringAssert.Contains(errorCsv, "Age must be a number between 0 and 130");

        // Partial import: leave the three rows out.
        await import.Locator("[data-testid='di-skip-invalid']").CheckAsync();
        await NextAsync(page);
        await import.Locator("[data-testid='di-step-import']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await import.Locator("[data-testid='di-start-import']").ClickAsync();
        await import.Locator("[data-testid='di-import-result']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 180000 });
        StringAssert.Contains(
            await import.Locator("[data-testid='di-import-result']").InnerTextAsync(), "4997");
        Assert.AreEqual("100", await import.Locator("[data-testid='di-progress']").GetAttributeAsync("data-percent"));

        // Database-side assert through the demo target panel.
        await Assertions.Expect(page.Locator("[data-testid='import-db-count']"))
            .ToHaveTextAsync("4997", new LocatorAssertionsToHaveTextOptions { Timeout = 30000 });
        await SaveScreenshotAsync(page, "import-result-4997");

        // Continuation: the failed-rows file keeps the schema layout for a corrected re-import.
        var failedDownload = await page.RunAndWaitForDownloadAsync(
            () => import.Locator("[data-testid='di-download-failed']").ClickAsync());
        var failedCsv = await FetchDownloadedTextAsync(page, failedDownload);
        StringAssert.Contains(failedCsv, "Name,Email,Age,City");
        StringAssert.Contains(failedCsv, "not-an-email");

        // Fix the three rows and import them in a follow-up run.
        await import.Locator("[data-testid='wizard-complete'] button").ClickAsync();
        await import.Locator("[data-testid='di-step-upload']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var fixedCsv = "Name,Email,Age,City\r\nContact 120,contact120@example.com,38,Praha\r\n" +
                       "Contact 2500,contact2500@example.com,58,Praha\r\nContact 4998,contact4998@example.com,56,Praha\r\n";
        await UploadAsync(page, "contacts-fixed.csv", Encoding.UTF8.GetBytes(fixedCsv));
        await import.Locator("[data-testid='di-preview']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await NextAsync(page);   // → mapping
        await import.Locator("[data-testid='di-step-mapping']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await NextAsync(page);   // → validation (clean)
        await import.Locator("[data-testid='di-validation-summary']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        await NextAsync(page);   // → import
        await import.Locator("[data-testid='di-start-import']").ClickAsync();
        await import.Locator("[data-testid='di-import-result']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        await Assertions.Expect(page.Locator("[data-testid='import-db-count']"))
            .ToHaveTextAsync("5000", new LocatorAssertionsToHaveTextOptions { Timeout = 30000 });
        await SaveScreenshotAsync(page, "continuation-5000");
        AssertNoBlazorErrors(handle);
    }

    // ── XLSX via the open format ─────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DataImport_XlsxUpload_ParsesAndImports()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var import = Import(page);

        await UploadAsync(page, "contacts.xlsx", BuildXlsx(
            [
                ["Name", "Email", "Age", "City"],
                ["Bedřich Novák", "bedrich@example.com", "46", "Řež"],
                ["Alice Malá", "alice@example.com", "31", "Praha"]
            ]),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        await Assertions.Expect(import.Locator("[data-testid='di-preview']"))
            .ToContainTextAsync("Bedřich Novák", new LocatorAssertionsToContainTextOptions { Timeout = 60000 });
        await SaveScreenshotAsync(page, "xlsx-preview");

        await NextAsync(page);
        await import.Locator("[data-testid='di-step-mapping']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await NextAsync(page);
        await import.Locator("[data-testid='di-validation-summary']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        await NextAsync(page);
        await import.Locator("[data-testid='di-start-import']").ClickAsync();
        await import.Locator("[data-testid='di-import-result']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        await Assertions.Expect(page.Locator("[data-testid='import-db-count']"))
            .ToHaveTextAsync("2", new LocatorAssertionsToHaveTextOptions { Timeout = 30000 });
        StringAssert.Contains(
            await page.Locator("[data-testid='import-demo-db']").InnerTextAsync(), "Bedřich Novák");
        AssertNoBlazorErrors(handle);
    }

    // ── Dialect + encoding and the unsupported-file gate (edge cases) ────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DataImport_Windows1250SemicolonDialect_AndUnsupportedFileGate()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var import = Import(page);

        // Edge: an unsupported extension is rejected with a message, not a crash.
        await UploadAsync(page, "contacts.pdf", Encoding.UTF8.GetBytes("%PDF-1.4"), "application/pdf");
        await import.Locator("[data-testid='di-gate-message']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await SaveScreenshotAsync(page, "edge-unsupported-file");

        // Windows-1250 + semicolons: pick the dialect, then upload the legacy file.
        await import.Locator("[data-testid='di-delimiter']")
            .SelectOptionAsync(new SelectOptionValue { Value = ";" });
        await import.Locator("[data-testid='di-encoding']")
            .SelectOptionAsync(new SelectOptionValue { Value = "windows-1250" });
        var win1250 = Encoding.GetEncoding(1250);
        await UploadAsync(page, "legacy.csv", win1250.GetBytes("Name;Email;Age;City\r\nBedřich Novák;bedrich@example.com;46;Řež\r\n"));

        var preview = import.Locator("[data-testid='di-preview']");
        await Assertions.Expect(preview).ToContainTextAsync("Bedřich Novák",
            new LocatorAssertionsToContainTextOptions { Timeout = 60000 });
        await Assertions.Expect(preview).ToContainTextAsync("Řež",
            new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "edge-dialect-win1250");
        AssertNoBlazorErrors(handle);
    }

    // ── Rollback (edge) ──────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DataImport_Rollback_RestoresTheDatabase()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var import = Import(page);

        var csv = "Name,Email,Age,City\r\nAlice,a@x.com,30,Praha\r\nBob,b@x.com,40,Brno\r\nCara,c@x.com,50,Ostrava\r\n";
        await UploadAsync(page, "small.csv", Encoding.UTF8.GetBytes(csv));
        await import.Locator("[data-testid='di-preview']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await NextAsync(page);
        await import.Locator("[data-testid='di-step-mapping']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await NextAsync(page);
        await import.Locator("[data-testid='di-validation-summary']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        await NextAsync(page);
        await import.Locator("[data-testid='di-start-import']").ClickAsync();
        await import.Locator("[data-testid='di-import-result']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        await Assertions.Expect(page.Locator("[data-testid='import-db-count']"))
            .ToHaveTextAsync("3", new LocatorAssertionsToHaveTextOptions { Timeout = 30000 });

        await import.Locator("[data-testid='di-rollback']").ClickAsync();

        await import.Locator("[data-testid='di-rolled-back']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await Assertions.Expect(page.Locator("[data-testid='import-db-count']"))
            .ToHaveTextAsync("0", new LocatorAssertionsToHaveTextOptions { Timeout = 30000 });
        await SaveScreenshotAsync(page, "edge-rollback");
        AssertNoBlazorErrors(handle);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static byte[] BuildXlsx(string[][] rows)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            var rowIndex = 0u;
            foreach (var cells in rows)
            {
                rowIndex++;
                var row = new Row { RowIndex = rowIndex };
                foreach (var value in cells)
                {
                    row.Append(new Cell
                    {
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text(value))
                    });
                }

                sheetData.Append(row);
            }

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "List1"
            });
        }

        return stream.ToArray();
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "data-import");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{fileName}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
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

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
