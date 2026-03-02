using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public record VirtualPerson(string Name, int Index);

public class TmDataTableVirtualizationTests : LocalizationTestBase
{
    private static List<VirtualPerson> MakePeople(int count) =>
        Enumerable.Range(1, count).Select(i => new VirtualPerson($"Person {i}", i)).ToList();

    private IRenderedComponent<TmDataTable<VirtualPerson>> RenderVirtualTable(
        DataTableScrollMode scrollMode = DataTableScrollMode.Virtualized,
        int itemCount = 100,
        float virtualItemSize = 48f,
        int virtualOverscanCount = 3,
        string? virtualScrollHeight = "600px",
        bool showPagination = true)
    {
        return RenderComponent<TmDataTable<VirtualPerson>>(p =>
        {
            p.Add(c => c.Items, MakePeople(itemCount));
            p.Add(c => c.ScrollMode, scrollMode);
            p.Add(c => c.VirtualItemSize, virtualItemSize);
            p.Add(c => c.VirtualOverscanCount, virtualOverscanCount);
            p.Add(c => c.VirtualScrollHeight, virtualScrollHeight);
            p.Add(c => c.ShowPagination, showPagination);
            p.Add(c => c.DefaultPageSize, 25);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<VirtualPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Field", (Func<VirtualPerson, object?>)(x => x.Name));
                b.CloseComponent();

                b.OpenComponent<TmDataTableColumn<VirtualPerson>>(10);
                b.AddAttribute(11, "Title", "Index");
                b.AddAttribute(12, "PropertyName", "Index");
                b.AddAttribute(13, "Field", (Func<VirtualPerson, object?>)(x => x.Index));
                b.AddAttribute(14, "Sortable", true);
                b.CloseComponent();
            });
        });
    }

    // --- ScrollMode parameter ---

    [Fact]
    public void DataTable_ScrollMode_Pagination_ShowsPaginationControls()
    {
        var cut = RenderVirtualTable(scrollMode: DataTableScrollMode.Pagination, itemCount: 50);

        cut.FindAll(".tm-pagination-container").Should().NotBeEmpty();
    }

    [Fact]
    public void DataTable_ScrollMode_Virtualized_HidesPagination()
    {
        var cut = RenderVirtualTable(scrollMode: DataTableScrollMode.Virtualized, itemCount: 50);

        cut.FindAll(".tm-pagination-container").Should().BeEmpty();
    }

    [Fact]
    public void DataTable_ScrollMode_Virtualized_RendersVirtualScrollContainer()
    {
        var cut = RenderVirtualTable();

        cut.FindAll(".tm-data-table-virtual-scroll").Should().HaveCount(1);
    }

    [Fact]
    public void DataTable_ScrollMode_Virtualized_SetsScrollHeight()
    {
        var cut = RenderVirtualTable(virtualScrollHeight: "800px");

        var container = cut.Find(".tm-data-table-virtual-scroll");
        container.GetAttribute("style").Should().Contain("height: 800px");
    }

    [Fact]
    public void DataTable_ScrollMode_Pagination_DoesNotRenderVirtualScroll()
    {
        var cut = RenderVirtualTable(scrollMode: DataTableScrollMode.Pagination);

        cut.FindAll(".tm-data-table-virtual-scroll").Should().BeEmpty();
    }

    // --- Client-side virtualization ---

    [Fact]
    public void DataTable_Virtualized_ClientSide_RendersAllItems()
    {
        // bUnit renders all items from <Virtualize> (no actual viewport)
        var cut = RenderVirtualTable(itemCount: 20);

        // In virtualized mode, all items should be available (no pagination)
        cut.FindAll("tbody tr").Count.Should().Be(20);
    }

    [Fact]
    public void DataTable_Virtualized_ClientSide_NoPaginationApplied()
    {
        // With 100 items and pageSize 25, pagination mode would show 25
        // Virtualized mode should show all items
        var cut = RenderVirtualTable(itemCount: 100);

        cut.FindAll("tbody tr").Count.Should().Be(100);
    }

    [Fact]
    public async Task DataTable_Virtualized_ClientSide_SortingWorks()
    {
        var cut = RenderVirtualTable(itemCount: 5);

        // Click sortable header to sort by Index descending
        await cut.InvokeAsync(() => cut.FindAll("th").First(th => th.TextContent.Contains("Index")).Click());
        await cut.InvokeAsync(() => cut.FindAll("th").First(th => th.TextContent.Contains("Index")).Click());

        var rows = cut.FindAll("tbody tr");
        rows[0].TextContent.Should().Contain("Person 5");
    }

    // --- Server-side virtualization ---

    [Fact]
    public async Task DataTable_Virtualized_ServerSide_CallsProviderOnInit()
    {
        var provider = Substitute.For<IDataTableDataProvider<VirtualPerson>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new PagedResult<VirtualPerson>
                {
                    Items = MakePeople(10),
                    TotalCount = 100,
                    Page = 1,
                    PageSize = 10
                }));

        var cut = RenderComponent<TmDataTable<VirtualPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.ScrollMode, DataTableScrollMode.Virtualized);
            p.Add(c => c.VirtualScrollHeight, "600px");
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<VirtualPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Field", (Func<VirtualPerson, object?>)(x => x.Name));
                b.CloseComponent();
            });
        });

        await cut.InvokeAsync(() => { });

        await provider.Received().GetDataAsync(
            Arg.Any<DataTableQuery>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DataTable_Virtualized_ServerSide_HidesPagination()
    {
        var provider = Substitute.For<IDataTableDataProvider<VirtualPerson>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new PagedResult<VirtualPerson>
                {
                    Items = MakePeople(25),
                    TotalCount = 100,
                    Page = 1,
                    PageSize = 25
                }));

        var cut = RenderComponent<TmDataTable<VirtualPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.ScrollMode, DataTableScrollMode.Virtualized);
            p.Add(c => c.VirtualScrollHeight, "600px");
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<VirtualPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Field", (Func<VirtualPerson, object?>)(x => x.Name));
                b.CloseComponent();
            });
        });

        cut.FindAll(".tm-pagination-container").Should().BeEmpty();
    }

    // --- Default parameter values ---

    [Fact]
    public void DataTable_ScrollMode_DefaultsTo_Pagination()
    {
        var cut = RenderComponent<TmDataTable<VirtualPerson>>(p =>
        {
            p.Add(c => c.Items, MakePeople(5));
        });

        // Default ScrollMode is Pagination - no virtual scroll container
        cut.FindAll(".tm-data-table-virtual-scroll").Should().BeEmpty();
    }
}
