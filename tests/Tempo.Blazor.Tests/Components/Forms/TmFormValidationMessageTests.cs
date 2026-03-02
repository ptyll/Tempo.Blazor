using System.Linq.Expressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Forms;

/// <summary>TDD tests for TmFormValidationMessage.</summary>
public class TmFormValidationMessageTests : LocalizationTestBase
{
    private sealed class FormModel
    {
        public string Name { get; set; } = "";
    }

    private IRenderedFragment RenderWithEditForm(FormModel model, Expression<Func<object>> forExpr, string? cssClass = null)
    {
        return Render(builder =>
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddAttribute(1, nameof(EditForm.Model), model);
            builder.AddAttribute(2, nameof(EditForm.ChildContent), (RenderFragment<EditContext>)(context =>
                (RenderTreeBuilder b) =>
                {
                    b.OpenComponent<TmFormValidationMessage>(0);
                    b.AddAttribute(1, nameof(TmFormValidationMessage.For), forExpr);
                    if (cssClass is not null)
                        b.AddAttribute(2, nameof(TmFormValidationMessage.Class), cssClass);
                    b.CloseComponent();
                }));
            builder.CloseComponent();
        });
    }

    private static void AddFieldError(IRenderedFragment cut, string fieldName, params string[] errors)
    {
        var editContext = cut.FindComponent<EditForm>().Instance.EditContext!;
        var field = editContext.Field(fieldName);
        var store = new ValidationMessageStore(editContext);
        foreach (var error in errors)
        {
            store.Add(field, error);
        }
        editContext.NotifyValidationStateChanged();
    }

    [Fact]
    public void TmFormValidationMessage_Hidden_When_No_Errors()
    {
        var model = new FormModel();
        var cut = RenderWithEditForm(model, () => model.Name);

        cut.FindAll(".tm-form-validation-message").Count.Should().Be(0);
    }

    [Fact]
    public void TmFormValidationMessage_Shows_Error_Message()
    {
        var model = new FormModel();
        var cut = RenderWithEditForm(model, () => model.Name);
        AddFieldError(cut, nameof(FormModel.Name), "Name is required");

        cut.Find(".tm-form-validation-message").Should().NotBeNull();
        cut.Find(".tm-form-validation-message-text").TextContent.Should().Contain("Name is required");
    }

    [Fact]
    public void TmFormValidationMessage_Shows_Icon()
    {
        var model = new FormModel();
        var cut = RenderWithEditForm(model, () => model.Name);
        AddFieldError(cut, nameof(FormModel.Name), "Error");

        cut.Find(".tm-form-validation-message-icon").Should().NotBeNull();
    }

    [Fact]
    public void TmFormValidationMessage_Has_Alert_Role()
    {
        var model = new FormModel();
        var cut = RenderWithEditForm(model, () => model.Name);
        AddFieldError(cut, nameof(FormModel.Name), "Error");

        cut.Find(".tm-form-validation-message").GetAttribute("role").Should().Be("alert");
    }

    [Fact]
    public void TmFormValidationMessage_Shows_Multiple_Messages()
    {
        var model = new FormModel();
        var cut = RenderWithEditForm(model, () => model.Name);
        AddFieldError(cut, nameof(FormModel.Name), "Error one", "Error two");

        cut.FindAll(".tm-form-validation-message-text").Count.Should().Be(2);
    }

    [Fact]
    public void TmFormValidationMessage_Applies_Custom_Class()
    {
        var model = new FormModel();
        var cut = RenderWithEditForm(model, () => model.Name, "custom-class");
        AddFieldError(cut, nameof(FormModel.Name), "Error");

        cut.Find(".tm-form-validation-message").ClassList.Should().Contain("custom-class");
    }
}
