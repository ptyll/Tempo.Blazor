using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Services;

/// <summary>
/// In-memory implementation of IDashboardProvider for testing/demo purposes.
/// Data is lost on application restart.
/// Register as Scoped — each circuit (Server) or tab (WASM) gets its own instance with per-user data.
/// </summary>
public class InMemoryDashboardProvider : IDashboardProvider
{
    private readonly Dictionary<string, DashboardConfig> _dashboards = new();
    private readonly Dictionary<string, string> _defaultDashboards = new(); // userId -> dashboardId

    /// <summary>Initializes a new instance with default dashboard configuration.</summary>
    public InMemoryDashboardProvider()
    {
        // Create default dashboard
        var defaultDashboard = new DashboardConfig
        {
            Id = "default",
            Name = "Main Dashboard",
            IsDefault = true,
            CreatedBy = "system",
            Grid = new GridConfig { Columns = 12, RowHeight = 60, Gap = 16 },
            Widgets = new List<WidgetInstance>
            {
                new WidgetInstance
                {
                    WidgetId = "kpi-revenue",
                    X = 0, Y = 0, Width = 3, Height = 2
                },
                new WidgetInstance
                {
                    WidgetId = "kpi-users",
                    X = 3, Y = 0, Width = 3, Height = 2
                },
                new WidgetInstance
                {
                    WidgetId = "chart-line",
                    X = 6, Y = 0, Width = 6, Height = 4
                },
                new WidgetInstance
                {
                    WidgetId = "list-tasks",
                    X = 0, Y = 2, Width = 4, Height = 6
                },
                new WidgetInstance
                {
                    WidgetId = "calendar-mini",
                    X = 4, Y = 2, Width = 4, Height = 5
                }
            }
        };

        _dashboards[defaultDashboard.Id] = defaultDashboard;
        _defaultDashboards["system"] = defaultDashboard.Id;

        // A second, non-default dashboard so the delete confirmation can be reviewed.
        _dashboards["operations"] = new DashboardConfig
        {
            Id = "operations",
            Name = "Operations",
            IsDefault = false,
            CreatedBy = "system",
            Grid = new GridConfig { Columns = 12, RowHeight = 60, Gap = 16 }
        };
    }

    /// <summary>Retrieves all dashboards for a user or system dashboards.</summary>
    /// <param name="userId">Optional user ID to filter dashboards.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of dashboard configurations.</returns>
    public Task<IEnumerable<DashboardConfig>> GetDashboardsAsync(string? userId = null, CancellationToken ct = default)
    {
        var dashboards = _dashboards.Values
            .Where(d => string.IsNullOrEmpty(userId) || d.CreatedBy == userId || d.CreatedBy == "system")
            .OrderBy(d => d.Name)
            .ToList();

        return Task.FromResult<IEnumerable<DashboardConfig>>(dashboards);
    }

    /// <summary>Retrieves a specific dashboard by ID.</summary>
    /// <param name="dashboardId">The dashboard identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dashboard configuration or null if not found.</returns>
    public Task<DashboardConfig?> GetDashboardAsync(string dashboardId, CancellationToken ct = default)
    {
        _dashboards.TryGetValue(dashboardId, out var dashboard);
        return Task.FromResult(dashboard);
    }

    /// <summary>Retrieves the default dashboard for a user.</summary>
    /// <param name="userId">Optional user ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The default dashboard configuration or null.</returns>
    public Task<DashboardConfig?> GetDefaultDashboardAsync(string? userId = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(userId) && _defaultDashboards.TryGetValue(userId, out var defaultId))
        {
            _dashboards.TryGetValue(defaultId, out var dashboard);
            if (dashboard != null) return Task.FromResult<DashboardConfig?>(dashboard);
        }

        // Fallback to any default dashboard
        var defaultDashboard = _dashboards.Values.FirstOrDefault(d => d.IsDefault);
        return Task.FromResult(defaultDashboard);
    }

    /// <summary>Saves or updates a dashboard configuration.</summary>
    /// <param name="dashboard">The dashboard to save.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The saved dashboard configuration.</returns>
    public Task<DashboardConfig> SaveDashboardAsync(DashboardConfig dashboard, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(dashboard.Id))
        {
            dashboard.Id = Guid.NewGuid().ToString();
        }

        dashboard.ModifiedAt = DateTime.UtcNow;
        _dashboards[dashboard.Id] = dashboard;

        return Task.FromResult(dashboard);
    }

    /// <summary>Deletes a dashboard by ID.</summary>
    /// <param name="dashboardId">The dashboard identifier to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Completed task.</returns>
    public Task DeleteDashboardAsync(string dashboardId, CancellationToken ct = default)
    {
        _dashboards.Remove(dashboardId);

        // Remove from default mapping if needed
        var userEntry = _defaultDashboards.FirstOrDefault(x => x.Value == dashboardId);
        if (userEntry.Key != null)
        {
            _defaultDashboards.Remove(userEntry.Key);
        }

        return Task.CompletedTask;
    }

    /// <summary>Sets a dashboard as default for a user.</summary>
    /// <param name="dashboardId">The dashboard identifier to set as default.</param>
    /// <param name="userId">Optional user ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Completed task.</returns>
    public Task SetDefaultDashboardAsync(string dashboardId, string? userId = null, CancellationToken ct = default)
    {
        var user = userId ?? "anonymous";

        // Get all dashboards visible to this user (including system dashboards)
        var userDashboards = _dashboards.Values
            .Where(d => d.CreatedBy == user || d.CreatedBy == "system")
            .ToList();

        // Clear previous default for this user's visible dashboards
        foreach (var dash in userDashboards)
        {
            dash.IsDefault = false;
        }

        // Set new default
        if (_dashboards.TryGetValue(dashboardId, out var dashboard))
        {
            dashboard.IsDefault = true;
            _defaultDashboards[user] = dashboardId;
        }

        return Task.CompletedTask;
    }
}
