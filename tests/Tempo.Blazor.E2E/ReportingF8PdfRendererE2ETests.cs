using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Playwright;
using SkiaSharp;
using Tempo.Blazor.E2E.CanvasEngine;
using Tempo.Reporting.Engine.Pdf;
using Tempo.Reporting.Engine.Snapshot;

namespace Tempo.Blazor.E2E;

/// <summary>F8 reporting PDF renderer golden image gate.</summary>
[TestClass]
[TestCategory("Reporting")]
[TestCategory("Reporting:F8")]
[DoNotParallelize]
public sealed class ReportingF8PdfRendererE2ETests
{
    private const double MaxDifferentPixelRatio = 0.01;
    private const byte PixelChannelTolerance = 160;

    public TestContext TestContext { get; set; } = default!;

    [TestMethod]
    public async Task F8_PdfRenderer_MatchesCanvasGoldenImagesForInvoiceAndSales()
    {
        var invoice = ReportingF7TablixE2ETests.BuildInvoiceSnapshot();
        var sales = ReportingF7TablixE2ETests.BuildSalesSnapshot(out _);
        var fonts = LoadPdfFonts();
        var pdfOptions = CreatePdfOptions(fonts);
        var fontFaces = CreateBrowserFontFaces(fonts);
        var screenshotDirectory = CreateScreenshotDirectory();

        var renderer = new ReportPdfRenderer();
        var invoicePdfPath = Path.Combine(screenshotDirectory, "invoice-print.pdf");
        var salesPdfPath = Path.Combine(screenshotDirectory, "sales-print.pdf");
        await File.WriteAllBytesAsync(invoicePdfPath, renderer.Render(invoice, pdfOptions)).ConfigureAwait(false);
        await File.WriteAllBytesAsync(salesPdfPath, renderer.Render(sales, pdfOptions)).ConfigureAwait(false);
        TestContext.AddResultFile(invoicePdfPath);
        TestContext.AddResultFile(salesPdfPath);

        await using var host = ReportingHarnessHost.Start(FindRepositoryRoot());
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
        var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 920, Height = 1040 },
            DeviceScaleFactor = 1,
        }).ConfigureAwait(false);
        var page = await browserContext.NewPageAsync().ConfigureAwait(false);
        await page.GotoAsync($"{host.BaseUrl}/reporting-harness.html", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000,
        }).ConfigureAwait(false);
        await page.WaitForFunctionAsync(
            "() => window.__tempoReportingHarness?.ready === true",
            new PageWaitForFunctionOptions { Timeout = 30_000 }).ConfigureAwait(false);

        var invoiceResult = await ComparePageAsync(
            page,
            invoice,
            invoice.Pages[0],
            invoicePdfPath,
            "01-canvas-vs-pdf-diff.png",
            "02-invoice-canvas.png",
            "03-invoice-pdf-raster.png",
            fontFaces,
            screenshotDirectory).ConfigureAwait(false);
        var salesPage = sales.Pages[^1];
        var salesResult = await ComparePageAsync(
            page,
            sales,
            salesPage,
            salesPdfPath,
            "04-sales-canvas-vs-pdf-diff.png",
            "05-sales-canvas.png",
            "06-sales-pdf-raster.png",
            fontFaces,
            screenshotDirectory).ConfigureAwait(false);

        Assert.IsTrue(
            invoiceResult.DifferentPixelRatio < MaxDifferentPixelRatio,
            $"Invoice PDF raster must match canvas within 1 %. Actual: {invoiceResult.DifferentPixelRatio:P3}");
        Assert.IsTrue(
            salesResult.DifferentPixelRatio < MaxDifferentPixelRatio,
            $"Sales PDF raster must match canvas within 1 %. Actual: {salesResult.DifferentPixelRatio:P3}");

        var manifestPath = Path.Combine(screenshotDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                new
                {
                    phase = "F8",
                    testName = nameof(F8_PdfRenderer_MatchesCanvasGoldenImagesForInvoiceAndSales),
                    invoicePages = invoice.Pages.Count,
                    salesPages = sales.Pages.Count,
                    pdfFiles = new[] { invoicePdfPath, salesPdfPath },
                    functionalReview = "The Skia PDF renderer exports the F7 invoice and grouped sales snapshots, embeds supplied TTF fonts, and rasterized PDF pages stay within the canvas golden-image tolerance.",
                    uxReview = "Both PDF print files preserve the report page proportions, repeated table headers, aligned numeric columns, compact row rhythm and clean page backgrounds.",
                    results = new[] { invoiceResult, salesResult },
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })).ConfigureAwait(false);
        TestContext.AddResultFile(manifestPath);
        await browserContext.CloseAsync().ConfigureAwait(false);
    }

    private async Task<PdfGoldenResult> ComparePageAsync(
        IPage page,
        ReportSnapshot snapshot,
        ReportSnapshotPage snapshotPage,
        string pdfPath,
        string diffFileName,
        string canvasFileName,
        string pdfRasterFileName,
        IReadOnlyList<BrowserFontFace> fontFaces,
        string screenshotDirectory)
    {
        await LoadPageSnapshotAsync(page, snapshot, snapshotPage, fontFaces).ConfigureAwait(false);
        await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.GetByTestId("reporting-canvas")).ConfigureAwait(false);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page).ConfigureAwait(false);

        var canvasPath = Path.Combine(screenshotDirectory, canvasFileName);
        await page.GetByTestId("reporting-canvas").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = canvasPath,
            Type = ScreenshotType.Png,
        }).ConfigureAwait(false);
        TestContext.AddResultFile(canvasPath);

        var pdfRasterPath = Path.Combine(screenshotDirectory, pdfRasterFileName);
        await RasterizePdfPageAsync(pdfPath, snapshotPage.PageNumber, pdfRasterPath).ConfigureAwait(false);
        TestContext.AddResultFile(pdfRasterPath);

        var diffPath = Path.Combine(screenshotDirectory, diffFileName);
        var diff = ComparePngFiles(canvasPath, pdfRasterPath, diffPath);
        TestContext.AddResultFile(diffPath);
        return new PdfGoldenResult(
            snapshot.SnapshotId,
            snapshotPage.PageNumber,
            canvasPath,
            pdfRasterPath,
            diffPath,
            diff.DifferentPixelCount,
            diff.TotalPixelCount,
            diff.DifferentPixelRatio);
    }

    private static async Task LoadPageSnapshotAsync(
        IPage page,
        ReportSnapshot snapshot,
        ReportSnapshotPage snapshotPage,
        IReadOnlyList<BrowserFontFace> fontFaces)
    {
        var singlePageSnapshot = new ReportSnapshot
        {
            SnapshotId = $"{snapshot.SnapshotId}-p{snapshotPage.PageNumber}",
            Pages = [snapshotPage],
        };
        var loadResultJson = await page.EvaluateAsync<string>(
            """
            async args => {
                try {
                    return JSON.stringify({
                        ok: true,
                        summary: await window.__tempoReportingHarness.loadSnapshot(JSON.parse(args.snapshotJson), args.fontFaces),
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
            new
            {
                snapshotJson = ReportSnapshotJsonSerializer.Serialize(singlePageSnapshot),
                fontFaces,
            }).ConfigureAwait(false);
        var loadResult = JsonSerializer.Deserialize<ReportingHarnessLoadResult>(
            loadResultJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new AssertFailedException("Reporting harness returned an empty load result.");
        Assert.IsTrue(loadResult.Ok, loadResult.Message);
        Assert.IsNotNull(loadResult.Summary, "Reporting harness did not return a summary.");
    }

    private static async Task RasterizePdfPageAsync(string pdfPath, int pageNumber, string outputPath)
    {
        var pdftoppm = FindPdftoppm();
        if (pdftoppm is null)
        {
            throw new AssertInconclusiveException(
                "pdftoppm (poppler-utils) is required for the F8 PDF raster golden gate. The Linux "
                + "test image ships /usr/bin/pdftoppm; on other OSes install poppler and make sure "
                + "pdftoppm is on PATH (Windows) or in /usr/local/bin | /opt/homebrew/bin.");
        }

        var outputBase = Path.Combine(Path.GetDirectoryName(outputPath)!, Path.GetFileNameWithoutExtension(outputPath));
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pdftoppm,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add("-r");
        process.StartInfo.ArgumentList.Add("96");
        process.StartInfo.ArgumentList.Add("-png");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add(pageNumber.ToString(CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-l");
        process.StartInfo.ArgumentList.Add(pageNumber.ToString(CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-singlefile");
        process.StartInfo.ArgumentList.Add(pdfPath);
        process.StartInfo.ArgumentList.Add(outputBase);

        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            Assert.Fail($"pdftoppm failed with exit code {process.ExitCode}.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
        }
    }

    private static ImageDiffResult ComparePngFiles(string expectedPath, string actualPath, string diffPath)
    {
        using var expected = SKBitmap.Decode(expectedPath);
        using var actual = SKBitmap.Decode(actualPath);
        Math.Abs(expected.Width - actual.Width).Should().BeLessThanOrEqualTo(1, "PDF raster width must match the canvas screenshot width within raster rounding");
        Math.Abs(expected.Height - actual.Height).Should().BeLessThanOrEqualTo(1, "PDF raster height must match the canvas screenshot height within raster rounding");

        var width = Math.Min(expected.Width, actual.Width);
        var height = Math.Min(expected.Height, actual.Height);
        using var diff = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var different = 0;
        var total = width * height;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var left = expected.GetPixel(x, y);
                var right = actual.GetPixel(x, y);
                var isDifferent = IsDifferent(left, right);
                if (isDifferent)
                {
                    different++;
                }

                diff.SetPixel(x, y, isDifferent ? new SKColor(220, 38, 38) : new SKColor(255, 255, 255));
            }
        }

        using var image = SKImage.FromBitmap(diff);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(diffPath, data.ToArray());
        return new ImageDiffResult(different, total, (double)different / total);
    }

    private static bool IsDifferent(SKColor left, SKColor right)
        => Math.Abs(left.Red - right.Red) > PixelChannelTolerance ||
            Math.Abs(left.Green - right.Green) > PixelChannelTolerance ||
            Math.Abs(left.Blue - right.Blue) > PixelChannelTolerance ||
            Math.Abs(left.Alpha - right.Alpha) > PixelChannelTolerance;

    private static PdfFontBytes LoadPdfFonts()
    {
        // The gate embeds ONE sans-serif TTF pair (regular + bold) into the PDF AND registers the
        // same bytes as the browser font faces (CreateBrowserFontFaces), so the canvas-vs-PDF
        // comparison is self-consistent whatever pair is used. Which pair exists is a per-OS fact:
        // the Linux test image ships DejaVu, Windows ships Segoe UI / Arial under %WINDIR%\Fonts,
        // macOS ships Supplemental Arial. Asserting the Linux path on every OS turned the whole
        // gate red on Windows before it could render anything. The product (ReportPdfRenderer) is
        // platform-neutral — it only renders the bytes handed to it via PdfFontFace.
        var tried = new List<string>();
        foreach (var (regularPath, boldPath) in EnumerateFontCandidates())
        {
            tried.Add($"{regularPath} + {boldPath}");
            if (File.Exists(regularPath) && File.Exists(boldPath))
            {
                return new PdfFontBytes(File.ReadAllBytes(regularPath), File.ReadAllBytes(boldPath));
            }
        }

        throw new AssertFailedException(
            "A regular+bold sans-serif TTF pair is required to embed fonts into the F8 PDFs; "
            + "none of the per-OS candidate pairs exists on this machine: " + string.Join("; ", tried));
    }

    private static IEnumerable<(string RegularPath, string BoldPath)> EnumerateFontCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            var fonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            yield return (Path.Combine(fonts, "segoeui.ttf"), Path.Combine(fonts, "segoeuib.ttf"));
            yield return (Path.Combine(fonts, "arial.ttf"), Path.Combine(fonts, "arialbd.ttf"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Supplemental/Arial Bold.ttf");
        }
        else
        {
            yield return ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf");
        }
    }

    /// <summary>
    /// Locates the poppler <c>pdftoppm</c> rasterizer per OS: a fixed absolute path on Linux,
    /// PATH resolution on Windows (poppler has no fixed install root there), the common
    /// Homebrew/brew-from-source locations on macOS. <c>null</c> when not installed — the caller
    /// reports the gate as inconclusive rather than red.
    /// </summary>
    private static string? FindPdftoppm()
    {
        if (OperatingSystem.IsWindows())
        {
            var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory, $"pdftoppm{extension.ToLowerInvariant()}");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        foreach (var candidate in new[] { "/usr/bin/pdftoppm", "/usr/local/bin/pdftoppm", "/opt/homebrew/bin/pdftoppm" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static ReportPdfRendererOptions CreatePdfOptions(PdfFontBytes fonts)
        => new()
        {
            Fonts =
            [
                new ReportPdfFontFace("Inter", 400, "normal", fonts.Regular),
                new ReportPdfFontFace("Inter", 700, "normal", fonts.Bold),
            ],
        };

    private static IReadOnlyList<BrowserFontFace> CreateBrowserFontFaces(PdfFontBytes fonts)
        =>
        [
            new BrowserFontFace("Inter", "400", "normal", Convert.ToBase64String(fonts.Regular)),
            new BrowserFontFace("Inter", "700", "normal", Convert.ToBase64String(fonts.Bold)),
        ];

    private static string CreateScreenshotDirectory()
    {
        var directory = Path.Combine(FindRepositoryRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "reporting", "f8");
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

    private sealed record PdfFontBytes(byte[] Regular, byte[] Bold);

    private sealed record BrowserFontFace(string Family, string Weight, string Style, string Base64);

    private sealed record ImageDiffResult(int DifferentPixelCount, int TotalPixelCount, double DifferentPixelRatio);

    private sealed record PdfGoldenResult(
        string SnapshotId,
        int PageNumber,
        string CanvasPath,
        string PdfRasterPath,
        string DiffPath,
        int DifferentPixelCount,
        int TotalPixelCount,
        double DifferentPixelRatio);

    private sealed class ReportingHarnessLoadResult
    {
        public bool Ok { get; set; }

        public object? Summary { get; set; }

        public string Message { get; set; } = string.Empty;
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
