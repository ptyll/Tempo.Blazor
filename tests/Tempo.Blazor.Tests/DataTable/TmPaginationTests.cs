using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmPaginationTests : LocalizationTestBase
{
    [Fact]
    public void Pagination_ShowsCorrectPageNumbers_SmallCount()
    {
        var cut = RenderComponent<TmPagination>(p => p
            .Add(c => c.CurrentPage, 1)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10));

        // All 5 page buttons should be visible (no ellipsis needed for 5 pages)
        cut.FindAll(".tm-page-btn").Count.Should().Be(5);
    }

    [Fact]
    public void Pagination_PrevDisabledOnFirstPage()
    {
        var cut = RenderComponent<TmPagination>(p => p
            .Add(c => c.CurrentPage, 1)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10));

        cut.Find(".tm-pagination-prev").GetAttribute("disabled").Should().NotBeNull();
    }

    [Fact]
    public void Pagination_NextDisabledOnLastPage()
    {
        var cut = RenderComponent<TmPagination>(p => p
            .Add(c => c.CurrentPage, 5)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10));

        cut.Find(".tm-pagination-next").GetAttribute("disabled").Should().NotBeNull();
    }

    [Fact]
    public void Pagination_EllipsisForLargePageCount()
    {
        var cut = RenderComponent<TmPagination>(p => p
            .Add(c => c.CurrentPage, 5)
            .Add(c => c.TotalPages, 20)
            .Add(c => c.TotalCount, 200)
            .Add(c => c.PageSize, 10));

        // Should render ellipsis elements
        cut.FindAll(".tm-pagination-ellipsis").Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Pagination_PageSizeDropdown_ShowsOptions()
    {
        var cut = RenderComponent<TmPagination>(p => p
            .Add(c => c.CurrentPage, 1)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10)
            .Add(c => c.PageSizeOptions, new[] { 10, 25, 50, 100 }));

        var select = cut.Find(".tm-pagination-page-size");
        select.QuerySelectorAll("option").Length.Should().Be(4);
    }

    [Fact]
    public void Pagination_CurrentPageButton_HasActiveClass()
    {
        var cut = RenderComponent<TmPagination>(p => p
            .Add(c => c.CurrentPage, 3)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10));

        var activeBtns = cut.FindAll(".tm-page-btn.tm-page-btn-active");
        activeBtns.Count.Should().Be(1);
        activeBtns[0].TextContent.Trim().Should().Be("3");
    }

    [Fact]
    public void Pagination_ClickPageButton_FiresOnPageChange()
    {
        int? navigatedPage = null;
        var cut = RenderComponent<TmPagination>(p => p
            .Add(c => c.CurrentPage, 1)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10)
            .Add(c => c.OnPageChange, EventCallback.Factory.Create<int>(this, p => navigatedPage = p)));

        // Click page 3
        var pageBtns = cut.FindAll(".tm-page-btn");
        pageBtns[2].Click(); // 3rd page button (0-indexed)

        navigatedPage.Should().Be(3);
    }

    [Fact]
    public void Pagination_ShowsInfoText()
    {
        var cut = RenderComponent<TmPagination>(p => p
            .Add(c => c.CurrentPage, 2)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10));

        // Info text: "11–20 of 50"
        cut.Find(".tm-pagination-info").TextContent.Should().Contain("11");
        cut.Find(".tm-pagination-info").TextContent.Should().Contain("20");
        cut.Find(".tm-pagination-info").TextContent.Should().Contain("50");
    }
}
