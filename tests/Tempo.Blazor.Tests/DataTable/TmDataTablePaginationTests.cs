using Bunit;
using FluentAssertions;
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
        var cut = RenderComponent<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.DefaultPageSize, 10)
            .Add(c => c.ShowPagination, true));

        // TmPagination is rendered when there is more than 1 page
        cut.FindAll(".tm-pagination").Should().NotBeEmpty();
    }

    [Fact]
    public void DataTable_Pagination_ShowsOnlyFirstPageRows()
    {
        var cut = RenderComponent<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.DefaultPageSize, 10));

        cut.FindAll("tbody tr").Count.Should().Be(10);
    }

    [Fact]
    public void DataTable_Pagination_NextButton_LoadsNextPage()
    {
        var cut = RenderComponent<TmDataTable<PagePerson>>(p =>
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
        var cut = RenderComponent<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(30))
            .Add(c => c.DefaultPageSize, 10));

        cut.Find(".tm-pagination-prev").GetAttribute("disabled").Should().NotBeNull();
    }

    [Fact]
    public void DataTable_Pagination_No_Pagination_For_Single_Page()
    {
        var cut = RenderComponent<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(5))
            .Add(c => c.DefaultPageSize, 10));

        // Only 1 page → pagination should not render
        cut.FindAll(".tm-pagination").Should().BeEmpty();
    }

    [Fact]
    public void DataTable_Pagination_ShowPagination_False_HidesPagination()
    {
        var cut = RenderComponent<TmDataTable<PagePerson>>(p => p
            .Add(c => c.Items, MakePeople(50))
            .Add(c => c.DefaultPageSize, 10)
            .Add(c => c.ShowPagination, false));

        cut.FindAll(".tm-pagination").Should().BeEmpty();
    }
}
