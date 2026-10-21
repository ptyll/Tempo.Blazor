using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

internal record BasicPerson(string Name, int Age, string Role);

/// <summary>Basic rendering and interaction tests for TmDataTable.</summary>
public class TmDataTableBasicTests : LocalizationTestBase
{
    private static List<BasicPerson> People =>
    [
        new("Alice", 30, "Admin"),
        new("Bob",   25, "User"),
        new("Carol", 35, "Manager")
    ];

    [Fact]
    public void TmDataTable_Renders_Table_Element()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People));

        cut.Find("table").Should().NotBeNull();
    }

    [Fact]
    public void TmDataTable_Has_Base_CssClass()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People));

        cut.Find("table").ClassList.Should().Contain("tm-data-table");
    }

    [Fact]
    public void TmDataTable_Renders_Row_Per_Item()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People));

        // 3 data rows in tbody
        cut.FindAll("tbody tr").Count.Should().Be(3);
    }

    [Fact]
    public void TmDataTable_Loading_ShowsSkeletonRows_WithOneCellPerColumn()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.IsLoading, true)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Name").Add(x => x.Field, x => x.Name))
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c.Add(x => x.Title, "Age").Add(x => x.Field, x => x.Age)));

        cut.FindAll(".tm-spinner").Should().BeEmpty("the colspan spinner cell let the column widths jump");
        var table = cut.Find("table.tm-data-table");
        var rows = table.QuerySelectorAll("tr.tm-data-table-loading-row");
        rows.Length.Should().BeGreaterThan(0);
        var headerCellCount = table.QuerySelectorAll("thead th").Length;
        headerCellCount.Should().BeGreaterThan(0, $"markup: {cut.Markup[..Math.Min(600, cut.Markup.Length)]}");
        foreach (var row in rows)
        {
            row.QuerySelectorAll("td").Length.Should().Be(headerCellCount,
                "a skeleton row measures the same columns the data rows will");
            row.QuerySelectorAll(".tm-skeleton").Should().NotBeEmpty($"row HTML: {row.OuterHtml[..Math.Min(400, row.OuterHtml.Length)]}");
        }
    }

    [Fact]
    public void TmDataTable_Empty_Items_Shows_EmptyState()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson>())
            .Add(c => c.EmptyTitle, "No data found"));

        cut.FindAll(".tm-empty-state").Should().NotBeEmpty();
    }

    [Fact]
    public void TmDataTable_Empty_Items_ShowsIconAndLocalizedDescription()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson>())
            .Add(c => c.EmptyTitle, "No data found"));

        cut.Find(".tm-empty-state-icon").Should().NotBeNull("a bare text-only empty state was a UX review defect");
        cut.Find(".tm-empty-state-description").TextContent.Should().Be("No records to display.");
    }

    [Fact]
    public void TmDataTable_Renders_Column_Headers()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<BasicPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "Field", (Func<BasicPerson, object>)(x => x.Name));
                b.CloseComponent();
            }));

        cut.FindAll("thead th").Should().NotBeEmpty();
    }

    [Fact]
    public void TmDataTable_RowClick_Fires_OnRowClick()
    {
        BasicPerson? clicked = null;
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.OnRowClick, EventCallback.Factory.Create<BasicPerson>(this, p => clicked = p)));

        cut.FindAll("tbody tr").First().Click();

        clicked.Should().NotBeNull();
        clicked!.Name.Should().Be("Alice");
    }

    [Fact]
    public void TmDataTable_RowAttributes_Applies_CustomAttributes_ToRows()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.RowAttributes, row => new Dictionary<string, object>
            {
                ["data-testid"] = $"person-{row.Name.ToLowerInvariant()}",
                ["data-role"] = row.Role
            })
            .AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<BasicPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "Field", (Func<BasicPerson, object>)(x => x.Name));
                b.CloseComponent();
            }));

        var row = cut.Find("[data-testid='person-alice']");
        row.GetAttribute("data-role").Should().Be("Admin");
        row.TextContent.Should().Contain("Alice");
    }

    [Fact]
    public void TmDataTable_RowAttributes_Drops_ReservedNames_ToRows()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.RowAttributes, row => new Dictionary<string, object>
            {
                ["data-testid"] = $"person-{row.Name.ToLowerInvariant()}",
                ["class"] = "evil-injected",
                ["tabindex"] = "-99"
            })
            .AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<BasicPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "Field", (Func<BasicPerson, object>)(x => x.Name));
                b.CloseComponent();
            }));

        var row = cut.Find("[data-testid='person-alice']"); // non-reserved attribute still applied
        row.ClassList.Should().NotContain("evil-injected"); // reserved class dropped, table class not clobbered
        row.GetAttribute("tabindex").Should().NotBe("-99"); // reserved tabindex dropped, focus order not hijacked
    }
}
