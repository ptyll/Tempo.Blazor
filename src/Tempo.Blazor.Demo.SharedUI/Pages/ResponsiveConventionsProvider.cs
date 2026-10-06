using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.SharedUI.Pages;

/// <summary>
/// The dashboard the responsive conventions page renders. Static, so the three hosts show the same
/// five widgets and the layout is the only thing that changes between them.
/// </summary>
public sealed class ResponsiveConventionsProvider : IDashboardProvider
{
    private static readonly WidgetInstance[] Widgets =
    [
        new() { InstanceId = "rc-revenue", WidgetId = "rc-revenue", X = 0, Y = 0, Width = 8, Height = 3 },
        new() { InstanceId = "rc-orders", WidgetId = "rc-orders", X = 8, Y = 0, Width = 4, Height = 3 },
        new() { InstanceId = "rc-returns", WidgetId = "rc-returns", X = 0, Y = 3, Width = 4, Height = 3 },
        new() { InstanceId = "rc-margin", WidgetId = "rc-margin", X = 4, Y = 3, Width = 4, Height = 3 },
        new() { InstanceId = "rc-queue", WidgetId = "rc-queue", X = 8, Y = 3, Width = 4, Height = 3 },
    ];

    /// <summary>The dashboard the page seeds into the host's provider, once per host id.</summary>
    public static DashboardConfig Create(string id) => Dashboard(id);

    /// <inheritdoc />
    public Task<IEnumerable<DashboardConfig>> GetDashboardsAsync(string? userId = null, CancellationToken ct = default) =>
        Task.FromResult<IEnumerable<DashboardConfig>>([Dashboard("rc-wide")]);

    /// <inheritdoc />
    public Task<DashboardConfig?> GetDashboardAsync(string dashboardId, CancellationToken ct = default) =>
        Task.FromResult<DashboardConfig?>(Dashboard(dashboardId));

    /// <inheritdoc />
    public Task<DashboardConfig?> GetDefaultDashboardAsync(string? userId = null, CancellationToken ct = default) =>
        Task.FromResult<DashboardConfig?>(Dashboard("rc-wide"));

    /// <inheritdoc />
    public Task<DashboardConfig> SaveDashboardAsync(DashboardConfig dashboard, CancellationToken ct = default) =>
        Task.FromResult(dashboard);

    /// <inheritdoc />
    public Task DeleteDashboardAsync(string dashboardId, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SetDefaultDashboardAsync(string dashboardId, string? userId = null, CancellationToken ct = default) =>
        Task.CompletedTask;

    private static DashboardConfig Dashboard(string id) => new()
    {
        Id = id,
        Name = id,
        Grid = new GridConfig { Columns = 12, RowHeight = 48, Gap = 12 },
        Widgets = Widgets.Select(widget => new WidgetInstance
        {
            InstanceId = widget.InstanceId,
            WidgetId = widget.WidgetId,
            X = widget.X,
            Y = widget.Y,
            Width = widget.Width,
            Height = widget.Height,
        }).ToList(),
    };
}


