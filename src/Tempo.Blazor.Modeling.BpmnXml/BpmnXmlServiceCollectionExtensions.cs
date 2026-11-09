using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>DI registration for the headless BPMN 2.0 XML converter.</summary>
public static class BpmnXmlServiceCollectionExtensions
{
    /// <summary>
    /// Registers the headless BPMN 2.0 XML converter — importer (<see cref="IBpmnXmlImporter"/>) and
    /// exporter (<see cref="IBpmnXmlExporter"/>) — with the service collection. Idempotent — safe to
    /// call from host and library composition roots.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddTempoBpmnXml(this IServiceCollection services)
    {
        services.TryAddSingleton<IBpmnXmlImporter, BpmnXmlImporter>();
        services.TryAddSingleton<IBpmnXmlExporter, BpmnXmlExporter>();
        // Built-in tool dialect plugins (Camunda/Signavio/Bizagi). Registered as a set so hosts can
        // add their own IBpmnToolExtension implementations without replacing the built-ins.
        services.TryAddEnumerable(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<IBpmnToolExtension, BpmnCamundaToolExtension>());
        services.TryAddEnumerable(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<IBpmnToolExtension, BpmnSignavioToolExtension>());
        services.TryAddEnumerable(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<IBpmnToolExtension, BpmnBizagiToolExtension>());
        return services;
    }
}
