using FluentValidation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.FluentValidation;

namespace Tempo.Blazor.FluentValidation.Tests;

#region Test models

public class PersonModel
{
    public string FirstName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Age { get; set; }
}

public class PersonValidator : AbstractValidator<PersonModel>
{
    public PersonValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Age).InclusiveBetween(18, 120);
    }
}

public class AddressModel
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}

public class OrderModel
{
    public string OrderNumber { get; set; } = string.Empty;
    public AddressModel ShippingAddress { get; set; } = new();
}

public class OrderValidator : AbstractValidator<OrderModel>
{
    public OrderValidator()
    {
        RuleFor(x => x.OrderNumber).NotEmpty();
        RuleFor(x => x.ShippingAddress.Street).NotEmpty();
        RuleFor(x => x.ShippingAddress.City).NotEmpty();
    }
}

#endregion

public class FluentValidationValidatorTests : TestContext
{
    private IRenderedFragment RenderEditFormWithValidator<TModel>(TModel model) where TModel : class
    {
        return Render(builder =>
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddAttribute(1, nameof(EditForm.Model), model);
            builder.AddAttribute(2, nameof(EditForm.ChildContent),
                (RenderFragment<EditContext>)(context => childBuilder =>
                {
                    childBuilder.OpenComponent<FluentValidationValidator>(0);
                    childBuilder.CloseComponent();
                    childBuilder.OpenComponent<ValidationSummary>(1);
                    childBuilder.CloseComponent();
                }));
            builder.CloseComponent();
        });
    }

    [Fact]
    public void Validator_Invalid_AddsMessagesToEditContext()
    {
        // Arrange
        Services.AddSingleton<IValidator<PersonModel>>(new PersonValidator());
        var model = new PersonModel { FirstName = "", Email = "bad", Age = 10 };

        // Act
        var cut = RenderEditFormWithValidator(model);
        cut.Find("form").Submit();

        // Assert — all 3 fields should have validation errors in summary
        var errors = cut.FindAll(".validation-errors li");
        errors.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Validator_Valid_NoMessagesInEditContext()
    {
        // Arrange
        Services.AddSingleton<IValidator<PersonModel>>(new PersonValidator());
        var model = new PersonModel { FirstName = "John", Email = "john@example.com", Age = 30 };

        // Act
        var cut = RenderEditFormWithValidator(model);
        cut.Find("form").Submit();

        // Assert
        var errors = cut.FindAll(".validation-errors li");
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Validator_SingleField_OnlyValidatesThatField()
    {
        // Arrange
        Services.AddSingleton<IValidator<PersonModel>>(new PersonValidator());
        var model = new PersonModel { FirstName = "", Email = "bad", Age = 10 };

        // Act
        var cut = RenderEditFormWithValidator(model);

        // Get the EditContext and trigger field-level validation for FirstName only
        // Must use InvokeAsync to be on the Blazor render context
        var editForm = cut.FindComponent<EditForm>();
        var editContext = editForm.Instance.EditContext!;
        await cut.InvokeAsync(() =>
            editContext.NotifyFieldChanged(editContext.Field(nameof(PersonModel.FirstName))));

        // Assert — FirstName should have error from field-level validation
        var firstNameErrors = editContext.GetValidationMessages(editContext.Field(nameof(PersonModel.FirstName)));
        firstNameErrors.Should().NotBeEmpty();
    }

    [Fact]
    public void Validator_UsesInjectedValidator_FromDI()
    {
        // Arrange — register through DI
        Services.AddSingleton<IValidator<PersonModel>>(new PersonValidator());
        var model = new PersonModel { FirstName = "", Email = "bad", Age = 10 };

        // Act — should find and use the validator from DI
        var cut = RenderEditFormWithValidator(model);
        cut.Find("form").Submit();

        // Assert — validation messages should be present (validator was found via DI)
        var errors = cut.FindAll(".validation-errors li");
        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Validator_NoValidatorRegistered_DoesNotThrow()
    {
        // Arrange — NO validator registered for PersonModel
        var model = new PersonModel { FirstName = "John", Email = "john@example.com", Age = 30 };

        // Act & Assert — should not throw even without a registered validator
        var act = () => RenderEditFormWithValidator(model);
        act.Should().NotThrow();

        // Submit also should not throw
        var cut = act();
        var submitAct = () => cut.Find("form").Submit();
        submitAct.Should().NotThrow();
    }

    [Fact]
    public void Validator_NestedProperties_ValidatedCorrectly()
    {
        // Arrange
        Services.AddSingleton<IValidator<OrderModel>>(new OrderValidator());
        var model = new OrderModel
        {
            OrderNumber = "",
            ShippingAddress = new AddressModel { Street = "", City = "" }
        };

        // Act
        var cut = RenderEditFormWithValidator(model);
        cut.Find("form").Submit();

        // Assert — should have errors for OrderNumber, ShippingAddress.Street, ShippingAddress.City
        var errors = cut.FindAll(".validation-errors li");
        errors.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Validator_RequiresEditContext_ThrowsWithoutEditForm()
    {
        // Arrange
        Services.AddSingleton<IValidator<PersonModel>>(new PersonValidator());

        // Act & Assert — rendering without EditForm should throw
        var act = () => Render(builder =>
        {
            builder.OpenComponent<FluentValidationValidator>(0);
            builder.CloseComponent();
        });
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validator_FixError_ClearsValidationOnResubmit()
    {
        // Arrange
        Services.AddSingleton<IValidator<PersonModel>>(new PersonValidator());
        var model = new PersonModel { FirstName = "", Email = "bad", Age = 10 };

        var cut = RenderEditFormWithValidator(model);
        cut.Find("form").Submit();

        // Confirm there are errors
        cut.FindAll(".validation-errors li").Should().NotBeEmpty();

        // Fix the model
        model.FirstName = "John";
        model.Email = "john@example.com";
        model.Age = 30;

        // Re-submit
        cut.Find("form").Submit();

        // Assert — errors should be cleared
        cut.FindAll(".validation-errors li").Should().BeEmpty();
    }
}
