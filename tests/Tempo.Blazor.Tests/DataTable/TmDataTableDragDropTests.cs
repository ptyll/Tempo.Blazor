using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public record DndPerson(string Name, string Department, string Status);

public class TmDataTableDragDropTests : LocalizationTestBase
{
    private static List<DndPerson> People =>
    [
        new("Alice", "Engineering", "Active"),
        new("Bob", "Marketing", "Active"),
        new("Charlie", "Engineering", "Inactive"),
    ];

    private IRenderedComponent<TmDataTable<DndPerson>> RenderDndTable()
    {
        return RenderComponent<TmDataTable<DndPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ShowGrouping, true);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<DndPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Field", (Func<DndPerson, object?>)(x => x.Name));
                b.CloseComponent();

                b.OpenComponent<TmDataTableColumn<DndPerson>>(10);
                b.AddAttribute(11, "Title", "Department");
                b.AddAttribute(12, "PropertyName", "Department");
                b.AddAttribute(13, "Field", (Func<DndPerson, object?>)(x => x.Department));
                b.AddAttribute(14, "Groupable", true);
                b.CloseComponent();

                b.OpenComponent<TmDataTableColumn<DndPerson>>(20);
                b.AddAttribute(21, "Title", "Status");
                b.AddAttribute(22, "PropertyName", "Status");
                b.AddAttribute(23, "Field", (Func<DndPerson, object?>)(x => x.Status));
                b.AddAttribute(24, "Groupable", true);
                b.CloseComponent();
            });
        });
    }

    // --- Draggable column headers ---

    [Fact]
    public void DataTable_GroupableColumn_HasDraggableAttribute()
    {
        var cut = RenderDndTable();

        var headers = cut.FindAll("thead th");
        // Department (index 1) and Status (index 2) are groupable
        headers[1].GetAttribute("draggable").Should().Be("true");
        headers[2].GetAttribute("draggable").Should().Be("true");
    }

    [Fact]
    public void DataTable_NonGroupableColumn_NotDraggable()
    {
        var cut = RenderDndTable();

        var headers = cut.FindAll("thead th");
        // Name (index 0) is NOT groupable
        var draggable = headers[0].GetAttribute("draggable");
        (draggable == null || draggable == "false").Should().BeTrue();
    }

    [Fact]
    public void DataTable_GroupableColumn_HasGroupableClass()
    {
        var cut = RenderDndTable();

        var headers = cut.FindAll("thead th");
        headers[1].ClassList.Should().Contain("tm-col-groupable");
    }

    // --- Group zone ---

    [Fact]
    public void DataTable_GroupZone_VisibleWhenShowGrouping()
    {
        var cut = RenderDndTable();

        cut.FindAll(".tm-data-table-group-zone").Should().HaveCount(1);
    }

    [Fact]
    public void DataTable_GroupZone_ShowsPlaceholderWhenEmpty()
    {
        var cut = RenderDndTable();

        cut.FindAll(".tm-data-table-group-placeholder").Should().HaveCount(1);
    }

    // --- Drop behavior ---

    [Fact]
    public async Task DataTable_GroupZone_Drop_AddsColumn()
    {
        var cut = RenderDndTable();

        // Simulate drag start on Department header, then drop on group zone
        await cut.InvokeAsync(() =>
        {
            cut.Instance.OnColumnDragStart("Department");
        });

        cut.Find(".tm-data-table-group-zone").Drop();

        // Should have a group chip for Department
        cut.FindAll(".tm-data-table-group-chip").Should().HaveCount(1);
    }

    [Fact]
    public async Task DataTable_GroupZone_Drop_DuplicateIgnored()
    {
        var cut = RenderDndTable();

        // Add Department via drag
        await cut.InvokeAsync(() => cut.Instance.OnColumnDragStart("Department"));
        cut.Find(".tm-data-table-group-zone").Drop();

        // Try to add Department again
        await cut.InvokeAsync(() => cut.Instance.OnColumnDragStart("Department"));
        cut.Find(".tm-data-table-group-zone").Drop();

        // Should still have only 1 chip
        cut.FindAll(".tm-data-table-group-chip").Should().HaveCount(1);
    }

    // --- Chip reordering ---

    [Fact]
    public async Task DataTable_GroupChip_Draggable()
    {
        var cut = RenderDndTable();

        // Add two group columns
        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));
        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Status"));

        var chips = cut.FindAll(".tm-data-table-group-chip");
        chips.Should().HaveCount(2);
        chips[0].GetAttribute("draggable").Should().Be("true");
    }

    [Fact]
    public async Task DataTable_GroupChip_Remove_RemovesColumn()
    {
        var cut = RenderDndTable();

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));
        cut.FindAll(".tm-data-table-group-chip").Should().HaveCount(1);

        // Click remove button
        cut.Find(".tm-data-table-group-chip-remove").Click();

        cut.FindAll(".tm-data-table-group-chip").Should().BeEmpty();
    }
}
