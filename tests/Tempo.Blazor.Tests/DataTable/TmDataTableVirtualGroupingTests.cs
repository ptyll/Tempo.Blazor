using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public record VirtualGroupPerson(string Name, string Department, string Status, decimal Salary);

public class TmDataTableVirtualGroupingTests : LocalizationTestBase
{
    private static List<VirtualGroupPerson> People =>
    [
        new("Alice", "Engineering", "Active", 90000m),
        new("Bob", "Engineering", "Active", 85000m),
        new("Charlie", "Engineering", "Inactive", 70000m),
        new("Diana", "Marketing", "Active", 75000m),
        new("Eve", "Marketing", "Inactive", 65000m),
        new("Frank", "Sales", "Active", 80000m),
    ];

    private IRenderedComponent<TmDataTable<VirtualGroupPerson>> RenderVirtualGroupTable(
        bool groupsCollapsedByDefault = true)
    {
        return RenderComponent<TmDataTable<VirtualGroupPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ScrollMode, DataTableScrollMode.Virtualized);
            p.Add(c => c.VirtualScrollHeight, "600px");
            p.Add(c => c.ShowGrouping, true);
            p.Add(c => c.GroupsCollapsedByDefault, groupsCollapsedByDefault);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<VirtualGroupPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Field", (Func<VirtualGroupPerson, object?>)(x => x.Name));
                b.CloseComponent();

                b.OpenComponent<TmDataTableColumn<VirtualGroupPerson>>(10);
                b.AddAttribute(11, "Title", "Department");
                b.AddAttribute(12, "PropertyName", "Department");
                b.AddAttribute(13, "Field", (Func<VirtualGroupPerson, object?>)(x => x.Department));
                b.AddAttribute(14, "Groupable", true);
                b.CloseComponent();

                b.OpenComponent<TmDataTableColumn<VirtualGroupPerson>>(20);
                b.AddAttribute(21, "Title", "Status");
                b.AddAttribute(22, "PropertyName", "Status");
                b.AddAttribute(23, "Field", (Func<VirtualGroupPerson, object?>)(x => x.Status));
                b.AddAttribute(24, "Groupable", true);
                b.CloseComponent();

                b.OpenComponent<TmDataTableColumn<VirtualGroupPerson>>(30);
                b.AddAttribute(31, "Title", "Salary");
                b.AddAttribute(32, "PropertyName", "Salary");
                b.AddAttribute(33, "Field", (Func<VirtualGroupPerson, object?>)(x => x.Salary));
                b.CloseComponent();
            });
        });
    }

    // --- Virtualized grouping: collapsed ---

    [Fact]
    public async Task DataTable_VirtualizedGrouping_Collapsed_ShowsOnlyGroupHeaders()
    {
        var cut = RenderVirtualGroupTable(groupsCollapsedByDefault: true);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        // Should show 3 group header rows (Engineering, Marketing, Sales)
        cut.FindAll(".tm-data-table-group-row").Should().HaveCount(3);
        // No data rows visible
        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Should().BeEmpty();
    }

    // --- Virtualized grouping: expanded ---

    [Fact]
    public async Task DataTable_VirtualizedGrouping_Expanded_ShowsHeadersAndItems()
    {
        var cut = RenderVirtualGroupTable(groupsCollapsedByDefault: false);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        // 3 group rows + 6 data rows
        cut.FindAll(".tm-data-table-group-row").Should().HaveCount(3);
        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Should().HaveCount(6);
    }

    // --- Toggle expansion in virtualized mode ---

    [Fact]
    public async Task DataTable_VirtualizedGrouping_ExpandGroup_ShowsItems()
    {
        var cut = RenderVirtualGroupTable(groupsCollapsedByDefault: true);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        // Click first group toggle to expand
        var toggleBtn = cut.Find(".tm-data-table-group-toggle");
        toggleBtn.Click();

        // Now we should have some data rows visible
        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Count.Should().BeGreaterThan(0);
    }

    // --- Expand all / Collapse all in virtualized mode ---

    [Fact]
    public async Task DataTable_VirtualizedGrouping_ExpandAll_ShowsAllItems()
    {
        var cut = RenderVirtualGroupTable(groupsCollapsedByDefault: true);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));
        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Should().BeEmpty();

        cut.Find(".tm-data-table-group-expand-all").Click();

        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Should().HaveCount(6);
    }

    [Fact]
    public async Task DataTable_VirtualizedGrouping_CollapseAll_HidesAllItems()
    {
        var cut = RenderVirtualGroupTable(groupsCollapsedByDefault: false);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));
        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Should().HaveCount(6);

        cut.Find(".tm-data-table-group-collapse-all").Click();

        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Should().BeEmpty();
    }

    // --- No pagination in virtualized grouped mode ---

    [Fact]
    public async Task DataTable_VirtualizedGrouping_HidesPagination()
    {
        var cut = RenderVirtualGroupTable(groupsCollapsedByDefault: false);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        cut.FindAll(".tm-pagination-container").Should().BeEmpty();
    }

    // --- Multi-level grouping in virtualized mode ---

    [Fact]
    public async Task DataTable_VirtualizedGrouping_MultiLevel_ShowsNestedGroups()
    {
        var cut = RenderVirtualGroupTable(groupsCollapsedByDefault: false);

        await cut.InvokeAsync(() =>
        {
            cut.Instance.AddGroupColumn("Department");
            cut.Instance.AddGroupColumn("Status");
        });

        cut.FindAll(".tm-data-table-group-level-0").Should().HaveCount(3);
        cut.FindAll(".tm-data-table-group-level-1").Count.Should().BeGreaterThan(0);
    }
}
