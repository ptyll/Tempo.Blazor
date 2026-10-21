using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Filters;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Filters;

/// <summary>
/// Phase 5.2/5.3: the field picker focuses its own first option (never a page-wide querySelector),
/// and Escape inside a nested date picker closes only that picker.
/// </summary>
public class TmFilterBuilderFocusTests : LocalizationTestBase
{
    private static FilterDefinition Named(string name, string label, FilterFieldType type = FilterFieldType.Text) => new()
    {
        FieldName = name,
        FieldLabel = label,
        FieldType = type,
    };

    [Fact]
    public void OpeningSecondPicker_FocusesThatInstancesFirstOption()
    {
        var defs = new[] { Named("city", "City"), Named("name", "Name") };
        var cut = Render<TwoFilterBuilders>(p => p.Add(c => c.Definitions, defs));

        var addButtons = cut.FindAll(".tm-filter-builder-add");
        addButtons.Should().HaveCount(2);
        addButtons[1].Click();

        var secondOption = cut.FindAll(".tm-filter-builder")[1].QuerySelector(".tm-filter-field-option");
        secondOption.Should().NotBeNull();
        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].ShouldBeElementReferenceTo(secondOption!);
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "eval");
    }

    [Fact]
    public void EscapeInsideOpenDatePicker_ClosesOnlyTheDatePicker()
    {
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { Named("hired", "Hired", FilterFieldType.Date) }));

        cut.Find(".tm-filter-builder-add").Click();
        cut.Find(".tm-filter-field-option").Click();

        var dateTrigger = cut.Find(".tm-date-picker-trigger");
        dateTrigger.Click();
        cut.FindAll(".tm-date-picker-popup").Should().NotBeEmpty("the date picker popup must be open");

        cut.Find(".tm-date-picker").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.FindAll(".tm-date-picker-popup").Should().BeEmpty("Escape closes the nested date picker");
        cut.FindAll(".tm-filter-editor").Should().NotBeEmpty("the filter editor stays open");
    }

    private sealed class TwoFilterBuilders : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public IReadOnlyList<FilterDefinition> Definitions { get; set; } = [];

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmFilterBuilder>(0);
            builder.AddAttribute(1, "FilterDefinitions", Definitions);
            builder.CloseComponent();

            builder.OpenComponent<TmFilterBuilder>(2);
            builder.AddAttribute(3, "FilterDefinitions", Definitions);
            builder.CloseComponent();
        }
    }
}
