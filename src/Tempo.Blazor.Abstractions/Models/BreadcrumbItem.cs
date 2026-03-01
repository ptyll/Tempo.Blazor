using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Models;

/// <summary>
/// Default implementation of IBreadcrumbItem.
/// </summary>
public sealed record BreadcrumbItem : IBreadcrumbItem
{
    public string Label { get; init; } = string.Empty;
    public string? Href { get; init; }
    public string? Icon { get; init; }

    public BreadcrumbItem() { }

    public BreadcrumbItem(string label, string? href = null, string? icon = null)
    {
        Label = label;
        Href = href;
        Icon = icon;
    }
}
