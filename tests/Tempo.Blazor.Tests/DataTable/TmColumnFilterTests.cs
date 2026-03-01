using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmColumnFilterTests : LocalizationTestBase
{
    [Fact]
    public void ColumnFilter_Text_RendersTextInput()
    {
        var cut = RenderComponent<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Name")
            .Add(c => c.FilterType, FilterType.Text));

        cut.Find("input[type='text']").Should().NotBeNull();
    }

    [Fact]
    public void ColumnFilter_Number_RendersNumberInput()
    {
        var cut = RenderComponent<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Score")
            .Add(c => c.FilterType, FilterType.Number));

        cut.Find("input[type='number']").Should().NotBeNull();
    }

    [Fact]
    public void ColumnFilter_Date_RendersDateInput()
    {
        var cut = RenderComponent<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "CreatedAt")
            .Add(c => c.FilterType, FilterType.Date));

        cut.Find("input[type='date']").Should().NotBeNull();
    }

    [Fact]
    public void ColumnFilter_Boolean_RendersSelectWithYesNo()
    {
        var cut = RenderComponent<TmColumnFilter>(p => p
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

        var cut = RenderComponent<TmColumnFilter>(p => p
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
        var cut = RenderComponent<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Name")
            .Add(c => c.FilterType, FilterType.Text)
            .Add(c => c.OnFilterChange,
                EventCallback.Factory.Create<DataTableFilter?>(this, f => received = f)));

        cut.Find("input[type='text']").Change("Alice");

        received.Should().NotBeNull();
        received!.Column.Should().Be("Name");
        received.Operator.Should().Be("contains");
        received.Value.Should().Be("Alice");
    }

    [Fact]
    public void ColumnFilter_Clear_FiresCallbackWithNull()
    {
        DataTableFilter? received = new DataTableFilter("Name", "contains", "x");
        var cut = RenderComponent<TmColumnFilter>(p => p
            .Add(c => c.ColumnKey, "Name")
            .Add(c => c.FilterType, FilterType.Text)
            .Add(c => c.OnFilterChange,
                EventCallback.Factory.Create<DataTableFilter?>(this, f => received = f)));

        // Type something, then clear
        cut.Find("input[type='text']").Change("Alice");
        cut.Find(".tm-col-filter-clear").Click();

        received.Should().BeNull();
    }
}
