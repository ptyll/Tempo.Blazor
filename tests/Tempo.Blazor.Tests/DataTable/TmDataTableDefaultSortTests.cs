using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Public so NSubstitute can proxy <c>IDataTableDataProvider&lt;DefaultSortPerson&gt;</c> for the
/// server-side cases; a nested private record is not accessible to the generated proxy assembly.
/// </summary>
public record DefaultSortPerson(string Name, int Age);

/// <summary>
/// Covers <c>DefaultSortColumn</c> / <c>DefaultSortDirection</c>: the order a table is in before the user
/// has touched a header.
/// <para>
/// Sorting is tri-state (ascending → descending → none) and started at "none", so a page whose list used to
/// arrive pre-sorted had no way to say so — not through a parameter, not through the layout store, which
/// carries widths and pins only. The remaining route was to fake a click, which is a different thing: it
/// leaves the cycle one step along, so the next real click clears the sort instead of reversing it.
/// </para>
/// <para>
/// So these tests pin down both halves: the starting order itself, and that clicking from it still cycles
/// the way a user expects.
/// </para>
/// </summary>
public class TmDataTableDefaultSortTests : LocalizationTestBase
{
    // Deliberately not in alphabetical order, so "sorted" cannot pass by accident on input order.
    private static List<DefaultSortPerson> People =>
    [
        new("Charlie", 30),
        new("Alice",   25),
        new("Bob",     35),
    ];

    private IRenderedComponent<TmDataTable<DefaultSortPerson>> RenderWithColumns(
        Action<ComponentParameterCollectionBuilder<TmDataTable<DefaultSortPerson>>>? extra = null,
        bool sortable = true)
        => Render<TmDataTable<DefaultSortPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<DefaultSortPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Sortable", sortable);
                b.AddAttribute(4, "Field", (Func<DefaultSortPerson, object?>)(x => x.Name));
                b.CloseComponent();
            });
            extra?.Invoke(p);
        });

    private static IReadOnlyList<string> FirstColumn(IRenderedComponent<TmDataTable<DefaultSortPerson>> cut)
        => cut.FindAll("tbody tr")
              .Select(r => r.QuerySelector("td")!.TextContent.Trim())
              .ToList();

    // ── The starting order ────────────────────────────────────────

    [Fact]
    public void DefaultSortColumn_SortsBeforeAnyClick()
    {
        var cut = RenderWithColumns(p => p.Add(c => c.DefaultSortColumn, "Name"));

        FirstColumn(cut).Should().Equal("Alice", "Bob", "Charlie");
    }

    [Fact]
    public void DefaultSortDirection_Descending_StartsDescending()
    {
        var cut = RenderWithColumns(p => p
            .Add(c => c.DefaultSortColumn, "Name")
            .Add(c => c.DefaultSortDirection, DataTableSortDirection.Descending));

        FirstColumn(cut).Should().Equal("Charlie", "Bob", "Alice");
    }

    [Fact]
    public void WithoutDefaultSortColumn_TableStartsInTheOrderItWasGiven()
    {
        // Guards the default: adding the parameter must not start sorting tables that never asked.
        var cut = RenderWithColumns();

        FirstColumn(cut).Should().Equal("Charlie", "Alice", "Bob");
    }

    [Fact]
    public void DefaultSortColumn_IsAnnouncedAsAriaSort_BeforeAnyClick()
    {
        var cut = RenderWithColumns(p => p.Add(c => c.DefaultSortColumn, "Name"));

        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("ascending");
    }

    [Fact]
    public void DefaultSortColumn_MarksTheHeaderSorted_BeforeAnyClick()
    {
        var cut = RenderWithColumns(p => p.Add(c => c.DefaultSortColumn, "Name"));

        cut.FindAll("th.tm-col-sorted-asc").Should().NotBeEmpty();
    }

    // ── Clicking on from the starting order ───────────────────────

    [Fact]
    public void DefaultAscending_FirstClickOnThatColumn_SortsDescending()
    {
        // The contract a page relying on a pre-sorted list depends on: because the table already counts as
        // ascending, one click reverses it. Seeding by faking a click would land on ascending instead.
        var cut = RenderWithColumns(p => p.Add(c => c.DefaultSortColumn, "Name"));

        cut.Find("th[data-sortable='true']").Click();

        FirstColumn(cut).Should().Equal("Charlie", "Bob", "Alice");
        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("descending");
    }

    [Fact]
    public void DefaultAscending_SecondClickOnThatColumn_ClearsTheSort()
    {
        var cut = RenderWithColumns(p => p.Add(c => c.DefaultSortColumn, "Name"));

        var header = cut.Find("th[data-sortable='true']");
        header.Click(); // descending
        header.Click(); // none

        FirstColumn(cut).Should().Equal("Charlie", "Alice", "Bob");
        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("none");
    }

    [Fact]
    public void DefaultDescending_FirstClickOnThatColumn_ClearsTheSort()
    {
        // Descending is the last step of the cycle, so a table that starts there has one click left.
        var cut = RenderWithColumns(p => p
            .Add(c => c.DefaultSortColumn, "Name")
            .Add(c => c.DefaultSortDirection, DataTableSortDirection.Descending));

        cut.Find("th[data-sortable='true']").Click();

        FirstColumn(cut).Should().Equal("Charlie", "Alice", "Bob");
    }

    // ── Degenerate inputs ─────────────────────────────────────────

    [Fact]
    public void DefaultSortColumn_UnknownKey_LeavesTheTableUnsorted()
    {
        var cut = RenderWithColumns(p => p.Add(c => c.DefaultSortColumn, "NoSuchColumn"));

        FirstColumn(cut).Should().Equal("Charlie", "Alice", "Bob");
    }

    [Fact]
    public void DefaultSortColumn_OnANonSortableColumn_StillOrdersTheData()
    {
        // "Fixed order the user cannot change": the rows are sorted, but the header offers no sort affordance.
        var cut = RenderWithColumns(p => p.Add(c => c.DefaultSortColumn, "Name"), sortable: false);

        FirstColumn(cut).Should().Equal("Alice", "Bob", "Charlie");

        // Since 2.8.22 the attribute is ABSENT rather than "none": ARIA reserves aria-sort for a column
        // that participates in sorting, so "none" on a header the user cannot operate announced an
        // affordance that does not exist. The assertion still measures the same promise — the header
        // offers no sort affordance — it just measures it where the promise now lives.
        cut.Find("th[data-sortable='false']").GetAttribute("aria-sort").Should().BeNull();
    }

    [Fact]
    public void DefaultSortColumn_OnANonSortableColumn_IsNotChangedByClickingTheHeader()
    {
        var cut = RenderWithColumns(p => p.Add(c => c.DefaultSortColumn, "Name"), sortable: false);

        cut.Find("th[data-sortable='false']").Click();

        FirstColumn(cut).Should().Equal("Alice", "Bob", "Charlie");
    }

    // ── Server-side ───────────────────────────────────────────────

    [Fact]
    public async Task DefaultSortColumn_ReachesTheVeryFirstProviderQuery()
    {
        // The point of seeding before the first load: a server-side table must not fetch page one in the
        // provider's order and then re-sort the fetched slice, which would show the wrong rows entirely.
        var provider = Substitute.For<IDataTableDataProvider<DefaultSortPerson>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new PagedResult<DefaultSortPerson>
                {
                    Items = [new("Alice", 25)],
                    TotalCount = 1,
                    Page = 1,
                    PageSize = 25,
                }));

        var cut = Render<TmDataTable<DefaultSortPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.DefaultSortColumn, "Name");
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<DefaultSortPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Sortable", true);
                b.AddAttribute(4, "Field", (Func<DefaultSortPerson, object?>)(x => x.Name));
                b.CloseComponent();
            });
        });

        await cut.InvokeAsync(() => { });

        await provider.Received().GetDataAsync(
            Arg.Is<DataTableQuery>(q => q.SortColumn == "Name" && !q.SortDescending),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DefaultSortDirection_Descending_ReachesTheProviderAsDescending()
    {
        var provider = Substitute.For<IDataTableDataProvider<DefaultSortPerson>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new PagedResult<DefaultSortPerson>
                {
                    Items = [new("Alice", 25)],
                    TotalCount = 1,
                    Page = 1,
                    PageSize = 25,
                }));

        var cut = Render<TmDataTable<DefaultSortPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.DefaultSortColumn, "Name");
            p.Add(c => c.DefaultSortDirection, DataTableSortDirection.Descending);
        });

        await cut.InvokeAsync(() => { });

        await provider.Received().GetDataAsync(
            Arg.Is<DataTableQuery>(q => q.SortColumn == "Name" && q.SortDescending),
            Arg.Any<CancellationToken>());
    }
}
