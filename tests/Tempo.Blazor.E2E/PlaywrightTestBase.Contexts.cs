using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Shared browser lifecycle, browser-context creation/cleanup and failure traces of <see cref="PlaywrightTestBase"/>.
/// </summary>
public abstract partial class PlaywrightTestBase
{
    private static IBrowser? _browser;
    private static IPlaywright? _playwright;
    private static bool _headless = true;
    private static readonly string[] BrowserArgs = { "--enable-precise-memory-info", "--js-flags=--expose-gc" };
    private static readonly SemaphoreSlim BrowserLock = new(1, 1);

    // Per-test list of contexts created via CreatePageAsync / CreateContextAsync.
    // They MUST be disposed after each test, otherwise the shared static browser
    // process accumulates WebSocket/SignalR connections, service workers, and
    // IndexedDB handles for every sample page loaded, which eventually
    // exhausts Chromium and the OS kills the browser (manifests as cascade of
    // `TargetClosedException: Process exited` in later tests).
    private readonly List<IBrowserContext> _contextsToDispose = new();
    private readonly HashSet<IBrowserContext> _contextsWithTrace = [];

    /// <summary>
    /// Gets the browser instance for tests. If the shared Chromium process died since the last
    /// use, it is relaunched here so direct <c>Browser.NewContextAsync</c> callers are covered too.
    /// </summary>
    protected static IBrowser Browser
    {
        get
        {
            if (_browser is { IsConnected: true })
            {
                return _browser;
            }
            EnsureBrowserAsync().GetAwaiter().GetResult();
            return _browser!;
        }
    }

    /// <summary>
    /// Ensures the shared Chromium instance is alive. A long suite can outlive the browser
    /// process (renderer churn, memory pressure) — a dead singleton would otherwise fail every
    /// remaining test instantly at NewContextAsync. Relaunching limits the blast radius to the
    /// test that was in flight when the browser died.
    /// </summary>
    private static async Task EnsureBrowserAsync()
    {
        if (_browser is { IsConnected: true })
        {
            return;
        }

        await BrowserLock.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true })
            {
                return;
            }

            // A tracked browser existed and is now dead/gone: this launch is a RELAUNCH, so it is
            // recorded to TestResults/browser-relaunches.jsonl (H4) — the machine-readable half,
            // next to host-restarts.jsonl. The very first launch of the run is not a relaunch.
            var isRelaunch = _browser is not null || _playwright is not null;
            await LaunchBrowserCoreAsync();
            if (isRelaunch)
            {
                RecordBrowserRelaunch("browser-died-mid-run", testName: null);
            }
        }
        finally
        {
            BrowserLock.Release();
        }
    }

    /// <summary>
    /// B1 unit-test seam: when set, <see cref="LaunchBrowserCoreAsync"/> delegates the launch to
    /// this fake instead of Playwright, so the harness unit tests can exercise the relaunch path
    /// (and prove it no longer deadlocks on the re-entered <see cref="BrowserLock"/>) without a
    /// real Chromium. Null return = "launched nothing"; callers reset the statics around the test.
    /// </summary>
    internal static Func<Task<IBrowser?>>? BrowserLaunchOverride { get; set; }

    /// <summary>
    /// The actual launch body, WITHOUT <see cref="BrowserLock"/>. B1: both <see cref="EnsureBrowserAsync"/>
    /// and <see cref="ForceRelaunchBrowserAsync"/> need to launch under the lock, and SemaphoreSlim is
    /// NOT reentrant — ForceRelaunch holding the lock while calling EnsureBrowserAsync deadlocked the
    /// shard the moment the wedged-browser recovery in <see cref="CreateContextAsync(BrowserNewContextOptions, bool)"/> first fired.
    /// </summary>
    private static async Task LaunchBrowserCoreAsync()
    {
        if (BrowserLaunchOverride is not null)
        {
            _browser = await BrowserLaunchOverride().WaitAsync(BrowserCallTimeout);
            return;
        }

        _playwright ??= await Microsoft.Playwright.Playwright.CreateAsync().WaitAsync(BrowserCallTimeout);
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _headless,
            SlowMo = 100, // Add small delay between actions for stability
            // Enable precise performance.memory.usedJSHeapSize for the navigation memory-leak
            // probe; without the flag Chromium may omit the API entirely (returns 0 → NaN ratio).
            // --expose-gc makes window.gc() real so the probe measures retained memory after
            // collection — otherwise transient navigation allocations count as "growth".
            Args = BrowserArgs
        }).WaitAsync(BrowserCallTimeout);
    }

    /// <summary>
    /// Creates a new browser context for a test. The context is automatically
    /// closed in <see cref="BaseTestCleanup"/> after the test finishes.
    /// </summary>
    /// <param name="hasTouch">
    /// Emulates a coarse pointer (<c>(any-pointer: coarse)</c>). The responsive-chrome specs need it: the
    /// 44px touch targets are a coarse-pointer rule, and a desktop-pointer context can never see them.
    /// </param>
    protected Task<IBrowserContext> CreateContextAsync(bool hasTouch = false)
        => CreateContextAsync(1280, 720, hasTouch);

    /// <summary>
    /// <see cref="CreateContextAsync(bool)"/> with an explicit viewport (and, optionally, a phone-like
    /// <c>isMobile</c> context). Specs that need a viewport or <c>isMobile</c> used to call
    /// <c>Browser.NewContextAsync</c> directly and so lost the failure trace and the bounded, relaunching context
    /// creation of this helper - use this overload instead.
    /// </summary>
    /// <param name="width">Viewport width in CSS pixels.</param>
    /// <param name="height">Viewport height in CSS pixels.</param>
    /// <param name="hasTouch">Emulates a coarse pointer; see <see cref="CreateContextAsync(bool)"/>.</param>
    /// <param name="isMobile">Emulates a mobile browser (meta viewport, overlay scrollbars).</param>
    protected Task<IBrowserContext> CreateContextAsync(int width, int height, bool hasTouch = false, bool isMobile = false)
        => CreateContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = hasTouch,
            IsMobile = isMobile
        });

    /// <summary>
    /// The general context factory every other <c>CreateContextAsync</c> overload wraps: specs that need options the
    /// convenience overloads do not carry (color scheme, geolocation, permissions, storage state, a user agent ...)
    /// pass them here instead of calling <c>Browser.NewContextAsync</c>, so they keep the failure trace, the
    /// bounded, relaunching context creation and the registration for cleanup.
    /// </summary>
    /// <param name="options">
    /// The caller's options (required, never mutated: a clone is used). Unset <c>Locale</c> (en-US),
    /// <c>IgnoreHTTPSErrors</c> (true), <c>AcceptDownloads</c> (true) and <c>ViewportSize</c> (1280x720) get the suite defaults.
    /// </param>
    /// <param name="trace">Collect a failure trace (see <c>TM_E2E_TRACE_ON_FAILURE</c>); pass <c>false</c> to opt out.</param>
    protected internal async Task<IBrowserContext> CreateContextAsync(BrowserNewContextOptions options, bool trace = true)
    {
        ArgumentNullException.ThrowIfNull(options);

        options = new BrowserNewContextOptions(options);
        options.Locale ??= "en-US";
        options.IgnoreHTTPSErrors ??= true;
        options.AcceptDownloads ??= true;
        if (options.ViewportSize is null)
        {
            options.ViewportSize = new ViewportSize { Width = 1280, Height = 720 };
        }
        // A wedged browser answers IsConnected (its process lives) while protocol calls never
        // return — the shard-1 stall of 2026-11-18: the last test wrote its screenshot at 14:13
        // and the next NewContextAsync never came back, so the shard sat silent ~38 minutes until
        // it was killed externally, producing no TRX at all. Bound the call and treat a timeout as
        // "this browser is a corpse": relaunch once and let the retry ride the fresh browser.
        IBrowserContext context;
        try
        {
            context = await Browser.NewContextAsync(options).WaitAsync(BrowserCallTimeout);
        }
        catch (TimeoutException)
        {
            Console.WriteLine($"[e2e] Browser.NewContextAsync did not return within {BrowserCallTimeout.TotalSeconds:0}s — the shared browser looks wedged; relaunching it and retrying once.");
            await ForceRelaunchBrowserAsync(TestContext.TestName);
            context = await Browser.NewContextAsync(options).WaitAsync(BrowserCallTimeout);
        }

        if (trace && ShouldCollectTraceOnFailure())
        {
            await context.Tracing.StartAsync(new TracingStartOptions
            {
                Screenshots = true,
                Snapshots = true,
                Sources = true
            }).WaitAsync(BrowserCallTimeout);
            _contextsWithTrace.Add(context);
        }

        _contextsToDispose.Add(context);
        return context;
    }

    /// <summary>
    /// The wall-clock ceiling for ONE Playwright lifecycle call (create/close a context, launch or
    /// close the browser). Generous on purpose — under parallel load these legitimately take
    /// seconds — but a call that exceeds it has never come back in practice: the transport is
    /// wedged and only a relaunch helps (see <see cref="CreateContextAsync(BrowserNewContextOptions, bool)"/>).
    /// </summary>
    private static readonly TimeSpan BrowserCallTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Treats the shared browser as dead even when its process still runs: abandons the corpse
    /// (its teardown is bounded too — a wedged pipe never answers Close/Dispose) and relaunches a
    /// fresh Playwright + Chromium, so one wedged browser costs ONE loud retry instead of a silent
    /// multi-hour stall of the whole shard. Internal so the harness unit tests can drive it with
    /// <see cref="BrowserLaunchOverride"/>.
    /// </summary>
    internal static async Task ForceRelaunchBrowserAsync(string? testName = null)
    {
        await BrowserLock.WaitAsync();
        try
        {
            var staleBrowser = _browser;
            var stalePlaywright = _playwright;
            _browser = null;
            _playwright = null;
            if (staleBrowser is not null || stalePlaywright is not null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (staleBrowser is not null)
                        {
                            await staleBrowser.CloseAsync().WaitAsync(TimeSpan.FromSeconds(10));
                        }

                        stalePlaywright?.Dispose();
                    }
                    catch
                    {
                        // The corpse stays a corpse; its processes leave with the test run.
                    }
                });
            }

            // B1: launch through the lock-free core. Calling EnsureBrowserAsync() here — while THIS
            // method already holds BrowserLock — deadlocked on the non-reentrant semaphore.
            await LaunchBrowserCoreAsync();
            RecordBrowserRelaunch("wedged-browser-force-relaunch", testName);
        }
        finally
        {
            BrowserLock.Release();
        }
    }

    /// <summary>
    /// H4: snapshots the shared browser statics so the harness unit tests can run the relaunch
    /// path without a browser and leave a concurrent real Playwright class untouched.
    /// </summary>
    internal static (IBrowser? Browser, IPlaywright? Playwright) SnapshotBrowserForTest() => (_browser, _playwright);

    /// <summary>Restores what <see cref="SnapshotBrowserForTest"/> captured.</summary>
    internal static void RestoreBrowserForTest(IBrowser? browser, IPlaywright? playwright)
    {
        _browser = browser;
        _playwright = playwright;
    }

    /// <summary>Tracks a context this test opened itself, so cleanup closes it.</summary>
    [Obsolete("A context that needs RegisterContext was not created by CreateContextAsync (no failure trace, no bounded relaunching creation): use CreateContextAsync(options) / OpenAppPageAsync. Becomes private once every spec is migrated (E2E harness phase 1a).")]
    protected void RegisterContext(IBrowserContext context) => _contextsToDispose.Add(context);

    /// <summary>
    /// Creates a new page and navigates to the base URL.
    /// </summary>
    /// <param name="hasTouch">Emulates a coarse pointer; see <see cref="CreateContextAsync(bool)"/>.</param>
    protected async Task<IPage> CreatePageAsync(bool hasTouch = false)
    {
        var context = await CreateContextAsync(hasTouch);
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, BaseUrl);
        return page;
    }

    /// <summary>
    /// Closes all browser contexts that were created by the test via
    /// <see cref="CreatePageAsync"/> or <see cref="CreateContextAsync(BrowserNewContextOptions, bool)"/>.
    /// </summary>
    [TestCleanup]
    public async Task BaseTestCleanup()
    {
        foreach (var context in _contextsToDispose)
        {
            await StopTraceAsync(context);
            try
            {
                // Bounded: a context whose browser died mid-test can hang the close forever, and
                // TestCleanup runs between EVERY pair of tests — one wedged close must not stall
                // the shard the way the unbounded lifecycle calls stalled shard 1 on 2026-11-18.
                await context.CloseAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch
            {
                /* best-effort: browser may already be gone, or the close is wedged */
            }
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
                await context.Tracing.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));
                return;
            }

            var directory = TestContext.TestResultsDirectory ?? Path.GetTempPath();
            Directory.CreateDirectory(directory);
            var name = SanitizeResultFileName(TestContext.TestName ?? "playwright-test");
            var path = Path.Combine(directory, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}_trace.zip");
            await context.Tracing.StopAsync(new TracingStopOptions { Path = path }).WaitAsync(TimeSpan.FromSeconds(30));
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
}
