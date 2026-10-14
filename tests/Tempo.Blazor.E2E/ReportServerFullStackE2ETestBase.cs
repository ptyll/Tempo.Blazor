using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Reporting.Abstractions.Definitions;
using Tempo.Reporting.Abstractions.Dtos;
using Tempo.Reporting.Abstractions.Serialization;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Base for the NIGHTLY full-stack Report Server E2E lane (Fáze 13 PASS B). Unlike the committed CI
/// lane (<see cref="ReportServerE2ETestBase"/>, which self-hosts a Keycloak-free demo), this base
/// drives the <b>real</b> stack: the live Keycloak service (OIDC login + JWT bearer), the Api on
/// SQL Server with the EF-backed security stores and the scheduling worker enabled, the Web portal
/// with OIDC ON, and smtp4dev for scheduled-report delivery.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in only and GATED behind <c>TM_RS_FULLSTACK</c> (mirroring how the CI lane gates on
/// <c>TM_RS_E2E</c>): every test marks itself inconclusive unless the flag is set, so a normal
/// <c>dotnet test</c> / CI run never starts Keycloak-dependent hosts. Invoke the lane with
/// <c>TM_RS_FULLSTACK=1 dotnet test --filter TestCategory=ReportServerFullStack</c>.
/// </para>
/// <para>
/// Prerequisites the lane checks and (where possible) provisions: the Keycloak service must be
/// reachable at <c>http://localhost:8080</c> (realm <c>tempo-reports</c>) — the lane fails with a
/// clear message otherwise; smtp4dev is launched with the correct working directory if it is not
/// already running; SQL Server must be reachable for the Api's SqlServer provider. The Web client
/// secret is pulled from Keycloak at runtime (never committed).
/// </para>
/// </remarks>
public abstract class ReportServerFullStackE2ETestBase : PlaywrightTestBase
{
    /// <summary>Api host base URL. HTTPS on 7011 (distinct from the CI lane's 7001) so the WASM/browser
    /// legs can reach it over TLS without mixed-content, and the Web→Api BFF hop uses the trusted dev cert.</summary>
    protected const string ApiBaseUrl = "https://localhost:7011";

    /// <summary>Web (portal) base URL. Fixed at 7150 because that origin is the redirect URI registered
    /// on the Keycloak <c>tempo-report-web</c> client — the OIDC login redirect only succeeds here.
    /// NOTE: 7150 is also used by the Pass A CI demo lane (<see cref="ReportServerE2ETestBase"/>). That is
    /// safe only because the two lanes run under separate gated <c>--filter</c> invocations (each gated on
    /// its own env flag) and never in the same process — a single run binds this port with exactly one config.</summary>
    protected const string WebBaseUrl = "https://localhost:7150";

    /// <summary>Keycloak realm base.</summary>
    protected const string KeycloakRealmUrl = "http://localhost:8080/realms/tempo-reports";

    /// <summary>smtp4dev REST/web base.</summary>
    protected const string Smtp4DevWebUrl = "http://localhost:5050";

    /// <summary>Effective data tenant for the Keycloak users (no <c>tenant_id</c> claim → server default).</summary>
    protected const string TenantId = "default";

    /// <summary>The Keycloak confidential client the Web portal authenticates as.</summary>
    protected const string WebClientId = "tempo-report-web";

    private const string SmtpPort = "2525";
    private const string KeycloakBaseUrl = "http://localhost:8080";

    private static readonly SemaphoreSlim RsHostLock = new(1, 1);
    private static readonly List<HostProcess> RsHostProcesses = [];
    private static bool _rsHostsInitialized;
    private static bool _processExitHooked;
    private static string? _webClientSecret;

    private static readonly JsonSerializerOptions JsonWebOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    protected override string BaseUrl => WebBaseUrl;

    /// <summary>True when the full-stack lane is enabled via <c>TM_RS_FULLSTACK</c>.</summary>
    protected static bool LaneEnabled
    {
        get
        {
            var flag = Environment.GetEnvironmentVariable("TM_RS_FULLSTACK");
            return string.Equals(flag, "1", StringComparison.Ordinal) ||
                string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Ensures Keycloak, smtp4dev, the Api (SqlServer, real OIDC bearer, scheduling) and the Web
    /// (OIDC ON) are all up before each test. Marks the test inconclusive when the lane is disabled.
    /// </summary>
    [TestInitialize]
    public async Task ReportServerFullStackTestInitialize()
    {
        if (!LaneEnabled)
        {
            Assert.Inconclusive(
                "Report Server FULL-STACK E2E lane is disabled. Set TM_RS_FULLSTACK=1 to run it " +
                "(needs the live Keycloak service + smtp4dev + SQL Server): " +
                "dotnet test --filter TestCategory=ReportServerFullStack.");
        }

        await EnsureStackAsync(TestContext).ConfigureAwait(false);
    }

    private static async Task EnsureStackAsync(TestContext context)
    {
        await RsHostLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_rsHostsInitialized)
            {
                return;
            }

            HookProcessExitCleanupOnce();

            // 1) Keycloak must already be running (Windows service). Fail loud if not.
            if (!await IsUrlReachableAsync($"{KeycloakRealmUrl}/.well-known/openid-configuration").ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"Keycloak realm '{KeycloakRealmUrl}' is not reachable. The full-stack lane requires the " +
                    "Keycloak service to be running (realm tempo-reports, users admin1/author1/viewer1). " +
                    "Start it and re-run.");
            }

            // 2) Pull the Web client secret from Keycloak at runtime (never committed).
            _webClientSecret = await ResolveWebClientSecretAsync().ConfigureAwait(false);

            // 3) smtp4dev — launch it (with the required working directory) if it is not already up.
            await EnsureSmtp4DevAsync(context).ConfigureAwait(false);

            var repoRoot = FindRepositoryRootDirectory();

            // 4) Api on SQL Server: real Keycloak JWT bearer, EF-backed security stores (so ApiKeys +
            //    AuditEvents rows persist), and the scheduling worker on a fast poll for the email test.
            if (!await IsUrlReachableAsync($"{ApiBaseUrl}/health").ConfigureAwait(false))
            {
                await StartHostAsync(
                    context,
                    "ReportServer Api (full-stack)",
                    Path.Combine(repoRoot, "src", "Tempo.ReportServer.Api", "Tempo.ReportServer.Api.csproj"),
                    ApiBaseUrl,
                    $"{ApiBaseUrl}/health",
                    new Dictionary<string, string>
                    {
                        ["ConnectionStrings__ReportServer"] =
                            @"Server=localhost\SQLEXPRESS;Database=TempoReportServerFullStackE2E;Integrated Security=true;TrustServerCertificate=true;",
                        ["Database__Provider"] = "SqlServer",
                        ["Database__Seed__Enabled"] = "false",
                        // EF security persistence → real ApiKeys / AuditEvents rows to assert.
                        ["Security__Persistence"] = "Ef",
                        // Real Keycloak bearer (Dev scheme explicitly OFF).
                        ["Authentication__Dev__Enabled"] = "false",
                        ["Authentication__Jwt__Authority"] = KeycloakRealmUrl,
                        ["Authentication__Jwt__Audience"] = "tempo-report-api",
                        ["Authentication__Jwt__RequireHttpsMetadata"] = "false",
                        // Scheduling worker: fast poll so the email scenario completes within a bounded wait.
                        ["Scheduling__Enabled"] = "true",
                        ["Scheduling__PollInterval"] = "00:00:05",
                        ["Scheduling__Smtp__Host"] = "localhost",
                        ["Scheduling__Smtp__Port"] = SmtpPort,
                        ["Scheduling__Smtp__FromAddress"] = "reports@tempo.local",
                        // Allow the browser leg's cross-origin calls (7150 → 7011) should any be made.
                        ["Cors__FrontendOrigin"] = WebBaseUrl,
                    },
                    TimeSpan.FromSeconds(240)).ConfigureAwait(false);
            }

            // 5) Web on 7150 with OIDC ON against the live Keycloak, BFF pointed at the full-stack Api.
            if (!await IsUrlReachableAsync($"{WebBaseUrl}/_framework/blazor.web.js").ConfigureAwait(false))
            {
                await StartHostAsync(
                    context,
                    "ReportServer Web (full-stack OIDC)",
                    Path.Combine(repoRoot, "src", "Tempo.ReportServer.Web", "Tempo.ReportServer.Web.csproj"),
                    WebBaseUrl,
                    $"{WebBaseUrl}/_framework/blazor.web.js",
                    new Dictionary<string, string>
                    {
                        ["Api__BaseUrl"] = ApiBaseUrl,
                        ["Authentication__Oidc__Authority"] = KeycloakRealmUrl,
                        ["Authentication__Oidc__ClientId"] = WebClientId,
                        ["Authentication__Oidc__ClientSecret"] = _webClientSecret!,
                        ["Authentication__Oidc__RequireHttpsMetadata"] = "false",
                    },
                    TimeSpan.FromSeconds(300)).ConfigureAwait(false);
            }

            _rsHostsInitialized = true;
        }
        finally
        {
            RsHostLock.Release();
        }
    }

    // ---- Keycloak: secret + bearer tokens ---------------------------------------------------------

    /// <summary>
    /// Resolves the Web client secret: prefers <c>TM_RS_KC_WEB_SECRET</c>, otherwise pulls it from the
    /// Keycloak admin REST API (admin-cli direct grant, creds default admin/admin, overridable via
    /// <c>TM_RS_KC_ADMIN_USER</c>/<c>TM_RS_KC_ADMIN_PASS</c>). Never committed.
    /// </summary>
    private static async Task<string> ResolveWebClientSecretAsync()
    {
        var fromEnv = Environment.GetEnvironmentVariable("TM_RS_KC_WEB_SECRET");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var adminUser = Environment.GetEnvironmentVariable("TM_RS_KC_ADMIN_USER") ?? "admin";
        var adminPass = Environment.GetEnvironmentVariable("TM_RS_KC_ADMIN_PASS") ?? "admin";

        using var http = CreateLoopbackTolerantHttpClient();
        using var tokenResponse = await http.PostAsync(
            $"{KeycloakBaseUrl}/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = adminUser,
                ["password"] = adminPass,
            })).ConfigureAwait(false);
        tokenResponse.EnsureSuccessStatusCode();
        var adminToken = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false))
            .GetProperty("access_token").GetString()!;

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var clients = await http.GetFromJsonAsync<JsonElement>(
            $"{KeycloakBaseUrl}/admin/realms/tempo-reports/clients?clientId={WebClientId}").ConfigureAwait(false);
        var clientUuid = clients[0].GetProperty("id").GetString()!;
        var secret = await http.GetFromJsonAsync<JsonElement>(
            $"{KeycloakBaseUrl}/admin/realms/tempo-reports/clients/{clientUuid}/client-secret").ConfigureAwait(false);
        return secret.GetProperty("value").GetString()!;
    }

    /// <summary>
    /// Acquires a real Keycloak access token (aud <c>tempo-report-api</c>) for a portal user via the
    /// direct-grant flow, for HTTP-level Api scenarios (seeding, API keys, audit, scheduling).
    /// </summary>
    protected static async Task<string> AcquireBearerAsync(string user, string password = "Pass123!")
    {
        _webClientSecret ??= await ResolveWebClientSecretAsync().ConfigureAwait(false);
        using var http = CreateLoopbackTolerantHttpClient();
        using var response = await http.PostAsync(
            $"{KeycloakRealmUrl}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = WebClientId,
                ["client_secret"] = _webClientSecret!,
                ["username"] = user,
                ["password"] = password,
                ["scope"] = "openid tempo-report-api-audience",
            })).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        return payload.GetProperty("access_token").GetString()!;
    }

    /// <summary>Creates an HttpClient against the Api authenticated as <paramref name="user"/> (real bearer).</summary>
    protected static async Task<HttpClient> CreateBearerApiClientAsync(string user)
    {
        var token = await AcquireBearerAsync(user).ConfigureAwait(false);
        var client = CreateLoopbackTolerantHttpClient();
        client.BaseAddress = new Uri(ApiBaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Creates a bare HttpClient against the Api (no auth) for negative (401) assertions.</summary>
    protected static HttpClient CreateAnonymousApiClient()
    {
        var client = CreateLoopbackTolerantHttpClient();
        client.BaseAddress = new Uri(ApiBaseUrl);
        return client;
    }

    private static HttpClient CreateLoopbackTolerantHttpClient()
        => new(DemoApiTestHttp.CreateHandler()) { Timeout = TimeSpan.FromSeconds(60) };

    // ---- Keycloak browser login -------------------------------------------------------------------

    /// <summary>
    /// Logs in through the live Keycloak challenge from a page that has navigated to a protected URL:
    /// fills <c>#username</c>/<c>#password</c>, submits <c>#kc-login</c> and waits for the portal shell.
    /// </summary>
    protected static async Task KeycloakLoginAsync(IPage page, string user, string password = "Pass123!")
    {
        await page.Locator("#username").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
        await page.FillAsync("#username", user).ConfigureAwait(false);
        await page.FillAsync("#password", password).ConfigureAwait(false);
        await page.ClickAsync("#kc-login").ConfigureAwait(false);
        await page.GetByTestId("report-server-shell").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 }).ConfigureAwait(false);
    }

    /// <summary>Opens a fresh context, navigates to a protected page and completes the Keycloak login.</summary>
    protected async Task<(IBrowserContext Context, IPage Page)> LoginServerLegAsync(string user, string relativeUrl = "/reports")
    {
        var context = await CreateContextAsync().ConfigureAwait(false);
        await ForceServerLegAsync(context).ConfigureAwait(false);
        var page = await context.NewPageAsync().ConfigureAwait(false);
        await page.GotoAsync(AbsoluteUrl(relativeUrl), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000,
        }).ConfigureAwait(false);
        await KeycloakLoginAsync(page, user).ConfigureAwait(false);
        await WaitForInteractiveAsync(page).ConfigureAwait(false);
        return (context, page);
    }

    /// <summary>Short unique suffix to keep per-run catalog names collision-free on the shared SQL Server DB.</summary>
    protected static string UniqueTag() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>Polls <paramref name="condition"/> until true or fails the test with <paramref name="message"/>.</summary>
    protected static async Task PollAsync(Func<Task<bool>> condition, string message, int timeoutMs = 30_000)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(400).ConfigureAwait(false);
        }

        Assert.Fail(message);
    }

    // ---- Render-mode helpers (functional-server / functional-wasm) --------------------------------

    /// <summary>Blocks the WebAssembly binary so the InteractiveAuto portal stays on the Server circuit.</summary>
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
    /// Fetches the same-origin <c>/auth/token</c> in the browser and returns the access token, or an
    /// empty string when the session is gone (an unauthenticated call is challenged/redirected to a
    /// non-JSON Keycloak page rather than returning a token).
    /// </summary>
    protected static async Task<string> GetAuthTokenFromBrowserAsync(IPage page)
        => await page.EvaluateAsync<string>(
            """
            async () => {
                try {
                    const r = await fetch('/auth/token', { credentials: 'include' });
                    const ct = r.headers.get('content-type') || '';
                    if (!r.ok || !ct.includes('application/json')) return '';
                    const j = await r.json();
                    return j.accessToken ?? '';
                } catch { return ''; }
            }
            """).ConfigureAwait(false);

    /// <summary>Reads the <c>preferred_username</c> claim from a JWT access token (unvalidated decode).</summary>
    protected static string ReadPreferredUsername(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return string.Empty;
        }

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("preferred_username", out var value) ? value.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>Builds an absolute portal URL from a relative path.</summary>
    protected static string AbsoluteUrl(string relativeUrl)
        => relativeUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? relativeUrl
            : $"{WebBaseUrl}/{relativeUrl.TrimStart('/')}";

    // ---- Catalog seeding (via a real admin bearer, tenant "default") ------------------------------

    /// <summary>Creates a catalog folder via the Api (admin bearer) and returns its id + canonical path.</summary>
    protected static async Task<(string FolderId, string Path)> SeedFolderAsync(string name)
    {
        using var client = await CreateBearerApiClientAsync("admin1").ConfigureAwait(false);
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

    /// <summary>Creates a blank report via the Api (admin bearer) and returns its id.</summary>
    protected static async Task<string> SeedReportAsync(string folderId, string name)
    {
        var definition = new ReportDefinition { Name = name };
        using var client = await CreateBearerApiClientAsync("admin1").ConfigureAwait(false);
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

    // ---- smtp4dev REST ----------------------------------------------------------------------------

    /// <summary>Polls the smtp4dev REST API until a message whose subject contains <paramref name="subjectFragment"/>
    /// arrives, and returns its message id. Fails after <paramref name="timeoutMs"/>.</summary>
    protected static async Task<string> WaitForEmailAsync(string subjectFragment, int timeoutMs = 180_000)
    {
        using var http = CreateLoopbackTolerantHttpClient();
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var page = await http.GetFromJsonAsync<JsonElement>(
                    $"{Smtp4DevWebUrl}/api/Messages?pageSize=50&sortColumn=receivedDate&sortIsDescending=true",
                    JsonWebOptions).ConfigureAwait(false);
                if (page.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                {
                    foreach (var message in results.EnumerateArray())
                    {
                        var subject = message.TryGetProperty("subject", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                        if (subject.Contains(subjectFragment, StringComparison.OrdinalIgnoreCase))
                        {
                            return message.GetProperty("id").GetString()!;
                        }
                    }
                }
            }
            catch (HttpRequestException)
            {
                // smtp4dev still warming up — retry.
            }

            await Task.Delay(2000).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"No smtp4dev message with subject containing '{subjectFragment}' arrived within {timeoutMs / 1000}s.");
    }

    /// <summary>
    /// Returns true when the given smtp4dev message carries an <c>application/pdf</c> attachment. The
    /// message detail exposes a MIME <c>parts</c> tree; a PDF attachment is a part with
    /// <c>isAttachment=true</c> and a <c>Content-Type: application/pdf</c> header (and a <c>.pdf</c>
    /// entry in a part's <c>attachments</c> list).
    /// </summary>
    protected static async Task<bool> MessageHasPdfAttachmentAsync(string messageId)
    {
        using var http = CreateLoopbackTolerantHttpClient();
        var message = await http.GetFromJsonAsync<JsonElement>(
            $"{Smtp4DevWebUrl}/api/Messages/{messageId}", JsonWebOptions).ConfigureAwait(false);
        return message.TryGetProperty("parts", out var parts) && PartsHavePdf(parts);
    }

    private static bool PartsHavePdf(JsonElement part)
    {
        if (part.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in part.EnumerateArray())
            {
                if (PartsHavePdf(element))
                {
                    return true;
                }
            }

            return false;
        }

        if (part.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // A .pdf entry in this part's attachments list.
        if (part.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
        {
            foreach (var attachment in attachments.EnumerateArray())
            {
                var fileName = attachment.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? string.Empty : string.Empty;
                if (fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // An attachment part whose Content-Type is application/pdf.
        var isAttachment = part.TryGetProperty("isAttachment", out var ia) && ia.ValueKind == JsonValueKind.True;
        if (isAttachment && part.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Array)
        {
            foreach (var header in headers.EnumerateArray())
            {
                var name = header.TryGetProperty("name", out var hn) ? hn.GetString() ?? string.Empty : string.Empty;
                var value = header.TryGetProperty("value", out var hv) ? hv.GetString() ?? string.Empty : string.Empty;
                if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase) &&
                    value.Contains("application/pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // Recurse into child parts.
        if (part.TryGetProperty("childParts", out var children))
        {
            return PartsHavePdf(children);
        }

        return false;
    }

    private static async Task EnsureSmtp4DevAsync(TestContext context)
    {
        if (await IsUrlReachableAsync($"{Smtp4DevWebUrl}/api/Messages?pageSize=1").ConfigureAwait(false))
        {
            context.WriteLine("smtp4dev already running — reusing.");
            return;
        }

        var projectDir = Environment.GetEnvironmentVariable("TM_SMTP4DEV_DIR")
            ?? @"C:\work\smtp4dev-master\Rnwood.Smtp4dev";
        var exe = Path.Combine(projectDir, "bin", "Release", "net10.0", "Rnwood.Smtp4dev.exe");
        if (!File.Exists(exe))
        {
            throw new InvalidOperationException(
                $"smtp4dev executable not found at '{exe}'. Set TM_SMTP4DEV_DIR to its project directory " +
                "(the one containing wwwroot) or start smtp4dev manually (SMTP :2525, web :5050).");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            // IMPORTANT: WorkingDirectory must be the project dir (its wwwroot) or smtp4dev fails to start.
            WorkingDirectory = projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add($"--smtpport={SmtpPort}");
        startInfo.ArgumentList.Add($"--urls={Smtp4DevWebUrl}");
        startInfo.ArgumentList.Add("--db=");            // in-memory store
        startInfo.ArgumentList.Add("--nousersettings");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var host = new HostProcess("smtp4dev", process);
        process.OutputDataReceived += (_, args) => host.AddOutput(args.Data);
        process.ErrorDataReceived += (_, args) => host.AddOutput(args.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start smtp4dev.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        RsHostProcesses.Add(host);

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"smtp4dev exited before it became ready. Recent output:{Environment.NewLine}{host.RecentOutput}");
            }

            if (await IsUrlReachableAsync($"{Smtp4DevWebUrl}/api/Messages?pageSize=1").ConfigureAwait(false))
            {
                context.WriteLine("smtp4dev ready (SMTP :2525, web :5050).");
                return;
            }

            await Task.Delay(500).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"smtp4dev did not become ready within 60s. Recent output:{Environment.NewLine}{host.RecentOutput}");
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
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(urls);
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
            using var client = CreateLoopbackTolerantHttpClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            using var response = await client.GetAsync(url).ConfigureAwait(false);
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
            while (_output.Count > 160 && _output.TryDequeue(out _))
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
