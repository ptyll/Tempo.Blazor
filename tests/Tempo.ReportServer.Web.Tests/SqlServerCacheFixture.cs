using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Tempo.ReportServer.Web.Tests;

/// <summary>
/// The one SQL Server the Web.Tests SQL-cache lane runs against — an xUnit class fixture that
/// mirrors the <c>Tempo.ReportServer.Api.Tests.MsSql</c> pattern (Fáze 19):
/// <see cref="ConnectionEnvironmentVariable"/> wins when it is set (a developer's own SQL Server,
/// used exactly as given); otherwise a Testcontainers
/// <c>mcr.microsoft.com/mssql/server:2022-latest</c> container is started — the same image the
/// application's own E2E suite runs. When Docker is not reachable the fixture throws and the
/// dependent test FAILS with "Docker required" — a missing service is a red, never a silent skip.
/// <para>
/// The fixture owns one database (<see cref="DatabaseName"/>) holding the
/// <c>dbo.TokenCache</c> table in exactly the shape <c>dotnet sql-cache create</c> produces, so a
/// test can point two independent <c>SqlServerCache</c> instances at the same table.
/// </para>
/// </summary>
public sealed class SqlServerCacheFixture : IAsyncLifetime
{
    /// <summary>Environment variable carrying a developer-supplied SQL Server connection string.</summary>
    public const string ConnectionEnvironmentVariable = "REPORTSERVER_TEST_CONNECTION";

    /// <summary>SQL Server image — the same one the app E2E suite runs.</summary>
    public const string ContainerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    /// <summary>Database the fixture creates and prepares the cache table in.</summary>
    public const string DatabaseName = "TempoReportServerWebTests";

    private const string ContainerPassword = "Tempo_ReportServer_Tests!2026";

    private MsSqlContainer? _container;

    /// <summary>Connection string pointing at <see cref="DatabaseName"/> with the cache table ready.</summary>
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

        CacheConnectionString = await PrepareSqlCacheTableAsync(serverConnectionString, DatabaseName)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<string> PrepareSqlCacheTableAsync(string serverConnectionString, string database)
    {
        var cacheConnectionString = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = database }.ConnectionString;

        await using (var server = new SqlConnection(serverConnectionString))
        {
            await server.OpenAsync().ConfigureAwait(false);
            await using var createDb = server.CreateCommand();
            createDb.CommandText = $"IF DB_ID('{database}') IS NULL CREATE DATABASE [{database}];";
            await createDb.ExecuteNonQueryAsync().ConfigureAwait(false);
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
