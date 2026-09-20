using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Base class for Playwright E2E tests with support for multiple demo applications.
/// </summary>
[TestClass]
public abstract class PlaywrightTestBase
{
    private static IBrowser? _browser;
    private static IPlaywright? _playwright;
    private static readonly SemaphoreSlim BrowserLock = new(1, 1);
    private static readonly SemaphoreSlim HostLock = new(1, 1);
    private static readonly List<DemoHostProcess> DemoHostProcesses = [];
    private static bool _demoHostsInitialized;

    /// <summary>
    /// Scratch directory the demo hosts write their mutable state into, so a test run leaves the
    /// working tree alone. Created once per run and deliberately NOT deleted — a failed run's SQLite
    /// file is worth keeping, and the OS temp directory is where it belongs either way.
    /// </summary>
    private static readonly Lazy<string> DemoDataDirectory = new(() =>
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "tempo-blazor-e2e",
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        return directory;
    });

    /// <summary>
    /// Additional teardown callbacks run by the single assembly cleanup. Derived lanes that self-host
    /// extra processes (e.g. the report-server Api/Web in <see cref="ReportServerE2ETestBase"/>) register
    /// a killer here so a normal test run tears them down deterministically — MSTest permits only one
    /// <c>[AssemblyCleanup]</c> per assembly, so this is how other bases hook into it.
    /// </summary>
    internal static readonly List<Action> AdditionalAssemblyCleanups = [];

    // Per-test list of contexts created via CreatePageAsync / CreateContextAsync.
    // They MUST be disposed after each test, otherwise the shared static browser
    // process accumulates WebSocket/SignalR connections, service workers, and
    // IndexedDB handles for every sample page loaded, which eventually
    // exhausts Chromium and the OS kills the browser (manifests as cascade of
    // `TargetClosedException: Process exited` in later tests).
    private readonly List<IBrowserContext> _contextsToDispose = new();
    private readonly HashSet<IBrowserContext> _contextsWithTrace = [];

    /// <summary>
    /// Gets the base URL for the demo application under test.
    /// </summary>
    protected abstract string BaseUrl { get; }

    /// <summary>
    /// Gets the browser instance for tests.
    /// </summary>
    protected static IBrowser Browser => _browser!;

    /// <summary>
    /// Gets or sets the test context from MSTest.
    /// </summary>
    public TestContext TestContext { get; set; } = default!;

    /// <summary>
    /// One-time setup for all tests - initializes Playwright and browser.
    /// </summary>
    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassInitialize(TestContext context)
    {
        await EnsureDemoHostsAsync(context);

        await BrowserLock.WaitAsync();
        try
        {
            if (_playwright == null)
            {
                _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = !context.Properties.Contains("Headless") || context.Properties["Headless"]?.ToString() != "false",
                    SlowMo = 100 // Add small delay between actions for stability
                });
            }
        }
        finally
        {
            BrowserLock.Release();
        }
    }

    /// <summary>
    /// One-time cleanup for self-hosted demo applications.
    /// </summary>
    [AssemblyCleanup]
    public static async Task AssemblyCleanup()
    {
        await HostLock.WaitAsync();
        try
        {
            foreach (var host in DemoHostProcesses)
            {
                host.Dispose();
            }

            DemoHostProcesses.Clear();
            _demoHostsInitialized = false;

            foreach (var cleanup in AdditionalAssemblyCleanups)
            {
                try { cleanup(); }
                catch { /* best-effort teardown of extra self-hosted processes */ }
            }

            AdditionalAssemblyCleanups.Clear();
        }
        finally
        {
            HostLock.Release();
        }

        // Deliberately OUTSIDE the loop above: that loop swallows exceptions so a stuck host cannot
        // block teardown, and a baseline failure raised inside it would be silent. This is the last
        // thing the assembly does, so by now every capture the run made has landed.
        BaselineWriteSweep.AssertNoBaselinePngsExist();
    }

    /// <summary>
    /// One-time cleanup for all tests - disposes browser and Playwright.
    /// </summary>
    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassCleanup()
    {
        await BrowserLock.WaitAsync();
        try
        {
            if (_browser != null)
            {
                await _browser.CloseAsync();
                _browser = null;
            }
            _playwright?.Dispose();
            _playwright = null;
        }
        finally
        {
            BrowserLock.Release();
        }
    }

    /// <summary>
    /// Creates a new browser context for a test. The context is automatically
    /// closed in <see cref="BaseTestCleanup"/> after the test finishes.
    /// </summary>
    protected async Task<IBrowserContext> CreateContextAsync()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            AcceptDownloads = true
        });
        if (ShouldCollectTraceOnFailure())
        {
            await context.Tracing.StartAsync(new TracingStartOptions
            {
                Screenshots = true,
                Snapshots = true,
                Sources = true
            });
            _contextsWithTrace.Add(context);
        }

        _contextsToDispose.Add(context);
        return context;
    }

    /// <summary>
    /// Creates a new page and navigates to the base URL.
    /// </summary>
    protected async Task<IPage> CreatePageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(BaseUrl);
        await WaitForAppReadyAsync(page);
        return page;
    }

    /// <summary>
    /// Closes all browser contexts that were created by the test via
    /// <see cref="CreatePageAsync"/> or <see cref="CreateContextAsync"/>.
    /// </summary>
    [TestCleanup]
    public async Task BaseTestCleanup()
    {
        foreach (var context in _contextsToDispose)
        {
            await StopTraceAsync(context);
            try { await context.CloseAsync(); }
            catch { /* best-effort: browser may already be gone */ }
        }
        _contextsToDispose.Clear();
        _contextsWithTrace.Clear();
    }

    private static bool ShouldCollectTraceOnFailure()
        => !string.Equals(Environment.GetEnvironmentVariable("TM_E2E_TRACE_ON_FAILURE"), "false", StringComparison.OrdinalIgnoreCase);

    private async Task StopTraceAsync(IBrowserContext context)
    {
        if (!_contextsWithTrace.Contains(context))
        {
            return;
        }

        try
        {
            if (TestContext.CurrentTestOutcome == UnitTestOutcome.Passed)
            {
                await context.Tracing.StopAsync();
                return;
            }

            var directory = TestContext.TestResultsDirectory ?? Path.GetTempPath();
            Directory.CreateDirectory(directory);
            var name = SanitizeResultFileName(TestContext.TestName ?? "playwright-test");
            var path = Path.Combine(directory, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}_trace.zip");
            await context.Tracing.StopAsync(new TracingStopOptions { Path = path });
            TestContext.AddResultFile(path);
        }
        catch
        {
            // Best-effort diagnostics: context/browser may already be gone after a hard failure.
        }
    }

    private static string SanitizeResultFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    /// <summary>
    /// Waits for the Blazor application to be ready.
    /// </summary>
    protected async Task WaitForAppReadyAsync(IPage page)
    {
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions
        {
            Timeout = 60000
        });

        try
        {
            await page.WaitForSelectorAsync(".tm-app-loaded, main, [data-testid='app-ready']", new PageWaitForSelectorOptions
            {
                Timeout = 15000
            });
        }
        catch (TimeoutException)
        {
            await page.WaitForFunctionAsync(
                """
                () => {
                    const body = document.body;
                    const app = document.querySelector('#app, [data-testid="app"], main') || body;
                    const text = (app?.textContent || body?.textContent || '').trim();
                    return (document.readyState === 'interactive' || document.readyState === 'complete')
                        && !!body
                        && body.children.length > 0
                        && text.length > 0
                        && !/^loading[.\s]*$/i.test(text);
                }
                """,
                null,
                new PageWaitForFunctionOptions { Timeout = 45000 });
        }

        // Additional wait for WASM to boot in InteractiveAuto mode
        await page.WaitForTimeoutAsync(1000);
    }

    /// <summary>
    /// Takes a screenshot and attaches it to the test result.
    /// </summary>
    protected async Task TakeScreenshotAsync(IPage page, string name)
    {
        var screenshot = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        var path = Path.Combine(TestContext.TestResultsDirectory ?? ".", $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        await File.WriteAllBytesAsync(path, screenshot);
        TestContext.AddResultFile(path);
    }

    /// <summary>
    /// Clicks on a navigation menu item by its text. The match must target the nav <b>anchor</b> —
    /// a bare <c>nav:has-text(...)</c> resolves to the whole nav container (it contains every item's
    /// text), so clicking it lands on whatever link happens to sit at its midpoint.
    /// </summary>
    protected async Task NavigateToPageAsync(IPage page, string menuText)
    {
        var menuItem = page.Locator($"nav a:has-text('{menuText}'), nav button:has-text('{menuText}'), a:has-text('{menuText}'), button:has-text('{menuText}')").First;
        await menuItem.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Toggles dark mode and verifies the theme changed.
    /// </summary>
    protected async Task ToggleDarkModeAsync(IPage page)
    {
        // Find and click the theme toggle button
        var themeToggle = page.Locator("[data-testid='theme-toggle']:visible, button[aria-label*='theme' i]:visible, button[title*='dark' i]:visible").First;
        await themeToggle.ClickAsync();

        // Wait for the dark class to be applied/removed
        await page.WaitForTimeoutAsync(500);
    }

    /// <summary>
    /// Switches language to the specified culture. The demo switcher is a pair of buttons
    /// (<c>[data-testid='language-switcher-{culture}']</c>) that set localStorage and force a
    /// full reload — not a select element.
    /// </summary>
    protected async Task SwitchLanguageAsync(IPage page, string culture)
    {
        var button = page.Locator($"[data-testid='language-switcher-{culture}']").First;
        await button.ClickAsync();

        // SetCultureAsync force-loads the page — wait for the reload to finish and the app to
        // be interactive again rather than racing the first paint.
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await WaitForAppReadyAsync(page);
    }

    /// <summary>
    /// Performs memory heap snapshot for memory leak detection.
    /// </summary>
    protected async Task<long> GetHeapSizeAsync(IPage page)
    {
        var metrics = await page.EvaluateAsync<Dictionary<string, object>>("() => { return { usedJSHeapSize: performance.memory?.usedJSHeapSize || 0 }; }");
        // performance.memory is Chromium-only and can be absent mid-navigation on InteractiveAuto —
        // treat a missing key as 0 instead of crashing the leak probe.
        return metrics is not null && metrics.TryGetValue("usedJSHeapSize", out var heap)
            ? Convert.ToInt64(heap)
            : 0;
    }

    private static async Task EnsureDemoHostsAsync(TestContext context)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("TM_E2E_SELF_HOST"), "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await HostLock.WaitAsync();
        try
        {
            if (_demoHostsInitialized)
            {
                return;
            }

            var repoRoot = FindRepositoryRoot();
            await EnsureHostAsync(
                context,
                "Demo API",
                Path.Combine(repoRoot, "src", "Tempo.Blazor.Demo.Api", "Tempo.Blazor.Demo.Api.csproj"),
                "Tempo.Blazor.Demo.Api",
                ["https://localhost:5100"],
                TimeSpan.FromSeconds(120));

            await EnsureHostAsync(
                context,
                "Demo WASM",
                Path.Combine(repoRoot, "src", "Tempo.Blazor.Demo", "Tempo.Blazor.Demo.csproj"),
                "https",
                ["https://localhost:7106", "http://localhost:5010"],
                TimeSpan.FromSeconds(180));

            // Server and InteractiveAuto demo hosts: the ServerTestBase (:7107) and
            // InteractiveAutoTestBase (:7108) lanes previously assumed a developer had these running
            // by hand, so a bare `dotnet test` run failed every one of their tests in CreatePageAsync.
            // Self-host them the same way as the two lanes above.
            await EnsureHostAsync(
                context,
                "Demo Server",
                Path.Combine(repoRoot, "src", "Tempo.Blazor.Demo.Server", "Tempo.Blazor.Demo.Server.csproj"),
                "Tempo.Blazor.Demo.Server",
                ["https://localhost:7107"],
                TimeSpan.FromSeconds(120));

            await EnsureHostAsync(
                context,
                "Demo InteractiveAuto",
                Path.Combine(repoRoot, "src", "Tempo.Blazor.Demo.InteractiveAuto", "Tempo.Blazor.Demo.InteractiveAuto", "Tempo.Blazor.Demo.InteractiveAuto.csproj"),
                "Tempo.Blazor.Demo.InteractiveAuto",
                ["https://localhost:7108"],
                TimeSpan.FromSeconds(120));

            _demoHostsInitialized = true;
        }
        finally
        {
            HostLock.Release();
        }
    }

    private static async Task EnsureHostAsync(
        TestContext context,
        string name,
        string projectPath,
        string launchProfile,
        IReadOnlyList<string> urls,
        TimeSpan timeout)
    {
        if (await AllUrlsReachableAsync(urls))
        {
            return;
        }

        var process = StartDemoHostProcess(name, projectPath, launchProfile);
        DemoHostProcesses.Add(process);

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"{name} exited before it became ready. Recent output:{Environment.NewLine}{process.RecentOutput}");
            }

            if (await AllUrlsReachableAsync(urls))
            {
                context.WriteLine($"{name} ready at {string.Join(", ", urls)}.");
                return;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"{name} did not become ready at {string.Join(", ", urls)} within {timeout.TotalSeconds:n0}s. Recent output:{Environment.NewLine}{process.RecentOutput}");
    }

    private static DemoHostProcess StartDemoHostProcess(string name, string projectPath, string launchProfile)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = FindRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--launch-profile");
        startInfo.ArgumentList.Add(launchProfile);
        // The run build must not fail on the repo-wide NuGet audit warning-as-error (NU1902, AngleSharp
        // via bUnit/AngleSharp) or other warnings-as-errors — we only need the host to launch. Without
        // these the host dies during build and every dependent lane fails in ClassInitialize with
        // "…exited before it became ready". Mirrors ReportServerE2ETestBase.StartHostProcess.
        startInfo.ArgumentList.Add("--property:NuGetAudit=false");
        startInfo.ArgumentList.Add("--property:TreatWarningsAsErrors=false");
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";

        // The demo API keeps its diagrams in a SQLite file that is COMMITTED next to the project, so
        // booting it from a test run left `diagrams.db` modified and a -wal/-shm pair untracked in the
        // working tree. Point it at a per-run temp file instead; the demo API is the only host that
        // reads this key, and it is harmless on the others.
        startInfo.Environment["Demo__DiagramsDbPath"] =
            Path.Combine(DemoDataDirectory.Value, "diagrams.db");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var host = new DemoHostProcess(name, process);
        process.OutputDataReceived += (_, args) => host.AddOutput(args.Data);
        process.ErrorDataReceived += (_, args) => host.AddOutput(args.Data);

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {name}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return host;
    }

    private static async Task<bool> AllUrlsReachableAsync(IReadOnlyList<string> urls)
    {
        foreach (var url in urls)
        {
            if (!await IsUrlReachableAsync(url))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> IsUrlReachableAsync(string url)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync(url);
            return response.StatusCode != HttpStatusCode.ServiceUnavailable;
        }
        catch
        {
            return false;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class DemoHostProcess : IDisposable
    {
        private readonly string _name;
        private readonly Process _process;
        private readonly ConcurrentQueue<string> _output = new();

        public DemoHostProcess(string name, Process process)
        {
            _name = name;
            _process = process;
        }

        public bool HasExited => _process.HasExited;

        public string RecentOutput => string.Join(Environment.NewLine, _output.ToArray());

        public void AddOutput(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            _output.Enqueue(line);
            while (_output.Count > 80 && _output.TryDequeue(out _))
            {
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }
            }
            catch
            {
                // Best-effort cleanup after the test run.
            }
            finally
            {
                _process.Dispose();
            }
        }

        public override string ToString() => _name;
    }
}

/// <summary>
/// Base class for WASM demo tests.
/// </summary>
[TestCategory("WASM")]
public abstract class WasmTestBase : PlaywrightTestBase
{
    protected override string BaseUrl => "https://localhost:7106";
}

/// <summary>
/// Base class for Server demo tests.
/// </summary>
[TestCategory("Server")]
public abstract class ServerTestBase : PlaywrightTestBase
{
    protected override string BaseUrl => "https://localhost:7107";
}

/// <summary>
/// Base class for InteractiveAuto demo tests.
/// </summary>
[TestCategory("InteractiveAuto")]
public abstract class InteractiveAutoTestBase : PlaywrightTestBase
{
    protected override string BaseUrl => "https://localhost:7108";
}
