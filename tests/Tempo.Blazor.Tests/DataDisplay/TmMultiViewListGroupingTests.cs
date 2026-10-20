using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

public record MvlGroupItem(string Id, string Name, string Department, string Status);

public class TmMultiViewListGroupingTests : LocalizationTestBase
{
    private static List<MvlGroupItem> Items =>
    [
        new("1", "Alice", "Engineering", "Active"),
        new("2", "Bob", "Engineering", "Active"),
        new("3", "Charlie", "Marketing", "Inactive"),
        new("4", "Diana", "Marketing", "Active"),
        new("5", "Eve", "Sales", "Active"),
    ];

    private static IReadOnlyList<GroupFieldDefinition<MvlGroupItem>> GroupFields =>
    [
        new()
        {
            FieldName = "Department",
            Label = "Department",
            FieldAccessor = x => x.Department
        },
        new()
        {
            FieldName = "Status",
            Label = "Status",
            FieldAccessor = x => x.Status
        }
    ];

    private IRenderedComponent<TmMultiViewList<MvlGroupItem>> RenderWithGrouping(
        ListViewMode viewMode = ListViewMode.Table,
        bool groupsCollapsedByDefault = true)
    {
        return Render<TmMultiViewList<MvlGroupItem>>(p =>
        {
            p.Add(c => c.Items, Items);
            p.Add(c => c.ViewMode, viewMode);
            p.Add(c => c.TitleField, x => x.Name);
            p.Add(c => c.SubTitleField, x => x.Department);
            p.Add(c => c.GroupableFields, GroupFields);
            p.Add(c => c.GroupsCollapsedByDefault, groupsCollapsedByDefault);
        });
    }

    // --- Group picker ---

    [Fact]
    public void MultiViewList_GroupableFields_ShowsGroupPicker()
    {
        var cut = RenderWithGrouping();

        cut.FindAll(".tm-mvl-group-picker").Should().HaveCount(1);
    }

    [Fact]
    public void MultiViewList_NoGroupableFields_NoGroupPicker()
    {
        var cut = Render<TmMultiViewList<MvlGroupItem>>(p =>
        {
            p.Add(c => c.Items, Items);
            p.Add(c => c.TitleField, x => x.Name);
        });

        cut.FindAll(".tm-mvl-group-picker").Should().BeEmpty();
    }

    [Fact]
    public void MultiViewList_GroupPicker_ShowsDropdownOnClick()
    {
        var cut = RenderWithGrouping();

        cut.Find(".tm-mvl-group-btn").Click();

        cut.FindAll(".tm-mvl-group-dropdown").Should().HaveCount(1);
    }

    // --- Table view grouping ---

    [Fact]
    public void MultiViewList_TableView_GroupToggle_IsChevronOnlyCompactButton()
    {
        var cut = RenderWithGrouping();

        cut.Find(".tm-mvl-group-btn").Click();
        cut.FindAll(".tm-mvl-group-dropdown input[type='checkbox']").First().Change(true);

        var toggle = cut.Find(".tm-mvl-group-toggle");
        toggle.QuerySelector("svg").Should().NotBeNull();
        toggle.TextContent.Trim().Should().BeEmpty();
        toggle.QuerySelector(".tm-mvl-group-label").Should().BeNull();
        toggle.QuerySelector(".tm-mvl-group-count").Should().BeNull();
        toggle.HasAttribute("aria-controls").Should().BeFalse();
        toggle.ParentElement!.QuerySelector(".tm-mvl-group-label").Should().NotBeNull();
        toggle.ParentElement!.QuerySelector(".tm-mvl-group-count").Should().NotBeNull();
        toggle.ParentElement!.ClassList.Contains("tm-mvl-group-card").Should().BeFalse();
    }

    [Fact]
    public void MultiViewList_TableView_GroupByDepartment_ShowsGroupRows()
    {
        var cut = RenderWithGrouping(groupsCollapsedByDefault: true);

        // Open picker and select Department
        cut.Find(".tm-mvl-group-btn").Click();
        var checkboxes = cut.FindAll(".tm-mvl-group-dropdown input[type='checkbox']");
        checkboxes.First().Change(true);

        // Should show 3 group rows (Engineering, Marketing, Sales)
        cut.FindAll(".tm-mvl-group-row").Should().HaveCount(3);
    }

    [Fact]
    public void MultiViewList_TableView_Collapsed_HidesDataRows()
    {
        var cut = RenderWithGrouping(groupsCollapsedByDefault: true);

        cut.Find(".tm-mvl-group-btn").Click();
        cut.FindAll(".tm-mvl-group-dropdown input[type='checkbox']").First().Change(true);

        // Data rows should be hidden when collapsed
        cut.FindAll(".tm-mvl-row").Should().BeEmpty();
    }

    [Fact]
    public void MultiViewList_TableView_Expanded_ShowsDataRows()
    {
        var cut = RenderWithGrouping(groupsCollapsedByDefault: false);

        cut.Find(".tm-mvl-group-btn").Click();
        cut.FindAll(".tm-mvl-group-dropdown input[type='checkbox']").First().Change(true);

        // All 5 data rows should be visible + 3 group rows
        cut.FindAll(".tm-mvl-group-row").Should().HaveCount(3);
        cut.FindAll(".tm-mvl-row").Should().HaveCount(5);
    }

    // --- Card view grouping ---

    [Fact]
    public void MultiViewList_CardView_GroupByDepartment_ShowsGroupSections()
    {
        var cut = RenderWithGrouping(viewMode: ListViewMode.Card, groupsCollapsedByDefault: false);

        cut.Find(".tm-mvl-group-btn").Click();
        cut.FindAll(".tm-mvl-group-dropdown input[type='checkbox']").First().Change(true);

        var sections = cut.FindAll(".tm-mvl-group-section");
        sections.Should().HaveCount(3);
        sections.Should().OnlyContain(section => section.ClassList.Contains("tm-mvl-group-card"));

        var headers = cut.FindAll(".tm-mvl-group-toggle");
        headers.Should().HaveCount(3);
        headers.Should().OnlyContain(header => !string.IsNullOrWhiteSpace(header.GetAttribute("aria-controls")));
        headers.Should().OnlyContain(header => header.ParentElement!.ParentElement!.ClassList.Contains("tm-mvl-group-card"));
        headers.Select(header => header.GetAttribute("aria-controls"))
            .Should().OnlyContain(controlledId => !string.IsNullOrWhiteSpace(controlledId) && cut.FindAll($"#{controlledId}").Count == 1);
    }

    // --- List view grouping ---

    [Fact]
    public void MultiViewList_ListView_GroupByDepartment_ShowsGroupSections()
    {
        var cut = RenderWithGrouping(viewMode: ListViewMode.List, groupsCollapsedByDefault: false);

        cut.Find(".tm-mvl-group-btn").Click();
        cut.FindAll(".tm-mvl-group-dropdown input[type='checkbox']").First().Change(true);

        cut.FindAll(".tm-mvl-group-section").Should().HaveCount(3);
    }

    // --- Toggle expansion ---

    [Fact]
    public void MultiViewList_ToggleGroupExpansion_ShowsItems()
    {
        var cut = RenderWithGrouping(groupsCollapsedByDefault: true);

        cut.Find(".tm-mvl-group-btn").Click();
        cut.FindAll(".tm-mvl-group-dropdown input[type='checkbox']").First().Change(true);

        // Click first group toggle
        cut.Find(".tm-mvl-group-toggle").Click();

        // Some data rows should now be visible
        cut.FindAll(".tm-mvl-row").Count.Should().BeGreaterThan(0);
    }

    // --- Callback ---

    [Fact]
    public void MultiViewList_OnGroupingChanged_FiresCallback()
    {
        IReadOnlyList<string>? groupCols = null;
        var cut = Render<TmMultiViewList<MvlGroupItem>>(p =>
        {
            p.Add(c => c.Items, Items);
            p.Add(c => c.TitleField, x => x.Name);
            p.Add(c => c.GroupableFields, GroupFields);
            p.Add(c => c.OnGroupingChanged, EventCallback.Factory.Create<IReadOnlyList<string>>(
                this, cols => groupCols = cols));
        });

        cut.Find(".tm-mvl-group-btn").Click();
        cut.FindAll(".tm-mvl-group-dropdown input[type='checkbox']").First().Change(true);

        groupCols.Should().NotBeNull();
        groupCols.Should().Contain("Department");
    }

    // --- Group count display ---

    [Fact]
    public void MultiViewList_GroupRow_ShowsCount()
    {
        var cut = RenderWithGrouping(groupsCollapsedByDefault: true);

        cut.Find(".tm-mvl-group-btn").Click();
        cut.FindAll(".tm-mvl-group-dropdown input[type='checkbox']").First().Change(true);

        var groupRows = cut.FindAll(".tm-mvl-group-row");
        groupRows.Should().Contain(r => r.QuerySelector(".tm-mvl-group-count") != null);
    }

    // --- Deselect grouping ---

    [Fact]
    public void MultiViewList_DeselectGrouping_RemovesGroups()
    {
        var cut = RenderWithGrouping(groupsCollapsedByDefault: false);

        // Enable grouping
        cut.Find(".tm-mvl-group-btn").Click();
        cut.FindAll(".tm-mvl-group-dropdown input[type='checkbox']").First().Change(true);
        cut.FindAll(".tm-mvl-group-row").Should().HaveCount(3);

        // Disable grouping
        cut.Find(".tm-mvl-group-btn").Click();
        cut.FindAll(".tm-mvl-group-dropdown input[type='checkbox']").First().Change(false);
        cut.FindAll(".tm-mvl-group-row").Should().BeEmpty();
    }
}
