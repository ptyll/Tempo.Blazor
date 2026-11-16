using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Forms;

/// <summary>
/// Review round (plan 98c80bc3, Fáze 3, S4/N5): the <c>TmFormFieldDescribedByIds</c> cascade reaches
/// every remaining input — TmDecimalInput, TmMultiSelect, TmEntityPicker, TmRadioGroup, TmCodeEditor
/// and TmExpressionEditor. A wrapped control with no help text of its own references the field-level
/// help through <c>aria-describedby</c> — the same contract the error cascade has (TmTextInput,
/// TmTextArea, TmSelect and TmNumberInput carry their own tests in <c>TmFormFieldTests</c> and the
/// component test files). TmFilterableDropdown reads BOTH cascades and, read-only, drops the popup
/// affordance attributes.
/// </summary>
public class FormFieldDescribedByCascadeTests : LocalizationTestBase
{
    private const string HelpId = "region-field-field-help";
    private const string ErrorId = "region-field-field-error";

    [Fact]
    public void DecimalInput_PicksUpTheFieldHelpCascade()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Price")
            .Add(c => c.Id, "region-field")
            .Add(c => c.HelpText, "Net of VAT.")
            .AddChildContent<TmDecimalInput>(child => child
                .Add(x => x.Label, "Price")));

        cut.Find("input").GetAttribute("aria-describedby").Should().Be(HelpId,
            "the decimal input references the field-level help it does not own");
    }

    [Fact]
    public void MultiSelect_PicksUpTheFieldHelpCascade()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Regions")
            .Add(c => c.Id, "region-field")
            .Add(c => c.HelpText, "Pick the closest region.")
            .AddChildContent<TmMultiSelect<string, string>>(child => child
                .Add(x => x.Label, "Regions")
                .Add(x => x.Items, new List<string> { "Czech", "Moravian" })));

        cut.Find(".tm-multiselect").GetAttribute("aria-describedby").Should().Be(HelpId);
    }

    [Fact]
    public void EntityPicker_PicksUpTheFieldHelpCascade()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Owner")
            .Add(c => c.Id, "region-field")
            .Add(c => c.HelpText, "Search by name.")
            .AddChildContent<TmEntityPicker<string, string>>(child => child
                .Add(x => x.Label, "Owner")
                .Add(x => x.SearchProvider, (Func<string, Task<IEnumerable<string>>>)(query => Task.FromResult<IEnumerable<string>>([])))
                .Add(x => x.ValueSelector, (Func<string, string>)(item => item))
                .Add(x => x.DisplaySelector, (Func<string, string>)(item => item))));

        cut.Find("input").GetAttribute("aria-describedby").Should().Be(HelpId);
    }

    [Fact]
    public void RadioGroup_PicksUpTheFieldHelpCascade()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Risk")
            .Add(c => c.Id, "region-field")
            .Add(c => c.HelpText, "One choice.")
            .AddChildContent<TmRadioGroup<string>>(child => child
                .Add(x => x.Label, "Risk")
                .Add(x => x.Options, new List<RadioOption<string>> { new("Low", "low"), new("High", "high") })));

        cut.Find("[role='radiogroup']").GetAttribute("aria-describedby").Should().Be(HelpId);
    }

    [Fact]
    public void CodeEditor_PicksUpTheFieldHelpCascade()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Definition")
            .Add(c => c.Id, "region-field")
            .Add(c => c.HelpText, "JSON only.")
            .AddChildContent<TmCodeEditor>(child => child
                .Add(x => x.Label, "Definition")));

        cut.Find("textarea").GetAttribute("aria-describedby").Should().Be(HelpId);
    }

    [Fact]
    public void ExpressionEditor_PicksUpTheFieldHelpCascade()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Expression")
            .Add(c => c.Id, "region-field")
            .Add(c => c.HelpText, "Use {{variables}}.")
            .AddChildContent<TmExpressionEditor>(child => child
                .Add(x => x.Label, "Expression")));

        cut.Find("textarea").GetAttribute("aria-describedby").Should().Be(HelpId);
    }

    [Fact]
    public void FilterableDropdown_PicksUpBothCascades()
    {
        // Help: the field-level help id reaches the combobox trigger.
        var withHelp = Render<TmFormField>(p => p
            .Add(c => c.Label, "Owner")
            .Add(c => c.Id, "region-field")
            .Add(c => c.HelpText, "Search by name.")
            .AddChildContent<TmFilterableDropdown<string, string>>(child => child
                .Add(x => x.Items, new List<string> { "Contoso" })
                .Add(x => x.DisplayField, (Func<string, string>)(item => item))));

        withHelp.Find(".tm-filterable-dropdown-trigger").GetAttribute("aria-describedby")
            .Should().Be(HelpId, "the trigger references the field-level help");

        // Error: the field-level error id reaches the trigger the same way.
        var withError = Render<TmFormField>(p => p
            .Add(c => c.Label, "Owner")
            .Add(c => c.Id, "region-field")
            .Add(c => c.ErrorMessage, "Required")
            .AddChildContent<TmFilterableDropdown<string, string>>(child => child
                .Add(x => x.Items, new List<string> { "Contoso" })
                .Add(x => x.DisplayField, (Func<string, string>)(item => item))));

        withError.Find(".tm-filterable-dropdown-trigger").GetAttribute("aria-describedby")
            .Should().Be(ErrorId, "the trigger references the field-level error");
    }

    /// <summary>
    /// Review round (S4/N5): a read-only combobox never opens — <c>aria-haspopup</c> and
    /// <c>aria-controls</c> would advertise a popup that cannot appear, so both go away while
    /// <c>aria-readonly</c> names the locked state.
    /// </summary>
    [Fact]
    public void FilterableDropdown_ReadOnly_DropsHaspopupAndControls()
    {
        var cut = Render<TmFilterableDropdown<string, string>>(p => p
            .Add(x => x.Items, new List<string> { "Contoso" })
            .Add(x => x.DisplayField, (Func<string, string>)(item => item))
            .Add(x => x.ReadOnly, true));

        var trigger = cut.Find(".tm-filterable-dropdown-trigger");
        trigger.GetAttribute("aria-haspopup").Should().BeNull(
            "a locked combobox has no popup to point at");
        trigger.GetAttribute("aria-controls").Should().BeNull();
        trigger.GetAttribute("aria-readonly").Should().Be("true");

        var editable = Render<TmFilterableDropdown<string, string>>(p => p
            .Add(x => x.Items, new List<string> { "Contoso" })
            .Add(x => x.DisplayField, (Func<string, string>)(item => item)));

        editable.Find(".tm-filterable-dropdown-trigger").GetAttribute("aria-haspopup")
            .Should().Be("listbox", "the editable combobox keeps the popup contract");
    }
}
