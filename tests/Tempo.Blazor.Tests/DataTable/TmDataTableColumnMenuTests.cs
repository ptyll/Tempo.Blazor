using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Phase 6 contract for <see cref="ColumnMenuMode.Menu"/>: the kebab lives in each data column
/// header (never the toolbar), its items follow the column's own capabilities, and every action
/// reuses the table's existing sort / pin / group / hide / filter paths.
/// </summary>
public class TmDataTableColumnMenuTests : LocalizationTestBase
{
    private static readonly Person[] People =
    [
        new("Ann", "Sales"),
        new("Bob", "Sales"),
        new("Cara", "Ops"),
    ];

    [Fact]
    public void Default_mode_renders_no_column_menu_and_keeps_the_pin_button()
    {
        var cut = RenderTable();

        cut.FindAll(".tm-data-table-col-menu").Should().BeEmpty();
        cut.FindAll(".tm-data-table-toolbar .tm-data-table-col-menu").Should().BeEmpty();
        cut.FindAll(".tm-col-pin-btn").Should().NotBeEmpty("the pin button is the default affordance");
    }

    [Fact]
    public void Menu_mode_puts_one_kebab_in_every_data_header_and_none_in_the_toolbar()
    {
        var cut = RenderTable(ColumnMenuMode.Menu);

        var triggers = cut.FindAll(".tm-data-table-col-menu");
        triggers.Should().HaveCount(3, "every data column gets a menu; the expander and checkbox columns do not");
        triggers.Should().OnlyContain(t => t.ClassList.Contains("tm-data-table-col-menu"));
        cut.FindAll(".tm-data-table-toolbar .tm-data-table-col-menu").Should().BeEmpty();
        cut.FindAll(".tm-col-pin-btn").Should().BeEmpty("Menu mode replaces the standalone pin button");

        triggers[0].GetAttribute("aria-label").Should().Be("Column options: Name");
        triggers[0].GetAttribute("aria-haspopup").Should().Be("menu");
        triggers[0].GetAttribute("aria-expanded").Should().Be("false");
        triggers[0].GetAttribute("type").Should().Be("button");
        triggers[0].GetAttribute("tabindex").Should().Be("-1", "the kebab is reached with M, not as its own Tab stop");
    }

    [Fact]
    public void Menu_items_follow_the_columns_capabilities()
    {
        var cut = RenderTable(ColumnMenuMode.Menu);

        OpenMenu(cut, "Name");
        var items = MenuItems(cut);
        items.Select(i => i.GetAttribute("data-action")).Should().Equal(
            "sort-asc", "sort-desc", "pin-left", "pin-right", "group", "hide", "filter");
        cut.FindAll("[role='menu'] [role='separator']").Should().ContainSingle(
            "the sort group is separated from the column actions");
        items.Where(i => i.GetAttribute("data-action") is "sort-asc" or "sort-desc")
            .Should().OnlyContain(i => i.GetAttribute("role") == "menuitemradio",
                "sort direction is a radio group; menuitem does not support aria-checked");
        items.Where(i => i.GetAttribute("data-action") is not ("sort-asc" or "sort-desc"))
            .Should().OnlyContain(i => i.GetAttribute("role") == "menuitem");
        items.Should().OnlyContain(i => i.GetAttribute("tabindex") == "-1",
            "arrow keys move focus; Tab must not walk the items");
        items.Single(i => i.GetAttribute("data-action") == "sort-asc").GetAttribute("aria-checked")
            .Should().Be("false");
        items.Single(i => i.GetAttribute("data-action") == "sort-desc").GetAttribute("aria-checked")
            .Should().Be("false");

        OpenMenu(cut, "Note");
        MenuItems(cut).Select(i => i.GetAttribute("data-action")).Should().Equal("pin-left", "pin-right", "hide");
    }

    [Fact]
    public void Menu_omits_the_hide_item_when_the_column_cannot_be_hidden()
    {
        var cut = RenderTable(ColumnMenuMode.Menu, hideable: false);

        OpenMenu(cut, "Name");

        MenuItems(cut).Select(i => i.GetAttribute("data-action")).Should().NotContain("hide");
    }

    [Fact]
    public void Menu_mode_headers_reserve_the_kebab_space_via_tm_col_has_menu()
    {
        var cut = RenderTable(ColumnMenuMode.Menu);

        cut.Find(".tm-data-table-col-menu[data-column-key='Name']").ParentElement!.ClassList
            .Should().Contain("tm-col-has-menu",
                "the th must reserve the kebab's width so it never covers the label or sort arrow");

        // Without the menu (pin mode) the reservation class is not needed and must not appear.
        var plain = RenderTable(ColumnMenuMode.None);
        plain.FindAll(".tm-data-table-col-menu").Should().BeEmpty();
        plain.Find(".tm-data-table thead th").ClassList.Should().NotContain("tm-col-has-menu");
    }

    [Fact]
    public void Sorted_direction_shows_a_visible_check_in_the_menu()
    {
        var cut = RenderTable(ColumnMenuMode.Menu);
        OpenMenu(cut, "Name");
        ClickAction(cut, "sort-asc");
        OpenMenu(cut, "Name");

        var ascending = cut.Find("[role='menu'] [data-action='sort-asc']");
        ascending.QuerySelector(".tm-dropdown-item__check svg").Should().NotBeNull(
            "the sighted counterpart of aria-checked=true is a check icon (WCAG 1.3.1/1.4.1)");
        cut.Find("[role='menu'] [data-action='sort-desc']").QuerySelector(".tm-dropdown-item__check svg")
            .Should().BeNull("only the active direction carries the check");

        // Every item renders the check slot (empty for plain menuitems) so all labels align.
        var items = MenuItems(cut);
        cut.FindAll("[role='menu'] .tm-dropdown-item > .tm-dropdown-item__check")
            .Should().HaveCount(items.Count);
        items.Where(i => i.GetAttribute("role") == "menuitem")
            .Should().OnlyContain(i => i.QuerySelector(".tm-dropdown-item__check svg") == null,
                "plain menuitems keep an empty slot for label alignment");
    }

    [Fact]
    public void Sort_ascending_item_sets_the_sort_and_re_queries_the_provider()
    {
        var provider = Substitute.For<IDataTableDataProvider<Person>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PagedResult<Person> { Items = People, TotalCount = People.Length, Page = 1, PageSize = 25 }));
        var cut = RenderTable(ColumnMenuMode.Menu, provider: provider);

        OpenMenu(cut, "Name");
        ClickAction(cut, "sort-asc");

        cut.Find(".tm-data-table-col-menu[data-column-key='Name']").ParentElement!
            .GetAttribute("aria-sort").Should().Be("ascending");
        OpenMenu(cut, "Name");
        var ascending = cut.Find("[role='menu'] [data-action='sort-asc']");
        ascending.GetAttribute("role").Should().Be("menuitemradio");
        ascending.GetAttribute("aria-checked")
            .Should().Be("true", "the direction the column is already sorted in is marked");
        cut.Find("[role='menu'] [data-action='sort-desc']").GetAttribute("aria-checked").Should().Be("false");
        provider.Received().GetDataAsync(
            Arg.Is<DataTableQuery>(q => q.SortColumn == "Name" && !q.SortDescending),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Group_by_item_adds_the_column_to_the_grouping()
    {
        var grouped = new List<IReadOnlyList<string>>();
        var cut = RenderTable(ColumnMenuMode.Menu, onGroupingChanged: keys => grouped.Add(keys));

        OpenMenu(cut, "Name");
        ClickAction(cut, "group");

        grouped.Should().ContainSingle().Which.Should().Equal("Name");
        cut.FindAll(".tm-data-table-group-row").Should().NotBeEmpty();
    }

    [Fact]
    public void Hide_item_removes_the_column()
    {
        var cut = RenderTable(ColumnMenuMode.Menu);

        OpenMenu(cut, "Dept");
        ClickAction(cut, "hide");

        cut.FindAll(".tm-data-table-col-menu[data-column-key='Dept']").Should().BeEmpty();
        cut.FindAll(".tm-data-table-col-menu[data-column-key='Name']").Should().ContainSingle();
    }

    [Fact]
    public void Pin_items_set_the_pin_and_a_pinned_column_offers_unpin()
    {
        var cut = RenderTable(ColumnMenuMode.Menu);

        OpenMenu(cut, "Name");
        ClickAction(cut, "pin-left");

        cut.Find(".tm-data-table-col-menu[data-column-key='Name']").ParentElement!
            .ClassList.Should().Contain("tm-col-pinned-left");

        OpenMenu(cut, "Name");
        MenuItems(cut).Select(i => i.GetAttribute("data-action")).Should().Contain("unpin");
        ClickAction(cut, "unpin");

        cut.Find(".tm-data-table-col-menu[data-column-key='Name']").ParentElement!
            .ClassList.Should().NotContain("tm-col-pinned-left");
    }

    [Fact]
    public void Filter_item_focuses_the_columns_own_filter_control()
    {
        var cut = RenderTable(ColumnMenuMode.Menu);

        OpenMenu(cut, "Name");
        ClickAction(cut, "filter");

        var filter = cut.Find("input.tm-col-filter-input");
        filter.Id.Should().StartWith("tm-dt-").And.EndWith("-col-filter-Name");
        cut.Find($"#{filter.Id}").Should().NotBeNull();
    }

    [Fact]
    public void Two_tables_with_the_same_keys_get_distinct_menu_and_filter_ids()
    {
        var first = RenderTable(ColumnMenuMode.Menu);
        var second = RenderTable(ColumnMenuMode.Menu);

        OpenMenu(first, "Name");
        OpenMenu(second, "Name");

        var firstMenu = first.Find("[role='menu']").Id;
        var secondMenu = second.Find("[role='menu']").Id;
        firstMenu.Should().NotBe(secondMenu);
        firstMenu.Should().StartWith("tm-dt-").And.EndWith("-col-menu-Name");
        first.Find(".tm-data-table-col-menu[data-column-key='Name']").GetAttribute("aria-controls")
            .Should().Be(firstMenu);

        var firstFilter = first.Find("input.tm-col-filter-input").Id;
        var secondFilter = second.Find("input.tm-col-filter-input").Id;
        firstFilter.Should().NotBe(secondFilter);
        firstFilter.Should().StartWith("tm-dt-").And.EndWith("-col-filter-Name");
        firstFilter.Should().NotContain(" ", "a key with spaces must still produce a valid id");
    }

    [Fact]
    public void A_key_with_spaces_still_produces_a_valid_id()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ColumnMenuMode, ColumnMenuMode.Menu);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<Person>>(0);
                b.AddAttribute(1, "Title", "Full name");
                b.AddAttribute(2, "PropertyName", "Full name");
                b.AddAttribute(3, "Sortable", true);
                b.AddAttribute(4, "Filterable", true);
                b.AddAttribute(5, "Field", (Func<Person, object?>)(x => x.Name));
                b.CloseComponent();
            });
        });

        OpenMenu(cut, "Full name");

        var menuId = cut.Find("[role='menu']").Id;
        menuId.Should().EndWith("-col-menu-Full_name");
        menuId.Should().NotContain(" ");
        cut.Find("input.tm-col-filter-input").Id.Should().EndWith("-col-filter-Full_name");
    }

    [Fact]
    public void M_on_a_header_opens_its_menu_and_Escape_returns_to_the_header()
    {
        var cut = RenderTable(ColumnMenuMode.Menu);
        var header = cut.Find(".tm-data-table-col-menu[data-column-key='Name']").ParentElement!;

        header.KeyDown(Key("m"));

        cut.FindAll("[role='menu'] [role^='menuitem']").Should().NotBeEmpty();
        cut.Find(".tm-data-table-col-menu[aria-controls]").GetAttribute("aria-expanded").Should().Be("true");

        cut.Find("[role='menu']").KeyDown(Key("Escape"));

        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    [Fact]
    public void M_is_not_answered_while_a_filter_input_in_the_header_has_focus()
    {
        var cut = RenderTable(ColumnMenuMode.Menu, headerTemplate: true);
        var filter = cut.Find(".tm-data-table-col-menu[data-column-key='Name']").ParentElement!.QuerySelector("input")!;

        filter.KeyDown(Key("m"));

        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    [Fact]
    public void Grid_mode_answers_M_on_the_active_header_without_moving_the_cursor()
    {
        var cut = RenderTable(ColumnMenuMode.Menu, grid: true);
        var header = cut.Find(".tm-data-table-col-menu[data-column-key='Name']").ParentElement!;

        header.KeyDown(Key("m"));

        cut.FindAll("[role='menu'] [role^='menuitem']").Should().NotBeEmpty();
        cut.Find("th[tabindex='0']").TextContent.Trim().Should().Be(
            "Note", "M opens the menu of the header it was pressed on and does not move the grid cursor");
    }

    [Fact]
    public void Clear_sort_is_offered_only_while_the_column_is_sorted_and_removes_the_sort()
    {
        var provider = Substitute.For<IDataTableDataProvider<Person>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Person> { Items = People, TotalCount = People.Length });
        var cut = RenderTable(ColumnMenuMode.Menu, provider: provider);

        OpenMenu(cut, "Name");
        MenuItems(cut).Select(i => i.GetAttribute("data-action"))
            .Should().NotContain("clear-sort", "an unsorted column has nothing to clear");

        ClickAction(cut, "sort-asc");
        OpenMenu(cut, "Name");

        var clear = MenuItems(cut).Single(i => i.GetAttribute("data-action") == "clear-sort");
        clear.TextContent.Trim().Should().Be("Clear sort");

        clear.Click();

        cut.Find(".tm-data-table-col-menu[data-column-key='Name']").ParentElement!
            .GetAttribute("aria-sort").Should().Be("none");
        provider.Received().GetDataAsync(
            Arg.Is<DataTableQuery>(r => r.SortColumn == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Hide_is_not_offered_for_the_last_visible_column()
    {
        var cut = RenderTable(ColumnMenuMode.Menu);

        OpenMenu(cut, "Dept");
        ClickAction(cut, "hide");
        OpenMenu(cut, "Note");
        ClickAction(cut, "hide");

        OpenMenu(cut, "Name");

        MenuItems(cut).Select(i => i.GetAttribute("data-action"))
            .Should().NotContain("hide", "hiding the last visible column would leave the table with no columns");
    }

    [Fact]
    public void Alt_ArrowDown_opens_the_column_menu_and_is_announced()
    {
        var cut = RenderTable(ColumnMenuMode.Menu);
        var header = cut.Find(".tm-data-table-col-menu[data-column-key='Name']").ParentElement!;

        header.GetAttribute("aria-keyshortcuts").Should().Contain("Alt+ArrowDown");

        header.KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        cut.FindAll("[role='menu'] [role^='menuitem']").Should().NotBeEmpty();
    }

    [Fact]
    public void ContentOnly_toolbar_mode_still_renders_the_header_menu()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ColumnMenuMode, ColumnMenuMode.Menu);
            p.Add(c => c.ToolbarMode, DataToolbarMode.ContentOnly);
            AddColumns(p);
        });

        cut.FindAll(".tm-data-table-toolbar").Should().BeEmpty("ContentOnly hides the toolbar chrome");
        cut.FindAll(".tm-data-table-col-menu").Should().HaveCount(3, "the menu lives in the header, which is content");
    }

    [Fact]
    public void P_still_pins_the_column_in_menu_mode()
    {
        var cut = RenderTable(ColumnMenuMode.Menu);
        var header = cut.Find(".tm-data-table-col-menu[data-column-key='Name']").ParentElement!;

        header.GetAttribute("aria-keyshortcuts").Should().Contain("P");

        // The shortcut is answered on the header itself — the sort button has no key handler of its
        // own, and its keydown bubbles here. Pinning reorders the columns, so re-query afterwards.
        header.KeyDown(Key("p"));

        cut.Find(".tm-data-table-col-menu[data-column-key='Name']").ParentElement!.ClassList
            .Should().Contain("tm-col-pinned-left", "Menu mode removes the pin button, not the P shortcut");
    }

    [Fact]
    public void ShowColumnMenu_false_hides_the_kebab_even_in_menu_mode()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ColumnMenuMode, ColumnMenuMode.Menu);
            p.Add(c => c.ShowColumnMenu, false);
            AddColumns(p);
        });

        cut.FindAll(".tm-data-table-col-menu").Should().BeEmpty();
        cut.FindAll(".tm-col-pin-btn").Should().BeEmpty();
    }

    private IRenderedComponent<TmDataTable<Person>> RenderTable(
        ColumnMenuMode mode = ColumnMenuMode.None,
        bool hideable = true,
        bool headerTemplate = false,
        bool grid = false,
        IDataTableDataProvider<Person>? provider = null,
        Action<IReadOnlyList<string>>? onGroupingChanged = null)
        => Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "column-menu");
            p.Add(c => c.ColumnMenuMode, mode);
            // Group and Hide are features of the table, not of the column alone: the items exist
            // only while grouping is shown and the column picker renders.
            p.Add(c => c.ShowGrouping, true);
            p.Add(c => c.ShowColumnPicker, true);
            p.Add(c => c.DetailTemplate, (RenderFragment<Person>)(_ => __ => { }));
            p.Add(c => c.Selectable, true);
            if (grid)
                p.Add(c => c.KeyboardNavigation, DataTableKeyboardNavigation.Grid);
            if (provider is not null)
                p.Add(c => c.DataProvider, provider);
            else
                p.Add(c => c.Items, People);
            if (onGroupingChanged is not null)
                p.Add(c => c.OnGroupingChanged, EventCallback.Factory.Create<IReadOnlyList<string>>(this, onGroupingChanged));
            AddColumns(p, hideable, headerTemplate);
        });

    private static void AddColumns(ComponentParameterCollectionBuilder<TmDataTable<Person>> p, bool hideable = true, bool headerTemplate = false)
        => p.AddChildContent(b =>
        {
            b.OpenComponent<TmDataTableColumn<Person>>(0);
            b.AddAttribute(1, "Title", "Name");
            b.AddAttribute(2, "PropertyName", "Name");
            b.AddAttribute(3, "Sortable", true);
            b.AddAttribute(4, "Groupable", true);
            b.AddAttribute(5, "Filterable", true);
            b.AddAttribute(6, "Hideable", hideable);
            b.AddAttribute(7, "Field", (Func<Person, object?>)(x => x.Name));
            if (headerTemplate)
                b.AddAttribute(8, "HeaderTemplate", (RenderFragment)(c => c.AddMarkupContent(0, "<input aria-label='Name filter' />")));
            b.CloseComponent();

            b.OpenComponent<TmDataTableColumn<Person>>(10);
            b.AddAttribute(11, "Title", "Dept");
            b.AddAttribute(12, "PropertyName", "Dept");
            b.AddAttribute(13, "Sortable", true);
            b.AddAttribute(14, "Field", (Func<Person, object?>)(x => x.Dept));
            b.CloseComponent();

            b.OpenComponent<TmDataTableColumn<Person>>(20);
            b.AddAttribute(21, "Title", "Note");
            b.AddAttribute(22, "PropertyName", "Note");
            b.AddAttribute(23, "Sortable", false);
            b.AddAttribute(24, "Filterable", false);
            b.AddAttribute(25, "Hideable", true);
            b.AddAttribute(26, "Field", (Func<Person, object?>)(x => x.Note));
            b.CloseComponent();
        });

    private static void OpenMenu(IRenderedComponent<TmDataTable<Person>> cut, string columnKey)
        => cut.Find($".tm-data-table-col-menu[data-column-key='{columnKey}']").Click();

    private static IReadOnlyList<AngleSharp.Dom.IElement> MenuItems(IRenderedComponent<TmDataTable<Person>> cut)
        => cut.FindAll("[role='menu'] [role^='menuitem']");

    private static void ClickAction(IRenderedComponent<TmDataTable<Person>> cut, string action)
        => cut.Find($"[role='menu'] [data-action='{action}']").Click();

    private static KeyboardEventArgs Key(string key) => new() { Key = key };

    public record Person(string Name, string Dept, string? Note = null);
}
