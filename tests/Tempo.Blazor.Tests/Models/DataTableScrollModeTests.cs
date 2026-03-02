using FluentAssertions;
using Tempo.Blazor.Components.DataTable;

namespace Tempo.Blazor.Tests.Models;

public class DataTableScrollModeTests
{
    [Fact]
    public void DataTableScrollMode_HasPaginationValue()
    {
        var mode = DataTableScrollMode.Pagination;
        mode.Should().BeDefined();
    }

    [Fact]
    public void DataTableScrollMode_HasVirtualizedValue()
    {
        var mode = DataTableScrollMode.Virtualized;
        mode.Should().BeDefined();
    }

    [Fact]
    public void DataTableScrollMode_HasExactlyTwoValues()
    {
        Enum.GetValues<DataTableScrollMode>().Should().HaveCount(2);
    }
}
