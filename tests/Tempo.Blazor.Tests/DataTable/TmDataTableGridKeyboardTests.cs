using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmDataTableGridKeyboardTests : LocalizationTestBase
{
    private static List<BasicPerson> People =>
    [
        new("Alice", 30, "Admin"),
        new("Bob", 25, "User"),
        new("Carol", 35, "Manager")
    ];

    private IRenderedComponent<TmDataTable<BasicPerson>> RenderGrid(DataTableKeyboardNavigation mode) =>
        Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.KeyboardNavigation, mode)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Name").Add(x => x.Field, p => p.Name))
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Role").Add(x => x.Field, p => p.Role)));

    [Fact]
    public void Rows_KeepsRowTabStops()
    {
        var cut = RenderGrid(DataTableKeyboardNavigation.Rows);
        cut.FindAll("tbody tr[tabindex='0']").Should().HaveCount(3);
        cut.FindAll("td[tabindex]").Should().BeEmpty();
    }

    [Fact]
    public void Grid_HasOneRovingCell()
    {
        var cut = RenderGrid(DataTableKeyboardNavigation.Grid);
        cut.FindAll("td[tabindex='0']").Should().ContainSingle();
        cut.FindAll("td[tabindex='-1']").Should().NotBeEmpty();
    }

    [Fact]
    public void Grid_ArrowRight_MovesActiveCell()
    {
        var cut = RenderGrid(DataTableKeyboardNavigation.Grid);
        cut.Find("td[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        var active = cut.FindAll("tbody tr").First().QuerySelectorAll("td[tabindex='0']");
        active.Should().ContainSingle();
        active[0].GetAttribute("aria-colindex").Should().Be("2");
    }

    [Fact]
    public void Grid_Enter_ActivatesRowOnce()
    {
        var clicks = 0;
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .Add(c => c.OnRowClick, _ => clicks++)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Name").Add(x => x.Field, p => p.Name)));

        cut.Find("td[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });
        clicks.Should().Be(0);
        cut.Find("td[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        clicks.Should().Be(1);
    }
}
