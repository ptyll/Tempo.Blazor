using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.SqlServer;
using Microsoft.Extensions.Options;
using Tempo.ReportServer.Web.Services;

namespace Tempo.ReportServer.Web.Tests;

/// <summary>
/// Round-trip specification for <see cref="DistributedCacheReportServerTokenStore"/>, the scale-out
/// (shared cache) backing of the server-side token store.
/// </summary>
public sealed class DistributedCacheReportServerTokenStoreTests
{
    private static IDistributedCache NewCache()
        => new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    [Fact]
    public void SetThenGet_ReturnsStoredTokens()
    {
        var store = new DistributedCacheReportServerTokenStore(NewCache());
        var tokens = new ReportServerTokenSet("access-1", "refresh-1", DateTimeOffset.UtcNow.AddMinutes(5));

        store.Set("subject-1", tokens);
        var round = store.Get("subject-1");

        round.Should().NotBeNull();
        round!.AccessToken.Should().Be("access-1");
        round.RefreshToken.Should().Be("refresh-1");
        round.ExpiresUtc.Should().BeCloseTo(tokens.ExpiresUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Get_UnknownSubject_ReturnsNull()
    {
        var store = new DistributedCacheReportServerTokenStore(NewCache());

        store.Get("missing").Should().BeNull();
    }

    [Fact]
    public void Remove_DeletesTokens()
    {
        var cache = NewCache();
        var store = new DistributedCacheReportServerTokenStore(cache);
        store.Set("subject-1", new ReportServerTokenSet("a", "r", DateTimeOffset.UtcNow.AddMinutes(5)));

        store.Remove("subject-1");

        store.Get("subject-1").Should().BeNull();
    }

    [Fact]
    public void TwoStoresOverSharedCache_SeeSameTokens()
    {
        // Simulates two host instances sharing one distributed cache.
        var cache = NewCache();
        var instanceA = new DistributedCacheReportServerTokenStore(cache);
        var instanceB = new DistributedCacheReportServerTokenStore(cache);

        instanceA.Set("subject-1", new ReportServerTokenSet("access-shared", "refresh-shared", DateTimeOffset.UtcNow.AddMinutes(5)));

        instanceB.Get("subject-1")!.AccessToken.Should().Be("access-shared");
    }

    [Fact]
    public void TwoStoresOverSharedCache_RemoveOnA_IsSeenByB()
    {
        // A sign-out on one instance must be visible to every other instance sharing the cache.
        var cache = NewCache();
        var instanceA = new DistributedCacheReportServerTokenStore(cache);
        var instanceB = new DistributedCacheReportServerTokenStore(cache);
        instanceA.Set("subject-1", new ReportServerTokenSet("access-shared", "refresh-shared", DateTimeOffset.UtcNow.AddMinutes(5)));
        instanceB.Get("subject-1").Should().NotBeNull();

        instanceA.Remove("subject-1");

        instanceB.Get("subject-1").Should().BeNull("a remove on one instance is seen by the others");
    }

}

/// <summary>
/// The SQL-Server-backed leg of the <see cref="DistributedCacheReportServerTokenStore"/> contract —
/// split into its own class so the <see cref="SqlServerCacheFixture"/> container cost (and the
/// "Docker required" failure mode) lands only on the test that needs a real server, while the
/// in-memory tests above stay free of both.
/// </summary>
public sealed class DistributedCacheReportServerTokenStoreSqlTests : IClassFixture<SqlServerCacheFixture>
{
    private readonly SqlServerCacheFixture _fixture;

    /// <summary>Initializes a new instance over the shared SQL Server cache fixture.</summary>
    public DistributedCacheReportServerTokenStoreSqlTests(SqlServerCacheFixture fixture)
        => _fixture = fixture;

    /// <summary>
    /// Cross-instance sharing through a real SQL-Server-backed <see cref="IDistributedCache"/>
    /// (<c>AddDistributedSqlServerCache</c>), the production scale-out backing: a token saved
    /// through one <see cref="SqlServerCache"/> instance is read back — and its removal seen —
    /// through a second, independent instance over the same table. Fails loudly ("Docker
    /// required") when no SQL Server is reachable — a missing service is a red, never a skip.
    /// </summary>
    [Fact]
    public void TwoStoresOverSqlServerCache_ShareTokens_AcrossInstances()
    {
        static IDistributedCache NewSqlCache(string connectionString)
            => new SqlServerCache(Options.Create(new SqlServerCacheOptions
            {
                ConnectionString = connectionString,
                SchemaName = "dbo",
                TableName = "TokenCache",
            }));

        var instanceA = new DistributedCacheReportServerTokenStore(NewSqlCache(_fixture.CacheConnectionString));
        var instanceB = new DistributedCacheReportServerTokenStore(NewSqlCache(_fixture.CacheConnectionString));
        var subject = $"subject-{Guid.NewGuid():N}";

        instanceA.Set(subject, new ReportServerTokenSet("sql-access", "sql-refresh", DateTimeOffset.UtcNow.AddMinutes(5)));

        var seenByB = instanceB.Get(subject);
        seenByB.Should().NotBeNull("a second instance shares the SQL Server cache table");
        seenByB!.AccessToken.Should().Be("sql-access");
        seenByB.RefreshToken.Should().Be("sql-refresh");

        instanceA.Remove(subject);
        instanceB.Get(subject).Should().BeNull("a remove on instance A is seen by instance B through SQL Server");
    }
}
