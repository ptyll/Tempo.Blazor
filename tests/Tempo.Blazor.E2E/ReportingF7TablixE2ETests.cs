using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Tempo.Blazor.E2E.CanvasEngine;
using Tempo.Reporting.Abstractions;
using Tempo.Reporting.Abstractions.Data;
using Tempo.Reporting.Abstractions.Definitions;
using Tempo.Reporting.Engine.Fonts;
using Tempo.Reporting.Engine.Layout;
using Tempo.Reporting.Engine.Processing;
using Tempo.Reporting.Engine.Snapshot;
using DataFieldType = Tempo.Reporting.Abstractions.Data.ReportDataFieldType;

namespace Tempo.Blazor.E2E;

/// <summary>F7 reporting tablix visual gate.</summary>
[TestClass]
[TestCategory("Reporting")]
[TestCategory("Reporting:F7")]
[DoNotParallelize]
public sealed class ReportingF7TablixE2ETests
{
    private const string LogoDataUri =
        "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='96' height='96' viewBox='0 0 96 96'%3E%3Crect width='96' height='96' rx='18' fill='%230f172a'/%3E%3Cpath d='M24 30h48v10H54v34H42V40H24z' fill='%23ffffff'/%3E%3Ccircle cx='70' cy='70' r='8' fill='%232563eb'/%3E%3C/svg%3E";

    public TestContext TestContext { get; set; } = default!;

    [TestMethod]
    public async Task F7_ReportingHarness_RendersInvoiceAndGroupedSalesScreenshots()
    {
        var invoice = BuildInvoiceSnapshot();
        var sales = BuildSalesSnapshot(out var salesTotal);
        Assert.AreEqual(2, invoice.Pages.Count, "The F7 invoice fixture must remain a two-page report after switching line items to a tablix.");
        Assert.IsTrue(sales.Pages.Count >= 30, $"The F7 sales fixture must produce at least 30 pages. Actual: {sales.Pages.Count}.");
        Assert.IsTrue(
            sales.Pages.Last().Commands.Any(command =>
                command.Type == ReportSnapshotCommandType.TextRun &&
                string.Equals(command.Text, salesTotal.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)),
            "The sales report final table footer must contain the report-level aggregate total.");

        await using var host = ReportingHarnessHost.Start(FindRepositoryRoot());
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 920, Height = 1040 },
        });
        var page = await browserContext.NewPageAsync();
        await page.GotoAsync($"{host.BaseUrl}/reporting-harness.html", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000,
        });
        await PlaywrightTestBase.WaitForPredicateAsync(page, "() => window.__tempoReportingHarness?.ready === true", 30_000);

        var screenshotDirectory = CreateScreenshotDirectory();
        var results = new List<object>
        {
            await RenderSnapshotPageAsync(page, invoice, invoice.Pages[0], Path.Combine(screenshotDirectory, "01-invoice-p1.png")).ConfigureAwait(false),
            await RenderSnapshotPageAsync(page, invoice, invoice.Pages[1], Path.Combine(screenshotDirectory, "02-invoice-p2.png")).ConfigureAwait(false),
            await RenderSnapshotPageAsync(page, sales, sales.Pages[0], Path.Combine(screenshotDirectory, "03-sales-p1.png")).ConfigureAwait(false),
            await RenderSnapshotPageAsync(page, sales, sales.Pages[sales.Pages.Count / 2], Path.Combine(screenshotDirectory, $"04-sales-p{sales.Pages[sales.Pages.Count / 2].PageNumber}.png")).ConfigureAwait(false),
            await RenderSnapshotPageAsync(page, sales, sales.Pages[^1], Path.Combine(screenshotDirectory, $"05-sales-p{sales.Pages[^1].PageNumber}.png")).ConfigureAwait(false),
        };

        var manifestPath = Path.Combine(screenshotDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                new
                {
                    phase = "F7",
                    testName = nameof(F7_ReportingHarness_RendersInvoiceAndGroupedSalesScreenshots),
                    invoicePages = invoice.Pages.Count,
                    salesPages = sales.Pages.Count,
                    salesTotal,
                    functionalReview = "The invoice line items are rendered by a ReportTableElement. The sales fixture uses three grouping levels, repeated table headers, group footers and a report-level aggregate footer across 30+ pages.",
                    uxReview = "Both tablix fixtures keep numeric columns right aligned, use compact readable row padding, repeat headers on page breaks and avoid visible text collisions.",
                    pages = results,
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })).ConfigureAwait(false);
        TestContext.AddResultFile(manifestPath);
        await browserContext.CloseAsync();
    }

    private async Task<object> RenderSnapshotPageAsync(IPage page, ReportSnapshot snapshot, ReportSnapshotPage snapshotPage, string screenshotPath)
    {
        var summary = await LoadPageSnapshotAsync(page, snapshot, snapshotPage).ConfigureAwait(false);
        Assert.IsTrue(summary.TextRunCount > 20, $"Page {snapshotPage.PageNumber} must paint a substantial tablix text layout.");
        Assert.IsTrue(summary.PaintedCommandCount >= summary.TextRunCount + 8, $"Page {snapshotPage.PageNumber} must paint text plus table primitives.");
        await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.GetByTestId("reporting-canvas")).ConfigureAwait(false);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page).ConfigureAwait(false);

        await page.GetByTestId("reporting-harness-page").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = screenshotPath,
            Type = ScreenshotType.Png,
        }).ConfigureAwait(false);
        TestContext.AddResultFile(screenshotPath);
        return new
        {
            snapshot.SnapshotId,
            snapshotPage.PageNumber,
            screenshotPath,
            summary.TextRunCount,
            summary.PaintedCommandCount,
        };
    }

    internal static ReportSnapshot BuildInvoiceSnapshot()
    {
        var lines = InvoiceLines();
        var dataSet = InvoiceDataSet(lines);
        var subtotal = lines.Sum(line => line.Amount);
        var tax = decimal.Round(subtotal * 0.21m, 2);
        var total = subtotal + tax;
        var definition = new ReportDefinition
        {
            Id = "f7-invoice",
            Name = "F7 Invoice",
            PageSetup = new ReportPageSetup
            {
                PageSize = new ReportPageSize(640, 860),
                Margins = new ReportThickness(40),
            },
            Bands = new ReportBandCollection
            {
                PageHeader = CreateInvoicePageHeaderBand(),
                PageFooter = CreatePageFooterBand(),
            },
        };
        var table = CreateInvoiceTable();
        var tableBand = new ReportBand
        {
            Kind = ReportBandKind.Detail,
            Height = 540,
            Elements = [table],
        };
        var bands = new[]
        {
            Instance(CreateInvoiceHeaderBand(total)),
            new ReportBandInstance(ReportBandKind.Detail, null, null, [new ReportElementInstance(table, null, null)], sourceBand: tableBand),
            Instance(CreateInvoiceFooterBand(subtotal, tax, total)),
        };
        var context = Context(dataSet);
        return ReportSnapshotGenerator.Generate(
            new ReportInstance(definition, bands, context.DataSets, context),
            new F7FixedTextMeasurer(),
            new ReportSnapshotGeneratorOptions { SnapshotId = "f7-invoice", MinimumOrphanHeight = 24 });
    }

    internal static ReportSnapshot BuildSalesSnapshot(out decimal expectedTotal)
    {
        var rows = SalesRows();
        expectedTotal = rows.Sum(row => row.Amount);
        var dataSet = SalesDataSet(rows);
        var definition = new ReportDefinition
        {
            Id = "f7-sales",
            Name = "F7 Sales by Region",
            PageSetup = new ReportPageSetup
            {
                PageSize = new ReportPageSize(640, 430),
                Margins = new ReportThickness(30),
            },
            Bands = new ReportBandCollection
            {
                PageHeader = new ReportBand
                {
                    Kind = ReportBandKind.PageHeader,
                    Height = 42,
                    Elements =
                    [
                        TextBox("sales-title", "Sales by region", 0, 0, 300, 24, Style(15, bold: true)),
                        TextBox("sales-period", "Generated fixture | 3 grouping levels", 0, 24, 280, 14, Style(8.5, color: "#64748b")),
                        TextBox("sales-page", "Page PageNumber / TotalPages", 400, 12, 180, 12, Style(8.5, color: "#475569"), ReportHorizontalAlignment.Right),
                    ],
                },
                PageFooter = new ReportBand
                {
                    Kind = ReportBandKind.PageFooter,
                    Height = 24,
                    Elements =
                    [
                        new ReportLineElement { Id = "sales-footer-rule", X = 0, Y = 0, Width = 580, Height = 0, Stroke = new ReportBorderLine("#dbe3ef", 1) },
                        TextBox("sales-footer", "F7 tablix visual gate", 0, 8, 220, 12, Style(8, color: "#64748b")),
                    ],
                },
            },
        };
        var table = CreateSalesTable();
        var tableBand = new ReportBand
        {
            Kind = ReportBandKind.Detail,
            Height = 320,
            Elements = [table],
        };
        var context = Context(dataSet);
        return ReportSnapshotGenerator.Generate(
            new ReportInstance(
                definition,
                [new ReportBandInstance(ReportBandKind.Detail, null, null, [new ReportElementInstance(table, null, null)], sourceBand: tableBand)],
                context.DataSets,
                context),
            new F7FixedTextMeasurer(),
            new ReportSnapshotGeneratorOptions { SnapshotId = "f7-sales", MinimumOrphanHeight = 20 });
    }

    private static ReportBand CreateInvoicePageHeaderBand()
        => new()
        {
            Kind = ReportBandKind.PageHeader,
            Height = 70,
            Elements =
            [
                new ReportImageElement { Id = "brand-logo", SourceKind = ReportImageSourceKind.Embedded, Source = LogoDataUri, X = 0, Y = 4, Width = 46, Height = 46 },
                TextBox("brand-name", "Tempo Components s.r.o.", 58, 7, 260, 18, Style(13, bold: true, color: "#0f172a")),
                TextBox("brand-address", "Krizikova 12, 186 00 Praha 8", 58, 28, 260, 14, Style(9, color: "#475569")),
                TextBox("brand-contact", "billing@tempo.example  |  +420 222 000 111", 58, 44, 260, 14, Style(9, color: "#64748b")),
                TextBox("document-title", "INVOICE.", 380, 10, 180, 26, Style(20, bold: true, color: "#0f172a"), ReportHorizontalAlignment.Right),
                new ReportLineElement { Id = "header-rule", X = 0, Y = 62, Width = 560, Height = 0, Stroke = new ReportBorderLine("#dbe3ef", 1) },
            ],
        };

    private static ReportBand CreatePageFooterBand()
        => new()
        {
            Kind = ReportBandKind.PageFooter,
            Height = 38,
            Elements =
            [
                new ReportLineElement { Id = "footer-rule", X = 0, Y = 0, Width = 560, Height = 0, Stroke = new ReportBorderLine("#dbe3ef", 1) },
                TextBox("footer-note", "tempo.example | payment due net 14", 0, 11, 280, 14, Style(8.5, color: "#64748b")),
                TextBox("page-number", "Page PageNumber / TotalPages", 400, 11, 160, 14, Style(8.5, color: "#475569"), ReportHorizontalAlignment.Right),
            ],
        };

    private static ReportBand CreateInvoiceHeaderBand(decimal total)
        => new()
        {
            Kind = ReportBandKind.ReportHeader,
            Height = 146,
            KeepTogether = true,
            Elements =
            [
                TextBox("invoice-number", "Invoice #2026-007", 0, 8, 240, 24, Style(17, bold: true, color: "#111827")),
                TextBox("invoice-date", "Issued 22 Jun 2026", 390, 12, 170, 16, Style(10, color: "#475569"), ReportHorizontalAlignment.Right),
                TextBox("due-date", "Due 06 Jul 2026", 390, 30, 170, 16, Style(10, bold: true, color: "#1d4ed8"), ReportHorizontalAlignment.Right),
                new ReportShapeElement { Id = "bill-to-box", X = 0, Y = 54, Width = 264, Height = 74, FillColor = "#f8fafc", Border = ReportBorder.All("#e2e8f0", 1) },
                TextBox("bill-to-label", "Bill to", 14, 64, 120, 14, Style(8.5, bold: true, color: "#64748b")),
                TextBox("bill-to", "Northwind Retail a.s.\nVaclavske namesti 21\n110 00 Praha 1", 14, 82, 220, 42, Style(10), padding: new ReportThickness(0), canGrow: true),
                new ReportShapeElement { Id = "summary-box", X = 304, Y = 54, Width = 256, Height = 74, FillColor = "#eff6ff", Border = ReportBorder.All("#bfdbfe", 1) },
                TextBox("summary-label", "Amount due", 318, 66, 110, 14, Style(8.5, bold: true, color: "#1d4ed8")),
                TextBox("summary-total", Money(total), 412, 82, 132, 24, Style(18, bold: true), ReportHorizontalAlignment.Right),
            ],
        };

    private static ReportTableElement CreateInvoiceTable()
        => new()
        {
            Id = "invoice-lines",
            DataSetName = "InvoiceLines",
            X = 0,
            Y = 0,
            Width = 560,
            Height = 520,
            RepeatHeaderOnNewPage = true,
            ZebraStripeColor = "#f8fafc",
            Columns =
            [
                new ReportTableColumn("Description", 292),
                new ReportTableColumn("Qty", 52),
                new ReportTableColumn("Unit", 88),
                new ReportTableColumn("Amount", 128),
            ],
            Header = TableRow(26, "#0f172a", HeaderCell("Description"), HeaderCell("Qty", ReportHorizontalAlignment.Right), HeaderCell("Unit", ReportHorizontalAlignment.Right), HeaderCell("Amount", ReportHorizontalAlignment.Right)),
            Detail = TableRow(
                28,
                null,
                Cell("=Fields.Description"),
                Cell("=Fields.Quantity", ReportHorizontalAlignment.Right),
                Cell("=Fields.UnitText", ReportHorizontalAlignment.Right),
                Cell("=Fields.AmountText", ReportHorizontalAlignment.Right, bold: true)),
        };

    private static ReportBand CreateInvoiceFooterBand(decimal subtotal, decimal tax, decimal total)
        => new()
        {
            Kind = ReportBandKind.ReportFooter,
            Height = 96,
            KeepTogether = true,
            Elements =
            [
                new ReportLineElement { Id = "total-rule", X = 314, Y = 8, Width = 246, Height = 0, Stroke = new ReportBorderLine("#cbd5e1", 1) },
                TextBox("subtotal-label", "Subtotal", 334, 20, 90, 14, Style(9.5, color: "#475569")),
                TextBox("subtotal-value", Money(subtotal), 438, 20, 110, 14, Style(9.5), ReportHorizontalAlignment.Right),
                TextBox("tax-label", "VAT 21%", 334, 42, 90, 14, Style(9.5, color: "#475569")),
                TextBox("tax-value", Money(tax), 438, 42, 110, 14, Style(9.5), ReportHorizontalAlignment.Right),
                new ReportShapeElement { Id = "grand-total-fill", X = 318, Y = 64, Width = 242, Height = 28, FillColor = "#eff6ff", Border = ReportBorder.All("#bfdbfe", 1) },
                TextBox("grand-total-label", "Total", 334, 71, 90, 14, Style(10, bold: true, color: "#1d4ed8")),
                TextBox("grand-total-value", Money(total), 438, 69, 110, 16, Style(12, bold: true), ReportHorizontalAlignment.Right),
            ],
        };

    private static ReportTableElement CreateSalesTable()
        => new()
        {
            Id = "sales-table",
            DataSetName = "Sales",
            X = 0,
            Y = 0,
            Width = 580,
            Height = 300,
            RepeatHeaderOnNewPage = true,
            ZebraStripeColor = "#f8fafc",
            Columns =
            [
                new ReportTableColumn("Region", 110),
                new ReportTableColumn("Category", 120),
                new ReportTableColumn("Bucket", 80),
                new ReportTableColumn("Customer", 150),
                new ReportTableColumn("Amount", 120),
            ],
            Header = TableRow(18, "#0f172a", HeaderCell("Region"), HeaderCell("Category"), HeaderCell("Bucket"), HeaderCell("Customer"), HeaderCell("Amount", ReportHorizontalAlignment.Right)),
            Groups =
            [
                new ReportTableGroupDefinition { Name = "Region", Expression = "=Fields.Region", Header = TableRow(18, "#dbeafe", Cell("Region", bold: true), Cell("=Fields.Region", bold: true), Cell(""), Cell(""), Cell("")), Footer = TableRow(18, "#eff6ff", Cell("Region total", bold: true), Cell(""), Cell(""), Cell(""), Cell("=Sum(Fields.Amount)", ReportHorizontalAlignment.Right, bold: true)) },
                new ReportTableGroupDefinition { Name = "Category", Expression = "=Fields.Category", Header = TableRow(18, "#f1f5f9", Cell(""), Cell("=Fields.Category", bold: true), Cell(""), Cell(""), Cell("")), Footer = TableRow(18, null, Cell(""), Cell("Category total", bold: true), Cell(""), Cell(""), Cell("=Sum(Fields.Amount)", ReportHorizontalAlignment.Right, bold: true)) },
                new ReportTableGroupDefinition { Name = "Bucket", Expression = "=Fields.Bucket", Header = TableRow(18, null, Cell(""), Cell(""), Cell("=Fields.Bucket", bold: true), Cell(""), Cell("")), Footer = TableRow(18, null, Cell(""), Cell(""), Cell("Bucket total", bold: true), Cell(""), Cell("=Sum(Fields.Amount)", ReportHorizontalAlignment.Right, bold: true)) },
            ],
            Detail = TableRow(18, null, Cell("=Fields.Region"), Cell("=Fields.Category"), Cell("=Fields.Bucket"), Cell("=Fields.Customer"), Cell("=Fields.Amount", ReportHorizontalAlignment.Right)),
            Footer = TableRow(20, "#e0f2fe", Cell("Report total", bold: true), Cell(""), Cell(""), Cell(""), Cell("=Sum(Fields.Amount, \"report\")", ReportHorizontalAlignment.Right, bold: true)),
        };

    private static ReportTableRow TableRow(double height, string? background, params ReportTableCell[] cells)
        => new()
        {
            Height = height,
            BackgroundColor = background,
            Cells = cells.ToList(),
        };

    private static ReportTableCell HeaderCell(string text, ReportHorizontalAlignment alignment = ReportHorizontalAlignment.Left)
        => Cell(text, alignment, bold: true, color: "#ffffff");

    private static ReportTableCell Cell(
        string value,
        ReportHorizontalAlignment alignment = ReportHorizontalAlignment.Left,
        bool bold = false,
        string color = "#0f172a")
        => new()
        {
            Text = value.StartsWith('=') ? null : value,
            Expression = value.StartsWith('=') ? value : null,
            HorizontalAlignment = alignment,
            TextStyle = Style(8.5, bold, color),
            Padding = new ReportThickness(5, 3, 5, 3),
            CanGrow = true,
        };

    private static ReportTextBoxElement TextBox(
        string id,
        string text,
        double x,
        double y,
        double width,
        double height,
        ReportTextStyle style,
        ReportHorizontalAlignment horizontalAlignment = ReportHorizontalAlignment.Left,
        ReportThickness? padding = null,
        bool canGrow = false)
        => new()
        {
            Id = id,
            Text = text,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            TextStyle = style,
            HorizontalAlignment = horizontalAlignment,
            Padding = padding ?? new ReportThickness(0),
            CanGrow = canGrow,
        };

    private static ReportBandInstance Instance(ReportBand band)
    {
        var elements = band.Elements
            .Select(element => element is ReportTextBoxElement textBox
                ? new ReportTextBoxInstance(textBox, textBox.Text, textBox.Text ?? string.Empty)
                : new ReportElementInstance(element, null, null))
            .ToArray();
        return new ReportBandInstance(band.Kind, null, null, elements, sourceBand: band);
    }

    private static ReportProcessingContext Context(ProcessedDataSet dataSet)
        => new(
            new ReportExecutionContext("tenant", "user", "en-US"),
            dataSets: new Dictionary<string, ProcessedDataSet>(StringComparer.Ordinal) { [dataSet.Name] = dataSet });

    private static ReportTextStyle Style(double size, bool bold = false, string color = "#111827")
        => new() { FontFamily = "Arial", FontSize = size, Bold = bold, Color = color, LineHeight = 1.12 };

    private static IReadOnlyList<InvoiceLine> InvoiceLines()
        =>
        [
            new(1, "Component library license - Core", 1, 1200m),
            new(2, "Design system implementation workshop", 2, 420m),
            new(3, "Blazor reporting prototype", 1, 980m),
            new(4, "Accessibility review", 3, 155m),
            new(5, "Localization setup", 1, 360m),
            new(6, "Theme token audit", 2, 210m),
            new(7, "Data table integration support", 4, 135m),
            new(8, "Chart configuration package", 1, 315m),
            new(9, "Dashboard layout consultation", 2, 185m),
            new(10, "PDF viewer onboarding", 1, 240m),
            new(11, "Scheduler styling pass", 2, 165m),
            new(12, "Form validation integration", 3, 145m),
            new(13, "Command palette setup", 1, 195m),
            new(14, "Icon registry migration", 2, 115m),
            new(15, "Kanban board customization", 2, 175m),
            new(16, "Import wizard configuration", 1, 285m),
            new(17, "Export options hardening", 1, 215m),
            new(18, "Tree view performance pass", 2, 128m),
            new(19, "Timeline visual tuning", 1, 188m),
            new(20, "Attachment manager policy setup", 1, 205m),
            new(21, "Search input interaction review", 3, 88m),
            new(22, "Spreadsheet formulas spike", 1, 530m),
            new(23, "Report server planning session", 2, 225m),
            new(24, "Release readiness checklist", 1, 174.5m),
        ];

    private static IReadOnlyList<SalesLine> SalesRows()
    {
        var rows = new List<SalesLine>();
        var regions = new[] { "North", "South", "West" };
        var categories = new[] { "Platform", "Services", "Training" };
        var buckets = new[] { "SMB", "Mid", "Enterprise" };
        var index = 1;
        foreach (var region in regions)
        {
            foreach (var category in categories)
            {
                foreach (var bucket in buckets)
                {
                    for (var i = 1; i <= 18; i++)
                    {
                        var amount = 80m + index % 17 * 13m + i * 2m;
                        rows.Add(new SalesLine(region, category, bucket, $"Customer {index:000}", amount));
                        index++;
                    }
                }
            }
        }

        return rows;
    }

    private static ProcessedDataSet InvoiceDataSet(IEnumerable<InvoiceLine> rows)
        => new(
            "InvoiceLines",
            [
                new ReportDataColumn("Description", DataFieldType.String),
                new ReportDataColumn("Quantity", DataFieldType.Number),
                new ReportDataColumn("UnitText", DataFieldType.String),
                new ReportDataColumn("Amount", DataFieldType.Number),
                new ReportDataColumn("AmountText", DataFieldType.String),
            ],
            rows.Select(row => new ProcessedDataRow(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Description"] = row.Description,
                ["Quantity"] = row.Quantity,
                ["UnitText"] = Money(row.UnitPrice),
                ["Amount"] = row.Amount,
                ["AmountText"] = Money(row.Amount),
            })).ToArray());

    private static ProcessedDataSet SalesDataSet(IEnumerable<SalesLine> rows)
        => new(
            "Sales",
            [
                new ReportDataColumn("Region", DataFieldType.String),
                new ReportDataColumn("Category", DataFieldType.String),
                new ReportDataColumn("Bucket", DataFieldType.String),
                new ReportDataColumn("Customer", DataFieldType.String),
                new ReportDataColumn("Amount", DataFieldType.Number),
            ],
            rows.Select(row => new ProcessedDataRow(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Region"] = row.Region,
                ["Category"] = row.Category,
                ["Bucket"] = row.Bucket,
                ["Customer"] = row.Customer,
                ["Amount"] = row.Amount,
            })).ToArray());

    private static string Money(decimal value)
        => string.Create(CultureInfo.InvariantCulture, $"${value:N2}");

    private static async Task<ReportingHarnessSummary> LoadPageSnapshotAsync(IPage page, ReportSnapshot snapshot, ReportSnapshotPage snapshotPage)
    {
        var singlePageSnapshot = new ReportSnapshot
        {
            SnapshotId = $"{snapshot.SnapshotId}-p{snapshotPage.PageNumber}",
            Pages = [snapshotPage],
        };
        var loadResultJson = await page.EvaluateAsync<string>(
            """
            async snapshotJson => {
                try {
                    return JSON.stringify({
                        ok: true,
                        summary: await window.__tempoReportingHarness.loadSnapshot(JSON.parse(snapshotJson), []),
                        message: ''
                    });
                } catch (error) {
                    return JSON.stringify({
                        ok: false,
                        summary: null,
                        message: `${error?.name || 'Error'}: ${error?.message || error}`
                    });
                }
            }
            """,
            ReportSnapshotJsonSerializer.Serialize(singlePageSnapshot)).ConfigureAwait(false);
        var loadResult = JsonSerializer.Deserialize<ReportingHarnessLoadResult>(
            loadResultJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new AssertFailedException("Reporting harness returned an empty load result.");
        Assert.IsTrue(loadResult.Ok, loadResult.Message);
        return loadResult.Summary ?? throw new AssertFailedException("Reporting harness did not return a summary.");
    }

    private static string CreateScreenshotDirectory()
    {
        var directory = Path.Combine(FindRepositoryRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "reporting", "f7");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }

    private sealed record InvoiceLine(int Index, string Description, int Quantity, decimal UnitPrice)
    {
        public decimal Amount => Quantity * UnitPrice;
    }

    private sealed record SalesLine(string Region, string Category, string Bucket, string Customer, decimal Amount);

    private sealed class ReportingHarnessLoadResult
    {
        public bool Ok { get; set; }

        public ReportingHarnessSummary? Summary { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    private sealed class ReportingHarnessSummary
    {
        public int CommandCount { get; set; }

        public int PaintedCommandCount { get; set; }

        public int TextRunCount { get; set; }
    }

    private sealed class F7FixedTextMeasurer : ITextMeasurer
    {
        public TextMeasurement MeasureRun(TextMeasureRequest request)
        {
            var glyphCount = request.Text.EnumerateRunes().Count();
            var width = request.Text.EnumerateRunes().Sum(rune => rune.ToString() switch
            {
                " " => request.FontSize * 0.28,
                "." or "," or ":" or ";" or "|" => request.FontSize * 0.24,
                "/" or "-" => request.FontSize * 0.32,
                _ when char.IsDigit(rune.ToString(), 0) => request.FontSize * 0.52,
                _ => request.FontSize * 0.55,
            });
            return new TextMeasurement(
                width + Math.Max(0, glyphCount - 1) * request.LetterSpacing,
                Ascent: request.FontSize * 0.78,
                Descent: request.FontSize * 0.22,
                LineGap: 0,
                LineHeight: request.FontSize * 1.15,
                GlyphCount: glyphCount,
                FallbackGlyphCount: 0,
                MissingGlyphCount: 0);
        }
    }

    private sealed class ReportingHarnessHost : IAsyncDisposable
    {
        private readonly DirectoryInfo _repositoryRoot;
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _serverTask;

        private ReportingHarnessHost(DirectoryInfo repositoryRoot, int port)
        {
            _repositoryRoot = repositoryRoot;
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener = new HttpListener();
            _listener.Prefixes.Add($"{BaseUrl}/");
            _listener.Start();
            _serverTask = Task.Run(() => ServeAsync(_cancellation.Token));
        }

        public string BaseUrl { get; }

        public static ReportingHarnessHost Start(DirectoryInfo repositoryRoot)
            => new(repositoryRoot, GetFreePort());

        public async ValueTask DisposeAsync()
        {
            _cancellation.Cancel();
            _listener.Stop();
            try
            {
                await _serverTask.ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                _listener.Close();
                _cancellation.Dispose();
            }
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => ServeRequestAsync(context, cancellationToken), cancellationToken);
            }
        }

        private async Task ServeRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            try
            {
                var path = ResolvePath(context.Request.Url?.AbsolutePath ?? "/");
                if (path is null || !File.Exists(path))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                context.Response.ContentType = ContentTypeFor(path);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                context.Response.Close();
            }
        }

        private string? ResolvePath(string requestPath)
        {
            var decoded = WebUtility.UrlDecode(requestPath).Replace('\\', '/');
            if (string.Equals(decoded, "/", StringComparison.Ordinal))
            {
                decoded = "/reporting-harness.html";
            }

            if (decoded.StartsWith("/_content/Tempo.Blazor/", StringComparison.Ordinal))
            {
                var relative = decoded["/_content/Tempo.Blazor/".Length..];
                return SafeCombine(Path.Combine(_repositoryRoot.FullName, "src", "Tempo.Blazor", "wwwroot"), relative);
            }

            return SafeCombine(Path.Combine(_repositoryRoot.FullName, "src", "Tempo.Blazor.Demo", "wwwroot"), decoded.TrimStart('/'));
        }

        private static string? SafeCombine(string root, string relativePath)
        {
            var fullRoot = Path.GetFullPath(root);
            var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
            return fullPath.StartsWith(fullRoot, StringComparison.Ordinal) ? fullPath : null;
        }

        private static string ContentTypeFor(string path)
            => Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".css" => "text/css; charset=utf-8",
                ".html" => "text/html; charset=utf-8",
                ".js" or ".mjs" => "application/javascript; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".png" => "image/png",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream",
            };

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
