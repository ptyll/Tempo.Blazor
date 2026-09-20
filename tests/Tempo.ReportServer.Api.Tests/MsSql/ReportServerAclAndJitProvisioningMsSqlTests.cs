using Tempo.ReportServer.Api.Security;
using Tempo.ReportServer.Api.Storage;
using Tempo.Reporting.Abstractions;
using Tempo.Reporting.Abstractions.Dtos;

namespace Tempo.ReportServer.Api.Tests.MsSql;

/// <summary>
/// Real SQL Server specification for the Fáze 4 persistence: just-in-time user provisioning
/// (<see cref="EfReportServerUserProvisioner"/>) and per-folder ACL grants resolved with folder
/// inheritance (<see cref="EfReportFolderPermissionStore"/> + <see cref="ReportPermissionResolver"/>).
/// </summary>
[Collection(MsSqlTestCollection.Name)]
public sealed class ReportServerAclAndJitProvisioningMsSqlTests : IAsyncLifetime, IClassFixture<MsSqlTestDatabase>
{
    private const string Tenant = "tenant-acl";
    private readonly MsSqlTestDatabase _database;

    public ReportServerAclAndJitProvisioningMsSqlTests(MsSqlTestDatabase database)
        => _database = database;

    public Task InitializeAsync() => _database.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task JitProvisioning_InsertsOnFirstSight_ThenRefreshesOnReturn()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-17T08:00:00Z"));

        await using (var context = _database.CreateDbContext(Tenant))
        {
            var provisioner = new EfReportServerUserProvisioner(context, clock);
            var first = await provisioner.UpsertAsync("sub-1", Tenant, "a@x.io", "Author One");
            first.FirstSeenAt.Should().Be(clock.GetUtcNow());
            first.LastSeenAt.Should().Be(clock.GetUtcNow());
        }

        clock.Advance(TimeSpan.FromHours(3));

        await using (var context = _database.CreateDbContext(Tenant))
        {
            var provisioner = new EfReportServerUserProvisioner(context, clock);
            var second = await provisioner.UpsertAsync("sub-1", Tenant, "author.one@x.io", "Author One");
            second.FirstSeenAt.Should().Be(DateTimeOffset.Parse("2026-07-17T08:00:00Z"));
            second.LastSeenAt.Should().Be(clock.GetUtcNow());
            second.Email.Should().Be("author.one@x.io");
        }

        await using (var verify = _database.CreateDbContext(Tenant))
        {
            var users = verify.Users.ToList();
            users.Should().ContainSingle(user => user.Subject == "sub-1");
        }
    }

    [Fact]
    public async Task FolderGrant_IsInheritedByChildFolders_AndScopedToTheSubject()
    {
        string parentId;
        string childId;
        await using (var context = _database.CreateDbContext(Tenant))
        {
            var store = new EfReportServerStore(context);
            var parent = await store.CreateFolderAsync(new CreateReportFolderRequestDto { TenantId = Tenant, Name = "Finance" });
            var child = await store.CreateFolderAsync(new CreateReportFolderRequestDto { TenantId = Tenant, Name = "Q3", ParentFolderId = parent.FolderId });
            parentId = parent.FolderId;
            childId = child.FolderId;
        }

        var execution = new ReportExecutionContext(Tenant, "grantor", "en-US");
        await using (var context = _database.CreateDbContext(Tenant))
        {
            var permissions = new EfReportFolderPermissionStore(context);
            await permissions.GrantAsync(parentId, "sub-viewer", ReportServerRole.Viewer, execution);
        }

        await using (var context = _database.CreateDbContext(Tenant))
        {
            var permissions = new EfReportFolderPermissionStore(context);
            var resolver = new ReportPermissionResolver(permissions);

            // The grantee inherits the parent grant on the child folder: render is allowed, edit is not.
            var viewer = ReportSecurityContext.ForUser(Tenant, "sub-viewer", []);
            var canRender = await resolver.AuthorizeAsync(
                viewer,
                new ReportPermissionRequirement(ReportPermission.Render, ReportResourceKind.Render),
                childId);
            var canEdit = await resolver.AuthorizeAsync(
                viewer,
                new ReportPermissionRequirement(ReportPermission.EditDefinition, ReportResourceKind.ReportDefinition),
                childId);

            // A different subject with no grant and no role has nothing on the child folder.
            var stranger = ReportSecurityContext.ForUser(Tenant, "sub-stranger", []);
            var strangerRender = await resolver.AuthorizeAsync(
                stranger,
                new ReportPermissionRequirement(ReportPermission.Render, ReportResourceKind.Render),
                childId);

            canRender.Allowed.Should().BeTrue();
            canEdit.Allowed.Should().BeFalse();
            strangerRender.Allowed.Should().BeFalse();
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public MutableTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
