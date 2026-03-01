using Tempo.Blazor.Models;

namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Persistence provider for dashboard configurations.
/// Implement this to save/load dashboards from any storage (API, localStorage, etc.).
/// </summary>
public interface IDashboardProvider
{
    /// <summary>Gets all available dashboards for current user.</summary>
    Task<IEnumerable<DashboardConfig>> GetDashboardsAsync(string? userId = null, CancellationToken ct = default);
    
    /// <summary>Gets a specific dashboard by ID.</summary>
    Task<DashboardConfig?> GetDashboardAsync(string dashboardId, CancellationToken ct = default);
    
    /// <summary>Gets the default dashboard for user.</summary>
    Task<DashboardConfig?> GetDefaultDashboardAsync(string? userId = null, CancellationToken ct = default);
    
    /// <summary>Saves a dashboard (create or update).</summary>
    Task<DashboardConfig> SaveDashboardAsync(DashboardConfig dashboard, CancellationToken ct = default);
    
    /// <summary>Deletes a dashboard.</summary>
    Task DeleteDashboardAsync(string dashboardId, CancellationToken ct = default);
    
    /// <summary>Sets a dashboard as default.</summary>
    Task SetDefaultDashboardAsync(string dashboardId, string? userId = null, CancellationToken ct = default);
}

/// <summary>
/// Registry of available widget definitions.
/// </summary>
public interface IWidgetRegistry
{
    /// <summary>Gets all available widget definitions.</summary>
    IEnumerable<WidgetDefinition> GetAllWidgets();
    
    /// <summary>Gets widget by ID.</summary>
    WidgetDefinition? GetWidget(string widgetId);
    
    /// <summary>Gets widgets by category.</summary>
    IEnumerable<WidgetDefinition> GetWidgetsByCategory(string category);
    
    /// <summary>Registers a new widget type.</summary>
    void RegisterWidget(WidgetDefinition widget);
    
    /// <summary>Gets all categories with widgets.</summary>
    IEnumerable<WidgetCategory> GetCategories();
}
