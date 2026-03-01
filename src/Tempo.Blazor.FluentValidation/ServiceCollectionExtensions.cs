using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Tempo.Blazor.FluentValidation;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to register
/// FluentValidation validators via assembly scanning.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Scans the specified assemblies for FluentValidation validators
    /// (types inheriting from <see cref="AbstractValidator{T}"/>)
    /// and registers them as scoped services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">Assemblies to scan for validators.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTempoFluentValidation(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var validatorTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract
                         && t.BaseType?.IsGenericType == true
                         && t.BaseType.GetGenericTypeDefinition() == typeof(AbstractValidator<>));

            foreach (var validatorType in validatorTypes)
            {
                var modelType = validatorType.BaseType!.GetGenericArguments()[0];
                var serviceType = typeof(IValidator<>).MakeGenericType(modelType);
                services.AddScoped(serviceType, validatorType);
            }
        }

        return services;
    }
}
