using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public record ServerPerson(string Name, string Dept);

public class TmDataTableServerSideTests : LocalizationTestBase
{
    private static PagedResult<ServerPerson> MakeResult(int page = 1, int pageSize = 25) =>
        new()
        {
            Items = [new("Alice", "Engineering"), new("Bob", "Marketing")],
            TotalCount = 2,
            Page = page,
            PageSize = pageSize,
        };

    private IDataTableDataProvider<ServerPerson> BuildProvider(PagedResult<ServerPerson>? result = null)
    {
        var provider = Substitute.For<IDataTableDataProvider<ServerPerson>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(result ?? MakeResult()));
        return provider;
    }

    [Fact]
    public async Task DataTable_WithDataProvider_CallsGetDataOnMount()
    {
        var provider = BuildProvider();
        var cut = RenderComponent<TmDataTable<ServerPerson>>(p => p
            .Add(c => c.DataProvider, provider));

        await cut.InvokeAsync(() => { });

        await provider.Received(1).GetDataAsync(
            Arg.Is<DataTableQuery>(q => q.Page == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DataTable_WithDataProvider_RendersProvidedItems()
    {
        var provider = BuildProvider();
        var cut = RenderComponent<TmDataTable<ServerPerson>>(p => p
            .Add(c => c.DataProvider, provider));

        await cut.InvokeAsync(() => { });

        cut.FindAll("tbody tr").Count.Should().Be(2);
    }

    [Fact]
    public async Task DataTable_SortChange_CallsProviderWithSortColumn()
    {
        var provider = BuildProvider();
        var cut = RenderComponent<TmDataTable<ServerPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<ServerPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Sortable", true);
                b.AddAttribute(4, "Field", (Func<ServerPerson, object?>)(x => x.Name));
                b.CloseComponent();
            });
        });

        await cut.InvokeAsync(() => { });

        cut.Find("th[data-sortable='true']").Click();
        await cut.InvokeAsync(() => { });

        await provider.Received().GetDataAsync(
            Arg.Is<DataTableQuery>(q => q.SortColumn == "Name" && !q.SortDescending),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DataTable_WithDataProvider_ShowsLoadingSpinnerDuringFetch()
    {
        // Provider that takes "time" (but in test will be immediate)
        var provider = BuildProvider();
        var cut = RenderComponent<TmDataTable<ServerPerson>>(p => p
            .Add(c => c.DataProvider, provider));

        // After loading completes, spinner should be gone
        await cut.InvokeAsync(() => { });
        cut.FindAll(".tm-spinner").Should().BeEmpty();
        cut.FindAll("tbody tr").Count.Should().Be(2);
    }

    [Fact]
    public async Task DataTable_PageChange_WithDataProvider_CallsProviderWithNewPage()
    {
        // 50 total items, page size 10
        var firstPage = new PagedResult<ServerPerson>
        {
            Items = Enumerable.Range(1, 10).Select(i => new ServerPerson($"P{i}", "Dept")).ToList(),
            TotalCount = 50, Page = 1, PageSize = 10
        };
        var secondPage = new PagedResult<ServerPerson>
        {
            Items = Enumerable.Range(11, 10).Select(i => new ServerPerson($"P{i}", "Dept")).ToList(),
            TotalCount = 50, Page = 2, PageSize = 10
        };

        var provider = Substitute.For<IDataTableDataProvider<ServerPerson>>();
        provider.GetDataAsync(Arg.Is<DataTableQuery>(q => q.Page == 1), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(firstPage));
        provider.GetDataAsync(Arg.Is<DataTableQuery>(q => q.Page == 2), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(secondPage));

        var cut = RenderComponent<TmDataTable<ServerPerson>>(p => p
            .Add(c => c.DataProvider, provider)
            .Add(c => c.DefaultPageSize, 10));

        await cut.InvokeAsync(() => { });

        cut.Find(".tm-pagination-next").Click();
        await cut.InvokeAsync(() => { });

        await provider.Received(1).GetDataAsync(
            Arg.Is<DataTableQuery>(q => q.Page == 2),
            Arg.Any<CancellationToken>());
    }
}
