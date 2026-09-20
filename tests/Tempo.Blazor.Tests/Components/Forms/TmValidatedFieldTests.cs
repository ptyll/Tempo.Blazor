using System.Linq.Expressions;
using Bunit;
using Bunit.Rendering;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Forms;

/// <summary>
/// TDD tests for <see cref="TmValidatedField"/> — application gap register #10: <c>Required</c>
/// used to paint only the asterisk on the label and never reached the inner input, so
/// <c>aria-required</c> was missing; and <c>IsValid</c> meant "non-empty and error-free", so a
/// field the user merely typed into got the green <c>tm-input-valid</c> frame without anyone
/// having validated it. "Valid" now means "the EditContext ran a validation pass and this field
/// has no messages"; before that the field is neutral.
/// </summary>
public class TmValidatedFieldTests : LocalizationTestBase
{
    private sealed class FormModel
    {
        public string Name { get; set; } = "";
    }

    private IRenderedComponent<ContainerFragment> RenderInEditForm(FormModel model, bool required = false)
    {
        return Render(builder =>
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddAttribute(1, nameof(EditForm.Model), model);
            builder.AddAttribute(2, nameof(EditForm.ChildContent), (RenderFragment<EditContext>)(_ =>
                (RenderTreeBuilder b) =>
                {
                    b.OpenComponent<TmValidatedField>(0);
                    b.AddAttribute(1, nameof(TmValidatedField.Label), "Name");
                    b.AddAttribute(2, nameof(TmValidatedField.Required), required);
                    b.AddAttribute(3, nameof(TmValidatedField.Value), model.Name);
                    b.AddAttribute(4, nameof(TmValidatedField.ValueChanged),
                        EventCallback.Factory.Create<string>(this, v => model.Name = v));
                    b.AddAttribute(5, nameof(TmValidatedField.ValueExpression),
                        (Expression<Func<string>>)(() => model.Name));
                    b.CloseComponent();
                }));
            builder.CloseComponent();
        });
    }

    [Fact]
    public void Required_IsForwardedToTheInnerInput()
    {
        var cut = RenderInEditForm(new FormModel(), required: true);

        var input = cut.Find("input");
        input.GetAttribute("aria-required").Should().Be("true",
            "Required must reach the inner TmTextInput — a standalone input has emitted " +
            "aria-required since 2.8.24 and the wrapper was dropping it");
        input.GetAttribute("required").Should().NotBeNull();
    }

    [Fact]
    public void NotRequired_Input_HasNoAriaRequired()
    {
        var cut = RenderInEditForm(new FormModel(), required: false);

        cut.Find("input").GetAttribute("aria-required").Should().BeNull();
    }

    [Fact]
    public void UnvalidatedField_ShowsNoGreenFrame_EvenWithText()
    {
        var model = new FormModel { Name = "typed but never validated" };
        var cut = RenderInEditForm(model);

        cut.Find("input").ClassList.Should().NotContain("tm-input-valid",
            "pole, které neprošlo validací, nemá dostat zelený rámeček — neprázdnost není " +
            "důkaz platnosti (gap register #10b)");
        cut.FindAll(".tm-input-validation-success").Should().BeEmpty();
    }

    [Fact]
    public void ValidatedField_WithoutErrors_GetsGreenFrame()
    {
        var model = new FormModel { Name = "valid value" };
        var cut = RenderInEditForm(model);

        var editContext = cut.FindComponent<EditForm>().Instance.EditContext!;
        editContext.Validate(); // raises OnValidationRequested + OnValidationStateChanged

        cut.WaitForAssertion(() =>
            cut.Find("input").ClassList.Should().Contain("tm-input-valid"));
        cut.Find(".tm-input-validation-success").Should().NotBeNull();
    }

    [Fact]
    public void FieldWithValidationError_ShowsErrorState_NotGreen()
    {
        var model = new FormModel { Name = "bad" };
        var cut = RenderInEditForm(model);

        var editContext = cut.FindComponent<EditForm>().Instance.EditContext!;
        var store = new ValidationMessageStore(editContext);
        store.Add(editContext.Field(nameof(FormModel.Name)), "Name is not valid");
        editContext.NotifyValidationStateChanged();

        cut.WaitForAssertion(() =>
            cut.Find("input").ClassList.Should().Contain("tm-input-error"));
        cut.Find("input").ClassList.Should().NotContain("tm-input-valid");
        cut.Find("input").GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void ValidationOnAnotherField_DoesNotInvalidateStateTracking()
    {
        // ValidationStateChanged fired for the whole context marks a pass having happened —
        // a field with no messages after a validation pass legitimately reports valid.
        var model = new FormModel { Name = "x" };
        var cut = RenderInEditForm(model);

        var editContext = cut.FindComponent<EditForm>().Instance.EditContext!;
        editContext.NotifyValidationStateChanged();

        cut.WaitForAssertion(() =>
            cut.Find("input").ClassList.Should().Contain("tm-input-valid"));
    }
}
