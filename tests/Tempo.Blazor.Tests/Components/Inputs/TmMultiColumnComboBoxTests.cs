using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Inputs;

public class TmMultiColumnComboBoxTests : LocalizationTestBase
{
    private record Product(int Id, string Name, string Category, decimal Price);

    private static IReadOnlyList<Product> GetProducts() => new List<Product>
    {
        new(1, "Laptop", "Electronics", 1200m),
        new(2, "Mouse", "Electronics", 25m),
        new(3, "Desk", "Furniture", 300m),
        new(4, "Chair", "Furniture", 150m),
    };

    private static IReadOnlyList<MultiColumnComboBoxColumn<Product>> GetColumns() => new List<MultiColumnComboBoxColumn<Product>>
    {
        new() { Title = "Name", Field = p => p.Name },
        new() { Title = "Category", Field = p => p.Category },
        new() { Title = "Price", Field = p => p.Price, Width = "80px" },
    };

    /// <summary>Provider that supports inline create-new and recent items via the K10 default-interface methods.</summary>
    private sealed class FakeProductProvider : IDropdownDataProvider<Product>
    {
        public Task<DropdownDataResult<Product>> GetItemsAsync(DropdownSearchRequest request, CancellationToken ct = default)
            => Task.FromResult(DropdownDataResult<Product>.WithAllItems(GetProducts()));

        public Task<Product?> CreateAsync(string text, CancellationToken ct = default)
            => Task.FromResult<Product?>(new Product(99, text, "New", 0m));

        public Task<IReadOnlyList<Product>> GetRecentAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Product>>(new List<Product> { new(2, "Mouse", "Electronics", 25m) });
    }

    [Fact]
    public void TmMultiColumnComboBox_Renders_Trigger()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns()));

        cut.Find(".tm-multi-column-combo-box__trigger").Should().NotBeNull();
    }

    [Fact]
    public void TmMultiColumnComboBox_Placeholder_Shown_When_No_Value()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns()));

        cut.Find(".tm-multi-column-combo-box__placeholder").TextContent.Should().Contain("Select an item");
    }

    [Fact]
    public void TmMultiColumnComboBox_Click_Opens_Dropdown()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns()));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();

        cut.Find(".tm-multi-column-combo-box__dropdown").Should().NotBeNull();
    }

    [Fact]
    public void TmMultiColumnComboBox_Dropdown_Shows_Grid_With_Columns()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns()));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();

        var headers = cut.FindAll(".tm-multi-column-combo-box__th");
        headers.Count.Should().Be(3);
        headers[0].TextContent.Should().Be("Name");
        headers[1].TextContent.Should().Be("Category");
        headers[2].TextContent.Should().Be("Price");
    }

    [Fact]
    public void TmMultiColumnComboBox_Dropdown_Shows_Data_Rows()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns()));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();

        var rows = cut.FindAll(".tm-multi-column-combo-box__tr");
        rows.Count.Should().Be(4);
    }

    [Fact]
    public void TmMultiColumnComboBox_Select_Row_Sets_Value()
    {
        int? selectedValue = null;

        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns())
            .Add(c => c.ValueChanged, v => selectedValue = v));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();
        var rows = cut.FindAll(".tm-multi-column-combo-box__tr");
        rows[0].Click();

        selectedValue.Should().Be(1);
    }

    [Fact]
    public void TmMultiColumnComboBox_Selected_Row_Has_Selected_Class()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns())
            .Add(c => c.Value, 2));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();

        var rows = cut.FindAll(".tm-multi-column-combo-box__tr");
        rows[1].ClassList.Should().Contain("tm-multi-column-combo-box__tr--selected");
    }

    [Fact]
    public void TmMultiColumnComboBox_Filter_Reduces_Rows()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns()));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();

        // Filter input
        var filterInput = cut.Find(".tm-multi-column-combo-box__filter input");
        filterInput.Input("Furn");

        var rows = cut.FindAll(".tm-multi-column-combo-box__tr");
        rows.Count.Should().Be(2); // Desk, Chair
    }

    [Fact]
    public void TmMultiColumnComboBox_Clear_Button_Clears_Value()
    {
        int? selectedValue = 999;

        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns())
            .Add(c => c.Value, 1)
            .Add(c => c.ValueChanged, v => selectedValue = v));

        cut.Find(".tm-multi-column-combo-box__clear").Click();

        selectedValue.Should().Be(0); // default(int)
    }

    [Fact]
    public void TmMultiColumnComboBox_Disabled_Hides_Interactions()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns())
            .Add(c => c.Disabled, true));

        cut.Find(".tm-multi-column-combo-box--disabled").Should().NotBeNull();
    }

    // ── ARIA semantics ──────────────────────────────────────────────────────
    // The panel wraps a filter, a recent list and a create row around the grid —
    // role=dialog describes that container; role=grid belongs to the <table> alone.

    [Fact]
    public void TmMultiColumnComboBox_Panel_Role_Is_Dialog_Not_Grid()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns()));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();

        cut.Find(".tm-multi-column-combo-box__dropdown").GetAttribute("role").Should().Be("dialog");
        cut.Find("table.tm-multi-column-combo-box__grid").GetAttribute("role").Should().Be("grid");
    }

    [Fact]
    public void TmMultiColumnComboBox_Trigger_AriaHaspopup_Is_Dialog()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns()));

        cut.Find(".tm-multi-column-combo-box__trigger").GetAttribute("aria-haspopup").Should().Be("dialog");
    }

    [Fact]
    public void TmMultiColumnComboBox_Custom_Class_Applied()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns())
            .Add(c => c.Class, "my-combo"));

        cut.Find(".tm-multi-column-combo-box").ClassList.Should().Contain("my-combo");
    }

    // ── Accent-insensitive filtering (FormD normalization) ──────────────────

    private static IReadOnlyList<Product> GetCzechCities() => new List<Product>
    {
        new(1, "Ústí nad Labem", "Ústecký kraj", 0m),
        new(2, "Praha", "Hlavní město", 0m),
        new(3, "Český Krumlov", "Jihočeský kraj", 0m),
    };

    [Fact]
    public void TmMultiColumnComboBox_Filter_Without_Diacritics_Matches_Accented_Rows()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetCzechCities())
            .Add(c => c.ValueField, x => x.Id)
            .Add(c => c.TextField, x => x.Name)
            .Add(c => c.Columns, GetColumns()));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();
        cut.Find(".tm-multi-column-combo-box__filter input").Input("usti");

        var rows = cut.FindAll(".tm-multi-column-combo-box__tr");
        rows.Count.Should().Be(1);
        rows[0].TextContent.Should().Contain("Ústí nad Labem");
    }

    [Fact]
    public void TmMultiColumnComboBox_Filter_Matches_Accent_Insensitively_Across_All_Columns()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetCzechCities())
            .Add(c => c.ValueField, x => x.Id)
            .Add(c => c.TextField, x => x.Name)
            .Add(c => c.Columns, GetColumns()));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();
        cut.Find(".tm-multi-column-combo-box__filter input").Input("jihocesky");

        var rows = cut.FindAll(".tm-multi-column-combo-box__tr");
        rows.Count.Should().Be(1);
        rows[0].TextContent.Should().Contain("Český Krumlov");
    }

    [Fact]
    public void TmMultiColumnComboBox_AccentInsensitiveFilter_Disabled_Requires_Exact_Accents()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetCzechCities())
            .Add(c => c.ValueField, x => x.Id)
            .Add(c => c.TextField, x => x.Name)
            .Add(c => c.Columns, GetColumns())
            .Add(c => c.AccentInsensitiveFilter, false));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();
        cut.Find(".tm-multi-column-combo-box__filter input").Input("usti");

        cut.FindAll(".tm-multi-column-combo-box__tr").Should().BeEmpty();
        cut.Find(".tm-multi-column-combo-box__empty").Should().NotBeNull();
    }

    [Fact]
    public void TmMultiColumnComboBox_NoResults_Shown_When_Filter_Matches_Nothing()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns()));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();

        var filterInput = cut.Find(".tm-multi-column-combo-box__filter input");
        filterInput.Input("XYZ");

        cut.Find(".tm-multi-column-combo-box__empty").TextContent.Should().Contain("No results");
    }

    // ── K10: multi-select, create-new, recent ──────────────────────

    [Fact]
    public void TmMultiColumnComboBox_MultiSelect_Toggles_And_RaisesSelectedValuesChanged()
    {
        IReadOnlyList<int> selected = new List<int>();

        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns())
            .Add(c => c.MultiSelect, true)
            .Add(c => c.SelectedValues, selected)
            .Add(c => c.SelectedValuesChanged, v => selected = v));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();
        cut.FindAll(".tm-multi-column-combo-box__tr")[0].Click();

        selected.Should().BeEquivalentTo(new[] { 1 });

        // Simulate two-way binding (@bind-SelectedValues) before the second toggle.
        cut.Render(p => p.Add(c => c.SelectedValues, selected));
        cut.FindAll(".tm-multi-column-combo-box__tr")[1].Click();

        selected.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void TmMultiColumnComboBox_MultiSelect_Deselect_RemovesValue()
    {
        IReadOnlyList<int> selected = new List<int> { 1 };

        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns())
            .Add(c => c.MultiSelect, true)
            .Add(c => c.SelectedValues, selected)
            .Add(c => c.SelectedValuesChanged, v => selected = v));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();
        cut.FindAll(".tm-multi-column-combo-box__tr")[0].Click(); // Laptop (id 1) selected → toggle off

        selected.Should().BeEmpty();
    }

    [Fact]
    public void TmMultiColumnComboBox_MultiSelect_Shows_Chips_For_Selected()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns())
            .Add(c => c.MultiSelect, true)
            .Add(c => c.SelectedValues, new List<int> { 1, 3 }));

        cut.FindAll(".tm-multi-column-combo-box__chip").Should().HaveCount(2);
    }

    [Fact]
    public void TmMultiColumnComboBox_CreateNew_AddsAndSelectsItem()
    {
        int? value = null;

        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns())
            .Add(c => c.AllowCreateNew, true)
            .Add(c => c.DataProvider, new FakeProductProvider())
            .Add(c => c.ValueChanged, v => value = v));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();
        cut.Find(".tm-multi-column-combo-box__filter input").Input("Gadget");
        cut.Find(".tm-multi-column-combo-box__create").Click();

        value.Should().Be(99);
    }

    [Fact]
    public void TmMultiColumnComboBox_Recent_RendersItems_OnOpen()
    {
        var cut = Render<TmMultiColumnComboBox<Product, int>>(p => p
            .Add(c => c.Data, GetProducts())
            .Add(c => c.ValueField, p => p.Id)
            .Add(c => c.TextField, p => p.Name)
            .Add(c => c.Columns, GetColumns())
            .Add(c => c.ShowRecent, true)
            .Add(c => c.DataProvider, new FakeProductProvider()));

        cut.Find(".tm-multi-column-combo-box__trigger").Click();

        cut.WaitForState(() => cut.FindAll(".tm-multi-column-combo-box__recent-item").Count > 0);
        cut.Find(".tm-multi-column-combo-box__recent").TextContent.Should().Contain("Mouse");
    }
}
