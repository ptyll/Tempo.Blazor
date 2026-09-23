using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Tempo.ReportServer.Api.Tests.MsSql;

/// <summary>
/// N213 — the external-mode isolation contract of <see cref="MsSqlTestDatabase"/>. Deliberately
/// NOT part of <see cref="MsSqlTestCollection"/>: the "external" server is simulated by this
/// class's own Testcontainers SQL Server, and the <c>REPORTSERVER_TEST_CONNECTION</c> resolution
/// output (a <c>ResolvedServer</c> over a verbatim connection string) is injected through
/// <see cref="MsSqlTestDatabase.ServerOverride"/> — the shared
/// <see cref="MsSqlContainerFixture.Server"/> static is never touched, so this class stays
/// parallel-safe against the catalog collection. No real developer SQL Server is ever involved.
/// <para>
/// Red evidence for the pre-fix behaviour: under the old code the first test's
/// <c>sys.databases</c> probe WOULD have contained <c>SomeUnrelatedName</c> — the fixture used the
/// external string verbatim, so its Respawner targeted whatever database the developer named.
/// </para>
/// </summary>
public class MsSqlExternalModeIsolationTests : IAsyncLifetime
{
    private MsSqlContainer? _container;
    private string _adminConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder(MsSqlTestDatabase.ContainerImage)
            .Build();
        await _container.StartAsync();
        _adminConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            TrustServerCertificate = true,
        }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExternalMode_CreatesItsOwnDatabase_IgnoringInitialCatalog()
    {
        string external = new SqlConnectionStringBuilder(_adminConnectionString)
        {
            InitialCatalog = "SomeUnrelatedName",
        }.ConnectionString;

        var database = new MsSqlTestDatabase
        {
            ServerOverride = new MsSqlContainerFixture.ResolvedServer(external),
        };

        await database.InitializeAsync();
        try
        {
            string ownedCatalog = new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog;
            Assert.StartsWith("tempo_test_", ownedCatalog);

            var names = await QueryDatabaseNamesAsync();
            Assert.DoesNotContain(names, name => name == "SomeUnrelatedName");
            Assert.Contains(names, name => name == ownedCatalog);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExternalMode_DisposeAsync_DropsTheOwnedDatabase()
    {
        var database = new MsSqlTestDatabase
        {
            ServerOverride = new MsSqlContainerFixture.ResolvedServer(_adminConnectionString),
        };

        await database.InitializeAsync();
        string ownedCatalog = new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog;
        Assert.StartsWith("tempo_test_", ownedCatalog);

        await database.DisposeAsync();

        var names = await QueryDatabaseNamesAsync();
        Assert.DoesNotContain(names, name => name == ownedCatalog);
    }

    [Fact]
    public async Task ExternalMode_ConnectionWithoutCreateDatabasePermission_Throws()
    {
        const string deniedLogin = "tempo_denied_login";
        const string deniedPassword = "Denied_Passw0rd!";
        await ExecuteOnAdminAsync($"""
            CREATE LOGIN [{deniedLogin}] WITH PASSWORD = '{deniedPassword}';
            CREATE USER [{deniedLogin}] FOR LOGIN [{deniedLogin}];
            DENY CREATE ANY DATABASE TO [{deniedLogin}];
            """);

        string denied = new SqlConnectionStringBuilder(_adminConnectionString)
        {
            UserID = deniedLogin,
            Password = deniedPassword,
            IntegratedSecurity = false,
        }.ConnectionString;

        var database = new MsSqlTestDatabase
        {
            ServerOverride = new MsSqlContainerFixture.ResolvedServer(denied),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.InitializeAsync());
        Assert.Contains("CREATE DATABASE", exception.Message, StringComparison.OrdinalIgnoreCase);

        // The denied login must not have left a half-created tempo_test_* behind either.
        var names = await QueryDatabaseNamesAsync();
        Assert.DoesNotContain(names, name => name.StartsWith("tempo_test_", StringComparison.Ordinal));

        await database.DisposeAsync();
    }

    [Fact]
    public void ResolvedServerRedactsConnectionString()
    {
        // N175: the synthesized record ToString() would print ServerConnectionString verbatim —
        // including Password= — into any future assert or log line. ToString prints nothing of
        // the string at all: no value, no key names.
        var server = new MsSqlContainerFixture.ResolvedServer(
            "Server=localhost,1433;User Id=sa;Password=Tempo_ReportServer_Tests!2026;TrustServerCertificate=True");

        string printed = server.ToString();
        Assert.Equal("ResolvedServer { redacted }", printed);
        Assert.DoesNotContain("Tempo_ReportServer_Tests!2026", printed);
        Assert.DoesNotContain("Password", printed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost", printed);
    }

    [Fact]
    public async Task InitializeAsync_DropsOwnedDatabase_WhenMigrationFails()
    {
        // N179: when CREATE DATABASE succeeds but a later init step throws, xUnit never calls
        // DisposeAsync on the failed fixture — without the catch-path drop the owned database
        // would linger on the server until it dies. The hook stands in for the EF migration
        // blowing up (it runs at exactly that point in InitializeAsync).
        // The hook captures the generated name while it is still set — a successful cleanup
        // nulls the field (no double-drop), so post-failure reads would see null.
        string? ownedCatalog = null;
        var database = new MsSqlTestDatabase
        {
            ServerOverride = new MsSqlContainerFixture.ResolvedServer(_adminConnectionString),
        };
        database.AfterDatabaseCreatedHook = () =>
        {
            ownedCatalog = database.OwnedDatabaseForTest;
            return Task.FromException(new InvalidOperationException("simulated migration failure"));
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => database.InitializeAsync());
        Assert.Equal("simulated migration failure", exception.Message);

        Assert.False(string.IsNullOrEmpty(ownedCatalog));
        Assert.StartsWith("tempo_test_", ownedCatalog);

        var names = await QueryDatabaseNamesAsync();
        Assert.DoesNotContain(names, name => name == ownedCatalog);
    }

    private async Task<IReadOnlyList<string>> QueryDatabaseNamesAsync()
    {
        var names = new List<string>();
        var master = new SqlConnectionStringBuilder(_adminConnectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sys.databases";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private async Task ExecuteOnAdminAsync(string commandText)
    {
        var master = new SqlConnectionStringBuilder(_adminConnectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }
}
