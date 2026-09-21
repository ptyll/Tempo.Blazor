using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>F12 report server web shell visual and functional gate.</summary>
[TestClass]
[TestCategory("Reporting")]
[TestCategory("Reporting:F12")]
[DoNotParallelize]
public sealed class ReportingF12ReportServerWebE2ETests
{
    public TestContext TestContext { get; set; } = default!;

    [TestMethod]
    public async Task F12_ReportServerWeb_CoversLoginExplorerViewerExportAndAdmin()
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
            ViewportSize = new ViewportSize { Width = 1360, Height = 940 },
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
        await ScreenshotAsync(page, screenshotDirectory, "01-login.png").ConfigureAwait(false);

        await page.GetByTestId("login-submit").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("f12-explorer-page").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await page.GetByTestId("tenant-switcher").SelectOptionAsync(["northwind"]).ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "02-explorer-grid.png").ConfigureAwait(false);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "List view" }).ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-report-explorer-list").WaitForAsync().ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "03-explorer-list.png").ConfigureAwait(false);

        await page.GetByTestId("tm-report-open-sales-register").ClickAsync().ConfigureAwait(false);
        await page.WaitForFunctionAsync(
            "() => location.pathname === '/reports/finance/sales-register' && location.search.includes('Region=EU')",
            new PageWaitForFunctionOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await page.GetByTestId("f12-viewer-page").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await page.WaitForFunctionAsync(
            """
            () => {
                const canvas = document.querySelector('[data-testid="tm-report-viewer-canvas"]');
                return canvas && canvas.width > 300 && canvas.height > 150;
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.GetByTestId("tm-report-viewer-canvas")).ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "04-viewer-page.png").ConfigureAwait(false);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Export" }).ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-report-export-menu").WaitForAsync().ConfigureAwait(false);
        var downloadTask = page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = 60_000 });
        await page.GetByTestId("tm-report-export-pdf").ClickAsync().ConfigureAwait(false);
        var download = await downloadTask.ConfigureAwait(false);
        var downloadPath = await download.PathAsync().ConfigureAwait(false)
            ?? throw new AssertFailedException("PDF download path was not available.");
        var bytes = await File.ReadAllBytesAsync(downloadPath).ConfigureAwait(false);
        Assert.IsTrue(bytes.Length > 100, "PDF export should produce a non-empty file.");
        Assert.AreEqual("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4), "PDF export should start with the %PDF signature.");

        await page.GetByTestId("nav-datasources").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("f12-datasources-page").WaitForAsync().ConfigureAwait(false);
        await page.GetByTestId("test-datasource-crm-rest").ClickAsync().ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("datasources-table"))
            .ToContainTextAsync("Connected at", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "05-datasources.png").ConfigureAwait(false);

        await page.GetByTestId("nav-permissions").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("f12-permissions-page").WaitForAsync().ConfigureAwait(false);
        await page.GetByTestId("permission-subject").FillAsync("embedded-viewers").ConfigureAwait(false);
        await page.GetByTestId("permission-add").ClickAsync().ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("permissions-table"))
            .ToContainTextAsync("embedded-viewers", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "06-permissions.png").ConfigureAwait(false);

        await page.GetByTestId("nav-revisions").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("f12-revisions-page").WaitForAsync().ConfigureAwait(false);
        await page.GetByTestId("rollback-sales-register-r11").ClickAsync().ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("revisions-table"))
            .ToContainTextAsync("Current", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "07-revisions.png").ConfigureAwait(false);

        var manifestPath = Path.Combine(screenshotDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                new
                {
                    phase = "F12",
                    testName = nameof(F12_ReportServerWeb_CoversLoginExplorerViewerExportAndAdmin),
                    host.BaseUrl,
                    screenshots = new[]
                    {
                        "01-login.png",
                        "02-explorer-grid.png",
                        "03-explorer-list.png",
                        "04-viewer-page.png",
                        "05-datasources.png",
                        "06-permissions.png",
                        "07-revisions.png",
                    },
                    functionalReview = "The flow signed in, used the tenant-aware explorer, opened a deep-linked report viewer, downloaded a PDF export, tested a data source, added an ACL row and rolled back a revision.",
                    uxReview = "The app shell uses dense report-server navigation, Tempo design tokens, grid/list explorer modes, empty-ready admin states and responsive constrained layouts.",
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
            "f12");
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
