using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmDataTableVirtualRowHeightTests : LocalizationTestBase
{
    [Fact]
    public void Virtualized_SetsRowHeightToken()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson> { new("Ada", 1, "User") })
            .Add(c => c.ScrollMode, DataTableScrollMode.Virtualized)
            .Add(c => c.VirtualItemSize, 48f));

        cut.Find(".tm-data-table-virtual-scroll").GetAttribute("style")
            .Should().Contain("--tm-data-table-row-height: 48px");
    }
}
