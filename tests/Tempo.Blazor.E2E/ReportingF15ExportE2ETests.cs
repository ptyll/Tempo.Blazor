using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Tempo.Blazor.E2E;

/// <summary>F15 CSV/XLSX report export gate.</summary>
[TestClass]
[TestCategory("Reporting")]
[TestCategory("Reporting:F15")]
[DoNotParallelize]
public sealed class ReportingF15ExportE2ETests
{
    public TestContext TestContext { get; set; } = default!;

    [TestMethod]
    public async Task F15_ReportViewer_DownloadsCsvAndXlsxExports()
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
            ViewportSize = new ViewportSize { Width = 1360, Height = 900 },
        }).ConfigureAwait(false);
        var page = await browserContext.NewPageAsync().ConfigureAwait(false);
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
        await page.WaitForFunctionAsync(
            """
            () => {
                const canvas = document.querySelector('[data-testid="tm-report-viewer-canvas"]');
                return canvas && canvas.width > 700 && canvas.height > 500;
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 60_000 }).ConfigureAwait(false);

        var csvDownload = await DownloadFromMenuAsync(page, "tm-report-export-csv").ConfigureAwait(false);
        var csvPath = await csvDownload.PathAsync().ConfigureAwait(false)
            ?? throw new AssertFailedException("CSV download path was not available.");
        var csvBytes = await File.ReadAllBytesAsync(csvPath).ConfigureAwait(false);
        Assert.IsTrue(
            csvBytes.Length >= 3 && csvBytes[0] == 0xEF && csvBytes[1] == 0xBB && csvBytes[2] == 0xBF,
            "CSV export should include a UTF-8 BOM.");
        var csv = Encoding.UTF8.GetString(csvBytes[3..]);
        Assert.IsTrue(csv.Contains("Customer,Region,Total,Status", StringComparison.Ordinal), "CSV should include table headers.");
        Assert.IsTrue(csv.Contains("Europe Customer 01,EU,937,Open", StringComparison.Ordinal), "CSV should include processed sales rows.");
        await Assertions.Expect(page.GetByTestId("viewer-export-status"))
            .ToContainTextAsync("sales-dashboard.csv", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);

        var xlsxDownload = await DownloadFromMenuAsync(page, "tm-report-export-xlsx").ConfigureAwait(false);
        var xlsxPath = await xlsxDownload.PathAsync().ConfigureAwait(false)
            ?? throw new AssertFailedException("XLSX download path was not available.");
        var xlsxBytes = await File.ReadAllBytesAsync(xlsxPath).ConfigureAwait(false);
        Assert.IsTrue(
            xlsxBytes.Length >= 4 && xlsxBytes[0] == 0x50 && xlsxBytes[1] == 0x4B && xlsxBytes[2] == 0x03 && xlsxBytes[3] == 0x04,
            "XLSX export should be an OpenXML zip package.");
        Assert.IsTrue(xlsxBytes.Length > 1_000, "XLSX export should produce a non-empty workbook.");
        await Assertions.Expect(page.GetByTestId("viewer-export-status"))
            .ToContainTextAsync("sales-dashboard.xlsx", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);

        var manifestPath = Path.Combine(CreateScreenshotDirectory(), "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                new
                {
                    phase = "F15",
                    testName = nameof(F15_ReportViewer_DownloadsCsvAndXlsxExports),
                    host.BaseUrl,
                    downloads = new[]
                    {
                        csvDownload.SuggestedFilename,
                        xlsxDownload.SuggestedFilename,
                    },
                    csvBytes = csvBytes.Length,
                    xlsxBytes = xlsxBytes.Length,
                    diagnostics,
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })).ConfigureAwait(false);
        TestContext.AddResultFile(manifestPath);
        await browserContext.CloseAsync().ConfigureAwait(false);
    }

    private static async Task<IDownload> DownloadFromMenuAsync(IPage page, string testId)
    {
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Export" }).ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-report-export-menu").WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 }).ConfigureAwait(false);
        var downloadTask = page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = 60_000 });
        await page.GetByTestId(testId).ClickAsync().ConfigureAwait(false);
        return await downloadTask.ConfigureAwait(false);
    }

    private string CreateScreenshotDirectory()
    {
        var directory = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "__screenshots__",
            "reporting",
            "f15");
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
                _log.Add(line);
            }
        }

        private async Task WaitUntilReadyAsync()
        {
            using var client = new HttpClient();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (_process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Tempo.ReportServer.Web exited before startup. Log:{Environment.NewLine}{string.Join(Environment.NewLine, _log)}");
                }

                try
                {
                    using var response = await client.GetAsync(BaseUrl).ConfigureAwait(false);
                    if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                }

                await Task.Delay(500).ConfigureAwait(false);
            }

            throw new TimeoutException($"Tempo.ReportServer.Web did not become ready. Log:{Environment.NewLine}{string.Join(Environment.NewLine, _log)}");
        }

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
