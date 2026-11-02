using Microsoft.Extensions.DependencyInjection;

namespace Tempo.Blazor.Configuration;

/// <summary>
/// Extension methods for registering the full Tempo.Blazor split package set.
/// </summary>
public static class AllServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core Tempo.Blazor services and all split feature package services.
    /// </summary>
    public static IServiceCollection AddTempoBlazorAll(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTempoBlazor();
        services.AddTempoBlazorPdfViewer();
        services.AddTempoBlazorCodes();
        services.AddTempoBlazorDiagramEditor();
        services.AddTempoBlazorWireframe();
        services.AddTempoBlazorModeling();
        services.AddTempoBlazorSpreadsheet();
        services.AddTempoBlazorGanttXlsx();
        services.AddTempoBlazorDataTableXlsx();
        services.AddTempoBlazorDocumentEditor();
        services.AddTempoBlazorNotionEditor();
        services.AddTempoBlazorSigning();
        services.AddTempoBlazorAI();

        return services;
    }
}
