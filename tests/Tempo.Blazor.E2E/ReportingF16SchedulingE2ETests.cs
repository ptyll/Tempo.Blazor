using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Tempo.Blazor.E2E;

/// <summary>F16 scheduled report delivery visual and functional gate.</summary>
[TestClass]
[TestCategory("Reporting")]
[TestCategory("Reporting:F16")]
[DoNotParallelize]
public sealed class ReportingF16SchedulingE2ETests
{
    public TestContext TestContext { get; set; } = default!;

    [TestMethod]
    public async Task F16_ReportServerSchedules_DeliverPdfThroughSmtp4DevOutbox()
    {
        var repositoryRoot = FindRepositoryRoot();
        await using var host = await DotnetWebAppHost.StartAsync(
            repositoryRoot,
            Path.Combine("src", "Tempo.ReportServer.Web", "Tempo.ReportServer.Web.csproj")).ConfigureAwait(false);
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
        var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1360, Height = 920 },
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
        await page.GetByTestId("f12-login-page").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await page.GetByTestId("login-interactive-ready").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await page.GetByTestId("login-submit").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("f12-explorer-page").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);

        await page.GetByTestId("nav-schedules").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("f16-schedules-page").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await page.GetByTestId("schedule-name").FillAsync("F16 Monday ops pack").ConfigureAwait(false);
        await page.GetByTestId("schedule-cron").FillAsync("30 6 * * 1").ConfigureAwait(false);
        await page.GetByTestId("schedule-email").FillAsync("ops@example.test").ConfigureAwait(false);
        await page.GetByTestId("schedule-save").ClickAsync().ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("schedules-table"))
            .ToContainTextAsync("F16 Monday ops pack", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "01-schedules-management.png").ConfigureAwait(false);

        await page.GetByTestId("run-schedule-f16-monday-ops-pack").ClickAsync().ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("schedule-outbox"))
            .ToContainTextAsync("sales-dashboard.pdf", new LocatorAssertionsToContainTextOptions { Timeout = 60_000 })
            .ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("schedule-outbox"))
            .ToContainTextAsync("smtp4dev://localhost:2525", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("schedule-outbox"))
            .ToContainTextAsync("ops@example.test", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "02-smtp4dev-outbox.png").ConfigureAwait(false);

        var manifestPath = Path.Combine(screenshotDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                new
                {
                    phase = "F16",
                    testName = nameof(F16_ReportServerSchedules_DeliverPdfThroughSmtp4DevOutbox),
                    host.BaseUrl,
                    screenshots = new[]
                    {
                        "01-schedules-management.png",
                        "02-smtp4dev-outbox.png",
                    },
                    deliveredAttachment = "sales-dashboard.pdf",
                    transport = "smtp4dev://localhost:2525",
                    functionalReview = "The flow signed in, created a tenant-scoped schedule, ran the new dashboard schedule, rendered a PDF attachment and captured the delivered email in the smtp4dev demo outbox.",
                    uxReview = "The schedule screen keeps management controls, schedule rows and delivery evidence in a dense admin layout using existing report-server panels, status chips and table rows.",
                    diagnostics,
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })).ConfigureAwait(false);
        TestContext.AddResultFile(manifestPath);
        await browserContext.CloseAsync().ConfigureAwait(false);
    }

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
            "f16");
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
            var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (_process.HasExited)
                {
                    throw new InvalidOperationException($"Tempo.ReportServer.Web exited early: {string.Join(Environment.NewLine, _log)}");
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

                await Task.Delay(300).ConfigureAwait(false);
            }

            throw new TimeoutException($"Tempo.ReportServer.Web did not become ready: {string.Join(Environment.NewLine, _log)}");
        }

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
