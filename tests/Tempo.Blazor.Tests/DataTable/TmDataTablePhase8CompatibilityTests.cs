using FluentAssertions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmDataTablePhase8CompatibilityTests : LocalizationTestBase
{
    private static List<BasicPerson> People =>
    [
        new("Alice", 30, "Admin"),
        new("Bob", 25, "User")
    ];

    [Fact]
    public void GridNavigation_CoexistsWithScrollLayout()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .Add(c => c.MobileLayout, DataTableMobileLayout.Scroll)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, person => person.Name)));

        cut.Find(".tm-data-table-wrapper--responsive").Should().NotBeNull();
        cut.FindAll("td[tabindex]").Should().NotBeEmpty();
    }

    [Fact]
    public void GridNavigation_CoexistsWithGrouping()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .Add(c => c.ShowGrouping, true)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Role")
                .Add(x => x.PropertyName, "Role")
                .Add(x => x.Field, person => person.Role)
                .Add(x => x.Groupable, true)));

        cut.FindAll("td[tabindex]").Should().NotBeEmpty();
        cut.Find(".tm-data-table-group-zone").Should().NotBeNull();
    }

    [Fact]
    public void CardAppearance_CoexistsWithCardsMobileLayout()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.Appearance, DataTableAppearance.Card)
            .Add(c => c.MobileLayout, DataTableMobileLayout.Cards)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, person => person.Name)));

        cut.Find(".tm-data-table-wrapper--card-surface").Should().NotBeNull();
        cut.Find(".tm-data-table-wrapper--cards").Should().NotBeNull();
    }
}
