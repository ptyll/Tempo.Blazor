using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Retro review of phase 11: the data-table filter-count summary must be a live region (11.5) and
/// its visibility must be decided by <c>ShouldRenderFilterSummary()</c> plus <c>ToolbarMode</c> (R18).
/// </summary>
public class TmDataTableFilterSummaryLiveRegionTests : LocalizationTestBase
{
    private static List<SummaryPerson> People =>
    [
        new("Alice", "Admin"),
        new("Bob", "User"),
    ];

    private static void AddFilterableColumn(ComponentParameterCollectionBuilder<TmDataTable<SummaryPerson>> p)
        => p.AddChildContent<TmDataTableColumn<SummaryPerson>>(col => col
            .Add(c => c.Title, "Name")
            .Add(c => c.PropertyName, nameof(SummaryPerson.Name))
            .Add(c => c.Filterable, true));

    [Fact]
    public void FilterSummary_Count_IsALiveRegion()
    {
        var cut = Render<TmDataTable<SummaryPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ShowFilterSummary, true);
            AddFilterableColumn(p);
        });

        cut.Find(".tm-col-filter input").Input("Alice");

        var liveCount = cut.Find(".tm-data-table-filter-summary [role='status'][aria-live='polite']");
        liveCount.TextContent.Should().Contain("Filters: 1");
    }

    [Fact]
    public void FilterSummary_LiveRegion_ExistsEmptyBeforeFiltersAndAnnouncesAfter()
    {
        var cut = Render<TmDataTable<SummaryPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ShowFilterSummary, true);
            AddFilterableColumn(p);
        });

        // The live region is in the DOM before any filter exists (and empty), so the first
        // filter change is actually announced instead of appearing together with its text.
        var live = cut.Find(".tm-data-table-filter-summary-live");
        live.GetAttribute("role").Should().Be("status");
        live.GetAttribute("aria-live").Should().Be("polite");
        live.TextContent.Trim().Should().BeEmpty();

        cut.Find(".tm-col-filter input").Input("Alice");

        cut.Find(".tm-data-table-filter-summary-live").TextContent.Should().Contain("Filters: 1");
    }

    [Theory]
    [InlineData(DataToolbarMode.Full, true)]
    [InlineData(DataToolbarMode.ActionsOnly, true)]
    [InlineData(DataToolbarMode.SearchOnly, false)]
    [InlineData(DataToolbarMode.ContentOnly, false)]
    public void FilterSummary_RendersOnlyInFullAndActionsToolbars(DataToolbarMode mode, bool expected)
    {
        var cut = Render<TmDataTable<SummaryPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ShowFilterSummary, true);
            p.Add(c => c.ToolbarMode, mode);
            AddFilterableColumn(p);
        });

        cut.Find(".tm-col-filter input").Input("Alice");

        cut.FindAll(".tm-data-table-filter-summary").Should().HaveCount(expected ? 1 : 0);
        cut.FindAll(".tm-data-table-filter-summary-live").Should().HaveCount(expected ? 1 : 0);
    }

    public sealed record SummaryPerson(string Name, string Role);
}
