using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Tempo.Blazor.E2E;

/// <summary>F13 report designer MVP screenshot gate.</summary>
[TestClass]
[TestCategory("Reporting")]
[TestCategory("Reporting:F13")]
[DoNotParallelize]
public sealed class ReportingF13ReportDesignerE2ETests
{
    public TestContext TestContext { get; set; } = default!;

    [TestMethod]
    public async Task F13_ReportDesigner_CoversD1ToD4ScreenshotGate()
    {
        var repositoryRoot = FindRepositoryRoot();
        await using var host = await DotnetWebAppHost.StartAsync(
            repositoryRoot,
            Path.Combine("src", "Tempo.ReportServer.Web", "Tempo.ReportServer.Web.csproj")).ConfigureAwait(false);
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

        await page.GotoAsync(host.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000,
        }).ConfigureAwait(false);
        await page.GetByTestId("f12-login-page").WaitForAsync().ConfigureAwait(false);
        await page.GetByTestId("login-interactive-ready").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await page.GetByTestId("login-submit").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("f12-explorer-page").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);

        await page.GetByTestId("nav-designer").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("f13-designer-page").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await page.GetByTestId("tm-report-designer").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);

        await page.GetByTestId("tm-designer-zoom").SelectOptionAsync(["125"]).ConfigureAwait(false);
        await FillAndBlurAsync(page.GetByTestId("tm-designer-band-height-Detail"), "180").ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "d1-canvas-bands.png").ConfigureAwait(false);

        await page.GetByTestId("tm-designer-page-setup-toggle").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-designer-page-setup").WaitForAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-designer-page-orientation").SelectOptionAsync(["Landscape"]).ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "d1-page-setup.png").ConfigureAwait(false);

        await page.GetByTestId("tm-designer-zoom").SelectOptionAsync(["100"]).ConfigureAwait(false);
        await page.GetByTestId("tm-designer-add-textbox").ClickAsync().ConfigureAwait(false);
        await FillAndBlurAsync(page.GetByTestId("tm-designer-property-text"), "Customer total").ConfigureAwait(false);
        await page.GetByTestId("tm-designer-copy").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-designer-add-image").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-designer-add-shape").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-designer-add-table").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-designer-add-chart").ClickAsync().ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("tm-designer-canvas"))
            .ToContainTextAsync("Customer total", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "d2-elements-properties.png").ConfigureAwait(false);

        await page.GetByTestId("tm-designer-tab-data").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-designer-data-panel").WaitForAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-designer-insert-field-Sales-Customer").ClickAsync().ConfigureAwait(false);
        await FillAndBlurAsync(page.GetByTestId("tm-designer-expression-input"), "=Fields.").ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("tm-designer-expression-error"))
            .ToContainTextAsync("Select a field", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "d3-data-expression.png").ConfigureAwait(false);

        await page.GetByTestId("tm-designer-tab-preview").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("tm-designer-preview-panel").WaitForAsync().ConfigureAwait(false);
        await Assertions.Expect(page.GetByTestId("tm-designer-preview"))
            .ToContainTextAsync("Sales Register", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 })
            .ConfigureAwait(false);
        await page.GetByTestId("tm-designer-publish").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("designer-save-status").WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 }).ConfigureAwait(false);
        await ScreenshotAsync(page, screenshotDirectory, "d4-preview-publish.png").ConfigureAwait(false);

        var manifestPath = Path.Combine(screenshotDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                new
                {
                    phase = "F13",
                    testName = nameof(F13_ReportDesigner_CoversD1ToD4ScreenshotGate),
                    host.BaseUrl,
                    screenshots = new[]
                    {
                        "d1-canvas-bands.png",
                        "d1-page-setup.png",
                        "d2-elements-properties.png",
                        "d3-data-expression.png",
                        "d4-preview-publish.png",
                    },
                    functionalReview = "The flow exercised the designer canvas bands, zoom, page setup, element insertion/copy/properties, field insertion, expression validation, preview and publish save event.",
                    uxReview = "The designer uses a dense report-authoring layout with a palette, ruler-like canvas, band grid, side properties, data/query panels and preview/revision sidebars.",
                    diagnostics,
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })).ConfigureAwait(false);
        TestContext.AddResultFile(manifestPath);
        await browserContext.CloseAsync().ConfigureAwait(false);
    }

    private static async Task FillAndBlurAsync(ILocator locator, string value)
    {
        await locator.FillAsync(value).ConfigureAwait(false);
        await locator.PressAsync("Tab").ConfigureAwait(false);
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
            "f13");
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
