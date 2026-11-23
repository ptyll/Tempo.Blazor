using Bunit.Rendering;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Forms;

public class TmValidationSummaryTests : LocalizationTestBase
{
    private IRenderedComponent<ContainerFragment> RenderWithEditContext<TModel>(TModel model, Action<ComponentParameterCollectionBuilder<TmValidationSummary>>? configure = null)
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

    private IRenderedComponent<ContainerFragment> RenderInEditForm<TModel>(TModel model, Action<Dictionary<string, object?>>? addParams = null)
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

    /// <summary>
    /// The summary is a per-FORM list, not a per-FIELD one, so the same sentence attached to two fields —
    /// which is exactly what a cross-field rule ("Fill in at least one contact") and a server response
    /// merged into the live store both produce — was printed once per field. The user then reads the same
    /// instruction twice and cannot tell whether two things are wrong or one.
    /// </summary>
    [Fact]
    public void ValidationSummary_SameMessageOnTwoFields_IsListedOnce()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model);

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Fill in at least one contact.");
        messageStore.Add(editContext.Field(nameof(TestModel.Email)), "Fill in at least one contact.");
        editContext.NotifyValidationStateChanged();

        var items = cut.FindAll(".tm-validation-summary-list li");
        items.Count.Should().Be(1, "the same sentence twice tells the user nothing the first one did not");
        items[0].TextContent.Should().Contain("Fill in at least one contact.");
    }

    /// <summary>
    /// The de-duplication must not swallow genuinely different messages, and must not reorder them: the
    /// list is read top to bottom and the first entry is the one a screen reader announces first.
    /// </summary>
    [Fact]
    public void ValidationSummary_KeepsDistinctMessages_InOrder()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model);

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        messageStore.Add(editContext.Field(nameof(TestModel.Email)), "Email is invalid.");
        editContext.NotifyValidationStateChanged();

        var items = cut.FindAll(".tm-validation-summary-list li");
        items.Count.Should().Be(2);
        items[0].TextContent.Should().Contain("Name is required.");
        items[1].TextContent.Should().Contain("Email is invalid.");
    }

    /// <summary>
    /// Visibility keys off the same collection the list renders, so it has to survive the de-duplication:
    /// a summary that hides itself because "one duplicate is not really an error" would be a data-loss bug
    /// dressed as a cosmetic one.
    /// </summary>
    [Fact]
    public void ValidationSummary_StaysVisible_WhenTheOnlyErrorIsDuplicated()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model);

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Duplicated.");
        messageStore.Add(editContext.Field(nameof(TestModel.Email)), "Duplicated.");
        editContext.NotifyValidationStateChanged();

        cut.Find(".tm-validation-summary").Should().NotBeNull();
    }

    // ── Fáze 7: GroupBy, OnMessageClick, ItemTemplate ──

    [Fact]
    public void ValidationSummary_GroupBy_RendersGroupTitles()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model, p =>
        {
            p["GroupBy"] = (Func<string, string>)(message =>
                message.Contains("Name") ? "Identity" : "Contact");
        });

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        messageStore.Add(editContext.Field(nameof(TestModel.Email)), "Email is invalid.");
        editContext.NotifyValidationStateChanged();

        var groups = cut.FindAll(".tm-validation-summary-group");
        groups.Count.Should().Be(2);
        groups[0].QuerySelector(".tm-validation-summary-group-title")!.TextContent.Should().Be("Identity");
        groups[0].QuerySelectorAll("li").Should().ContainSingle(li => li.TextContent.Contains("Name is required."));
        groups[1].QuerySelector(".tm-validation-summary-group-title")!.TextContent.Should().Be("Contact");
    }

    /// <summary>
    /// WCAG 1.3.1: each group is exposed as a named region — the group element carries
    /// role="group" with aria-labelledby pointing at the title element's id, so a screen reader
    /// announces the group title with its list instead of reading the title as loose text.
    /// </summary>
    [Fact]
    public void ValidationSummary_GroupBy_GroupHasRoleAndLabelledbyPointingAtTheTitleId()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model, p =>
        {
            p["GroupBy"] = (Func<string, string>)(message =>
                message.Contains("Name") ? "Identity" : "Contact");
        });

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        messageStore.Add(editContext.Field(nameof(TestModel.Email)), "Email is invalid.");
        editContext.NotifyValidationStateChanged();

        foreach (var group in cut.FindAll(".tm-validation-summary-group"))
        {
            group.GetAttribute("role").Should().Be("group");
            var labelledBy = group.GetAttribute("aria-labelledby");
            labelledBy.Should().NotBeNullOrWhiteSpace();
            var title = group.QuerySelector(".tm-validation-summary-group-title")!;
            title.Id.Should().Be(labelledBy, "aria-labelledby must point at the group title's id");
        }
    }

    [Fact]
    public void ValidationSummary_WithoutGroupBy_RendersFlatList()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model);

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        editContext.NotifyValidationStateChanged();

        cut.FindAll(".tm-validation-summary-group").Should().BeEmpty();
        cut.FindAll(".tm-validation-summary-list li").Should().HaveCount(1);
    }

    [Fact]
    public void ValidationSummary_OnMessageClick_RendersButtons_AndRaisesMessage()
    {
        string? clicked = null;
        var model = new TestModel();
        var cut = RenderInEditForm(model, p =>
        {
            p["OnMessageClick"] = EventCallback.Factory.Create<string>(this, m => clicked = m);
        });

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        editContext.NotifyValidationStateChanged();

        var button = cut.Find(".tm-validation-summary-item");
        button.Should().NotBeNull("a clickable message is a real button — one native activation path");
        button.Click();
        clicked.Should().Be("Name is required.");
    }

    [Fact]
    public void ValidationSummary_WithoutOnMessageClick_RendersPlainListItems()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model);

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        editContext.NotifyValidationStateChanged();

        cut.FindAll(".tm-validation-summary-item").Should().BeEmpty();
    }

    [Fact]
    public void ValidationSummary_ItemTemplate_ReplacesTheMessageText()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model, p =>
        {
            p["ItemTemplate"] = (RenderFragment<string>)(message => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "my-message-item");
                builder.AddContent(2, $"[{message}]");
                builder.CloseElement();
            });
        });

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        editContext.NotifyValidationStateChanged();

        cut.Find(".my-message-item").TextContent.Should().Contain("[Name is required.]");
    }

    [Fact]
    public void ValidationSummary_MixingGroupByAndGroupItemsBy_Throws()
    {
        var model = new TestModel();
        Action act = () => RenderInEditForm(model, p =>
        {
            p["GroupBy"] = (Func<string, string>)(_ => "A");
            p["GroupItemsBy"] = (Func<ValidationSummaryItem, string>)(_ => "B");
        });

        act.Should().Throw<ArgumentException>().WithMessage("*GroupBy*GroupItemsBy*");
    }

    [Fact]
    public void ValidationSummary_MixingOnMessageClickAndOnItemClick_Throws()
    {
        var model = new TestModel();
        Action act = () => RenderInEditForm(model, p =>
        {
            p["OnMessageClick"] = EventCallback.Factory.Create<string>(this, _ => { });
            p["OnItemClick"] = EventCallback.Factory.Create<ValidationSummaryItem>(this, _ => { });
        });

        act.Should().Throw<ArgumentException>().WithMessage("*OnMessageClick*OnItemClick*");
    }

    [Fact]
    public void ValidationSummary_MixingItemTemplateAndItemTemplateWithField_Throws()
    {
        var model = new TestModel();
        Action act = () => RenderInEditForm(model, p =>
        {
            p["ItemTemplate"] = (RenderFragment<string>)(_ => builder => { });
            p["ItemTemplateWithField"] = (RenderFragment<ValidationSummaryItem>)(_ => builder => { });
        });

        act.Should().Throw<ArgumentException>().WithMessage("*ItemTemplate*ItemTemplateWithField*");
    }

    [Fact]
    public void ValidationSummary_FieldIdentity_ListsSameMessageOnTwoFieldsTwice()
    {
        var model = new TestModel();
        ValidationSummaryItem? clicked = null;
        var cut = RenderInEditForm(model, p =>
        {
            p["OnItemClick"] = EventCallback.Factory.Create<ValidationSummaryItem>(this, item => clicked = item);
        });

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Fill in at least one contact.");
        messageStore.Add(editContext.Field(nameof(TestModel.Email)), "Fill in at least one contact.");
        editContext.NotifyValidationStateChanged();

        var buttons = cut.FindAll(".tm-validation-summary-item");
        buttons.Count.Should().Be(2, "field identity keeps one row per (Field, Message)");
        buttons[0].Click();
        clicked.Should().NotBeNull();
        clicked!.Field.FieldName.Should().Be(nameof(TestModel.Name));
        clicked.Message.Should().Be("Fill in at least one contact.");
    }

    [Fact]
    public void ValidationSummary_GroupItemsBy_RendersGroupTitlesFromFields()
    {
        var model = new TestModel();
        var cut = RenderInEditForm(model, p =>
        {
            p["GroupItemsBy"] = (Func<ValidationSummaryItem, string>)(item =>
                item.Field.FieldName == nameof(TestModel.Name) ? "Identity" : "Contact");
        });

        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        var messageStore = new ValidationMessageStore(editContext);
        messageStore.Add(editContext.Field(nameof(TestModel.Name)), "Name is required.");
        messageStore.Add(editContext.Field(nameof(TestModel.Email)), "Email is invalid.");
        editContext.NotifyValidationStateChanged();

        var groups = cut.FindAll(".tm-validation-summary-group");
        groups.Count.Should().Be(2);
        groups[0].QuerySelector(".tm-validation-summary-group-title")!.TextContent.Should().Be("Identity");
        groups[1].QuerySelector(".tm-validation-summary-group-title")!.TextContent.Should().Be("Contact");
    }

    private class TestModel
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}
