namespace Tempo.Blazor.Components.Diagram.Models;

/// <summary>Visual style overrides for a single table cell.</summary>
public sealed class DiagramTableCellStyle
{
    /// <summary>Background color (CSS color string).</summary>
    public string? BackgroundColor { get; set; }

    /// <summary>Border color (CSS color string).</summary>
    public string? BorderColor { get; set; }

    /// <summary>Text align: left, center, right.</summary>
    public string? TextAlign { get; set; }

    /// <summary>Font weight, e.g. "bold" or "normal".</summary>
    public string? FontWeight { get; set; }

    /// <summary>Cell ink (CSS color string). Header cells that paint
    /// <c>var(--tm-diagram-stencil-table-header-fill)</c> set
    /// <c>var(--tm-diagram-node-ink-on-fill)</c> so the label clears 4.5:1 in dark mode.
    /// Null keeps the CSS default <c>--tm-diagram-node-ink</c> (muted paper labels).</summary>
    public string? Color { get; set; }

    /// <summary>Shallow copy so table commands keep the header on-fill ink across undo.</summary>
    public DiagramTableCellStyle Clone() => new()
    {
        BackgroundColor = BackgroundColor,
        BorderColor = BorderColor,
        TextAlign = TextAlign,
        FontWeight = FontWeight,
        Color = Color
    };
}
