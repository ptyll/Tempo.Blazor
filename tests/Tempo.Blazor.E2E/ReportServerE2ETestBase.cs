using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Reporting.Abstractions.Definitions;
using Tempo.Reporting.Abstractions.Dtos;
using Tempo.Reporting.Abstractions.Serialization;
using Tempo.ReportServer.Api;
using Tempo.ReportServer.Api.Storage;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Base for the committed, CI-runnable Report Server E2E lane. Self-hosts the report-server
/// <b>Api</b> (SQLite, Keycloak-free dev auth) and <b>Web</b> (OIDC OFF, portal pointed at the Api)
/// over plain HTTP and drives them with Playwright, applying the E2E skill's functional-server /
/// functional-wasm render-mode split against the <c>#render-mode-marker</c> the Web renders.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in only: the hosts start (and the tests run) solely when <c>TM_RS_E2E</c> is set to
/// <c>1</c>/<c>true</c>, so the default demo E2E run is unaffected. Invoke the lane with
/// <c>TM_RS_E2E=1 dotnet test --filter TestCategory=ReportServerE2E</c> (set
/// <c>TM_E2E_SELF_HOST=false</c> to skip the unrelated demo hosts).
/// </para>
/// <para>
/// Why full-stack-minus-Keycloak and not the self-contained demo Web: the portal's catalog,
/// favorites and render-run history pages are pure HTTP consumers of the Api (<c>Api:BaseUrl</c>);
/// the demo Web host maps only render/metadata/export endpoints and holds no catalog store, and in
/// OIDC-off demo mode the portal calls the Api anonymously. The additive, config-gated
/// <c>Authentication:Dev</c> scheme on the Api authenticates those anonymous portal calls as a fixed
/// dev principal so the real catalog flows can run without Keycloak. DB assertions read the very
/// SQLite database the Api writes.
/// </para>
/// </remarks>
public abstract class ReportServerE2ETestBase : PlaywrightTestBase
{
    /// <summary>Api host base URL (plain HTTP so the Web→Api hop needs no dev-cert trust).</summary>
    protected const string ApiBaseUrl = "http://localhost:7001";

    /// <summary>
    /// Web (portal) host base URL. HTTPS on 7150 so it matches the WebAssembly leg's baked
    /// <c>Api:BaseUrl</c> (<c>https://localhost:7150</c>) — the browser-side client then resolves against
    /// this origin (a graceful 404 for the catalog routes it does not host). The Web→Api hop is
    /// server-side HTTP and needs no dev-cert trust.
    /// </summary>
    protected const string WebBaseUrl = "https://localhost:7150";

    /// <summary>Data tenant used by the OIDC-off demo session and the dev principal.</summary>
    protected const string TenantId = "northwind";

    private static readonly SemaphoreSlim RsHostLock = new(1, 1);
    private static readonly List<HostProcess> RsHostProcesses = [];
    private static bool _rsHostsInitialized;
    private static string _dbPath = string.Empty;
    private static bool _processExitHooked;

    /// <inheritdoc />
    protected override string BaseUrl => WebBaseUrl;

    /// <summary>True when the Report Server E2E lane is enabled via <c>TM_RS_E2E</c>.</summary>
    protected static bool LaneEnabled
    {
        get
        {
            var flag = Environment.GetEnvironmentVariable("TM_RS_E2E");
            return string.Equals(flag, "1", StringComparison.Ordinal) ||
                string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>SQLite connection string for the database the running Api writes.</summary>
    private static string DbConnectionString => $"Data Source={_dbPath}";

    /// <summary>
    /// Ensures the Api + Web hosts are running before each test. Marks the test inconclusive (rather
    /// than failing) when the lane is not enabled, so the class is inert in the default E2E run.
    /// </summary>
    [TestInitialize]
    public async Task ReportServerTestInitialize()
    {
        if (!LaneEnabled)
        {
            Assert.Inconclusive(
                "Report Server E2E lane is disabled. Set TM_RS_E2E=1 to run it " +
                "(dotnet test --filter TestCategory=ReportServerE2E).");
        }

        await EnsureReportServerHostsAsync(TestContext).ConfigureAwait(false);
    }

    private static async Task EnsureReportServerHostsAsync(TestContext context)
    {
        await RsHostLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_rsHostsInitialized)
            {
                return;
            }

            var repoRoot = FindRepositoryRootDirectory();
            _dbPath = ResolveDbPath();

            HookProcessExitCleanupOnce();

            // The Api owns the catalog database. When we start it fresh, wipe any prior file so each
            // lane run begins with an empty catalog; when an Api is already up (developer reuse) we
            // leave its data alone — every scenario keys on unique per-run names so pre-existing rows
            // never confuse an assertion.
            var apiAlreadyUp = await IsUrlReachableAsync($"{ApiBaseUrl}/health").ConfigureAwait(false);
            if (!apiAlreadyUp)
            {
                DeleteDatabaseFiles(_dbPath);
                await StartHostAsync(
                    context,
                    "ReportServer Api",
                    Path.Combine(repoRoot, "src", "Tempo.ReportServer.Api", "Tempo.ReportServer.Api.csproj"),
                    ApiBaseUrl,
                    $"{ApiBaseUrl}/health",
                    new Dictionary<string, string>
                    {
                        ["ConnectionStrings__ReportServer"] = DbConnectionString,
                        ["Database__Provider"] = "Sqlite",
                        ["Database__Seed__Enabled"] = "false",
                        ["Authentication__Dev__Enabled"] = "true",
                        ["Authentication__Dev__TenantId"] = TenantId,
                        ["Authentication__Dev__Roles"] = "report-admin",
                        // Allow the browser WASM leg's cross-origin calls (7150 -> 7001) should any be made.
                        ["Cors__FrontendOrigin"] = WebBaseUrl,
                    },
                    TimeSpan.FromSeconds(180)).ConfigureAwait(false);
            }

            var webAlreadyUp = await IsUrlReachableAsync(WebBaseUrl).ConfigureAwait(false);
            if (!webAlreadyUp)
            {
                await StartHostAsync(
                    context,
                    "ReportServer Web",
                    Path.Combine(repoRoot, "src", "Tempo.ReportServer.Web", "Tempo.ReportServer.Web.csproj"),
                    WebBaseUrl,
                    WebBaseUrl,
                    new Dictionary<string, string>
                    {
                        // OIDC stays OFF (appsettings Authority is empty). Point the portal's typed
                        // API client at the Api host so server-side (Server-leg) catalog calls resolve.
                        ["Api__BaseUrl"] = ApiBaseUrl,
                    },
                    TimeSpan.FromSeconds(240)).ConfigureAwait(false);
            }

            _rsHostsInitialized = true;
        }
        finally
        {
            RsHostLock.Release();
        }
    }

    private static string ResolveDbPath()
    {
        // Keep the (potentially large) catalog DB off the tight C: drive when Z: is available.
        var root = Directory.Exists(@"Z:\")
            ? @"Z:\rs-e2e"
            : Path.Combine(Path.GetTempPath(), "rs-e2e");
        Directory.CreateDirectory(root);
        return Path.Combine(root, "reportserver-e2e.db");
    }

    private static void DeleteDatabaseFiles(string dbPath)
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            try
            {
                var path = dbPath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best-effort: a stale handle just means the run reuses the existing schema.
            }
        }
    }

    // ---- Render-mode helpers (functional-server / functional-wasm) --------------------------------

    /// <summary>
    /// Forces the Server render leg by blocking the WebAssembly binary so the InteractiveAuto runtime
    /// cannot boot the browser runtime and stays on the SignalR circuit. Only <c>*.wasm</c> is aborted
    /// (not <c>_framework/dotnet.*</c>): the <c>dotnet.js</c> loader must still run so blazor.web.js can
    /// negotiate the Server fallback — the same approach the project's f12 drivers use.
    /// </summary>
    protected static async Task ForceServerLegAsync(IBrowserContext browserContext)
        => await browserContext.RouteAsync("**/*.wasm", route => route.AbortAsync()).ConfigureAwait(false);

    /// <summary>Waits for the render-mode marker to report an interactive renderer.</summary>
    protected static async Task WaitForInteractiveAsync(IPage page, int timeoutMs = 60_000)
        => await page.Locator("#render-mode-marker[data-interactive='true']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = timeoutMs })
            .ConfigureAwait(false);

    /// <summary>Reads the active render mode (Static | Server | WebAssembly) from the marker.</summary>
    protected static async Task<string> GetRenderModeAsync(IPage page)
        => await page.Locator("#render-mode-marker").GetAttributeAsync("data-mode").ConfigureAwait(false)
            ?? string.Empty;

    /// <summary>
    /// Opens a page on the Server render leg: a fresh context with the WASM runtime blocked, navigated
    /// to <paramref name="relativeUrl"/> and settled to <c>data-interactive=true</c>.
    /// </summary>
    protected async Task<IPage> OpenServerPageAsync(string relativeUrl)
    {
        var browserContext = await CreateContextAsync().ConfigureAwait(false);
        await ForceServerLegAsync(browserContext).ConfigureAwait(false);
        var page = await browserContext.NewPageAsync().ConfigureAwait(false);
        await page.GotoAsync(AbsoluteUrl(relativeUrl), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000,
        }).ConfigureAwait(false);
        await WaitForInteractiveAsync(page).ConfigureAwait(false);
        return page;
    }

    /// <summary>
    /// Opens a page on the WebAssembly render leg: primes the WASM cache on a first visit, then reloads
    /// until the marker reports <c>WebAssembly</c> (the InteractiveAuto leg switches to the browser
    /// runtime once it is cached).
    /// </summary>
    protected async Task<IPage> OpenWasmPageAsync(string relativeUrl)
    {
        var browserContext = await CreateContextAsync().ConfigureAwait(false);
        var page = await browserContext.NewPageAsync().ConfigureAwait(false);

        var target = AbsoluteUrl(relativeUrl);
        await page.GotoAsync(target, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000,
        }).ConfigureAwait(false);
        await WaitForInteractiveAsync(page).ConfigureAwait(false);

        // Reload until the InteractiveAuto runtime has cached and adopted the WebAssembly leg.
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(120);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (string.Equals(await GetRenderModeAsync(page).ConfigureAwait(false), "WebAssembly", StringComparison.Ordinal))
            {
                return page;
            }

            await page.WaitForTimeoutAsync(1500).ConfigureAwait(false);
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 }).ConfigureAwait(false);
            await WaitForInteractiveAsync(page).ConfigureAwait(false);
        }

        return page;
    }

    /// <summary>
    /// Opens a seeded report's viewer on the Server leg. The demo portal keeps its session per-circuit
    /// and in-memory, so a full navigation to <c>/reports/{id}</c> would start a fresh, unauthenticated
    /// circuit and be bounced to the login page. Instead this signs in, selects the report's folder in
    /// the explorer tree and clicks the report — an SPA navigation that keeps the signed-in circuit.
    /// </summary>
    protected async Task<IPage> OpenSeededReportViewerAsync(string folderPath, string reportId)
    {
        var page = await OpenServerPageAsync("/").ConfigureAwait(false);
        await DemoSignInAsync(page).ConfigureAwait(false);

        await page.GetByTestId($"tm-report-folder-{folderPath}").ClickAsync(new LocatorClickOptions { Timeout = 30_000 }).ConfigureAwait(false);
        var open = page.GetByTestId($"tm-report-open-{reportId.ToLowerInvariant()}");
        await open.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 }).ConfigureAwait(false);
        await open.ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("f12-viewer-page").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
        return page;
    }

    /// <summary>Signs in through the OIDC-off demo login form and lands on the report explorer.</summary>
    protected static async Task DemoSignInAsync(IPage page)
    {
        await page.GetByTestId("login-interactive-ready").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await page.GetByTestId("login-submit").ClickAsync().ConfigureAwait(false);
        await page.GetByTestId("f12-explorer-page").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
    }

    /// <summary>Builds an absolute portal URL from a relative path.</summary>
    protected static string AbsoluteUrl(string relativeUrl)
        => relativeUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? relativeUrl
            : $"{WebBaseUrl}/{relativeUrl.TrimStart('/')}";

    // ---- Database assertions (read the DB the Api writes) -----------------------------------------

    /// <summary>Opens a context on the Api's SQLite catalog database for direct row assertions.</summary>
    protected static ReportServerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ReportServerDbContext>()
            .UseSqlite(DbConnectionString)
            .Options;
        return new ReportServerDbContext(options, new ReportServerRequestContext());
    }

    /// <summary>Counts catalog folders by tenant + name (query filters ignored — no ambient tenant).</summary>
    protected static Task<int> CountFoldersAsync(string tenantId, string name)
        => QueryWithRetryAsync(db => db.Folders.IgnoreQueryFilters()
            .CountAsync(folder => folder.TenantId == tenantId && folder.Name == name));

    /// <summary>Counts catalog reports by tenant + name.</summary>
    protected static Task<int> CountReportsAsync(string tenantId, string name)
        => QueryWithRetryAsync(db => db.Reports.IgnoreQueryFilters()
            .CountAsync(report => report.TenantId == tenantId && report.Name == name));

    /// <summary>Counts per-user favorites for a report in a tenant.</summary>
    protected static Task<int> CountFavoritesAsync(string tenantId, string reportId)
        => QueryWithRetryAsync(db => db.Favorites
            .CountAsync(favorite => favorite.TenantId == tenantId && favorite.ReportId == reportId));

    /// <summary>Returns the most recent render-run row for a report, or null when none.</summary>
    protected static Task<RenderRunEntity?> LatestRenderRunAsync(string tenantId, string reportId)
        // Order by the autoincrement Id (highest = newest): SQLite cannot ORDER BY a DateTimeOffset.
        => QueryWithRetryAsync(db => db.RenderRuns
            .Where(run => run.TenantId == tenantId && run.ReportId == reportId)
            .OrderByDescending(run => run.Id)
            .FirstOrDefaultAsync());

    private static async Task<T> QueryWithRetryAsync<T>(Func<ReportServerDbContext, Task<T>> query)
    {
        // The Api process holds the same SQLite file; a concurrent write can briefly lock it, so read
        // with a short retry rather than failing an assertion on a transient "database is locked".
        Exception? last = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                await using var db = CreateDbContext();
                return await query(db).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or DbUpdateException)
            {
                last = ex;
                await Task.Delay(250).ConfigureAwait(false);
            }
        }

        throw last ?? new InvalidOperationException("Report Server DB query failed without an exception.");
    }

    // ---- Catalog seeding (through the Api, authenticated by the dev scheme) -----------------------

    /// <summary>Creates an HttpClient targeting the Api host (authenticated by the dev scheme, tenant <see cref="TenantId"/>).</summary>
    protected static HttpClient CreateApiClient()
        => new() { BaseAddress = new Uri(ApiBaseUrl), Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Creates a catalog folder via the Api and returns its id + canonical path.</summary>
    protected static async Task<(string FolderId, string Path)> SeedFolderAsync(string name)
    {
        using var client = CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/folders", new CreateReportFolderRequestDto
        {
            TenantId = TenantId,
            Name = name,
        }).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var folder = await response.Content.ReadFromJsonAsync<ReportFolderDto>().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Folder creation returned no body.");
        return (folder.FolderId, folder.Path);
    }

    /// <summary>
    /// Creates a report (blank or with a single required <c>AsOfDate</c> date parameter) via the Api and
    /// returns its id. The definition is serialized with the canonical serializer the server reads.
    /// </summary>
    protected static async Task<string> SeedReportAsync(string folderId, string name, bool parametric)
    {
        var definition = new ReportDefinition { Name = name };
        if (parametric)
        {
            definition.Parameters.Add(new ReportParameterDefinition
            {
                Name = "AsOfDate",
                Label = "As of date",
                DataType = ReportParameterType.Date,
                Required = true,
            });
        }

        using var client = CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/reports", new CreateReportRequestDto
        {
            TenantId = TenantId,
            FolderId = folderId,
            Name = name,
            DefinitionJson = ReportDefinitionJsonSerializer.Serialize(definition),
        }).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var report = await response.Content.ReadFromJsonAsync<ReportDetailDto>().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Report creation returned no body.");
        return report.ReportId;
    }

    // ---- Host process management ------------------------------------------------------------------

    private static async Task StartHostAsync(
        TestContext context,
        string name,
        string projectPath,
        string urls,
        string readinessUrl,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = FindRepositoryRootDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        // Ignore launchSettings (the Web profile opens a browser and binds a different port) and bind ours.
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(urls);
        // The run build must not fail on the repo-wide NuGet audit warning-as-error (NU1902, AngleSharp
        // via bUnit/AngleSharp) or other warnings-as-errors — we only need the host to launch.
        startInfo.ArgumentList.Add("--property:NuGetAudit=false");
        startInfo.ArgumentList.Add("--property:TreatWarningsAsErrors=false");
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";
        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var host = new HostProcess(name, process);
        process.OutputDataReceived += (_, args) => host.AddOutput(args.Data);
        process.ErrorDataReceived += (_, args) => host.AddOutput(args.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {name}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        RsHostProcesses.Add(host);

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"{name} exited before it became ready. Recent output:{Environment.NewLine}{host.RecentOutput}");
            }

            if (await IsUrlReachableAsync(readinessUrl).ConfigureAwait(false))
            {
                context.WriteLine($"{name} ready at {urls}.");
                return;
            }

            await Task.Delay(500).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"{name} did not become ready at {readinessUrl} within {timeout.TotalSeconds:n0}s. " +
            $"Recent output:{Environment.NewLine}{host.RecentOutput}");
    }

    private static async Task<bool> IsUrlReachableAsync(string url)
    {
        try
        {
            using var client = new HttpClient(DemoApiTestHttp.CreateHandler()) { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync(url).ConfigureAwait(false);
            // Require a real 200 so "ready" means the host actually serves (Api /health, Web login page),
            // not merely that the port answers.
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }

    private static void HookProcessExitCleanupOnce()
    {
        if (_processExitHooked)
        {
            return;
        }

        _processExitHooked = true;

        // Deterministic teardown on a normal test run goes through the single assembly cleanup; the
        // ProcessExit hook is the fallback for paths that skip it. Both are idempotent.
        PlaywrightTestBase.AdditionalAssemblyCleanups.Add(KillReportServerHosts);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillReportServerHosts();
    }

    private static void KillReportServerHosts()
    {
        foreach (var host in RsHostProcesses)
        {
            host.Dispose();
        }

        RsHostProcesses.Clear();
    }

    private static string FindRepositoryRootDirectory()
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

    private sealed class HostProcess : IDisposable
    {
        private readonly string _name;
        private readonly Process _process;
        private readonly ConcurrentQueue<string> _output = new();

        public HostProcess(string name, Process process)
        {
            _name = name;
            _process = process;
        }

        public string RecentOutput => string.Join(Environment.NewLine, _output.ToArray());

        public void AddOutput(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            _output.Enqueue(line);
            while (_output.Count > 120 && _output.TryDequeue(out _))
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
                // Best-effort cleanup at process exit.
            }
            finally
            {
                _process.Dispose();
            }
        }

        public override string ToString() => _name;
    }
}
