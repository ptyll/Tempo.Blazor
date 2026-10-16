using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmColumnFilterTests : LocalizationTestBase
{
    [Fact]
    public void ColumnFilter_Text_RendersTextInput()
    {
        var cut = Render<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Name")
            .Add(c => c.FilterType, FilterType.Text));

        cut.Find("input[type='text']").Should().NotBeNull();
    }

    [Fact]
    public void ColumnFilter_Number_RendersNumberInput()
    {
        var cut = Render<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Score")
            .Add(c => c.FilterType, FilterType.Number));

        cut.Find("input[type='number']").Should().NotBeNull();
    }

    [Fact]
    public void ColumnFilter_Date_RendersDateInput()
    {
        var cut = Render<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "CreatedAt")
            .Add(c => c.FilterType, FilterType.Date));

        cut.Find("input[type='date']").Should().NotBeNull();
    }

    [Fact]
    public void ColumnFilter_Boolean_RendersSelectWithYesNo()
    {
        var cut = Render<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "IsActive")
            .Add(c => c.FilterType, FilterType.Boolean));

        var select = cut.Find("select.tm-col-filter-select");
        select.Should().NotBeNull();
        var options = select.QuerySelectorAll("option");
        options.Length.Should().BeGreaterThanOrEqualTo(3); // blank + Yes + No
    }

    [Fact]
    public void ColumnFilter_Select_RendersDropdownWithOptions()
    {
        var options = new List<SelectOption<string>>
        {
            new() { Value = "admin", Label = "Admin" },
            new() { Value = "user",  Label = "User" },
        };

        var cut = Render<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Role")
            .Add(c => c.FilterType, FilterType.Select)
            .Add(c => c.FilterOptions, options));

        var select = cut.Find("select.tm-col-filter-select");
        // blank option + 2 items = 3
        select.QuerySelectorAll("option").Length.Should().Be(3);
    }

    [Fact]
    public void ColumnFilter_OnChange_FiresCallbackWithFilter()
    {
        DataTableFilter? received = null;
        var cut = Render<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Name")
            .Add(c => c.FilterType, FilterType.Text)
            .Add(c => c.OnFilterChange,
                EventCallback.Factory.Create<DataTableFilter?>(this, f => received = f)));

        cut.Find("input[type='text']").Input("Alice"); // live: column text filter applies on input

        received.Should().NotBeNull();
        received!.Column.Should().Be("Name");
        received.Operator.Should().Be("contains");
        received.Value.Should().Be("Alice");
    }

    [Fact]
    public void ColumnFilter_Clear_FiresCallbackWithNull()
    {
        DataTableFilter? received = new DataTableFilter("Name", "contains", "x");
        var cut = Render<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Name")
            .Add(c => c.FilterType, FilterType.Text)
            .Add(c => c.OnFilterChange,
                EventCallback.Factory.Create<DataTableFilter?>(this, f => received = f)));

        // Type something, then clear
        cut.Find("input[type='text']").Input("Alice"); // live: column text filter applies on input
        cut.Find(".tm-col-filter-clear").Click();

        received.Should().BeNull();
    }

    [Fact]
    public void ColumnFilter_ColumnTitle_SetsAriaLabelAndPlaceholder()
    {
        var cut = Render<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Name")
            .Add(c => c.ColumnTitle, "Jméno")
            .Add(c => c.FilterType, FilterType.Text));

        var input = cut.Find("input");
        input.GetAttribute("aria-label").Should().Be("Filter: Jméno");
        input.GetAttribute("placeholder").Should().Be("Filter: Jméno");
    }

    [Fact]
    public void ColumnFilter_WithoutColumnTitle_KeepsGenericFilterLabel()
    {
        var cut = Render<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Name")
            .Add(c => c.FilterType, FilterType.Text));

        cut.Find("input").GetAttribute("aria-label").Should().Be("Filter");
        cut.Find("input").GetAttribute("placeholder").Should().Be("Filter");
    }

    [Fact]
    public void ColumnFilter_ControlledValue_OverwritesDisplayedText()
    {
        var cut = Render<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Name")
            .Add(c => c.Value, "from-parent"));

        cut.Find("input").GetAttribute("value").Should().Be("from-parent");

        cut.Render(p => p
            .Add(c => c.ColumnKey, "Name")
            .Add(c => c.Value, ""));

        cut.Find("input").GetAttribute("value").Should().BeEmpty();
    }

    [Fact]
    public void ColumnFilter_WithoutValue_KeepsTypedText()
    {
        var cut = Render<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Name"));

        cut.Find("input").Input("Alice");
        cut.Find("input").GetAttribute("value").Should().Be("Alice");
    }

    [Fact]
    public void ColumnFilter_Select_ShowsClearButton()
    {
        var cut = Render<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Role")
            .Add(c => c.FilterType, FilterType.Select)
            .Add(c => c.FilterOptions, new List<SelectOption<string>> { new() { Value = "admin", Label = "Admin" } })
            .Add(c => c.Value, "admin"));

        cut.Find(".tm-col-filter-clear").Should().NotBeNull();
    }

    [Fact]
    public void ColumnFilter_FilterOperator_Equals_IsForwarded()
    {
        DataTableFilter? received = null;
        var cut = Render<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Name")
            .Add(c => c.FilterOperator, "equals")
            .Add(c => c.OnFilterChange, EventCallback.Factory.Create<DataTableFilter?>(this, f => received = f)));

        cut.Find("input").Input("Alice");

        received!.Operator.Should().Be("equals");
    }

    [Fact]
    public void ColumnFilter_WithoutFilterOperator_UsesContains()
    {
        DataTableFilter? received = null;
        var cut = Render<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Name")
            .Add(c => c.OnFilterChange, EventCallback.Factory.Create<DataTableFilter?>(this, f => received = f)));

        cut.Find("input").Input("Alice");

        received!.Operator.Should().Be("contains");
    }
}
