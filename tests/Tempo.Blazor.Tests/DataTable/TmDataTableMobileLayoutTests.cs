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
        cut.Markup.Should().NotContain("tm-data-table-wrapper--container");
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
        cut.Find(".tm-data-table-wrapper--container").Should().NotBeNull();
        cut.Find("td[data-card-role=\"Field\"]").Should().NotBeNull();
        cut.Find("td[data-label=\"Name\"]").Should().NotBeNull();
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
    public void CardMode_KeepsTheCardWrapperWithoutCardCellChrome()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson> { new("Ada", 37, "Engineer") })
            .Add(c => c.CardMode, true)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, person => person.Name)));

        cut.Find(".tm-data-table-wrapper--card").Should().NotBeNull();
        cut.Markup.Should().NotContain("tm-data-table-wrapper--cards");
        cut.Markup.Should().NotContain("tm-data-table-cell--card");
        cut.Markup.Should().NotContain("data-card-role");
        cut.Find("td[data-label=\"Name\"]").Should().NotBeNull();
        cut.Markup.Should().NotContain("tm-data-table-wrapper--container");
        cut.Markup.Should().NotContain("tm-data-table-wrapper--responsive");
    }

    [Fact]
    public void CardMode_DoesNotHideHiddenColumns()
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

        cut.Markup.Should().NotContain("data-card-role");
        cut.FindAll("tbody td").Should().HaveCount(2);
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
        cut.Find(".tm-data-table-wrapper--container").Should().NotBeNull();
    }

    [Fact]
    public void Scroll_PinsFirstVisibleColumnOnlyInScrollMode()
    {
        var scroll = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson> { new("Ada", 37, "Engineer") })
            .Add(c => c.MobileLayout, DataTableMobileLayout.Scroll)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Name").Add(x => x.Field, x => x.Name))
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Age").Add(x => x.Field, x => x.Age).Add(x => x.MobilePinned, true)));
        scroll.FindAll("th.data-mobile-pinned").Should().HaveCount(1);
        scroll.FindAll("td.data-mobile-pinned").Should().HaveCount(1);

        var plain = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson> { new("Ada", 37, "Engineer") })
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Name").Add(x => x.Field, x => x.Name)));
        plain.Markup.Should().NotContain("data-mobile-pinned");
    }

    [Fact]
    public void SourceCss_HasBalancedBraces()
    {
        var cssPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../src/Tempo.Blazor/wwwroot/css/components/_data-table.css"));
        var css = File.ReadAllText(cssPath);

        css.Count(character => character == '{').Should().Be(css.Count(character => character == '}'));
    }

    [Fact]
    public void Scroll_AddsResponsiveClass()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson>())
            .Add(c => c.MobileLayout, DataTableMobileLayout.Scroll));

        cut.Find(".tm-data-table-wrapper--responsive").Should().NotBeNull();
        cut.Find(".tm-data-table-wrapper--container").Should().NotBeNull();
    }
}
