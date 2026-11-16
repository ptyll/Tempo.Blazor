using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Forms;

public class TmFormFieldTests : LocalizationTestBase
{
    [Fact]
    public void FormField_RendersLabel()
    {
        var cut = Render<TmFormField>(p => p.Add(c => c.Label, "Email"));

        cut.Find("label.tm-form-field-label").TextContent.Should().Contain("Email");
    }

    [Fact]
    public void FormField_Required_RendersAsterisk()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label,    "Name")
            .Add(c => c.Required, true));

        cut.Find(".tm-form-field-required").Should().NotBeNull();
    }

    [Fact]
    public void FormField_HelpText_Rendered()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label,    "Password")
            .Add(c => c.HelpText, "Must be at least 8 characters."));

        cut.Find(".tm-form-field-help").TextContent.Should().Contain("Must be at least 8 characters.");
    }

    [Fact]
    public void FormField_ErrorMessage_Rendered()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label,        "Email")
            .Add(c => c.ErrorMessage, "Invalid email address."));

        cut.Find(".tm-form-field-error").TextContent.Should().Contain("Invalid email address.");
    }

    [Fact]
    public void FormField_ErrorMessage_HasErrorClass()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label,        "Email")
            .Add(c => c.ErrorMessage, "Required"));

        cut.Find(".tm-form-field").ClassList.Should().Contain("tm-form-field--error");
    }

    [Fact]
    public void FormField_ValidationFor_ShowsEditContextError()
    {
        var model = new TestModel();
        var editContext = new EditContext(model);

        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Name")
            .AddCascadingValue(editContext)
            .Add(c => c.ValidationFor, () => model.Name));

        var messages = new ValidationMessageStore(editContext);
        messages.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        editContext.NotifyValidationStateChanged();

        cut.Find(".tm-form-field-error").TextContent.Should().Contain("Name is required.");
        cut.Find(".tm-form-field").ClassList.Should().Contain("tm-form-field--error");
    }

    [Fact]
    public void FormField_ValidationFor_ErrorClearsAfterValidation()
    {
        var model = new TestModel();
        var editContext = new EditContext(model);

        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Name")
            .AddCascadingValue(editContext)
            .Add(c => c.ValidationFor, () => model.Name));

        var messages = new ValidationMessageStore(editContext);
        messages.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        editContext.NotifyValidationStateChanged();

        messages.Clear();
        editContext.NotifyValidationStateChanged();

        cut.FindAll(".tm-form-field-error").Should().BeEmpty();
        cut.Find(".tm-form-field").ClassList.Should().NotContain("tm-form-field--error");
    }

    [Fact]
    public void FormField_ExplicitErrorMessage_TakesPrecedenceOverEditContext()
    {
        var model = new TestModel();
        var editContext = new EditContext(model);

        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label,        "Name")
            .Add(c => c.ErrorMessage, "Explicit error")
            .AddCascadingValue(editContext)
            .Add(c => c.ValidationFor, () => model.Name));

        var messages = new ValidationMessageStore(editContext);
        messages.Add(editContext.Field(nameof(TestModel.Name)), "EditContext error.");
        editContext.NotifyValidationStateChanged();

        cut.Find(".tm-form-field-error").TextContent.Should().Contain("Explicit error");
    }

    [Fact]
    public void FormField_HelpText_HiddenWhenEditContextErrorPresent()
    {
        var model = new TestModel();
        var editContext = new EditContext(model);

        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label,    "Name")
            .Add(c => c.HelpText, "Some help")
            .AddCascadingValue(editContext)
            .Add(c => c.ValidationFor, () => model.Name));

        var messages = new ValidationMessageStore(editContext);
        messages.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        editContext.NotifyValidationStateChanged();

        cut.FindAll(".tm-form-field-help").Should().BeEmpty();
    }

    // ── Described-by cascade (plan 98c80bc3, Fáze 3) ─────────────────────────

    /// <summary>
    /// Fáze 3: the field's help text carries a stable id ("<c>{Id}-field-help</c>", mirroring the error
    /// paragraph's <c>{Id}-field-error</c>) and is published to the wrapped control through the
    /// <c>TmFormFieldDescribedByIds</c> cascade, so a wrapped input with no help text of its own
    /// references the field-level help through <c>aria-describedby</c> — the same contract the error-id
    /// cascade already has.
    /// </summary>
    [Fact]
    public void FormField_HelpText_CarriesAStableIdAndIsCascadedToTheWrappedInput()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Region")
            .Add(c => c.Id, "region-field")
            .Add(c => c.HelpText, "Pick the closest region.")
            .AddChildContent<TmTextInput>(child => child
                .Add(x => x.Label, "City")));

        var help = cut.Find(".tm-form-field-help");
        help.GetAttribute("id").Should().Be("region-field-field-help");

        var described = cut.Find("input").GetAttribute("aria-describedby");
        described.Should().Be("region-field-field-help",
            "the wrapped input references the field-level help even though it shows none of its own");
    }

    [Fact]
    public void FormField_WithoutHelpText_CascadesNothingAndTheInputShowsNoDescribedBy()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Region")
            .AddChildContent<TmTextInput>(child => child
                .Add(x => x.Label, "City")));

        cut.Find("input").GetAttribute("aria-describedby").Should().BeNull();
    }

    [Fact]
    public void FormField_HelpWithExplicitInputId_ProducesAStableReference()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Region")
            .Add(c => c.Id, "region-field")
            .Add(c => c.HelpText, "Pick the closest region.")
            .AddChildContent<TmTextInput>(child => child
                .Add(x => x.Id, "city-input")
                .Add(x => x.Label, "City")));

        cut.Find("input").GetAttribute("aria-describedby").Should().Be("region-field-field-help");
    }

    private class TestModel
    {
        public string Name { get; set; } = string.Empty;
    }
}
