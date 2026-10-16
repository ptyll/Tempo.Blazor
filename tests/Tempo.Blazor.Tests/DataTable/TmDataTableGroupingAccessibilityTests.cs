using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmDataTableGroupingAccessibilityTests : LocalizationTestBase
{
    private static List<GroupPerson> People =>
    [
        new("Alice", "Engineering", "Active", 90000m),
        new("Diana", "Marketing", "Active", 75000m),
    ];

    private IRenderedComponent<TmDataTable<GroupPerson>> RenderTable(DataToolbarMode mode = DataToolbarMode.Full) =>
        Render<TmDataTable<GroupPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ShowGrouping, true);
            p.Add(c => c.GroupsCollapsedByDefault, true);
            p.Add(c => c.ToolbarMode, mode);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<GroupPerson>>(0);
                b.AddAttribute(1, "Title", "Department");
                b.AddAttribute(2, "PropertyName", "Department");
                b.AddAttribute(3, "Groupable", true);
                b.AddAttribute(4, "Field", (Func<GroupPerson, object?>)(x => x.Department));
                b.CloseComponent();
            });
        });

    [Fact]
    public async Task GroupToggle_HasExpandedStateAndLocalizedLabel()
    {
        var cut = RenderTable();
        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        var toggle = cut.Find(".tm-data-table-group-toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        toggle.GetAttribute("aria-label").Should().Be("Expand group Engineering");
        toggle.InnerHtml.Should().NotContain("▶");

        toggle.Click();
        cut.Find(".tm-data-table-group-toggle").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find(".tm-data-table-group-toggle").GetAttribute("aria-label").Should().Be("Collapse group Engineering");
    }

    [Fact]
    public async Task ChipRemove_HasLocalizedLabelAndIcon()
    {
        var cut = RenderTable();
        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Department"));

        var remove = cut.Find(".tm-data-table-group-chip-remove");
        remove.GetAttribute("aria-label").Should().Be("Remove grouping by Department");
        remove.TextContent.Should().NotContain("×");
    }

    [Fact]
    public async Task AddGrouping_AddsGroupWithoutDrag()
    {
        var cut = RenderTable();
        cut.Find(".tm-data-table-add-grouping button").Click();
        cut.Find(".tm-dropdown-item").Click();

        cut.WaitForAssertion(() => cut.FindAll(".tm-data-table-group-row").Should().NotBeEmpty());
    }

    [Fact]
    public void AddGrouping_HiddenInContentOnly()
    {
        var cut = RenderTable(DataToolbarMode.ContentOnly);
        cut.FindAll(".tm-data-table-add-grouping").Should().BeEmpty();
    }
}
