using FluentAssertions;

namespace Tempo.Blazor.Tests.Models;

public class DataTableViewGroupingTests
{
    [Fact]
    public void DataTableView_GroupByColumns_DefaultsToEmptyList()
    {
        var view = new Tempo.Blazor.Models.DataTableView();

        view.GroupByColumns.Should().NotBeNull();
        view.GroupByColumns.Should().BeEmpty();
    }

    [Fact]
    public void DataTableView_GroupByColumns_CanBeSetAndRead()
    {
        var view = new Tempo.Blazor.Models.DataTableView
        {
            GroupByColumns = new List<string> { "Status", "Category" }
        };

        view.GroupByColumns.Should().HaveCount(2);
        view.GroupByColumns[0].Should().Be("Status");
        view.GroupByColumns[1].Should().Be("Category");
    }

    [Fact]
    public void DataTableView_GroupByColumns_PreservesOrder()
    {
        var view = new Tempo.Blazor.Models.DataTableView();
        view.GroupByColumns.Add("Department");
        view.GroupByColumns.Add("Status");
        view.GroupByColumns.Add("Priority");

        view.GroupByColumns.Should().ContainInOrder("Department", "Status", "Priority");
    }
}
