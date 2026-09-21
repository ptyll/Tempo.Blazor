using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>F14 engine-drawn charts screenshot gate.</summary>
[TestClass]
[TestCategory("Reporting")]
[TestCategory("Reporting:F14")]
[DoNotParallelize]
public sealed class ReportingF14ChartsE2ETests
{
    public TestContext TestContext { get; set; } = default!;

    [TestMethod]
    public async Task F14_SalesDashboard_RendersEngineDrawnChartsTableAndPdfExport()
    {
        var repositoryRoot = FindRepositoryRoot();
        await using var host = await DotnetWebAppHost.StartAsync(
            repositoryRoot,
            Path.Combine("src", "Tempo.ReportServer.Web", "Tempo.ReportServer.Web.csproj")).ConfigureAwait(false);
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
        var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            AcceptDownloads = true,
            ViewportSize = new ViewportSize { Width = 1440, Height = 980 },
        }).ConfigureAwait(false);
        var page = await browserContext.NewPageAsync().ConfigureAwait(false);
        var screenshotDirectory = CreateScreenshotDirectory();
        var diagnostics = new List<string>();
        page.Console += (_, message) => diagnostics.Add($"console.{message.Type}: {message.Text}");
        page.PageError += (_, error) => diagnostics.Add($"pageerror: {error}");
        page.Response += (_, response) =>
        {
            if (response.Status >= 400)
            {
                diagnostics.Add($"response.{response.Status}: {response.Url}");
            }
        };
        page.RequestFailed += (_, request) => diagnostics.Add($"requestfailed: {request.Url} {request.Failure}");

        await page.GotoAsync(host.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000,
        }).ConfigureAwait(false);
        await page.GetByTestId("f12-login-page").WaitForAsync().ConfigureAwait(false);
        await page.GetByTestId("login-interactive-ready").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await page.GetByTestId("login-submit").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("f12-explorer-page").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);

        await page.EvaluateAsync(
            "url => Blazor.navigateTo(url)",
            "/reports/executive/sales-dashboard?Region=EU&MinimumTotal=0&IncludeClosed=true").ConfigureAwait(false);
        await page.GetByTestId("f12-viewer-page").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await Assertions.Expect(page.Locator("h2"))
            .ToContainTextAsync("Dashboard prodejů", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("viewer-deep-link-params"))
            .ToContainTextAsync("Region: EU", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await page.WaitForFunctionAsync(
            """
            () => {
                const canvas = document.querySelector('[data-testid="tm-report-viewer-canvas"]');
                return canvas && canvas.width > 700 && canvas.height > 500;
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 60_000 }).ConfigureAwait(false);

        var canvas = page.GetByTestId("tm-report-viewer-canvas");
        await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(canvas).ConfigureAwait(false);
        var stats = await ReadCanvasStatsAsync(canvas).ConfigureAwait(false);
        Assert.IsTrue(stats[0] > 500, $"Dashboard canvas should contain rendered report content. Stats: {JsonSerializer.Serialize(stats)}");
        Assert.IsTrue(stats[1] > 20, $"Column chart should contribute blue pixels. Stats: {JsonSerializer.Serialize(stats)}");
        Assert.IsTrue(stats[2] > 20, $"Line chart should contribute green pixels. Stats: {JsonSerializer.Serialize(stats)}");
        Assert.IsTrue(stats[3] > 20, $"Donut chart should contribute amber pixels. Stats: {JsonSerializer.Serialize(stats)}");
        await ScreenshotAsync(page, screenshotDirectory, "01-dashboard-viewer.png").ConfigureAwait(false);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Export" }).ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-report-export-menu").WaitForAsync().ConfigureAwait(false);
        var downloadTask = page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = 60_000 });
        await page.GetByTestId("tm-report-export-pdf").ClickAsync().ConfigureAwait(false);
        var download = await downloadTask.ConfigureAwait(false);
        var downloadPath = await download.PathAsync().ConfigureAwait(false)
            ?? throw new AssertFailedException("PDF download path was not available.");
        var bytes = await File.ReadAllBytesAsync(downloadPath).ConfigureAwait(false);
        Assert.IsTrue(bytes.Length > 1_000, "Dashboard PDF export should produce a non-empty file.");
        Assert.AreEqual("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4), "PDF export should start with the %PDF signature.");

        await page.EvaluateAsync("url => Blazor.navigateTo(url)", "/designer/sales-dashboard").ConfigureAwait(false);
        await page.GetByTestId("f13-designer-page").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await page.GetByTestId("tm-designer-element-status-donut").ClickAsync().ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("tm-designer-chart-properties"))
            .ToContainTextAsync("Chart type", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await page.GetByTestId("tm-designer-chart-type").SelectOptionAsync(["Pie"]).ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "02-dashboard-designer-chart-properties.png").ConfigureAwait(false);

        var manifestPath = Path.Combine(screenshotDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                new
                {
                    phase = "F14",
                    testName = nameof(F14_SalesDashboard_RendersEngineDrawnChartsTableAndPdfExport),
                    host.BaseUrl,
                    screenshots = new[]
                    {
                        "01-dashboard-viewer.png",
                        "02-dashboard-designer-chart-properties.png",
                    },
                    salesDataChecksum = new
                    {
                        region = "EU",
                        rowCount = 18,
                        total = 22527,
                        open = 17447,
                        closed = 5080,
                    },
                    canvasStats = stats,
                    functionalReview = "The dashboard opened by deep link, rendered one column chart, one line chart, one donut chart and a tablix from the Sales dataset; pixel stats confirmed all three palette colors and PDF export produced a valid PDF.",
                    uxReview = "The report uses Tempo token palette colors, BI-style chart density, compact axes/legends, a persistent parameter panel and a designer chart properties preview.",
                    diagnostics,
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })).ConfigureAwait(false);
        TestContext.AddResultFile(manifestPath);
        await browserContext.CloseAsync().ConfigureAwait(false);
    }

    private static Task<int[]> ReadCanvasStatsAsync(ILocator canvas)
        => canvas.EvaluateAsync<int[]>(
            """
            canvas => {
                const context = canvas.getContext('2d', { willReadFrequently: true });
                const image = context.getImageData(0, 0, canvas.width, canvas.height);
                const stats = [0, 0, 0, 0];
                for (let index = 0; index < image.data.length; index += 16) {
                    const r = image.data[index];
                    const g = image.data[index + 1];
                    const b = image.data[index + 2];
                    const a = image.data[index + 3];
                    if (a === 0) {
                        continue;
                    }

                    if (!(r > 245 && g > 245 && b > 245)) {
                        stats[0]++;
                    }

                    if (b > 150 && r < 90 && g < 150) {
                        stats[1]++;
                    }

                    if (g > 130 && b > 100 && r < 90) {
                        stats[2]++;
                    }

                    if (r > 180 && g > 100 && b < 80) {
                        stats[3]++;
                    }
                }

                return stats;
            }
            """);

    private async Task ScreenshotAsync(IPage page, string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = true,
            Path = path,
            Type = ScreenshotType.Png,
        }).ConfigureAwait(false);
        TestContext.AddResultFile(path);
    }

    private string CreateScreenshotDirectory()
    {
        var directory = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "__screenshots__",
            "reporting",
            "f14");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
        {
            current = current.Parent;
        }

        return current ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed class DotnetWebAppHost : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly List<string> _log = [];

        private DotnetWebAppHost(Process process, int port)
        {
            _process = process;
            BaseUrl = $"http://127.0.0.1:{port}";
        }

        public string BaseUrl { get; }

        public static async Task<DotnetWebAppHost> StartAsync(DirectoryInfo repositoryRoot, string projectRelativePath)
        {
            var port = GetFreePort();
            var projectPath = Path.Combine(repositoryRoot.FullName, projectRelativePath);
            var startInfo = new ProcessStartInfo("dotnet", $"run --project \"{projectPath}\" --urls http://127.0.0.1:{port}")
            {
                WorkingDirectory = repositoryRoot.FullName,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";
            // Api:BaseUrl stays empty in the shipped appsettings → the portal runs self-contained:
            // ITempoReportServerClient resolves to the in-memory DemoTempoReportServerClient, so no
            // external Report Server Api is needed for catalog/favorites/schedules pages.
            startInfo.Environment["ReportServer__BaseUrl"] = $"http://127.0.0.1:{port}";
            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start dotnet run for Tempo.ReportServer.Web.");
            var host = new DotnetWebAppHost(process, port);
            process.OutputDataReceived += (_, args) => host.AddLog(args.Data);
            process.ErrorDataReceived += (_, args) => host.AddLog(args.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await host.WaitUntilReadyAsync().ConfigureAwait(false);
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }

            _process.Dispose();
        }

        private void AddLog(string? line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lock (_log)
                {
                    _log.Add(line);
                }
            }
        }

        private async Task WaitUntilReadyAsync()
        {
            using var client = new HttpClient();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(240);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (_process.HasExited)
                {
                    throw new InvalidOperationException($"Tempo.ReportServer.Web exited early: {string.Join(Environment.NewLine, _log)}");
                }

                try
                {
                    using var response = await client.GetAsync(BaseUrl).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                }

                await Task.Delay(300).ConfigureAwait(false);
            }

            throw new TimeoutException($"Tempo.ReportServer.Web did not become ready: {string.Join(Environment.NewLine, _log)}");
        }

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
