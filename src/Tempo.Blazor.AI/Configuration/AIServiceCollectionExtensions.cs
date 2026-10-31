using Microsoft.Extensions.DependencyInjection;

namespace Tempo.Blazor.Configuration;

/// <summary>Extension methods for registering the Tempo.Blazor AI component group.</summary>
public static class AIServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services required by the AI conversation components.
    /// </summary>
    /// <remarks>
    /// Registers the core Tempo.Blazor services the AI components build on; later phases add the AI
    /// renderer services here. The model and streaming contracts are planned in
    /// <c>Tempo.Blazor.Abstractions</c> (<c>Tempo.Blazor.AI.Models</c> / <c>Tempo.Blazor.AI.Services</c>,
    /// plan 7f21f26b Fáze 2), so a backend will be able to use them without Blazor, and no LLM SDK is
    /// referenced from here.
    /// </remarks>
    /// <param name="services">The service collection to add the AI services to.</param>
    /// <returns>The same service collection, so registration calls can be chained.</returns>
    public static IServiceCollection AddTempoBlazorAI(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTempoBlazor();
        return services;
    }
}
