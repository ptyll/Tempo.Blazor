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
        var cut = Render<TmPagination>(p => p
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
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 1)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10));

        cut.Find(".tm-pagination-prev").GetAttribute("disabled").Should().NotBeNull();
    }

    [Fact]
    public void Pagination_NextDisabledOnLastPage()
    {
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 5)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10));

        cut.Find(".tm-pagination-next").GetAttribute("disabled").Should().NotBeNull();
    }

    [Fact]
    public void Pagination_EllipsisForLargePageCount()
    {
        var cut = Render<TmPagination>(p => p
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
        var cut = Render<TmPagination>(p => p
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
        var cut = Render<TmPagination>(p => p
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
        var cut = Render<TmPagination>(p => p
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
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 2)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10));

        // Info text: "11–20 of 50"
        cut.Find(".tm-pagination-info").TextContent.Should().Contain("11");
        cut.Find(".tm-pagination-info").TextContent.Should().Contain("20");
        cut.Find(".tm-pagination-info").TextContent.Should().Contain("50");
    }

    [Fact]
    public void Pagination_ShowInfo_False_DropsTheRangeButKeepsTheControls()
    {
        // For a host that already states the range next to the pager (TmDataTable's summary does),
        // so the count is not printed twice side by side.
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 2)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10)
            .Add(c => c.ShowInfo, false));

        cut.FindAll(".tm-pagination-info").Should().BeEmpty();
        cut.FindAll("[data-testid='pagination-info']").Should().BeEmpty();
        cut.Find("[data-testid='pagination-next']").Should().NotBeNull();
        cut.FindAll(".tm-page-btn").Count.Should().Be(5);
    }

    [Fact]
    public void Pagination_ShowInfo_DefaultsToTrue_SoAStandalonePagerKeepsItsRange()
    {
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 2)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10));

        cut.FindAll("[data-testid='pagination-info']").Should().ContainSingle();
    }

    [Fact]
    public void Pagination_EmitsDefaultTestIds_ForEveryControl()
    {
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 2)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10)
            .Add(c => c.PageSizeOptions, new[] { 10, 25 }));

        cut.Find("[data-testid='pagination']").ClassList.Should().Contain("tm-pagination");
        cut.Find("[data-testid='pagination-info']").TextContent.Should().Contain("11");
        cut.Find("[data-testid='pagination-prev']").ClassList.Should().Contain("tm-pagination-prev");
        cut.Find("[data-testid='pagination-next']").ClassList.Should().Contain("tm-pagination-next");
        cut.Find("[data-testid='pagination-page-3']").TextContent.Trim().Should().Be("3");
        cut.Find("[data-testid='pagination-page-size']").TagName.Should().Be("SELECT");
    }

    [Fact]
    public void Pagination_TestIdPrefix_NamespacesEveryTestId()
    {
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 2)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10)
            .Add(c => c.PageSizeOptions, new[] { 10, 25 })
            .Add(c => c.TestIdPrefix, "users"));

        cut.Find("[data-testid='users-pagination']").Should().NotBeNull();
        cut.Find("[data-testid='users-pagination-info']").Should().NotBeNull();
        cut.Find("[data-testid='users-pagination-prev']").Should().NotBeNull();
        cut.Find("[data-testid='users-pagination-next']").Should().NotBeNull();
        cut.Find("[data-testid='users-pagination-page-3']").Should().NotBeNull();
        cut.Find("[data-testid='users-pagination-page-size']").Should().NotBeNull();

        // The unprefixed ids must be gone, otherwise two prefixed instances would still collide.
        cut.FindAll("[data-testid='pagination-next']").Should().BeEmpty();
    }

    [Fact]
    public void Pagination_DataTestId_OverridesRootTestId_ButNotTheParts()
    {
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 1)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10)
            .Add(c => c.DataTestId, "pager-of-invoices"));

        cut.Find("[data-testid='pager-of-invoices']").ClassList.Should().Contain("tm-pagination");
        cut.Find("[data-testid='pagination-next']").Should().NotBeNull();
    }

    [Fact]
    public void Pagination_ActivePageButton_IsMarkedAriaCurrent()
    {
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 3)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10));

        var current = cut.FindAll("[aria-current='page']");
        current.Count.Should().Be(1);
        current[0].GetAttribute("data-testid").Should().Be("pagination-page-3");

        cut.Find("[data-testid='pagination-page-2']").HasAttribute("aria-current").Should().BeFalse();
    }

    [Fact]
    public void Pagination_Disabled_RendersDisabledClassOnRoot()
    {
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 1)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10)
            .Add(c => c.Disabled, true));

        cut.Find(".tm-pagination").ClassList.Should().Contain(
            "tm-pagination-disabled",
            "Disabled musí třídu na kořen opravdu vydat — pět běhů strážce ji nevidělo, protože "
            + "žádná trasa zakázaný pager nevykreslila");
    }

    [Fact]
    public void Pagination_Enabled_DoesNotRenderDisabledClass()
    {
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 1)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10));

        cut.Find(".tm-pagination").ClassList.Should().NotContain("tm-pagination-disabled");
    }

    [Fact]
    public void Pagination_Disabled_DoesNotNavigateWhenAPageButtonIsClicked()
    {
        int? navigatedPage = null;
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 1)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10)
            .Add(c => c.Disabled, true)
            .Add(c => c.OnPageChange, EventCallback.Factory.Create<int>(this, page => navigatedPage = page)));

        cut.FindAll(".tm-page-btn")[2].Click();

        navigatedPage.Should().BeNull("Disabled není jen CSS — klik na stránku se musí spolknout");
    }

    [Fact]
    public void Pagination_Disabled_DisablesInteractiveChildren()
    {
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 2)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10)
            .Add(c => c.PageSizeOptions, new[] { 10, 25 })
            .Add(c => c.Disabled, true));

        cut.Find(".tm-pagination-prev").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-pagination-next").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-pagination-page-size").HasAttribute("disabled").Should().BeTrue();
        foreach (var button in cut.FindAll(".tm-page-btn"))
        {
            button.HasAttribute("disabled").Should().BeTrue("každé číslo stránky je ovládací prvek");
        }
    }

    [Fact]
    public void Pagination_HostSplattedAttributes_StillReachTheRoot()
    {
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 1)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.PageSize, 10)
            .AddUnmatched("data-testid", "host-owned-pager")
            .AddUnmatched("aria-label", "Invoice pages"));

        var root = cut.Find(".tm-pagination");
        root.GetAttribute("data-testid").Should().Be("host-owned-pager");
        root.GetAttribute("aria-label").Should().Be("Invoice pages");
    }
}
