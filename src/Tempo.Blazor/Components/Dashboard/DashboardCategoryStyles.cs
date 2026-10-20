using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Dashboard;

/// <summary>
/// Shared resolution of a widget category to its colour presentation: built-in categories render
/// through the theme-aware categorical tokens via a <c>tm-dashboard-category--{id}</c> class
/// (the rules live in <c>_dashboard.css</c>), while custom/host-supplied categories carry their
/// model <c>.Color</c> inline through <c>--category-color</c>. Serves both the widget card
/// in <c>TmWidgetSelector</c> and the 40px icon tile in <c>TmDashboard</c>, so a category keeps
/// one colour everywhere.
/// </summary>
internal static class DashboardCategoryStyles
{
    /// <summary>
    /// BEM colour class for a built-in category (maps to <c>var(--tm-category-N)</c>);
    /// empty for custom/unknown categories, which resolve through their inline colour instead.
    /// </summary>
    public static string GetColorClass(string categoryId)
        => IsBuiltInCategory(categoryId) ? $"tm-dashboard-category--{categoryId.ToLowerInvariant()}" : "";

    /// <summary>
    /// Inline <c>--category-color</c> declaration for custom/unknown categories (falling back to
    /// <c>var(--tm-category-fallback)</c> when neither the category nor its colour is registered);
    /// empty for built-in categories, whose class already supplies the variable.
    /// </summary>
    public static string GetColorStyle(string categoryId, IEnumerable<WidgetCategory> categories)
    {
        if (IsBuiltInCategory(categoryId)) return "";
        var color = categories.FirstOrDefault(c => c.Id == categoryId)?.Color ?? "var(--tm-category-fallback)";
        return $"--category-color: {color}";
    }

    private static bool IsBuiltInCategory(string categoryId)
        => WidgetCategories.All.Any(c => c.Id == categoryId);
}
