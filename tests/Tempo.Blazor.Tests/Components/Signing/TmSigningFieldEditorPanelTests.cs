using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

public class TmSigningFieldEditorPanelTests : LocalizationTestBase
{
    [Fact]
    public void Render_NoSelectedField_ShowsEmptyState()
    {
        var cut = Render<TmSigningFieldEditorPanel>();

        cut.Find(".tm-signing-field-editor-panel__empty").TextContent.Should().Contain("Select a field");
    }

    [Fact]
    public void Render_WithField_ShowsPanelTitle()
    {
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField()));

        cut.Find(".tm-signing-field-editor-panel__title").TextContent.Should().Contain("Full name");
    }

    [Fact]
    public void ChangeType_InvokesFieldChanged()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__type").Change(SigningFieldType.Number.ToString());

        captured.Should().NotBeNull();
        captured!.Type.Should().Be(SigningFieldType.Number);
    }

    [Fact]
    public void ChangeType_FromChoiceToSignature_RemovesChoiceOptionsAndInvalidDefault()
    {
        SigningField? captured = null;
        var field = CreateChoiceField(SigningFieldType.Select);
        field.DefaultValue = "option-a";
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, changed => captured = changed)));

        cut.Find(".tm-signing-field-editor-panel__type").Change(SigningFieldType.Signature.ToString());

        captured.Should().NotBeNull();
        captured!.Type.Should().Be(SigningFieldType.Signature);
        captured.Options.Should().BeEmpty();
        captured.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void ChangeType_ToChoice_AddsDefaultOptionsWhenMissing()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, changed => captured = changed)));

        cut.Find(".tm-signing-field-editor-panel__type").Change(SigningFieldType.Select.ToString());

        captured.Should().NotBeNull();
        captured!.Type.Should().Be(SigningFieldType.Select);
        captured.Options.Select(option => option.Value).Should().Equal("Option 1", "Option 2");
    }

    [Fact]
    public void ReadOnly_DisablesControls()
    {
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.ReadOnly, true));

        cut.Find(".tm-signing-field-editor-panel__name").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-signing-field-editor-panel__type").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void EditName_InvokesFieldChanged()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__name").Change("Legal name");

        captured.Should().NotBeNull();
        captured!.Name.Should().Be("Legal name");
    }

    [Fact]
    public void EditTitle_InvokesFieldChanged()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__field-title-input").Change("Your full legal name");

        captured.Should().NotBeNull();
        captured!.Title.Should().Be("Your full legal name");
    }

    [Fact]
    public void EditDescription_InvokesFieldChanged()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__description").Change("Use the name from your ID.");

        captured.Should().NotBeNull();
        captured!.Description.Should().Be("Use the name from your ID.");
    }

    [Fact]
    public void ToggleRequired_InvokesFieldChanged()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__required").Change(true);

        captured.Should().NotBeNull();
        captured!.Required.Should().BeTrue();
    }

    [Fact]
    public void ToggleReadOnly_InvokesFieldChanged()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__readonly").Change(true);

        captured.Should().NotBeNull();
        captured!.ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void TogglePrefillable_InvokesFieldChanged()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__prefillable").Change(true);

        captured.Should().NotBeNull();
        captured!.Prefillable.Should().BeTrue();
    }

    [Fact]
    public void SelectSubmitterRole_InvokesFieldChanged()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.SubmitterRoles, CreateRoles())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__submitter").Change("role-b");

        captured.Should().NotBeNull();
        captured!.SubmitterUuid.Should().Be("role-b");
    }

    [Theory]
    [InlineData(SigningFieldType.Select)]
    [InlineData(SigningFieldType.Radio)]
    [InlineData(SigningFieldType.Multiple)]
    public void ChoiceField_RendersOptionsEditor(SigningFieldType type)
    {
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateChoiceField(type)));

        cut.Find(".tm-signing-field-editor-panel__options").TextContent.Should().Contain("Options");
    }

    [Fact]
    public void AddOption_InvokesFieldChanged()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateChoiceField(SigningFieldType.Select))
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__add-option").Click();

        captured.Should().NotBeNull();
        captured!.Options.Should().HaveCount(3);
        captured.Options.Last().Value.Should().Be("Option 3");
    }

    [Fact]
    public void RenameOption_InvokesFieldChanged()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateChoiceField(SigningFieldType.Select))
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find("[data-option-uuid='option-a'] .tm-signing-field-editor-panel__option-value").Change("Czech Republic");

        captured.Should().NotBeNull();
        captured!.Options.First(option => option.Uuid == "option-a").Value.Should().Be("Czech Republic");
    }

    [Fact]
    public void RemoveOption_InvokesFieldChanged()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateChoiceField(SigningFieldType.Select))
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find("[data-option-uuid='option-b'] .tm-signing-field-editor-panel__remove-option").Click();

        captured.Should().NotBeNull();
        captured!.Options.Select(option => option.Uuid).Should().NotContain("option-b");
    }

    [Fact]
    public void MoveOptionDown_ReordersOptions()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateChoiceField(SigningFieldType.Select))
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find("[data-option-uuid='option-a'] .tm-signing-field-editor-panel__move-option-down").Click();

        captured.Should().NotBeNull();
        captured!.Options[0].Uuid.Should().Be("option-b");
        captured.Options[1].Uuid.Should().Be("option-a");
    }

    [Fact]
    public void DefaultValueSelect_UpdatesDefaultValue()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateChoiceField(SigningFieldType.Select))
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__default-option").Change("option-b");

        captured.Should().NotBeNull();
        captured!.DefaultValue.Should().Be("option-b");
    }

    [Fact]
    public void OptionMapButton_InvokesMappingCallback()
    {
        TmSigningFieldOptionAreaMappingEventArgs? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateChoiceField(SigningFieldType.Radio))
                      .Add(p => p.OptionAreaMappingRequested, EventCallback.Factory.Create<TmSigningFieldOptionAreaMappingEventArgs>(this, args => captured = args)));

        cut.Find("[data-option-uuid='option-a'] .tm-signing-field-editor-panel__map-option").Click();

        captured.Should().NotBeNull();
        captured!.Field.Uuid.Should().Be("field-1");
        captured.Option.Uuid.Should().Be("option-a");
    }

    [Fact]
    public void ValidationNone_ClearsValidation()
    {
        SigningField? captured = null;
        var field = CreateField();
        field.Validation = new SigningFieldValidation { Pattern = "\\d+" };
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, changed => captured = changed)));

        cut.Find(".tm-signing-field-editor-panel__validation-mode").Change("None");

        captured.Should().NotBeNull();
        captured!.Validation.Should().BeNull();
    }

    [Fact]
    public void ValidationRegex_UpdatesPattern()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__validation-mode").Change("Regex");
        cut.Find(".tm-signing-field-editor-panel__validation-pattern").Change("^[A-Z]+$");

        captured.Should().NotBeNull();
        captured!.Validation!.Pattern.Should().Be("^[A-Z]+$");
    }

    [Fact]
    public void TextLengthValidation_UpdatesMinMaxAndMessage()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__validation-min").Change("2");
        cut.Find(".tm-signing-field-editor-panel__validation-max").Change("80");
        cut.Find(".tm-signing-field-editor-panel__validation-message").Change("Use 2 to 80 characters.");

        captured.Should().NotBeNull();
        captured!.Validation!.Min.Should().Be("2");
        captured.Validation.Max.Should().Be("80");
        captured.Validation.Message.Should().Be("Use 2 to 80 characters.");
    }

    [Fact]
    public void LocalizationEditor_UpdatesLocalizedFieldText()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.SupportedCultures, new[] { "en-US", "cs-CZ" })
                      .Add(p => p.ShowLocalizationEditor, true)
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__localization-culture").Change("cs-CZ");
        cut.Find(".tm-signing-field-editor-panel__localized-label").Change("Celé jméno");
        cut.Find(".tm-signing-field-editor-panel__localized-title").Change("Vaše celé jméno");
        cut.Find(".tm-signing-field-editor-panel__localized-description").Change("Použijte jméno z dokladu.");
        cut.Find(".tm-signing-field-editor-panel__localized-placeholder").Change("Jan Novák");
        cut.Find(".tm-signing-field-editor-panel__localized-validation-message").Change("Jméno je povinné.");

        captured.Should().NotBeNull();
        captured!.Labels.Translations["cs-CZ"].Should().Be("Celé jméno");
        captured.Titles.Translations["cs-CZ"].Should().Be("Vaše celé jméno");
        captured.Descriptions.Translations["cs-CZ"].Should().Be("Použijte jméno z dokladu.");
        captured.Placeholders.Translations["cs-CZ"].Should().Be("Jan Novák");
        captured.Validation!.Messages.Translations["cs-CZ"].Should().Be("Jméno je povinné.");
    }

    [Fact]
    public void LocalizationEditor_UpdatesOptionLabelWithoutChangingValue()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateChoiceField(SigningFieldType.Select))
                      .Add(p => p.SupportedCultures, new[] { "en-US", "cs-CZ" })
                      .Add(p => p.ShowLocalizationEditor, true)
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__localization-culture").Change("cs-CZ");
        cut.Find("[data-localized-option-uuid='option-a'] .tm-signing-field-editor-panel__localized-option-label").Change("Jedna");

        captured.Should().NotBeNull();
        var option = captured!.Options.Single(item => item.Uuid == "option-a");
        option.Value.Should().Be("One");
        option.Labels.Translations["cs-CZ"].Should().Be("Jedna");
    }

    [Fact]
    public void LocalizationEditor_IsHiddenByDefault()
    {
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.SupportedCultures, new[] { "en-US", "cs-CZ" }));

        cut.FindAll(".tm-signing-field-editor-panel__localization").Should().BeEmpty();
    }

    [Fact]
    public void LocalizationEditor_ShowsTemplateLanguage()
    {
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.SupportedCultures, new[] { "en-US", "cs-CZ" })
                      .Add(p => p.FallbackCulture, "cs-CZ")
                      .Add(p => p.ShowLocalizationEditor, true));

        cut.Find(".tm-signing-field-editor-panel__template-language")
            .TextContent
            .Should()
            .Contain("Template language")
            .And.Contain("cs-CZ");
    }

    [Fact]
    public void LocalizationEditor_ShowsMissingTranslationWarningForActiveCulture()
    {
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.SupportedCultures, new[] { "en-US", "cs-CZ" })
                      .Add(p => p.ShowLocalizationEditor, true));

        var warning = cut.Find(".tm-signing-field-editor-panel__missing-localization");
        warning.GetAttribute("role").Should().Be("status");
        warning.GetAttribute("aria-live").Should().Be("polite");
        warning.TextContent.Should().Contain("signer-facing text");
        warning.TextContent.Should().Contain("en-US");
    }

    [Fact]
    public void LocalizationEditor_DoesNotWarnWhenActiveCultureHasTranslations()
    {
        var field = CreateField();
        field.Labels.Translations["en-US"] = "Full name";
        field.Titles.Translations["en-US"] = "Full legal name";
        field.Descriptions.Translations["en-US"] = "Use your legal name.";

        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.SupportedCultures, new[] { "en-US", "cs-CZ" })
                      .Add(p => p.ShowLocalizationEditor, true));

        cut.FindAll(".tm-signing-field-editor-panel__missing-localization").Should().BeEmpty();
    }

    [Fact]
    public void NumberValidation_UpdatesMinMaxStep()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField(type: SigningFieldType.Number))
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__validation-min").Change("10");
        cut.Find(".tm-signing-field-editor-panel__validation-max").Change("100");
        cut.Find(".tm-signing-field-editor-panel__validation-step").Change("0.5");

        captured.Should().NotBeNull();
        captured!.Validation!.Min.Should().Be("10");
        captured.Validation.Max.Should().Be("100");
        captured.Validation.Step.Should().Be("0.5");
    }

    [Fact]
    public void DateValidation_UpdatesMinMaxAndFormat()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField(type: SigningFieldType.Date))
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__validation-min").Change("2026-01-01");
        cut.Find(".tm-signing-field-editor-panel__validation-max").Change("2026-12-31");
        cut.Find(".tm-signing-field-editor-panel__date-format").Change("yyyy-MM-dd");

        captured.Should().NotBeNull();
        captured!.Validation!.Min.Should().Be("2026-01-01");
        captured.Validation.Max.Should().Be("2026-12-31");
        captured.Preferences.Format.Should().Be("yyyy-MM-dd");
    }

    [Fact]
    public void SignaturePreferences_UpdateFormatAndSignatureId()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField(type: SigningFieldType.Signature))
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__signature-format").Change("typed");
        cut.Find(".tm-signing-field-editor-panel__signature-id").Change(true);

        captured.Should().NotBeNull();
        captured!.Preferences.Format.Should().Be("typed");
        captured.Preferences.WithSignatureId.Should().BeTrue();
    }

    [Fact]
    public void StampPreferences_UpdateWithLogo()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField(type: SigningFieldType.Stamp))
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__stamp-logo").Change(true);

        captured.Should().NotBeNull();
        captured!.Preferences.WithLogo.Should().BeTrue();
    }

    [Fact]
    public void TextPreferences_UpdateFontAlignAndColor()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__font-family").Change("serif");
        cut.Find(".tm-signing-field-editor-panel__font-size").Change("18");
        cut.Find(".tm-signing-field-editor-panel__align").Change("center");
        cut.Find(".tm-signing-field-editor-panel__color").Change("#123456");

        captured.Should().NotBeNull();
        captured!.Preferences.FontFamily.Should().Be("serif");
        captured.Preferences.FontSize.Should().Be(18);
        captured.Preferences.Align.Should().Be("center");
        captured.Preferences.Color.Should().Be("#123456");
    }

    [Fact]
    public void CopyToAllPagesButton_InvokesCallback()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.CopyToAllPagesRequested, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__copy-to-pages").Click();

        captured.Should().NotBeNull();
        captured!.Uuid.Should().Be("field-1");
    }

    [Fact]
    public void ColorInput_FallsBackToRoleHexColor()
    {
        var field = CreateField();
        field.Preferences.Color = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.SubmitterRoles, CreateRoles()));

        cut.Find(".tm-signing-field-editor-panel__color")
            .GetAttribute("value").Should().Be("#2563eb",
                "the colour input needs a concrete colour — role-a has none, so the palette colour of its order wins");
    }

    [Fact]
    public void ColorInput_RoleWithoutColorAtLaterOrder_FallsBackToThatOrdersPaletteColor()
    {
        var field = CreateField();
        field.Preferences.Color = null;
        field.SubmitterUuid = "role-b";
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.SubmitterRoles, CreateRoles()));

        cut.Find(".tm-signing-field-editor-panel__color")
            .GetAttribute("value").Should().Be("#16a34a");
    }

    [Fact]
    public void ColorInput_FieldWithoutSubmitterRole_FallsBackToFirstPaletteColor()
    {
        var field = CreateField();
        field.Preferences.Color = null;
        field.SubmitterUuid = "role-unknown";
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.SubmitterRoles, CreateRoles()));

        cut.Find(".tm-signing-field-editor-panel__color")
            .GetAttribute("value").Should().Be("#2563eb");
    }

    [Fact]
    public void ColorInput_SavedPreference_BeatsTheRoleColour()
    {
        var field = CreateField();
        field.Preferences.Color = "#123456";
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.SubmitterRoles, CreateRoles()));

        cut.Find(".tm-signing-field-editor-panel__color")
            .GetAttribute("value").Should().Be("#123456");
    }

    [Fact]
    public void ConditionButton_OpensConditionBuilder()
    {
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.Fields, CreateFields()));

        cut.Find(".tm-signing-field-editor-panel__open-conditions").Click();

        cut.Find(".tm-signing-field-editor-panel__condition-builder .tm-condition-builder")
            .Should()
            .NotBeNull();
    }

    [Fact]
    public void FormulaButton_RendersOnlyForNumberOrPayment()
    {
        var textCut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField()));
        var numberCut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField(type: SigningFieldType.Number)));

        textCut.FindAll(".tm-signing-field-editor-panel__open-formula").Should().BeEmpty();
        numberCut.FindAll(".tm-signing-field-editor-panel__open-formula").Should().HaveCount(1);
    }

    [Fact]
    public void ConditionBuilderChange_WritesConditionsToField()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__open-conditions").Click();
        cut.Find(".tm-condition-builder__field").Change("country");
        cut.Find(".tm-condition-builder__value-select").Change("country-cz");

        captured.Should().NotBeNull();
        captured!.Conditions.Should().ContainSingle();
        captured.Conditions[0].FieldUuid.Should().Be("country");
        captured.Conditions[0].Value.Should().Be("country-cz");
    }

    [Fact]
    public void ConditionBuilderChange_KeepsConditionBuilderOpenWhenParentPassesUpdatedField()
    {
        SigningField? current = CreateField();
        IRenderedComponent<TmSigningFieldEditorPanel>? cut = null;
        cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, current)
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field =>
                      {
                          current = field;
                          cut!.Render(parameters => parameters.Add(p => p.Field, current));
                      })));

        cut.Find(".tm-signing-field-editor-panel__open-conditions").Click();
        cut.Find(".tm-condition-builder__field").Change("country");

        cut.FindAll(".tm-signing-field-editor-panel__condition-builder .tm-condition-builder")
            .Should()
            .HaveCount(1);
    }

    [Fact]
    public void FormulaSave_WritesFormulaToPreferences()
    {
        SigningField? captured = null;
        var cut = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateField(type: SigningFieldType.Number))
                      .Add(p => p.Fields, CreateFormulaFields())
                      .Add(p => p.FieldChanged, EventCallback.Factory.Create<SigningField>(this, field => captured = field)));

        cut.Find(".tm-signing-field-editor-panel__open-formula").Click();
        cut.Find("[data-field-uuid='subtotal']").Click();
        cut.Find(".tm-formula-builder__save").Click();

        captured.Should().NotBeNull();
        captured!.Preferences.Formula.Should().Be("{{subtotal}}");
        captured.ReadOnly.Should().BeTrue();
    }

    private static SigningField CreateField(SigningFieldType type = SigningFieldType.Text)
    {
        return new SigningField
        {
            Uuid = "field-1",
            Name = "Full name",
            Title = "Full legal name",
            Description = "Use your legal name.",
            Type = type,
            SubmitterUuid = "role-a",
            Preferences = new SigningFieldPreferences
            {
                Color = "#2563eb",
                Align = "left",
                FontFamily = "sans",
                FontSize = 14
            }
        };
    }

    private static SigningField CreateChoiceField(SigningFieldType type)
    {
        var field = CreateField(type);
        field.Options =
        [
            new SigningFieldOption { Uuid = "option-a", Value = "One" },
            new SigningFieldOption { Uuid = "option-b", Value = "Two" }
        ];

        return field;
    }

    private static IReadOnlyList<SigningSubmitterRole> CreateRoles()
    {
        return
        [
            new SigningSubmitterRole { Uuid = "role-a", Name = "Signer" },
            new SigningSubmitterRole { Uuid = "role-b", Name = "Approver" }
        ];
    }

    private static IReadOnlyList<SigningField> CreateFields()
    {
        return
        [
            CreateField(),
            new SigningField
            {
                Uuid = "country",
                Name = "Country",
                Type = SigningFieldType.Select,
                Options =
                [
                    new SigningFieldOption { Uuid = "country-cz", Value = "Czech Republic" },
                    new SigningFieldOption { Uuid = "country-us", Value = "United States" }
                ]
            }
        ];
    }

    private static IReadOnlyList<SigningField> CreateFormulaFields()
    {
        return
        [
            CreateField(SigningFieldType.Number),
            new SigningField
            {
                Uuid = "subtotal",
                Name = "Subtotal",
                Type = SigningFieldType.Number
            }
        ];
    }
}
