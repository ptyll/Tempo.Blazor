using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.AI.Markdown;

namespace Tempo.Blazor.Configuration;

/// <summary>Extension methods for registering the Tempo.Blazor AI component group.</summary>
public static class AIServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services required by the AI conversation components.
    /// </summary>
    /// <remarks>
    /// Every registration uses <c>TryAdd*</c>, so a host that registered its own service (or a test that
    /// pre-registers a fake) always wins and calling the method twice is a no-op. The method never
    /// registers a <c>TimeProvider</c>: a component resolves it as <c>GetService&lt;TimeProvider&gt;() ??
    /// TimeProvider.System</c>, so the host keeps the last word.
    /// </remarks>
    /// <param name="services">The service collection to add the AI services to.</param>
    /// <returns>The same service collection, so registration calls can be chained.</returns>
    public static IServiceCollection AddTempoBlazorAI(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTempoBlazor();

        // The safe markdown pipeline behind TmMarkdownView. Stateless (the Markdig pipeline is built once
        // per process), so a singleton is enough and a host may replace it with a subclass.
        services.TryAddSingleton<AiMarkdownRenderer>();

        return services;
    }
}
