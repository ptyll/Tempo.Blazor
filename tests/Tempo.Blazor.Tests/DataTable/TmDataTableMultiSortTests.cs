using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>Multi-column sort: contract, in-memory provider, and component behavior.</summary>
public class TmDataTableMultiSortTests : LocalizationTestBase
{
    public record Person(string Name, string Dept, int Age);

    // ── Contract ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetEffectiveSortDescriptors_FallsBackToSingleColumn()
    {
        var query = new DataTableQuery { SortColumn = "Name", SortDescending = true };

        query.GetEffectiveSortDescriptors().Should().ContainSingle()
            .Which.Should().Be(new SortDescriptor("Name", DataTableSortDirection.Descending));
    }

    [Fact]
    public void GetEffectiveSortDescriptors_NoSort_ReturnsEmpty()
    {
        new DataTableQuery().GetEffectiveSortDescriptors().Should().BeEmpty();
    }

    [Fact]
    public void GetEffectiveSortDescriptors_PrefersDescriptorList()
    {
        var query = new DataTableQuery
        {
            SortColumn = "Ignored",
            SortDescriptors = [new SortDescriptor("Dept"), new SortDescriptor("Age", DataTableSortDirection.Descending)]
        };

        var effective = query.GetEffectiveSortDescriptors();
        effective.Should().HaveCount(2);
        effective[0].Should().Be(new SortDescriptor("Dept", DataTableSortDirection.Ascending));
        effective[1].Should().Be(new SortDescriptor("Age", DataTableSortDirection.Descending));
    }

    // ── In-memory provider ────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryProvider_MultiSort_AppliesThenBy()
    {
        var data = new List<Person>
        {
            new("Bob", "B", 30),
            new("Ann", "A", 40),
            new("Al", "A", 20)
        };
        var accessors = new Dictionary<string, Func<Person, object?>>
        {
            ["Dept"] = x => x.Dept,
            ["Age"] = x => x.Age
        };
        var provider = new InMemoryDataProvider<Person>(data, accessors);

        var result = await provider.GetDataAsync(new DataTableQuery
        {
            PageSize = 10,
            SortDescriptors = [new SortDescriptor("Dept"), new SortDescriptor("Age", DataTableSortDirection.Descending)]
        });

        // Dept asc, then Age desc: A/40, A/20, B/30
        result.Items.Select(p => p.Name).Should().Equal("Ann", "Al", "Bob");
    }

    [Fact]
    public async Task InMemoryProvider_LegacySingleSort_StillWorks()
    {
        var data = new List<Person> { new("Bob", "B", 30), new("Ann", "A", 40) };
        var accessors = new Dictionary<string, Func<Person, object?>> { ["Name"] = x => x.Name };
        var provider = new InMemoryDataProvider<Person>(data, accessors);

        var result = await provider.GetDataAsync(new DataTableQuery { PageSize = 10, SortColumn = "Name" });

        result.Items.Select(p => p.Name).Should().Equal("Ann", "Bob");
    }

    // ── Component ──────────────────────────────────────────────────────────────

    private static void AddColumn(RenderTreeBuilder b, ref int seq, string title, Func<Person, object?> field)
    {
        b.OpenComponent<TmDataTableColumn<Person>>(seq++);
        b.AddAttribute(seq++, "Title", title);
        b.AddAttribute(seq++, "PropertyName", title);
        b.AddAttribute(seq++, "Sortable", true);
        b.AddAttribute(seq++, "Field", field);
        b.CloseComponent();
    }

    private IRenderedComponent<TmDataTable<Person>> RenderTable(IDataTableDataProvider<Person>? provider = null, IEnumerable<Person>? items = null)
        => Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "multisort-test");
            if (provider is not null) p.Add(c => c.DataProvider, provider);
            if (items is not null) p.Add(c => c.Items, items);
            p.AddChildContent(b =>
            {
                var seq = 0;
                AddColumn(b, ref seq, "Name", x => x.Name);
                AddColumn(b, ref seq, "Dept", x => x.Dept);
                AddColumn(b, ref seq, "Age", x => x.Age);
            });
        });

    [Fact]
    public async Task ShiftClick_AddsSecondarySort_ProviderReceivesDescriptors()
    {
        var provider = Substitute.For<IDataTableDataProvider<Person>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new PagedResult<Person> { Items = [], TotalCount = 0, Page = 1, PageSize = 25 }));

        var cut = RenderTable(provider);
        await cut.InvokeAsync(() => { });

        cut.FindAll("th[data-sortable='true']")[0].Click();                                        // plain → Name asc
        await cut.InvokeAsync(() => { });
        cut.FindAll("th[data-sortable='true']")[1].Click(new MouseEventArgs { ShiftKey = true });  // shift → add Dept asc
        await cut.InvokeAsync(() => { });

        await provider.Received().GetDataAsync(
            Arg.Is<DataTableQuery>(q =>
                q.SortDescriptors.Count == 2 &&
                q.SortDescriptors[0].Column == "Name" &&
                q.SortDescriptors[1].Column == "Dept" &&
                q.SortColumn == "Name"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void MultiSort_ShowsPrecedenceBadges()
    {
        var cut = RenderTable(items:
        [
            new Person("Bob", "B", 30),
            new Person("Ann", "A", 40),
            new Person("Al", "A", 20)
        ]);

        cut.FindAll("th[data-sortable='true']")[1].Click();                                        // Dept primary
        cut.FindAll("th[data-sortable='true']")[2].Click(new MouseEventArgs { ShiftKey = true });  // Age secondary

        cut.FindAll(".tm-sort-order").Should().HaveCount(2);
        cut.FindAll(".tm-sort-order")[0].TextContent.Trim().Should().Be("1");
        cut.FindAll(".tm-sort-order")[1].TextContent.Trim().Should().Be("2");
    }

    /// <summary>
    /// ARIA allows at most ONE directional <c>aria-sort</c> claim per table — under multi-sort every
    /// participating header used to announce its own direction, which is out of contract (carry-forward
    /// from the 20C UX review). Only the PRIMARY descriptor (index 0) announces
    /// ascending/descending; a secondary sort key reads <c>none</c> — sortable, not the column
    /// controlling the sort. Its precedence badge (aria-hidden) and the button's next-action name
    /// still carry the detail.
    /// </summary>
    [Fact]
    public void MultiSort_OnlyPrimaryHeaderClaimsAriaSortDirection()
    {
        var cut = RenderTable(items:
        [
            new Person("Bob", "B", 30),
            new Person("Ann", "A", 40),
            new Person("Al", "A", 20)
        ]);

        cut.FindAll("th[data-sortable='true']")[1].Click();                                       // Dept primary asc
        cut.FindAll("th[data-sortable='true']")[2].Click(new MouseEventArgs { ShiftKey = true });  // Age secondary asc

        cut.FindAll("th[data-sortable='true']")
           .Select(h => h.GetAttribute("aria-sort"))
           .Should().Equal(new[] { "none", "ascending", "none" },
               "aria-sort is a single-claim attribute — the secondary sort key must not announce " +
               "a second direction; it reads 'none' like any sortable, non-controlling column");
    }

    [Fact]
    public void ClientMode_MultiSort_OrdersRows()
    {
        var cut = RenderTable(items:
        [
            new Person("Bob", "B", 30),
            new Person("Ann", "A", 40),
            new Person("Al", "A", 20)
        ]);

        cut.FindAll("th[data-sortable='true']")[1].Click();                                        // Dept asc
        cut.FindAll("th[data-sortable='true']")[2].Click(new MouseEventArgs { ShiftKey = true });  // Age asc (secondary)

        var rows = cut.FindAll("tbody tr");
        // Dept asc, Age asc: A/20 (Al), A/40 (Ann), B/30 (Bob)
        rows[0].QuerySelector("td")!.TextContent.Trim().Should().Be("Al");
        rows[1].QuerySelector("td")!.TextContent.Trim().Should().Be("Ann");
        rows[2].QuerySelector("td")!.TextContent.Trim().Should().Be("Bob");
    }
}
