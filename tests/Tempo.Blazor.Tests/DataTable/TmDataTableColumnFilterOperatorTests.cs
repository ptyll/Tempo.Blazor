using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmDataTableColumnFilterOperatorTests : LocalizationTestBase
{
    public record Person(string Name, int Age);

    [Fact]
    public async Task WithoutFilterOperator_QueryUsesContains()
    {
        DataTableQuery? seen = null;
        var provider = Substitute.For<IDataTableDataProvider<Person>>();
        provider.GetDataAsync(Arg.Do<DataTableQuery>(q => seen = q), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Person> { Items = [], TotalCount = 0, Page = 1, PageSize = 10 });

        var cut = Render<TmDataTable<Person>>(p => p
            .Add(c => c.DataProvider, provider)
            .AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<Person>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Filterable", true);
                b.AddAttribute(4, "Field", (Func<Person, object?>)(x => x.Name));
                b.CloseComponent();
            }));

        cut.Find("input.tm-col-filter-input").Input("Ada");
        await cut.InvokeAsync(() => { });

        seen!.Filters.Should().ContainSingle(f => f.Column == "Name" && f.Operator == "contains" && (string?)f.Value == "Ada");
    }

    [Fact]
    public async Task FilterOperatorEquals_IsForwardedToQueryAndInMemoryProvider()
    {
        DataTableQuery? seen = null;
        var people = new List<Person> { new("Ada", 30), new("Alan", 41) };
        var memory = new InMemoryDataProvider<Person>(people, new Dictionary<string, Func<Person, object?>> { ["Name"] = x => x.Name });
        var provider = Substitute.For<IDataTableDataProvider<Person>>();
        provider.GetDataAsync(Arg.Do<DataTableQuery>(q => seen = q), Arg.Any<CancellationToken>())
            .Returns(ci => memory.GetDataAsync(ci.Arg<DataTableQuery>()));

        var cut = Render<TmDataTable<Person>>(p => p
            .Add(c => c.DataProvider, provider)
            .AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<Person>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Filterable", true);
                b.AddAttribute(4, "FilterOperator", "equals");
                b.AddAttribute(5, "Field", (Func<Person, object?>)(x => x.Name));
                b.CloseComponent();
            }));

        cut.Find("input.tm-col-filter-input").Input("Ada");
        await cut.InvokeAsync(() => { });

        seen!.Filters.Should().ContainSingle(f => f.Operator == "equals");
        var page = await memory.GetDataAsync(seen);
        page.Items.Should().ContainSingle(p => p.Name == "Ada");
    }
}
