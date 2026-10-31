using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
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

    // ── SI1 (Signing redesign phase 5): radio cards on a mobile layout ──

    /// <summary>On a mobile layout a SELECT field with at most five options renders as radio CARDS
    /// (the whole card is the clickable row, 56 px) — the system picker stays for the >5 case, so
    /// the compact card set only replaces it where every option fits on screen.</summary>
    [Fact]
    public void ChoiceStep_Mobile_UpToFiveOptions_RendersRadioCards()
    {
        var field = CreateChoiceField(SigningFieldType.Select, required: true);
        field.Options = Enumerable.Range(1, 5)
            .Select(index => new SigningFieldOption { Uuid = $"opt-{index}", Value = $"Option {index}" })
            .ToList();

        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, field)
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile));

        cut.FindAll(".tm-radio-option").Should().HaveCount(5, "five options render as five radio cards");
        cut.FindAll("select").Should().BeEmpty("the system picker is replaced by the cards on mobile");
        cut.Find(".tm-radio-group").ClassList.Should().Contain("tm-signing-choice-step__options--cards",
            "the card look rides the Signing CSS modifier on the core TmRadioGroup");

        // The phase-2 group contract is kept: a real fieldset named by the hidden legend.
        cut.Find("fieldset.tm-signing-choice-step__group").QuerySelector("legend").Should().NotBeNull();
    }

    /// <summary>Six options overflow the card set: the mobile layout keeps the core TmSelect (the
    /// system picker), exactly like the desktop layout does.</summary>
    [Fact]
    public void ChoiceStep_Mobile_SixOptions_KeepsTmSelect()
    {
        var field = CreateChoiceField(SigningFieldType.Select, required: true);
        field.Options = Enumerable.Range(1, 6)
            .Select(index => new SigningFieldOption { Uuid = $"opt-{index}", Value = $"Option {index}" })
            .ToList();

        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, field)
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile));

        cut.Find("select.tm-select").Should().NotBeNull("six options stay the system picker");
        cut.FindAll(".tm-radio-option").Should().BeEmpty();
    }

    /// <summary>A RADIO field on a mobile layout renders its (unchanged) TmRadioGroup with the card
    /// modifier when the options fit — the cards are a LOOK on the same control, not a new one.</summary>
    [Fact]
    public void ChoiceStep_Mobile_RadioType_RendersCardsWithModifier()
    {
        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, CreateChoiceField(SigningFieldType.Radio))
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile));

        cut.Find(".tm-radio-group").ClassList.Should().Contain("tm-signing-choice-step__options--cards");
        cut.FindAll(".tm-radio-option").Should().HaveCount(2);
    }

    /// <summary>Selecting a radio card raises the same stable option uuid the select raised — the
    /// swap is a presentation detail, the value contract is untouched.</summary>
    [Fact]
    public async Task ChoiceStep_Mobile_RadioCards_SelectRaisesStableValue()
    {
        object? value = null;
        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, CreateChoiceField(SigningFieldType.Select))
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<object?>(this, changed => value = changed)));

        await cut.FindAll("input[type='radio']")[2].ChangeAsync(new ChangeEventArgs { Value = "two" });

        value.Should().Be("two");
    }

    /// <summary>Desktop (Auto / forced Desktop) keeps the select — the cards are a MOBILE layout
    /// presentation only.</summary>
    [Fact]
    public void ChoiceStep_Desktop_UnderFiveOptions_KeepsTmSelect()
    {
        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, CreateChoiceField(SigningFieldType.Select))
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.Find("select.tm-select").Should().NotBeNull();
        cut.FindAll(".tm-radio-option").Should().BeEmpty();
    }

    /// <summary>An OPTIONAL select rendered as cards keeps a way to CLEAR the choice (phase 5
    /// review): a localized "None" card rides FIRST and sends the empty value — the system
    /// picker's selectable placeholder keeps its contract in the card presentation.</summary>
    [Fact]
    public void ChoiceStep_Mobile_OptionalSelect_CardsCarryLocalizedNoneCardFirst()
    {
        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, CreateChoiceField(SigningFieldType.Select))
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile));

        var options = cut.FindAll(".tm-radio-option");
        options.Should().HaveCount(3, "the None card rides before the field's own two options");
        options[0].TextContent.Should().Contain("None", "the card set's clear choice is localized");
        options[0].QuerySelector("input[type='radio']")!.HasAttribute("checked").Should().BeTrue(
            "the empty value maps to the None card (the select's empty placeholder state)");
    }

    /// <summary>Selecting the None card raises the EMPTY value — the same contract the system
    /// picker's selectable placeholder had.</summary>
    [Fact]
    public async Task ChoiceStep_Mobile_NoneCard_SendsEmptyValue()
    {
        object? value = "untouched";
        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, CreateChoiceField(SigningFieldType.Select))
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<object?>(this, changed => value = changed)));

        await cut.FindAll(".tm-radio-option input[type='radio']")[0].ChangeAsync(new ChangeEventArgs { Value = "" });

        value.Should().Be(string.Empty, "the None card clears the choice");
    }

    /// <summary>A REQUIRED select's card set carries no None card — the required validation
    /// explains the missing choice instead of offering a value that cannot be submitted.</summary>
    [Fact]
    public void ChoiceStep_Mobile_RequiredSelect_HasNoNoneCard()
    {
        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, CreateChoiceField(SigningFieldType.Select, required: true))
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile));

        cut.FindAll(".tm-radio-option").Should().HaveCount(2, "required fields keep only their real options");
        cut.FindAll(".tm-radio-option").Should().NotContain(option => option.TextContent.Contains("None"));
    }

    /// <summary>A RADIO field never gains the None card — a radio group has no empty semantics
    /// (the signer picking between real options), only the SELECT's placeholder contract maps to it.</summary>
    [Fact]
    public void ChoiceStep_Mobile_OptionalRadio_HasNoNoneCard()
    {
        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, CreateChoiceField(SigningFieldType.Radio))
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile));

        cut.FindAll(".tm-radio-option").Should().HaveCount(2);
        cut.FindAll(".tm-radio-option").Should().NotContain(option => option.TextContent.Contains("None"));
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
