using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Configuration;

/// <summary>
/// Extension methods for registering Tempo.Blazor modeling editor services.
/// </summary>
public static class ModelingServiceCollectionExtensions
{
    /// <summary>
    /// Registers services required by the modeling editor component group.
    /// </summary>
    public static IServiceCollection AddTempoBlazorModeling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTempoBlazorDiagramEditor();

        // The loading delays run on an injectable clock so tests advance a FakeTimeProvider
        // instead of sleeping. TryAdd: a host that already pinned its own TimeProvider keeps it.
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingNotationProfile, BpmnNotationProfile>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingNotationProfile, BpmnLegacyModelingNotationProfile>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingNotationProfile, UmlNotationProfile>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingNotationProfile, ArchimateModelingNotationProfile>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingNotationProfile, Archimate32NotationProfile>());
        services.TryAddSingleton<ModelingNotationProfileRegistry>(sp =>
            new ModelingNotationProfileRegistry(
                sp.GetServices<IModelingNotationProfile>(),
                sp.GetService<ILogger<ModelingNotationProfileRegistry>>()));
        services.TryAddSingleton<IModelingNotationProfileProvider>(sp =>
            sp.GetRequiredService<ModelingNotationProfileRegistry>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingNotationRelationshipRulesProvider, BpmnRelationshipRulesProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingNotationRelationshipRulesProvider, UmlRelationshipRulesProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingNotationRelationshipRulesProvider, Archimate32RelationshipRulesProvider>());
        services.TryAddSingleton<IModelingRelationshipRulesProvider, BuiltInModelingRelationshipRulesProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingNotationViewpointRulesProvider, BpmnViewpointRulesProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingNotationViewpointRulesProvider, UmlViewpointRulesProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingNotationViewpointRulesProvider, Archimate32ViewpointRulesProvider>());
        services.TryAddSingleton<IModelingViewpointRulesProvider, BuiltInModelingViewpointRulesProvider>();
        services.TryAddSingleton<IModelingStencilMapper, BuiltInModelingStencilMapper>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider, DemoModelingModelProvider>());

        // Fáze 14: the write contract is SEPARATE from the read-only provider (decision
        // MODELING-INSPECTOR-EDITING). Writers are resolved by ProviderKey; the demo provider
        // ships an in-memory demo writer so the demo page can edit out of the box.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelWriter, DemoModelingModelWriter>());

        services.TryAddScoped<ModelingDiagramGenerator>();
        services.TryAddScoped<IModelingDiagramProjector>(sp => sp.GetRequiredService<ModelingDiagramGenerator>());

        return services;
    }
}
