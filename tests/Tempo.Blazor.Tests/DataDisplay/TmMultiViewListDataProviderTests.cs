using Bunit;
using FluentAssertions;
using NSubstitute;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

public record MvlServerItem(string Id, string Name, string Category);

public class TmMultiViewListDataProviderTests : LocalizationTestBase
{
    private static List<MvlServerItem> MakeItems(int count) =>
        Enumerable.Range(1, count).Select(i => new MvlServerItem(i.ToString(), $"Item {i}", $"Cat {i % 3}")).ToList();

    private IDataTableDataProvider<MvlServerItem> CreateMockProvider(
        List<MvlServerItem>? items = null,
        int totalCount = 0)
    {
        var data = items ?? MakeItems(5);
        var provider = Substitute.For<IDataTableDataProvider<MvlServerItem>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new PagedResult<MvlServerItem>
                {
                    Items = data,
                    TotalCount = totalCount > 0 ? totalCount : data.Count,
                    Page = 1,
                    PageSize = 25
                }));
        return provider;
    }

    [Fact]
    public async Task MultiViewList_DataProvider_CallsGetDataOnMount()
    {
        var provider = CreateMockProvider();

        var cut = RenderComponent<TmMultiViewList<MvlServerItem>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.TitleField, x => x.Name);
        });

        await cut.InvokeAsync(() => { });

        await provider.Received().GetDataAsync(
            Arg.Any<DataTableQuery>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MultiViewList_DataProvider_RendersServerItems()
    {
        var provider = CreateMockProvider(MakeItems(3));

        var cut = RenderComponent<TmMultiViewList<MvlServerItem>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.TitleField, x => x.Name);
        });

        await cut.InvokeAsync(() => { });

        cut.FindAll(".tm-mvl-row").Should().HaveCount(3);
    }

    [Fact]
    public async Task MultiViewList_DataProvider_RendersInCardView()
    {
        var provider = CreateMockProvider(MakeItems(3));

        var cut = RenderComponent<TmMultiViewList<MvlServerItem>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.TitleField, x => x.Name);
            p.Add(c => c.ViewMode, ListViewMode.Card);
        });

        await cut.InvokeAsync(() => { });

        cut.FindAll(".tm-mvl-card").Should().HaveCount(3);
    }

    [Fact]
    public async Task MultiViewList_DataProvider_RendersInListView()
    {
        var provider = CreateMockProvider(MakeItems(3));

        var cut = RenderComponent<TmMultiViewList<MvlServerItem>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.TitleField, x => x.Name);
            p.Add(c => c.ViewMode, ListViewMode.List);
        });

        await cut.InvokeAsync(() => { });

        cut.FindAll(".tm-mvl-list-item").Should().HaveCount(3);
    }

    [Fact]
    public async Task MultiViewList_DataProvider_ShowsPaginationForMultiplePages()
    {
        var provider = Substitute.For<IDataTableDataProvider<MvlServerItem>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new PagedResult<MvlServerItem>
                {
                    Items = MakeItems(25),
                    TotalCount = 100,
                    Page = 1,
                    PageSize = 25
                }));

        var cut = RenderComponent<TmMultiViewList<MvlServerItem>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.TitleField, x => x.Name);
            p.Add(c => c.ShowPagination, true);
        });

        await cut.InvokeAsync(() => { });

        cut.FindAll(".tm-pagination-container").Should().NotBeEmpty();
    }

    [Fact]
    public void MultiViewList_ClientSide_NoPagination_WhenSinglePage()
    {
        var cut = RenderComponent<TmMultiViewList<MvlServerItem>>(p =>
        {
            p.Add(c => c.Items, MakeItems(3));
            p.Add(c => c.TitleField, x => x.Name);
            p.Add(c => c.ShowPagination, true);
        });

        // 3 items with default page size of 25 = single page, no pagination
        cut.FindAll(".tm-pagination-container").Should().BeEmpty();
    }
}
