using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Forms;

public class TmValidationSummaryTests : LocalizationTestBase
{
    private IRenderedFragment RenderWithEditContext<TModel>(TModel model, Action<ComponentParameterCollectionBuilder<TmValidationSummary>>? configure = null)
        where TModel : class
    {
        return Render(builder =>
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddAttribute(1, nameof(EditForm.Model), model);
            builder.AddAttribute(2, nameof(EditForm.ChildContent),
                (RenderFragment<EditContext>)(context => childBuilder =>
                {
                    childBuilder.OpenComponent<TmValidationSummary>(0);
                    configure?.Invoke(new ComponentParameterCollectionBuilder<TmValidationSummary>());
                    childBuilder.CloseComponent();
                }));
            builder.CloseComponent();
        });
    }

    private IRenderedFragment RenderInEditForm<TModel>(TModel model, Action<Dictionary<string, object?>>? addParams = null)
        where TModel : class
    {
        return Render(builder =>
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddAttribute(1, nameof(EditForm.Model), model);
            builder.AddAttribute(2, nameof(EditForm.ChildContent),
                (RenderFragment<EditContext>)(context => childBuilder =>
                {
                    childBuilder.OpenComponent<TmValidationSummary>(0);
                    var extraParams = new Dictionary<string, object?>();
                    addParams?.Invoke(extraParams);
                    var seq = 1;
                    foreach (var (key, value) in extraParams)
                    {
                        childBuilder.AddAttribute(seq++, key, value);
                    }
                    childBuilder.CloseComponent();
                }));
            builder.CloseComponent();
        });
    }

    [Fact]
    public void ValidationSummary_NoErrors_NotVisible()
    {
        // Arrange & Act — valid model, no validation triggered
        var model = new TestModel { Name = "Valid" };
        var cut = RenderInEditForm(model);

        // Assert — component should not render the error container
        cut.FindAll(".tm-validation-summary").Should().BeEmpty();
    }

    [Fact]
    public void ValidationSummary_WithErrors_Visible()
    {
        // Arrange — model with empty required field
        var model = new TestModel();
        var cut = RenderInEditForm(model);

        // Manually add validation errors to EditContext
        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        editContext.NotifyValidationStateChanged();

        // Assert
        var summary = cut.Find(".tm-validation-summary");
        summary.Should().NotBeNull();
    }

    [Fact]
    public void ValidationSummary_ShowsErrorsList()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model);

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        messageStore.Add(editContext.Field(nameof(TestModel.Email)), "Email is invalid.");
        editContext.NotifyValidationStateChanged();

        // Assert — should have 2 error items in the list
        var items = cut.FindAll(".tm-validation-summary-list li");
        items.Count.Should().Be(2);
        items[0].TextContent.Should().Contain("Name is required.");
        items[1].TextContent.Should().Contain("Email is invalid.");
    }

    [Fact]
    public void ValidationSummary_ShowErrorsList_False_HidesList()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model, p =>
        {
            p["ShowErrorsList"] = false;
        });

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        editContext.NotifyValidationStateChanged();

        // Assert — summary should exist but no list items
        cut.Find(".tm-validation-summary").Should().NotBeNull();
        cut.FindAll(".tm-validation-summary-list").Should().BeEmpty();
    }

    [Fact]
    public void ValidationSummary_CustomTitle_Rendered()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model, p =>
        {
            p["Title"] = "Custom Error Title";
        });

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Error");
        editContext.NotifyValidationStateChanged();

        // Assert — custom title should be rendered
        cut.Find(".tm-validation-summary-title").TextContent.Should().Contain("Custom Error Title");
    }

    [Fact]
    public void ValidationSummary_DefaultTitle_FromLocalizer()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model);

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Error");
        editContext.NotifyValidationStateChanged();

        // Assert — default title from localizer
        var title = cut.Find(".tm-validation-summary-title").TextContent;
        title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidationSummary_ManualMode_HiddenWhenShowIsFalse()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model, p =>
        {
            p["ManualMode"] = true;
            p["Show"] = false;
        });

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Error");
        editContext.NotifyValidationStateChanged();

        // Assert — ManualMode=true + Show=false → hidden even with errors
        cut.FindAll(".tm-validation-summary").Should().BeEmpty();
    }

    [Fact]
    public void ValidationSummary_ManualMode_VisibleWhenShowIsTrue()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model, p =>
        {
            p["ManualMode"] = true;
            p["Show"] = true;
        });

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Error");
        editContext.NotifyValidationStateChanged();

        // Assert — ManualMode=true + Show=true + errors → visible
        cut.Find(".tm-validation-summary").Should().NotBeNull();
    }

    [Fact]
    public void ValidationSummary_CustomClass_Applied()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model, p =>
        {
            p["Class"] = "my-custom-class";
        });

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Error");
        editContext.NotifyValidationStateChanged();

        // Assert
        var summary = cut.Find(".tm-validation-summary");
        summary.ClassList.Should().Contain("my-custom-class");
    }

    [Fact]
    public void ValidationSummary_HasIcon()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model);

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Error");
        editContext.NotifyValidationStateChanged();

        // Assert — should have an icon
        cut.Find(".tm-validation-summary-icon").Should().NotBeNull();
    }

    [Fact]
    public void ValidationSummary_ErrorsCleared_HidesComponent()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model);

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);

        // Add errors
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Error");
        editContext.NotifyValidationStateChanged();
        cut.Find(".tm-validation-summary").Should().NotBeNull();

        // Clear errors
        messageStore.Clear();
        editContext.NotifyValidationStateChanged();

        // Assert — component should hide
        cut.FindAll(".tm-validation-summary").Should().BeEmpty();
    }

    [Fact]
    public void ValidationSummary_HasRoleAlert()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model);

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Error");
        editContext.NotifyValidationStateChanged();

        // Assert — should have role="alert" for accessibility
        var summary = cut.Find(".tm-validation-summary");
        summary.GetAttribute("role").Should().Be("alert");
    }

    private class TestModel
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}
