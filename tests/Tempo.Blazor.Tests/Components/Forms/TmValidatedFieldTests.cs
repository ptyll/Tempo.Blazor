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

    private static RenderFragment<EditContext> BuildFieldFragment(FormModel model, bool required = false)
    {
        return _ => BuildFieldMarkup(model, required);
    }

    private static RenderFragment BuildFieldMarkup(FormModel model, bool required = false)
    {
        return builder =>
        {
            builder.OpenComponent<TmValidatedField>(0);
            builder.AddAttribute(1, nameof(TmValidatedField.Label), "Name");
            builder.AddAttribute(2, nameof(TmValidatedField.Required), required);
            builder.AddAttribute(3, nameof(TmValidatedField.Value), model.Name);
            builder.AddAttribute(4, nameof(TmValidatedField.ValueChanged),
                EventCallback.Factory.Create<string>(model, v => model.Name = v));
            builder.AddAttribute(5, nameof(TmValidatedField.ValueExpression),
                (Expression<Func<string>>)(() => model.Name));
            builder.CloseComponent();
        };
    }

    private IRenderedComponent<EditForm> RenderInEditForm(FormModel model, bool required = false)
    {
        return Render<EditForm>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.ChildContent, BuildFieldFragment(model, required)));
    }

    private static EditContext ContextOf(IRenderedComponent<EditForm> cut) => cut.Instance.EditContext!;

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

        var editContext = ContextOf(cut);
        editContext.Validate();
        // A real validator (DataAnnotationsValidator/FluentValidationValidator) calls
        // NotifyValidationStateChanged after processing OnValidationRequested — the test tree has
        // no validator component, so we simulate that notification explicitly (N151).
        editContext.NotifyValidationStateChanged();

        cut.WaitForAssertion(() =>
            cut.Find("input").ClassList.Should().Contain("tm-input-valid"));
        cut.Find(".tm-input-validation-success").Should().NotBeNull();
    }

    [Fact]
    public void AsyncValidation_InFlight_DoesNotShowGreenBeforeStateChanges()
    {
        // N151: Validate() alone fires OnValidationRequested — an async validator (e.g.
        // FluentValidation) has ACCEPTED the request but not yet produced results. The field must
        // stay neutral until OnValidationStateChanged arrives; painting tm-input-valid in the
        // in-flight window is the green→red flicker this fix removes.
        var model = new FormModel { Name = "in-flight" };
        var cut = RenderInEditForm(model);

        var editContext = ContextOf(cut);
        editContext.Validate(); // request only — no NotifyValidationStateChanged yet

        cut.Find("input").ClassList.Should().NotContain("tm-input-valid",
            "a validation request whose results have not arrived must not paint the field valid");

        editContext.NotifyValidationStateChanged(); // the async pass now completed with no errors

        cut.WaitForAssertion(() =>
            cut.Find("input").ClassList.Should().Contain("tm-input-valid"));
    }

    [Fact]
    public void EditContextSwap_ReSubscribesValidationHandlers()
    {
        // N152: when the cascaded EditContext instance changes (e.g. the host re-renders with a
        // new Model), the SAME field instance must re-subscribe — otherwise it keeps listening
        // on the discarded context and never reacts to validation on the new one. A plain
        // (non-fixed) CascadingValue swap preserves the field instance, so this test exercises
        // the subscription itself, not component recreation.
        var model1 = new FormModel { Name = "first" };
        var editContext1 = new EditContext(model1);
        var cut = Render<CascadingValue<EditContext>>(parameters => parameters
            .Add(p => p.Value, editContext1)
            .Add(p => p.ChildContent, (RenderFragment)BuildFieldMarkup(model1)));
        var field1 = cut.FindComponent<TmValidatedField>().Instance;

        var model2 = new FormModel { Name = "second" };
        var editContext2 = new EditContext(model2);
        cut.Render(parameters => parameters
            .Add(p => p.Value, editContext2)
            .Add(p => p.ChildContent, (RenderFragment)BuildFieldMarkup(model2)));

        var field2 = cut.FindComponent<TmValidatedField>().Instance;
        field2.Should().BeSameAs(field1,
            "the cascading-value swap must reuse the field instance — a recreated field would " +
            "subscribe fresh in OnInitialized and this test would prove nothing");

        // (1) Resubscription: a completed validation pass on the NEW context must paint valid.
        editContext2.NotifyValidationStateChanged();
        cut.WaitForAssertion(() =>
            cut.Find("input").ClassList.Should().Contain("tm-input-valid",
                "the field must listen to the NEW EditContext — a handler still bound to the " +
                "discarded one would never see this notification"));

        // (2) FieldIdentifier must follow the new ValueExpression: messages stored under the new
        // model must surface as the error state.
        var store = new ValidationMessageStore(editContext2);
        store.Add(editContext2.Field(nameof(FormModel.Name)), "error on the new context");
        editContext2.NotifyValidationStateChanged();

        cut.WaitForAssertion(() =>
            cut.Find("input").ClassList.Should().Contain("tm-input-error"));
    }

    [Fact]
    public void EditContextSwap_ResetsValidationRan_UntilNewContextValidates()
    {
        // 20D carry-forward: _validationRan survived an EditContext swap, so the field kept
        // painting tm-input-valid on a context that never validated — the N151 defect class
        // run backwards (a carried-over flag instead of an early flag).
        var model1 = new FormModel { Name = "first" };
        var editContext1 = new EditContext(model1);
        var cut = Render<CascadingValue<EditContext>>(parameters => parameters
            .Add(p => p.Value, editContext1)
            .Add(p => p.ChildContent, (RenderFragment)BuildFieldMarkup(model1)));

        // A completed validation pass on the FIRST context paints the green frame.
        editContext1.NotifyValidationStateChanged();
        cut.WaitForAssertion(() =>
            cut.Find("input").ClassList.Should().Contain("tm-input-valid"));

        // Swap to a fresh context that has NOT validated — the field must go neutral.
        var model2 = new FormModel { Name = "second" };
        var editContext2 = new EditContext(model2);
        cut.Render(parameters => parameters
            .Add(p => p.Value, editContext2)
            .Add(p => p.ChildContent, (RenderFragment)BuildFieldMarkup(model2)));

        cut.Find("input").ClassList.Should().NotContain("tm-input-valid",
            "the new EditContext never ran a validation pass — a _validationRan carried over " +
            "from the discarded context would keep the green frame on an unvalidated field");
        cut.FindAll(".tm-input-validation-success").Should().BeEmpty();

        // …and a completed pass on the NEW context turns it green again.
        editContext2.NotifyValidationStateChanged();
        cut.WaitForAssertion(() =>
            cut.Find("input").ClassList.Should().Contain("tm-input-valid"));
    }

    [Fact]
    public void FieldWithValidationError_ShowsErrorState_NotGreen()
    {
        var model = new FormModel { Name = "bad" };
        var cut = RenderInEditForm(model);

        var editContext = ContextOf(cut);
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

        var editContext = ContextOf(cut);
        editContext.NotifyValidationStateChanged();

        cut.WaitForAssertion(() =>
            cut.Find("input").ClassList.Should().Contain("tm-input-valid"));
    }
}
