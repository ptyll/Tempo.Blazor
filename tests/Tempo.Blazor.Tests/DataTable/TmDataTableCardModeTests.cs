using Bunit;
using FluentAssertions;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>Public so NSubstitute can proxy <c>IDataTableDataProvider&lt;CardPerson&gt;</c>.</summary>
public record CardPerson(string Name, int Age);

/// <summary>
/// Opt-in responsive card-mode (A2). Card-mode is OFF by default so the 9 existing
/// consumers render byte-identically; ON, each data cell carries a <c>data-label</c>
/// (from the column Title) and the wrapper gains a card class the scoped CSS keys off.
/// </summary>
public class TmDataTableCardModeTests : LocalizationTestBase
{
    private static List<CardPerson> People => [new("Alice", 30), new("Bob", 25)];

    private static void AddColumns(Bunit.ComponentParameterCollectionBuilder<TmDataTable<CardPerson>> p)
    {
        p.Add(c => c.Items, People);
        p.AddChildContent(b =>
        {
            b.OpenComponent<TmDataTableColumn<CardPerson>>(0);
            b.AddAttribute(1, "Title", "Name");
            b.AddAttribute(2, "Field", (Func<CardPerson, object?>)(x => x.Name));
            b.CloseComponent();
            b.OpenComponent<TmDataTableColumn<CardPerson>>(3);
            b.AddAttribute(4, "Title", "Age");
            b.AddAttribute(5, "Field", (Func<CardPerson, object?>)(x => x.Age));
            b.CloseComponent();
        });
    }

    [Fact]
    public void CardMode_Off_ByDefault_NoDataLabelOnCells_AndNoCardClass()
    {
        var cut = Render<TmDataTable<CardPerson>>(AddColumns);

        cut.FindAll("tbody td[data-label]").Should().BeEmpty(
            "card-mode is off by default, so no data-label is emitted (byte-identical to today)");
        cut.Find(".tm-data-table-wrapper").ClassList.Should().NotContain("tm-data-table-wrapper--card");
    }

    [Fact]
    public void CardMode_On_EmitsDataLabelFromColumnTitle()
    {
        var cut = Render<TmDataTable<CardPerson>>(p =>
        {
            AddColumns(p);
            p.Add(c => c.CardMode, true);
        });

        var firstRowCells = cut.FindAll("tbody tr")[0].QuerySelectorAll("td[data-label]");
        firstRowCells.Should().HaveCount(2);
        firstRowCells[0].GetAttribute("data-label").Should().Be("Name");
        firstRowCells[1].GetAttribute("data-label").Should().Be("Age");
    }

    [Fact]
    public void CardMode_On_AddsWrapperCardClass()
    {
        var cut = Render<TmDataTable<CardPerson>>(p =>
        {
            AddColumns(p);
            p.Add(c => c.CardMode, true);
        });

        cut.Find(".tm-data-table-wrapper").ClassList.Should().Contain("tm-data-table-wrapper--card");
    }

    [Fact]
    public void CardMode_Css_HasScopedCardTransform_WithDataLabelContent()
    {
        var css = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_data-table.css"));

        css.Should().MatchRegex(@"@container tm-data-table \(width < 640px\)",
            "card transform must be scoped to the table's own width, not the viewport");
        css.Should().Contain("tm-data-table-wrapper--card",
            "card CSS must be scoped to the opt-in wrapper class so the 9 consumers are unaffected");
        css.Should().Contain("attr(data-label)",
            "cards render the column label via the data-label attribute");
    }

    [Fact]
    public async Task CardMode_On_CoexistsWith_ServerSort_ViaDataProvider()
    {
        var provider = Substitute.For<IDataTableDataProvider<CardPerson>>();
        provider.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new PagedResult<CardPerson>
                {
                    Items = People,
                    TotalCount = 2,
                    Page = 1,
                    PageSize = 25,
                }));

        var cut = Render<TmDataTable<CardPerson>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.CardMode, true);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<CardPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Sortable", true);
                b.AddAttribute(4, "Field", (Func<CardPerson, object?>)(x => x.Name));
                b.CloseComponent();
            });
        });

        await cut.InvokeAsync(() => { });

        // Card-mode still emits the data-label…
        cut.FindAll("tbody td[data-label]").Should().NotBeEmpty();

        // …and server-side sort (DataProvider + SortDescriptors) still fires on header click.
        cut.Find("th[data-sortable='true']").Click();
        await cut.InvokeAsync(() => { });

        await provider.Received().GetDataAsync(
            Arg.Is<DataTableQuery>(q => q.SortColumn == "Name"),
            Arg.Any<CancellationToken>());
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the repository root should be discoverable from the test output directory");
        return directory!.FullName;
    }
}
