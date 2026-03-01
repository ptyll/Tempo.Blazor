using FluentAssertions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.DataTable;

internal record Product(string Name, string Category, decimal Price, int Stock);

public class InMemoryDataProviderTests
{
    private static IReadOnlyList<Product> Products =>
    [
        new("Apple",  "Fruit",    1.5m,  100),
        new("Banana", "Fruit",    0.8m,  200),
        new("Carrot", "Vegetable",0.5m,  300),
        new("Daikon", "Vegetable",1.2m,  50),
        new("Egg",    "Dairy",    3.0m,  80),
        new("Feta",   "Dairy",    5.0m,  60),
        new("Grape",  "Fruit",    2.5m,  120),
        new("Honey",  "Other",    8.0m,  30),
        new("Iceberg","Vegetable",1.1m,  150),
        new("Jam",    "Other",    3.5m,  40),
    ];

    [Fact]
    public async Task InMemory_Pagination_ReturnsCorrectPage()
    {
        var provider = new InMemoryDataProvider<Product>(Products);

        var result = await provider.GetDataAsync(new DataTableQuery { Page = 2, PageSize = 3 });

        result.Page.Should().Be(2);
        result.PageSize.Should().Be(3);
        result.Items.Count.Should().Be(3);
        result.Items[0].Name.Should().Be("Daikon");
        result.Items[1].Name.Should().Be("Egg");
        result.Items[2].Name.Should().Be("Feta");
    }

    [Fact]
    public async Task InMemory_Pagination_TotalCountIsCorrect()
    {
        var provider = new InMemoryDataProvider<Product>(Products);

        var result = await provider.GetDataAsync(new DataTableQuery { Page = 1, PageSize = 3 });

        result.TotalCount.Should().Be(10);
        result.TotalPages.Should().Be(4);
    }

    [Fact]
    public async Task InMemory_Pagination_LastPageHasRemainingItems()
    {
        var provider = new InMemoryDataProvider<Product>(Products);

        var result = await provider.GetDataAsync(new DataTableQuery { Page = 4, PageSize = 3 });

        result.Items.Count.Should().Be(1); // 10 items, page 4 of size 3 → 1 item
        result.Items[0].Name.Should().Be("Jam");
    }

    [Fact]
    public async Task InMemory_Sort_Ascending_SortsCorrectly()
    {
        var provider = new InMemoryDataProvider<Product>(Products,
            new Dictionary<string, Func<Product, object?>> { ["Name"] = p => p.Name });

        var result = await provider.GetDataAsync(new DataTableQuery
        {
            Page = 1, PageSize = 10,
            SortColumn = "Name", SortDescending = false
        });

        result.Items[0].Name.Should().Be("Apple");
        result.Items[9].Name.Should().Be("Jam");
    }

    [Fact]
    public async Task InMemory_Sort_Descending_SortsCorrectly()
    {
        var provider = new InMemoryDataProvider<Product>(Products,
            new Dictionary<string, Func<Product, object?>> { ["Name"] = p => p.Name });

        var result = await provider.GetDataAsync(new DataTableQuery
        {
            Page = 1, PageSize = 10,
            SortColumn = "Name", SortDescending = true
        });

        result.Items[0].Name.Should().Be("Jam");
        result.Items[9].Name.Should().Be("Apple");
    }

    [Fact]
    public async Task InMemory_TextFilter_FiltersCorrectly()
    {
        var provider = new InMemoryDataProvider<Product>(Products,
            new Dictionary<string, Func<Product, object?>> { ["Category"] = p => p.Category });

        var result = await provider.GetDataAsync(new DataTableQuery
        {
            Page = 1, PageSize = 10,
            Filters = [new DataTableFilter("Category", "contains", "Fruit")]
        });

        result.TotalCount.Should().Be(3);
        result.Items.Should().OnlyContain(p => p.Category == "Fruit");
    }

    [Fact]
    public async Task InMemory_SearchText_FiltersAcrossAllStringFields()
    {
        var provider = new InMemoryDataProvider<Product>(Products,
            new Dictionary<string, Func<Product, object?>>
            {
                ["Name"]     = p => p.Name,
                ["Category"] = p => p.Category,
            });

        var result = await provider.GetDataAsync(new DataTableQuery
        {
            Page = 1, PageSize = 10,
            SearchText = "dairy"
        });

        result.TotalCount.Should().Be(2);
        result.Items.Should().OnlyContain(p => p.Category == "Dairy");
    }

    [Fact]
    public async Task InMemory_TotalCount_ReflectsFilteredCount()
    {
        var provider = new InMemoryDataProvider<Product>(Products,
            new Dictionary<string, Func<Product, object?>> { ["Category"] = p => p.Category });

        var result = await provider.GetDataAsync(new DataTableQuery
        {
            Page = 1, PageSize = 5,
            Filters = [new DataTableFilter("Category", "contains", "Vegetable")]
        });

        result.TotalCount.Should().Be(3);
        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task InMemory_NoQuery_ReturnsFirstPage()
    {
        var provider = new InMemoryDataProvider<Product>(Products);

        var result = await provider.GetDataAsync(new DataTableQuery());

        result.Page.Should().Be(1);
        result.TotalCount.Should().Be(10);
        result.Items.Count.Should().Be(10); // default pageSize=25 → all 10 items on page 1
    }
}
