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
    public void Cards_AddsCardClassAndDefaultFieldRole()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson> { new("Ada", 37, "Engineer") })
            .Add(c => c.MobileLayout, DataTableMobileLayout.Cards)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, person => person.Name)));

        cut.Markup.Should().NotContain("tm-data-table-wrapper--responsive");
        cut.Find(".tm-data-table-wrapper--cards").Should().NotBeNull();
        cut.Find("td[data-card-role=\"Field\"]").Should().NotBeNull();
    }

    [Fact]
    public void Cards_HidesHiddenColumn()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson> { new("Ada", 37, "Engineer") })
            .Add(c => c.MobileLayout, DataTableMobileLayout.Cards)
            .Add(c => c.ScrollMode, DataTableScrollMode.Virtualized)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, person => person.Name))
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Secret")
                .Add(x => x.Field, person => person.Name)
                .Add(x => x.CardRole, DataTableCardRole.Hidden)));

        cut.Markup.Should().NotContain("data-card-role=\"Hidden\"");
        cut.FindAll("tbody td").Should().HaveCount(1);
    }

    [Fact]
    public void CardMode_WithMobileLayoutUnset_AddsCardsClassAndFieldRole()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson> { new("Ada", 37, "Engineer") })
            .Add(c => c.CardMode, true)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, person => person.Name)));

        cut.Find(".tm-data-table-wrapper--cards").Should().NotBeNull();
        cut.Find(".tm-data-table-wrapper--card").Should().NotBeNull();
        cut.Find("td[data-card-role=\"Field\"]").Should().NotBeNull();
        cut.Markup.Should().NotContain("tm-data-table-wrapper--responsive");
    }

    [Fact]
    public void CardMode_HidesHiddenColumn()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson> { new("Ada", 37, "Engineer") })
            .Add(c => c.CardMode, true)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, person => person.Name))
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Secret")
                .Add(x => x.Field, person => person.Name)
                .Add(x => x.CardRole, DataTableCardRole.Hidden)));

        cut.Markup.Should().NotContain("data-card-role=\"Hidden\"");
        cut.FindAll("tbody td").Should().HaveCount(1);
    }

    [Fact]
    public void CardMode_WithScrollLayoutDoesNotAddCardsClass()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson>())
            .Add(c => c.CardMode, true)
            .Add(c => c.MobileLayout, DataTableMobileLayout.Scroll));

        cut.Markup.Should().NotContain("tm-data-table-wrapper--cards");
        cut.Find(".tm-data-table-wrapper--responsive").Should().NotBeNull();
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
