using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;
using Tempo.ReportServer.Api;
using Tempo.ReportServer.Api.Storage;
using Tempo.Reporting.Abstractions;
using Testcontainers.MsSql;

namespace Tempo.ReportServer.Api.Tests.MsSql;

/// <summary>
/// The one SQL Server instance the whole MsSql suite runs against. xUnit 2.x has no assembly
/// fixture, so the closest equivalent is used: a collection fixture on
/// <see cref="MsSqlTestCollection"/>, which covers exactly the classes in
/// <c>Tempo.ReportServer.Api.Tests.MsSql</c> — one container per test run.
/// <para>
/// Resolution order: <see cref="MsSqlTestDatabase.ConnectionEnvironmentVariable"/> wins when it
/// is set — and N213: an external connection is now only a SERVER address; every
/// <see cref="MsSqlTestDatabase"/> creates its own <c>tempo_test_*</c> database on it, so a
/// developer-supplied connection string can never point the suite's Respawner at a database the
/// developer owns. Otherwise a Testcontainers <c>mcr.microsoft.com/mssql/server:2022-latest</c>
/// container is started, the same image the application's own E2E suite runs. When Docker is not
/// reachable the fixture throws and every test in the collection FAILS with "Docker required" —
/// a missing service is a red, never a skip.
/// </para>
/// </summary>
public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    private const string ContainerPassword = "Tempo_ReportServer_Tests!2026";

    private MsSqlContainer? _container;

    /// <summary>
    /// Static bridge to the per-class <see cref="MsSqlTestDatabase"/> fixtures. xUnit cannot
    /// inject a collection fixture into a class fixture, and the runner initializes a collection
    /// fixture before it runs any test class of the collection — so the class fixtures read what
    /// was resolved here.
    /// </summary>
    internal static ResolvedServer? Server { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable(MsSqlTestDatabase.ConnectionEnvironmentVariable)
                is { Length: > 0 } externalConnection)
        {
            Server = new ResolvedServer(externalConnection);
            return;
        }

        _container = new MsSqlBuilder(MsSqlTestDatabase.ContainerImage)
            .WithPassword(ContainerPassword)
            .Build();
        try
        {
            await _container.StartAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Docker required for the Tempo.ReportServer.Api.Tests.MsSql suite: no "
                + $"{MsSqlTestDatabase.ConnectionEnvironmentVariable} environment variable is set "
                + $"and the {MsSqlTestDatabase.ContainerImage} container could not be started.",
                exception);
        }

        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            // Testcontainers serves a self-signed certificate — required here, not a weakening.
            TrustServerCertificate = true,
        };
        Server = new ResolvedServer(builder.ConnectionString);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        Server = null;
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>What <see cref="MsSqlTestDatabase"/> connects to.</summary>
    /// <param name="ServerConnectionString">The container's connection string in container mode;
    /// the verbatim <c>REPORTSERVER_TEST_CONNECTION</c> in external mode — used as a SERVER
    /// address only: its <c>Initial Catalog</c> is ignored because every fixture database is
    /// created fresh (N213).</param>
    internal sealed record ResolvedServer(string ServerConnectionString)
    {
        /// <summary>
        /// N175: the synthesized record <c>ToString()</c> would print the connection string
        /// verbatim — including <c>Password=</c> — into any future assert or log line. The string
        /// is never needed in readable output (diagnose through
        /// <c>SqlConnectionStringBuilder(ServerConnectionString).DataSource</c> explicitly if it
        /// ever is), so <c>ToString</c> prints nothing of it at all — no value, no key names.
        /// </summary>
        public override string ToString() => "ResolvedServer { redacted }";
    }
}

/// <summary>
/// Per-class real SQL Server test database for the report server catalog — an xUnit class
/// fixture: every class in <see cref="MsSqlTestCollection"/> gets its own database created with
/// <c>CREATE DATABASE</c> on the shared server (see <see cref="MsSqlContainerFixture"/>) and
/// dropped again on dispose, so classes cannot leak rows into each other even when a test
/// forgets <see cref="ResetAsync"/>. The database is migrated once through the authored EF Core
/// migrations and reset between tests with Respawn (the EF migrations-history table is
/// preserved).
/// <para>
/// N213 — the external-override path is IDENTICAL: with <c>REPORTSERVER_TEST_CONNECTION</c> set,
/// the given connection string is a SERVER address, never a database. Any <c>Initial Catalog</c>
/// the user put in it is ignored and a private <c>tempo_test_*</c> database is created anyway —
/// the old "use it as-is" branch let Respawner wipe every table of a database the developer owns.
/// An external login without CREATE DATABASE rights is refused outright rather than silently
/// falling back to the shared database.
/// </para>
/// </summary>
public sealed class MsSqlTestDatabase : IAsyncLifetime
{
    /// <summary>
    /// Environment variable that overrides the SQL Server test connection string. The value is a
    /// SERVER address: its <c>Initial Catalog</c> is always ignored — the fixture creates its own
    /// <c>tempo_test_*</c> database on that server and drops it on dispose (N213).
    /// </summary>
    public const string ConnectionEnvironmentVariable = "REPORTSERVER_TEST_CONNECTION";

    /// <summary>
    /// The SQL Server image the suite starts when no connection override is set. N178: pinned by
    /// digest 2026-09-23 (was the floating <c>:2022-latest</c> tag — a CU re-tag could silently
    /// change engine behavior between runs). MUST stay byte-identical with
    /// <c>tests/Tempo.ReportServer.Web.Tests/SqlServerCacheFixture.cs</c>'s
    /// <c>ContainerImage</c> — both lanes must run the same engine; guarded by
    /// <c>PinnedImageDigestMatchesInBothFixtures</c>.
    /// </summary>
    internal const string ContainerImage = "mcr.microsoft.com/mssql/server@sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090";

    private Respawner? _respawner;
    private string? _connectionString;
    private string? _ownedDatabase;
    private string? _serverConnectionString;

    /// <summary>
    /// Test seam (N213): an explicit server resolution — exactly the shape
    /// <c>REPORTSERVER_TEST_CONNECTION</c> produces — injected without touching the shared static
    /// <see cref="MsSqlContainerFixture.Server"/>, so an isolation test stays parallel-safe against
    /// the mssql-report-catalog collection.
    /// </summary>
    internal MsSqlContainerFixture.ResolvedServer? ServerOverride { get; set; }

    /// <summary>
    /// Test seam (N179): invoked inside <see cref="InitializeAsync"/> after
    /// <c>CREATE DATABASE</c> succeeds and before the EF migrations run — lets an isolation test
    /// inject a mid-init failure deterministically and observe the owned-database cleanup.
    /// </summary>
    internal Func<Task>? AfterDatabaseCreatedHook { get; set; }

    /// <summary>Test seam (N179): the generated <c>tempo_test_*</c> name, for sys.databases probes.</summary>
    internal string? OwnedDatabaseForTest => _ownedDatabase;

    /// <summary>The connection string for this class's SQL Server catalog test database.</summary>
    public string ConnectionString =>
        _connectionString
        ?? throw new InvalidOperationException(
            $"{nameof(MsSqlTestDatabase)} was read before its fixture initialized.");

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        MsSqlContainerFixture.ResolvedServer server = ServerOverride
            ?? MsSqlContainerFixture.Server
            ?? throw new InvalidOperationException(
                $"{nameof(MsSqlContainerFixture)} resolved no server; its initialization is "
                + "guaranteed to run before any class of the mssql-report-catalog collection.");

        _serverConnectionString = server.ServerConnectionString;
        _ownedDatabase = $"tempo_test_{Guid.NewGuid():N}";
        _connectionString = new SqlConnectionStringBuilder(server.ServerConnectionString)
        {
            InitialCatalog = _ownedDatabase,
        }.ConnectionString;

        try
        {
            // Always CREATE DATABASE — the only isolation the fixture can guarantee (N213).
            await CreateOwnedDatabaseAsync(_serverConnectionString, _ownedDatabase).ConfigureAwait(false);

            if (AfterDatabaseCreatedHook is { } afterCreateHook)
            {
                await afterCreateHook().ConfigureAwait(false);
            }

            // Apply the catalog migrations inside the freshly created database.
            await using (var context = CreateDbContext("default"))
            {
                await context.Database.MigrateAsync().ConfigureAwait(false);
            }

            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
            {
                TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory")],
                DbAdapter = DbAdapter.SqlServer,
            }).ConfigureAwait(false);
        }
        catch
        {
            // N213/N179: an owned database whose initialization failed must not linger on a server
            // the fixture does not own — drop it (best effort) before the exception escapes.
            await DropOwnedDatabaseQuietlyAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Resets all catalog tables to empty, keeping the schema and migration history.</summary>
    public async Task ResetAsync()
    {
        if (_respawner is null)
        {
            return;
        }

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await _respawner.ResetAsync(connection).ConfigureAwait(false);
    }

    /// <summary>Creates a fresh EF context whose ambient tenant is <paramref name="tenantId"/>.</summary>
    public ReportServerDbContext CreateDbContext(string tenantId)
    {
        var requestContext = new ReportServerRequestContext();
        requestContext.Set(new ReportExecutionContext(tenantId, "test-user", "en-US"));
        var options = new DbContextOptionsBuilder<ReportServerDbContext>()
            .UseSqlServer(
                ConnectionString,
                sql => sql.MigrationsAssembly(typeof(ReportServerDbContext).Assembly.GetName().Name))
            .Options;
        return new ReportServerDbContext(options, requestContext);
    }

    /// <summary>Creates an <see cref="EfReportServerStore"/> whose ambient tenant is <paramref name="tenantId"/>.</summary>
    public (ReportServerDbContext Context, EfReportServerStore Store) CreateStore(string tenantId)
    {
        var context = CreateDbContext(tenantId);
        return (context, new EfReportServerStore(context));
    }

    /// <summary>Creates an <see cref="EfReportApiKeyStore"/> over a fresh context.</summary>
    public (ReportServerDbContext Context, EfReportApiKeyStore Store) CreateApiKeyStore(string tenantId = "tenant-a")
    {
        var context = CreateDbContext(tenantId);
        return (context, new EfReportApiKeyStore(context));
    }

    /// <summary>Creates an <see cref="EfReportAuditLog"/> over a fresh context.</summary>
    public (ReportServerDbContext Context, EfReportAuditLog Log) CreateAuditLog(string tenantId = "tenant-a")
    {
        var context = CreateDbContext(tenantId);
        return (context, new EfReportAuditLog(context));
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        // The fixture owns this database — drop it so the shared server does not accumulate one
        // test database per class (in external mode the server is NOT thrown away with the run,
        // so the drop is what keeps it clean). SINGLE_USER WITH ROLLBACK kicks pooled connections
        // still holding the database open; the pool entry is cleared first so nothing reopens it.
        if (_connectionString is not null)
        {
            // N176: the probing connection is a resource too — dispose it after clearing the pool.
            using var probe = new SqlConnection(_connectionString);
            SqlConnection.ClearPool(probe);
        }

        await DropOwnedDatabaseAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// <c>CREATE DATABASE</c> on the resolved server — the shared private helper also used by
    /// <see cref="DropOwnedDatabaseAsync"/>'s sibling path. A denied login is refused here with a
    /// message that says WHY: without the permission the fixture cannot guarantee isolation, so
    /// falling back to a shared database would repeat the N213 wipe-a-developer's-database hole.
    /// </summary>
    private static async Task CreateOwnedDatabaseAsync(string serverConnectionString, string database)
    {
        try
        {
            // The name is generated here (hex suffix), never user input — bracket quoting is safe.
            await ExecuteAgainstMasterAsync(
                serverConnectionString, $"CREATE DATABASE [{database}]").ConfigureAwait(false);
        }
        catch (SqlException ex) when (IsCreateDatabaseDenied(ex))
        {
            throw new InvalidOperationException(
                $"{ConnectionEnvironmentVariable} must carry CREATE DATABASE permission — a shared "
                + "database without it must not be used, because the fixture cannot guarantee "
                + "isolation there.", ex);
        }
    }

    private static bool IsCreateDatabaseDenied(SqlException ex) =>
        ex.Message.Contains("CREATE DATABASE permission", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("permission", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs <paramref name="commandText"/> against <c>master</c> on the resolved server — shared
    /// by create-on-init and drop-on-dispose so both see the same connection handling.
    /// </summary>
    private static async Task ExecuteAgainstMasterAsync(string serverConnectionString, string commandText)
    {
        var masterConnectionString = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString;

        await using var master = new SqlConnection(masterConnectionString);
        await master.OpenAsync().ConfigureAwait(false);
        await using var command = master.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Drops <see cref="_ownedDatabase"/> if it exists. Uses the server string captured at init
    /// (works in ServerOverride/isolation tests where the collection static is null); a missing
    /// server string means the container is going down anyway and takes the database with it.
    /// </summary>
    private async Task DropOwnedDatabaseAsync()
    {
        if (_ownedDatabase is null)
        {
            return;
        }

        string? masterConnection = _serverConnectionString
            ?? MsSqlContainerFixture.Server?.ServerConnectionString;
        if (masterConnection is not { Length: > 0 })
        {
            return;
        }

        await ExecuteAgainstMasterAsync(
            masterConnection,
            $"IF DB_ID('{_ownedDatabase}') IS NOT NULL "
            + $"ALTER DATABASE [{_ownedDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; "
            + $"IF DB_ID('{_ownedDatabase}') IS NOT NULL DROP DATABASE [{_ownedDatabase}]").ConfigureAwait(false);
        _ownedDatabase = null;
    }

    /// <summary>Best-effort drop used on the init-failure path — must never mask the real error.</summary>
    private async Task DropOwnedDatabaseQuietlyAsync()
    {
        try
        {
            await DropOwnedDatabaseAsync().ConfigureAwait(false);
        }
        catch
        {
            // The original exception is the finding; a cleanup failure would only hide it.
        }
    }
}

/// <summary>xUnit collection that shares one SQL Server across all MsSql test classes.</summary>
[CollectionDefinition(Name)]
public sealed class MsSqlTestCollection : ICollectionFixture<MsSqlContainerFixture>
{
    /// <summary>Collection name.</summary>
    public const string Name = "mssql-report-catalog";
}
