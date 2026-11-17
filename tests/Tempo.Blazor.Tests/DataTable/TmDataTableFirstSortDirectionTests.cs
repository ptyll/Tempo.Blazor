using Bunit;
using FluentAssertions;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Fáze 7: the first click on a sortable column has always sorted ASCENDING.
/// <c>FirstSortDirection</c> (table default; the column's nullable override of the same name
/// wins when set) restores the legacy grids that start at DESCENDING. The tri-state cycle
/// becomes none → first → flipped → none; defaults are unchanged.
/// </summary>
public class TmDataTableFirstSortDirectionTests : LocalizationTestBase
{
    public record SortPerson(string Name, int Age);

    private static List<SortPerson> People => [new("Alice", 30), new("Bob", 25)];

    private IDataTableDataProvider<SortPerson> Provider()
    {
        var provider = Substitute.For<IDataTableDataProvider<SortPerson>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PagedResult<SortPerson>
            {
                Items = People, TotalCount = 2, Page = 1, PageSize = 25,
            }));
        return provider;
    }

    private (IRenderedComponent<TmDataTable<SortPerson>> Cut, IDataTableDataProvider<SortPerson> Provider) RenderWithSortableColumn(
        Action<ComponentParameterCollectionBuilder<TmDataTable<SortPerson>>>? configureTable = null,
        Action<ComponentParameterCollectionBuilder<TmDataTableColumn<SortPerson>>>? configureColumn = null)
    {
        var provider = Provider();
        var cut = Render<TmDataTable<SortPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            configureTable?.Invoke(p);
            p.AddChildContent<TmDataTableColumn<SortPerson>>(c =>
            {
                c.Add(x => x.Title, "Name")
                 .Add(x => x.PropertyName, "Name")
                 .Add(x => x.Sortable, true)
                 .Add(x => x.Field, (Func<SortPerson, object?>)(x => x.Name));
                configureColumn?.Invoke(c);
            });
        });
        return (cut, provider);
    }

    private static DataTableQuery? LastQuery(IDataTableDataProvider<SortPerson> provider) =>
        provider.ReceivedCalls()
            .Select(c => c.GetArguments()[0] as DataTableQuery)
            .LastOrDefault(q => q is not null);

    [Fact]
    public async Task Default_FirstClick_SortsAscending()
    {
        var (cut, provider) = RenderWithSortableColumn();
        await cut.InvokeAsync(() => cut.Find("th[data-sortable='true']").Click());

        LastQuery(provider)!.SortDescending.Should().BeFalse();
    }

    [Fact]
    public async Task FirstSortDirection_Descending_FirstClick_SortsDescending()
    {
        var (cut, provider) = RenderWithSortableColumn(p => p.Add(c => c.FirstSortDirection, DataTableSortDirection.Descending));
        await cut.InvokeAsync(() => cut.Find("th[data-sortable='true']").Click());

        LastQuery(provider)!.SortDescending.Should().BeTrue();
    }

    [Fact]
    public async Task ColumnFirstSortDirection_Descending_WinsOverTheTableDefault()
    {
        var (cut, provider) = RenderWithSortableColumn(
            configureColumn: c => c.Add(x => x.FirstSortDirection, DataTableSortDirection.Descending));
        await cut.InvokeAsync(() => cut.Find("th[data-sortable='true']").Click());

        LastQuery(provider)!.SortDescending.Should().BeTrue();
    }

    [Fact]
    public async Task ColumnFirstSortDirection_Ascending_WinsOverADescendingTable()
    {
        var (cut, provider) = RenderWithSortableColumn(
            p => p.Add(c => c.FirstSortDirection, DataTableSortDirection.Descending),
            configureColumn: c => c.Add(x => x.FirstSortDirection, DataTableSortDirection.Ascending));
        await cut.InvokeAsync(() => cut.Find("th[data-sortable='true']").Click());

        LastQuery(provider)!.SortDescending.Should().BeFalse();
    }

    [Fact]
    public async Task DescendingFirst_Cycle_IsDescendingThenAscendingThenClear()
    {
        var (cut, provider) = RenderWithSortableColumn(p => p.Add(c => c.FirstSortDirection, DataTableSortDirection.Descending));
        var header = () => cut.Find("th[data-sortable='true']");

        await cut.InvokeAsync(() => header().Click());
        LastQuery(provider)!.SortDescending.Should().BeTrue("first click = descending");

        await cut.InvokeAsync(() => header().Click());
        LastQuery(provider)!.SortDescending.Should().BeFalse("second click = ascending");

        await cut.InvokeAsync(() => header().Click());
        LastQuery(provider)!.SortColumn.Should().BeNullOrEmpty("third click clears the sort");
    }

    [Fact]
    public async Task DefaultCycle_IsUnchanged_AscendingThenDescendingThenClear()
    {
        var (cut, provider) = RenderWithSortableColumn();
        var header = () => cut.Find("th[data-sortable='true']");

        await cut.InvokeAsync(() => header().Click());
        LastQuery(provider)!.SortDescending.Should().BeFalse();

        await cut.InvokeAsync(() => header().Click());
        LastQuery(provider)!.SortDescending.Should().BeTrue();

        await cut.InvokeAsync(() => header().Click());
        LastQuery(provider)!.SortColumn.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task SortButton_NextAction_FollowsTheEffectiveCycle()
    {
        var (cut, _) = RenderWithSortableColumn(
            configureColumn: c => c.Add(x => x.FirstSortDirection, DataTableSortDirection.Descending));

        cut.Find(".tm-th-sort").GetAttribute("aria-label")
            .Should().Contain("descending", "the next activation of an unsorted descending-first column sorts descending");
    }
}
