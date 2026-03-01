using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Filters;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Filters;

public class TmFilterChipTests : LocalizationTestBase
{
    private static ActiveFilter MakeFilter(string field = "status", string label = "Status",
        FilterOperator op = FilterOperator.Equals, string display = "Active")
        => new(field, label, op, "active", display);

    [Fact]
    public void FilterChip_RendersFieldAndValue()
    {
        var filter = MakeFilter(label: "Status", display: "Active");
        var cut = RenderComponent<TmFilterChip>(p => p.Add(c => c.Filter, filter));

        cut.Markup.Should().Contain("Status");
        cut.Markup.Should().Contain("Active");
    }

    [Fact]
    public void FilterChip_RemoveButton_FiresOnRemove()
    {
        ActiveFilter? removed = null;
        var filter = MakeFilter();
        var cut = RenderComponent<TmFilterChip>(p => p
            .Add(c => c.Filter,    filter)
            .Add(c => c.OnRemove,  (ActiveFilter f) => removed = f));

        cut.Find(".tm-filter-chip-remove").Click();

        removed.Should().NotBeNull();
        removed!.FieldName.Should().Be("status");
    }

    [Fact]
    public void FilterChip_ClickChip_FiresOnEdit()
    {
        ActiveFilter? edited = null;
        var filter = MakeFilter();
        var cut = RenderComponent<TmFilterChip>(p => p
            .Add(c => c.Filter,  filter)
            .Add(c => c.OnEdit,  (ActiveFilter f) => edited = f));

        cut.Find(".tm-filter-chip").Click();

        edited.Should().NotBeNull();
        edited!.FieldName.Should().Be("status");
    }
}
