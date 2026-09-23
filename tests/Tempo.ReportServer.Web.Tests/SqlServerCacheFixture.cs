using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Tempo.ReportServer.Web.Tests;

/// <summary>
/// The one SQL Server the Web.Tests SQL-cache lane runs against — an xUnit class fixture that
/// mirrors the <c>Tempo.ReportServer.Api.Tests.MsSql</c> pattern (Fáze 19):
/// <see cref="ConnectionEnvironmentVariable"/> wins when it is set; otherwise a Testcontainers
/// <c>mcr.microsoft.com/mssql/server:2022-latest</c> container is started — the same image the
/// application's own E2E suite runs. When Docker is not reachable the fixture throws and the
/// dependent test FAILS with "Docker required" — a missing service is a red, never a silent skip.
/// <para>
/// N213 — an external connection string is a SERVER address, never a database: any
/// <c>Initial Catalog</c> it carries is ignored and the fixture creates its own
/// <c>tempo_cache_test_*</c> database (dropped again on dispose, external servers included).
/// The previous behaviour — overwriting whatever <c>Initial Catalog</c> said, or the fixed
/// <c>TempoReportServerWebTests</c> name — let the lane write into (and clean) a database a
/// developer actually uses.
/// </para>
/// <para>
/// The fixture owns one database holding the <c>dbo.TokenCache</c> table in exactly the shape
/// <c>dotnet sql-cache create</c> produces, so a test can point two independent
/// <c>SqlServerCache</c> instances at the same table.
/// </para>
/// </summary>
public sealed class SqlServerCacheFixture : IAsyncLifetime
{
    /// <summary>
    /// Environment variable carrying a developer-supplied SQL Server connection string. The value
    /// is a SERVER address: its <c>Initial Catalog</c> is always ignored — the fixture creates its
    /// own <c>tempo_cache_test_*</c> database on that server and drops it on dispose (N213).
    /// </summary>
    public const string ConnectionEnvironmentVariable = "REPORTSERVER_TEST_CONNECTION";

    /// <summary>SQL Server image — the same one the app E2E suite runs.</summary>
    public const string ContainerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    /// <summary>
    /// Name prefix of the per-fixture generated database — kept as the documentation reference for
    /// the generated <c>tempo_cache_test_{guid}</c> names; the actual database is unique per run
    /// (N213), so this constant is the prefix, not the name.
    /// </summary>
    public const string DatabaseName = "tempo_cache_test";

    private const string ContainerPassword = "Tempo_ReportServer_Tests!2026";

    private MsSqlContainer? _container;
    private string? _serverConnectionString;
    private string? _ownedDatabase;

    /// <summary>Connection string pointing at the fixture's own database with the cache table ready.</summary>
    public string CacheConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        string serverConnectionString;
        if (Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable) is { Length: > 0 } externalConnection)
        {
            serverConnectionString = externalConnection;
        }
        else
        {
            _container = new MsSqlBuilder(ContainerImage)
                .WithPassword(ContainerPassword)
                .Build();
            try
            {
                await _container.StartAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Docker required for the SQL-cache token-store tests: no "
                    + $"{ConnectionEnvironmentVariable} environment variable is set "
                    + $"and the {ContainerImage} container could not be started.",
                    exception);
            }

            serverConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
            {
                // Testcontainers serves a self-signed certificate — required here, not a weakening.
                TrustServerCertificate = true,
            }.ConnectionString;
        }

        _serverConnectionString = serverConnectionString;
        _ownedDatabase = $"{DatabaseName}_{Guid.NewGuid():N}";
        CacheConnectionString = await PrepareSqlCacheTableAsync(serverConnectionString, _ownedDatabase)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        // N213: drop the fixture-owned database on ANY server kind — in external mode the server
        // outlives the run, so without the drop every lane would leave a tempo_cache_test_* behind.
        if (_serverConnectionString is { Length: > 0 } server && _ownedDatabase is not null)
        {
            var masterConnectionString = new SqlConnectionStringBuilder(server)
            {
                InitialCatalog = "master",
            }.ConnectionString;

            if (CacheConnectionString is { Length: > 0 })
            {
                using var probe = new SqlConnection(CacheConnectionString);
                SqlConnection.ClearPool(probe);
            }

            await using var master = new SqlConnection(masterConnectionString);
            await master.OpenAsync().ConfigureAwait(false);
            await using var drop = master.CreateCommand();
            drop.CommandText =
                $"IF DB_ID('{_ownedDatabase}') IS NOT NULL "
                + $"ALTER DATABASE [{_ownedDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; "
                + $"IF DB_ID('{_ownedDatabase}') IS NOT NULL DROP DATABASE [{_ownedDatabase}]";
            await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<string> PrepareSqlCacheTableAsync(string serverConnectionString, string database)
    {
        var cacheConnectionString = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = database }.ConnectionString;

        try
        {
            // Connect to master explicitly — an external string may carry an Initial Catalog that
            // does not exist (it is ignored by design, N213), and opening it would fail here.
            var masterConnectionString = new SqlConnectionStringBuilder(serverConnectionString)
            {
                InitialCatalog = "master",
            }.ConnectionString;
            await using (var server = new SqlConnection(masterConnectionString))
            {
                await server.OpenAsync().ConfigureAwait(false);
                await using var createDb = server.CreateCommand();
                // The name is generated per run (GUID suffix), never user input — bracket quoting is safe.
                createDb.CommandText = $"CREATE DATABASE [{database}];";
                await createDb.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
        catch (SqlException ex) when (ex.Message.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{ConnectionEnvironmentVariable} must carry CREATE DATABASE permission — a shared "
                + "database without it must not be used, because the fixture cannot guarantee "
                + "isolation there.", ex);
        }

        await using var db = new SqlConnection(cacheConnectionString);
        await db.OpenAsync().ConfigureAwait(false);
        await using var createTable = db.CreateCommand();
        // The schema `dotnet sql-cache create` produces for a Microsoft.Extensions.Caching.SqlServer table.
        createTable.CommandText = """
            IF OBJECT_ID('dbo.TokenCache', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[TokenCache](
                    [Id] [nvarchar](449) COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL,
                    [Value] [varbinary](max) NOT NULL,
                    [ExpiresAtTime] [datetimeoffset](7) NOT NULL,
                    [SlidingExpirationInSeconds] [bigint] NULL,
                    [AbsoluteExpiration] [datetimeoffset](7) NULL,
                    CONSTRAINT [pk_TokenCache_Id] PRIMARY KEY CLUSTERED ([Id] ASC));
                CREATE NONCLUSTERED INDEX [Index_TokenCache_ExpiresAtTime] ON [dbo].[TokenCache]([ExpiresAtTime] ASC);
            END
            """;
        await createTable.ExecuteNonQueryAsync().ConfigureAwait(false);
        return cacheConnectionString;
    }
}
