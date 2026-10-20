using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>Tests for TmDataTable toolbar chrome visibility (ShowToolbar, ShowViewManager).</summary>
public class TmDataTableToolbarTests : LocalizationTestBase
{
    private record ToolbarPerson(string Name, int Age, string Role);

    private static RenderFragment Columns => b =>
    {
        b.OpenComponent<TmDataTableColumn<ToolbarPerson>>(0);
        b.AddAttribute(1, "Title", "Name");
        b.AddAttribute(2, "Field", (Func<ToolbarPerson, object>)(x => x.Name));
        b.CloseComponent();
    };

    private static List<ToolbarPerson> People =>
    [
        new("Alice", 30, "Admin"),
        new("Bob", 25, "User"),
        new("Carol", 35, "Manager")
    ];

    private static IDataTableViewProvider CreateViewProvider() => Substitute.For<IDataTableViewProvider>();

    [Fact]
    public void TmDataTable_ShowToolbar_False_HidesToolbarAndControls()
    {
        var cut = Render<TmDataTable<ToolbarPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ShowToolbar, false));

        cut.FindAll(".tm-data-table-toolbar").Should().BeEmpty();
        cut.FindAll(".tm-input-search").Should().BeEmpty();
        cut.FindAll(".tm-column-picker").Should().BeEmpty();
        cut.FindAll(".tm-view-manager").Should().BeEmpty();
    }

    [Fact]
    public void TmDataTable_ShowToolbar_True_WithNoVisibleControls_DoesNotRenderEmptyToolbar()
    {
        var cut = Render<TmDataTable<ToolbarPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ShowToolbar, true)
            .Add(c => c.ShowSearch, false)
            .Add(c => c.ShowColumnPicker, false)
            .Add(c => c.ShowViewManager, false));

        cut.FindAll(".tm-data-table-toolbar").Should().BeEmpty();
    }

    [Fact]
    public void TmDataTable_ShowViewManager_False_HidesViewManager()
    {
        var provider = CreateViewProvider();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));

        var cut = Render<TmDataTable<ToolbarPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ViewProvider, provider)
            .Add(c => c.ViewContext, "test-context")
            .Add(c => c.ShowViewManager, false));

        cut.FindAll(".tm-view-manager").Should().BeEmpty();
    }

    [Fact]
    public void TmDataTable_ShowToolbar_True_WithControls_RendersToolbar()
    {
        var cut = Render<TmDataTable<ToolbarPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ShowToolbar, true));

        cut.FindAll(".tm-data-table-toolbar").Should().ContainSingle();
        cut.FindAll(".tm-input-search").Should().ContainSingle();
    }

    [Theory]
    [InlineData(DataToolbarMode.Full, true, true)]
    [InlineData(DataToolbarMode.SearchOnly, true, false)]
    [InlineData(DataToolbarMode.ActionsOnly, false, true)]
    [InlineData(DataToolbarMode.ContentOnly, false, false)]
    public void TmDataTable_ToolbarMode_RendersExpectedSearchAndColumnPicker(DataToolbarMode mode, bool expectSearch, bool expectColumnPicker)
    {
        var cut = Render<TmDataTable<ToolbarPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ToolbarMode, mode)
            .AddChildContent(Columns));

        cut.FindAll(".tm-input-search").Any().Should().Be(expectSearch);
        cut.FindAll(".tm-column-picker").Any().Should().Be(expectColumnPicker);
    }

    [Fact]
    public void TmDataTable_ToolbarMode_ContentOnly_HidesExternalFilterBuilder()
    {
        var provider = CreateViewProvider();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));

        var cut = Render<TmDataTable<ToolbarPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ViewContext, "test-context")
            .Add(c => c.ViewProvider, provider)
            .Add(c => c.ExternalFilterDefinitions, [new FilterDefinition { FieldName = "Name", FieldLabel = "Name" }])
            .Add(c => c.ToolbarMode, DataToolbarMode.ContentOnly));

        cut.FindAll(".tm-data-table-toolbar").Should().BeEmpty();
        cut.FindAll(".tm-input-search").Should().BeEmpty();
        cut.FindAll(".tm-view-manager").Should().BeEmpty();
        cut.FindAll(".tm-data-table-external-filters").Should().BeEmpty();
    }

    [Fact]
    public void TmDataTable_ToolbarMode_ActionsOnly_RendersColumnPickerAndViewManagerButNotSearch()
    {
        var provider = CreateViewProvider();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));

        var cut = Render<TmDataTable<ToolbarPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ViewContext, "test-context")
            .Add(c => c.ViewProvider, provider)
            .Add(c => c.ToolbarMode, DataToolbarMode.ActionsOnly)
            .AddChildContent(Columns));

        cut.FindAll(".tm-input-search").Should().BeEmpty();
        cut.FindAll(".tm-column-picker").Should().ContainSingle();
        cut.FindAll(".tm-view-manager").Should().ContainSingle();
    }

    [Fact]
    public void TmDataTable_ToolbarMode_Full_WithProviderAndExternalFilters_RendersAllChrome()
    {
        var provider = CreateViewProvider();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));

        var cut = Render<TmDataTable<ToolbarPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ViewContext, "test-context")
            .Add(c => c.ViewProvider, provider)
            .Add(c => c.ExternalFilterDefinitions, [new FilterDefinition { FieldName = "Name", FieldLabel = "Name" }])
            .AddChildContent(Columns));

        cut.FindAll(".tm-input-search").Should().ContainSingle();
        cut.FindAll(".tm-column-picker").Should().ContainSingle();
        cut.FindAll(".tm-view-manager").Should().ContainSingle();
        cut.FindAll(".tm-data-table-external-filters").Should().ContainSingle();
    }

    [Fact]
    public void ClearAllFilters_EmptiesColumnFilterInputs()
    {
        var cut = Render<TmDataTable<ToolbarPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ShowFilterSummary, true)
            .AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<ToolbarPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Filterable", true);
                b.AddAttribute(4, "Field", (Func<ToolbarPerson, object?>)(x => x.Name));
                b.CloseComponent();
            }));

        cut.Find("input.tm-col-filter-input").Input("Ali");
        cut.Find("input.tm-col-filter-input").GetAttribute("value").Should().Be("Ali");

        cut.Find(".tm-col-filter-clear-all").Click();

        cut.Find("input.tm-col-filter-input").GetAttribute("value").Should().BeEmpty();
    }
}
