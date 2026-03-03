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
        var cut = RenderComponent<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People));

        cut.Find("table").Should().NotBeNull();
    }

    [Fact]
    public void TmDataTable_Has_Base_CssClass()
    {
        var cut = RenderComponent<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People));

        cut.Find("table").ClassList.Should().Contain("tm-data-table");
    }

    [Fact]
    public void TmDataTable_Renders_Row_Per_Item()
    {
        var cut = RenderComponent<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People));

        // 3 data rows in tbody
        cut.FindAll("tbody tr").Count.Should().Be(3);
    }

    [Fact]
    public void TmDataTable_Loading_Shows_Spinner()
    {
        var cut = RenderComponent<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.IsLoading, true));

        cut.FindAll(".tm-spinner").Should().NotBeEmpty();
    }

    [Fact]
    public void TmDataTable_Empty_Items_Shows_EmptyState()
    {
        var cut = RenderComponent<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, new List<BasicPerson>())
            .Add(c => c.EmptyTitle, "No data found"));

        cut.FindAll(".tm-empty-state").Should().NotBeEmpty();
    }

    [Fact]
    public void TmDataTable_Renders_Column_Headers()
    {
        var cut = RenderComponent<TmDataTable<BasicPerson>>(p => p
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
        var cut = RenderComponent<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.OnRowClick, EventCallback.Factory.Create<BasicPerson>(this, p => clicked = p)));

        cut.FindAll("tbody tr").First().Click();

        clicked.Should().NotBeNull();
        clicked!.Name.Should().Be("Alice");
    }
}
