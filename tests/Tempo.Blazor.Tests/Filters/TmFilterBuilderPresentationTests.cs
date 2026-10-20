using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Components.Filters;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Filters;

/// <summary>
/// <see cref="FilterBuilderPresentation"/>: Inline stays in the document flow, Popover and Auto
/// open the picker and editor through <see cref="TmOverlayPanel"/> anchored to Add filter.
/// </summary>
public class TmFilterBuilderPresentationTests : LocalizationTestBase
{
    private static FilterDefinition Name => new()
    {
        FieldName = "name",
        FieldLabel = "Name",
        FieldType = FilterFieldType.Text,
    };

    [Fact]
    public void Inline_renders_the_picker_in_flow_with_no_overlay()
    {
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { Name }));

        cut.Find(".tm-filter-builder-add").Click();

        cut.FindComponents<TmOverlayPanel>().Should().BeEmpty();
        cut.FindAll(".tm-filter-field-option").Should().NotBeEmpty();
        cut.Markup.Should().NotContain("tm-filter-builder--popover");
    }

    [Fact]
    public void Popover_anchors_an_overlay_to_the_add_button()
    {
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { Name })
            .Add(c => c.Presentation, FilterBuilderPresentation.Popover));

        cut.FindComponents<TmOverlayPanel>().Should().BeEmpty("the panel exists only while open");
        cut.Find(".tm-filter-builder-add").GetAttribute("aria-expanded").Should().Be("false");

        cut.Find(".tm-filter-builder-add").Click();

        var panel = cut.FindComponent<TmOverlayPanel>();
        panel.Instance.IsOpen.Should().BeTrue();
        panel.Instance.MobilePresentation.Should().Be(PanelPresentation.Popover);
        panel.Instance.Class.Should().Be("tm-filter-builder-panel");
        cut.Find(".tm-filter-builder-add").GetAttribute("aria-expanded").Should().Be("true");
        cut.Markup.Should().NotContain("tm-filter-builder--popover");
    }

    [Fact]
    public void Auto_asks_the_overlay_to_choose_popover_or_sheet()
    {
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { Name })
            .Add(c => c.Presentation, FilterBuilderPresentation.Auto));

        cut.Find(".tm-filter-builder-add").Click();

        cut.FindComponent<TmOverlayPanel>().Instance.MobilePresentation.Should().Be(PanelPresentation.Auto);
    }

    [Fact]
    public void Popover_keeps_the_editor_inside_the_same_overlay()
    {
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { Name })
            .Add(c => c.Presentation, FilterBuilderPresentation.Popover));

        cut.Find(".tm-filter-builder-add").Click();
        cut.Find(".tm-filter-field-option").Click();

        cut.FindComponents<TmOverlayPanel>().Should().ContainSingle();
        cut.FindComponent<TmOverlayPanel>().Find(".tm-filter-editor");
        cut.Find(".tm-filter-builder-add").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void Popover_cancel_closes_the_overlay()
    {
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { Name })
            .Add(c => c.Presentation, FilterBuilderPresentation.Popover));

        cut.Find(".tm-filter-builder-add").Click();
        cut.Find(".tm-filter-cancel").Click();

        cut.FindComponents<TmOverlayPanel>().Should().BeEmpty();
        cut.Find(".tm-filter-builder-add").GetAttribute("aria-expanded").Should().Be("false");
    }
}
