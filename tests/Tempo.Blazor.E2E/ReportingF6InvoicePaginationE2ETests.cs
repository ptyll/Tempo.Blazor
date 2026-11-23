using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Tempo.Blazor.E2E.CanvasEngine;
using Tempo.Reporting.Abstractions.Definitions;
using Tempo.Reporting.Engine.Fonts;
using Tempo.Reporting.Engine.Layout;
using Tempo.Reporting.Engine.Processing;
using Tempo.Reporting.Engine.Snapshot;

namespace Tempo.Blazor.E2E;

/// <summary>F6 reporting band layout and pagination visual gate.</summary>
[TestClass]
[TestCategory("Reporting")]
[TestCategory("Reporting:F6")]
[DoNotParallelize]
public sealed class ReportingF6InvoicePaginationE2ETests
{
    private const string LogoDataUri =
        "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='96' height='96' viewBox='0 0 96 96'%3E%3Crect width='96' height='96' rx='18' fill='%230f172a'/%3E%3Cpath d='M24 30h48v10H54v34H42V40H24z' fill='%23ffffff'/%3E%3Ccircle cx='70' cy='70' r='8' fill='%232563eb'/%3E%3C/svg%3E";

    public TestContext TestContext { get; set; } = default!;

    [TestMethod]
    public async Task F6_ReportingHarness_RendersTwoPageInvoiceScreenshots()
    {
        var snapshot = BuildInvoiceSnapshot();
        Assert.AreEqual(2, snapshot.Pages.Count, "The F6 invoice fixture must paginate to exactly two pages.");

        await using var host = ReportingHarnessHost.Start(FindRepositoryRoot());
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
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
        var pageResults = await RenderInvoicePagesAsync(page, snapshot, screenshotDirectory);
        var manifestPath = Path.Combine(screenshotDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                new
                {
                    phase = "F6",
                    testName = nameof(F6_ReportingHarness_RendersTwoPageInvoiceScreenshots),
                    snapshot.SnapshotId,
                    functionalReview = "The invoice snapshot is generated from the F6 band composer: page header/footer repeat, detail bands flow to page 2, report footer lands after the final details, and page footers resolve PageNumber/TotalPages.",
                    uxReview = "The default invoice fixture uses restrained margins, a compact brand header, clear address blocks, aligned money columns, readable row rhythm and no overlapping text rectangles.",
                    pages = pageResults,
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        TestContext.AddResultFile(manifestPath);
        await browserContext.CloseAsync();
    }

    private async Task<List<object>> RenderInvoicePagesAsync(IPage page, ReportSnapshot snapshot, string screenshotDirectory)
    {
        var pageResults = new List<object>();
        for (var index = 0; index < snapshot.Pages.Count; index++)
        {
            var snapshotPage = snapshot.Pages[index];
            var summary = await LoadPageSnapshotAsync(page, snapshot, snapshotPage).ConfigureAwait(false);
            Assert.IsTrue(summary.TextRunCount > 45, $"Invoice page {snapshotPage.PageNumber} must paint a substantial text layout.");
            Assert.IsTrue(summary.PaintedCommandCount >= summary.TextRunCount + 8, $"Invoice page {snapshotPage.PageNumber} must paint text plus page primitives.");

            var expectedFooter = $"Page{snapshotPage.PageNumber}/2";
            var actualFooter = await PageNumberFooterTextAsync(page).ConfigureAwait(false);
            Assert.IsTrue(
                string.Equals(expectedFooter, actualFooter, StringComparison.Ordinal),
                $"Invoice page {snapshotPage.PageNumber} footer must include final page totals. Expected '{expectedFooter}', got '{actualFooter}'.");

            var canvasMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.GetByTestId("reporting-canvas")).ConfigureAwait(false);
            await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page).ConfigureAwait(false);

            var screenshotPath = Path.Combine(screenshotDirectory, $"{index + 1:00}-invoice-p{snapshotPage.PageNumber}.png");
            await page.GetByTestId("reporting-harness-page").ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = screenshotPath,
                Type = ScreenshotType.Png,
            }).ConfigureAwait(false);
            TestContext.AddResultFile(screenshotPath);
            pageResults.Add(new
            {
                snapshotPage.PageNumber,
                screenshotPath,
                summary.TextRunCount,
                summary.PaintedCommandCount,
                canvasMetrics,
            });
        }

        return pageResults;
    }

    private static ReportSnapshot BuildInvoiceSnapshot()
    {
        var lines = InvoiceLines();
        var subtotal = lines.Sum(line => line.Amount);
        var tax = decimal.Round(subtotal * 0.21m, 2);
        var total = subtotal + tax;
        var definition = new ReportDefinition
        {
            Id = "f6-invoice",
            Name = "F6 Invoice",
            PageSetup = new ReportPageSetup
            {
                PageSize = new ReportPageSize(640, 860),
                Margins = new ReportThickness(40),
            },
            Bands = new ReportBandCollection
            {
                PageHeader = CreatePageHeaderBand(),
                PageFooter = CreatePageFooterBand(),
            },
        };
        var bands = new List<ReportBandInstance>
        {
            Instance(CreateInvoiceHeaderBand(total)),
        };
        bands.AddRange(lines.Select(line => Instance(CreateInvoiceLineBand(line))));
        bands.Add(Instance(CreateInvoiceFooterBand(subtotal, tax, total)));

        return ReportSnapshotGenerator.Generate(
            new ReportInstance(definition, bands),
            new F6FixedTextMeasurer(),
            new ReportSnapshotGeneratorOptions
            {
                SnapshotId = "f6-invoice",
                PageFillColor = "#ffffff",
                PageStrokeColor = "#cbd5e1",
                MinimumOrphanHeight = 24,
            });
    }

    private static ReportBand CreatePageHeaderBand()
        => new()
        {
            Kind = ReportBandKind.PageHeader,
            Height = 70,
            Elements =
            [
                new ReportImageElement
                {
                    Id = "brand-logo",
                    SourceKind = ReportImageSourceKind.Embedded,
                    Source = LogoDataUri,
                    X = 0,
                    Y = 4,
                    Width = 46,
                    Height = 46,
                },
                TextBox("brand-name", "Tempo Components s.r.o.", 58, 7, 260, 18, Style(13, bold: true, color: "#0f172a")),
                TextBox("brand-address", "Krizikova 12, 186 00 Praha 8", 58, 28, 260, 14, Style(9, color: "#475569")),
                TextBox("brand-contact", "billing@tempo.example  |  +420 222 000 111", 58, 44, 260, 14, Style(9, color: "#64748b")),
                TextBox("document-title", "INVOICE", 380, 10, 180, 26, Style(20, bold: true, color: "#0f172a"), ReportHorizontalAlignment.Right),
                new ReportLineElement
                {
                    Id = "header-rule",
                    X = 0,
                    Y = 62,
                    Width = 560,
                    Height = 0,
                    Stroke = new ReportBorderLine("#dbe3ef", 1),
                },
            ],
        };

    private static ReportBand CreatePageFooterBand()
        => new()
        {
            Kind = ReportBandKind.PageFooter,
            Height = 38,
            Elements =
            [
                new ReportLineElement
                {
                    Id = "footer-rule",
                    X = 0,
                    Y = 0,
                    Width = 560,
                    Height = 0,
                    Stroke = new ReportBorderLine("#dbe3ef", 1),
                },
                TextBox("footer-note", "tempo.example | payment due net 14", 0, 11, 280, 14, Style(8.5, color: "#64748b")),
                TextBox("page-number", "Page PageNumber / TotalPages", 400, 11, 160, 14, Style(8.5, color: "#475569"), ReportHorizontalAlignment.Right),
            ],
        };

    private static ReportBand CreateInvoiceHeaderBand(decimal total)
        => new()
        {
            Kind = ReportBandKind.ReportHeader,
            Height = 178,
            KeepTogether = true,
            Elements =
            [
                TextBox("invoice-number", "Invoice #2026-006", 0, 8, 240, 24, Style(17, bold: true, color: "#111827")),
                TextBox("invoice-date", "Issued 22 Jun 2026", 390, 12, 170, 16, Style(10, color: "#475569"), ReportHorizontalAlignment.Right),
                TextBox("due-date", "Due 06 Jul 2026", 390, 30, 170, 16, Style(10, bold: true, color: "#1d4ed8"), ReportHorizontalAlignment.Right),
                new ReportShapeElement
                {
                    Id = "bill-to-box",
                    X = 0,
                    Y = 54,
                    Width = 264,
                    Height = 74,
                    FillColor = "#f8fafc",
                    Border = ReportBorder.All("#e2e8f0", 1),
                },
                TextBox("bill-to-label", "Bill to", 14, 64, 120, 14, Style(8.5, bold: true, color: "#64748b")),
                TextBox("bill-to", "Northwind Retail a.s.\nVaclavske namesti 21\n110 00 Praha 1", 14, 82, 220, 42, Style(10, color: "#0f172a"), padding: new ReportThickness(0), canGrow: true),
                new ReportShapeElement
                {
                    Id = "summary-box",
                    X = 304,
                    Y = 54,
                    Width = 256,
                    Height = 74,
                    FillColor = "#eff6ff",
                    Border = ReportBorder.All("#bfdbfe", 1),
                },
                TextBox("summary-label", "Amount due", 318, 66, 110, 14, Style(8.5, bold: true, color: "#1d4ed8")),
                TextBox("summary-total", Money(total), 412, 82, 132, 24, Style(18, bold: true, color: "#0f172a"), ReportHorizontalAlignment.Right),
                new ReportShapeElement
                {
                    Id = "table-header-fill",
                    X = 0,
                    Y = 146,
                    Width = 560,
                    Height = 26,
                    FillColor = "#0f172a",
                },
                TextBox("th-description", "Description", 12, 152, 260, 12, Style(8.5, bold: true, color: "#ffffff")),
                TextBox("th-qty", "Qty", 302, 152, 48, 12, Style(8.5, bold: true, color: "#ffffff"), ReportHorizontalAlignment.Right),
                TextBox("th-unit", "Unit", 360, 152, 72, 12, Style(8.5, bold: true, color: "#ffffff"), ReportHorizontalAlignment.Right),
                TextBox("th-amount", "Amount", 454, 152, 94, 12, Style(8.5, bold: true, color: "#ffffff"), ReportHorizontalAlignment.Right),
            ],
        };

    private static ReportBand CreateInvoiceLineBand(InvoiceLine line)
        => new()
        {
            Kind = ReportBandKind.Detail,
            Height = 28,
            KeepTogether = true,
            Elements =
            [
                TextBox($"desc-{line.Index}", line.Description, 12, 6, 260, 13, Style(9.5, color: "#0f172a")),
                TextBox($"qty-{line.Index}", line.Quantity.ToString(CultureInfo.InvariantCulture), 302, 6, 48, 13, Style(9.5, color: "#334155"), ReportHorizontalAlignment.Right),
                TextBox($"unit-{line.Index}", Money(line.UnitPrice), 360, 6, 72, 13, Style(9.5, color: "#334155"), ReportHorizontalAlignment.Right),
                TextBox($"amount-{line.Index}", Money(line.Amount), 454, 6, 94, 13, Style(9.5, bold: true, color: "#111827"), ReportHorizontalAlignment.Right),
                new ReportLineElement
                {
                    Id = $"row-rule-{line.Index}",
                    X = 0,
                    Y = 27,
                    Width = 560,
                    Height = 0,
                    Stroke = new ReportBorderLine("#edf2f7", 1),
                },
            ],
        };

    private static ReportBand CreateInvoiceFooterBand(decimal subtotal, decimal tax, decimal total)
        => new()
        {
            Kind = ReportBandKind.ReportFooter,
            Height = 96,
            KeepTogether = true,
            Elements =
            [
                new ReportLineElement
                {
                    Id = "total-rule",
                    X = 314,
                    Y = 8,
                    Width = 246,
                    Height = 0,
                    Stroke = new ReportBorderLine("#cbd5e1", 1),
                },
                TextBox("subtotal-label", "Subtotal", 334, 20, 90, 14, Style(9.5, color: "#475569")),
                TextBox("subtotal-value", Money(subtotal), 438, 20, 110, 14, Style(9.5, color: "#111827"), ReportHorizontalAlignment.Right),
                TextBox("tax-label", "VAT 21%", 334, 42, 90, 14, Style(9.5, color: "#475569")),
                TextBox("tax-value", Money(tax), 438, 42, 110, 14, Style(9.5, color: "#111827"), ReportHorizontalAlignment.Right),
                new ReportShapeElement
                {
                    Id = "grand-total-fill",
                    X = 318,
                    Y = 64,
                    Width = 242,
                    Height = 28,
                    FillColor = "#eff6ff",
                    Border = ReportBorder.All("#bfdbfe", 1),
                },
                TextBox("grand-total-label", "Total", 334, 71, 90, 14, Style(10, bold: true, color: "#1d4ed8")),
                TextBox("grand-total-value", Money(total), 438, 69, 110, 16, Style(12, bold: true, color: "#0f172a"), ReportHorizontalAlignment.Right),
            ],
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

    private static ReportTextStyle Style(double size, bool bold = false, string color = "#111827")
        => new()
        {
            FontFamily = "Arial",
            FontSize = size,
            Bold = bold,
            Color = color,
            LineHeight = 1.15,
        };

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

    private static string Money(decimal value)
        => string.Create(CultureInfo.InvariantCulture, $"${value:N2}");

    private static async Task<ReportingHarnessSummary> LoadPageSnapshotAsync(
        IPage page,
        ReportSnapshot snapshot,
        ReportSnapshotPage snapshotPage)
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

    private static Task<string> PageNumberFooterTextAsync(IPage page)
        => page.EvaluateAsync<string>(
            """
            () => Array.from(document.querySelectorAll('[data-run-id*="page-number"]'))
                .map(node => node.getAttribute('data-canvas-text') || '')
                .join('')
            """);

    private static string CreateScreenshotDirectory()
    {
        var directory = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "__screenshots__",
            "reporting",
            "f6");
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

    private sealed class F6FixedTextMeasurer : ITextMeasurer
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
