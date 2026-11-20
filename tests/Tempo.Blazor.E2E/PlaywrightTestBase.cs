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
    private static bool _headless = true;
    private static readonly string[] BrowserArgs = { "--enable-precise-memory-info", "--js-flags=--expose-gc" };
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

        _headless = !context.Properties.Contains("Headless") || context.Properties["Headless"]?.ToString() != "false";
        await EnsureBrowserAsync();
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
                await _browser.CloseAsync().WaitAsync(TimeSpan.FromSeconds(30));
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
    /// Navigates to <paramref name="url"/> and waits for the app to be ready, with the ONE controlled recovery from
    /// a slow cold boot: when the readiness gate times out the SAME page is reloaded once (the app and its cached
    /// framework assets are up by then; only the first boot was slow) and gated again. This is the only place that
    /// recovery lives — specs and per-feature harnesses call this instead of keeping a local retry copy
    /// ("ReadinessInfrastructureTests.NoSpecKeepsALocalColdBootRecoveryCopy" enforces it). A second timeout is a real
    /// failure and propagates. The wait budgets come from <see cref="ReadinessTimeouts"/> (<c>TM_E2E_READY_TIMEOUT_MS</c>).
    /// </summary>
    protected async Task GotoAppReadyAsync(IPage page, string url, WaitUntilState waitUntil = WaitUntilState.Load)
    {
        var timeouts = ReadinessTimeouts.FromEnvironment();
        var navigated = false;
        // The first attempt INCLUDES the navigation: a GotoAsync that times out on a slow cold boot is recovered
        // by the same single controlled reload as a readiness timeout. When the navigation never committed (the page
        // is still about:blank) there is nothing to reload, so the recovery navigates again.
        await ColdBootRecovery.RunAsync(
            async () =>
            {
                if (!navigated)
                {
                    navigated = true;
                    await page.GotoAsync(url, new PageGotoOptions { WaitUntil = waitUntil, Timeout = timeouts.DomContentLoadedMs });
                }

                await WaitForAppReadyAsync(page);
            },
            () => RecoverBootAsync(ReloadOrNavigateAsync));

        return;

        Task ReloadOrNavigateAsync() =>
            string.Equals(page.Url, "about:blank", StringComparison.OrdinalIgnoreCase)
                ? page.GotoAsync(url, new PageGotoOptions { WaitUntil = waitUntil, Timeout = timeouts.DomContentLoadedMs })
                : page.ReloadAsync(new PageReloadOptions { WaitUntil = waitUntil, Timeout = timeouts.DomContentLoadedMs });
    }

    /// <summary>
    /// Reloads the CURRENT page and waits for the app to be ready, with the same ONE controlled recovery as
    /// <see cref="GotoAppReadyAsync"/>. Specs that reload a page (to prove state survives a reload, to re-run a boot
    /// path) call this instead of <c>page.ReloadAsync()</c> + <c>WaitForAppReadyAsync</c>: a reload boots the WASM
    /// runtime again, and a boot whose one framework fetch dies stalls for good without the recovery.
    /// </summary>
    protected async Task ReloadAppReadyAsync(IPage page)
    {
        var timeouts = ReadinessTimeouts.FromEnvironment();
        var reloaded = false;
        await ColdBootRecovery.RunAsync(
            async () =>
            {
                if (!reloaded)
                {
                    reloaded = true;
                    await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = timeouts.DomContentLoadedMs });
                }

                await WaitForAppReadyAsync(page);
            },
            () => RecoverBootAsync(() => page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = timeouts.DomContentLoadedMs })));
    }

    /// <summary>
    /// The recovery step shared by <see cref="GotoAppReadyAsync"/> and <see cref="ReloadAppReadyAsync"/>. The readiness
    /// gate timed out. Besides a slow first boot, the other cause is a host that DIED while the app was booting
    /// (observed 2026-11-18: Demo WASM died mid-run and the recovery reload landed on net::ERR_CONNECTION_REFUSED).
    /// Resurrect the hosts the same way a ClassInitialize would BEFORE the reload, and once more if the reload itself
    /// still meets a refused connection (the host died between the probe and the reload). A second refusal still
    /// propagates - the recovery stays bounded.
    /// </summary>
    private async Task RecoverBootAsync(Func<Task> reload)
    {
        await RecoverDemoHostsAsync();
        try
        {
            await reload();
        }
        catch (PlaywrightException ex) when (IsConnectionRefused(ex))
        {
            await RecoverDemoHostsAsync();
            await reload();
        }
    }
    /// <summary>
    /// Whether <paramref name="ex"/> is the browser-level "nothing is listening" error — the
    /// signature of a self-hosted demo host that died under the in-flight test.
    /// </summary>
    internal static bool IsConnectionRefused(PlaywrightException ex) =>
        ex.Message.Contains("ERR_CONNECTION_REFUSED", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("ERR_CONNECTION_RESET", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("ERR_EMPTY_RESPONSE", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Waits for the Blazor application to be ready.
    /// </summary>
    protected async Task WaitForAppReadyAsync(IPage page)
    {
        // Every budget derives from TM_E2E_READY_TIMEOUT_MS (default 120 s; the former hard-coded 60 s / 15 s / 45 s
        // windows were too tight for a cold WASM boot under full-lane CPU load). All waits are on a STATE, so a
        // longer cap only extends the wait of a slow boot, it never sleeps. Boots past 15 s are logged so a
        // creeping regression is visible in the test output instead of only in the duration. A cold-boot TIMEOUT is
        // recovered by GotoAppReadyAsync (one controlled reload), never by a per-spec copy.
        // ONE deadline per attempt (ReadyMs): each sub-wait gets only the time that is left, so the pre-gates can
        // never add up to more than the authoritative cap.
        var timeouts = ReadinessTimeouts.FromEnvironment();
        var attemptWatch = System.Diagnostics.Stopwatch.StartNew();
        float Remaining(int capMs) =>
            Math.Max(1f, Math.Min(capMs, timeouts.ReadyMs - (float)attemptWatch.Elapsed.TotalMilliseconds));

        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions
        {
            Timeout = Remaining(timeouts.DomContentLoadedMs)
        });

        try
        {
            await page.WaitForSelectorAsync(".tm-app-loaded, main, [data-testid='app-ready']", new PageWaitForSelectorOptions
            {
                Timeout = Remaining(timeouts.SelectorMs)
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
                new PageWaitForFunctionOptions { Timeout = Remaining(timeouts.FallbackMs) });
        }

        // Stateful readiness instead of the former fixed 1000 ms sleep (N193): the shared demo
        // MainLayout sets [data-blazor-ready] on <body> from OnAfterRenderAsync(firstRender) —
        // which prerendering never calls — so the attribute lands exactly when an interactive
        // runtime has rendered (WASM boot finished / server circuit up). A host whose runtime
        // never comes up fails this wait instead of silently passing under a sleep.
        var bootWatch = System.Diagnostics.Stopwatch.StartNew();
        await page.WaitForFunctionAsync(
            "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
            null,
            new PageWaitForFunctionOptions { Timeout = Remaining(timeouts.ReadyMs) });
        bootWatch.Stop();
        if (bootWatch.Elapsed > TimeSpan.FromSeconds(15))
        {
            TestContext.WriteLine($"[e2e] slow boot: data-blazor-ready after {bootWatch.Elapsed.TotalSeconds:F1}s (timeout {timeouts.ReadyMs}ms)");
        }
    }
    /// <summary>
    /// Waits until fonts are loaded, no CSS transition runs, no smooth scroll is in flight and the boxes of
    /// <paramref name="selector"/> are identical across 3 consecutive frames (15 s budget).
    /// </summary>
    /// <remarks>
    /// The page is still MOVING when the app reports ready: the demo's Tailwind Play CDN generates CSS for
    /// freshly rendered classes after the interactive render, and <c>html { scroll-behavior: smooth }</c> turns
    /// every programmatic/focus scroll into an animation (docs/e2e-test-lanes.md "Timing-masked races").
    /// Geometry assertions must wait for this settled state, not for a fixed delay. <paramref name="selector"/>
    /// should list every element whose box the following assertions read; the window scroll position is part of
    /// the signature, so an in-flight smooth scroll counts as "moving" even when the elements' boxes are stable.
    /// A selector that matches no element never settles — the wait times out instead of passing on an empty page.
    /// </remarks>
    protected static async Task WaitForLayoutToSettleAsync(IPage page, string selector)
    {
        var settled = await page.EvaluateAsync<bool>(
            """
            async (selector) => {
                const frame = () => new Promise(resolve => requestAnimationFrame(() => resolve()));
                const transitionRunning = () => document.getAnimations()
                    .some(animation => animation instanceof CSSTransition && animation.playState === 'running');
                const deadline = performance.now() + 15000;
                let previous = null;
                let stableFrames = 0;
                while (performance.now() < deadline) {
                    await document.fonts.ready;
                    await frame();
                    const nodes = document.querySelectorAll(selector);
                    if (nodes.length === 0) {
                        // A selector that matches nothing must NOT settle on the empty signature:
                        // reset and keep polling so the wait times out instead of passing.
                        previous = null;
                        stableFrames = 0;
                        continue;
                    }
                    const current = [...nodes]
                        .map(node => {
                            const r = node.getBoundingClientRect();
                            return `${r.left},${r.top + window.scrollY},${r.width},${r.height}`;
                        })
                        .concat([`${window.scrollX},${window.scrollY}`])
                        .join('|');
                    const quiet = document.fonts.status === 'loaded' && !transitionRunning();
                    stableFrames = quiet && current === previous ? stableFrames + 1 : 0;
                    if (stableFrames >= 3) {
                        return true;
                    }

                    previous = current;
                }

                return false;
            }
            """, selector);
        Assert.IsTrue(settled,
            $"the layout never settled within 15s (fonts loading, a CSS transition running, the '{selector}' boxes/scroll still moving, or the selector matched no element) — geometry read now would be mid-reflow");
    }

    /// <summary>
    /// Waits until every FINITE animation inside <paramref name="scopeSelector"/> (the element and its subtree) has
    /// finished, and asserts it did (10 s budget). Infinite animations (spinners, pulses) are ignored.
    /// </summary>
    /// <remarks>
    /// <see cref="WaitForLayoutToSettleAsync"/> watches CSS TRANSITIONS only, and an entrance animation that holds its
    /// start state through a delay (<c>animation-delay</c>) looks perfectly stable until it plays. A spec that hovers
    /// or clicks a moving target then fights Playwright's actionability retries. This helper reads
    /// <c>getAnimations({ subtree: true })</c> (<c>running</c> and <c>pending</c> play states), loops until a pass
    /// finds no finite animation still running (an animation that starts while waiting is caught by the next pass)
    /// and races the whole loop against a 10 s timeout so a never-ending finite animation fails loudly instead of
    /// hanging.
    /// </remarks>
    /// <returns><c>true</c> when the scope was animation-quiet within the budget (the call has already asserted it).</returns>
    protected static async Task<bool> WaitForAnimationsToFinishAsync(IPage page, string scopeSelector)
    {
        var finished = await page.EvaluateAsync<bool>(
            """
            async (scopeSelector) => {
                const scope = document.querySelector(scopeSelector);
                if (!scope) {
                    return false;
                }

                const isFiniteRunning = animation =>
                    (animation.playState === 'running' || animation.playState === 'pending')
                    && animation.effect
                    && Number.isFinite(animation.effect.getComputedTiming().endTime);
                let cancelled = false;
                const drain = async () => {
                    while (!cancelled) {
                        const running = scope.getAnimations({ subtree: true }).filter(isFiniteRunning);
                        if (running.length === 0) {
                            return true;
                        }

                        await Promise.all(running.map(animation => animation.finished.catch(() => { })));
                    }

                    return false;
                };
                const timeout = new Promise(resolve => setTimeout(() => resolve(false), 10000));
                const result = await Promise.race([drain(), timeout]);
                cancelled = true;
                return result;
            }
            """, scopeSelector);
        Assert.IsTrue(finished,
            $"the finite animations inside '{scopeSelector}' did not finish within 10s (or the scope matched no element) - a hover/click now would hit a moving target");
        return finished;
    }
    /// <summary>
    /// Takes a screenshot and attaches it to the test result.
    /// </summary>
    protected async Task TakeScreenshotAsync(IPage page, string name)
    {
        var screenshot = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true,
            // Screenshot captures bypass the default action timeout — a stalled renderer/GPU
            // would otherwise hang the whole shard with no failure reported (seen on
            // DiagramEdge Phase3 under load).
            Timeout = 60_000
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
    /// Puts the demo shell into <paramref name="theme"/> (<c>light</c>/<c>dark</c>) through the real header toggle.
    /// The demo frame carries its own <c>data-theme</c> (ThemeService), and a nested <c>[data-theme="light"]</c> is
    /// the documented light island that re-pins every token - so setting the attribute on <c>&lt;html&gt;</c> from
    /// outside never darkens the page content; the shell's own theme is the contract.
    /// </summary>
    protected static async Task EnsureDemoThemeAsync(IPage page, string theme)
    {
        var shell = page.Locator(".demo-shell").First;
        if (!string.Equals(await shell.GetAttributeAsync("data-theme"), theme, StringComparison.Ordinal))
        {
            // dispatch in-page: a top-layer popover/sheet or a hidden-per-band toggle must not block it
            await page.EvaluateAsync("() => { const t = [...document.querySelectorAll('[data-testid=theme-toggle]')].find(e => e.offsetParent !== null) ?? document.querySelector('[data-testid=theme-toggle]'); t?.click(); }");
        }

        await Assertions.Expect(shell).ToHaveAttributeAsync("data-theme", theme,
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);
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
        // Evaluate a scalar, not an object: Playwright's .NET serializer wraps returned objects in a
        // preserved-reference envelope ({"$id":"1", ...}) that materializes as an EMPTY
        // Dictionary<string,object> — the key lookup then silently yields 0 and the probe reads
        // "unmeasurable" on every run even though performance.memory is alive. A bare number has no
        // envelope, so EvaluateAsync<long> returns the real heap size. performance.memory is
        // Chromium-only and can be absent mid-navigation on InteractiveAuto — the ?./|| still maps
        // that to 0 for the caller to assert on.
        return await page.EvaluateAsync<long>("() => performance.memory?.usedJSHeapSize || 0");
    }

    /// <summary>
    /// Re-probes (and, when needed, resurrects) the self-hosted demo hosts from an IN-FLIGHT
    /// failure. ClassInitialize already resurrects a dead host at every class boundary, but a test
    /// whose host died MID-TEST used to fail with <c>net::ERR_CONNECTION_REFUSED</c> and the
    /// resurrection waited for the next class — the casualty was avoidable. The cold-boot recovery
    /// in <see cref="GotoAppReadyAsync"/> calls this before its one reload, so a host that died
    /// while the app booted comes back for the SAME test. H1: the INSTANCE <see cref="TestContext"/>
    /// is forwarded, so the restart record names the test that triggered the recovery and its
    /// diagnostics land in that test's log instead of nowhere.
    /// </summary>
    internal Task RecoverDemoHostsAsync() => EnsureDemoHostsAsync(TestContext);

    private static async Task EnsureDemoHostsAsync(TestContext? context)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("TM_E2E_SELF_HOST"), "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await HostLock.WaitAsync();
        try
        {
            // No "already initialized" fast path here on purpose: every class init re-probes the
            // URLs (cheap when healthy — a refused/wedged host is answered in milliseconds) so a
            // demo host that dies mid-run is resurrected instead of failing every later test with
            // ERR_CONNECTION_REFUSED.
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
        TestContext? context,
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

        var existing = DemoHostProcesses.FirstOrDefault(host => host.Name == name);
        if (existing is { HasExited: false })
        {
            if (DateTimeOffset.UtcNow - existing.StartedAtUtc <= timeout)
            {
                // Still inside its readiness window (slow boot under load) — wait on the same
                // process rather than stack a second `dotnet run` on the same port.
                await WaitForHostReadyAsync(context, existing, urls, timeout);
                return;
            }

            // Alive past its readiness window and not serving — PROBABLY wedged. H3: one failed
            // probe is also the signature of a host that answers just after the probe timed out
            // under full-lane load, so the kill needs THREE consecutive failed probes spread over
            // ~6 s. Any healthy probe leaves the host in place.
            if (!await IsHostStuckAcrossConsecutiveProbesAsync(urls))
            {
                return;
            }

            // Wedged: alive past its readiness window, three probes in a row saw nothing. Replace it.
            DemoHostProcesses.Remove(existing);
            var wedgedOutput = existing.RecentOutput;
            existing.Dispose();
            RecordHostRestart(context, name, "unreachable-past-window", wedgedOutput);
            context?.WriteLine($"{name} was unreachable past its readiness window (3 consecutive failed probes) — restarting.");
        }
        else if (existing is not null)
        {
            // The process we started died mid-run — resurrect it. Throttle back-to-back restarts
            // so a permanently-broken host fails fast instead of paying the full startup per class.
            var sinceLastStart = DateTimeOffset.UtcNow - existing.StartedAtUtc;
            DemoHostProcesses.Remove(existing);
            var recentOutput = existing.RecentOutput;
            // B2: the exit code is captured INSIDE Dispose() before the Process handle goes away,
            // so reading it after Dispose is safe (the disposed Process object throws).
            existing.Dispose();
            if (sinceLastStart < TimeSpan.FromSeconds(30))
            {
                throw new InvalidOperationException($"{name} exited before it became ready. Recent output:{Environment.NewLine}{recentOutput}");
            }

            RecordHostRestart(context, name, "died-mid-run", recentOutput, existing.ExitCode);
            context?.WriteLine($"{name} died mid-run — restarting it.");
        }
        else if (_demoHostsInitialized)
        {
            // The hosts were healthy at first init but this one is unreachable now, and no tracked
            // process exists — an externally-started host died. Adopt it: self-host a replacement
            // instead of letting every remaining test fail with connection refused.
            RecordHostRestart(context, name, "externally-died", "");
            context?.WriteLine($"{name} became unreachable mid-run — self-hosting a replacement.");
        }

        var process = StartDemoHostProcess(name, projectPath, launchProfile);
        DemoHostProcesses.Add(process);
        await WaitForHostReadyAsync(context, process, urls, timeout);
    }

    private static async Task WaitForHostReadyAsync(
        TestContext? context,
        DemoHostProcess process,
        IReadOnlyList<string> urls,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"{process} exited before it became ready. Recent output:{Environment.NewLine}{process.RecentOutput}");
            }

            if (await AllUrlsReachableAsync(urls))
            {
                context?.WriteLine($"{process} ready at {string.Join(", ", urls)}.");
                return;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"{process} did not become ready at {string.Join(", ", urls)} within {timeout.TotalSeconds:n0}s. Recent output:{Environment.NewLine}{process.RecentOutput}");
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

    /// <summary>
    /// H3: whether a host is REALLY wedged — THREE consecutive failed probes spread over ~6 s.
    /// The old single-probe verdict killed hosts that were alive and answered a moment after the
    /// one probe timed out (a full-lane load spikes every probe at once), discarding a process
    /// that was a second away from serving. Any healthy probe in the window answers "not stuck".
    /// </summary>
    internal static async Task<bool> IsHostStuckAcrossConsecutiveProbesAsync(IReadOnlyList<string> urls)
    {
        const int requiredFailures = 3;
        for (var probe = 1; probe <= requiredFailures; probe++)
        {
            if (await AllUrlsReachableAsync(urls))
            {
                return false;
            }

            if (probe < requiredFailures)
            {
                // Probes at ~0 s / ~3 s / ~6 s: the window rides out one slow probe round without
                // stretching the wedged-host replacement beyond a few seconds.
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        return true;
    }

    /// <summary>
    /// One pooled probe client for the whole class (N193): the previous per-call
    /// <c>new HttpClient(handler)</c> churned a socket per 500 ms readiness poll across eight
    /// shards. The handler accepts untrusted certificates on loopback addresses only — every demo
    /// host is a self-signed localhost dev cert (see <see cref="DemoApiTestHttp"/>).
    /// </summary>
    private static readonly HttpClient ProbeClient = new(DemoApiTestHttp.CreateHandler())
    { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>
    /// Records a host resurrection to <c>TestResults/host-restarts.jsonl</c> — the machine-readable
    /// half of N209. The restart itself stays (the suite should still try to finish); what ends is
    /// its invisibility: the release-evidence run reads <see cref="HostRestartLog.TotalHostRestarts"/>
    /// and refuses to report a run with hidden server crashes as a clean green.
    /// </summary>
    private static void RecordHostRestart(TestContext? context, string name, string reason, string recentOutput, int? exitCode = null)
    {
        try
        {
            HostRestartLog.AppendRestartRecord(
                Path.Combine(FindRepositoryRoot(), "TestResults", "host-restarts.jsonl"),
                name,
                reason,
                recentOutput,
                exitCode,
                testName: context?.TestName);
        }
        catch
        {
            // The record is evidence, not the mechanism: a disk problem must not stop the restart.
        }
    }

    /// <summary>
    /// H4: records a shared-browser relaunch to <c>TestResults/browser-relaunches.jsonl</c> and
    /// <see cref="HostRestartLog.TotalBrowserRelaunches"/> — the browser twin of
    /// <see cref="RecordHostRestart"/>, so the lane summary can show how often the run had to
    /// resurrect the wedge-prone shared Chromium. Evidence, not the mechanism: a failed append
    /// never fails the relaunch.
    /// </summary>
    private static void RecordBrowserRelaunch(string reason, string? testName)
    {
        try
        {
            HostRestartLog.AppendBrowserRelaunchRecord(
                Path.Combine(FindRepositoryRoot(), "TestResults", "browser-relaunches.jsonl"),
                reason,
                testName);
        }
        catch
        {
            // The record is evidence, not the mechanism.
        }
    }

    /// <summary>
    /// The readiness verdict over a /health response (N193): 2xx exactly. The previous predicate
    /// — <c>status != ServiceUnavailable</c> — counted a crashed app answering 500 (or a
    /// misrouted 404) as reachable, so a dead host passed its own readiness gate.
    /// </summary>
    internal static bool IsReadyStatusCode(HttpStatusCode statusCode) =>
        (int)statusCode is >= 200 and <= 299;

    private static async Task<bool> IsUrlReachableAsync(string url)
    {
        try
        {
            // N193: probe the dedicated readiness endpoint, not the root URL — a crashed app
            // answering 500 on "/" used to read as "reachable" because only 503 counted as down.
            // /health must answer 2xx exactly. For the standalone WASM dev server there is no
            // endpoint to add: its SPA fallback serves index.html for extensionless paths, so
            // /health answers 200 as soon as the host serves at all — precisely the signal this
            // probe asks for.
            using var response = await ProbeClient.GetAsync($"{url.TrimEnd('/')}/health");
            return IsReadyStatusCode(response.StatusCode);
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

    /// <summary>
    /// The tracked demo host process. Internal (nested) so the harness unit tests can exercise the
    /// exit-code capture (B2) without standing a demo host up.
    /// </summary>
    internal sealed class DemoHostProcess : IDisposable
    {
        private readonly string _name;
        private readonly Process _process;
        private readonly ConcurrentQueue<string> _output = new();
        // B2: the exit code is captured ONCE — inside Dispose(), BEFORE _process.Dispose(). A
        // disposed Process throws InvalidOperationException from HasExited/ExitCode, so the
        // died-mid-run restart record that reads ExitCode after Dispose used to crash instead of
        // recording the evidence.
        private int? _exitCode;
        private bool _disposed;

        public DemoHostProcess(string name, Process process)
        {
            _name = name;
            _process = process;
            StartedAtUtc = DateTimeOffset.UtcNow;
        }

        public string Name => _name;

        /// <summary>When this process was launched — used to throttle resurrection attempts.</summary>
        public DateTimeOffset StartedAtUtc { get; }

        public bool HasExited => _process.HasExited;

        /// <summary>
        /// The dead process's exit code, or null while it is alive / not yet reaped. Recorded with a
        /// died-mid-run restart so the JSONL line can tell a crash (a Win32 fault code) from an
        /// external hard kill (taskkill leaves exit code 1 and an empty output tail). Safe to read
        /// after <see cref="Dispose"/>: the value rides the field captured before the handle died,
        /// and a disposed process answers null instead of throwing.
        /// </summary>
        public int? ExitCode => _exitCode ?? (!_disposed && _process.HasExited ? _process.ExitCode : null);

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

                // B2: capture BEFORE _process.Dispose() — after it the Process object is dead and
                // every member read throws InvalidOperationException.
                _exitCode ??= _process.HasExited ? _process.ExitCode : null;
            }
            catch
            {
                // Best-effort cleanup after the test run.
            }
            finally
            {
                _disposed = true;
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
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
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
