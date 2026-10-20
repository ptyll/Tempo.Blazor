using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.Dashboard;

/// <summary>
/// Shared resolution of a widget definition's icon name to a name TmIcon can actually render.
/// The model default (<c>WidgetDefinition.Icon</c> = "widget") and any other name that is
/// neither a built-in nor registered in <see cref="IconRegistry"/> would fall through to
/// TmIcon's empty <c>tm-icon-unknown</c> span — a dashed, untinted box that reads as a bug
/// inside the category tile. Such names resolve to the <see cref="IconNames.Box"/> glyph
/// instead, which is the documented "unknown widget" picture. The check mirrors TmIcon's own
/// resolution order (built-in switch first, then the registry), so anything TmIcon could
/// render — including a host icon registered at startup — keeps working unchanged.
/// Serves both the widget header tile in <c>TmDashboard</c> and the selector card in
/// <c>TmWidgetSelector</c>, so one definition shows one icon everywhere.
/// </summary>
internal static class DashboardWidgetIcons
{
    /// <summary>
    /// Returns <paramref name="icon"/> when TmIcon can render it (built-in or registered);
    /// null/empty and unresolvable names return <see cref="IconNames.Box"/>.
    /// </summary>
    public static string ResolveIcon(string? icon)
        => string.IsNullOrWhiteSpace(icon)
            || (TmIcon.GetBuiltInSvg(icon).Length == 0 && !IconRegistry.Contains(icon))
                ? IconNames.Box
                : icon;
}
