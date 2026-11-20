using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Base class for Playwright E2E tests with support for multiple demo applications.
/// </summary>
[TestClass]
public abstract partial class PlaywrightTestBase
{
    /// <summary>
    /// Additional teardown callbacks run by the single assembly cleanup. Derived lanes that self-host
    /// extra processes (e.g. the report-server Api/Web in <see cref="ReportServerE2ETestBase"/>) register
    /// a killer here so a normal test run tears them down deterministically — MSTest permits only one
    /// <c>[AssemblyCleanup]</c> per assembly, so this is how other bases hook into it.
    /// </summary>
    internal static readonly List<Action> AdditionalAssemblyCleanups = [];

    /// <summary>
    /// Gets the base URL for the demo application under test.
    /// </summary>
    protected abstract string BaseUrl { get; }

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
