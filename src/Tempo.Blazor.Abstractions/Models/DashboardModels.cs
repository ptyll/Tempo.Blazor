namespace Tempo.Blazor.Models;

/// <summary>
/// Represents a dashboard configuration with widget layout and settings.
/// </summary>
public class DashboardConfig
{
    /// <summary>Unique identifier for the dashboard.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>Dashboard name.</summary>
    public string Name { get; set; } = "My Dashboard";
    
    /// <summary>Whether this is the default dashboard.</summary>
    public bool IsDefault { get; set; }
    
    /// <summary>Grid configuration (columns, row height, etc.).</summary>
    public GridConfig Grid { get; set; } = new();
    
    /// <summary>List of widgets on the dashboard.</summary>
    public List<WidgetInstance> Widgets { get; set; } = [];
    
    /// <summary>User who created/owns this dashboard.</summary>
    public string CreatedBy { get; set; } = string.Empty;
    
    /// <summary>Last modified timestamp.</summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Grid configuration for dashboard layout.
/// </summary>
public class GridConfig
{
    /// <summary>Number of columns (default: 12).</summary>
    public int Columns { get; set; } = 12;
    
    /// <summary>Height of one row in pixels (default: 60).</summary>
    public int RowHeight { get; set; } = 60;
    
    /// <summary>Gap between widgets in pixels.</summary>
    public int Gap { get; set; } = 16;
    
    /// <summary>Whether to snap widgets to grid.</summary>
    public bool SnapToGrid { get; set; } = true;
    
    /// <summary>Whether widgets can overlap.</summary>
    public bool AllowOverlap { get; set; } = false;
}

/// <summary>
/// Instance of a widget on the dashboard with position and size.
/// </summary>
public class WidgetInstance
{
    /// <summary>Unique instance ID.</summary>
    public string InstanceId { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>Reference to widget definition ID.</summary>
    public string WidgetId { get; set; } = string.Empty;
    
    /// <summary>Custom title (optional override).</summary>
    public string? CustomTitle { get; set; }
    
    /// <summary>Grid position (column start, 0-based).</summary>
    public int X { get; set; }
    
    /// <summary>Grid position (row start, 0-based).</summary>
    public int Y { get; set; }
    
    /// <summary>Width in grid columns.</summary>
    public int Width { get; set; } = 4;
    
    /// <summary>Height in grid rows.</summary>
    public int Height { get; set; } = 4;
    
    /// <summary>Widget-specific configuration JSON.</summary>
    public string? ConfigJson { get; set; }
    
    /// <summary>Whether widget is minimized.</summary>
    public bool IsMinimized { get; set; }
    
    /// <summary>Whether widget is collapsed (show only header).</summary>
    public bool IsCollapsed { get; set; }
    
    /// <summary>Z-index for layering.</summary>
    public int ZIndex { get; set; }
}

/// <summary>
/// Widget definition (metadata about available widgets).
/// </summary>
public class WidgetDefinition
{
    /// <summary>Unique widget type ID.</summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>Widget display name.</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Widget description.</summary>
    public string? Description { get; set; }
    
    /// <summary>Widget category for grouping.</summary>
    public string Category { get; set; } = "General";
    
    /// <summary>Icon name (from IconRegistry).</summary>
    public string Icon { get; set; } = "widget";
    
    /// <summary>Default width in grid columns.</summary>
    public int DefaultWidth { get; set; } = 4;
    
    /// <summary>Default height in grid rows.</summary>
    public int DefaultHeight { get; set; } = 4;
    
    /// <summary>Minimum allowed width.</summary>
    public int MinWidth { get; set; } = 2;
    
    /// <summary>Minimum allowed height.</summary>
    public int MinHeight { get; set; } = 2;
    
    /// <summary>Maximum allowed width (0 = unlimited).</summary>
    public int MaxWidth { get; set; }
    
    /// <summary>Maximum allowed height (0 = unlimited).</summary>
    public int MaxHeight { get; set; }
    
    /// <summary>Whether widget can be resized.</summary>
    public bool IsResizable { get; set; } = true;
    
    /// <summary>Whether widget can be moved.</summary>
    public bool IsDraggable { get; set; } = true;
    
    /// <summary>Whether multiple instances are allowed.</summary>
    public bool AllowMultiple { get; set; } = true;
    
    /// <summary>Component type for rendering (full type name).</summary>
    public string ComponentType { get; set; } = string.Empty;
    
    /// <summary>Default configuration for new instances.</summary>
    public Dictionary<string, object>? DefaultConfig { get; set; }
}

/// <summary>
/// Widget category for grouping in selector.
/// </summary>
public class WidgetCategory
{
    /// <summary>Category ID.</summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Description.</summary>
    public string? Description { get; set; }
    
    /// <summary>Icon name.</summary>
    public string Icon { get; set; } = "folder";
    
    /// <summary>Display order.</summary>
    public int Order { get; set; }
    
    /// <summary>Color for visual distinction.</summary>
    public string? Color { get; set; }
}

/// <summary>
/// Available widget categories with built-in options.
/// </summary>
public static class WidgetCategories
{
    public const string Analytics = "Analytics";
    public const string Charts = "Charts";
    public const string QuickActions = "QuickActions";
    public const string Lists = "Lists";
    public const string Calendar = "Calendar";
    public const string Notifications = "Notifications";
    public const string System = "System";
    public const string Custom = "Custom";
    
    public static readonly List<WidgetCategory> All = new()
    {
        new() { Id = Analytics, Name = "Analytics & KPIs", Icon = "bar-chart-2", Order = 1, Color = "#3b82f6" },
        new() { Id = Charts, Name = "Charts & Graphs", Icon = "pie-chart", Order = 2, Color = "#8b5cf6" },
        new() { Id = QuickActions, Name = "Quick Actions", Icon = "zap", Order = 3, Color = "#f59e0b" },
        new() { Id = Lists, Name = "Lists & Tables", Icon = "list", Order = 4, Color = "#10b981" },
        new() { Id = Calendar, Name = "Calendar & Events", Icon = "calendar", Order = 5, Color = "#ec4899" },
        new() { Id = Notifications, Name = "Notifications", Icon = "bell", Order = 6, Color = "#ef4444" },
        new() { Id = System, Name = "System Info", Icon = "monitor", Order = 7, Color = "#6b7280" },
        new() { Id = Custom, Name = "Custom Widgets", Icon = "box", Order = 8, Color = "#14b8a6" }
    };
}
