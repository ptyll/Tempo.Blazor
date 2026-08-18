using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

internal record PagePerson(string Name, int Index);

public class TmDataTablePaginationTests : LocalizationTestBase
{
    private static List<PagePerson> MakePeople(int count) =>
        Enumerable.Range(1, count).Select(i => new PagePerson($"Person {i}", i)).ToList();

    [Fact]
    public void DataTable_Pagination_Shows_TmPagination_For_MultiPage()
    {
        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.DefaultPageSize, 10)
            .Add(c => c.ShowPagination, true));

        // TmPagination is rendered when there is more than 1 page
        cut.FindAll(".tm-pagination").Should().NotBeEmpty();
    }

    [Fact]
    public void DataTable_Pagination_ShowsOnlyFirstPageRows()
    {
        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.DefaultPageSize, 10));

        cut.FindAll("tbody tr").Count.Should().Be(10);
    }

    [Fact]
    public void DataTable_Pagination_NextButton_LoadsNextPage()
    {
        var cut = Render<TmDataTable<PagePerson>>(p =>
        {
            p.Add(c => c.Items, MakePeople(30));
            p.Add(c => c.DefaultPageSize, 10);
            p.AddChildContent<TmDataTableColumn<PagePerson>>(cp => cp
                .Add(c => c.Title, "Name")
                .Add(c => c.Field, (Func<PagePerson, object?>)(x => x.Name)));
        });

        cut.Find(".tm-pagination-next").Click();

        // Page 2: Person 11..20
        var rows = cut.FindAll("tbody tr");
        rows.Count.Should().Be(10);
        // First row on page 2 contains "Person 11"
        rows[0].TextContent.Should().Contain("Person 11");
    }

    [Fact]
    public void DataTable_Pagination_PreviousButton_DisabledOnFirstPage()
    {
        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(30))
            .Add(c => c.DefaultPageSize, 10));

        cut.Find(".tm-pagination-prev").GetAttribute("disabled").Should().NotBeNull();
    }

    [Fact]
    public void DataTable_Pagination_No_Pagination_For_Single_Page()
    {
        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(5))
            .Add(c => c.DefaultPageSize, 10));

        // Only 1 page → pagination should not render
        cut.FindAll(".tm-pagination").Should().BeEmpty();
    }

    [Fact]
    public void DataTable_Pagination_ShowPagination_False_HidesPagination()
    {
        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.DefaultPageSize, 10)
            .Add(c => c.ShowPagination, false));

        cut.FindAll(".tm-pagination").Should().BeEmpty();
    }

    [Fact]
    public void DataTable_Pagination_ExposesTestIds_ForControlsAndSummary()
    {
        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.DefaultPageSize, 10));

        cut.Find("[data-testid='pagination-container']").Should().NotBeNull();
        cut.Find("[data-testid='pagination-summary']").TextContent.Should().Contain("50");
        cut.Find("[data-testid='pagination-prev']").Should().NotBeNull();
        cut.Find("[data-testid='pagination-next']").Should().NotBeNull();
        cut.Find("[data-testid='pagination-page-2']").Should().NotBeNull();
    }

    [Fact]
    public void DataTable_TestIdPrefix_IsPropagatedIntoTheBuiltInPagination()
    {
        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.DefaultPageSize, 10)
            .Add(c => c.TestIdPrefix, "people"));

        cut.Find("[data-testid='people-pagination']").Should().NotBeNull();
        cut.Find("[data-testid='people-pagination-next']").Should().NotBeNull();
        cut.Find("[data-testid='people-pagination-page-2']").Should().NotBeNull();
        cut.Find("[data-testid='people-pagination-summary']").Should().NotBeNull();

        // Two prefixed tables on one page must not collide on the bare ids.
        cut.FindAll("[data-testid='pagination-next']").Should().BeEmpty();
    }

    [Fact]
    public void DataTable_PaginationAttributes_AreSplattedOntoThePaginationRoot()
    {
        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.DefaultPageSize, 10)
            .Add(c => c.PaginationAttributes, new Dictionary<string, object>
            {
                ["aria-label"] = "People pages",
            }));

        cut.Find(".tm-pagination").GetAttribute("aria-label").Should().Be("People pages");
    }

    [Fact]
    public void DataTable_ClickingPageTestId_NavigatesToThatPage()
    {
        var cut = Render<TmDataTable<PagePerson>>(p =>
        {
            p.Add(c => c.Items, MakePeople(30));
            p.Add(c => c.DefaultPageSize, 10);
            p.AddChildContent<TmDataTableColumn<PagePerson>>(cp => cp
                .Add(c => c.Title, "Name")
                .Add(c => c.Field, (Func<PagePerson, object?>)(x => x.Name)));
        });

        cut.Find("[data-testid='pagination-page-3']").Click();

        var rows = cut.FindAll("tbody tr");
        rows.Count.Should().Be(10);
        rows[0].TextContent.Should().Contain("Person 21");
        cut.Find("[aria-current='page']").GetAttribute("data-testid").Should().Be("pagination-page-3");
    }

    [Fact]
    public void DataTable_PaginationInfoTemplate_ReplacesTheBuiltInSummary()
    {
        DataTablePaginationInfo? captured = null;
        RenderFragment<DataTablePaginationInfo> template = info => builder =>
        {
            captured = info;
            builder.AddContent(0, $"Page {info.CurrentPage} of {info.TotalPages} ({info.TotalCount} records)");
        };

        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(22))
            .Add(c => c.DefaultPageSize, 10)
            .Add(c => c.PaginationInfoTemplate, template));

        cut.Find("[data-testid='pagination-summary']").TextContent.Trim()
            .Should().Be("Page 1 of 3 (22 records)");

        captured.Should().Be(new DataTablePaginationInfo(
            CurrentPage: 1, TotalPages: 3, PageSize: 10, TotalCount: 22, StartItem: 1, EndItem: 10));
    }

    [Fact]
    public void DataTable_PaginationInfoTemplate_SeesTheItemRangeOfTheLastPage()
    {
        RenderFragment<DataTablePaginationInfo> template = info => builder =>
            builder.AddContent(0, $"{info.StartItem}-{info.EndItem}/{info.TotalCount}");

        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(22))
            .Add(c => c.DefaultPageSize, 10)
            .Add(c => c.PaginationInfoTemplate, template));

        cut.Find("[data-testid='pagination-page-3']").Click();

        cut.Find("[data-testid='pagination-summary']").TextContent.Trim().Should().Be("21-22/22");
    }

    // ── Where the item range is printed ───────────────────────────
    //
    // Both the table's summary and the embedded TmPagination know the range, and both used to render it,
    // so the footer stated the count twice next to itself. Exactly one of them may show it.

    [Fact]
    public void DataTable_PaginationFooter_StatesTheItemRangeExactlyOnce()
    {
        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.DefaultPageSize, 10));

        cut.FindAll(".tm-pagination-container .tm-pagination-info").Should().ContainSingle();
    }

    [Fact]
    public void DataTable_ByDefault_TheRangeIsTheTableSummary_NotThePagerLabel()
    {
        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.DefaultPageSize, 10));

        cut.Find("[data-testid='pagination-summary']").TextContent.Should().Contain("50");
        cut.FindAll("[data-testid='pagination-info']").Should().BeEmpty();
    }

    [Fact]
    public void DataTable_PaginationInfoPlacement_Pagination_MovesTheRangeIntoThePager()
    {
        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.DefaultPageSize, 10)
            .Add(c => c.PaginationInfoPlacement, DataTablePaginationInfoPlacement.Pagination));

        cut.Find("[data-testid='pagination-info']").TextContent.Should().Contain("50");
        cut.FindAll("[data-testid='pagination-summary']").Should().BeEmpty();
        cut.FindAll(".tm-pagination-container .tm-pagination-info").Should().ContainSingle();
    }

    [Fact]
    public void DataTable_PaginationInfoPlacement_None_LeavesOnlyThePageControls()
    {
        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.DefaultPageSize, 10)
            .Add(c => c.PaginationInfoPlacement, DataTablePaginationInfoPlacement.None));

        cut.FindAll(".tm-pagination-container .tm-pagination-info").Should().BeEmpty();
        cut.Find("[data-testid='pagination-next']").Should().NotBeNull();
    }

    [Fact]
    public void DataTable_PaginationInfoPlacement_Pagination_DoesNotRenderTheSummaryTemplate()
    {
        // PaginationInfoTemplate customises the table's own summary; moving the range into the pager
        // means the pager's own localized wording is what shows, and the template is simply not rendered.
        var templateWasRendered = false;
        RenderFragment<DataTablePaginationInfo> template = _ => builder =>
        {
            templateWasRendered = true;
            builder.AddContent(0, "host wording");
        };

        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(22))
            .Add(c => c.DefaultPageSize, 10)
            .Add(c => c.PaginationInfoTemplate, template)
            .Add(c => c.PaginationInfoPlacement, DataTablePaginationInfoPlacement.Pagination));

        templateWasRendered.Should().BeFalse();
        cut.Find("[data-testid='pagination-info']").TextContent.Should().NotContain("host wording");
    }

    // ── Rows the table was handed must be reachable ───────────────
    //
    // The pager is the only element that reaches pages 2..N of a client-side (Items) table.
    // With ShowPagination=false it is not rendered, so a slice would leave the remaining rows
    // in no element at all. Slicing is therefore derived from the pager: no pager, no slice.
    // The opposite arm — pager shown, slice applied — is measured by
    // DataTable_Pagination_ShowsOnlyFirstPageRows above, so over-fixing is caught there.

    [Fact]
    public void DataTable_WithoutPagination_RendersEveryItem_NotJustTheFirstPage()
    {
        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.DefaultPageSize, 10)
            .Add(c => c.ShowPagination, false));

        // Positive control: no element in the DOM could reach rows 11..50.
        cut.FindAll(".tm-pagination").Should().BeEmpty();

        cut.FindAll("tbody tr").Count.Should()
            .Be(50, "no pager is rendered, so every row handed to the table must be in the DOM");
    }

    [Fact]
    public void DataTable_WithoutPagination_RendersEveryItem_EvenWithAControlledPageSize()
    {
        var cut = Render<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.PageSize, 10)
            .Add(c => c.ShowPagination, false));

        cut.FindAll(".tm-pagination").Should().BeEmpty();

        cut.FindAll("tbody tr").Count.Should()
            .Be(50, "a controlled PageSize sizes the pager's page, and there is no pager here");
    }
}
