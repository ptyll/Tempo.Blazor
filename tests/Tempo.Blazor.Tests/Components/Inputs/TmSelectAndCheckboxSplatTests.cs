using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>
/// The additive splat hooks the Signing steps (redesign phase 4) compose:
/// <see cref="TmSelect.SelectAttributes"/> splats onto the native <c>&lt;select&gt;</c> (the
/// wrapper splat cannot reach it) and <see cref="TmCheckbox.InputAttributes"/> onto the native
/// checkbox input; a <see cref="TmCheckbox.HelpText"/> block gains an id the input references via
/// <c>aria-describedby</c>. Nothing existing changes when the parameters are omitted.
/// </summary>
public class TmSelectAndCheckboxSplatTests : LocalizationTestBase
{
    [Fact]
    public void SelectAttributes_SplatOntoTheNativeSelect()
    {
        var cut = Render<TmSelect<string>>(p => p
            .Add(x => x.Label, "Country")
            .Add(x => x.Options, new List<SelectOption<string>> { new("+420", "CZ +420") })
            .Add(x => x.SelectAttributes, new Dictionary<string, object>
            {
                ["aria-invalid"] = "true",
                ["aria-describedby"] = "step-description step-validation",
                ["aria-labelledby"] = "step-title"
            }));

        var select = cut.Find("select");
        select.GetAttribute("aria-invalid").Should().Be("true");
        select.GetAttribute("aria-describedby").Should().Be("step-description step-validation");
        select.GetAttribute("aria-labelledby").Should().Be("step-title");
    }

    [Fact]
    public void SelectAttributes_Omitted_SelectKeepsItsOwnAttributesOnly()
    {
        var cut = Render<TmSelect<string>>(p => p
            .Add(x => x.Label, "Country")
            .Add(x => x.Options, new List<SelectOption<string>> { new("+420", "CZ +420") }));

        cut.Find("select").GetAttribute("aria-labelledby").Should().BeNull();
    }

    [Fact]
    public void CheckboxInputAttributes_SplatOntoTheNativeInput()
    {
        var cut = Render<TmCheckbox>(p => p
            .Add(x => x.Label, "I agree")
            .Add(x => x.InputAttributes, new Dictionary<string, object>
            {
                ["aria-invalid"] = "true",
                ["aria-describedby"] = "step-validation"
            }));

        var input = cut.Find("input[type='checkbox']");
        input.GetAttribute("aria-invalid").Should().Be("true");
        input.GetAttribute("aria-describedby").Should().Contain("step-validation");
    }

    [Fact]
    public void CheckboxHelpText_CarriesAnId_ReferencedFromTheInput()
    {
        var cut = Render<TmCheckbox>(p => p
            .Add(x => x.Label, "Newsletter")
            .Add(x => x.HelpText, "We send one e-mail a month."));

        var help = cut.Find(".tm-checkbox-help");
        var helpId = help.GetAttribute("id");
        helpId.Should().NotBeNullOrWhiteSpace("the input must be able to reference the help text");

        cut.Find("input[type='checkbox']")
            .GetAttribute("aria-describedby")
            .Should().Be(helpId);
    }

    [Fact]
    public void CheckboxHelpText_AndSplattedDescription_MergeInTheInputReference()
    {
        var cut = Render<TmCheckbox>(p => p
            .Add(x => x.Label, "Newsletter")
            .Add(x => x.HelpText, "We send one e-mail a month.")
            .Add(x => x.InputAttributes, new Dictionary<string, object>
            {
                ["aria-describedby"] = "step-validation"
            }));

        var describedBy = cut.Find("input[type='checkbox']").GetAttribute("aria-describedby");
        describedBy.Should().Contain(cut.Find(".tm-checkbox-help").GetAttribute("id"));
        describedBy.Should().Contain("step-validation");
    }

    [Fact]
    public void CheckboxWithoutHelpText_SetsNoDescribedBy()
    {
        var cut = Render<TmCheckbox>(p => p.Add(x => x.Label, "Only"));

        cut.Find("input[type='checkbox']").GetAttribute("aria-describedby").Should().BeNull();
    }
}
