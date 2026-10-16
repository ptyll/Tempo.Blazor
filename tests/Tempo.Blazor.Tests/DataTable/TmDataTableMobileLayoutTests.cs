using FluentAssertions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmDataTableMobileLayoutTests : LocalizationTestBase
{
    [Fact]
    public void None_DoesNotAddResponsiveClass()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p.Add(c => c.Items, new List<BasicPerson>()));

        cut.Markup.Should().NotContain("tm-data-table-wrapper--responsive");
    }

    [Fact]
    public void Cards_DoesNotAddResponsiveClass()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson>())
            .Add(c => c.MobileLayout, DataTableMobileLayout.Cards));

        cut.Markup.Should().NotContain("tm-data-table-wrapper--responsive");
        cut.Markup.Should().NotContain("data-card-role");
    }

    [Fact]
    public void Scroll_AddsResponsiveClass()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson>())
            .Add(c => c.MobileLayout, DataTableMobileLayout.Scroll));

        cut.Find(".tm-data-table-wrapper--responsive").Should().NotBeNull();
    }
}
