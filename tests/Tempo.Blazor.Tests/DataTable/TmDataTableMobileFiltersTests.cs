using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.DataTable;

public sealed class TmDataTableMobileFiltersTests : LocalizationTestBase
{
    private sealed record Person(string Name);

    [Fact]
    public void ShowFilterSummary_IsFalseByDefaultAndClearControlIsOptIn()
    {
        var cut = Render<TmDataTable<Person>>(p => p
            .Add(c => c.ViewContext, "people")
            .Add(c => c.Items, new[] { new Person("A") })
            .AddChildContent<TmDataTableColumn<Person>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.PropertyName, nameof(Person.Name))
                .Add(x => x.Filterable, true)));

        Assert.False(cut.Instance.ShowFilterSummary);
        Assert.DoesNotContain("tm-data-table-filter-summary", cut.Markup);
    }

    [Fact]
    public void ShowFilterSummary_RendersForActiveFilterOnlyWhenEnabled()
    {
        var enabled = Render<TmDataTable<Person>>(p => p
            .Add(c => c.ViewContext, "people-enabled")
            .Add(c => c.Items, new[] { new Person("A") })
            .Add(c => c.ShowFilterSummary, true)
            .AddChildContent<TmDataTableColumn<Person>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.PropertyName, nameof(Person.Name))
                .Add(x => x.Filterable, true)));
        enabled.InvokeAsync(() => enabled.FindComponent<TmColumnFilter>().Instance.OnFilterChange.InvokeAsync(
            new DataTableFilter(nameof(Person.Name), "contains", "A"))).GetAwaiter().GetResult();

        Assert.Contains("tm-data-table-filter-summary", enabled.Markup);

        var disabled = Render<TmDataTable<Person>>(p => p
            .Add(c => c.ViewContext, "people-disabled")
            .Add(c => c.Items, new[] { new Person("A") })
            .AddChildContent<TmDataTableColumn<Person>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.PropertyName, nameof(Person.Name))
                .Add(x => x.Filterable, true)));
        disabled.InvokeAsync(() => disabled.FindComponent<TmColumnFilter>().Instance.OnFilterChange.InvokeAsync(
            new DataTableFilter(nameof(Person.Name), "contains", "A"))).GetAwaiter().GetResult();

        Assert.DoesNotContain("tm-data-table-filter-summary", disabled.Markup);
    }
}
