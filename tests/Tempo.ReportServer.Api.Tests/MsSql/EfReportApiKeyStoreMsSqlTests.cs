using Microsoft.EntityFrameworkCore;
using Tempo.ReportServer.Api.Security;

namespace Tempo.ReportServer.Api.Tests.MsSql;

/// <summary>
/// Contract tests for <see cref="Storage.EfReportApiKeyStore"/> against a real SQL Server database
/// (decision O1 / ADR-0001). Assert both the store behaviour and the persisted rows, including that
/// only the key hash is stored (never the plain text) and that expiration/revocation are enforced.
/// </summary>
[Collection(MsSqlTestCollection.Name)]
public sealed class EfReportApiKeyStoreMsSqlTests : IClassFixture<MsSqlTestDatabase>
{
    private readonly MsSqlTestDatabase _db;

    public EfReportApiKeyStoreMsSqlTests(MsSqlTestDatabase db) => _db = db;

    [Fact]
    public async Task Create_PersistsHashOnly_AndValidateReturnsScopedDescriptor()
    {
        await _db.ResetAsync();
        ReportApiKeyCreationResult created;
        var (context, store) = _db.CreateApiKeyStore();
        await using (context)
        {
            created = await store.CreateAsync("tenant-a", "embedded-app", ReportPermission.Render | ReportPermission.Export);
            created.PlainTextKey.Should().StartWith("tmr_");

            var descriptor = await store.ValidateAsync(created.PlainTextKey);
            descriptor.Should().NotBeNull();
            descriptor!.TenantId.Should().Be("tenant-a");
            descriptor.ApplicationId.Should().Be("embedded-app");
            descriptor.Permissions.Should().Be(ReportPermission.Render | ReportPermission.Export);
            descriptor.RevokedAt.Should().BeNull();
        }

        // Direct DB assertion: exactly one row, and the plain text key is NOT stored anywhere.
        await using var verify = _db.CreateDbContext("tenant-a");
        var row = await verify.ApiKeys.SingleAsync();
        row.KeyId.Should().Be(created.KeyId);
        row.KeyHash.Should().NotBeNullOrEmpty();
        row.KeyHash.Should().NotContain(created.PlainTextKey);
        row.KeyHash.Should().Be(ReportApiKeyMaterial.ComputeHash(created.PlainTextKey));
    }

    [Fact]
    public async Task Validate_RejectsUnknownRevokedAndExpiredKeys()
    {
        await _db.ResetAsync();
        var (context, store) = _db.CreateApiKeyStore();
        await using (context)
        {
            var active = await store.CreateAsync("tenant-a", "app-a", ReportPermission.Render);
            var revoked = await store.CreateAsync("tenant-a", "app-b", ReportPermission.Render);
            var expired = await store.CreateAsync(
                "tenant-a",
                "app-c",
                ReportPermission.Render,
                expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

            await store.RevokeAsync(revoked.KeyId, "tenant-a", "admin");

            (await store.ValidateAsync("tmr_unknown")).Should().BeNull();
            (await store.ValidateAsync("not-a-report-key")).Should().BeNull();
            (await store.ValidateAsync(revoked.PlainTextKey)).Should().BeNull();
            (await store.ValidateAsync(expired.PlainTextKey)).Should().BeNull();
            (await store.ValidateAsync(active.PlainTextKey)).Should().NotBeNull();

            var stored = await store.GetAsync(revoked.KeyId, "tenant-a");
            stored!.RevokedByUserId.Should().Be("admin");
            stored.RevokedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task List_ReturnsTenantKeysNewestFirst_AndIsolatesTenants()
    {
        await _db.ResetAsync();
        var (context, store) = _db.CreateApiKeyStore();
        await using (context)
        {
            await store.CreateAsync("tenant-a", "app-1", ReportPermission.Render);
            await store.CreateAsync("tenant-a", "app-2", ReportPermission.View);
            await store.CreateAsync("tenant-b", "app-3", ReportPermission.All);

            var forTenantA = await store.ListAsync("tenant-a");
            forTenantA.Should().HaveCount(2);
            forTenantA.Should().OnlyContain(descriptor => descriptor.TenantId == "tenant-a");

            var forTenantB = await store.ListAsync("tenant-b");
            forTenantB.Should().ContainSingle(descriptor => descriptor.ApplicationId == "app-3");
        }
    }

    [Fact]
    public async Task Rotate_RevokesOldKeyAndIssuesReplacementAtomically()
    {
        await _db.ResetAsync();
        ReportApiKeyCreationResult original;
        ReportApiKeyCreationResult? rotated;
        var (context, store) = _db.CreateApiKeyStore();
        await using (context)
        {
            original = await store.CreateAsync("tenant-a", "embedded-app", ReportPermission.Render | ReportPermission.Export);

            rotated = await store.RotateAsync(original.KeyId, "tenant-a", "admin");
            rotated.Should().NotBeNull();
            rotated!.KeyId.Should().NotBe(original.KeyId);
            rotated.Descriptor.ApplicationId.Should().Be("embedded-app");
            rotated.Descriptor.Permissions.Should().Be(ReportPermission.Render | ReportPermission.Export);

            // Old key no longer validates; new key does.
            (await store.ValidateAsync(original.PlainTextKey)).Should().BeNull();
            (await store.ValidateAsync(rotated.PlainTextKey)).Should().NotBeNull();

            // Rotating a non-existent key returns null and issues nothing.
            (await store.RotateAsync("rk_missing", "tenant-a", "admin")).Should().BeNull();
        }

        await using var verify = _db.CreateDbContext("tenant-a");
        (await verify.ApiKeys.CountAsync()).Should().Be(2);
        var old = await verify.ApiKeys.SingleAsync(key => key.KeyId == original.KeyId);
        old.RevokedAt.Should().NotBeNull();
        old.RevokedByUserId.Should().Be("admin");
    }
}
