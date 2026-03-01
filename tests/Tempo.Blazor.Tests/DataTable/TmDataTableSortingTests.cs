using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmDataTableSortingTests : LocalizationTestBase
{
    // Names in non-alphabetical order to make sort detectable
    private static List<SortPerson> People =>
    [
        new("Charlie", 30),
        new("Alice",   25),
        new("Bob",     35),
    ];

    private IRenderedComponent<TmDataTable<SortPerson>> RenderWithSortableNameColumn(
        Action<ComponentParameterCollectionBuilder<TmDataTable<SortPerson>>>? extra = null)
    {
        return RenderComponent<TmDataTable<SortPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<SortPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Sortable", true);
                b.AddAttribute(4, "Field", (Func<SortPerson, object?>)(x => x.Name));
                b.CloseComponent();
            });
            extra?.Invoke(p);
        });
    }

    [Fact]
    public void DataTable_ClickSortableHeader_SortsAscending()
    {
        var cut = RenderWithSortableNameColumn();

        cut.Find("th[data-sortable='true']").Click();

        var rows = cut.FindAll("tbody tr");
        rows.Count.Should().Be(3);
        rows[0].QuerySelector("td")!.TextContent.Trim().Should().Be("Alice");
        rows[2].QuerySelector("td")!.TextContent.Trim().Should().Be("Charlie");
    }

    [Fact]
    public void DataTable_ClickSortableHeader_Twice_SortsDescending()
    {
        var cut = RenderWithSortableNameColumn();

        var header = cut.Find("th[data-sortable='true']");
        header.Click(); // ascending
        header.Click(); // descending

        var rows = cut.FindAll("tbody tr");
        rows[0].QuerySelector("td")!.TextContent.Trim().Should().Be("Charlie");
        rows[2].QuerySelector("td")!.TextContent.Trim().Should().Be("Alice");
    }

    [Fact]
    public void DataTable_ClickSortableHeader_Third_ClearsSort()
    {
        var cut = RenderWithSortableNameColumn();

        var header = cut.Find("th[data-sortable='true']");
        header.Click(); // ascending
        header.Click(); // descending
        header.Click(); // clear → original order

        var rows = cut.FindAll("tbody tr");
        rows[0].QuerySelector("td")!.TextContent.Trim().Should().Be("Charlie"); // original first
    }

    [Fact]
    public void DataTable_SortableHeader_HasSortIcon()
    {
        var cut = RenderWithSortableNameColumn();

        cut.FindAll("th[data-sortable='true'] .tm-sort-icon").Should().NotBeEmpty();
    }

    [Fact]
    public void DataTable_AfterSortAscending_HeaderHasAscendingClass()
    {
        var cut = RenderWithSortableNameColumn();

        cut.Find("th[data-sortable='true']").Click();

        cut.FindAll("th.tm-col-sorted-asc").Should().NotBeEmpty();
    }

    [Fact]
    public void DataTable_AfterSortDescending_HeaderHasDescendingClass()
    {
        var cut = RenderWithSortableNameColumn();

        var header = cut.Find("th[data-sortable='true']");
        header.Click();
        header.Click();

        cut.FindAll("th.tm-col-sorted-desc").Should().NotBeEmpty();
    }
}

internal record SortPerson(string Name, int Age);
