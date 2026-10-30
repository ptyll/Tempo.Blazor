using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// TmSigningChoiceStep on the Tm components (Signing redesign phase 4, SI1): select renders
/// <c>TmSelect</c>, the radio variant <c>TmRadioGroup</c> (still inside the real
/// <c>fieldset</c>/<c>legend</c> group the phase-2 contract established, with the per-instance
/// radio name), and the checkbox variants <c>TmCheckbox</c> option rows — the grouped checkbox
/// fields carrying their own descriptions.
/// </summary>
public class TmSigningChoiceStepTests : LocalizationTestBase
{
    [Fact]
    public void ChoiceStep_Select_UsesTmSelect()
    {
        var field = CreateChoiceField(SigningFieldType.Select, required: true);
        field.Description = "Pick one delivery option.";
        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, field));

        cut.Find("select.tm-select").Should().NotBeNull("the select variant renders the core TmSelect");
        cut.FindAll("option").Select(option => option.TextContent).Should().Contain(["One", "Two"]);

        var select = cut.Find("select");
        // The name comes from AriaLabel (the visible field label)…
        select.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace("the select carries the field label as its accessible name");

        // …and the phase-4 review's SelectAttributes splat carries the phase-2 plumbing onto the
        // NATIVE <select>: the step title as the labelledby, the description / validation ids as
        // the description, and the invalid state while a message shows.
        select.GetAttribute("aria-labelledby").Should().NotBeNullOrWhiteSpace();
        var describedBy = select.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrWhiteSpace();
        cut.Find($"#{describedBy}").TextContent.Should().Contain("Pick one delivery option.");
        select.GetAttribute("aria-invalid").Should().Be("false");

        cut.Find("select").Change(string.Empty);
        cut.Find("select").GetAttribute("aria-invalid").Should().Be("true",
            "the native select mirrors the invalid state once the step shows a message");
    }

    [Fact]
    public void ChoiceStep_Select_RendersLocalizedOptionsAndKeepsStableValue()
    {
        object? value = null;
        var field = CreateChoiceField(SigningFieldType.Select);
        field.Options[0].Labels.Translations["cs"] = "Jedna";
        field.Options[1].Labels.Translations["cs"] = "Dvě";

        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, field)
            .Add(p => p.Culture, "cs-CZ")
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<object?>(this, changed => value = changed)));

        cut.FindAll("option").Select(option => option.TextContent).Should().Contain(["Jedna", "Dvě"]);

        cut.Find("select").Change("two");

        value.Should().Be("two");
    }

    [Fact]
    public void ChoiceStep_Radio_UsesTmRadioGroup_WithLegend()
    {
        var field = CreateChoiceField(SigningFieldType.Radio);
        field.Description = "Pick one delivery option.";
        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, field));

        // The phase-2 contract is kept: a real fieldset named by the (visually hidden) legend…
        var fieldset = cut.Find("fieldset.tm-signing-choice-step__group");
        fieldset.QuerySelector("legend").Should().NotBeNull();
        fieldset.QuerySelector("legend")!.TextContent.Should().NotBeEmpty();

        // …rendered through the core TmRadioGroup (radiogroup role + TmRadio rows).
        cut.Find(".tm-radio-group").Should().NotBeNull();
        cut.FindAll(".tm-radio-option").Should().HaveCount(2);
        cut.Find(".tm-radio-option").TextContent.Should().Contain("One");

        // Radios share ONE per-instance name (arrow keys move within the group) and the group is
        // described by the step's description / validation ids.
        var radios = cut.FindAll("input[type='radio']");
        radios.Select(radio => radio.GetAttribute("name")).Distinct().Should().ContainSingle();
        radios.First().GetAttribute("name").Should().NotBeNullOrWhiteSpace();
        fieldset.GetAttribute("aria-describedby").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ChoiceStep_Radio_SelectRaisesStableValue()
    {
        object? value = null;
        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, CreateChoiceField(SigningFieldType.Radio))
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<object?>(this, changed => value = changed)));

        cut.FindAll("input[type='radio']")[1].Change("two");

        value.Should().Be("two");
    }

    [Fact]
    public void ChoiceStep_CheckboxGroup_UsesTmCheckbox_OptionRowsWithDescription()
    {
        var first = new SigningField
        {
            Uuid = "a",
            Name = "Consent A",
            Type = SigningFieldType.Checkbox,
            Description = "Marketing consent"
        };
        var second = new SigningField
        {
            Uuid = "b",
            Name = "Consent B",
            Type = SigningFieldType.Checkbox,
            Description = "Newsletter consent"
        };
        var group = new SigningField { Uuid = "group", Name = "Consents", Type = SigningFieldType.Checkbox };

        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, group)
            .Add(p => p.Fields, [first, second]));

        cut.FindAll(".tm-checkbox-wrapper").Should().HaveCount(2, "the grouped checkboxes are TmCheckbox rows");
        cut.FindAll(".tm-checkbox-help").Select(node => node.TextContent.Trim())
            .Should().Equal("Marketing consent", "Newsletter consent");

        // The rows are still the phase-2 fieldset/legend group with the hint in its description.
        var fieldset = cut.Find("fieldset.tm-signing-choice-step__group");
        fieldset.QuerySelector("legend").Should().NotBeNull();
        fieldset.GetAttribute("aria-describedby").Should().NotBeNullOrWhiteSpace();

        // The row's description is reachable from the checkbox itself: the core TmCheckbox help
        // block carries an id the native input references (phase 4 review, "checkbox-group help
        // ids" on the focusable element).
        var rows = cut.FindAll(".tm-checkbox-wrapper");
        foreach (var row in rows)
        {
            var input = row.QuerySelector("input[type='checkbox']");
            var describedBy = input!.GetAttribute("aria-describedby");
            describedBy.Should().NotBeNullOrWhiteSpace("each row's help text must be referenced from its input");
            row.QuerySelector($"#{describedBy}")!.TextContent.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void Render_Multiple_RendersCheckboxOptionRowsAndKeepsStableValues()
    {
        object? value = null;
        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, CreateChoiceField(SigningFieldType.Multiple))
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<object?>(this, changed => value = changed)));

        cut.FindAll(".tm-checkbox-wrapper").Should().HaveCount(2);

        cut.FindAll("input[type='checkbox']")[1].Change(true);

        value.Should().BeAssignableTo<string[]>();
        value.As<string[]>().Should().Equal("two");
    }

    [Fact]
    public void Render_AnonymousCheckbox_UsesInstruction()
    {
        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Internal", Type = SigningFieldType.Checkbox })
            .Add(p => p.AnonymousCheckbox, true));

        cut.Find(".tm-signing-choice-step__option--single").TextContent.Should().Contain("Check to confirm");
    }

    [Fact]
    public void RequiredChoice_EmptySelect_ShowsValidation()
    {
        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, CreateChoiceField(SigningFieldType.Select, required: true)));

        cut.Find("select").Change(string.Empty);

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("choice");
    }

    private static SigningField CreateChoiceField(SigningFieldType type, bool required = false)
    {
        return new SigningField
        {
            Uuid = "choice",
            Name = "Choice",
            Type = type,
            Required = required,
            Options =
            [
                new SigningFieldOption { Uuid = "one", Value = "One" },
                new SigningFieldOption { Uuid = "two", Value = "Two" }
            ]
        };
    }
}
