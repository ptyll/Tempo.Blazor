using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public record GroupPerson(string Name, string Department, string Status, decimal Salary);

/// <summary>TDD tests for TmDataTable grouping functionality.</summary>
public class TmDataTableGroupingTests : LocalizationTestBase
{
    private static List<GroupPerson> People =>
    [
        new("Alice", "Engineering", "Active", 90000m),
        new("Bob", "Engineering", "Active", 85000m),
        new("Charlie", "Engineering", "Inactive", 70000m),
        new("Diana", "Marketing", "Active", 75000m),
        new("Eve", "Marketing", "Inactive", 65000m),
        new("Frank", "Sales", "Active", 80000m),
    ];

    private IRenderedComponent<TmDataTable<GroupPerson>> RenderGroupableTable(
        bool showGrouping = true,
        bool groupsCollapsedByDefault = true,
        EventCallback<IReadOnlyList<string>>? onGroupingChanged = null)
    {
        return RenderComponent<TmDataTable<GroupPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ShowGrouping, showGrouping);
            p.Add(c => c.GroupsCollapsedByDefault, groupsCollapsedByDefault);
            if (onGroupingChanged.HasValue)
                p.Add(c => c.OnGroupingChanged, onGroupingChanged.Value);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<GroupPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Field", (Func<GroupPerson, object?>)(x => x.Name));
                b.CloseComponent();

                b.OpenComponent<TmDataTableColumn<GroupPerson>>(10);
                b.AddAttribute(11, "Title", "Department");
                b.AddAttribute(12, "PropertyName", "Department");
                b.AddAttribute(13, "Field", (Func<GroupPerson, object?>)(x => x.Department));
                b.AddAttribute(14, "Groupable", true);
                b.CloseComponent();

                b.OpenComponent<TmDataTableColumn<GroupPerson>>(20);
                b.AddAttribute(21, "Title", "Status");
                b.AddAttribute(22, "PropertyName", "Status");
                b.AddAttribute(23, "Field", (Func<GroupPerson, object?>)(x => x.Status));
                b.AddAttribute(24, "Groupable", true);
                b.CloseComponent();

                b.OpenComponent<TmDataTableColumn<GroupPerson>>(30);
                b.AddAttribute(31, "Title", "Salary");
                b.AddAttribute(32, "PropertyName", "Salary");
                b.AddAttribute(33, "Field", (Func<GroupPerson, object?>)(x => x.Salary));
                b.CloseComponent();
            });
        });
    }

    // --- Drop Zone ---

    [Fact]
    public void DataTable_ShowGrouping_RendersDropZone()
    {
        var cut = RenderGroupableTable(showGrouping: true);
        cut.FindAll(".tm-data-table-group-zone").Should().HaveCount(1);
    }

    [Fact]
    public void DataTable_ShowGroupingFalse_NoDropZone()
    {
        var cut = RenderGroupableTable(showGrouping: false);
        cut.FindAll(".tm-data-table-group-zone").Should().BeEmpty();
    }

    [Fact]
    public void DataTable_GroupZone_ShowsPlaceholderWhenEmpty()
    {
        var cut = RenderGroupableTable();
        cut.FindAll(".tm-data-table-group-placeholder").Should().HaveCount(1);
    }

    // --- Adding group columns ---

    [Fact]
    public async Task DataTable_AddGroupColumn_ShowsGroupRows()
    {
        var cut = RenderGroupableTable(groupsCollapsedByDefault: true);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        cut.FindAll(".tm-data-table-group-row").Should().HaveCount(3);
    }

    [Fact]
    public async Task DataTable_GroupRow_ShowsCount()
    {
        var cut = RenderGroupableTable(groupsCollapsedByDefault: true);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        var groupRows = cut.FindAll(".tm-data-table-group-row");
        groupRows.Should().Contain(r => r.QuerySelector(".tm-data-table-group-count") != null);
    }

    [Fact]
    public async Task DataTable_GroupsCollapsedByDefault_True_HidesDataRows()
    {
        var cut = RenderGroupableTable(groupsCollapsedByDefault: true);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Should().BeEmpty();
    }

    [Fact]
    public async Task DataTable_GroupsCollapsedByDefault_False_ShowsDataRows()
    {
        var cut = RenderGroupableTable(groupsCollapsedByDefault: false);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        cut.FindAll(".tm-data-table-group-row").Should().HaveCount(3);
        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Should().HaveCount(6);
    }

    // --- Toggle expansion ---

    [Fact]
    public async Task DataTable_ToggleGroupExpansion_ExpandsCollapsedGroup()
    {
        var cut = RenderGroupableTable(groupsCollapsedByDefault: true);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        var toggleBtn = cut.Find(".tm-data-table-group-toggle");
        toggleBtn.Click();

        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DataTable_ToggleGroupExpansion_CollapsesExpandedGroup()
    {
        var cut = RenderGroupableTable(groupsCollapsedByDefault: false);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Count.Should().Be(6);

        var toggleBtn = cut.Find(".tm-data-table-group-toggle");
        toggleBtn.Click();

        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Count.Should().BeLessThan(6);
    }

    // --- Remove group column ---

    [Fact]
    public async Task DataTable_RemoveGroupColumn_UngroupsData()
    {
        var cut = RenderGroupableTable(groupsCollapsedByDefault: false);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));
        cut.FindAll(".tm-data-table-group-row").Should().HaveCount(3);

        await cut.InvokeAsync(() => cut.Instance.RemoveGroupColumn("Department"));

        cut.FindAll(".tm-data-table-group-row").Should().BeEmpty();
        cut.FindAll("tbody tr").Should().HaveCount(6);
    }

    // --- Multi-level grouping ---

    [Fact]
    public async Task DataTable_MultiLevelGrouping_ShowsNestedGroups()
    {
        var cut = RenderGroupableTable(groupsCollapsedByDefault: false);

        await cut.InvokeAsync(() =>
        {
            cut.Instance.AddGroupColumn("Department");
            cut.Instance.AddGroupColumn("Status");
        });

        cut.FindAll(".tm-data-table-group-level-0").Should().HaveCount(3);
        cut.FindAll(".tm-data-table-group-level-1").Count.Should().BeGreaterThan(0);
    }

    // --- Group zone chips ---

    [Fact]
    public async Task DataTable_GroupZone_ShowsChipsForGroupedColumns()
    {
        var cut = RenderGroupableTable();

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        cut.FindAll(".tm-data-table-group-chip").Should().HaveCount(1);
    }

    [Fact]
    public async Task DataTable_GroupZone_ChipRemoveButton_RemovesGrouping()
    {
        var cut = RenderGroupableTable(groupsCollapsedByDefault: false);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));
        cut.FindAll(".tm-data-table-group-row").Should().HaveCount(3);

        cut.Find(".tm-data-table-group-chip-remove").Click();

        cut.FindAll(".tm-data-table-group-row").Should().BeEmpty();
    }

    // --- Expand All / Collapse All ---

    [Fact]
    public async Task DataTable_GroupZone_ExpandAllButton_ExpandsAll()
    {
        var cut = RenderGroupableTable(groupsCollapsedByDefault: true);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Should().BeEmpty();

        cut.Find(".tm-data-table-group-expand-all").Click();

        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Should().HaveCount(6);
    }

    [Fact]
    public async Task DataTable_GroupZone_CollapseAllButton_CollapsesAll()
    {
        var cut = RenderGroupableTable(groupsCollapsedByDefault: false);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Should().HaveCount(6);

        cut.Find(".tm-data-table-group-collapse-all").Click();

        cut.FindAll("tbody tr:not(.tm-data-table-group-row)").Should().BeEmpty();
    }

    // --- Callback ---

    [Fact]
    public async Task DataTable_OnGroupingChanged_FiresCallback()
    {
        IReadOnlyList<string>? groupColumns = null;
        var callback = EventCallback.Factory.Create<IReadOnlyList<string>>(
            this, cols => groupColumns = cols);

        var cut = RenderGroupableTable(onGroupingChanged: callback);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        groupColumns.Should().NotBeNull();
        groupColumns.Should().Contain("Department");
    }

    // --- Group row toggle button ---

    [Fact]
    public async Task DataTable_GroupRow_HasToggleButton()
    {
        var cut = RenderGroupableTable(groupsCollapsedByDefault: true);

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        var toggleButtons = cut.FindAll(".tm-data-table-group-toggle");
        toggleButtons.Should().HaveCount(3);
    }

    // --- Server-side grouping ---

    [Fact]
    public async Task DataTable_Grouping_WithServerSide_SendsGroupByInQuery()
    {
        var provider = Substitute.For<IDataTableDataProvider<GroupPerson>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new PagedResult<GroupPerson>
                {
                    Items = People,
                    TotalCount = 6,
                    Page = 1,
                    PageSize = 25
                }));

        var cut = RenderComponent<TmDataTable<GroupPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.ShowGrouping, true);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<GroupPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Field", (Func<GroupPerson, object?>)(x => x.Name));
                b.CloseComponent();

                b.OpenComponent<TmDataTableColumn<GroupPerson>>(10);
                b.AddAttribute(11, "Title", "Department");
                b.AddAttribute(12, "PropertyName", "Department");
                b.AddAttribute(13, "Field", (Func<GroupPerson, object?>)(x => x.Department));
                b.AddAttribute(14, "Groupable", true);
                b.CloseComponent();
            });
        });

        await cut.InvokeAsync(() => { });

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        await provider.Received().GetDataAsync(
            Arg.Is<DataTableQuery>(q => q.GroupByColumns.Contains("Department")),
            Arg.Any<CancellationToken>());
    }
}
