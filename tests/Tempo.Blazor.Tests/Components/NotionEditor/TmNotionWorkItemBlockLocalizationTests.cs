using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.NotionEditor.Blocks.Embed;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Fáze 4k review (plan b75365fb): the refresh button of <c>TmNotionWorkItemBlock</c> rendered
/// the RAW key <c>Notion_WorkItem_Refresh</c> as its text and title because the mock localizer
/// did not carry the key. These tests run against the DEFAULT English mock localizer (no
/// <c>UseCustomLocalization</c> override), so a key missing from
/// <c>LocalizationTestBase.BuildEnglishLocalizer</c> turns them RED — the same way a missing
/// key in the shipped resources would render the raw key to a real user.
/// </summary>
public sealed class TmNotionWorkItemBlockLocalizationTests : LocalizationTestBase
{
    [Fact]
    public void RefreshButton_RendersTheLocalizedLabel_NotTheRawKey()
    {
        var cut = RenderBlock();

        cut.WaitForAssertion(() => cut.Find(".tm-work-item--card").Should().NotBeNull());

        var refresh = cut.Find(".tm-work-item__refresh");
        refresh.GetAttribute("title").Should().Be("Refresh",
            "the title comes from Notion_WorkItem_Refresh — a missing mock key renders the raw key");
        refresh.TextContent.Trim().Should().Be("Refresh",
            "the button text comes from Notion_WorkItem_Refresh — a missing mock key renders the raw key");
        refresh.GetAttribute("title").Should().NotBe("Notion_WorkItem_Refresh");
        refresh.TextContent.Trim().Should().NotBe("Notion_WorkItem_Refresh");
    }

    [Fact]
    public void DisplayModeButtons_RenderTheLocalizedLabels_NotRawKeys()
    {
        var cut = RenderBlock();

        cut.WaitForAssertion(() => cut.Find(".tm-work-item--card").Should().NotBeNull());

        var modes = cut.FindAll(".tm-work-item__mode");
        modes.Should().HaveCount(3, "Card, List and Inline");
        modes[0].TextContent.Trim().Should().Be("Card");
        modes[1].TextContent.Trim().Should().Be("List");
        modes[2].TextContent.Trim().Should().Be("Inline");
    }

    private IRenderedComponent<TmNotionWorkItemBlock> RenderBlock()
    {
        var item = new TmWorkItem
        {
            Id = "DEMO-101",
            SourceKey = "demo",
            ExternalId = "DEMO-101",
            Url = "https://tracker.example/work/DEMO-101",
            Title = "Prepare release checklist",
            StatusLabel = "Done",
            StatusColor = "#22c55e"
        };
        var content = new WorkItemBlockContent
        {
            SourceKey = "demo",
            ExternalId = "DEMO-101",
            DisplayMode = WorkItemDisplayMode.Card,
            CachedSnapshot = item
        };
        var providers = new TmWorkItemProviderRegistry(
            [new StaticWorkItemProvider("demo", "Demo tracker", [item])]);

        return Render<TmNotionWorkItemBlock>(p => p
            .Add(x => x.Content, content)
            .Add(x => x.WorkItemProviders, providers)
            .Add(x => x.ReadOnly, false));
    }

    private sealed class StaticWorkItemProvider : TmWorkItemProviderBase
    {
        private readonly IReadOnlyList<TmWorkItem> _items;

        public StaticWorkItemProvider(string sourceKey, string displayName, IReadOnlyList<TmWorkItem> items)
        {
            SourceKey = sourceKey;
            DisplayName = displayName;
            _items = items;
        }

        public override string SourceKey { get; }
        public override string DisplayName { get; }

        public override Task<TmWorkItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.FirstOrDefault(item =>
                string.Equals(item.ExternalId ?? item.Id, id, StringComparison.OrdinalIgnoreCase)));

        public override Task<Tempo.Blazor.Models.PagedResult<TmWorkItem>> SearchAsync(
            TmWorkItemQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new Tempo.Blazor.Models.PagedResult<TmWorkItem>
            {
                Items = _items,
                TotalCount = _items.Count,
                Page = 1,
                PageSize = _items.Count
            });
    }
}
