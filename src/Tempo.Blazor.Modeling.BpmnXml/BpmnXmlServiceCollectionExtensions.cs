using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>DI registration for the headless BPMN 2.0 XML converter.</summary>
public static class BpmnXmlServiceCollectionExtensions
{
    /// <summary>
    /// Registers the headless BPMN 2.0 XML importer (<see cref="IBpmnXmlImporter"/>) with the service
    /// collection. Idempotent — safe to call from host and library composition roots.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddTempoBpmnXml(this IServiceCollection services)
    {
        services.TryAddSingleton<IBpmnXmlImporter, BpmnXmlImporter>();
        return services;
    }
}
