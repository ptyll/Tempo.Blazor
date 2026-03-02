using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

public record MvlVirtualItem(string Id, string Name, string Category, int Index);

public class TmMultiViewListVirtualizationTests : LocalizationTestBase
{
    private static List<MvlVirtualItem> MakeItems(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new MvlVirtualItem(i.ToString(), $"Item {i}", i % 3 == 0 ? "A" : "B", i))
            .ToList();

    private IRenderedComponent<TmMultiViewList<MvlVirtualItem>> RenderVirtualMvl(
        ListViewMode viewMode = ListViewMode.Table,
        DataTableScrollMode scrollMode = DataTableScrollMode.Virtualized,
        int itemCount = 50,
        string? virtualScrollHeight = "600px")
    {
        return RenderComponent<TmMultiViewList<MvlVirtualItem>>(p =>
        {
            p.Add(c => c.Items, MakeItems(itemCount));
            p.Add(c => c.ViewMode, viewMode);
            p.Add(c => c.ScrollMode, scrollMode);
            p.Add(c => c.VirtualScrollHeight, virtualScrollHeight);
            p.Add(c => c.TitleField, x => x.Name);
            p.Add(c => c.SubTitleField, x => x.Category);
            p.Add(c => c.ShowPagination, true);
            p.Add(c => c.DefaultPageSize, 25);
        });
    }

    // --- ScrollMode parameter ---

    [Fact]
    public void MultiViewList_Virtualized_Table_HidesPagination()
    {
        var cut = RenderVirtualMvl(viewMode: ListViewMode.Table, scrollMode: DataTableScrollMode.Virtualized, itemCount: 50);

        cut.FindAll(".tm-pagination-container").Should().BeEmpty();
    }

    [Fact]
    public void MultiViewList_Pagination_Mode_DoesNotHaveVirtualScroll()
    {
        var cut = RenderVirtualMvl(viewMode: ListViewMode.Table, scrollMode: DataTableScrollMode.Pagination, itemCount: 50);

        cut.FindAll(".tm-mvl-virtual-scroll").Should().BeEmpty();
    }

    // --- Table view virtualization ---

    [Fact]
    public void MultiViewList_Virtualized_Table_RendersVirtualScrollContainer()
    {
        var cut = RenderVirtualMvl(viewMode: ListViewMode.Table);

        cut.FindAll(".tm-mvl-virtual-scroll").Should().HaveCount(1);
    }

    [Fact]
    public void MultiViewList_Virtualized_Table_SetsScrollHeight()
    {
        var cut = RenderVirtualMvl(viewMode: ListViewMode.Table, virtualScrollHeight: "800px");

        var container = cut.Find(".tm-mvl-virtual-scroll");
        container.GetAttribute("style").Should().Contain("height: 800px");
    }

    [Fact]
    public void MultiViewList_Virtualized_Table_RendersAllItems()
    {
        // bUnit renders all items from <Virtualize> (no actual viewport)
        var cut = RenderVirtualMvl(viewMode: ListViewMode.Table, itemCount: 20);

        cut.FindAll(".tm-mvl-row").Count.Should().Be(20);
    }

    // --- Card view virtualization ---

    [Fact]
    public void MultiViewList_Virtualized_Card_RendersVirtualScrollContainer()
    {
        var cut = RenderVirtualMvl(viewMode: ListViewMode.Card);

        cut.FindAll(".tm-mvl-virtual-scroll").Should().HaveCount(1);
    }

    [Fact]
    public void MultiViewList_Virtualized_Card_RendersAllCards()
    {
        var cut = RenderVirtualMvl(viewMode: ListViewMode.Card, itemCount: 15);

        cut.FindAll(".tm-mvl-card").Count.Should().Be(15);
    }

    // --- List view virtualization ---

    [Fact]
    public void MultiViewList_Virtualized_List_RendersVirtualScrollContainer()
    {
        var cut = RenderVirtualMvl(viewMode: ListViewMode.List);

        cut.FindAll(".tm-mvl-virtual-scroll").Should().HaveCount(1);
    }

    [Fact]
    public void MultiViewList_Virtualized_List_RendersAllItems()
    {
        var cut = RenderVirtualMvl(viewMode: ListViewMode.List, itemCount: 15);

        cut.FindAll(".tm-mvl-list-item").Count.Should().Be(15);
    }

    // --- Pagination mode: no virtual scroll ---

    [Fact]
    public void MultiViewList_Pagination_Table_NoVirtualScroll()
    {
        var cut = RenderVirtualMvl(viewMode: ListViewMode.Table, scrollMode: DataTableScrollMode.Pagination);

        cut.FindAll(".tm-mvl-virtual-scroll").Should().BeEmpty();
    }

    // --- Default ScrollMode is Pagination ---

    [Fact]
    public void MultiViewList_ScrollMode_DefaultsTo_Pagination()
    {
        var cut = RenderComponent<TmMultiViewList<MvlVirtualItem>>(p =>
        {
            p.Add(c => c.Items, MakeItems(5));
            p.Add(c => c.TitleField, x => x.Name);
        });

        // Default ScrollMode is Pagination - no virtual scroll container
        cut.FindAll(".tm-mvl-virtual-scroll").Should().BeEmpty();
    }

    // --- Virtualized + Grouping ---

    [Fact]
    public void MultiViewList_Virtualized_WithGrouping_RendersGroupRows()
    {
        var cut = RenderComponent<TmMultiViewList<MvlVirtualItem>>(p =>
        {
            p.Add(c => c.Items, MakeItems(10));
            p.Add(c => c.ViewMode, ListViewMode.Table);
            p.Add(c => c.ScrollMode, DataTableScrollMode.Virtualized);
            p.Add(c => c.VirtualScrollHeight, "600px");
            p.Add(c => c.TitleField, x => x.Name);
            p.Add(c => c.GroupableFields, new List<GroupFieldDefinition<MvlVirtualItem>>
            {
                new()
                {
                    FieldName = "Category",
                    Label = "Category",
                    FieldAccessor = x => x.Category
                }
            });
            p.Add(c => c.GroupsCollapsedByDefault, false);
        });

        // Enable grouping
        cut.Find(".tm-mvl-group-btn").Click();
        cut.FindAll(".tm-mvl-group-dropdown input[type='checkbox']").First().Change(true);

        // Should have group rows and data rows
        cut.FindAll(".tm-mvl-group-row").Count.Should().BeGreaterThan(0);
        cut.FindAll(".tm-mvl-row").Count.Should().BeGreaterThan(0);

        // Should still have virtual scroll container
        cut.FindAll(".tm-mvl-virtual-scroll").Should().HaveCount(1);
    }
}
