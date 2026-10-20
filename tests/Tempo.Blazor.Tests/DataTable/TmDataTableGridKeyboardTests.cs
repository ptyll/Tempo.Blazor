using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
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
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Name").Add(x => x.Field, person => person.Name))
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Role").Add(x => x.Field, person => person.Role)));

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
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Name").Add(x => x.Field, person => person.Name)));

        cut.Find("td[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });
        clicks.Should().Be(0);
        cut.Find("td[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        clicks.Should().Be(1);
    }

    [Fact]
    public void Grid_RowKeyDown_DoesNotActivate()
    {
        // bUnit does not bubble, so the row handler is invoked directly. In Grid mode it must
        // be a no-op: the cell owns activation, and a bubbling Enter must not fire twice.
        var clicks = 0;
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .Add(c => c.OnRowClick, _ => clicks++)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Name").Add(x => x.Field, person => person.Name)));

        cut.Find("tbody tr").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        clicks.Should().Be(0);
        cut.Find("td[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        clicks.Should().Be(1);
    }

    [Fact]
    public void Grid_Space_TogglesSelection_WithoutRowClick()
    {
        var clicks = 0;
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.Selectable, true)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .Add(c => c.OnRowClick, _ => clicks++)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Name").Add(x => x.Field, person => person.Name)));

        var active = cut.Find("td[data-grid-cell][tabindex='0']");
        active.KeyDown(new KeyboardEventArgs { Key = " " });

        clicks.Should().Be(0);
        cut.FindAll("tbody tr.tm-row-selected").Should().ContainSingle();
    }

    [Fact]
    public void Grid_SelectableAndDetail_HasExactlyOneTabStop_DefaultingToFirstDataColumn()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.Selectable, true)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .Add(c => c.DetailTemplate, (RenderFragment<BasicPerson>)(_ => b => b.AddContent(0, "detail")))
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Name").Add(x => x.Field, person => person.Name)));

        var stops = cut.FindAll("[data-grid-cell][tabindex='0']");
        stops.Should().ContainSingle();
        stops[0].GetAttribute("aria-colindex").Should().Be("3");
        cut.FindAll("td.tm-col-expander[role='gridcell']").Should().NotBeEmpty();
        cut.FindAll("td.tm-col-check[role='gridcell']").Should().NotBeEmpty();
    }

    [Fact]
    public void Grid_Home_MovesToCheckboxColumn()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.Selectable, true)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Name").Add(x => x.Field, person => person.Name)));

        cut.Find("td[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "Home" });

        var active = cut.Find("td[tabindex='0']");
        active.ClassList.Should().Contain("tm-col-check");
        active.GetAttribute("aria-colindex").Should().Be("1");
    }

    [Fact]
    public void Grid_ArrowLeftOnFirstColumn_Stays()
    {
        var cut = RenderGrid(DataTableKeyboardNavigation.Grid);
        cut.Find("td[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        var active = cut.Find("td[tabindex='0']");
        active.GetAttribute("aria-colindex").Should().Be("1");
        cut.FindAll("td[tabindex='0']").Should().ContainSingle();
    }

    [Fact]
    public void Grid_PageShrink_KeepsExactlyOneTabStop()
    {
        var items = Enumerable.Range(1, 30).Select(i => new BasicPerson($"P{i}", i, "User")).ToList();
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, items)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .Add(c => c.DefaultPageSize, 25)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Name").Add(x => x.Field, person => person.Name)));

        for (var i = 0; i < 24; i++)
            cut.Find("td[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.FindAll("td[tabindex='0']").Should().ContainSingle();

        cut.Render(p => p.Add(c => c.PageSize, 10));

        cut.FindAll("td[data-grid-cell][tabindex='0']").Should().ContainSingle();
    }

    [Fact]
    public void Grid_Empty_HeaderIsTheTabStop()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, Array.Empty<BasicPerson>())
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .Add(c => c.EmptyTitle, "Nothing here")
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Name").Add(x => x.Field, person => person.Name)));

        cut.FindAll("td[tabindex='0']").Should().BeEmpty();
        cut.FindAll("th[role='columnheader'][tabindex='0']").Should().ContainSingle();
    }

    [Fact]
    public async Task Grid_Grouped_HeaderIsNavigable_AndEnterCollapses()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .Add(c => c.ShowGrouping, true)
            .Add(c => c.GroupsCollapsedByDefault, false)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Role")
                .Add(x => x.PropertyName, "Role")
                .Add(x => x.Field, person => person.Role)
                .Add(x => x.Groupable, true)));

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Role"));

        var header = cut.Find("tr.tm-data-table-group-row td[role='gridcell']");
        header.GetAttribute("tabindex").Should().Be("0");
        header.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.Find("tr.tm-data-table-group-row button").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void Grid_Virtualized_CellsAreGridCells()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .Add(c => c.ScrollMode, DataTableScrollMode.Virtualized)
            .Add(c => c.VirtualScrollHeight, "200px")
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Name").Add(x => x.Field, person => person.Name)));

        cut.FindAll("td[role='gridcell']").Should().NotBeEmpty();
        cut.FindAll("td[tabindex='0']").Should().ContainSingle();
    }

    [Fact]
    public void Grid_F2_StartsEdit_EscapeReturns()
    {
        var people = new List<EditableGridPerson> { new() { Name = "Alice" } };
        var cut = Render<TmDataTable<EditableGridPerson>>(p => p
            .Add(c => c.Items, people)
            .Add(c => c.Editable, true)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .AddChildContent<TmDataTableColumn<EditableGridPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, person => person.Name)
                .Add(x => x.Editable, true)
                .Add(x => x.EditTemplate, (RenderFragment<EditableGridPerson>)(item => b =>
                    b.AddMarkupContent(0, $"<input class=\"name-editor\" value=\"{item.Name}\" />")))));

        cut.Find("td[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "F2" });
        cut.FindAll("tr.tm-row-editing").Should().ContainSingle();

        cut.Find("tr.tm-row-editing").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.FindAll("tr.tm-row-editing").Should().BeEmpty();
        cut.FindAll("td[tabindex='0']").Should().ContainSingle();
    }

    [Fact]
    public void Grid_CardLayout_ColumnCountIgnoresHidden()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .Add(c => c.MobileLayout, DataTableMobileLayout.Cards)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, person => person.Name)
                .Add(x => x.CardRole, DataTableCardRole.Title))
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Secret")
                .Add(x => x.Field, person => person.Role)
                .Add(x => x.CardRole, DataTableCardRole.Hidden)));

        cut.Find("table").GetAttribute("aria-colcount").Should().Be("1");
        cut.FindAll("td[role='gridcell']").Should().HaveCount(3);
    }

    [Fact]
    public void Grid_End_WithEditActions_KeepsExactlyOneTabStop()
    {
        var people = new List<EditableGridPerson> { new() { Name = "Alice" }, new() { Name = "Bob" } };
        var cut = Render<TmDataTable<EditableGridPerson>>(p => p
            .Add(c => c.Items, people)
            .Add(c => c.Editable, true)
            .Add(c => c.EditActionsMode, EditActionsMode.Icons)
            .Add(c => c.ShowColumnMenu, false)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .AddChildContent<TmDataTableColumn<EditableGridPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, person => person.Name)
                .Add(x => x.Editable, true)
                .Add(x => x.EditTemplate, (RenderFragment<EditableGridPerson>)(item => b =>
                    b.AddMarkupContent(0, $"<input class=\"name-editor\" value=\"{item.Name}\" />")))));

        cut.Find("td[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "End" });

        var stops = cut.FindAll("[tabindex='0']");
        stops.Should().ContainSingle();
        stops[0].ClassList.Should().Contain("tm-data-table__edit-actions");
        stops[0].GetAttribute("role").Should().Be("gridcell");
        cut.Find("button.tm-data-table__edit-action--start").GetAttribute("tabindex").Should().Be("-1");
    }

    [Fact]
    public async Task Grid_CollapseGroup_KeepsExactlyOneTabStop()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .Add(c => c.ShowColumnMenu, false)
            .Add(c => c.ShowGrouping, true)
            .Add(c => c.GroupsCollapsedByDefault, false)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Role")
                .Add(x => x.PropertyName, "Role")
                .Add(x => x.Field, person => person.Role)
                .Add(x => x.Groupable, true))
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, person => person.Name)));

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Role"));

        // The first group header is the initial stop. Move onto an item below it, then collapse
        // the group that contains that item. The cursor must not stay on a row that no longer renders.
        cut.Find("[data-grid-cell][tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find("[data-grid-cell][tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find("tr.tm-data-table-group-row button").Click();

        cut.FindAll("[tabindex='0']").Should().ContainSingle();
        cut.Find("tr.tm-data-table-group-row [tabindex='0']").Should().NotBeNull();
    }

    [Fact]
    public void Grid_HideColumn_KeepsExactlyOneTabStop()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .Add(c => c.ShowColumnMenu, false)
            .Add(c => c.ShowToolbar, true)
            .Add(c => c.ShowColumnPicker, true)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.PropertyName, "name")
                .Add(x => x.Field, person => person.Name))
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Role")
                .Add(x => x.PropertyName, "role")
                .Add(x => x.Field, person => person.Role)));

        cut.Find("td[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "End" });
        cut.Find("td[tabindex='0']").GetAttribute("aria-colindex").Should().Be("2");

        cut.Find(".tm-column-picker-toggle").Click();
        cut.Find(".tm-column-picker-item input[data-key='role']").Change(false);

        cut.FindAll("[tabindex='0']").Should().ContainSingle();
        cut.Find("table").GetAttribute("aria-colcount").Should().Be("1");
    }

    [Fact]
    public void Grid_InnerControls_AreNotTabStops()
    {
        var people = new List<EditableGridPerson> { new() { Name = "Alice", Role = "Admin" }, new() { Name = "Bob", Role = "User" } };
        var cut = Render<TmDataTable<EditableGridPerson>>(p => p
            .Add(c => c.Items, people)
            .Add(c => c.Selectable, true)
            .Add(c => c.Editable, true)
            .Add(c => c.EditActionsMode, EditActionsMode.Icons)
            .Add(c => c.ShowColumnMenu, false)
            .Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid)
            .Add(c => c.ShowGrouping, true)
            .Add(c => c.GroupsCollapsedByDefault, false)
            .Add(c => c.DetailTemplate, (RenderFragment<EditableGridPerson>)(_ => b => b.AddContent(0, "detail")))
            .AddChildContent<TmDataTableColumn<EditableGridPerson>>(c => c
                .Add(x => x.Title, "Role")
                .Add(x => x.PropertyName, "Role")
                .Add(x => x.Field, person => person.Role)
                .Add(x => x.Groupable, true)
                .Add(x => x.Editable, true)
                .Add(x => x.EditTemplate, (RenderFragment<EditableGridPerson>)(item => b =>
                    b.AddMarkupContent(0, $"<input value=\"{item.Role}\" />"))))
            .AddChildContent<TmDataTableColumn<EditableGridPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, person => person.Name)));

        cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Role")).GetAwaiter().GetResult();

        cut.FindAll("[tabindex='0']").Should().ContainSingle();
        var controls = cut.FindAll("tbody button, tbody input");
        controls.Should().NotBeEmpty();
        controls.Should().OnlyContain(c => c.GetAttribute("tabindex") == "-1");
    }

    [Fact]
    public void Grid_AriaRowCount_IncludesHeaderRows()
    {
        var cut = RenderGrid(DataTableKeyboardNavigation.Grid);
        // Header is aria-rowindex 1; body rows continue from GridBodyRowOffset, so three items
        // announce a count of 4 and the last rendered body row is index 3.
        cut.Find("table").GetAttribute("aria-rowcount").Should().Be("4");
        cut.FindAll("tbody tr").Last().GetAttribute("aria-rowindex").Should().Be("3");
    }

    [Fact]
    public async Task Grid_ArrowRight_FocusesMovedCell()
    {
        var cut = RenderGrid(DataTableKeyboardNavigation.Grid);
        cut.Find("td[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        cut.WaitForAssertion(() =>
            JSInterop.VerifyInvoke("tmDataTable.focusGridCell").Should().NotBeNull());
        await Task.CompletedTask;
    }

    public sealed class EditableGridPerson
    {
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
    }
}
