using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Services;

/// <summary>
/// In-memory implementation of IDashboardProvider for testing/demo purposes.
/// Data is lost on application restart.
/// </summary>
public class InMemoryDashboardProvider : IDashboardProvider
{
    private readonly Dictionary<string, DashboardConfig> _dashboards = new();
    private readonly Dictionary<string, string> _defaultDashboards = new(); // userId -> dashboardId

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
    }

    public Task<IEnumerable<DashboardConfig>> GetDashboardsAsync(string? userId = null, CancellationToken ct = default)
    {
        var dashboards = _dashboards.Values
            .Where(d => string.IsNullOrEmpty(userId) || d.CreatedBy == userId || d.CreatedBy == "system")
            .OrderBy(d => d.Name)
            .ToList();

        return Task.FromResult<IEnumerable<DashboardConfig>>(dashboards);
    }

    public Task<DashboardConfig?> GetDashboardAsync(string dashboardId, CancellationToken ct = default)
    {
        _dashboards.TryGetValue(dashboardId, out var dashboard);
        return Task.FromResult(dashboard);
    }

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
