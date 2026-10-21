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

public class TmMultiViewListToolbarTests : LocalizationTestBase
{
    private record MvlToolbarItem(
        string Title,
        string? SubTitle = null,
        string? StatusLabel = null,
        string? StatusColor = null,
        DateTimeOffset? Date = null,
        string? AvatarUrl = null) : IMultiViewListItem
    {
        public string Id => Title;
        public IReadOnlyList<ITag>? Tags => null;
    }

    private static IReadOnlyList<MvlToolbarItem> SampleItems(int count = 3) =>
        Enumerable.Range(1, count)
            .Select(i => new MvlToolbarItem($"Item {i}", $"Sub {i}"))
            .ToArray();

    private static IDataTableViewProvider CreateViewProvider() => Substitute.For<IDataTableViewProvider>();

    [Fact]
    public void ItemCount_IsHiddenByDefault()
    {
        var cut = Render<TmMultiViewList<string>>(p => p.Add(c => c.Items, new List<string> { "one", "two" }));

        cut.FindAll(".tm-mvl-item-count").Should().BeEmpty();
    }

    [Fact]
    public void ItemCount_RendersWhenEnabled()
    {
        var cut = Render<TmMultiViewList<string>>(p => p
            .Add(c => c.Items, new List<string> { "one", "two" })
            .Add(c => c.ShowItemCount, true));

        cut.Find(".tm-mvl-item-count").TextContent.Should().Be("Items: 2");
    }

    [Fact]
    public async Task ItemCount_StepsAsideWhileTheFilterResultPreviewShowsTheSameCount()
    {
        var provider = CreateViewProvider();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));

        var cut = Render<TmMultiViewList<MvlToolbarItem>>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.ShowItemCount, true)
            .Add(c => c.ShowFilterResultPreview, true)
            .Add(c => c.ViewContext, "test-context")
            .Add(c => c.ViewProvider, provider)
            .Add(c => c.ExternalFilterDefinitions, [new FilterDefinition { FieldName = "Title", FieldLabel = "Title" }]));

        cut.FindAll(".tm-mvl-item-count").Should().ContainSingle("without active filters there is no preview to duplicate");

        var builder = cut.FindComponent<TmFilterBuilder>();
        await cut.InvokeAsync(() => builder.Instance.OnFiltersChanged.InvokeAsync(
            new List<ActiveFilter> { new("Title", "Title", FilterOperator.Contains, "Item 1", "Item 1") }));

        cut.FindAll(".tm-mvl-item-count").Should().BeEmpty(
            "the result preview already prints the matching count — two identical numbers are noise");
    }

    [Fact]
    public void IconsOnly_DoesNotRenderViewNames()
    {
        var cut = Render<TmMultiViewList<string>>(p => p.Add(c => c.Items, new List<string>()));
        cut.Find(".tm-mvl-switch-table").TextContent.Should().NotContain("Table view");
    }

    [Fact]
    public void IconsAndText_RendersViewNames()
    {
        var cut = Render<TmMultiViewList<string>>(p => p
            .Add(c => c.Items, new List<string>())
            .Add(c => c.ViewSwitcherLabels, ViewSwitcherLabels.IconsAndText));
        cut.Find(".tm-mvl-switch-table").TextContent.Should().Contain("Table view");
        cut.Find(".tm-mvl-switch-card").TextContent.Should().Contain("Card view");
        cut.Find(".tm-mvl-switch-list").TextContent.Should().Contain("List view");
    }

    // Restored from 259f900a (commit ad20da83 rewrote this file and dropped the toolbar chrome tests).

    [Fact]
    public void MultiViewList_ShowToolbar_False_HidesToolbarAndControls()
    {
        var cut = Render<TmMultiViewList<MvlToolbarItem>>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.ShowToolbar, false));

        cut.FindAll(".tm-mvl-toolbar").Should().BeEmpty();
        cut.FindAll(".tm-input-search").Should().BeEmpty();
        cut.FindAll(".tm-mvl-switcher").Should().BeEmpty();
        cut.FindAll(".tm-view-manager").Should().BeEmpty();
    }

    [Fact]
    public void MultiViewList_ShowToolbar_True_WithNoVisibleControls_DoesNotRenderEmptyToolbar()
    {
        var cut = Render<TmMultiViewList<MvlToolbarItem>>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.ShowToolbar, true)
            .Add(c => c.ShowSearch, false)
            .Add(c => c.ShowViewSwitcher, false)
            .Add(c => c.ShowViewManager, false));

        cut.FindAll(".tm-mvl-toolbar").Should().BeEmpty();
    }

    [Fact]
    public void MultiViewList_ShowViewManager_False_HidesViewManager()
    {
        var provider = CreateViewProvider();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));

        var cut = Render<TmMultiViewList<MvlToolbarItem>>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.ViewProvider, provider)
            .Add(c => c.ViewContext, "test-context")
            .Add(c => c.ShowViewManager, false));

        cut.FindAll(".tm-view-manager").Should().BeEmpty();
    }

    [Fact]
    public void MultiViewList_ShowToolbar_True_WithControls_RendersToolbar()
    {
        var cut = Render<TmMultiViewList<MvlToolbarItem>>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.ShowToolbar, true));

        cut.FindAll(".tm-mvl-toolbar").Should().ContainSingle();
        cut.FindAll(".tm-input-search").Should().ContainSingle();
    }

    [Theory]
    [InlineData(DataToolbarMode.Full, true, true)]
    [InlineData(DataToolbarMode.SearchOnly, true, false)]
    [InlineData(DataToolbarMode.ActionsOnly, false, true)]
    [InlineData(DataToolbarMode.ContentOnly, false, false)]
    public void MultiViewList_ToolbarMode_RendersExpectedSearchAndSwitcher(DataToolbarMode mode, bool expectSearch, bool expectSwitcher)
    {
        var cut = Render<TmMultiViewList<MvlToolbarItem>>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.ToolbarMode, mode));

        cut.FindAll(".tm-input-search").Any().Should().Be(expectSearch);
        cut.FindAll(".tm-mvl-switcher").Any().Should().Be(expectSwitcher);
    }

    [Fact]
    public void MultiViewList_ToolbarMode_ActionsOnly_RendersGroupPickerAndViewManagerButNotSearch()
    {
        var provider = CreateViewProvider();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));

        var cut = Render<TmMultiViewList<MvlToolbarItem>>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.ViewContext, "test-context")
            .Add(c => c.ViewProvider, provider)
            .Add(c => c.GroupableFields, [new GroupFieldDefinition<MvlToolbarItem> { FieldName = "Title", Label = "Title", FieldAccessor = i => i.Title }])
            .Add(c => c.ToolbarMode, DataToolbarMode.ActionsOnly));

        cut.FindAll(".tm-input-search").Should().BeEmpty();
        cut.FindAll(".tm-mvl-group-picker").Should().ContainSingle();
        cut.FindAll(".tm-view-manager").Should().ContainSingle();
    }

    [Fact]
    public void MultiViewList_ToolbarMode_ContentOnly_HidesToolbarAndExternalFilterBuilder()
    {
        var provider = CreateViewProvider();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));

        var cut = Render<TmMultiViewList<MvlToolbarItem>>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.ViewContext, "test-context")
            .Add(c => c.ViewProvider, provider)
            .Add(c => c.ExternalFilterDefinitions, [new FilterDefinition { FieldName = "Title", FieldLabel = "Title" }])
            .Add(c => c.ToolbarMode, DataToolbarMode.ContentOnly));

        cut.FindAll(".tm-mvl-toolbar").Should().BeEmpty();
        cut.FindAll(".tm-input-search").Should().BeEmpty();
        cut.FindAll(".tm-view-manager").Should().BeEmpty();
        cut.FindAll(".tm-mvl-external-filters").Should().BeEmpty();
    }

    [Fact]
    public void MultiViewList_ToolbarMode_Full_WithProviderAndExternalFilters_RendersAllChrome()
    {
        var provider = CreateViewProvider();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));

        var cut = Render<TmMultiViewList<MvlToolbarItem>>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.ViewContext, "test-context")
            .Add(c => c.ViewProvider, provider)
            .Add(c => c.ExternalFilterDefinitions, [new FilterDefinition { FieldName = "Title", FieldLabel = "Title" }])
            .Add(c => c.GroupableFields, [new GroupFieldDefinition<MvlToolbarItem> { FieldName = "Title", Label = "Title", FieldAccessor = i => i.Title }]));

        cut.FindAll(".tm-input-search").Should().ContainSingle();
        cut.FindAll(".tm-mvl-switcher").Should().ContainSingle();
        cut.FindAll(".tm-mvl-group-picker").Should().ContainSingle();
        cut.FindAll(".tm-view-manager").Should().ContainSingle();
        cut.FindAll(".tm-mvl-external-filters").Should().ContainSingle();
    }

    /// <summary>Restored from 18cd0ad3. The save-view dialog grouping section was dropped with the toolbar rewrite.</summary>
    [Fact]
    public void ViewDialog_ShowsGroupingSection_WhenGroupableFieldExists()
    {
        var provider = CreateViewProvider();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));

        var cut = Render<TmMultiViewList<MvlToolbarItem>>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.ViewContext, "test-context")
            .Add(c => c.ViewProvider, provider)
            .Add(c => c.GroupableFields, [new GroupFieldDefinition<MvlToolbarItem> { FieldName = "Title", Label = "Title", FieldAccessor = i => i.Title }]));

        cut.Find(".tm-view-manager-toggle").Click();
        cut.Find(".tm-btn-primary").Click();

        cut.FindAll(".tm-view-group-columns").Should().ContainSingle();
        cut.Markup.Should().Contain("Title");
    }
}
