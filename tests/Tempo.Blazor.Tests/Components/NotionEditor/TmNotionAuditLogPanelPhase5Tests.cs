using Bunit.Rendering;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 5 (P6): audit log layout + details — a real table on a wide container (<c>th</c> column
/// headers), cards on a narrow one, the per-row expansion revealing ALL metadata with translated
/// known keys, and the compact filters sheet with the active-filter count.
/// </summary>
public sealed class TmNotionAuditLogPanelPhase5Tests : LocalizationTestBase
{
    public TmNotionAuditLogPanelPhase5Tests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Close"] = "Close",
            ["Tm_Loading"] = "Loading",
            ["Tm_Retry"] = "Retry",
            ["Notion_Audit_Title"] = "Audit log",
            ["Notion_Audit_Action_Create"] = "Created",
            ["Notion_Audit_Action_Edit"] = "Edited",
            ["Notion_Audit_Action_Delete"] = "Deleted",
            ["Notion_Audit_Action_Move"] = "Moved",
            ["Notion_Audit_Action_Restrict"] = "Restricted",
            ["Notion_Audit_Export"] = "Export CSV",
            ["Notion_Audit_Empty"] = "No audit entries match the current filters.",
            ["Notion_Audit_Filter"] = "Filter",
            ["Notion_Audit_User"] = "User",
            ["Notion_Audit_Action"] = "Action",
            ["Notion_Audit_Action_All"] = "All actions",
            ["Notion_Audit_From"] = "From",
            ["Notion_Audit_To"] = "To",
            ["Notion_Audit_Clear"] = "Clear",
            ["Notion_Audit_DownloadReady"] = "CSV ready",
            ["Notion_Audit_LoadError"] = "Audit log could not be loaded.",
            ["Notion_Audit_Timestamp"] = "Timestamp",
            ["Notion_Audit_Target"] = "Target",
            ["Notion_Audit_Details"] = "Details",
            ["Notion_Audit_Count"] = "{0} entries",
            ["Notion_Audit_Page"] = "Audit log page",
            ["Notion_Audit_PageStatus"] = "Page {0} of {1}",
            ["Notion_Audit_Previous"] = "Previous page",
            ["Notion_Audit_Next"] = "Next page",
            ["Notion_Audit_ColAction"] = "Action",
            ["Notion_Audit_ColTimestamp"] = "Date and time",
            ["Notion_Audit_ColUser"] = "User",
            ["Notion_Audit_ColEntityType"] = "Entity type",
            ["Notion_Audit_ColEntityId"] = "Entity ID",
            ["Notion_Audit_ColDetails"] = "Details",
            ["Notion_Audit_DetailsFor"] = "Details for {0} {1}",
            ["Notion_Audit_Expand"] = "Show details",
            ["Notion_Audit_Collapse"] = "Hide details",
            ["Notion_Audit_FiltersSheet"] = "Filters",
            ["Notion_Audit_FiltersOpen"] = "Filters ({0})",
            ["Notion_Audit_More"] = "More actions",
            ["Notion_Audit_Meta_Title"] = "Title",
            ["Notion_Audit_Meta_Parent"] = "Parent",
            ["Notion_Audit_Meta_Changes"] = "Changes",
            ["Notion_Audit_Meta_Reason"] = "Reason",
            ["Notion_Audit_Meta_User"] = "With user",
            ["Notion_Audit_Meta_Permission"] = "Permission",
            ["Notion_Audit_Entity_Page"] = "Page",
            ["Notion_Audit_Entity_Database"] = "Database"
        });
    }

    [Fact]
    public void LayoutTable_RendersColumnHeaders()
    {
        var cut = RenderPanel(new FakeActivityProvider(SampleEntries()), layout: TmNotionAuditLogLayout.Table);

        cut.WaitForAssertion(() =>
        {
            var headers = cut.FindAll(".tm-notion-audit__table th")
                .Select(th => th.TextContent.Trim())
                .ToArray();
            headers.Should().Equal("Action", "Date and time", "User", "Entity type", "Entity ID", "Details");
        });
    }

    [Fact]
    public async Task RowExpansion_RevealsAllMetadataWithTranslatedKeys()
    {
        var entry = Entry(
            "audit-100",
            new DateTimeOffset(2026, 1, 14, 8, 0, 0, TimeSpan.Zero),
            "alice", "Alice Morgan", "edit",
            new Dictionary<string, object>
            {
                ["title"] = "Strategy",
                ["changes"] = 3,
                ["reason"] = "Cleanup"
            });
        var cut = RenderPanel(new FakeActivityProvider([entry]));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='notion-audit-entry']").Should().ContainSingle());

        var toggle = cut.Find(".tm-notion-audit__row-toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        toggle.GetAttribute("aria-label").Should().StartWith("Details for ");
        await toggle.ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            var expanded = cut.Find(".tm-notion-audit__row-details");
            expanded.TextContent.Should().Contain("Title");
            expanded.TextContent.Should().Contain("Strategy");
            expanded.TextContent.Should().Contain("Changes");
            expanded.TextContent.Should().Contain("3");
            expanded.TextContent.Should().Contain("Reason");
            expanded.TextContent.Should().Contain("Cleanup");
            cut.Find(".tm-notion-audit__row-toggle").GetAttribute("aria-expanded").Should().Be("true");
        });
    }

    [Fact]
    public async Task KnownEntityType_IsTranslated()
    {
        var entry = Entry(
            "audit-101",
            new DateTimeOffset(2026, 1, 14, 8, 0, 0, TimeSpan.Zero),
            "alice", "Alice Morgan", "create",
            new Dictionary<string, object> { ["title"] = "Tasks" });
        var cut = RenderPanel(new FakeActivityProvider([entry]));

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-notion-audit__entity-type").TextContent.Trim().Should().Be("Page");
            cut.Find(".tm-notion-audit__table .tm-notion-audit__entity-type").TextContent.Trim().Should().Be("Page");
        });

        await Task.CompletedTask;
    }

    [Fact]
    public async Task CompactFilters_SheetFiltersEntries()
    {
        var cut = RenderPanel(new FakeActivityProvider(SampleEntries()));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='notion-audit-entry']").Should().HaveCount(2));

        await cut.Find("[data-testid='notion-audit-filters-open']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
            cut.Find(".tm-modal-overlay [data-testid='notion-audit-user-filter-sheet']").Should().NotBeNull());

        cut.Find("[data-testid='notion-audit-user-filter-sheet']").Input("Bob");
        await cut.Find("[data-testid='notion-audit-apply-sheet']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='notion-audit-entry']").Should().ContainSingle();
            cut.Find("[data-testid='notion-audit-entry']").TextContent.Should().Contain("Bob Stone");
        });
    }

    private IRenderedComponent<ContainerFragment> RenderPanel(
        ITmActivityProvider provider,
        int pageSize = 10,
        TmNotionAuditLogLayout layout = TmNotionAuditLogLayout.Auto)
        => Render(builder =>
        {
            builder.OpenComponent<TmNotionAuditLogPanel>(0);
            builder.AddAttribute(1, nameof(TmNotionAuditLogPanel.ActivityProvider), provider);
            builder.AddAttribute(2, nameof(TmNotionAuditLogPanel.PageSize), pageSize);
            builder.AddAttribute(3, nameof(TmNotionAuditLogPanel.Layout), layout);
            builder.CloseComponent();
        });

    private static IReadOnlyList<TmActivityEntry> SampleEntries()
        =>
        [
            Entry("audit-201", new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero), "bob", "Bob Stone", "create", new Dictionary<string, object> { ["title"] = "Created page" }),
            Entry("audit-202", new DateTimeOffset(2026, 1, 12, 8, 0, 0, TimeSpan.Zero), "alice", "Alice Morgan", "edit", new Dictionary<string, object> { ["title"] = "Edited page" })
        ];

    private static TmActivityEntry Entry(
        string id, DateTimeOffset timestamp, string userId, string user, string action,
        Dictionary<string, object> metadata)
        => new()
        {
            Id = id,
            Timestamp = timestamp,
            Actor = new TmUserRef { Id = userId, DisplayName = user },
            Action = action,
            EntityRef = TmEntityRef.Create("page", "11111111-1111-1111-1111-111111111111"),
            Metadata = metadata
        };

    private sealed class FakeActivityProvider(IReadOnlyList<TmActivityEntry> entries) : ITmActivityProvider
    {
        public TmActivityProviderCapabilities Capabilities
            => TmActivityProviderCapabilities.Read
            | TmActivityProviderCapabilities.Query
            | TmActivityProviderCapabilities.Append;

        TmActivityProviderCapabilities ITmCapabilityProvider<TmActivityProviderCapabilities>.Capabilities => Capabilities;

        public Task<IReadOnlyList<TmActivityEntry>> GetForEntityAsync(TmEntityRef entityRef, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TmActivityEntry>>(entries.Where(entry => entry.EntityRef.Equals(entityRef)).ToArray());

        public Task<TmActivityEntry> AppendAsync(TmActivityEntry entry, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PagedResult<TmActivityEntry>> QueryAsync(TmActivityQuery query, CancellationToken cancellationToken = default)
        {
            var matches = entries
                .Where(entry => string.IsNullOrWhiteSpace(query.SearchText)
                    || entry.Actor?.Id.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase) == true
                    || entry.Actor?.DisplayName.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase) == true)
                .Where(entry => string.IsNullOrWhiteSpace(query.Action) || string.Equals(entry.Action, query.Action, StringComparison.OrdinalIgnoreCase))
                .Where(entry => query.From is null || entry.Timestamp >= query.From.Value)
                .Where(entry => query.To is null || entry.Timestamp <= query.To.Value)
                .OrderByDescending(entry => entry.Timestamp)
                .ToArray();

            var take = Math.Clamp(query.Take, 1, 100);
            return Task.FromResult(new PagedResult<TmActivityEntry>
            {
                Items = matches.Skip(Math.Max(0, query.Skip)).Take(take).ToArray(),
                TotalCount = matches.Length,
                Page = Math.Max(0, query.Skip) / take + 1,
                PageSize = take
            });
        }
    }
}
