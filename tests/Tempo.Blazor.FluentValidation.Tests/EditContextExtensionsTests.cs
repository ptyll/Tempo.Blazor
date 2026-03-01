using FluentValidation;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.FluentValidation;

namespace Tempo.Blazor.FluentValidation.Tests;

public class EditContextExtensionsTests
{
    private IServiceProvider BuildServiceProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddFluentValidation_ReturnsEditContext()
    {
        // Arrange
        var model = new PersonModel { FirstName = "John", Email = "john@example.com", Age = 30 };
        var editContext = new EditContext(model);
        var sp = BuildServiceProvider(s => s.AddSingleton<IValidator<PersonModel>>(new PersonValidator()));

        // Act
        var result = editContext.AddFluentValidation(sp);

        // Assert
        result.Should().BeSameAs(editContext);
    }

    [Fact]
    public void Validate_WithFluentValidation_UsesFluentRules()
    {
        // Arrange
        var model = new PersonModel { FirstName = "", Email = "bad", Age = 10 };
        var editContext = new EditContext(model);
        var sp = BuildServiceProvider(s => s.AddSingleton<IValidator<PersonModel>>(new PersonValidator()));

        editContext.AddFluentValidation(sp);

        // Act
        var isValid = editContext.Validate();

        // Assert
        isValid.Should().BeFalse();
        var firstNameErrors = editContext.GetValidationMessages(editContext.Field(nameof(PersonModel.FirstName)));
        firstNameErrors.Should().NotBeEmpty();
        var emailErrors = editContext.GetValidationMessages(editContext.Field(nameof(PersonModel.Email)));
        emailErrors.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_WithValidModel_ReturnsTrue()
    {
        // Arrange
        var model = new PersonModel { FirstName = "John", Email = "john@example.com", Age = 30 };
        var editContext = new EditContext(model);
        var sp = BuildServiceProvider(s => s.AddSingleton<IValidator<PersonModel>>(new PersonValidator()));

        editContext.AddFluentValidation(sp);

        // Act
        var isValid = editContext.Validate();

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void AddFluentValidation_WithoutValidator_DoesNotThrow()
    {
        // Arrange — no validator registered
        var model = new PersonModel { FirstName = "", Email = "bad", Age = 10 };
        var editContext = new EditContext(model);
        var sp = BuildServiceProvider();

        // Act & Assert — should not throw
        var act = () => editContext.AddFluentValidation(sp);
        act.Should().NotThrow();

        // Validate also should not throw (no validator = no errors added)
        editContext.AddFluentValidation(sp);
        var isValid = editContext.Validate();
        isValid.Should().BeTrue(); // No validator = no errors = valid
    }
}
