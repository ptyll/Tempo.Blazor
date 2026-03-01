using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.FluentValidation;

namespace Tempo.Blazor.FluentValidation.Tests;

#region Test validators for scanning

public class CustomerModel
{
    public string Name { get; set; } = string.Empty;
}

public class CustomerValidator : AbstractValidator<CustomerModel>
{
    public CustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
    }
}

#endregion

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddTempoFluentValidation_RegistersValidatorsFromAssembly()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act — scan this test assembly which contains PersonValidator, OrderValidator, CustomerValidator
        services.AddTempoFluentValidation(typeof(PersonValidator).Assembly);

        // Assert
        var sp = services.BuildServiceProvider();
        sp.GetService<IValidator<PersonModel>>().Should().NotBeNull();
        sp.GetService<IValidator<PersonModel>>().Should().BeOfType<PersonValidator>();
    }

    [Fact]
    public void AddTempoFluentValidation_RegistersAllValidatorsInAssembly()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTempoFluentValidation(typeof(PersonValidator).Assembly);

        // Assert — all validators from the test assembly should be registered
        var sp = services.BuildServiceProvider();
        sp.GetService<IValidator<PersonModel>>().Should().NotBeNull();
        sp.GetService<IValidator<OrderModel>>().Should().NotBeNull();
        sp.GetService<IValidator<CustomerModel>>().Should().NotBeNull();
    }

    [Fact]
    public void AddTempoFluentValidation_ReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddTempoFluentValidation(typeof(PersonValidator).Assembly);

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddTempoFluentValidation_MultipleAssemblies_RegistersAll()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act — pass same assembly twice, should not error
        services.AddTempoFluentValidation(
            typeof(PersonValidator).Assembly,
            typeof(CustomerValidator).Assembly);

        // Assert
        var sp = services.BuildServiceProvider();
        sp.GetService<IValidator<PersonModel>>().Should().NotBeNull();
    }

    [Fact]
    public void AddTempoFluentValidation_EmptyAssembly_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert — assembly with no validators should not throw
        var act = () => services.AddTempoFluentValidation(typeof(string).Assembly);
        act.Should().NotThrow();
    }

    [Fact]
    public void AddTempoFluentValidation_ValidatorsAreScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTempoFluentValidation(typeof(PersonValidator).Assembly);

        // Act
        var sp = services.BuildServiceProvider();
        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();

        var v1 = scope1.ServiceProvider.GetService<IValidator<PersonModel>>();
        var v2 = scope2.ServiceProvider.GetService<IValidator<PersonModel>>();

        // Assert — different scopes should get different instances (scoped)
        v1.Should().NotBeNull();
        v2.Should().NotBeNull();
        v1.Should().NotBeSameAs(v2);
    }
}
