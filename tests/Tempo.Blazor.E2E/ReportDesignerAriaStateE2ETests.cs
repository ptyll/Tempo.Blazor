using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan b75365fb Fáze 4h — the <c>TmReportDesigner</c> tab bar (design / data / preview) renders
/// <c>aria-selected</c> as the STRINGS "true"/"false" in both positions (a bool comparison
/// rendered the invalid empty value on the active tab and dropped the attribute on the inactive
/// ones). The designer lives only in the Report Server web portal, so the spec self-hosts
/// <c>Tempo.ReportServer.Web</c> exactly like the F13 gate, signs in through the demo login and
/// exercises the tabs in a real browser; a scoped axe scan (wcag2a/2aa, aria-valid-attr-value
/// among them) runs on the tab bar in both themes and light/dark screenshots land in
/// <c>__screenshots__/reporting/fze-4h/</c> for the UX review.
/// </summary>
[TestClass]
[TestCategory("Reporting")]
[TestCategory("Reporting:F13")]
[DoNotParallelize]
public sealed class ReportDesignerAriaStateE2ETests : PlaywrightTestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";

    private DotnetWebAppHost? _host;

    // The spec navigates to its own self-hosted ReportServer.Web (a dynamic port), never to a demo
    // app — BaseUrl only satisfies the abstract member and is unread on this path.
    protected override string BaseUrl => _host?.BaseUrl ?? "http://127.0.0.1";

    [TestMethod]
    public async Task ReportDesigner_Tabs_AnnounceTheStringState_InBothStates()
    {
        var repositoryRoot = FindRepositoryRoot();
        await using var host = await DotnetWebAppHost.StartAsync(
            repositoryRoot,
            Path.Combine("src", "Tempo.ReportServer.Web", "Tempo.ReportServer.Web.csproj")).ConfigureAwait(false);
        _host = host;
        // The shared browser + central context factory (trace, bounded relaunch, cleanup) — the
        // readiness ratchet forbids a spec-owned NewContextAsync even for a self-hosted app.
        var browserContext = await CreateContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 980 },
        }).ConfigureAwait(false);
        var page = await browserContext.NewPageAsync().ConfigureAwait(false);
        var clientErrors = new List<string>();
        page.PageError += (_, error) => { lock (clientErrors) clientErrors.Add("PAGEERROR: " + error); };

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
        await page.GetByTestId("tm-report-designer").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);

        var design = page.GetByTestId("tm-designer-tab-design");
        var data = page.GetByTestId("tm-designer-tab-data");
        var preview = page.GetByTestId("tm-designer-tab-preview");

        Assert.AreEqual("true", await design.GetAttributeAsync("aria-selected").ConfigureAwait(false),
            "the Design tab is active — the string \"true\", never the invalid empty value");
        Assert.AreEqual("false", await data.GetAttributeAsync("aria-selected").ConfigureAwait(false),
            "an inactive tab keeps the attribute — the string \"false\", never an omitted attribute");
        Assert.AreEqual("false", await preview.GetAttributeAsync("aria-selected").ConfigureAwait(false));

        await data.ClickAsync().ConfigureAwait(false);
        await Assertions.Expect(data).ToHaveAttributeAsync("aria-selected", "true").ConfigureAwait(false);
        await Assertions.Expect(design).ToHaveAttributeAsync("aria-selected", "false").ConfigureAwait(false);
        await Assertions.Expect(preview).ToHaveAttributeAsync("aria-selected", "false").ConfigureAwait(false);

        await preview.ClickAsync().ConfigureAwait(false);
        await Assertions.Expect(preview).ToHaveAttributeAsync("aria-selected", "true").ConfigureAwait(false);
        await Assertions.Expect(data).ToHaveAttributeAsync("aria-selected", "false").ConfigureAwait(false);

        await design.ClickAsync().ConfigureAwait(false);
        await Assertions.Expect(design).ToHaveAttributeAsync("aria-selected", "true").ConfigureAwait(false);

        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn }).ConfigureAwait(false);
        var violations = await page.EvaluateAsync<string[]>(
            """
            async () => {
                await Promise.all(document.getAnimations()
                    .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                    .map(a => a.finished.catch(() => { })));
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const result = await axe.run(document.querySelector('.tm-report-designer__tabs'), {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """).ConfigureAwait(false);
        Assert.AreEqual(0, violations.Length,
            "axe critical/serious violations on the designer tabs:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));

        var directory = Path.Combine(
            repositoryRoot.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "reporting", "fze-4h");
        Directory.CreateDirectory(directory);
        var screenshotPath = Path.Combine(directory, "designer-tabs-light-1440.png");
        await page.GetByTestId("tm-report-designer").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = screenshotPath,
            Type = ScreenshotType.Png,
        }).ConfigureAwait(false);
        TestContext.AddResultFile(screenshotPath);

        // Dark pass: the Report Server portal has no theme toggle of its own — its stylesheets are
        // the core tempo-blazor.css tokens, which flip through [data-theme="dark"] / .tm-dark on
        // the root element (tokens-dark.css), so the test drives the same switch directly.
        await page.EvaluateAsync(
            """
            () => {
                document.documentElement.setAttribute('data-theme', 'dark');
                document.documentElement.classList.add('tm-dark');
            }
            """).ConfigureAwait(false);
        await page.WaitForTimeoutAsync(400).ConfigureAwait(false);

        var darkViolations = await page.EvaluateAsync<string[]>(
            """
            async () => {
                await Promise.all(document.getAnimations()
                    .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                    .map(a => a.finished.catch(() => { })));
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const result = await axe.run(document.querySelector('.tm-report-designer__tabs'), {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """).ConfigureAwait(false);
        Assert.AreEqual(0, darkViolations.Length,
            "axe critical/serious violations on the designer tabs (dark):" + Environment.NewLine
            + string.Join(Environment.NewLine, darkViolations));

        var darkScreenshotPath = Path.Combine(directory, "designer-tabs-dark-1440.png");
        await page.GetByTestId("tm-report-designer").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = darkScreenshotPath,
            Type = ScreenshotType.Png,
        }).ConfigureAwait(false);
        TestContext.AddResultFile(darkScreenshotPath);

        lock (clientErrors)
        {
            Assert.IsTrue(clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", clientErrors));
        }
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
            // external Report Server Api is needed.
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
