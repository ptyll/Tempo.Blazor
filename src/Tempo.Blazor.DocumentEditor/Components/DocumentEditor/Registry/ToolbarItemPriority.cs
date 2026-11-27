using Tempo.Blazor.Components.Toolbar;

namespace Tempo.Blazor.Components.DocumentEditor.Registry;

/// <summary>
/// The rank of a ribbon command when the bar runs out of room - the document editor's declarative metadata
/// (<see cref="DocumentToolbarItem.Priority"/>), published on every ribbon command as <c>data-priority</c>.
/// The measuring and the "More" menu are the core <c>TmToolbar</c>'s: the ribbon maps this value onto
/// <see cref="ToolbarButtonPriority"/> (<see cref="ToolbarItemPriorityExtensions.ToCorePriority"/>) and keeps no
/// overflow logic of its own.
/// </summary>
public enum ToolbarItemPriority
{
    /// <summary>Primary item that should stay prominent when possible.</summary>
    Primary,

    /// <summary>Secondary item that can move to overflow earlier.</summary>
    Secondary,

    /// <summary>Item intended mainly for overflow or low-frequency command surfaces.</summary>
    OverflowOnly
}

/// <summary>Maps the ribbon's <see cref="ToolbarItemPriority"/> onto the core toolbar's overflow rank.</summary>
public static class ToolbarItemPriorityExtensions
{
    /// <summary>The core <see cref="ToolbarButtonPriority"/> a ribbon command of this priority is registered with.</summary>
    public static ToolbarButtonPriority ToCorePriority(this ToolbarItemPriority priority) => priority switch
    {
        ToolbarItemPriority.Primary => ToolbarButtonPriority.Primary,
        ToolbarItemPriority.Secondary => ToolbarButtonPriority.Secondary,
        ToolbarItemPriority.OverflowOnly => ToolbarButtonPriority.OverflowOnly,
        _ => ToolbarButtonPriority.Secondary,
    };

    /// <summary>The <c>data-priority</c> value published on a ribbon command (the lower-case enum name).</summary>
    public static string ToDataValue(this ToolbarItemPriority priority) => priority.ToString().ToLowerInvariant();
}
