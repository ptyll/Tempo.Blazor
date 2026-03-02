using FluentAssertions;

namespace Tempo.Blazor.Tests.Models;

public class DataTableQueryGroupingTests
{
    [Fact]
    public void DataTableQuery_GroupByColumns_DefaultsToEmpty()
    {
        var query = new Tempo.Blazor.Models.DataTableQuery();

        query.GroupByColumns.Should().NotBeNull();
        query.GroupByColumns.Should().BeEmpty();
    }

    [Fact]
    public void DataTableQuery_GroupByColumns_CanBeInitialized()
    {
        var query = new Tempo.Blazor.Models.DataTableQuery
        {
            GroupByColumns = ["Status", "Department"]
        };

        query.GroupByColumns.Should().HaveCount(2);
        query.GroupByColumns[0].Should().Be("Status");
        query.GroupByColumns[1].Should().Be("Department");
    }

    [Fact]
    public void DataTableQuery_GroupByColumns_IsReadOnly()
    {
        var query = new Tempo.Blazor.Models.DataTableQuery
        {
            GroupByColumns = ["Status"]
        };

        query.GroupByColumns.Should().BeAssignableTo<IReadOnlyList<string>>();
    }
}
