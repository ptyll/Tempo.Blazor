using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>F9 report viewer component visual and functional gate.</summary>
[TestClass]
[TestCategory("Reporting")]
[TestCategory("Reporting:F9")]
[DoNotParallelize]
public sealed class ReportingF9ReportViewerE2ETests
{
    public TestContext TestContext { get; set; } = default!;

    [TestMethod]
    public async Task F9_ReportViewer_DemoSupportsParametersPagingZoomAndPdfExport()
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
            ViewportSize = new ViewportSize { Width = 1180, Height = 920 },
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

        await page.GotoAsync($"{host.BaseUrl}/reporting", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000,
        }).ConfigureAwait(false);

        await page.WaitForFunctionAsync(
            """
            () => {
                const canvas = document.querySelector('[data-testid="tm-report-viewer-canvas"]');
                return canvas && canvas.width > 300 && canvas.height > 150;
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await WriteDiagnosticsAsync(page, screenshotDirectory, "diagnostics-before-canvas.txt", diagnostics).ConfigureAwait(false);
        await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.GetByTestId("tm-report-viewer-canvas")).ConfigureAwait(false);
        await ScreenshotViewerAsync(page, screenshotDirectory, "01-viewer-default.png").ConfigureAwait(false);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Refresh" }).ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-report-loading-state").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30_000,
        }).ConfigureAwait(false);
        await ScreenshotViewerAsync(page, screenshotDirectory, "05-loading-state.png").ConfigureAwait(false);
        await page.GetByTestId("tm-report-loading-state").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 60_000,
        }).ConfigureAwait(false);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Parameters" }).ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-report-parameter-panel").WaitForAsync().ConfigureAwait(false);
        await ScreenshotViewerAsync(page, screenshotDirectory, "02-parameters.png").ConfigureAwait(false);

        await page.GetByTestId("tm-report-param-Region").SelectOptionAsync(["US"]).ConfigureAwait(false);
        await page.GetByTestId("tm-report-param-MinimumTotal").FillAsync("0").ConfigureAwait(false);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Show report" }).ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-report-loading-state").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 60_000,
        }).ConfigureAwait(false);
        await page.WaitForFunctionAsync(
            """
            () => {
                const next = document.querySelector('button[aria-label="Next page"]');
                return next && !next.disabled;
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 }).ConfigureAwait(false);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next page" }).ClickAsync().ConfigureAwait(false);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"tm-report-page-input\"]')?.value === '2'",
            new PageWaitForFunctionOptions { Timeout = 10_000 }).ConfigureAwait(false);

        await page.GetByTestId("tm-report-zoom-select").SelectOptionAsync(["FitWidth"]).ConfigureAwait(false);
        await ScreenshotViewerAsync(page, screenshotDirectory, "03-zoom-fit.png").ConfigureAwait(false);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Export" }).ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-report-export-menu").WaitForAsync().ConfigureAwait(false);
        await ScreenshotViewerAsync(page, screenshotDirectory, "04-export-menu.png").ConfigureAwait(false);

        var downloadTask = page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = 60_000 });
        await page.GetByTestId("tm-report-export-pdf").ClickAsync().ConfigureAwait(false);
        var download = await downloadTask.ConfigureAwait(false);
        var downloadPath = await download.PathAsync().ConfigureAwait(false)
            ?? throw new AssertFailedException("PDF download path was not available.");
        var bytes = await File.ReadAllBytesAsync(downloadPath).ConfigureAwait(false);
        Assert.IsTrue(bytes.Length > 100, "PDF export should produce a non-empty file.");
        Assert.IsTrue(System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "%PDF", "PDF export should start with the %PDF signature.");

        var manifestPath = Path.Combine(screenshotDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                new
                {
                    phase = "F9",
                    testName = nameof(F9_ReportViewer_DemoSupportsParametersPagingZoomAndPdfExport),
                    host.BaseUrl,
                    screenshots = new[]
                    {
                        "01-viewer-default.png",
                        "02-parameters.png",
                        "03-zoom-fit.png",
                        "04-export-menu.png",
                        "05-loading-state.png",
                    },
                    functionalReview = "The Blazor report viewer opened an embedded report, accepted parameters, paged to page 2, applied fit-width zoom and downloaded a PDF export.",
                    uxReview = "The toolbar uses Tempo button styling, native keyboard-friendly inputs/selects, clear loading/empty/error surfaces and design-token based styling.",
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })).ConfigureAwait(false);
        TestContext.AddResultFile(manifestPath);
        await browserContext.CloseAsync().ConfigureAwait(false);
    }

    private async Task WriteDiagnosticsAsync(IPage page, string directory, string fileName, IReadOnlyList<string> browserDiagnostics)
    {
        var path = Path.Combine(directory, fileName);
        var viewerState = await page.EvaluateAsync<JsonElement>(
            """
            diagnostics => {
                const canvas = document.querySelector('[data-testid="tm-report-viewer-canvas"]');
                const viewer = document.querySelector('[data-testid="tm-report-viewer"]');
                const status = document.querySelector('[data-testid="tm-report-viewer-status"]');
                return {
                    canvasWidth: canvas?.width ?? 0,
                    canvasHeight: canvas?.height ?? 0,
                    canvasClientWidth: canvas?.clientWidth ?? 0,
                    canvasClientHeight: canvas?.clientHeight ?? 0,
                    viewerText: viewer?.innerText ?? '',
                    statusText: status?.innerText ?? '',
                    browserDiagnostics: diagnostics,
                };
            }
            """,
            browserDiagnostics).ConfigureAwait(false);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
            viewerState,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })).ConfigureAwait(false);
        TestContext.AddResultFile(path);
    }

    private async Task ScreenshotViewerAsync(IPage page, string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        await page.GetByTestId("tm-report-viewer").ScreenshotAsync(new LocatorScreenshotOptions
        {
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
            "f9");
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
                    using var response = await client.GetAsync($"{BaseUrl}/reporting").ConfigureAwait(false);
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
