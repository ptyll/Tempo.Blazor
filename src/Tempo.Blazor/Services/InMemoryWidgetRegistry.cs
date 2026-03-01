using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Services;

/// <summary>
/// In-memory implementation of IWidgetRegistry.
/// Contains built-in widget definitions for common use cases.
/// </summary>
public class InMemoryWidgetRegistry : IWidgetRegistry
{
    private readonly Dictionary<string, WidgetDefinition> _widgets = new();
    private readonly List<WidgetCategory> _categories = WidgetCategories.All.ToList();

    public InMemoryWidgetRegistry()
    {
        RegisterDefaultWidgets();
    }

    private void RegisterDefaultWidgets()
    {
        // Analytics & KPIs
        RegisterWidget(new WidgetDefinition
        {
            Id = "kpi-revenue",
            Name = "Revenue KPI",
            Description = "Shows total revenue with trend indicator",
            Category = WidgetCategories.Analytics,
            Icon = "dollar-sign",
            DefaultWidth = 3,
            DefaultHeight = 2,
            MinWidth = 2,
            MinHeight = 2,
            ComponentType = "Tempo.Blazor.Components.Widgets.KpiWidget"
        });

        RegisterWidget(new WidgetDefinition
        {
            Id = "kpi-users",
            Name = "Active Users",
            Description = "Current active users count",
            Category = WidgetCategories.Analytics,
            Icon = "users",
            DefaultWidth = 3,
            DefaultHeight = 2,
            MinWidth = 2,
            MinHeight = 2,
            ComponentType = "Tempo.Blazor.Components.Widgets.KpiWidget"
        });

        RegisterWidget(new WidgetDefinition
        {
            Id = "kpi-conversion",
            Name = "Conversion Rate",
            Description = "Sales conversion percentage",
            Category = WidgetCategories.Analytics,
            Icon = "percent",
            DefaultWidth = 3,
            DefaultHeight = 2,
            MinWidth = 2,
            MinHeight = 2,
            ComponentType = "Tempo.Blazor.Components.Widgets.KpiWidget"
        });

        // Charts
        RegisterWidget(new WidgetDefinition
        {
            Id = "chart-line",
            Name = "Line Chart",
            Description = "Trend visualization over time",
            Category = WidgetCategories.Charts,
            Icon = "trending-up",
            DefaultWidth = 6,
            DefaultHeight = 4,
            MinWidth = 3,
            MinHeight = 3,
            ComponentType = "Tempo.Blazor.Components.Widgets.LineChartWidget"
        });

        RegisterWidget(new WidgetDefinition
        {
            Id = "chart-bar",
            Name = "Bar Chart",
            Description = "Comparison between categories",
            Category = WidgetCategories.Charts,
            Icon = "bar-chart-2",
            DefaultWidth = 6,
            DefaultHeight = 4,
            MinWidth = 3,
            MinHeight = 3,
            ComponentType = "Tempo.Blazor.Components.Widgets.BarChartWidget"
        });

        RegisterWidget(new WidgetDefinition
        {
            Id = "chart-pie",
            Name = "Pie Chart",
            Description = "Distribution visualization",
            Category = WidgetCategories.Charts,
            Icon = "pie-chart",
            DefaultWidth = 4,
            DefaultHeight = 4,
            MinWidth = 3,
            MinHeight = 3,
            ComponentType = "Tempo.Blazor.Components.Widgets.PieChartWidget"
        });

        RegisterWidget(new WidgetDefinition
        {
            Id = "chart-performance",
            Name = "Performance Graph",
            Description = "System or business performance metrics",
            Category = WidgetCategories.Charts,
            Icon = "activity",
            DefaultWidth = 8,
            DefaultHeight = 5,
            MinWidth = 4,
            MinHeight = 3,
            ComponentType = "Tempo.Blazor.Components.Widgets.PerformanceWidget"
        });

        // Quick Actions
        RegisterWidget(new WidgetDefinition
        {
            Id = "action-create",
            Name = "Create New",
            Description = "Quick action buttons for creating entities",
            Category = WidgetCategories.QuickActions,
            Icon = "plus-circle",
            DefaultWidth = 4,
            DefaultHeight = 3,
            MinWidth = 2,
            MinHeight = 2,
            ComponentType = "Tempo.Blazor.Components.Widgets.QuickActionsWidget"
        });

        RegisterWidget(new WidgetDefinition
        {
            Id = "action-shortcuts",
            Name = "Shortcuts",
            Description = "Frequently used shortcuts",
            Category = WidgetCategories.QuickActions,
            Icon = "zap",
            DefaultWidth = 3,
            DefaultHeight = 3,
            MinWidth = 2,
            MinHeight = 2,
            ComponentType = "Tempo.Blazor.Components.Widgets.ShortcutsWidget"
        });

        // Lists
        RegisterWidget(new WidgetDefinition
        {
            Id = "list-recent",
            Name = "Recent Items",
            Description = "Recently accessed or modified items",
            Category = WidgetCategories.Lists,
            Icon = "clock",
            DefaultWidth = 4,
            DefaultHeight = 5,
            MinWidth = 3,
            MinHeight = 3,
            ComponentType = "Tempo.Blazor.Components.Widgets.RecentItemsWidget"
        });

        RegisterWidget(new WidgetDefinition
        {
            Id = "list-tasks",
            Name = "My Tasks",
            Description = "Pending tasks and todos",
            Category = WidgetCategories.Lists,
            Icon = "check-square",
            DefaultWidth = 4,
            DefaultHeight = 6,
            MinWidth = 3,
            MinHeight = 4,
            ComponentType = "Tempo.Blazor.Components.Widgets.TasksWidget"
        });

        RegisterWidget(new WidgetDefinition
        {
            Id = "list-alerts",
            Name = "Alerts",
            Description = "Important notifications and alerts",
            Category = WidgetCategories.Lists,
            Icon = "alert-triangle",
            DefaultWidth = 4,
            DefaultHeight = 4,
            MinWidth = 3,
            MinHeight = 3,
            ComponentType = "Tempo.Blazor.Components.Widgets.AlertsWidget"
        });

        // Calendar
        RegisterWidget(new WidgetDefinition
        {
            Id = "calendar-mini",
            Name = "Mini Calendar",
            Description = "Compact calendar view",
            Category = WidgetCategories.Calendar,
            Icon = "calendar",
            DefaultWidth = 4,
            DefaultHeight = 5,
            MinWidth = 3,
            MinHeight = 4,
            ComponentType = "Tempo.Blazor.Components.Widgets.MiniCalendarWidget"
        });

        RegisterWidget(new WidgetDefinition
        {
            Id = "calendar-events",
            Name = "Upcoming Events",
            Description = "List of upcoming calendar events",
            Category = WidgetCategories.Calendar,
            Icon = "clock",
            DefaultWidth = 4,
            DefaultHeight = 5,
            MinWidth = 3,
            MinHeight = 3,
            ComponentType = "Tempo.Blazor.Components.Widgets.EventsWidget"
        });

        // Notifications
        RegisterWidget(new WidgetDefinition
        {
            Id = "notifications",
            Name = "Notifications",
            Description = "Recent notifications feed",
            Category = WidgetCategories.Notifications,
            Icon = "bell",
            DefaultWidth = 4,
            DefaultHeight = 6,
            MinWidth = 3,
            MinHeight = 3,
            ComponentType = "Tempo.Blazor.Components.Widgets.NotificationsWidget"
        });

        // System
        RegisterWidget(new WidgetDefinition
        {
            Id = "system-status",
            Name = "System Status",
            Description = "Server health and status indicators",
            Category = WidgetCategories.System,
            Icon = "monitor",
            DefaultWidth = 4,
            DefaultHeight = 3,
            MinWidth = 2,
            MinHeight = 2,
            ComponentType = "Tempo.Blazor.Components.Widgets.SystemStatusWidget"
        });

        RegisterWidget(new WidgetDefinition
        {
            Id = "weather",
            Name = "Weather",
            Description = "Current weather and forecast",
            Category = WidgetCategories.System,
            Icon = "sun",
            DefaultWidth = 3,
            DefaultHeight = 3,
            MinWidth = 2,
            MinHeight = 2,
            AllowMultiple = false,
            ComponentType = "Tempo.Blazor.Components.Widgets.WeatherWidget"
        });
    }

    public void RegisterWidget(WidgetDefinition widget)
    {
        _widgets[widget.Id] = widget;
    }

    public IEnumerable<WidgetDefinition> GetAllWidgets()
    {
        return _widgets.Values.OrderBy(w => w.Name);
    }

    public WidgetDefinition? GetWidget(string widgetId)
    {
        return _widgets.TryGetValue(widgetId, out var widget) ? widget : null;
    }

    public IEnumerable<WidgetDefinition> GetWidgetsByCategory(string category)
    {
        return _widgets.Values.Where(w => w.Category == category).OrderBy(w => w.Name);
    }

    public IEnumerable<WidgetCategory> GetCategories()
    {
        return _categories.Where(c => GetWidgetsByCategory(c.Id).Any());
    }
}
