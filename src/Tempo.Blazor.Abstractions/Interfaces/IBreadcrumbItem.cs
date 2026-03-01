namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents a single breadcrumb item in TmBreadcrumbs.
/// </summary>
public interface IBreadcrumbItem
{
    /// <summary>Display label for the breadcrumb.</summary>
    string Label { get; }

    /// <summary>Navigation URL. Null for the current (last) breadcrumb.</summary>
    string? Href { get; }

    /// <summary>Optional icon name from IconNames constants.</summary>
    string? Icon { get; }
}
