using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.NotionEditor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public sealed class TmNotionPageHistoryDiffTests : LocalizationTestBase
{
    [Fact]
    public void DiffViewer_SanitizesHistoricalBlockHtml()
    {
        var block = CapturingHistoryProvider.Block(
            Guid.Parse("cf230000-0000-0000-0000-000000000003"),
            0,
            BlockType.Paragraph,
            "<strong>Kept</strong><img src=x onerror=\"window.__xss=true\"><script>bad()</script>");
        var diffs = new[]
        {
            new BlockDiff(block.Id.ToString("D"), BlockDiffType.Added, null, block)
        };

        var cut = Render<TmNotionDiffViewer>(parameters => parameters
            .Add(component => component.Diffs, diffs));

        cut.Markup.Should().Contain("<strong>Kept</strong>");
        cut.Markup.Should().NotContain("<img");
        cut.Markup.Should().NotContain("<script");
        cut.Markup.Should().NotContain("onerror");
    }

    [Fact]
    public async Task HistoryPanel_ComparesVersionsAndTogglesDiffModes()
    {
        var provider = new CapturingHistoryProvider();
        var context = new NotionEditorContext
        {
            HistoryProvider = provider
        };

        var host = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(component => component.Value, context)
            .AddChildContent<TmNotionPageHistory>(child => child
                .Add(component => component.Visible, true)
                .Add(component => component.PageId, CapturingHistoryProvider.PageId.ToString("D"))));

        host.WaitForAssertion(() => host.FindAll(".tm-nph__version-item").Should().HaveCount(2));

        await host.FindAll(".tm-nph__version-item")[0].ClickAsync(new MouseEventArgs());
        await host.Find(".tm-nph__toolbar-btn--secondary").ClickAsync(new MouseEventArgs());
        await host.FindAll(".tm-nph__version-item")[1].ClickAsync(new MouseEventArgs());

        host.WaitForAssertion(() =>
        {
            provider.LastDiffPageId.Should().Be(CapturingHistoryProvider.PageId.ToString("D"));
            host.Find("[data-testid='notion-diff-viewer']").ClassList.Should().Contain("tm-ndv--inline");
            host.FindAll(".tm-ndv__entry--modified").Should().ContainSingle();
            host.FindAll(".tm-ndv__entry--moved").Should().ContainSingle();
        });

        await host.Find("[data-testid='notion-diff-mode-side-by-side']").ClickAsync(new MouseEventArgs());

        host.WaitForAssertion(() =>
        {
            host.Find("[data-testid='notion-diff-viewer']").ClassList.Should().Contain("tm-ndv--sidebyside");
            host.FindAll(".tm-ndv__pane--before").Should().NotBeEmpty();
            host.FindAll(".tm-ndv__pane--after").Should().NotBeEmpty();
        });
    }

    [Fact]
    public void HistoryPanel_ContentIsNamedRegion_NotMainLandmark()
    {
        // N190 — the host layout owns the single <main> landmark; the history content area is a
        // labelled <section> region instead (036ce5fd/a59cac9a rule, generalised).
        var context = new NotionEditorContext
        {
            HistoryProvider = new CapturingHistoryProvider()
        };

        var host = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(component => component.Value, context)
            .AddChildContent<TmNotionPageHistory>(child => child
                .Add(component => component.Visible, true)
                .Add(component => component.PageId, CapturingHistoryProvider.PageId.ToString("D"))));

        host.WaitForAssertion(() => host.FindAll(".tm-nph__version-item").Should().HaveCount(2));

        host.FindAll("main").Should().BeEmpty("the host layout owns the single <main> landmark");
        host.Find("section.tm-nph__content")
            .GetAttribute("aria-label").Should().Be("Version content");
    }

    private sealed class CapturingHistoryProvider : INotionVersionProvider
    {
        public static readonly Guid PageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private readonly List<NotionPageVersionDto> _versions;

        public CapturingHistoryProvider()
        {
            var modifiedId = Guid.Parse("cf230000-0000-0000-0000-000000000001");
            var movedId = Guid.Parse("cf230000-0000-0000-0000-000000000002");
            _versions =
            [
                Version("cf230000-0000-0000-0000-000000000101", "Current", 0,
                [
                    Block(movedId, 0, BlockType.Heading2, "Moved heading"),
                    Block(modifiedId, 1, BlockType.Paragraph, "Updated paragraph")
                ]),
                Version("cf230000-0000-0000-0000-000000000100", "Previous", 1,
                [
                    Block(modifiedId, 0, BlockType.Paragraph, "Original paragraph"),
                    Block(movedId, 1, BlockType.Heading2, "Moved heading")
                ])
            ];
        }

        public string? LastDiffPageId { get; private set; }

        public Task<PagedResult<IPageVersion>> GetVersionsAsync(string pageId, int page, int pageSize)
            => Task.FromResult(new PagedResult<IPageVersion>
            {
                Items = _versions.Cast<IPageVersion>().ToList(),
                TotalCount = _versions.Count,
                Page = page,
                PageSize = pageSize
            });

        public Task<IPageVersion> GetVersionAsync(string pageId, string versionId)
            => Task.FromResult<IPageVersion>(_versions.Single(version => version.Id.ToString("D") == versionId));

        public Task RestoreVersionAsync(string pageId, string versionId)
            => Task.CompletedTask;

        public Task<IReadOnlyList<BlockDiff>> GetDiffAsync(string pageId, string versionIdA, string versionIdB)
        {
            LastDiffPageId = pageId;
            var before = _versions.Single(version => version.Id.ToString("D") == versionIdA);
            var after = _versions.Single(version => version.Id.ToString("D") == versionIdB);
            return Task.FromResult(NotionBlockDiffService.Compare(before.BlocksSnapshot, after.BlocksSnapshot));
        }

        public Task<IEnumerable<BlockDiff>> CompareVersionsAsync(string versionId1, string versionId2)
            => GetDiffAsync(PageId.ToString("D"), versionId1, versionId2)
                .ContinueWith(task => task.Result.AsEnumerable());

        private static NotionPageVersionDto Version(string id, string description, int hoursAgo, List<PageBlock> blocks) => new()
        {
            Id = Guid.Parse(id),
            PageId = PageId,
            EditedAt = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc).AddHours(-hoursAgo),
            EditedByUserId = "tester",
            EditedByDisplayName = "Test User",
            ChangeDescription = description,
            BlocksSnapshot = blocks
        };

        internal static PageBlock Block(Guid id, int order, BlockType type, string html) => new()
        {
            Id = id,
            PageId = PageId,
            Type = type,
            Order = order,
            Content = type == BlockType.Heading2
                ? new HeadingBlockContent { Level = 2, Html = html }
                : new TextBlockContent { Html = html },
            CreatedAt = new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc),
            LastEditedAt = new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc).AddMinutes(order)
        };
    }
}
