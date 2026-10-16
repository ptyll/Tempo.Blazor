using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmDataTableAppearanceTests : LocalizationTestBase
{
    [Fact]
    public void Plain_DoesNotAddCardClass()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p.Add(c => c.Items, new List<BasicPerson>()));
        cut.Markup.Should().NotContain("tm-data-table-wrapper--card-surface");
    }

    [Fact]
    public void Card_AddsCardClass()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson>())
            .Add(c => c.Appearance, DataTableAppearance.Card));
        cut.Find(".tm-data-table-wrapper--card-surface").Should().NotBeNull();
    }
}
