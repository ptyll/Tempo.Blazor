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
/// is set (a developer's own SQL Server, used exactly as given — one shared database). Otherwise
/// a Testcontainers <c>mcr.microsoft.com/mssql/server:2022-latest</c> container is started, the
/// same image the application's own E2E suite runs. When Docker is not reachable the fixture
/// throws and every test in the collection FAILS with "Docker required" — a missing service is
/// a red, never a skip.
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
            Server = new ResolvedServer(externalConnection, PerClassDatabases: false);
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
        Server = new ResolvedServer(builder.ConnectionString, PerClassDatabases: true);
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

    /// <summary>What <see cref="MsSqlTestDatabase"/> connects to, and whether it may CREATE DATABASE.</summary>
    /// <param name="ServerConnectionString">The container's connection string in container mode;
    /// the verbatim <c>REPORTSERVER_TEST_CONNECTION</c> in external mode.</param>
    /// <param name="PerClassDatabases">True only when the fixture owns the server and each test
    /// class may create (and drop) its own database. False for an external connection string,
    /// which is used as given — its login is not promised to carry CREATE DATABASE rights.</param>
    internal sealed record ResolvedServer(string ServerConnectionString, bool PerClassDatabases);
}

/// <summary>
/// Per-class real SQL Server test database for the report server catalog — an xUnit class
/// fixture: every class in <see cref="MsSqlTestCollection"/> gets its own database created with
/// <c>CREATE DATABASE</c> on the shared server (see <see cref="MsSqlContainerFixture"/>) and
/// dropped again on dispose, so classes cannot leak rows into each other even when a test
/// forgets <see cref="ResetAsync"/>. The database is migrated once through the authored EF Core
/// migrations and reset between tests with Respawn (the EF migrations-history table is
/// preserved). With <c>REPORTSERVER_TEST_CONNECTION</c> set there is no owned server: the given
/// connection string is used as-is for a single shared database, exactly as before.
/// </summary>
public sealed class MsSqlTestDatabase : IAsyncLifetime
{
    /// <summary>Environment variable that overrides the SQL Server test connection string.</summary>
    public const string ConnectionEnvironmentVariable = "REPORTSERVER_TEST_CONNECTION";

    /// <summary>The SQL Server image the suite starts when no connection override is set.</summary>
    internal const string ContainerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    private Respawner? _respawner;
    private string? _connectionString;
    private string? _ownedDatabase;

    /// <summary>The connection string for this class's SQL Server catalog test database.</summary>
    public string ConnectionString =>
        _connectionString
        ?? throw new InvalidOperationException(
            $"{nameof(MsSqlTestDatabase)} was read before its fixture initialized.");

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        MsSqlContainerFixture.ResolvedServer server = MsSqlContainerFixture.Server
            ?? throw new InvalidOperationException(
                $"{nameof(MsSqlContainerFixture)} resolved no server; its initialization is "
                + "guaranteed to run before any class of the mssql-report-catalog collection.");

        if (server.PerClassDatabases)
        {
            _ownedDatabase = $"TempoReportServerTests_{Guid.NewGuid():N}";
            _connectionString = new SqlConnectionStringBuilder(server.ServerConnectionString)
            {
                InitialCatalog = _ownedDatabase,
            }.ConnectionString;

            await using (var master = new SqlConnection(server.ServerConnectionString))
            {
                await master.OpenAsync().ConfigureAwait(false);
                await using var create = master.CreateCommand();
                // The name is generated here (hex suffix), never user input — bracket quoting is safe.
                create.CommandText = $"CREATE DATABASE [{_ownedDatabase}]";
                await create.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
        else
        {
            _connectionString = server.ServerConnectionString;
        }

        // Apply the catalog migrations (creates the database too when the connection string's
        // own InitialCatalog does not exist — the external-override path).
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
        if (_ownedDatabase is null)
        {
            return;
        }

        // The fixture owns this database — drop it so the shared container does not accumulate
        // one test database per class. SINGLE_USER WITH ROLLBACK kicks pooled connections still
        // holding the database open; the pool entry is cleared first so nothing reopens it.
        // A null Server at this point means the collection fixture is already disposed — the
        // container is going down anyway and takes the database with it.
        if (_connectionString is not null)
        {
            SqlConnection.ClearPool(new SqlConnection(_connectionString));
        }

        if (MsSqlContainerFixture.Server?.ServerConnectionString is not { Length: > 0 } masterConnection)
        {
            return;
        }

        await using var master = new SqlConnection(masterConnection);
        await master.OpenAsync().ConfigureAwait(false);
        await using var drop = master.CreateCommand();
        drop.CommandText =
            $"IF DB_ID('{_ownedDatabase}') IS NOT NULL "
            + $"ALTER DATABASE [{_ownedDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; "
            + $"IF DB_ID('{_ownedDatabase}') IS NOT NULL DROP DATABASE [{_ownedDatabase}]";
        await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}

/// <summary>xUnit collection that shares one SQL Server across all MsSql test classes.</summary>
[CollectionDefinition(Name)]
public sealed class MsSqlTestCollection : ICollectionFixture<MsSqlContainerFixture>
{
    /// <summary>Collection name.</summary>
    public const string Name = "mssql-report-catalog";
}
