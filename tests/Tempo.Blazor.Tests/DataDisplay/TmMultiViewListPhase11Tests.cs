using Bunit;
using FluentAssertions;
using NSubstitute;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Components.Filters;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>
/// Phase 11 (performance, ToolbarMode, a11y): group body ids are unique per instance, the item
/// count follows ToolbarMode and reads the provider total, and the avatar column leads.
/// </summary>
public class TmMultiViewListPhase11Tests : LocalizationTestBase
{
    public record Item(string Title, string? Avatar = null) : IMultiViewListItem
    {
        public string Id => Title;
        public string? SubTitle => null;
        public string? StatusLabel => null;
        public string? StatusColor => null;
        public DateTimeOffset? Date => null;
        public string? AvatarUrl => Avatar;
        public IReadOnlyList<ITag>? Tags => null;
    }

    private static List<Item> Items => [new("Ada Lovelace"), new("Alan Turing"), new("Grace Hopper")];

    [Fact]
    public void GroupBodyIds_AreUniquePerInstance_EvenForTheSameKey()
    {
        var groups = new[] { new GroupFieldDefinition<Item> { FieldName = "Title", Label = "Title", FieldAccessor = i => i.Title } };

        IRenderedComponent<TmMultiViewList<Item>> RenderOne() => Render<TmMultiViewList<Item>>(p => p
            .Add(c => c.Items, Items)
            .Add(c => c.ViewMode, ListViewMode.Card)
            .Add(c => c.GroupableFields, groups));

        var first = RenderOne();
        var second = RenderOne();

        OpenGroupPicker(first);
        OpenGroupPicker(second);

        var firstIds = first.FindAll(".tm-mvl-group-body").Select(b => b.Id).ToList();
        var secondIds = second.FindAll(".tm-mvl-group-body").Select(b => b.Id).ToList();

        firstIds.Should().NotBeEmpty();
        firstIds.Should().OnlyHaveUniqueItems();
        firstIds.Intersect(secondIds).Should().BeEmpty(
            "two lists on one page must not share aria-controls targets");
        // 12 hex chars is what the old SHA-256 prefix produced. An instance id carries the picker's
        // own guid, so it is longer than that and cannot collide with another list's.
        firstIds.Should().OnlyContain(id => id.Length > "tm-mvl-group-body-".Length + 12);
    }

    [Theory]
    [MemberData(nameof(ItemCountModes))]
    public void ItemCount_FollowsToolbarMode(DataToolbarMode mode, bool visible)
    {
        var cut = Render<TmMultiViewList<Item>>(p => p
            .Add(c => c.Items, Items)
            .Add(c => c.ShowItemCount, true)
            .Add(c => c.ToolbarMode, mode));

        cut.FindAll(".tm-mvl-item-count").Should().HaveCount(visible ? 1 : 0);
    }

    public static IEnumerable<object[]> ItemCountModes() =>
    [
        [DataToolbarMode.Full, true],
        [DataToolbarMode.ActionsOnly, true],
        [DataToolbarMode.SearchOnly, true],
        [DataToolbarMode.ContentOnly, false],
    ];

    [Fact]
    public void SelectAll_PartialSelectedIds_IsMixed_AndClearsWhenSearchLeavesOne()
    {
        var cut = Render<TmMultiViewList<Item>>(p => p
            .Add(c => c.Items, Items)
            .Add(c => c.AllowSelection, true)
            .Add(c => c.SelectedIds, ["Ada Lovelace"]));

        // A host-supplied partial selection never went through a toggle, so the mixed state has
        // to be computed from the visible rows, not from a count left at zero.
        LastIndeterminate().Should().BeTrue();

        cut.Find("input[type=search]").Input("Ada");

        LastIndeterminate().Should().BeFalse(
            "the only visible row is the selected one, so the checkbox is no longer mixed");
    }

    private bool LastIndeterminate() =>
        JSInterop.Invocations
            .Where(call => call.Identifier == "tmDataTable.setIndeterminate")
            .Select(call => (bool)call.Arguments[1]!)
            .Last();

    [Fact]
    public void ItemCount_IsAnnounced_AndReadsTheProviderTotal()
    {
        var provider = Substitute.For<IDataTableDataProvider<Item>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Item> { Items = Items.Take(1).ToList(), TotalCount = 42, Page = 1, PageSize = 1 });

        var cut = Render<TmMultiViewList<Item>>(p => p
            .Add(c => c.DataProvider, provider)
            .Add(c => c.ShowItemCount, true));

        var count = cut.Find(".tm-mvl-item-count");
        count.GetAttribute("aria-live").Should().Be("polite");
        count.GetAttribute("role").Should().Be("status");
        count.TextContent.Should().Be("Items: 42", "a provider's page is a slice; the count is the total");
    }

    [Fact]
    public void AvatarColumn_LeadsTheRow_AndRendersInitialsWithoutAUrl()
    {
        var cut = Render<TmMultiViewList<Item>>(p => p
            .Add(c => c.Items, Items)
            .Add(c => c.AvatarFallback, AvatarFallback.Initials));

        var headers = cut.FindAll("thead th").Select(th => th.TextContent.Trim()).ToList();
        headers.Should().StartWith(["Avatar"], "the avatar is the first data column, ahead of the title");

        cut.FindAll(".tm-mvl-avatar-initials").Select(s => s.TextContent)
            .Should().Equal("AL", "AT", "GH");
    }

    [Fact]
    public void AvatarColumn_WithNoUrlAndNoFallback_KeepsAnEmptyCell()
    {
        var cut = Render<TmMultiViewList<Item>>(p => p
            .Add(c => c.Items, Items)
            .Add(c => c.AvatarFallback, AvatarFallback.None));

        // None keeps the column off, which is the pre-redesign default: the cell is not rendered at
        // all, so there is nothing to align and nothing to show.
        cut.FindAll(".tm-mvl-avatar-initials").Should().BeEmpty();
        cut.Markup.Should().NotContain("tm-mvl-list-avatar");
    }

    [Fact]
    public void ContentOnly_WithItemCount_RendersNoToolbar()
    {
        var cut = Render<TmMultiViewList<Item>>(p => p
            .Add(c => c.Items, Items)
            .Add(c => c.ShowItemCount, true)
            .Add(c => c.ToolbarMode, DataToolbarMode.ContentOnly));

        cut.FindAll(".tm-mvl-toolbar").Should().BeEmpty(
            "ContentOnly hands the toolbar to the page, even when the count is asked for");
    }

    [Fact]
    public void ActionsOnly_RendersNeitherSearchNorSummary()
    {
        var cut = Render<TmMultiViewList<Item>>(p => p
            .Add(c => c.Items, Items)
            .Add(c => c.ToolbarMode, DataToolbarMode.ActionsOnly));

        cut.FindAll(".tm-input-search").Should().BeEmpty();
        cut.FindAll(".tm-filter-summary, .tm-data-table-filter-summary").Should().BeEmpty();
    }

    [Fact]
    public async Task ClearAll_IsHiddenInContentOnlyMode()
    {
        var definitions = new[]
        {
            new FilterDefinition { FieldName = "Title", FieldLabel = "Title", FieldType = FilterFieldType.Text },
        };

        var views = Substitute.For<IDataTableViewProvider>();
        var shown = Render<TmMultiViewList<Item>>(p => p
            .Add(c => c.Items, Items)
            .Add(c => c.ViewProvider, views)
            .Add(c => c.ExternalFilterDefinitions, definitions.ToList()));
        var filterBuilder = shown.FindComponent<TmFilterBuilder>();
        await shown.InvokeAsync(() => filterBuilder.Instance.OnFiltersChanged.InvokeAsync(
            [new ActiveFilter("Title", "Title", FilterOperator.Contains, "Ada", "Ada")]));
        shown.FindAll(".tm-filter-clear-all").Should().ContainSingle("the button exists once a filter is active");

        var cut = Render<TmMultiViewList<Item>>(p => p
            .Add(c => c.Items, Items)
            .Add(c => c.ViewProvider, views)
            .Add(c => c.ToolbarMode, DataToolbarMode.ContentOnly)
            .Add(c => c.ExternalFilterDefinitions, definitions.ToList()));

        cut.FindComponents<TmFilterBuilder>().Should().BeEmpty(
            "ContentOnly hands the filter chrome, clear-all included, to the page");
    }

    private static void OpenGroupPicker(IRenderedComponent<TmMultiViewList<Item>> cut)
    {
        cut.Find(".tm-mvl-group-btn").Click();
        cut.Find(".tm-mvl-group-option input").Change(true);
    }
}
