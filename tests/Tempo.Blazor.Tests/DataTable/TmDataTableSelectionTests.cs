using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

internal record SelPerson(string Name, string Role);

public class TmDataTableSelectionTests : LocalizationTestBase
{
    private static List<SelPerson> People =>
    [
        new("Alice", "Admin"),
        new("Bob",   "User"),
        new("Carol", "Manager"),
    ];

    [Fact]
    public void DataTable_Selectable_ShowsCheckboxColumn()
    {
        var cut = RenderComponent<TmDataTable<SelPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.Selectable, true));

        cut.FindAll("tbody tr td input[type='checkbox']").Should().NotBeEmpty();
    }

    [Fact]
    public void DataTable_CheckRow_AddsToSelection()
    {
        IReadOnlyList<SelPerson>? selected = null;
        var cut = RenderComponent<TmDataTable<SelPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.Selectable, true)
            .Add(c => c.OnSelectionChanged,
                EventCallback.Factory.Create<IReadOnlyList<SelPerson>>(this, s => selected = s)));

        // Check the first row checkbox
        cut.FindAll("tbody tr td input[type='checkbox']").First().Change(true);

        selected.Should().NotBeNull();
        selected!.Count.Should().Be(1);
        selected[0].Name.Should().Be("Alice");
    }

    [Fact]
    public void DataTable_CheckAllHeader_SelectsAllVisibleRows()
    {
        IReadOnlyList<SelPerson>? selected = null;
        var cut = RenderComponent<TmDataTable<SelPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.Selectable, true)
            .Add(c => c.OnSelectionChanged,
                EventCallback.Factory.Create<IReadOnlyList<SelPerson>>(this, s => selected = s)));

        // Header checkbox = select all
        cut.Find("thead input[type='checkbox']").Change(true);

        selected.Should().NotBeNull();
        selected!.Count.Should().Be(3);
    }

    [Fact]
    public void DataTable_OnSelectionChanged_FiresWithSelectedItems()
    {
        IReadOnlyList<SelPerson>? lastSelection = null;
        var cut = RenderComponent<TmDataTable<SelPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.Selectable, true)
            .Add(c => c.OnSelectionChanged,
                EventCallback.Factory.Create<IReadOnlyList<SelPerson>>(this, s => lastSelection = s)));

        cut.FindAll("tbody tr td input[type='checkbox']")[0].Change(true);
        cut.FindAll("tbody tr td input[type='checkbox']")[1].Change(true);

        lastSelection.Should().NotBeNull();
        lastSelection!.Count.Should().Be(2);
    }

    [Fact]
    public void DataTable_DeselectAll_ClearsSelection()
    {
        IReadOnlyList<SelPerson>? lastSelection = null;
        var cut = RenderComponent<TmDataTable<SelPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.Selectable, true)
            .Add(c => c.OnSelectionChanged,
                EventCallback.Factory.Create<IReadOnlyList<SelPerson>>(this, s => lastSelection = s)));

        // Select all
        cut.Find("thead input[type='checkbox']").Change(true);
        lastSelection!.Count.Should().Be(3);

        // Click "Deselect all" button in selection bar
        cut.Find(".tm-data-table-selection-bar button").Click();

        lastSelection.Count.Should().Be(0);
    }

    [Fact]
    public void DataTable_SelectionBar_ShowsCount()
    {
        var cut = RenderComponent<TmDataTable<SelPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.Selectable, true));

        // Select 2 rows
        cut.FindAll("tbody tr td input[type='checkbox']")[0].Change(true);
        cut.FindAll("tbody tr td input[type='checkbox']")[1].Change(true);

        // Selection bar should appear with "2 selected"
        cut.Find(".tm-data-table-selection-bar").TextContent.Should().Contain("2");
    }
}
