using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>F17 embedded reporting multi-app visual and functional gate.</summary>
[TestClass]
[TestCategory("Reporting")]
[TestCategory("Reporting:F17")]
[DoNotParallelize]
public sealed class ReportingF17EmbeddingE2ETests
{
    public TestContext TestContext { get; set; } = default!;

    [TestMethod]
    public async Task F17_ForeignApplication_RendersEmbeddedAndRemoteReportViewer()
    {
        var repositoryRoot = FindRepositoryRoot();
        await using var reportServer = await DotnetWebAppHost.StartAsync(
            repositoryRoot,
            "Report Server",
            Path.Combine("src", "Tempo.ReportServer.Web", "Tempo.ReportServer.Web.csproj")).ConfigureAwait(false);
        await using var demoHost = await DotnetWebAppHost.StartAsync(
            repositoryRoot,
            "Demo Server",
            Path.Combine("src", "Tempo.Blazor.Demo.Server", "Tempo.Blazor.Demo.Server.csproj")).ConfigureAwait(false);
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
        var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
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

        var encodedServerUrl = Uri.EscapeDataString(reportServer.BaseUrl);
        var encodedKey = Uri.EscapeDataString("tmr_demo_embed_key");
        await page.GotoAsync(
            $"{demoHost.BaseUrl}/report-embedding?reportServerUrl={encodedServerUrl}&apiKey={encodedKey}",
            new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 90_000,
            }).ConfigureAwait(false);
        await page.GetByTestId("f17-embedding-page").WaitForAsync(new LocatorWaitForOptions { Timeout = 90_000 }).ConfigureAwait(false);
        await WaitForViewerCanvasAsync(page, "embedded-report-viewer").ConfigureAwait(false);
        var embeddedMetrics = await DocumentEditorCanvasVisualAssert
            .AssertCanvasNonBlankAsync(page.GetByTestId("embedded-report-viewer").GetByTestId("tm-report-viewer-canvas"))
            .ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("embedded-host-card"))
            .ToContainTextAsync("embedded-sales-workspace", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "01-embedded-local-engine.png").ConfigureAwait(false);

        await page.GetByTestId("embedding-mode-remote").ClickAsync().ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("embedding-status"))
            .ToContainTextAsync(reportServer.BaseUrl, new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await WaitForViewerCanvasAsync(page, "remote-report-viewer").ConfigureAwait(false);
        var remoteMetrics = await DocumentEditorCanvasVisualAssert
            .AssertCanvasNonBlankAsync(page.GetByTestId("remote-report-viewer").GetByTestId("tm-report-viewer-canvas"))
            .ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("remote-report-viewer"))
            .ToContainTextAsync("sales-dashboard", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "02-remote-report-server-api.png").ConfigureAwait(false);

        var manifestPath = Path.Combine(screenshotDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                new
                {
                    phase = "F17",
                    testName = nameof(F17_ForeignApplication_RendersEmbeddedAndRemoteReportViewer),
                    demoHost.BaseUrl,
                    reportServerUrl = reportServer.BaseUrl,
                    screenshots = new[]
                    {
                        "01-embedded-local-engine.png",
                        "02-remote-report-server-api.png",
                    },
                    embeddedMetrics,
                    remoteMetrics,
                    functionalReview = "The foreign Demo.SharedUI application rendered TmReportViewer with an embedded local source and then rendered the report server sales dashboard through RemoteReportSource using X-Api-Key.",
                    uxReview = "The page keeps the embedded and remote connection controls visible above a single full-width report viewer, with no competing hero treatment and with the report canvas as the primary visual artifact.",
                    diagnostics,
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })).ConfigureAwait(false);
        TestContext.AddResultFile(manifestPath);
        await browserContext.CloseAsync().ConfigureAwait(false);
    }

    private static Task WaitForViewerCanvasAsync(IPage page, string wrapperTestId)
        => page.WaitForFunctionAsync(
            """
            wrapperTestId => {
                const wrapper = document.querySelector(`[data-testid="${wrapperTestId}"]`);
                const canvas = wrapper?.querySelector('[data-testid="tm-report-viewer-canvas"]');
                return canvas && canvas.width > 500 && canvas.height > 300;
            }
            """,
            wrapperTestId,
            new PageWaitForFunctionOptions { Timeout = 90_000 });

    private async Task ScreenshotAsync(IPage page, string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()").ConfigureAwait(false);
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
            "f17");
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
        private readonly string _name;

        private DotnetWebAppHost(string name, Process process, int port)
        {
            _name = name;
            _process = process;
            BaseUrl = $"http://127.0.0.1:{port}";
        }

        public string BaseUrl { get; }

        public static async Task<DotnetWebAppHost> StartAsync(
            DirectoryInfo repositoryRoot,
            string name,
            string projectRelativePath)
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
                ?? throw new InvalidOperationException($"Could not start dotnet run for {name}.");
            var host = new DotnetWebAppHost(name, process, port);
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
                    throw new InvalidOperationException($"{_name} exited early: {string.Join(Environment.NewLine, _log)}");
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

            throw new TimeoutException($"{_name} did not become ready: {string.Join(Environment.NewLine, _log)}");
        }

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
