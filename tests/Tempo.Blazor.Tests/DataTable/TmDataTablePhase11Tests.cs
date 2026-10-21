using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Resources;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Components.Filters;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Phase 11 (performance, ToolbarMode, a11y): the indeterminate flag is sent to JS only when it
/// changes, a column filter and the filter builder share one writer, and the result count is announced.
/// </summary>
public class TmDataTablePhase11Tests : LocalizationTestBase
{
    private static List<SelPerson> People =>
    [
        new("Alice", "Admin"),
        new("Bob", "User"),
        new("Carol", "Manager"),
    ];

    [Fact]
    public void SelectAll_Indeterminate_IsSentToJsOnlyWhenItChanges()
    {
        var cut = Render<TmDataTable<SelPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.Selectable, true));

        // The first render reports the initial (not mixed) state, and that is the only call.
        JSInterop.VerifyInvoke("tmDataTable.setIndeterminate", 1);

        // A second render that does not touch the selection must not pay the interop again.
        cut.Render();
        JSInterop.Invocations
            .Count(call => call.Identifier == "tmDataTable.setIndeterminate")
            .Should().Be(1);
    }

    [Fact]
    public async Task ColumnFilter_AndFilterBuilder_WriteTheSameQuery()
    {
        DataTableQuery? fromColumn = null;
        DataTableQuery? fromBuilder = null;

        IDataTableDataProvider<SelPerson> Provider()
        {
            var provider = Substitute.For<IDataTableDataProvider<SelPerson>>();
            provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
                .Returns(new PagedResult<SelPerson> { Items = People, TotalCount = People.Count });
            return provider;
        }

        var column = Render<TmDataTable<SelPerson>>(p => p
            .Add(c => c.DataProvider, Provider())
            .AddChildContent<TmDataTableColumn<SelPerson>>(col => col
                .Add(c => c.Title, "Name")
                .Add(c => c.PropertyName, nameof(SelPerson.Name))
                .Add(c => c.Filterable, true)));
        column.FindComponent<TmColumnFilter>().Find("input").Input("Alice");
        fromColumn = LastQuery(column);

        var builder = Render<TmDataTable<SelPerson>>(p => p
            .Add(c => c.DataProvider, Provider())
            .Add(c => c.ViewProvider, Substitute.For<IDataTableViewProvider>())
            .Add(c => c.ExternalFilterDefinitions, [new FilterDefinition
            {
                FieldName = nameof(SelPerson.Name),
                FieldLabel = "Name",
                FieldType = FilterFieldType.Text,
            }])
            .AddChildContent<TmDataTableColumn<SelPerson>>(col => col
                .Add(c => c.Title, "Name")
                .Add(c => c.PropertyName, nameof(SelPerson.Name))
                .Add(c => c.Filterable, true)));
        var filterBuilder = builder.FindComponent<TmFilterBuilder>();
        await builder.InvokeAsync(() => filterBuilder.Instance.OnFiltersChanged.InvokeAsync(
            [new ActiveFilter(nameof(SelPerson.Name), "Name", FilterOperator.Contains, "Alice", "Alice")]));
        fromBuilder = LastQuery(builder);

        fromColumn!.Filters.Should().ContainSingle();
        fromBuilder!.Filters.Should().ContainSingle();
        // The column filter writes the operator as the enum's lower-case name, the builder writes the
        // enum's own name. The writer is the same; only that spelling differs.
        fromColumn.Filters[0].Column.Should().Be(fromBuilder.Filters[0].Column);
        fromColumn.Filters[0].Value.Should().Be(fromBuilder.Filters[0].Value);
        fromColumn.Filters[0].Operator.Should().BeEquivalentTo(fromBuilder.Filters[0].Operator);
    }

    [Fact]
    public void FilterSummary_DoesNotAnnounce_TheLiveCountDoes()
    {
        var cut = Render<TmDataTable<SelPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ShowFilterSummary, true)
            .AddChildContent<TmDataTableColumn<SelPerson>>(col => col
                .Add(c => c.Title, "Name")
                .Add(c => c.PropertyName, nameof(SelPerson.Name))
                .Add(c => c.Filterable, true)));

        cut.Find(".tm-col-filter input").Input("Alice");

        // Two polite regions on one filter change used to speak twice. The always-present live
        // count already covers the announcement, so the summary stays silent.
        var summary = cut.Find(".tm-data-table-filter-summary");
        summary.HasAttribute("role").Should().BeFalse();
        summary.HasAttribute("aria-live").Should().BeFalse();

        var live = cut.Find(".tm-data-table-live-count");
        live.GetAttribute("aria-live").Should().Be("polite");
    }

    [Fact]
    public void FilterSummary_Count_IsALiveRegion()
    {
        var cut = Render<TmDataTable<SelPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ShowFilterSummary, true)
            .AddChildContent<TmDataTableColumn<SelPerson>>(col => col
                .Add(c => c.Title, "Name")
                .Add(c => c.PropertyName, nameof(SelPerson.Name))
                .Add(c => c.Filterable, true)));

        cut.Find(".tm-col-filter input").Input("Alice");

        // 11.5 retro: the plural filter count itself is a polite live region, so adding or
        // removing a filter is announced as "Filters: N".
        var live = cut.Find(".tm-data-table-filter-summary [role='status'][aria-live='polite']");
        live.TextContent.Should().Contain("Filters: 1");
    }

    [Fact]
    public void CardToolbar_TriggersShareTheSmallOutlineButton()
    {
        var cut = Render<TmDataTable<SelPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.Appearance, DataTableAppearance.Card)
            .Add(c => c.ShowExport, true)
            .Add(c => c.ViewProvider, Substitute.For<IDataTableViewProvider>())
            .AddChildContent<TmDataTableColumn<SelPerson>>(col => col
                .Add(c => c.Title, "Name")
                .Add(c => c.PropertyName, nameof(SelPerson.Name))
                .Add(c => c.Hideable, true)));

        // Choose columns, Views and Export used to render at three different heights.
        foreach (var trigger in new[]
        {
            cut.Find(".tm-column-picker-toggle"),
            cut.Find(".tm-view-manager-toggle"),
            cut.Find(".tm-data-table__export .tm-dropdown-trigger"),
        })
        {
            trigger.ClassList.Should().Contain("tm-btn");
            trigger.ClassList.Should().Contain("tm-btn-outline");
            trigger.ClassList.Should().Contain("tm-btn-sm");
        }
    }

    [Fact]
    public void SaveCurrent_ModalSubmit_ReadsSaveAsCopy()
    {
        var cut = Render<TmDataTable<SelPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ViewProvider, Substitute.For<IDataTableViewProvider>())
            .AddChildContent<TmDataTableColumn<SelPerson>>(col => col
                .Add(c => c.Title, "Name")
                .Add(c => c.PropertyName, nameof(SelPerson.Name))));

        cut.Find(".tm-view-manager-toggle").Click();
        cut.FindAll("button").First(b => b.TextContent.Contains("Save Current as New", StringComparison.Ordinal)).Click();

        var submit = cut.Find(".tm-view-modal-footer .tm-btn-primary");
        submit.TextContent.Should().Contain("Save as copy");
        submit.TextContent.Should().NotContain("Save changes");
    }

    [Theory]
    [InlineData("cs", "Uložit jako kopii")]
    [InlineData("sk", "Uložiť ako kópiu")]
    [InlineData("fr", "Enregistrer une copie")]
    public void SaveAsCopy_ResolvesInEveryShippedLanguage(string culture, string expected)
    {
        using var _ = CultureScope.Use(culture);
        var localizer = new DefaultTmLocalizer(new JsonStringLocalizer<TmResources>());

        localizer["TmViewManager_SaveAsCopy"].Should().Be(expected);
    }

    [Fact]
    public void FilterSummary_IsHiddenInContentOnlyMode()
    {
        var cut = Render<TmDataTable<SelPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ShowFilterSummary, true)
            .Add(c => c.ToolbarMode, DataToolbarMode.ContentOnly)
            .AddChildContent<TmDataTableColumn<SelPerson>>(col => col
                .Add(c => c.Title, "Name")
                .Add(c => c.PropertyName, nameof(SelPerson.Name))
                .Add(c => c.Filterable, true)));

        cut.Find(".tm-col-filter input").Input("Alice");

        cut.FindAll(".tm-data-table-filter-summary").Should().BeEmpty(
            "ContentOnly hands the filter chrome to the page, so the summary must not render");
    }

    [Fact]
    public void CzechPlurals_Inflect_One_Few_Many()
    {
        var czech = LocalizationTestBase.BuildCzechLocalizer();

        czech.Plural("TmDataTable_BulkActions", 1).Should().Be("1 vybrán");
        czech.Plural("TmDataTable_BulkActions", 3).Should().Be("3 vybrány");
        czech.Plural("TmDataTable_BulkActions", 5).Should().Be("5 vybráno");

        czech.Plural("TmMvl_ItemCount", 1).Should().Be("1 položka");
        czech.Plural("TmMvl_ItemCount", 3).Should().Be("3 položky");
        czech.Plural("TmMvl_ItemCount", 5).Should().Be("5 položek");
    }

    private static DataTableQuery LastQuery(IRenderedComponent<TmDataTable<SelPerson>> cut)
    {
        var provider = cut.Instance.DataProvider!;
        return provider.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IDataTableDataProvider<SelPerson>.GetDataAsync))
            .Select(call => (DataTableQuery)call.GetArguments()[0]!)
            .Last();
    }
}
