using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Filters;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Filters;

public class TmFilterBuilderTests : LocalizationTestBase
{
    private static FilterDefinition TextFilter => new()
    {
        FieldName  = "name",
        FieldLabel = "Name",
        FieldType  = FilterFieldType.Text,
    };

    private static FilterDefinition SelectFilter => new()
    {
        FieldName  = "status",
        FieldLabel = "Status",
        FieldType  = FilterFieldType.Select,
        Options    = [new SelectOption<string>("active", "Active"), new SelectOption<string>("inactive", "Inactive")],
    };

    [Fact]
    public void FilterBuilder_AddFilter_ShowsFieldDropdown()
    {
        var cut = RenderComponent<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { TextFilter }));

        cut.Find(".tm-filter-builder-add").Click();

        cut.FindAll(".tm-filter-field-option").Should().NotBeEmpty();
    }

    [Fact]
    public void FilterBuilder_SelectField_ShowsOperatorDropdown()
    {
        var cut = RenderComponent<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { TextFilter }));

        cut.Find(".tm-filter-builder-add").Click();
        cut.FindAll(".tm-filter-field-option").First().Click();

        cut.FindAll(".tm-filter-operator-select").Should().NotBeEmpty();
    }

    [Fact]
    public void FilterBuilder_SelectOperator_ShowsValueInput()
    {
        var cut = RenderComponent<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { TextFilter }));

        cut.Find(".tm-filter-builder-add").Click();
        cut.FindAll(".tm-filter-field-option").First().Click();
        // operator is pre-selected (first one), but click apply to see value input
        cut.FindAll(".tm-filter-operator-select").Should().NotBeEmpty();
        cut.FindAll(".tm-filter-value-input").Should().NotBeEmpty();
    }

    [Fact]
    public void FilterBuilder_Apply_FiresOnFiltersChanged()
    {
        IReadOnlyList<ActiveFilter>? captured = null;
        var cut = RenderComponent<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions,  new[] { TextFilter })
            .Add(c => c.OnFiltersChanged,   (IReadOnlyList<ActiveFilter> fs) => captured = fs));

        cut.Find(".tm-filter-builder-add").Click();
        cut.FindAll(".tm-filter-field-option").First().Click();
        cut.Find(".tm-filter-value-input").Change("John");
        cut.Find(".tm-filter-apply").Click();

        captured.Should().NotBeNull();
        captured!.Should().HaveCount(1);
        captured[0].FieldName.Should().Be("name");
    }

    [Fact]
    public void FilterBuilder_Remove_RemovesChip()
    {
        var existing = new[]
        {
            new ActiveFilter("name", "Name", FilterOperator.Contains, "John", "John"),
        };
        var cut = RenderComponent<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { TextFilter })
            .Add(c => c.ActiveFilters,     existing));

        cut.FindAll(".tm-filter-chip").Should().HaveCount(1);
        cut.Find(".tm-filter-chip-remove").Click();
        cut.FindAll(".tm-filter-chip").Should().BeEmpty();
    }

    [Fact]
    public void FilterBuilder_Clear_ClearsAllFilters()
    {
        IReadOnlyList<ActiveFilter>? captured = null;
        var existing = new[]
        {
            new ActiveFilter("name", "Name", FilterOperator.Contains, "John", "John"),
            new ActiveFilter("status", "Status", FilterOperator.Equals, "active", "Active"),
        };
        var cut = RenderComponent<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions,  new[] { TextFilter, SelectFilter })
            .Add(c => c.ActiveFilters,      existing)
            .Add(c => c.OnFiltersChanged,   (IReadOnlyList<ActiveFilter> fs) => captured = fs));

        cut.Find(".tm-filter-clear-all").Click();

        captured.Should().NotBeNull();
        captured!.Should().BeEmpty();
    }

    [Fact]
    public void FilterBuilder_IsEmpty_HiddenWhenNoFilters()
    {
        var cut = RenderComponent<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { TextFilter }));

        cut.FindAll(".tm-filter-clear-all").Should().BeEmpty();
    }

    [Fact]
    public void FilterBuilder_DateFilter_UsesTmDatePicker()
    {
        var dateDef = new FilterDefinition
        {
            FieldName  = "createdAt",
            FieldLabel = "Created",
            FieldType  = FilterFieldType.Date,
        };
        var cut = RenderComponent<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { dateDef }));

        cut.Find(".tm-filter-builder-add").Click();
        cut.FindAll(".tm-filter-field-option").First().Click();

        cut.FindAll(".tm-date-picker").Should().NotBeEmpty();
    }

    [Fact]
    public void FilterBuilder_SelectFilter_UsesDropdown()
    {
        var cut = RenderComponent<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { SelectFilter }));

        cut.Find(".tm-filter-builder-add").Click();
        cut.FindAll(".tm-filter-field-option").First().Click();

        cut.FindAll(".tm-filter-select").Should().NotBeEmpty();
    }
}
