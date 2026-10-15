using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Forms;

/// <summary>
/// The <c>TmFormField</c> error-announcement contract (cleanup round of the a11y plan): the error
/// paragraph gets a stable DOM id (<see cref="TmFormField.ErrorId"/>), the
/// <see cref="TmFormField.AnnounceError"/> opt-out drops only its <c>role="alert"</c> (the same
/// semantics as the nine input components from the previous round), and the id is cascaded to the
/// wrapped controls (<c>TmFormFieldErrorId</c>) so an input with no own
/// <c>Error</c> links the field-level message through <c>aria-describedby</c>. The default DOM is
/// frozen with full-markup snapshots: the only change for hosts that set nothing is the added
/// <c>id</c> attribute.
/// </summary>
public class TmFormFieldErrorAnnouncementTests : LocalizationTestBase
{
    private static RenderFragment InputFragment(string id) => builder =>
    {
        builder.OpenComponent<TmTextInput>(0);
        builder.AddAttribute(1, nameof(TmTextInput.Id), id);
        builder.CloseComponent();
    };

    // ── stable error id ─────────────────────────────────────────

    [Fact]
    public void FormField_ErrorParagraph_CarriesTheStableErrorId()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Id, "email-signup")
            .Add(c => c.Label, "Email")
            .Add(c => c.ErrorMessage, "Required"));

        var message = cut.Find(".tm-form-field-error");
        message.GetAttribute("id").Should().Be("email-signup-field-error");
        cut.Instance.ErrorId.Should().Be("email-signup-field-error",
            "the id is exposed publicly so hosts and wrapped controls can reference it");
    }

    [Fact]
    public void FormField_ErrorId_IsGeneratedAndStableAcrossRerenders_WithoutAnExplicitId()
    {
        var cut = Render<TmFormField>(p => p.Add(c => c.ErrorMessage, "Required"));

        var first = cut.Instance.ErrorId;
        first.Should().NotBeNullOrWhiteSpace().And.EndWith("-field-error");

        cut.Render(p => p.Add(c => c.ErrorMessage, "Still required"));

        cut.Instance.ErrorId.Should().Be(first, "the id must not change between renders");
        cut.Find(".tm-form-field-error").GetAttribute("id").Should().Be(first);
    }

    // ── AnnounceError ───────────────────────────────────────────

    [Fact]
    public void FormField_Error_DefaultAnnouncesViaRoleAlert()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Id, "email-signup")
            .Add(c => c.ErrorMessage, "Required"));

        cut.Find(".tm-form-field-error").GetAttribute("role").Should().Be("alert");
    }

    [Fact]
    public void FormField_AnnounceError_False_DropsOnlyTheRole()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Id, "email-signup")
            .Add(c => c.ErrorMessage, "Required")
            .Add(c => c.AnnounceError, false));

        var message = cut.Find(".tm-form-field-error");
        message.HasAttribute("role").Should().BeFalse(
            "focus moved to the field lets aria-describedby read the error; role=alert would announce it a second time");
        message.TextContent.Should().Contain("Required", "the message itself must still render");
        message.GetAttribute("id").Should().Be("email-signup-field-error", "the describedby link must survive");
        cut.Find(".tm-form-field").ClassList.Should().Contain("tm-form-field--error",
            "the error styling is independent of the announcement");
    }

    /// <summary>Freezes the unchanged default error DOM (full markup): only the id attribute is new.</summary>
    [Fact]
    public void FormField_Error_DefaultAnnounce_FullMarkupMatches()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Id, "snap-err")
            .Add(c => c.Label, "Email")
            .Add(c => c.ErrorMessage, "Enter a valid address"));

        cut.MarkupMatches(
            @"<div class=""tm-form-field tm-form-field--error  "">
                <label class=""tm-form-field-label"">Email</label>
                <div class=""tm-form-field-control""></div>
                <p id=""snap-err-field-error"" class=""tm-form-field-error"" role=""alert"">Enter a valid address</p>
              </div>");
    }

    /// <summary>With the announcement off, the same DOM holds except the role attribute disappears.</summary>
    [Fact]
    public void FormField_AnnounceError_False_FullMarkupMatchesExceptRole()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Id, "snap-mute")
            .Add(c => c.Label, "Email")
            .Add(c => c.ErrorMessage, "Enter a valid address")
            .Add(c => c.AnnounceError, false));

        cut.MarkupMatches(
            @"<div class=""tm-form-field tm-form-field--error  "">
                <label class=""tm-form-field-label"">Email</label>
                <div class=""tm-form-field-control""></div>
                <p id=""snap-mute-field-error"" class=""tm-form-field-error"">Enter a valid address</p>
              </div>");
    }

    // ── cascade to wrapped inputs ───────────────────────────────

    [Fact]
    public void FormField_WrappedInput_DescribesTheFieldError_WhenItHasNoOwnError()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Id, "email-signup")
            .Add(c => c.ErrorMessage, "Required")
            .AddChildContent(InputFragment("name-input")));

        cut.Find("input").GetAttribute("aria-describedby").Should().Be("email-signup-field-error",
            "the field error id is cascaded and the input with no own Error links it");
    }

    [Fact]
    public void FormField_WrappedInput_KeepsItsOwnErrorDescribedBy()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Id, "email-signup")
            .Add(c => c.ErrorMessage, "Field-level error")
            .AddChildContent(builder =>
            {
                builder.OpenComponent<TmTextInput>(0);
                builder.AddAttribute(1, nameof(TmTextInput.Id), "name-input");
                builder.AddAttribute(2, nameof(TmTextInput.Error), "own error");
                builder.CloseComponent();
            }));

        cut.Find("input").GetAttribute("aria-describedby").Should().Be("name-input-error",
            "the input's own error wins, exactly as when it renders standalone");
    }

    [Fact]
    public void FormField_WrappedInput_DescribesItsHelpAndThenTheFieldError()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Id, "email-signup")
            .Add(c => c.ErrorMessage, "Field-level error")
            .AddChildContent(builder =>
            {
                builder.OpenComponent<TmTextInput>(0);
                builder.AddAttribute(1, nameof(TmTextInput.Id), "name-input");
                builder.AddAttribute(2, nameof(TmTextInput.HelpText), "Some help");
                builder.CloseComponent();
            }));

        cut.Find("input").GetAttribute("aria-describedby").Should().Be("name-input-help email-signup-field-error",
            "both visible descriptions are listed in DOM order");
    }

    [Fact]
    public void FormField_NoError_WrappedInputHasNoFieldDescribedBy()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Id, "email-signup")
            .AddChildContent(InputFragment("name-input")));

        cut.Find("input").HasAttribute("aria-describedby").Should().BeFalse(
            "without an error nothing is described and the DOM is unchanged");
    }

    [Fact]
    public void FormField_EditContextError_CascadesToTheWrappedInputOnTheSameRender()
    {
        var model = new TestModel();
        var editContext = new EditContext(model);

        var cut = Render<TmFormField>(p => p
            .Add(c => c.Id, "email-signup")
            .AddCascadingValue(editContext)
            .Add(c => c.ValidationFor, () => model.Name)
            .AddChildContent(InputFragment("name-input")));

        cut.Find("input").HasAttribute("aria-describedby").Should().BeFalse();

        var messages = new ValidationMessageStore(editContext);
        messages.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        editContext.NotifyValidationStateChanged();

        cut.Find(".tm-form-field-error").GetAttribute("id").Should().Be("email-signup-field-error");
        cut.Find("input").GetAttribute("aria-describedby").Should().Be("email-signup-field-error",
            "the cascading value change re-renders the wrapped input in the same validation round");
    }

    private class TestModel
    {
        public string Name { get; set; } = string.Empty;
    }
}
